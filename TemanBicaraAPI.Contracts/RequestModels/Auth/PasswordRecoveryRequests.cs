using MediatR;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;

namespace TemanBicaraAPI.Contracts.RequestModels.Auth;

public sealed record ForgotPasswordRequest(string Email) : IRequest<PasswordRecoveryResponse>;
public sealed record VerifyPasswordResetRequest(string RecoveryTicket, string Code) : IRequest<PasswordResetGrant>;
public sealed record ResetPasswordRequest(string ResetToken, string Password, string ConfirmPassword) : IRequest;
