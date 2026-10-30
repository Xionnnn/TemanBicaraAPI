using MediatR;
using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.RequestHandlers.Auth;

public sealed class RegisterHandler(TemanBicaraDbContext db,
    Microsoft.AspNetCore.Identity.IPasswordHasher<UserAccount> hasher, VerificationCodes codes)
    : IRequestHandler<RegisterRequest, VerificationResponse>
{
    public async Task<VerificationResponse> Handle(RegisterRequest request, CancellationToken ct)
    {
        var roleId = await db.Roles.Where(r => r.RoleName == request.Role).Select(r => r.Id).SingleAsync(ct);
        var account = new UserAccount(Guid.NewGuid(), request.Username.Trim(), request.Email.Trim().ToLowerInvariant(),
            "", request.Role, 0, null, null);
        account = account with { PasswordHash = hasher.HashPassword(account, request.Password) };
        var entity = new User { Id = account.Id, UserName = account.Username, Email = account.Email,
            Password = account.PasswordHash, Rolesid = roleId };
        db.Users.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.Entry(entity).State = EntityState.Detached;
            throw new AuthProblem(409, new("account_exists", "Username atau email sudah digunakan."));
        }
        return await codes.Send(account, ct);
    }
}
