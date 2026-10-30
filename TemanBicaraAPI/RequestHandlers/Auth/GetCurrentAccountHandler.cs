using MediatR;
using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.RequestHandlers.Auth;

public sealed class GetCurrentAccountHandler(TemanBicaraDbContext db)
    : IRequestHandler<GetCurrentAccountRequest, TemanBicaraAPI.Contracts.DTOs.Auth.AccountDto>
{
    public async Task<TemanBicaraAPI.Contracts.DTOs.Auth.AccountDto> Handle(GetCurrentAccountRequest request, CancellationToken ct)
    {
        var user = await db.Accounts(request.UserId).SingleOrDefaultAsync(ct)
            ?? throw new AuthProblem(401, new("invalid_credential", "Silakan masuk kembali untuk mengakses layanan online."));
        return AccountQueries.ToDto(user);
    }
}
