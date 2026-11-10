using FluentValidation;
using TemanBicaraAPI.Contracts.RequestModels.Auth;

namespace TemanBicaraAPI.Validators.Auth;

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordValidator()
    {
        RuleFor(value => value.Email).NotEmpty().MaximumLength(254).Must(value =>
            new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(value));
    }
}

public sealed class VerifyPasswordResetValidator : AbstractValidator<VerifyPasswordResetRequest>
{
    public VerifyPasswordResetValidator()
    {
        RuleFor(value => value.RecoveryTicket).NotEmpty().Must(value => Guid.TryParse(value, out var id) && id != Guid.Empty);
        RuleFor(value => value.Code).NotEmpty().Matches("^[0-9]{6}$");
    }
}

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(value => value.ResetToken).NotEmpty().Matches("^[A-F0-9]{64}$");
        RuleFor(value => value.Password).NotEmpty().MaximumLength(128);
        RuleFor(value => value.ConfirmPassword).NotEmpty().Equal(value => value.Password)
            .WithErrorCode("password_mismatch").WithMessage("Konfirmasi password tidak sama.");
    }
}
