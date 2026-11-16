namespace TemanBicaraAPI.Contracts.ResponseModels.Common;
public sealed record ApiError(string Code, string Message, string? VerificationTicket = null,
    string? Email = null, int? ResendAfter = null, string? RecoveryTicket = null);
