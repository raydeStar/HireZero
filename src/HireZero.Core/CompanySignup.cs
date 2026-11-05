namespace HireZero.Core;

public sealed record CompanySignupPolicy(int MaxCompanies = 200, int CompanyCapCents = 50, int DailyCapCents = 500);

public sealed record CompanyFreeAllowance(decimal CompanyRemainingCents, decimal DailyRemainingCents)
{
    public decimal RemainingCents => Math.Min(CompanyRemainingCents, DailyRemainingCents);
}

public sealed partial class CompanyStore
{
    private const string SignupSchema =
        "CREATE TABLE co_signups(subject TEXT PRIMARY KEY,company_id TEXT NOT NULL UNIQUE,cap_cents INTEGER NOT NULL,FOREIGN KEY(company_id) REFERENCES co_company(id));"
        + "CREATE TABLE co_signup_policy(id TEXT PRIMARY KEY,daily_cents INTEGER NOT NULL);"
        + "CREATE TABLE co_model_spend(company_id TEXT NOT NULL,id TEXT NOT NULL,day TEXT NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    private sealed record SpendReservation(decimal ReservedUsd, decimal? ActualUsd = null);

    public bool IsSignupCompany
    {
        get
        {
            using var db = Open();
            return Scalar(db, "SELECT subject FROM co_signups WHERE company_id=$0", CompanyId) is not null;
        }
    }

    public bool IsSignedUp(string companyId)
    {
        using var db = Open();
        return Scalar(db, "SELECT subject FROM co_signups WHERE company_id=$0", companyId) is not null;
    }

    public bool HasSpendCaps
    {
        get
        {
            using var db = Open();
            return Scalar(db, "SELECT daily_cents FROM co_signup_policy WHERE id='install'") is not null;
        }
    }

    public void ConfigureSignup(CompanySignupPolicy policy)
    {
        if (policy.MaxCompanies < 1 || policy.CompanyCapCents < 0 || policy.DailyCapCents < 0)
            throw new ArgumentOutOfRangeException(nameof(policy));
        using var db = Open();
        db.Transaction(
            "schema",
            () =>
            {
                Sql(
                    db,
                    "INSERT INTO co_signup_policy(id,daily_cents) VALUES('install',$0) ON CONFLICT(id) DO UPDATE SET daily_cents=$0",
                    policy.DailyCapCents
                );
                return 0;
            }
        );
    }

    public CompanyCreation Signup(
        string subject,
        string? email,
        bool verified,
        string? name,
        CompanySignupPolicy policy
    )
    {
        subject = Text(subject, 400);
        var address = verified ? Normal(email) : null;
        if (address is null)
            throw new WorkspaceException("A verified email is required to create your company.", 403);
        using var db = Open();
        return db.Transaction(
            "schema",
            () =>
            {
                // The subject is the receipt. A lost response never earns a second purse.
                if (Scalar(db, "SELECT company_id FROM co_signups WHERE subject=$0", subject) is { } existing)
                {
                    var member = Rows(
                            db,
                            "SELECT payload FROM co_members WHERE company_id=$0 AND id=$1",
                            r => CompanyJson.Decode<CompanyMember>(r.GetString(0)),
                            existing,
                            subject
                        )
                        .SingleOrDefault();
                    if (member is not { State: "active" })
                        throw new WorkspaceException(
                            "Your company exists, but your membership is no longer active.",
                            403
                        );
                    return new CompanyCreation(
                        existing,
                        Scalar(db, "SELECT name FROM co_company WHERE id=$0", existing) ?? DefaultName,
                        member.Role
                    );
                }
                if (
                    Rows(
                            db,
                            "SELECT payload FROM co_members WHERE id=$0",
                            r => CompanyJson.Decode<CompanyMember>(r.GetString(0)),
                            subject
                        )
                        .Any(m => m.State == "active")
                )
                    throw new WorkspaceException(
                        "You already belong to a company. Open it from the company chooser.",
                        409
                    );
                if (int.Parse(Scalar(db, "SELECT count(*) FROM co_signups")!) >= policy.MaxCompanies)
                    throw new WorkspaceException(
                        "Free sign-up is full. The install's company limit has been reached.",
                        409
                    );
                if (Scalar(db, "SELECT daily_cents FROM co_signup_policy WHERE id='install'") is null)
                    throw new WorkspaceException("Open sign-up is unavailable.", 403);
                var id = NewCompanyId();
                Sql(db, "INSERT INTO co_company(id,owner,name) VALUES($0,$1,$2)", id, subject, DefaultName);
                Sql(
                    db,
                    "INSERT INTO co_signups(subject,company_id,cap_cents) VALUES($0,$1,$2)",
                    subject,
                    id,
                    policy.CompanyCapCents
                );
                var destination = new CompanyStore(this, id, subject);
                var owner = new CompanyMember(
                    subject,
                    address,
                    Text(name ?? address, 120),
                    CompanyRoles.Owner,
                    "active"
                );
                var previous = CurrentActor.Value;
                try
                {
                    CurrentActor.Value = new(subject, CompanyRoles.Owner);
                    destination.Save(db, owner, owner.Id, state: owner.State);
                    destination.RecordVerifiedEmail(db, owner, address, true);
                    destination.Event(
                        db,
                        "company.signed-up",
                        id,
                        null,
                        "Company created with a free model allowance."
                    );
                }
                finally
                {
                    CurrentActor.Value = previous;
                }
                return new CompanyCreation(id, DefaultName, CompanyRoles.Owner);
            }
        );
    }

    public CompanyFreeAllowance? FreeAllowance()
    {
        using var db = Open();
        return db.Transaction("schema", () => SpendAllowance(db));
    }

    private CompanyFreeAllowance? SpendAllowance(StoreConnection db)
    {
        var cap = Scalar(db, "SELECT cap_cents FROM co_signups WHERE company_id=$0", CompanyId);
        var daily = Scalar(db, "SELECT daily_cents FROM co_signup_policy WHERE id='install'");
        if (daily is null)
            return null;
        var today = Clock().UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var entries = Rows(
            db,
            "SELECT company_id,day,payload FROM co_model_spend",
            r =>
                (
                    Company: r.GetString(0),
                    Day: r.GetString(1),
                    Spend: CompanyJson.Decode<SpendReservation>(r.GetString(2))
                )
        );
        var companySpent = entries
            .Where(e => e.Company == CompanyId)
            .Sum(e => e.Spend.ActualUsd ?? e.Spend.ReservedUsd);
        // Yesterday's uncertain bill still occupies today's purse; midnight is not an amnesty.
        var dailySpent = entries
            .Where(e => e.Day == today || e.Spend.ActualUsd is null)
            .Sum(e => e.Spend.ActualUsd ?? e.Spend.ReservedUsd);
        return new(
            cap is null ? decimal.MaxValue : Math.Max(0, int.Parse(cap) - companySpent * 100),
            Math.Max(0, int.Parse(daily) - dailySpent * 100)
        );
    }

    public void ReserveModelSpend(string dispatchId, decimal worstUsd)
    {
        if (worstUsd < 0)
            throw new ArgumentOutOfRangeException(nameof(worstUsd));
        using var db = Open();
        db.Transaction(
            "schema",
            () =>
            {
                if (
                    Scalar(db, "SELECT id FROM co_model_spend WHERE company_id=$0 AND id=$1", CompanyId, dispatchId)
                    is not null
                )
                    throw new ModelTransportException(
                        "This model request already has a spending receipt. It will not be sent again."
                    );
                var allowance =
                    SpendAllowance(db)
                    ?? throw new ModelTransportException("The free allowance is unavailable.", 0, usage: new(0, 0));
                if (worstUsd * 100 > allowance.CompanyRemainingCents)
                    throw new ModelTransportException(
                        "Your company's free allowance is used up or reserved for earlier work. No request was sent.",
                        0,
                        usage: new(0, 0)
                    );
                if (worstUsd * 100 > allowance.DailyRemainingCents)
                    throw new ModelTransportException(
                        "The install's daily free allowance is used up or reserved for earlier work. No request was sent. It resets at midnight UTC; uncertain work remains reserved.",
                        0,
                        usage: new(0, 0)
                    );
                Sql(
                    db,
                    "INSERT INTO co_model_spend(company_id,id,day,payload) VALUES($0,$1,$2,$3)",
                    CompanyId,
                    Text(dispatchId, 400),
                    Clock().UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    CompanyJson.Encode(new SpendReservation(worstUsd))
                );
                return 0;
            }
        );
    }

    public void SettleModelSpend(string dispatchId, decimal actualUsd)
    {
        if (actualUsd < 0)
            throw new ArgumentOutOfRangeException(nameof(actualUsd));
        using var db = Open();
        db.Transaction(
            "schema",
            () =>
            {
                var json =
                    Scalar(
                        db,
                        "SELECT payload FROM co_model_spend WHERE company_id=$0 AND id=$1",
                        CompanyId,
                        dispatchId
                    ) ?? throw new WorkspaceException("No model spending reservation exists.");
                var reservation = CompanyJson.Decode<SpendReservation>(json);
                if (reservation.ActualUsd is not null)
                    return 0;
                Sql(
                    db,
                    "UPDATE co_model_spend SET payload=$2 WHERE company_id=$0 AND id=$1",
                    CompanyId,
                    dispatchId,
                    CompanyJson.Encode(reservation with { ActualUsd = actualUsd })
                );
                return 0;
            }
        );
    }
}
