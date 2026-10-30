using MediatR;
using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.RequestHandlers.Auth;

public sealed class VerifyHandler(TemanBicaraDbContext db, CredentialProtector credentials,
    VerificationCodes codes, TimeProvider clock) : IRequestHandler<VerifyRequest>
{
    public async Task Handle(VerifyRequest request, CancellationToken ct)
    {
        var id = credentials.ReadVerification(request.VerificationTicket);
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = codes.HashCode(id, request.Code);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize verification with resend and persist failed-attempt increments before returning an error.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM public.users WHERE id={id} FOR UPDATE")
            .AsTracking().SingleOrDefaultAsync(ct);
        if (user is null || user.EmailVerifiedAt is not null || user.VerificationCodeHash is null ||
            user.VerificationExpiresAt is null || user.VerificationExpiresAt <= now || user.VerificationAttempts >= 5)
            throw InvalidCode();
        var matches = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(user.VerificationCodeHash), System.Text.Encoding.UTF8.GetBytes(hash));
        if (matches)
        {
            user.EmailVerifiedAt = now;
            user.VerificationCodeHash = null;
            user.VerificationExpiresAt = null;
            user.VerificationSentAt = null;
            user.VerificationAttempts = 0;
        }
        else user.VerificationAttempts++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (!matches) throw InvalidCode();
    }

    private static AuthProblem InvalidCode() => new(400, new("invalid_code",
        "Kode salah, kedaluwarsa, atau sudah digunakan. Kirim ulang jika diperlukan."));
}
