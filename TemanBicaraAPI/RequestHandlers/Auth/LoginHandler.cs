using MediatR;
using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.RequestHandlers.Auth;

public sealed class LoginHandler(TemanBicaraDbContext db,
    Microsoft.AspNetCore.Identity.IPasswordHasher<UserAccount> hasher, CredentialProtector credentials,
    VerificationCodes codes) : IRequestHandler<LoginRequest, LoginResponse>
{
    private static readonly string DummyHash = new Microsoft.AspNetCore.Identity.PasswordHasher<UserAccount>()
        .HashPassword(null!, "timing-only-password");

    public async Task<LoginResponse> Handle(LoginRequest request, CancellationToken ct)
    {
        var name = request.Username.Trim().ToLowerInvariant();
        var id = await db.Users.AsNoTracking().Where(u => u.UserName.Trim().ToLower() == name)
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
        var user = id is null ? null : await db.Accounts(id).SingleOrDefaultAsync(ct);
        // Unknown accounts still perform password verification to reduce timing differences.
        var candidate = user ?? new UserAccount(Guid.Empty, "", "", DummyHash, "", 0, null, null);
        var result = hasher.VerifyHashedPassword(candidate, candidate.PasswordHash, request.Password);
        if (user is null || result == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
            throw new AuthProblem(401, new("invalid_login", "Username atau password salah."));
        if (user.VerifiedAt is null)
        {
            var challenge = codes.Challenge(user);
            throw new AuthProblem(403, new("verification_required", "Verifikasi email Anda terlebih dahulu.",
                challenge.VerificationTicket, challenge.Email, challenge.ResendAfter));
        }
        return new(credentials.Issue(user), AccountQueries.ToDto(user));
    }
}
