using Microsoft.EntityFrameworkCore;
using TemanBicaraAPI.Contracts.DTOs.Auth;
using TemanBicaraAPI.Entities;

namespace TemanBicaraAPI.Commons.Authentication;

public static class AccountQueries
{
    public static IQueryable<UserAccount> Accounts(this TemanBicaraDbContext db, Guid? id = null) =>
        from user in db.Users.AsNoTracking().Where(u => id == null || u.Id == id)
        join role in db.Roles.AsNoTracking() on user.Rolesid equals role.Id
        select new UserAccount(user.Id, user.UserName, user.Email, user.Password, role.RoleName,
            user.AuthVersion, user.EmailVerifiedAt, user.VerificationSentAt);

    public static AccountDto ToDto(UserAccount user) => new(user.Id, user.Username, user.Role);
}
