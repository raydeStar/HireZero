using System.Text.RegularExpressions;
using HireZero.Core;

namespace HireZero.Roles.Developer;

// In Home, asking for work on a specific pull request (fix, diagnose, review, look at, merge…) opens a task thread for that PR.
// Status questions ("how's #216 going?") stay in Home and are answered from the open-work digest.
public sealed class PullRequestTaskSignal : ITaskThreadSignal
{
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);
    static readonly Regex Link = new(@"github\.com/([\w.-]+/[\w.-]+)/pull/(\d+)|dev\.azure\.com/[^/\s]+/[^/\s]+/_git/([^/\s?#]+)/pullrequest/(\d+)", RegexOptions.IgnoreCase, Limit);
    static readonly Regex Number = new(@"(?:#|\bPR\s*#?|\bpull request\s*#?)(\d{1,6})\b", RegexOptions.IgnoreCase, Limit);
    static readonly Regex Action = new(@"\b(fix|fixes|diagnose|address|resolve|repair|rebase|merge|review|look at|take a look|investigate|debug|failing|failure|broken)\b", RegexOptions.IgnoreCase, Limit);
    static readonly Regex Status = new(@"\b(status|how'?s|how is|how are|update on|progress|where are we)\b", RegexOptions.IgnoreCase, Limit);

    public string? TaskKey(string text)
    {
        try
        {
            if (!Action.IsMatch(text) || Status.IsMatch(text) && !Regex.IsMatch(text, @"\b(fix|diagnose|merge|rebase)\b", RegexOptions.IgnoreCase, Limit)) return null;
            if (Link.Match(text) is { Success: true } link)
                return link.Groups[1].Success ? "github:" + link.Groups[1].Value.ToLowerInvariant() + "#" + link.Groups[2].Value
                    : "ado:" + link.Groups[3].Value.ToLowerInvariant() + "!" + link.Groups[4].Value;
            return Number.Match(text) is { Success: true } number ? "pr#" + number.Groups[1].Value : null;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }
}
