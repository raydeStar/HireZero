namespace HireZero.Roles.Developer;

public static class GitHubRepositoryChat
{
    public static string ProposalKey(WorkRequest request, IReadOnlyList<ConversationMessage> messages) =>
        request.Id + ":" + (messages.LastOrDefault(note => note.Author != "workspace-agent" && note.Visibility != "owner")?.Id ?? "intake");

    public static bool CurrentProposal(RepositoryConnectionProposal proposal, WorkRequest request,
        IReadOnlyList<ConversationMessage> messages) => proposal.RequestId == request.Id &&
        proposal.Id == ProposalKey(request, messages) && proposal.Action is "connect" or "enableAutomatic" or "disableAutomatic";

    public static bool SameCheckout(RepositoryChoice choice, ReviewRepository repository) => choice.CheckoutId == repository.Id &&
        choice.Id == repository.GitHubId && string.Equals(choice.RootPath, repository.RootPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // A PR number or link, or a repository action, makes a message GitHub-specific.
    public static bool Specific(string text) => System.Text.RegularExpressions.Regex.IsMatch(text,
        @"#\d+|/pull/\d+|\bPR\s*\d+|\b(pull request|PR)\s+(number|no\.?)\s*\d+|\b(connect|disconnect|automat\w*|diagnose|fix|merge|close|rebase|approve)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));

    public static async Task<string?> ProposeAsync(WorkRequest request, IReadOnlyList<ConversationMessage> messages,
        DeveloperPrompts cells, GitHubReviewClient github, GitHubReviewStore store, RequestStore requests, GitHubChatClassifier classifier, CancellationToken token, bool deferBroad = false)
    {
        var repositoryNames = store.RepositoryNames();
        AgentActivity.Note("Working out what you're asking (Jev routing, then Luna).");
        var routing = await classifier.ClassifyAsync(GitHubChatIntent.Prompt(request, messages, repositoryNames), token);
        var intent = await cells.InterpretRepositoryChatAsync(request, messages, repositoryNames, routing, token);
        if (intent is null) return deferBroad ? null : "I couldn’t interpret that reply reliably. No repository or execution setting changed. Please try again.";
        // A general question about a known project is answered from its working context instead.
        if (deferBroad && (intent.Action is "none" or "clarify" || intent.Action == "inspect" && intent.Number is null)) return null;
        if (intent.Action == "inspect")
        {
            if (intent.Repository is not null && intent.Number is not null)
                return await InspectAsync(request, messages, intent.Repository, intent.Number.Value, cells, github, store, requests, token);
            var accountForRead = await github.IdentityAsync(token);
            var candidates = intent.Repository is null ? await github.MyReviewsAsync(accountForRead, token) :
                await github.ReviewsAsync(new("", accountForRead.Id, (await github.RepositoryAsync(intent.Repository, token)).Id,
                    intent.Repository, "", ""), accountForRead, token);
            if (candidates.Length == 0) return "GitHub returned no open PRs authored by " + accountForRead.Login + " in this search.";
            var summaries = new List<string>();
            foreach (var candidate in candidates.Take(5))
            {
                var evidence = await github.EvidenceAsync(candidate.Repository, candidate.Number, false, token);
                summaries.Add(evidence.Repository + " #" + evidence.Number + ": " + evidence.Title + " — " +
                    evidence.Checks.Count(check => check.State != "success") + " checks not green; " + evidence.Threads.Count(thread => !thread.Resolved) +
                    " unresolved review threads. https://github.com/" + evidence.Repository + "/pull/" + evidence.Number);
            }
            return "I checked your open PRs on GitHub.\n\n" + string.Join("\n\n", summaries) +
                (candidates.Length > 5 ? "\n\nShowing the first five recently updated PRs." : "") +
                "\n\nTell me which PR to diagnose. I’ll read its failure evidence and propose a fix before coding.";
        }
        if (intent.Action is "none" or "clarify") return intent.Reply;
        if (routing.Contradicts(intent)) return "Jev and OpenClaw disagree about that request. Do you want to connect a repository, enable read-only review observation, or return to confirming each fix? No setting changed.";
        var account = await github.IdentityAsync(token);
        var key = ProposalKey(request, messages);
        var retained = store.Proposals(request.Id, account.Id).SingleOrDefault(item => item.Id == key);
        if (retained is not null) return Describe(retained) + routing.ProposalNote();
        var choices = await github.ResolveAsync(intent.Repository!, token);
        var proposal = CreateProposal(request.Id, key, account, intent, choices, store.Repositories(account.Id));
        store.SaveProposal(proposal);
        return Describe(proposal) + routing.ProposalNote();
    }

    public static async Task<string> InspectAsync(WorkRequest request, IReadOnlyList<ConversationMessage> messages,
        string repository, int number, DeveloperPrompts cells, GitHubReviewClient github, GitHubReviewStore store, RequestStore requests, CancellationToken token)
    {
        var account = await github.IdentityAsync(token);
        var key = ProposalKey(request, messages);
        var generation = requests.AgentConsentGeneration(request.Id);
        AgentActivity.Note("Reading " + repository + " #" + number + " on GitHub: checks, failure logs, review threads and changed files.");
        var evidence = await github.EvidenceAsync(repository, number, true, token);
        var retained = store.Workflows(account.Id, request.Id).FirstOrDefault(item => item.MessageKey == key &&
            item.ConsentGeneration == generation &&
            item.Repository.Equals(evidence.Repository, StringComparison.OrdinalIgnoreCase) && item.Number == number && item.EvidenceSnapshot == evidence.Snapshot);
        if (retained is not null) return retained.Strategy.Summary + "\n\nThe fix strategy is shown below; execution waits for your confirmation.";
        var planned = await cells.PlanPullRequestAsync(messages.LastOrDefault()?.Body ?? request.Description, evidence, github, token);
        if (planned is null) return "I read the GitHub evidence, but the diagnosis did not meet the proposal format. No execution started. Please ask again.";
        var workflow = new PullRequestWorkflow(Guid.NewGuid().ToString("N"), request.Id, account.Id, key, evidence.Repository,
            evidence.RepositoryId, number, evidence.HeadSha, evidence.Snapshot, planned.Value.Strategy, planned.Value.Model)
            { ConsentGeneration = generation };
        if (!evidence.Complete || !evidence.Open || !evidence.Author.Equals(account.Login, StringComparison.OrdinalIgnoreCase))
            workflow = workflow with { State = "read-only", Error = "Execution requires your open PR and complete current GitHub evidence." };
        token.ThrowIfCancellationRequested();
        if (requests.AgentConsentGeneration(request.Id) != generation) throw new WorkspaceException("Consent changed during diagnosis. Request a fresh proposal.");
        store.SaveWorkflow(workflow);
        return "I inspected " + evidence.Repository + " #" + number + " on GitHub. " + planned.Value.Strategy.Summary +
            "\n\n" + (planned.Value.Model.Contains("astra") ? "Astra" : "Sol 6.1") + " validated the proposal after Luna’s initial triage. " +
            "Review the strategy below. Coding, commit and PR update wait for your confirmation; business-rule changes need a separate decision." +
            (evidence.Limitations.Length > 0 ? "\n\n" + string.Join(" ", evidence.Limitations) : "");
    }

    public static RepositoryConnectionProposal CreateProposal(string requestId, string key,
        GitHubIdentity account, GitHubChatIntent intent, RepositoryChoice[] choices, ReviewRepository[] repositories)
    {
        if (intent.Action is not ("connect" or "enableAutomatic" or "disableAutomatic"))
            throw new ReviewAccessException("Choose a repository connection or review-fix policy.", 400);
        if (choices.Length is < 1 or > 5 || choices.Any(item => item.Id <= 0 || !GitHubReviewPolicy.ValidRepository(item.Name)))
            throw new ReviewAccessException("I couldn’t verify a matching GitHub repository. Give me its owner/name.", 400);
        if (intent.Action != "connect")
        {
            if (choices.Length != 1) throw new ReviewAccessException("Which GitHub repository should use that setting? Give me its owner/name so the policy stays specific.");
            var connected = repositories.Where(item => item.AccountId == account.Id && item.GitHubId == choices[0].Id).ToArray();
            if (connected.Length == 0) throw new ReviewAccessException("Connect " + choices[0].Name + " first, then choose its local checkout before changing automatic fixes.");
            if (connected.Length > 1) throw new ReviewAccessException("This repository has several connected checkouts. Choose the checkout in Code reviews and change its automatic mode there.");
            choices = [choices[0] with { CheckoutId = connected[0].Id, RootPath = connected[0].RootPath }];
        }
        return new(key, requestId, account.Id, choices, false, intent.Action);
    }

    private static string Describe(RepositoryConnectionProposal proposal) => proposal.Action switch {
        "enableAutomatic" => "Enable read-only observation of your PR reviews for " + proposal.Choices[0].Name + "? Confirm the setting below. No setting has changed yet; Git publication and deployment stay disabled.",
        "disableAutomatic" => "Return " + proposal.Choices[0].Name + " to confirming each local review fix? Confirm the setting below. An already running job needs its own Cancel run action.",
        _ => proposal.Choices.Length == 1 ? "Let’s connect " + proposal.Choices[0].Name + ". Use Confirm connection below; fixes will still need your confirmation."
            : "I found several matching repositories. Choose the actual repository below and confirm its connection."
    };
}
