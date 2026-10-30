using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.ResponseModels.Common;
namespace TemanBicaraAPI.MobileAPI.Authentication;

public sealed class BearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, CredentialAuthentication auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PersistentBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var value = header[7..].Trim();
        if (value.Length is 0 or > 4096) return AuthenticateResult.Fail("Invalid credential.");
        try
        {
            var (user, ticket) = await auth.Authenticate(value, Context.RequestAborted);
            var identity = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("auth_version", ticket.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            }, SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }
        catch (AuthProblem) { return AuthenticateResult.Fail("Invalid credential."); }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Response.WriteAsJsonAsync(new ApiError("invalid_credential", "Silakan masuk kembali untuk mengakses layanan online."));
    }
}
