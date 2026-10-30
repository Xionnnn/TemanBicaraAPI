using FluentValidation;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
namespace TemanBicaraAPI.Validators.Auth;
public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(r => r.Username).NotEmpty().MaximumLength(32);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
    }
}
