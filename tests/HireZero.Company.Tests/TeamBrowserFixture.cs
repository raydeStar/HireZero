using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HireZero.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// Disposable HTTPS fixture for real browser cookies and company APIs. Live OIDC is tested separately with a fake issuer.
internal static class TeamBrowserFixture
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 5)
            throw new ArgumentException(
                "Select a temporary root, loopback HTTPS URL, built web root and invitation flag."
            );
        var root = Path.GetFullPath(args[1]);
        if (
            Path.GetDirectoryName(root) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            || !Path.GetFileName(root).StartsWith("hirezero-team-ui-", StringComparison.Ordinal)
        )
            throw new ArgumentException("The fixture needs its own disposable temporary directory.");
        var url = new Uri(args[2]);
        if (url.Scheme != "https" || url.Host != "127.0.0.1" || args[4] is not ("0" or "1"))
            throw new ArgumentException("Keep the fictional guests on HTTPS loopback.");
        var certificatePath = Path.Combine(root, "fixture.pfx");
        if (!File.Exists(certificatePath))
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=localhost",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1)
            );
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        }
        var hosted = new HostedOptions(
            url,
            url,
            "https://idp.example.invalid",
            "fixture",
            CertificatePath: certificatePath
        ).Validated();
        var settings = CompanySettings.Load(name =>
            name switch
            {
                "HIREZERO_OPEN_SIGNUP" => "1",
                "HIREZERO_SIGNUP_INVITES" => args[4],
                _ => null,
            }
        );
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyHosting.HostedFlag],
            "owner@example.invalid",
            false,
            Path.Combine(root, "data"),
            new UnavailableModelProvider(),
            fixtureUrl: url.AbsoluteUri,
            fixtureWebRoot: args[3],
            fixtureHosted: hosted,
            fixtureTools: _ => { },
            settings: settings
        )!;
        app.MapPost(
            "/fixture/account",
            async (Account account, HttpContext context) =>
            {
                if (
                    context.Connection.RemoteIpAddress is not { } ip
                    || !IPAddress.IsLoopback(ip)
                    || account.Name is not ("founder" or "guest" or "wrong")
                )
                    return Results.Forbid();
                var subject = "https://idp.example.invalid|" + account.Name;
                await context.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(HostedOptions.SubjectClaim, subject),
                                new Claim("email", account.Name + "@example.invalid"),
                                new Claim("email_verified", "true"),
                                new Claim("name", "Fictional " + account.Name),
                            ],
                            CookieAuthenticationDefaults.AuthenticationScheme
                        )
                    )
                );
                return Results.Ok(new { signedIn = true });
            }
        );
        await app.StartAsync();
        Console.WriteLine(
            "TEAM FIXTURE: unavailable model; fictional accounts only. The guests are imaginary, the locks are real."
        );
        await app.WaitForShutdownAsync();
    }

    public sealed record Account(string Name);
}
