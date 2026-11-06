using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Xunit.Runner.InProc.SystemConsole;

// The test executable doubles as the host for the Node browser smoke tests and the explicit Codex inspection;
// any other invocation runs the xUnit tests.
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.FirstOrDefault() == "--team-browser-fixture")
        {
            await TeamBrowserFixture.RunAsync(args);
            return Environment.ExitCode;
        }
        if (args.FirstOrDefault() is "--codex-inspect" or "--codex-pilot")
        {
            try
            {
                await CompanyCodexChecks.InspectInstalledAsync(args);
            }
            catch (Exception e) when (e is WorkspaceException or ModelTransportException)
            {
                Console.WriteLine("BLOCKED: " + e.Message);
                Environment.ExitCode = 2;
            }
            return Environment.ExitCode;
        }
        if (args.FirstOrDefault() is "--measured-browser-fixture" or "--company-browser-fixture")
        {
            await BrowserQueueFixture.RunAsync(args);
            return Environment.ExitCode;
        }
        // Mirrors xUnit's generated entry point: Microsoft.Testing.Platform for dotnet test, the console runner otherwise.
        if (args.Any(arg => arg is "--server" or "--internal-msbuild-node"))
            return await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(
                args,
                HireZero.Company.Tests.SelfRegisteredExtensions.AddSelfRegisteredExtensions
            );
        return await ConsoleRunner.Run(args);
    }
}
