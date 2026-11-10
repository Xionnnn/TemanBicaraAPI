namespace TemanBicaraAPI.Contracts.ResponseModels.Auth;

public sealed record PasswordRecoveryResponse(string RecoveryTicket, string Email, int ResendAfter);
public sealed record PasswordResetGrant(string ResetToken, int ExpiresIn);
