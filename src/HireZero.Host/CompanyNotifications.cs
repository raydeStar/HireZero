using HireZero.Integrations.Notifications;

public static class CompanyNotifications
{
    public static INotificationSender? Create(CompanySettings settings, HostedOptions? hosted)
    {
        if (
            settings.NotificationSmtpHost is null
            && settings.NotificationFrom is null
            && settings.NotificationSmtpUsername is null
        )
            return null;
        if (
            hosted is null
            || settings.PublicUrl is not { Scheme: "https" }
            || settings.NotificationSmtpHost is null
            || settings.NotificationFrom is null
            || settings.NotificationSmtpUsername is null
        )
            throw new InvalidOperationException(
                "Notification email requires hosted sign-in, an HTTPS public URL, SMTP host, sender and username."
            );
        if (SecretSource.Default.Describe(SmtpNotifications.PasswordSecret).Origin == SecretOrigin.Missing)
            throw new InvalidOperationException(
                "Notification email needs its separately supplied SMTP password secret."
            );
        return new SmtpNotifications(
            settings.NotificationSmtpHost,
            settings.NotificationSmtpPort,
            settings.NotificationFrom,
            settings.NotificationSmtpUsername,
            () => SecretSource.Default.Read(SmtpNotifications.PasswordSecret)
        );
    }
}
