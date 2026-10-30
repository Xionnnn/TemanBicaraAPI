using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.Commons.Authentication;

public sealed class CredentialAuthentication(TemanBicaraDbContext db, CredentialProtector credentials)
{
    public async Task<(UserAccount User, AuthTicket Ticket)> Authenticate(string value, CancellationToken ct)
    {
        var ticket = credentials.Read(value);
        var user = await db.Accounts(ticket.UserId).SingleOrDefaultAsync(ct);
        if (user is null || user.VerifiedAt is null || user.AuthVersion != ticket.Version ||
            ticket.PasswordStamp != CredentialProtector.PasswordStamp(user.PasswordHash))
            throw new AuthProblem(401, new("invalid_credential", "Silakan masuk kembali untuk mengakses layanan online."));
        return (user, ticket);
    }
}
