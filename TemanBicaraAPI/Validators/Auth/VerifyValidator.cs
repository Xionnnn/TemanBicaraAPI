using FluentValidation;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
namespace TemanBicaraAPI.Validators.Auth;
public sealed class VerifyValidator : AbstractValidator<VerifyRequest>
{
    public VerifyValidator()
    {
        RuleFor(r => r.VerificationTicket).NotEmpty().MaximumLength(4096);
        RuleFor(r => r.Code).NotEmpty().Matches(@"^\d{6}$");
    }
}
