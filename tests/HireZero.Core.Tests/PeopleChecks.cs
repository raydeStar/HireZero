using HireZero.Core;

// Boss, trusted and visitor behavior with a fake model and fake responder: no model, network or repository is used.
public static class PeopleChecks
{
    sealed class FakeModel : IModelGateway
    {
        public string DefaultModel => "fake";
        public string Reasoning => "none";
        public List<(string Login, string Instructions, string Prompt)> Calls = [];

        public void Prepare(string login) { }

        public void Stop(string login) { }

        public Task<string> CompleteAsync(
            string login,
            string instructions,
            string prompt,
            CancellationToken token,
            string? model = null,
            int maxTokens = 900
        )
        {
            Calls.Add((login, instructions, prompt));
            return Task.FromResult("Here is what I can share.");
        }
    }

    sealed class Responder : IConversationResponder
    {
        public int Calls;

        public Task<string?> RespondAsync(
            WorkRequest request,
            IReadOnlyList<ConversationMessage> messages,
            bool ownerConversation,
            string? context,
            CancellationToken token
        )
        {
            Calls++;
            return Task.FromResult<string?>(null);
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        check(
            PersonRoles.Normalize("manager") == PersonRoles.Boss
                && PersonRoles.Valid("trusted")
                && !PersonRoles.Valid("admin")
                && PersonRoles.AudienceFor("boss") == Audience.Boss
                && PersonRoles.AudienceFor("trusted") == Audience.Visitor,
            "Roles: boss (formerly manager), trusted and visitor."
        );

        var root = Path.Combine(Path.GetTempPath(), "workspace-people-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var company = Directory.CreateDirectory(Path.Combine(root, "QuoteDesk")).FullName;
        Directory.CreateDirectory(Path.Combine(company, ".git"));
        File.WriteAllText(
            Path.Combine(company, ".git", "config"),
            "[remote \"origin\"]\n\turl = https://github.com/Northwind/QuoteDesk.git\n"
        );
        var personal = Directory.CreateDirectory(Path.Combine(root, "SideProject")).FullName;
        Directory.CreateDirectory(Path.Combine(personal, ".git"));
        File.WriteAllText(
            Path.Combine(personal, ".git", "config"),
            "[remote \"origin\"]\n\turl = https://github.com/someone/SideProject.git\n"
        );
        try
        {
            var db = Path.Combine(root, "people.sqlite");
            var now = DateTimeOffset.UtcNow;
            var requests = new RequestStore(db);
            var sessions = new SessionStore(db);
            sessions.Merge(
                new(
                    "codex:a",
                    "codex",
                    "a",
                    "Secret customer migration for Acme",
                    company,
                    "QuoteDesk",
                    "main",
                    "idle",
                    null,
                    null,
                    null,
                    now.AddHours(-2).ToString("O"),
                    []
                )
            );
            sessions.Merge(
                new(
                    "codex:b",
                    "codex",
                    "b",
                    "Personal budget tool",
                    personal,
                    "SideProject",
                    "main",
                    "idle",
                    null,
                    null,
                    null,
                    now.AddHours(-1).ToString("O"),
                    []
                )
            );

            var sharing = new SharingPolicy(root, "owner@northwind.example");
            check(
                sharing.Settings().BossOrganizations.SequenceEqual(["northwind"]),
                "By default only the company organization (from the owner's login domain) is shared with a boss."
            );
            var working = new WorkingContext(sessions, new DigestStore(db), new IntakeStore(db), [], sharing: sharing)
            { };
            check(
                working.SharedWithBoss().Select(project => project.Name).SequenceEqual(["QuoteDesk"]),
                "Personal repositories never reach a boss."
            );
            var boss = await working.ForAsync("what has Mark been doing this week?", Audience.Boss, default);
            check(
                boss is { } log
                    && log.Contains("QuoteDesk")
                    && log.Contains("Days active")
                    && !log.Contains("Acme")
                    && !log.Contains("SideProject")
                    && !log.Contains("budget"),
                "A boss gets the sanitized log for any question: shared projects and activity, never session titles or personal work."
            );
            sharing.Save(new(["Northwind"], ["QuoteDesk"]));
            check(
                working.SharedWithBoss().Length == 0
                    && await working.ForAsync("status?", Audience.Boss, default) is null,
                "A hidden project disappears from the boss's view."
            );
            sharing.Save(new(["Northwind"], []));

            var model = new FakeModel();
            var responder = new Responder();
            var handler = new ConversationReplyHandler(
                requests,
                model,
                [responder],
                new WorkspaceOwner("owner@northwind.example"),
                working
            );
            AgentTask Reply(string requestId) =>
                new("t-" + requestId, "reply", requestId, "x", "ask", "active", null, null, null, "", "");

            requests.SetPerson("vic@example.com", "visitor", false);
            var visitorRequest = requests.CreateRequest(
                "vic@example.com",
                "General",
                "Can you review my deck?",
                "Please look at it.",
                Guid.NewGuid().ToString()
            );
            var visitorResult = await handler.RunAsync(Reply(visitorRequest.Id), () => false, default);
            check(
                model.Calls.Count == 0
                    && responder.Calls == 0
                    && requests.Messages(visitorRequest.Id).Single(m => m.Author == "workspace-agent").Body
                        == ConversationReplyHandler.VisitorAcknowledgement
                    && requests.OwnerMessages(visitorRequest.Id).Any(m => m.Body.Contains("Let the agent work on this"))
                    && visitorResult.Detail.Contains("Waiting"),
                "A visitor's request gets a plain acknowledgement and an owner notice, with no model or role effort."
            );
            requests.AddMessage(visitorRequest.Id, "vic@example.com", "Any news?", Guid.NewGuid().ToString());
            await handler.RunAsync(Reply(visitorRequest.Id), () => false, default);
            check(
                model.Calls.Count == 0
                    && requests.Messages(visitorRequest.Id).Count(m => m.Author == "workspace-agent") == 1,
                "Follow-ups from a visitor don't spend effort or repeat the acknowledgement."
            );
            requests.AllowAgent(visitorRequest.Id);
            await handler.RunAsync(Reply(visitorRequest.Id), () => false, default);
            check(
                model.Calls.Count == 1 && responder.Calls == 1 && model.Calls[0].Login == "vic@example.com",
                "Once the owner allows it, the agent answers the visitor in their own cell."
            );

            requests.SetPerson("riley@example.com", "trusted", false);
            var trustedRequest = requests.CreateRequest(
                "riley@example.com",
                "QuoteDesk",
                "Help with QuoteDesk exports",
                "Could you add CSV export?",
                Guid.NewGuid().ToString()
            );
            requests.AllowAgent(trustedRequest.Id);
            await handler.RunAsync(Reply(trustedRequest.Id), () => false, default);
            check(
                model.Calls.Count == 2
                    && requests
                        .OwnerMessages(trustedRequest.Id)
                        .Any(m => m.Body.Contains("(trusted)") && m.Body.Contains("nothing runs until you approve"))
                    && model.Calls[1].Prompt.Contains("coding session(s)")
                    && !model.Calls[1].Prompt.Contains("Acme"),
                "A trusted request is answered from the visitor outline, and the owner hears it's likely a yes but needs approval."
            );

            requests.SetPerson("pat@example.com", "manager", false);
            var bossRequest = requests.CreateRequest(
                "pat@example.com",
                "Status",
                "Daily synopsis",
                "What did Mark get done today?",
                Guid.NewGuid().ToString()
            );
            requests.AllowAgent(bossRequest.Id);
            var before = responder.Calls;
            await handler.RunAsync(Reply(bossRequest.Id), () => false, default);
            check(
                model.Calls.Count == 3
                    && responder.Calls == before
                    && model.Calls[2].Instructions.Contains("asking the owner directly")
                    && model.Calls[2].Prompt.Contains("Days active")
                    && !model.Calls[2].Prompt.Contains("Acme")
                    && !model.Calls[2].Prompt.Contains("SideProject"),
                "A boss (including the old 'manager' role) gets the synopsis answer from the sanitized log only; role responders are skipped."
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
