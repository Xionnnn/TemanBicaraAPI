using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TemanBicaraAPI.Contracts.RequestModels.Auth;

namespace TemanBicaraAPI.MobileAPI.Controllers.Auth;

[ApiController]
[Route("auth")]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) =>
        Accepted(await mediator.Send(request, ct));

    [HttpPost("verify-email"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Verify(VerifyRequest request, CancellationToken ct)
    {
        await mediator.Send(request, ct);
        return NoContent();
    }

    [HttpPost("resend-verification"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Resend(ResendRequest request, CancellationToken ct) => Ok(await mediator.Send(request, ct));

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) => Ok(await mediator.Send(request, ct));

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me(CancellationToken ct) => Ok(await mediator.Send(
        new GetCurrentAccountRequest(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)), ct));

    [HttpPost("forgot-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct) =>
        Accepted(await mediator.Send(request, ct));

    [HttpPost("verify-password-reset"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyPasswordReset(VerifyPasswordResetRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(request, ct));

    [HttpPost("reset-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await mediator.Send(request, ct);
        return NoContent();
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await mediator.Send(new LogoutRequest(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            long.Parse(User.FindFirstValue("auth_version")!, System.Globalization.CultureInfo.InvariantCulture)), ct);
        return NoContent();
    }
}
