using FluentValidation;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
namespace TemanBicaraAPI.Validators.Auth;
public sealed class ResendValidator : AbstractValidator<ResendRequest>
{
    public ResendValidator()
    {
        RuleFor(r => r.VerificationTicket).NotEmpty().MaximumLength(4096);
    }
}
