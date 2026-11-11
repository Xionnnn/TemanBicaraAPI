using FluentValidation;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
namespace TemanBicaraAPI.Validators.Auth;
public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(r => r.Username).NotEmpty().Matches(@"^[A-Za-z0-9_]{3,32}$");
        RuleFor(r => r.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(r => r.ConfirmPassword).NotEmpty();
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).Must(value =>
            new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(value));
        RuleFor(r => r.Role).NotEmpty().Must(role => role is "Teacher" or "Student");
        RuleFor(r => r.ConfirmPassword).Equal(r => r.Password).WithErrorCode("password_mismatch")
            .WithMessage("Konfirmasi password tidak sama.");
    }
}
