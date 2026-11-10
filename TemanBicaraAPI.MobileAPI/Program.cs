using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using TemanBicaraAPI.Entities;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Services;
using TemanBicaraAPI.Commons.Behaviors;
using TemanBicaraAPI.Contracts.ResponseModels.Common;
using TemanBicaraAPI.MobileAPI.Authentication;
using TemanBicaraAPI.MobileAPI.Middleware;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("ColdDb")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:ColdDb in appsettings.json, user secrets, or environment variables.");
var key = builder.Configuration["Auth:VerificationHashKey"];
if (string.IsNullOrWhiteSpace(key) || Convert.FromBase64String(key).Length < 32)
    throw new InvalidOperationException("Auth:VerificationHashKey must be at least 32 random bytes encoded as Base64.");
var keyRing = builder.Configuration["Auth:KeyRingPath"]
    ?? throw new InvalidOperationException("Set Auth:KeyRingPath to persistent private server storage.");

var protection = builder.Services.AddDataProtection().SetApplicationName("TemanBicara.ColdApi")
    .PersistKeysToFileSystem(new DirectoryInfo(keyRing));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
else if (!builder.Environment.IsDevelopment())
{
    var certificatePath = builder.Configuration["Auth:KeyCertificatePath"]
        ?? throw new InvalidOperationException("Set Auth:KeyCertificatePath to encrypt production keys at rest.");
    protection.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader
        .LoadPkcs12FromFile(certificatePath, builder.Configuration["Auth:KeyCertificatePassword"]));
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<TemanBicaraDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<IVerificationEmail, VerificationEmail>();
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddSingleton<CredentialProtector>();
builder.Services.AddScoped<CredentialAuthentication>();
builder.Services.AddScoped<VerificationCodes>();
builder.Services.AddScoped<PasswordRecovery>();
builder.Services.AddAuthentication(BearerHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BearerHandler>(BearerHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Auth:RequestsPerMinute", 10),
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ApiError("rate_limited", "Terlalu banyak percobaan. Tunggu satu menit."), ct);
    };
});
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
        new { code = "invalid_input", message = "Periksa kembali data yang Anda masukkan.",
            errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray()) });
});
builder.Services.AddOpenApi();

builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(TemanBicaraAPI.AssemblyMarker).Assembly);
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(TemanBicaraAPI.AssemblyMarker).Assembly);

var app = builder.Build();
app.UseAuthErrors();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
