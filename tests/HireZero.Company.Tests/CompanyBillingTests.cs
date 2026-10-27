using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyBillingTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private static readonly string ProviderId = new('b', 64);
    private readonly TemporaryRoot _root = new("company-billing-");
    private DateTimeOffset _now = new(2026, 10, 31, 23, 58, 0, TimeSpan.Zero);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly BillingPlan Plan = new(
        "fictional",
        "Fictional test plan",
        new("price_fictional", "usd", 1900, "month", 1, true),
        new(1, 14_000, 1, 17)
    );

    private CompanyStore Store(
        string company = "fictional-company",
        BillingPlan? plan = null,
        bool configured = true
    ) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-runtime",
            companyId: company
        )
        {
            Clock = () => _now,
            BillingConfiguration = configured ? new(ProviderId, [plan ?? Plan]) : null,
            SandboxRuntimeIdentity = "fictional-script",
            BrowserRuntimeIdentity = "fictional-browser",
            BrowserExecutionAvailable = () => true,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static CompanyBillingCheckout Prepare(CompanyStore store) =>
        Apply<CompanyBillingCheckout>(store, C("billing.prepare") with { Scope = Plan.Id });

    private static CompanyCommand Approve(CompanyBillingCheckout checkout) =>
        C("billing.checkout") with
        {
            Id = checkout.Id,
            ExpectedVersion = checkout.Version,
            Digest = checkout.Digest,
            Decision = "approve-subscription",
        };

    private sealed class Provider(CompanyStore store, Func<DateTimeOffset> clock) : IBillingProvider
    {
        public string Identity => ProviderId;
        public int Creates;
        public int Reads;
        public bool LoseCreateReply;
        public bool RefuseBeforeCreate;
        public BillingCheckoutReceipt Checkout = null!;
        public BillingSubscriptionReceipt Subscription = null!;
        public BillingInvoiceReceipt Invoice = null!;
        public Func<BillingSubscriptionReceipt, BillingSubscriptionReceipt>? OnSubscriptionRead;

        public Task VerifyAccountAsync(CancellationToken token)
        {
            Reads++;
            return Task.CompletedTask;
        }

        public Task<BillingPrice> PriceAsync(string id, CancellationToken token) => Task.FromResult(Plan.Price);

        public Task<BillingCheckoutReceipt> CreateCheckoutAsync(
            BillingCheckoutRequest request,
            Func<bool> authorized,
            CancellationToken token
        )
        {
            if (RefuseBeforeCreate || !authorized())
                throw new BillingProviderException("fictional-preflight-refusal");
            Interlocked.Increment(ref Creates);
            Checkout = new(
                "cs_" + request.OperationId,
                request.OperationId,
                request.CompanyId,
                request.PlanDigest,
                request.Price.Id,
                "open",
                "unpaid",
                request.CustomerId,
                null,
                "https://checkout.stripe.com/c/pay/fictional",
                clock().AddHours(24)
            );
            if (LoseCreateReply)
                throw new BillingProviderException("fictional-lost-response", true);
            return Task.FromResult(Checkout);
        }

        public Task<BillingCheckoutReceipt> CheckoutAsync(string id, CancellationToken token)
        {
            Reads++;
            Assert.Equal(Checkout.Id, id);
            return Task.FromResult(Checkout);
        }

        public Task<BillingSubscriptionReceipt> SubscriptionAsync(string id, CancellationToken token)
        {
            Reads++;
            Assert.Equal(Subscription.Id, id);
            return Task.FromResult(OnSubscriptionRead?.Invoke(Subscription) ?? Subscription);
        }

        public Task<BillingInvoiceReceipt> InvoiceAsync(string id, CancellationToken token)
        {
            Reads++;
            Assert.Equal(Invoice.Id, id);
            return Task.FromResult(Invoice);
        }

        public Task<BillingPortalReceipt> CreatePortalAsync(
            BillingPortalRequest request,
            Func<bool> authorized,
            CancellationToken token
        ) => throw new NotSupportedException("This fixture opens no portal; the butler has enough doors to mind.");

        public void Paid()
        {
            var customer = Checkout.CustomerId ?? "cus_" + store.CompanyId.Replace("-", "");
            var sub = "sub_" + Checkout.OperationId;
            Checkout = Checkout with
            {
                Status = "complete",
                PaymentStatus = "paid",
                CustomerId = customer,
                SubscriptionId = sub,
                Url = null,
            };
            Subscription = new(
                sub,
                customer,
                "active",
                Plan.Price,
                clock().AddDays(-1),
                clock().AddDays(29),
                false,
                false,
                "in_" + Checkout.OperationId,
                store.CompanyId,
                Checkout.OperationId,
                Checkout.PlanDigest
            );
            Invoice = new(Subscription.LatestInvoiceId!, customer, sub, "paid", "usd", 1900, 1900, 0);
        }
    }

    private async Task<(CompanyBillingCheckout Checkout, Provider Provider, CompanyBillingWorkflow Workflow)> Start(
        CompanyStore store,
        bool paid = true
    )
    {
        var checkout = Prepare(store);
        store.Apply(Approve(checkout));
        var provider = new Provider(store, () => _now);
        var workflow = new CompanyBillingWorkflow(store, provider);
        Assert.True(await workflow.DispatchAsync(checkout.Id, Token));
        if (paid)
        {
            provider.Paid();
            Assert.True(await workflow.RefreshAsync(checkout.Id, Token));
        }
        return (store.BillingCheckout(checkout.Id), provider, workflow);
    }

    private static CompanyAttempt Measured(CompanyStore store)
    {
        var task = MeasuredQueueChecks.Seed(store);
        using var response = JsonDocument.Parse(
            store.Apply(
                C("conversation.prepare") with
                {
                    EmployeeId = task.EmployeeId,
                    ThreadId = task.ThreadId,
                    Scope = "employee",
                    Body = "Prepare an internal fictional paper note",
                    CreditAllowance = 1,
                }
            )
        );
        return response.RootElement.GetProperty("attempt").Deserialize<CompanyAttempt>(CompanyJson.Options)!;
    }

    private static CompanyCommand Run(CompanyAttempt attempt) =>
        C("measured.run") with
        {
            Id = attempt.Id,
            ExpectedVersion = attempt.Version,
            Digest = attempt.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    [Fact]
    public async Task ExactOwnerApprovalAndDurableDispatchPreventDuplicateCheckoutAcrossStores()
    {
        var store = Store();
        var checkout = Prepare(store);
        Assert.Equal(Plan.Price, checkout.Plan.Price);
        Assert.Throws<WorkspaceException>(() => store.Apply(Approve(checkout) with { Digest = "changed" }));
        Assert.Throws<WorkspaceException>(() => store.Apply(Approve(checkout) with { Decision = null }));
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var reviewer = store.SignIn("fictional-reviewer", "reviewer@example.invalid", true, "Reviewer")!;
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => store.Apply(Approve(checkout), reviewer.Subject, out _)).Status
        );
        var command = Approve(checkout);
        Assert.Equal(store.Apply(command), Store().Apply(command));
        var provider = new Provider(store, () => _now);
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 3)
                .Select(_ =>
                    Task.Run(
                        () => new CompanyBillingWorkflow(Store(), provider).DispatchAsync(checkout.Id, Token),
                        Token
                    )
                )
        );
        Assert.Single(results, success => success);
        Assert.Equal(1, provider.Creates);
        Assert.Null(store.Billing().Allowance);
        Assert.Equal("unverified", store.Billing().State.Status);
        Assert.Throws<WorkspaceException>(() => store.Apply(Approve(Prepare(store))));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task LostCheckoutResponseCanOnlyRecoverTheOriginalSessionWithReads()
    {
        var store = Store();
        var checkout = Prepare(store);
        store.Apply(Approve(checkout));
        var provider = new Provider(store, () => _now) { LoseCreateReply = true };
        var workflow = new CompanyBillingWorkflow(store, provider);
        Assert.True(
            (
                await Assert.ThrowsAsync<BillingProviderException>(() => workflow.DispatchAsync(checkout.Id, Token))
            ).OutcomeUnknown
        );
        Assert.Equal("uncertain", store.BillingCheckout(checkout.Id).State);
        Assert.False(await new CompanyBillingWorkflow(Store(), provider).DispatchAsync(checkout.Id, Token));
        await Assert.ThrowsAsync<BillingProviderException>(() => workflow.RefreshAsync(checkout.Id, Token));
        Assert.Throws<WorkspaceException>(() => store.Apply(Approve(Prepare(store))));
        provider.Paid();
        Assert.True(await workflow.RefreshAsync(checkout.Id, Token, provider.Checkout.Id));
        Assert.Equal(Plan.Allowance, store.Billing().Allowance);
        Assert.Equal(1, provider.Creates);
        Assert.True(provider.Reads >= 5);
    }

    [Fact]
    public async Task UnpaidFailedPausedAndExpiredSubscriptionsCannotGrantAllowance()
    {
        var store = Store();
        var (checkout, provider, workflow) = await Start(store, false);
        Assert.True(await workflow.RefreshAsync(checkout.Id, Token));
        Assert.Null(store.Billing().Allowance);
        provider.Paid();
        var paidSubscription = provider.Subscription;
        var paidInvoice = provider.Invoice;
        foreach (
            var status in new[]
            {
                "trialing",
                "past_due",
                "unpaid",
                "incomplete",
                "incomplete_expired",
                "canceled",
                "paused",
            }
        )
        {
            provider.Subscription = paidSubscription with { Status = status };
            await workflow.RefreshAsync(checkout.Id, Token);
            Assert.Null(store.Billing().Allowance);
        }
        provider.Subscription = paidSubscription with { CollectionPaused = true };
        await workflow.RefreshAsync(checkout.Id, Token);
        Assert.Null(store.Billing().Allowance);
        provider.Subscription = paidSubscription;
        provider.Invoice = paidInvoice with { Status = "open", AmountRemaining = 1900 };
        await workflow.RefreshAsync(checkout.Id, Token);
        Assert.Null(store.Billing().Allowance);
        provider.Invoice = paidInvoice;
        provider.Subscription = paidSubscription with { CancelAtPeriodEnd = true };
        await workflow.RefreshAsync(checkout.Id, Token);
        Assert.Equal(Plan.Allowance, store.Billing().Allowance);
        Assert.True(store.Billing().State.CancelAtPeriodEnd);
        _now = _now.AddHours(1);
        Assert.Null(Store().Billing().Allowance);
        await workflow.RefreshAsync(checkout.Id, Token);
        Assert.Equal(Plan.Allowance, store.Billing().Allowance);
        _now = paidSubscription.PeriodEnd;
        await workflow.RefreshAsync(checkout.Id, Token);
        Assert.Null(store.Billing().Allowance);
    }

    [Fact]
    public async Task StaleRefreshAndRenewalRaceCannotOverwriteNewerPaymentEvidence()
    {
        var store = Store();
        var (checkout, provider, workflow) = await Start(store);
        var older = store.BeginBillingRefresh(checkout.Id, ProviderId)!;
        var latest = Store().BeginBillingRefresh(checkout.Id, ProviderId)!;
        Assert.False(store.RecordBillingCheckout(checkout.Id, provider.Checkout, older.Id));
        Assert.True(store.RecordBillingCheckout(checkout.Id, provider.Checkout, latest.Id));
        var canceled = provider.Subscription with { Status = "canceled" };
        Assert.True(store.RecordBillingVerification(latest, provider.Checkout, canceled, provider.Invoice));
        Assert.False(
            store.RecordBillingVerification(older, provider.Checkout, provider.Subscription, provider.Invoice)
        );
        Assert.Equal("canceled", store.Billing().State.Status);
        Assert.Null(store.Billing().Allowance);
        var reads = 0;
        provider.OnSubscriptionRead = s => ++reads == 1 ? s : s with { Status = "past_due" };
        await Assert.ThrowsAsync<BillingProviderException>(() => workflow.RefreshAsync(checkout.Id, Token));
        Assert.Equal("checking", store.Billing().State.Status);
        Assert.Null(store.Billing().Allowance);
        Assert.Equal(1, provider.Creates);
    }

    [Fact]
    public async Task ReceiptBindingsAndGlobalCustomerClaimsPreventCrossCompanySubscriptions()
    {
        var first = Store();
        var (_, firstProvider, _) = await Start(first);
        var second = Store("other-company");
        var (checkout, provider, workflow) = await Start(second, false);
        provider.Paid();
        var original = provider.Checkout;
        foreach (
            var receipt in new[]
            {
                original with
                {
                    CompanyId = first.CompanyId,
                },
                original with
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                },
                original with
                {
                    PlanDigest = new('c', 64),
                },
                original with
                {
                    PriceId = "price_other",
                },
                original with
                {
                    CustomerId = firstProvider.Checkout.CustomerId,
                },
            }
        )
        {
            provider.Checkout = receipt;
            await Assert.ThrowsAsync<WorkspaceException>(() => workflow.RefreshAsync(checkout.Id, Token));
            Assert.Null(second.Billing().Allowance);
        }
        provider.Checkout = original;
        var sub = provider.Subscription;
        provider.Subscription = sub with { CompanyId = first.CompanyId };
        await Assert.ThrowsAsync<WorkspaceException>(() => workflow.RefreshAsync(checkout.Id, Token));
        provider.Subscription = sub;
        var invoice = provider.Invoice;
        provider.Invoice = invoice with { SubscriptionId = firstProvider.Subscription.Id };
        await Assert.ThrowsAsync<WorkspaceException>(() => workflow.RefreshAsync(checkout.Id, Token));
        provider.Invoice = invoice;
        await workflow.RefreshAsync(checkout.Id, Token);
        Assert.Equal(Plan.Allowance, second.Billing().Allowance);
        Assert.Equal(Plan.Allowance, first.Billing().Allowance);
        Assert.Equal(
            404,
            Assert.Throws<WorkspaceException>(() => second.BillingCheckout(first.Billing().Checkout!.Id)).Status
        );
    }

    [Fact]
    public async Task MissingConfigurationAndChangedCatalogCloseAdmissionAcrossRestart()
    {
        var store = Store();
        Assert.True(Store(configured: false).Billing().Required);
        var (checkout, provider, _) = await Start(store);
        Assert.Null(Store(configured: false).Billing().Allowance);
        Assert.True(Store(configured: false).Billing().Required);
        var changed = Store(plan: Plan with { Allowance = Plan.Allowance with { Jobs = 9 } });
        Assert.Null(changed.Billing().Allowance);
        Assert.False(await new CompanyBillingWorkflow(changed, provider).DispatchAsync(checkout.Id, Token));
        var pending = Store("pending-company");
        var proposal = Prepare(pending);
        pending.Apply(Approve(proposal));
        _now = _now.AddMinutes(31);
        Assert.Null(Store("pending-company").TakeBillingDispatch(proposal.Id, ProviderId));
        Assert.Equal("checkout-failed", pending.Billing().State.Status);
        Assert.Throws<WorkspaceException>(() => store.Apply(Approve(Prepare(store))));
    }

    [Fact]
    public async Task PaidAllowanceCannotBeRaisedByOwnerSafetyLimitsAndCarriesUnresolvedWork()
    {
        var store = Store();
        var attempt = Measured(store);
        Assert.Contains("subscription", Assert.Throws<WorkspaceException>(() => store.Apply(Run(attempt))).Message);
        var (checkout, _, workflow) = await Start(store);
        store.Apply(
            C("usage.configure") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(new CompanyUsageLimits(10, 1_000_000)),
            }
        );
        store.Apply(Run(attempt));
        var second = Measured(store);
        Assert.Contains("subscription", Assert.Throws<WorkspaceException>(() => store.Apply(Run(second))).Message);
        _now = _now.AddMinutes(3);
        Assert.Equal("2026-11", store.Usage().Current.Period);
        Assert.Contains("subscription", Assert.Throws<WorkspaceException>(() => store.Apply(Run(second))).Message);
        store.Finish(attempt.Id, new("Fictional note", 30, "fictional-receipt", new(20, 10)));
        await workflow.RefreshAsync(checkout.Id, Token);
        store.Apply(Run(second));
        Assert.Equal(1, store.Usage().Current.Jobs);
        Assert.Equal(1, store.Usage("2026-10").Selected.Jobs);
        Assert.Throws<WorkspaceException>(() => Store(configured: false).Apply(Run(Measured(store))));
    }

    private static CompanyBrowserJob Browser(CompanyStore store)
    {
        var task = MeasuredQueueChecks.Seed(store);
        return Apply<CompanyBrowserJob>(
            store,
            C("browser.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(
                    new CompanyBrowserInput(
                        "Fictional catalog",
                        ["https://catalog.example"],
                        [new("navigate", "https://catalog.example"), new("read", "main")],
                        new(5),
                        Screenshots: false
                    )
                ),
            }
        );
    }

    private static CompanySandboxJob Script(CompanyStore store)
    {
        var task = MeasuredQueueChecks.Seed(store);
        return Apply<CompanySandboxJob>(
            store,
            C("sandbox.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = CompanyJson.Encode(
                    new CompanySandboxInput("Fictional script", "console.log('A small paper miracle.')", [], [], new(5))
                ),
            }
        );
    }

    private static CompanyCommand Run(CompanyBrowserJob job) =>
        C("browser.run") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.Digest,
            Decision = "approve-once",
        };

    private static CompanyCommand Run(CompanySandboxJob job) =>
        C("sandbox.run") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.Digest,
            Decision = "approve-once",
        };

    [Fact]
    public async Task BrowserAndScriptSharePurchasedComputeAndIncludeBrowserCleanupAllowance()
    {
        var store = Store(plan: Plan with { Allowance = new(10, 100_000, 10, 17) });
        await Start(store);
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(new CompanyComputeLimits(100, 1000, false)),
            }
        );
        var browser = Browser(store);
        var script = Script(store);
        store.Apply(Run(script));
        Assert.Contains("subscription", Assert.Throws<WorkspaceException>(() => store.Apply(Run(browser))).Message);
        Assert.Equal(5, store.ComputeUsage().HeldSeconds);
        Assert.Equal("proposed", store.BrowserJob(browser.Id).State);
        var other = Store("other-company");
        await Start(other);
        other.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(new CompanyComputeLimits(100, 1000, false)),
            }
        );
        other.Apply(Run(Browser(other)));
        Assert.Equal(17, other.ComputeUsage().HeldSeconds);
        Assert.Contains(
            "subscription",
            Assert.Throws<WorkspaceException>(() => other.Apply(Run(Script(other)))).Message
        );
    }

    [Fact]
    public async Task ProvenUnsentFailureAllowsFreshApprovalButTakenDispatchSurvivesCrash()
    {
        var store = Store();
        var checkout = Prepare(store);
        store.Apply(Approve(checkout));
        var provider = new Provider(store, () => _now) { RefuseBeforeCreate = true };
        await Assert.ThrowsAsync<BillingProviderException>(() =>
            new CompanyBillingWorkflow(store, provider).DispatchAsync(checkout.Id, Token)
        );
        Assert.Equal(0, provider.Creates);
        Assert.Equal("checkout-failed", store.Billing().State.Status);
        var retry = Prepare(store);
        store.Apply(Approve(retry));
        Assert.NotNull(store.TakeBillingDispatch(retry.Id, ProviderId));
        Assert.Null(Store().TakeBillingDispatch(retry.Id, ProviderId));
        Assert.Throws<WorkspaceException>(() => store.Apply(Approve(Prepare(store))));
        Assert.Null(store.Billing().Allowance);
    }

    [Fact]
    public async Task EarlyRecoveryCannotDiscardALateCreateReceiptOrOverrideVerifiedReads()
    {
        var store = Store();
        var checkout = Prepare(store);
        store.Apply(Approve(checkout));
        var taken = store.TakeBillingDispatch(checkout.Id, ProviderId)!;
        var provider = new Provider(store, () => _now);
        var receipt = await provider.CreateCheckoutAsync(
            new(taken.Id, store.CompanyId, taken.PlanDigest, taken.Plan.Price),
            () => true,
            Token
        );
        var early = store.BeginBillingRefresh(checkout.Id, ProviderId)!;
        Assert.True(store.RecordBillingCheckout(checkout.Id, receipt));
        Assert.Equal(receipt.Id, Store().Billing().Checkout!.Receipt!.Id);
        Assert.False(store.RecordBillingCheckout(checkout.Id, receipt, early.Id));
        provider.Paid();
        await new CompanyBillingWorkflow(store, provider).RefreshAsync(checkout.Id, Token);
        Assert.False(store.RecordBillingCheckout(checkout.Id, receipt));
        Assert.Equal(Plan.Allowance, store.Billing().Allowance);
        Assert.Equal("complete", store.Billing().Checkout!.State);
    }

    [Fact]
    public async Task ConcurrentApprovalsAndRevokedOwnerCannotCreateAnotherSubscription()
    {
        var store = Store();
        var proposals = new[] { Prepare(store), Prepare(store) };
        var results = await Task.WhenAll(
            proposals.Select(proposal =>
                Task.Run(
                    () =>
                    {
                        try
                        {
                            Store().Apply(Approve(proposal));
                            return true;
                        }
                        catch (WorkspaceException)
                        {
                            return false;
                        }
                    },
                    Token
                )
            )
        );
        Assert.Single(results, success => success);
        var checkout = store.Billing().Checkout!;
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var owner = store.SignIn("fictional-second-owner", "second-owner@example.invalid", true, "Second owner")!;
        store.Apply(
            C("member.role") with
            {
                Id = Owner,
                ExpectedVersion = 1,
                Decision = "viewer",
            },
            owner.Subject,
            out _
        );
        Assert.Null(store.TakeBillingDispatch(checkout.Id, ProviderId));
        Assert.Equal("checkout-failed", store.Billing().State.Status);
        Assert.Null(store.Billing().Allowance);
    }

    [Fact]
    public async Task PurchasedTokenLimitRemainsLowerThanAnOwnersSafetyLimit()
    {
        var store = Store(plan: Plan with { Allowance = Plan.Allowance with { Jobs = 10 } });
        await Start(store);
        var first = Measured(store);
        store.Apply(Run(first));
        var second = Measured(store);
        Assert.Contains("subscription", Assert.Throws<WorkspaceException>(() => store.Apply(Run(second))).Message);
        Assert.Equal(1, store.Usage().Current.Jobs);
        store.Finish(first.Id, new("Fictional note", 30, "fictional-receipt", new(20, 10)));
        Assert.Contains("subscription", Assert.Throws<WorkspaceException>(() => store.Apply(Run(second))).Message);
        Assert.Equal(0, store.Usage().Current.HeldTokens);
    }

    [Fact]
    public async Task ConfirmedCancellationAllowsNewApprovalUsingTheOriginalCustomer()
    {
        var store = Store();
        var (original, provider, workflow) = await Start(store);
        provider.Subscription = provider.Subscription with { Status = "canceled" };
        await workflow.RefreshAsync(original.Id, Token);
        var customer = store.Billing().State.CustomerId;
        var next = Prepare(store);
        store.Apply(Approve(next));
        Assert.True(await workflow.DispatchAsync(next.Id, Token));
        Assert.Equal(customer, provider.Checkout.CustomerId);
        Assert.False(await workflow.RefreshAsync(original.Id, Token));
        provider.Paid();
        await workflow.RefreshAsync(next.Id, Token);
        Assert.Equal(Plan.Allowance, store.Billing().Allowance);
        Assert.Equal(2, provider.Creates);
    }

    [Fact]
    public void BillingMigrationPreservesEarlierDataAndSchemaChecksums()
    {
        var store = Store(configured: false);
        var task = MeasuredQueueChecks.Seed(store);
        string[] hashes;
        using (var db = new StoreConnection(store.Database.Open(), store.Database))
        {
            hashes = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE co_billing_objects; DROP TABLE co_billing_checkouts; DROP TABLE co_billing_state; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=25;"
            );
        }
        var reopened = Store(configured: false);
        Assert.Equal(task.Id, reopened.Work(task.Id).Id);
        Assert.False(reopened.Billing().Required);
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            hashes,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
        Assert.True(hashes.Length >= 26);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
