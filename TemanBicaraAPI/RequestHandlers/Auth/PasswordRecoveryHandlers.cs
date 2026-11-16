using MediatR;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;

namespace TemanBicaraAPI.RequestHandlers.Auth;

public sealed class ForgotPasswordHandler(PasswordRecovery recovery) : IRequestHandler<ForgotPasswordRequest, PasswordRecoveryResponse>
{
    public Task<PasswordRecoveryResponse> Handle(ForgotPasswordRequest request, CancellationToken ct) => recovery.Request(request.Email, ct);
}

public sealed class VerifyPasswordResetHandler(PasswordRecovery recovery) : IRequestHandler<VerifyPasswordResetRequest, PasswordResetGrant>
{
    public Task<PasswordResetGrant> Handle(VerifyPasswordResetRequest request, CancellationToken ct) => recovery.Verify(request, ct);
}

public sealed class ResetPasswordHandler(PasswordRecovery recovery) : IRequestHandler<ResetPasswordRequest>
{
    public Task Handle(ResetPasswordRequest request, CancellationToken ct) => recovery.Reset(request, ct);
}
