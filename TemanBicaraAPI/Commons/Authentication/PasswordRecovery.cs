using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Commons.Services;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.Commons.Authentication;

public sealed class PasswordRecovery(TemanBicaraDbContext db, IVerificationEmail email,
    IPasswordHasher<UserAccount> hasher, IConfiguration configuration, TimeProvider clock, ILogger<PasswordRecovery> logger)
{
    public async Task<PasswordRecoveryResponse> Request(string emailAddress, CancellationToken ct)
    {
        var normalized = emailAddress.Trim().ToLowerInvariant();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        PasswordRecoveryResponse response;
        string recipient;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM public.users WHERE lower(btrim(email))={normalized} FOR UPDATE")
                .AsTracking().SingleOrDefaultAsync(ct);
            if (user is null) throw new AuthProblem(404, new("email_not_found", "Email tidak terdaftar pada akun mana pun."));
            var now = clock.GetUtcNow().UtcDateTime;
            var challenge = await db.PasswordResetChallenges.SingleOrDefaultAsync(value => value.UserId == user.Id, ct);
            var remaining = challenge is null ? 0 : Math.Max(0, (int)Math.Ceiling((challenge.SentAt.AddSeconds(60) - now).TotalSeconds));
            var masked = MaskEmail(user.Email);
            if (remaining > 0)
                throw new AuthProblem(429, new("resend_wait", "Tunggu sebelum mengirim kode lagi.",
                    Email: masked, ResendAfter: remaining, RecoveryTicket: challenge!.Id.ToString("D")));
            if (challenge is null)
            {
                challenge = new PasswordResetChallenge { UserId = user.Id };
                db.PasswordResetChallenges.Add(challenge);
            }
            challenge.Id = Guid.NewGuid();
            challenge.CodeHash = HashCode(user.Id, challenge.Id, code);
            challenge.SentAt = now;
            challenge.CodeExpiresAt = now.AddMinutes(10);
            challenge.Attempts = 0;
            challenge.PasswordStamp = CredentialProtector.PasswordStamp(user.Password);
            challenge.VerifiedAt = null;
            challenge.ResetTokenHash = null;
            challenge.ResetExpiresAt = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            response = new(challenge.Id.ToString("D"), masked, 60);
            recipient = user.Email;
        }
        try { await email.SendPasswordReset(recipient, code, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var smtp = ex as SmtpCommandException;
            logger.LogWarning("Password recovery email failed. ExceptionType={ExceptionType}; SmtpStatus={SmtpStatus}; SmtpError={SmtpError}",
                ex.GetType().Name, smtp is null ? null : (int?)smtp.StatusCode, smtp?.ErrorCode.ToString());
            throw new AuthProblem(503, new("email_delivery_failed", "Email belum terkirim. Coba kirim ulang kode.",
                Email: response.Email, ResendAfter: response.ResendAfter, RecoveryTicket: response.RecoveryTicket));
        }
        return response;
    }

    public async Task<PasswordResetGrant> Verify(VerifyPasswordResetRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.RecoveryTicket, out var id)) throw InvalidCode();
        var userId = await db.PasswordResetChallenges.AsNoTracking().Where(value => value.Id == id)
            .Select(value => (Guid?)value.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) throw InvalidCode();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(userId.Value, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var challenge = await db.PasswordResetChallenges.SingleOrDefaultAsync(value => value.UserId == userId, ct);
        if (user is null || challenge is null || challenge.Id != id || challenge.CodeHash is null ||
            challenge.VerifiedAt is not null || challenge.CodeExpiresAt <= now || challenge.Attempts >= 5 ||
            challenge.PasswordStamp != CredentialProtector.PasswordStamp(user.Password)) throw InvalidCode();
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge.CodeHash),
            Encoding.ASCII.GetBytes(HashCode(user.Id, id, request.Code)));
        string? token = null;
        if (matches)
        {
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            challenge.CodeHash = null;
            challenge.VerifiedAt = now;
            challenge.ResetTokenHash = HashToken(token);
            challenge.ResetExpiresAt = now.AddMinutes(10);
        }
        else challenge.Attempts++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (!matches) throw InvalidCode();
        return new(token!, 600);
    }

    public async Task Reset(ResetPasswordRequest request, CancellationToken ct)
    {
        var tokenHash = HashToken(request.ResetToken);
        var userId = await db.PasswordResetChallenges.AsNoTracking().Where(value => value.ResetTokenHash == tokenHash)
            .Select(value => (Guid?)value.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) throw InvalidGrant();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(userId.Value, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var challenge = await db.PasswordResetChallenges.SingleOrDefaultAsync(value => value.UserId == userId, ct);
        if (user is null || challenge is null || challenge.VerifiedAt is null || challenge.ResetTokenHash != tokenHash ||
            challenge.ResetExpiresAt is null || challenge.ResetExpiresAt <= now ||
            challenge.PasswordStamp != CredentialProtector.PasswordStamp(user.Password)) throw InvalidGrant();
        var account = await db.Accounts(user.Id).SingleAsync(ct);
        user.Password = hasher.HashPassword(account, request.Password);
        user.AuthVersion++;
        user.UpdatedAt = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);
        db.PasswordResetChallenges.Remove(challenge);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private Task<User?> LockUser(Guid id, CancellationToken ct) =>
        db.Users.FromSqlInterpolated($"SELECT * FROM public.users WHERE id={id} FOR UPDATE").AsTracking().SingleOrDefaultAsync(ct);

    private string HashCode(Guid userId, Guid challengeId, string code) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(configuration["Auth:VerificationHashKey"]!),
        Encoding.UTF8.GetBytes($"password-reset:{userId:D}:{challengeId:D}:{code}")));

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string MaskEmail(string value) => value[..1] + "***" + value[value.IndexOf('@')..];
    private static AuthProblem InvalidCode() => new(400, new("invalid_reset_code",
        "Kode salah, kedaluwarsa, atau sudah digunakan. Kirim ulang kode jika diperlukan."));
    private static AuthProblem InvalidGrant() => new(400, new("reset_expired",
        "Izin ganti password kedaluwarsa atau sudah digunakan. Minta kode baru."));
}
