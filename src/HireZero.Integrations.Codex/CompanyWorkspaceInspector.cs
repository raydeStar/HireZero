using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HireZero.Core;

namespace HireZero.Integrations.Codex;

public sealed class CompanyWorkspaceInspector(string gitExecutable) : ICompanyWorkspaceInspector
{
    public async Task<CompanyRepositorySnapshot> InspectAsync(string root,CancellationToken token)
    {
        if(!Path.IsPathFullyQualified(gitExecutable)||!File.Exists(gitExecutable))throw new WorkspaceException("An explicit Git executable is required.");
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);if(!Directory.Exists(root))throw new WorkspaceException("Checkout does not exist.");
        for(var p=new DirectoryInfo(root);p is not null;p=p.Parent)if((p.Attributes&FileAttributes.ReparsePoint)!=0)throw new WorkspaceException("Checkout aliases through reparse points are refused.");
        async Task<string> Git(params string[] args)
        {
            var start=new ProcessStartInfo(gitExecutable){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            var environment=new[]{"SystemRoot","WINDIR","ComSpec","PATHEXT","PATH","TEMP","TMP"}.ToDictionary(key=>key,key=>Environment.GetEnvironmentVariable(key));start.Environment.Clear();foreach(var pair in environment)if(pair.Value is not null)start.Environment[pair.Key]=pair.Value;
            foreach(var argument in new[]{"--no-optional-locks","-c","core.fsmonitor=false","-c","core.hooksPath="+Path.Combine(root,".git","disabled-hooks")}.Concat(args))start.ArgumentList.Add(argument);
            start.Environment["GIT_CONFIG_NOSYSTEM"]="1";start.Environment["GIT_CONFIG_GLOBAL"]=Path.Combine(root,".git","absent-global-config");start.Environment.Remove("GIT_CONFIG_COUNT");start.Environment.Remove("GIT_DIR");start.Environment.Remove("GIT_WORK_TREE");
            using var process=Process.Start(start)??throw new WorkspaceException("Git inspection did not start.");
            var output=process.StandardOutput.ReadToEndAsync(token);var error=process.StandardError.ReadToEndAsync(token);await process.WaitForExitAsync(token);await error;
            if(process.ExitCode!=0)throw new WorkspaceException("Read-only Git inspection failed.");return (await output).Trim();
        }
        var top=Path.GetFullPath(await Git("rev-parse","--show-toplevel")).TrimEnd(Path.DirectorySeparatorChar);
        if(!string.Equals(root,top,StringComparison.OrdinalIgnoreCase))throw new WorkspaceException("Use the exact checkout root.");
        var revision=await Git("rev-parse","HEAD");using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var stack=new Stack<string>();stack.Push(root);long total=0;int count=0;
        var files=new List<string>();
        while(stack.TryPop(out var directory))foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if(directory==root&&Path.GetFileName(entry)==".git")continue;
            var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)throw new WorkspaceException("Checkout contains an unapproved symlink or reparse path.");
            if((attributes&FileAttributes.Directory)!=0)stack.Push(entry);else files.Add(entry);
            if(files.Count+stack.Count>2000)throw new WorkspaceException("Use a bounded checkout for the execution pilot.");
        }
        foreach(var file in files.Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();total+=new FileInfo(file).Length;if(total>20*1024*1024||++count>2000)throw new WorkspaceException("Checkout inspection exceeds its bounded pilot scope.");
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root,file).Replace('\\','/')));hash.AppendData([0]);hash.AppendData(SHA256.HashData(await File.ReadAllBytesAsync(file,token)));
        }
        // Detect a concurrent ref change during fingerprinting; content is rechecked immediately before dispatch.
        if(revision!=await Git("rev-parse","HEAD"))throw new WorkspaceException("Repository changed during inspection.");
        return new(root,revision,Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
}
