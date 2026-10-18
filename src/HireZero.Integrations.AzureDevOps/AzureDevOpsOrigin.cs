using System.Text.RegularExpressions;

namespace HireZero.Integrations.AzureDevOps;

// Azure DevOps remote URLs: https://[user@]dev.azure.com/org/project/_git/repo, git@ssh.dev.azure.com:v3/org/project/repo
// and https://org.visualstudio.com/[DefaultCollection/]project/_git/repo. Organization names are often abbreviated.
public sealed class AzureDevOpsOriginFormat : IGitOriginFormat
{
    public const string Host = "azure-devops";
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);

    public GitOrigin? Parse(string url)
    {
        var devops = Regex.Match(
            url,
            @"dev\.azure\.com/([^/]+)/([^/]+)/_git/([^/?#]+?)(?:\.git)?/?$",
            RegexOptions.IgnoreCase,
            Limit
        );
        if (!devops.Success)
            devops = Regex.Match(
                url,
                @"ssh\.dev\.azure\.com:v3/([^/]+)/([^/]+)/([^/?#]+?)/?$",
                RegexOptions.IgnoreCase,
                Limit
            );
        if (devops.Success)
            return new(
                Host,
                GitRemote.DecodeName(devops.Groups[1].Value),
                GitRemote.DecodeName(devops.Groups[2].Value),
                GitRemote.DecodeName(devops.Groups[3].Value),
                AbbreviatedOrganization: true
            );
        var legacy = Regex.Match(
            url,
            @"//(?:[^@/]+@)?([A-Za-z0-9-]+)\.visualstudio\.com/(?:DefaultCollection/)?([^/]+)/_git/([^/?#]+?)/?$",
            RegexOptions.IgnoreCase,
            Limit
        );
        return legacy.Success
            ? new(
                Host,
                legacy.Groups[1].Value,
                GitRemote.DecodeName(legacy.Groups[2].Value),
                GitRemote.DecodeName(legacy.Groups[3].Value),
                AbbreviatedOrganization: true
            )
            : null;
    }
}
