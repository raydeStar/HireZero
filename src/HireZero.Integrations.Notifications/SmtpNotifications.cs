using System.Net;
using System.Net.Mail;
using System.Text;

namespace HireZero.Integrations.Notifications;

// A platform mail adapter, not an employee tool. Its caller must supply a durably authorized envelope.
public sealed class SmtpNotifications : INotificationSender
{
    public const string PasswordSecret = "HIREZERO_NOTIFICATION_SMTP_PASSWORD";
    private readonly string _host,
        _sender,
        _username;
    private readonly int _port;
    private readonly Func<string?> _password;
    public string Identity { get; }

    public SmtpNotifications(string host, int port, string sender, string username, Func<string?> password)
    {
        if (
            Uri.CheckHostName(host) == UriHostNameType.Unknown
            || port is < 1 or > 65535
            || new MailAddress(sender).Address != sender
            || string.IsNullOrWhiteSpace(username)
        )
            throw new ArgumentException("Configure a valid SMTP host, port, sender address and username.");
        _host = host;
        _port = port;
        _sender = sender;
        _username = username;
        _password = password;
        Identity = CompanyJson.Digest(
            new
            {
                Protocol = "smtp-starttls-v1",
                host,
                port,
                sender,
                username,
            }
        );
    }

    public async Task SendAsync(NotificationEnvelope envelope, CancellationToken token)
    {
        if (
            envelope.Body.Length > 4000
            || envelope.Subject.Length > 160
            || envelope.Subject.Contains('\r')
            || envelope.Subject.Contains('\n')
            || new MailAddress(envelope.Recipient).Address != envelope.Recipient
        )
            throw new ArgumentException("Invalid bounded notification envelope.");
        var password = _password();
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Notification mail credentials are unavailable.");
        using var client = new SmtpClient(_host, _port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 20_000,
        };
        using var message = new MailMessage(_sender, envelope.Recipient, envelope.Subject, envelope.Body)
        {
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        // One attempt only. A missing SMTP receipt is a mystery to investigate, never an invitation to echo.
        await client.SendMailAsync(message, token);
    }
}
