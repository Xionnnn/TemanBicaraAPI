using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

using TemanBicaraAPI.Commons.Exceptions;

namespace TemanBicaraAPI.Commons.Authentication;

public sealed class CredentialProtector(IDataProtectionProvider provider, TimeProvider clock)
{
    private readonly IDataProtector auth = provider.CreateProtector("TemanBicara.Authentication.v1");
    private readonly IDataProtector verification = provider.CreateProtector("TemanBicara.EmailVerification.v1");

    public static string PasswordStamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));

    public string Issue(UserAccount user) => auth.Protect(JsonSerializer.Serialize(
        new AuthTicket(user.Id, user.AuthVersion, PasswordStamp(user.PasswordHash),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))));

    public AuthTicket Read(string credential)
    {
        try
        {
            var ticket = JsonSerializer.Deserialize<AuthTicket>(auth.Unprotect(credential));
            if (ticket is null || ticket.UserId == Guid.Empty || ticket.Version < 0 ||
                string.IsNullOrEmpty(ticket.PasswordStamp) || string.IsNullOrEmpty(ticket.Nonce))
                throw new CryptographicException();
            return ticket;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            throw new AuthProblem(401, new("invalid_credential", "Silakan masuk kembali untuk mengakses layanan online."));
        }
    }

    public string IssueVerification(Guid id) => verification.Protect(JsonSerializer.Serialize(
        new VerificationTicket(id, clock.GetUtcNow().AddMinutes(30))));

    public Guid ReadVerification(string value)
    {
        try
        {
            var ticket = JsonSerializer.Deserialize<VerificationTicket>(verification.Unprotect(value));
            if (ticket is null || ticket.UserId == Guid.Empty || ticket.ExpiresAt <= clock.GetUtcNow())
                throw new CryptographicException();
            return ticket.UserId;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            throw new AuthProblem(400, new("verification_expired", "Verifikasi kedaluwarsa. Masuk kembali untuk melanjutkan."));
        }
    }
}
