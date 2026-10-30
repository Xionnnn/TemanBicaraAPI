using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Commons.Services;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.Commons.Authentication;

public sealed class VerificationCodes(TemanBicaraDbContext db, CredentialProtector credentials,
    IVerificationEmail email, IConfiguration configuration, TimeProvider clock)
{
    public string HashCode(Guid id, string code) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(configuration["Auth:VerificationHashKey"]!), Encoding.UTF8.GetBytes($"{id:D}:{code}")));

    public VerificationResponse Challenge(UserAccount user)
    {
        var at = user.Email.IndexOf('@');
        var masked = user.Email[..1] + "***" + user.Email[at..];
        var remaining = user.CodeSentAt is { } sent
            ? Math.Max(0, (int)Math.Ceiling((sent.AddSeconds(60) - clock.GetUtcNow().UtcDateTime).TotalSeconds)) : 0;
        return new(credentials.IssueVerification(user.Id), masked, remaining);
    }

    public async Task<VerificationResponse> Send(UserAccount user, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now.AddSeconds(-60);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var hash = HashCode(user.Id, code);
        var changed = await db.Users.Where(u => u.Id == user.Id && u.EmailVerifiedAt == null &&
            (u.VerificationSentAt == null || u.VerificationSentAt <= cutoff)).ExecuteUpdateAsync(set => set
                .SetProperty(u => u.VerificationCodeHash, hash)
                .SetProperty(u => u.VerificationExpiresAt, now.AddMinutes(10))
                .SetProperty(u => u.VerificationSentAt, now)
                .SetProperty(u => u.VerificationAttempts, 0), ct);
        if (changed != 1)
        {
            var current = await db.Accounts(user.Id).SingleOrDefaultAsync(ct) ?? user;
            var challenge = Challenge(current);
            throw new AuthProblem(429, new("resend_wait", "Tunggu sebelum mengirim kode lagi.",
                challenge.VerificationTicket, challenge.Email, challenge.ResendAfter));
        }
        var response = Challenge(user with { CodeSentAt = now });
        try { await email.Send(user.Email, code, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AuthProblem(503, new("email_delivery_failed", "Email belum terkirim. Coba kirim ulang kode.",
                response.VerificationTicket, response.Email, response.ResendAfter));
        }
        return response;
    }
}
