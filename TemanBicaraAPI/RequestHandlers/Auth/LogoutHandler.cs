using MediatR;
using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.RequestHandlers.Auth;

public sealed class LogoutHandler(TemanBicaraDbContext db) : IRequestHandler<LogoutRequest>
{
    public async Task Handle(LogoutRequest request, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == request.UserId && u.AuthVersion == request.AuthVersion)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1), ct);
}
