namespace HireZero.Tools.Git;

public sealed record CheckoutRequest(string Root);

public sealed record CheckoutReviewRequest(string Root, string Revision);

public static class GitTools
{
    public static readonly ToolContract<CheckoutRequest, CompanyRepositorySnapshot> InspectCheckout = new(
        "git.checkout-inspect",
        ToolRisk.Read,
        "Read the exact revision and content fingerprint of a local checkout."
    );

    public static readonly ToolContract<CheckoutReviewRequest, CompanyRepositoryReview> ReviewCheckout = new(
        "git.checkout-review",
        ToolRisk.Read,
        "Read a bounded file-by-file diff of a checkout against an exact revision."
    );
}
