namespace TemanBicaraAPI.Contracts.ResponseModels.Auth;
public sealed record VerificationResponse(string VerificationTicket, string Email, int ResendAfter);
