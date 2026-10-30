namespace TemanBicaraAPI.Commons.Authentication;
public sealed record UserAccount(Guid Id, string Username, string Email, string PasswordHash,
    string Role, long AuthVersion, DateTime? VerifiedAt, DateTime? CodeSentAt);
public sealed record AuthTicket(Guid UserId, long Version, string PasswordStamp, string Nonce);
public sealed record VerificationTicket(Guid UserId, DateTimeOffset ExpiresAt);
