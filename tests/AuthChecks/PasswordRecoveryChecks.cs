using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.Entities;

static class PasswordRecoveryChecks
{
    public static async Task<int> Run(HandlerClient service, TemanBicaraDbContext db, CapturedEmail email,
        TestClock clock, IPasswordHasher<UserAccount> hasher, CredentialProtector protector, IConfiguration config)
    {
        var assertions = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); Interlocked.Increment(ref assertions); }
        async Task Reject(int status, Func<Task> action)
        {
            try { await action(); throw new Exception("Expected password recovery rejection " + status); }
            catch (AuthProblem problem) { Check(problem.Status == status, problem.Error.Code); }
            catch (ValidationException) { Check(status == 400, "Recovery input validation"); }
        }

        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "TemanBicaraAPI.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Recovery migration source was not found.");
        var before = await db.Users.AsNoTracking().OrderBy(user => user.Id).Select(user => new { user.Id, user.Password, user.AuthVersion }).ToListAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE public.password_reset_challenges");
        var migration = await File.ReadAllTextAsync(Path.Combine(repository.FullName, "TemanBicaraAPI.MobileAPI", "Database", "002-password-recovery.sql"));
        await db.Database.ExecuteSqlRawAsync(migration);
        await db.Database.ExecuteSqlRawAsync(migration);
        var after = await db.Users.AsNoTracking().OrderBy(user => user.Id).Select(user => new { user.Id, user.Password, user.AuthVersion }).ToListAsync();
        Check(before.SequenceEqual(after), "Recovery migration changed existing accounts");
        Check(await db.Database.SqlQueryRaw<bool>("SELECT relrowsecurity AS \"Value\" FROM pg_class WHERE oid = 'public.password_reset_challenges'::regclass").SingleAsync(),
            "Recovery table does not enable row-level security");

        const string oldPassword = "original recovery password";
        const string newPassword = "replacement recovery password";
        var registration = await service.Register(new("recovery_user", oldPassword, oldPassword, "recovery@example.test", "Student"), default);
        await service.Verify(new(registration.VerificationTicket, email.LastCode!), default);
        var oldLogin = await service.Login(new("recovery_user", oldPassword), default);
        var accountId = oldLogin.Account.Id;
        var lastCode = email.LastCode;
        await Reject(404, () => service.ForgotPassword("unknown@example.test"));
        Check(email.LastCode == lastCode, "Unknown email triggered delivery");
        await Reject(400, () => service.ForgotPassword("not-an-email"));
        var challenge = await service.ForgotPassword("  RECOVERY@EXAMPLE.TEST  ");
        var code = email.LastCode!;
        Check(code.Length == 6 && challenge.ResendAfter == 60, "Recovery code or cooldown missing");
        var row = await db.PasswordResetChallenges.AsNoTracking().SingleAsync(value => value.UserId == accountId);
        Check(row.CodeHash != code && row.CodeHash!.Length == 64, "Recovery code was stored in plaintext");
        await Reject(429, () => service.ForgotPassword("recovery@example.test"));
        await Reject(400, () => service.VerifyPasswordReset(challenge.RecoveryTicket, "12"));
        await Reject(400, () => service.VerifyPasswordReset(challenge.RecoveryTicket, code == "000000" ? "000001" : "000000"));
        var grant = await service.VerifyPasswordReset(challenge.RecoveryTicket, code);
        Check(grant.ExpiresIn == 600 && grant.ResetToken.Length == 64, "Reset grant lifetime missing");
        row = await db.PasswordResetChallenges.AsNoTracking().SingleAsync(value => value.UserId == accountId);
        Check(row.CodeHash is null && row.ResetTokenHash != grant.ResetToken, "Verified code or raw reset token remained stored");
        await Reject(400, () => service.VerifyPasswordReset(challenge.RecoveryTicket, code));
        await Reject(400, () => service.ResetPassword(challenge.RecoveryTicket, newPassword, newPassword));
        await Reject(400, () => service.ResetPassword(grant.ResetToken, newPassword, "mismatch"));
        await service.ResetPassword(grant.ResetToken, newPassword, newPassword);
        await Reject(400, () => service.ResetPassword(grant.ResetToken, newPassword, newPassword));
        await Reject(401, () => service.Login(new("recovery_user", oldPassword), default));
        await Reject(401, () => service.Authenticate(oldLogin.Credential, default));
        var newLogin = await service.Login(new("recovery_user", newPassword), default);
        Check(newLogin.Account.Id == accountId && newLogin.Account.Role == "Student", "Recovery changed account identity or role");
        Check(!await db.PasswordResetChallenges.AnyAsync(value => value.UserId == accountId), "Consumed grant was not removed");

        challenge = await service.ForgotPassword("recovery@example.test");
        var oldCode = email.LastCode!;
        clock.Advance(TimeSpan.FromSeconds(61));
        var resent = await service.ForgotPassword("recovery@example.test");
        Check(challenge.RecoveryTicket != resent.RecoveryTicket, "Resend kept the old challenge");
        await Reject(400, () => service.VerifyPasswordReset(challenge.RecoveryTicket, oldCode));
        code = email.LastCode!;
        await using (var restarted = new HandlerClient(db, hasher, protector, email, config, clock))
            grant = await restarted.VerifyPasswordReset(resent.RecoveryTicket, code);
        Check(grant.ResetToken.Length == 64, "Recovery challenge did not survive a new service instance");
        clock.Advance(TimeSpan.FromMinutes(11));
        await Reject(400, () => service.ResetPassword(grant.ResetToken, newPassword, newPassword));

        challenge = await service.ForgotPassword("recovery@example.test");
        code = email.LastCode!;
        clock.Advance(TimeSpan.FromMinutes(11));
        await Reject(400, () => service.VerifyPasswordReset(challenge.RecoveryTicket, code));
        challenge = await service.ForgotPassword("recovery@example.test");
        code = email.LastCode!;
        var wrongCode = code == "000000" ? "000001" : "000000";
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Reject(400, () => service.VerifyPasswordReset(challenge.RecoveryTicket, wrongCode))));
        Check(await db.PasswordResetChallenges.Where(value => value.UserId == accountId).Select(value => value.Attempts).SingleAsync() == 5,
            "Concurrent wrong-code attempts were lost");
        await Reject(400, () => service.VerifyPasswordReset(challenge.RecoveryTicket, code));

        clock.Advance(TimeSpan.FromSeconds(61));
        challenge = await service.ForgotPassword("recovery@example.test");
        code = email.LastCode!;
        async Task<PasswordResetGrant?> TryVerify()
        {
            try { return await service.VerifyPasswordReset(challenge.RecoveryTicket, code); }
            catch (AuthProblem problem) when (problem.Status == 400) { return null; }
        }
        var verified = await Task.WhenAll(TryVerify(), TryVerify());
        Check(verified.Count(value => value is not null) == 1, "A code issued multiple grants concurrently");
        grant = verified.Single(value => value is not null)!;
        async Task<bool> TryReset(string password)
        {
            try { await service.ResetPassword(grant.ResetToken, password, password); return true; }
            catch (AuthProblem problem) when (problem.Status == 400) { return false; }
        }
        var reset = await Task.WhenAll(TryReset("concurrent recovery password one"), TryReset("concurrent recovery password two"));
        Check(reset.Count(value => value) == 1, "A reset grant was consumed twice concurrently");

        challenge = await service.ForgotPassword("recovery@example.test");
        grant = await service.VerifyPasswordReset(challenge.RecoveryTicket, email.LastCode!);
        clock.Advance(TimeSpan.FromSeconds(61));
        async Task<PasswordRecoveryResponse?> TryResend()
        {
            try { return await service.ForgotPassword("recovery@example.test"); }
            catch (AuthProblem problem) when (problem.Status == 429) { return null; }
        }
        var sends = await Task.WhenAll(TryResend(), TryResend());
        Check(sends.Count(value => value is not null) == 1, "Concurrent resend bypassed cooldown");
        await Reject(400, () => service.ResetPassword(grant.ResetToken, newPassword, newPassword));

        var pendingBefore = await db.Users.AsNoTracking().Where(user => user.UserName == "pending")
            .Select(user => new { user.VerificationCodeHash, user.VerificationAttempts, user.EmailVerifiedAt }).SingleAsync();
        var pendingChallenge = await service.ForgotPassword("pending@example.test");
        var pendingGrant = await service.VerifyPasswordReset(pendingChallenge.RecoveryTicket, email.LastCode!);
        await service.ResetPassword(pendingGrant.ResetToken, newPassword, newPassword);
        var pendingAfter = await db.Users.AsNoTracking().Where(user => user.UserName == "pending")
            .Select(user => new { user.VerificationCodeHash, user.VerificationAttempts, user.EmailVerifiedAt }).SingleAsync();
        Check(pendingBefore.Equals(pendingAfter), "Recovery changed registration verification state");

        clock.Advance(TimeSpan.FromSeconds(61));
        email.Fail = true;
        try
        {
            await service.ForgotPassword("recovery@example.test");
            throw new Exception("Expected recovery SMTP failure");
        }
        catch (AuthProblem problem)
        {
            Check(problem.Status == 503 && problem.Error.RecoveryTicket is not null && problem.Error.ResendAfter == 60,
                "SMTP failure lost the recovery continuation");
        }
        finally { email.Fail = false; }
        await Reject(429, () => service.ForgotPassword("recovery@example.test"));
        clock.Advance(TimeSpan.FromSeconds(61));
        challenge = await service.ForgotPassword("recovery@example.test");
        Check(challenge.RecoveryTicket.Length > 0, "Recovery did not resume after SMTP failure");
        return assertions;
    }
}
