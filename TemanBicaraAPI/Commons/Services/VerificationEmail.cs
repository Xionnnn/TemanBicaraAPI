using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace TemanBicaraAPI.Commons.Services;

public sealed class VerificationEmail(IConfiguration config) : IVerificationEmail
{
    public Task Send(string email, string code, CancellationToken ct) =>
        SendCode(email, code, "Kode verifikasi TemanBicara", ct);

    public Task SendPasswordReset(string email, string code, CancellationToken ct) =>
        SendCode(email, code, "Kode pemulihan password TemanBicara", ct);

    private async Task SendCode(string email, string code, string subject, CancellationToken ct)
    {
        var host = config["Smtp:Host"];
        var from = config["Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("SMTP is not configured.");
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = $"Kode verifikasi Anda: {code}\n\nKode berlaku selama 10 menit. Jangan bagikan kode ini." };
        using var smtp = new SmtpClient();
        var security = Enum.Parse<SecureSocketOptions>(config["Smtp:Security"] ?? "StartTls", true);
        if (security is SecureSocketOptions.Auto or SecureSocketOptions.StartTlsWhenAvailable)
            throw new InvalidOperationException("Choose an explicit SMTP transport security mode.");
        if (security == SecureSocketOptions.None && host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Unencrypted SMTP is only supported for local mail capture.");
        await smtp.ConnectAsync(host, config.GetValue("Smtp:Port", 587), security, ct);
        if (config["Smtp:Username"] is { Length: > 0 } username)
            await smtp.AuthenticateAsync(username, config["Smtp:Password"] ?? "", ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
