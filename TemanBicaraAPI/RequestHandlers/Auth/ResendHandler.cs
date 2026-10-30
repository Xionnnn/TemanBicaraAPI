using MediatR;
using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.RequestHandlers.Auth;

public sealed class ResendHandler(TemanBicaraDbContext db, CredentialProtector credentials, VerificationCodes codes)
    : IRequestHandler<ResendRequest, VerificationResponse>
{
    public async Task<VerificationResponse> Handle(ResendRequest request, CancellationToken ct)
    {
        var id = credentials.ReadVerification(request.VerificationTicket);
        var user = await db.Accounts(id).SingleOrDefaultAsync(ct);
        if (user is null || user.VerifiedAt is not null)
            throw new AuthProblem(400, new("verification_unavailable", "Verifikasi tidak tersedia. Silakan masuk."));
        return await codes.Send(user, ct);
    }
}
