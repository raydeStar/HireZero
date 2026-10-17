using System.Runtime.InteropServices;
using HireZero.Roles.Developer;
using Microsoft.Win32.SafeHandles;

public static class NativeCloneDirectoryChecks
{
    public static void Run(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return; // Automatic clone is unavailable on non-Windows platforms.
        var root = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "native-clone-lock-" + Guid.NewGuid().ToString("N"))
        );
        var moved = root + "-moved";
        Directory.CreateDirectory(root);
        var renameBlocked = false;
        var renameError = 0;
        var writeBlocked = false;
        var writeError = 0;
        try
        {
            using (var before = OpenForWrite(root))
                check(!before.IsInvalid, "The owned fixture permits directory-write access before taking the lease.");
            using (var lease = CloneDirectoryLease.OpenComponent(root))
            {
                check(
                    !lease.IsInvalid,
                    "The production component opener acquires a real native GENERIC_READ directory handle."
                );
                try
                {
                    Directory.Move(root, moved);
                }
                catch (IOException error)
                {
                    renameBlocked = true;
                    renameError = error.HResult & 0xffff;
                }
                check(
                    renameBlocked && renameError == 32 && Directory.Exists(root) && !Directory.Exists(moved),
                    "A native rename is blocked by a sharing violation while the production handle is open."
                );
                using var write = OpenForWrite(root);
                writeError = Marshal.GetLastWin32Error();
                writeBlocked = write.IsInvalid;
                check(
                    writeBlocked && writeError == 32,
                    "Directory-write/reparse mutation access is blocked while the production handle is open."
                );
                var child = Path.Combine(root, "clone-child");
                Directory.CreateDirectory(child);
                File.WriteAllText(Path.Combine(child, "fake-clone-write"), "offline fixture");
                check(
                    File.Exists(Path.Combine(child, "fake-clone-write")),
                    "Native parent locking still permits ordinary clone child-file creation."
                );
            }
            using (var after = OpenForWrite(root))
                check(!after.IsInvalid, "Directory-write access returns after the production handle is disposed.");
            Directory.Move(root, moved);
            check(
                Directory.Exists(moved) && !Directory.Exists(root),
                "Native rename succeeds after disposal, proving the lock rather than ACL denial prevented replacement."
            );
            Console.WriteLine(
                System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        PositiveNativeDirectoryLock = true,
                        RenameBlocked = renameBlocked,
                        RenameError = renameError,
                        DirectoryWriteBlocked = writeBlocked,
                        DirectoryWriteError = writeError,
                        ChildWritesSucceeded = true,
                        RenameAfterDispose = true,
                    }
                )
            );
        }
        finally
        {
            // Both names were freshly allocated owned temp directories; never follow a fixture reparse point.
            var temporary = Path.GetFullPath(Path.GetTempPath());
            foreach (var path in new[] { root, moved })
                if (
                    Directory.Exists(path)
                    && Path.GetFullPath(path).StartsWith(temporary, StringComparison.OrdinalIgnoreCase)
                    && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                )
                    Directory.Delete(path, recursive: true);
        }
    }

    private static SafeFileHandle OpenForWrite(string path) =>
        CreateFile(path, 0x40000000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string name,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template
    );
}
