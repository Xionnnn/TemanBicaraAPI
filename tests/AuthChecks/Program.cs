using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using TemanBicaraAPI.Commons.Authentication;
using TemanBicaraAPI.Commons.Services;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.DTOs.Auth;
using TemanBicaraAPI.Contracts.RequestModels.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;
using TemanBicaraAPI.MobileAPI.Authentication;
using TemanBicaraAPI.MobileAPI.Middleware;
using TemanBicaraAPI.Entities;
using TemanBicaraAPI.Commons.Behaviors;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TemanBicaraAPI.MobileAPI.Controllers.Auth;

using System.Net.Sockets;
using System.Text;

if (args.Contains("--live-schema"))
{
    var schemaRepository = new DirectoryInfo(AppContext.BaseDirectory);
    while (schemaRepository is not null && !File.Exists(Path.Combine(schemaRepository.FullName, "TemanBicaraAPI.slnx")))
        schemaRepository = schemaRepository.Parent;
    if (schemaRepository is null) throw new InvalidOperationException("Run the schema check from the repository build.");
    var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Path.Combine(schemaRepository.FullName, "TemanBicaraAPI.MobileAPI"))
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{environment}.json", optional: true);
    if (environment == "Development") configuration.AddUserSecrets("TemanBicaraAPI-LocalDevelopment");
    var liveConfig = configuration.AddEnvironmentVariables()
        .AddCommandLine(args.Where(a => a != "--live-schema").ToArray()).Build();
    using var configurationLifetime = liveConfig as IDisposable;
    var liveConnection = liveConfig.GetConnectionString("ColdDb")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:ColdDb for the read-only schema check.");
    await using var liveDb = new TemanBicaraDbContext(new DbContextOptionsBuilder<TemanBicaraDbContext>()
        .UseNpgsql(liveConnection).Options);
    await liveDb.Assets.Select(a => a.Id).Take(1).ToListAsync();
    await liveDb.ClassRooms.Select(c => c.Id).Take(1).ToListAsync();
    await liveDb.ClassRoomsStudents.Select(c => c.Studentid).Take(1).ToListAsync();
    await liveDb.ClassRoomsTeachers.Select(c => c.Teachersid).Take(1).ToListAsync();
    await liveDb.EventLogs.Select(e => e.Id).Take(1).ToListAsync();
    await liveDb.Roles.Select(r => r.Id).Take(1).ToListAsync();
    await liveDb.Sessions.Select(s => s.Id).Take(1).ToListAsync();
    await liveDb.Users.Select(u => u.Id).Take(1).ToListAsync();
    await liveDb.Users.Select(u => new { u.AuthVersion, u.EmailVerifiedAt, u.VerificationAttempts,
        u.VerificationExpiresAt, u.VerificationSentAt, u.VerificationCodeHash }).Take(1).ToListAsync();
    await liveDb.PasswordResetChallenges.Select(challenge => challenge.Id).Take(1).ToListAsync();
    Console.WriteLine("PASS: read-only EF mapping checks against all eight original public tables, authentication columns, and password recovery storage.");
    return;
}

var clock = new TestClock();
var testConnection = Environment.GetEnvironmentVariable("TEMANBICARA_TEST_DB")
    ?? throw new InvalidOperationException("Set TEMANBICARA_TEST_DB to a dedicated local PostgreSQL test database.");
var connectionCheck = new Npgsql.NpgsqlConnectionStringBuilder(testConnection);
if (connectionCheck.Host is not ("localhost" or "127.0.0.1") || connectionCheck.Database != "temanbicara_auth_tests")
    throw new InvalidOperationException("Tests require a loopback database named temanbicara_auth_tests.");
var options = new DbContextOptionsBuilder<TemanBicaraDbContext>().UseNpgsql(testConnection).Options;
await using var db = new TestDatabase(options);
await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();
await db.Database.ExecuteSqlRawAsync("""
    CREATE UNIQUE INDEX users_username_normalized_key ON public.users (lower(btrim(user_name)));
    CREATE UNIQUE INDEX users_email_normalized_key ON public.users (lower(btrim(email)));
    ALTER TABLE public.users ADD CONSTRAINT users_password_not_empty CHECK(length(password)>0);
    ALTER TABLE public.users ADD CONSTRAINT users_auth_version_nonnegative CHECK(auth_version>=0);
    ALTER TABLE public.users ADD CONSTRAINT users_verification_attempts_range CHECK(verification_attempts BETWEEN 0 AND 5);
    """);
db.Roles.AddRange(new Role { Id = 1, RoleName = "Teacher" }, new Role { Id = 2, RoleName = "Student" });
await db.SaveChangesAsync();
db.ChangeTracker.Clear();
var repository = new DatabaseUsers(db);
var email = new CapturedEmail();
var keyRing = Path.Combine(Path.GetTempPath(), "temanbicara-auth-checks-" + Guid.NewGuid());
var protector = new CredentialProtector(DataProtectionProvider.Create(new DirectoryInfo(keyRing)), clock);
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["Auth:VerificationHashKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
}).Build();
var hasher = new PasswordHasher<UserAccount>();
await using var service = new HandlerClient(db, hasher, protector, email, config, clock);
const string password = "a long test passphrase";
var assertions = 0;
void Check(bool result, string message) { if (!result) throw new Exception(message); assertions++; }
async Task Reject(int status, Func<Task> action) {
    try { await action(); throw new Exception("Expected rejection " + status); }
    catch (AuthProblem problem) { Check(problem.Status == status, problem.Error.Code); }
    catch (ValidationException) { Check(status == 400, "Validation rejection status"); }
}

foreach (var role in new[] { "Teacher", "Student" })
{
    var response = await service.Register(new(role, password, password, role + "@example.test", role), default);
    var account = repository.ByName(role)!;
    Check(account.PasswordHash != password, "Password stored in plaintext");
    Check(hasher.VerifyHashedPassword(account, account.PasswordHash, password) != PasswordVerificationResult.Failed, "Password hash invalid");
    Check(email.LastCode?.Length == 6, "No six digit code");
    await Reject(403, async () => { await service.Login(new(role, password), default); });
    await Reject(429, async () => { await service.Resend(response.VerificationTicket, default); });
    await Reject(400, () => service.Verify(new(response.VerificationTicket, (email.LastCode == "000000" ? "000001" : "000000")), default));
    await service.Verify(new(response.VerificationTicket, email.LastCode!), default);
    await Reject(400, () => service.Verify(new(response.VerificationTicket, email.LastCode!), default));
    var login = await service.Login(new(role.ToLowerInvariant(), password), default);
    var (authenticated, ticket) = await service.Authenticate(login.Credential, default);
    Check(authenticated.Id == account.Id && login.Account.Role == role, "Account identity mismatch");
    await Reject(401, async () => { await service.Authenticate(login.Credential + "tampered", default); });
    await Reject(401, async () => { await service.Authenticate(response.VerificationTicket, default); });
    var restarted = new CredentialProtector(DataProtectionProvider.Create(new DirectoryInfo(keyRing)), clock);
    Check(restarted.Read(login.Credential).UserId == account.Id, "Restart invalidated credential");
    clock.Advance(TimeSpan.FromDays(365));
    Check(protector.Read(login.Credential).UserId == account.Id, "Credential expired before logout");
    await repository.Revoke(account.Id, ticket.Version, default);
    await repository.Revoke(account.Id, ticket.Version, default);
    Check(repository.ByName(role)!.AuthVersion == 1, "Repeated logout advanced version twice");
    await Reject(401, async () => { await service.Authenticate(login.Credential, default); });
    var next = await service.Login(new(role, password), default);
    await repository.Revoke(account.Id, ticket.Version, default);
    await service.Authenticate(next.Credential, default);
    Check(protector.Read(next.Credential).Version == 1, "New login used stale version");
    repository.Replace(repository.ByName(role)! with { PasswordHash = hasher.HashPassword(account, "a changed test password") });
    await Reject(401, async () => { await service.Authenticate(next.Credential, default); });
}
await Reject(401, async () => { await service.Login(new("unknown", password), default); });
await Reject(401, async () => { await service.Login(new("Teacher", "incorrect"), default); });
await Reject(409, async () => { await service.Register(new("TEACHER", password, password, "other@example.test", "Teacher"), default); });
await Reject(400, async () => { await service.Register(new("other", password, "different", "other@example.test", "Student"), default); });

var challenge = await service.Register(new("pending", password, password, "pending@example.test", "Student"), default);
var firstCode = email.LastCode!;
clock.Advance(TimeSpan.FromSeconds(61));
var replaced = await service.Resend(challenge.VerificationTicket, default);
Check(repository.CodeHash != service.HashCode(repository.ByName("pending")!.Id, firstCode) || email.LastCode == firstCode, "Resend did not replace code");
for (var i = 0; i < 5; i++) await Reject(400, () => service.Verify(new(replaced.VerificationTicket, (email.LastCode == "000000" ? "000001" : "000000")), default));
await Reject(400, () => service.Verify(new(replaced.VerificationTicket, email.LastCode!), default));
clock.Advance(TimeSpan.FromSeconds(61));
var fresh = await service.Resend(replaced.VerificationTicket, default);
clock.Advance(TimeSpan.FromMinutes(11));
await Reject(400, () => service.Verify(new(fresh.VerificationTicket, email.LastCode!), default));
clock.Advance(TimeSpan.FromMinutes(20));
await Reject(400, async () => { await service.Resend(fresh.VerificationTicket, default); });
AuthProblem? continuation = null;
try { await service.Login(new("pending", password), default); } catch (AuthProblem p) { continuation = p; }
Check(continuation?.Error.VerificationTicket is not null, "Pending login did not resume verification");
email.Fail = true;
try { await service.Register(new("mailfail", password, password, "mailfail@example.test", "Student"), default); throw new Exception("SMTP failure was hidden"); }
catch (AuthProblem p) { Check(p.Status == 503 && p.Error.VerificationTicket is not null, "SMTP failure lost verification continuation"); }
email.Fail = false;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
TestServices.Add(builder.Services, db, hasher, protector, email, config, clock);
builder.Services.AddAuthentication(BearerHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BearerHandler>(BearerHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("auth", l => {
    l.PermitLimit = 1000; l.Window = TimeSpan.FromMinutes(1);
}));
builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
await using (var app = builder.Build())
{
    app.UseAuthErrors(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
    await app.StartAsync();
    using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    Check((await http.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous me allowed");
    var invalid = await http.PostAsJsonAsync("/auth/register", new RegisterRequest("badrole", password, password, "bad@example.test", "Admin"));
    Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Invalid role accepted");
    var mismatch = await http.PostAsJsonAsync("/auth/register", new RegisterRequest("mismatch", password, "different", "mismatch@example.test", "Student"));
    Check(mismatch.StatusCode == HttpStatusCode.BadRequest && (await mismatch.Content.ReadAsStringAsync()).Contains("password_mismatch"), "Password mismatch contract changed");
    var malformed = await http.PostAsync("/auth/login", new StringContent("{", Encoding.UTF8, "application/json"));
    Check(malformed.StatusCode == HttpStatusCode.BadRequest, "Malformed JSON was accepted");
    var signup = await http.PostAsJsonAsync("/auth/register", new RegisterRequest("http_student", password, password, "http@example.test", "Student"));
    Check(signup.StatusCode == HttpStatusCode.Accepted, "HTTP registration failed");
    var pending = (await signup.Content.ReadFromJsonAsync<VerificationResponse>())!;
    var loginPending = await http.PostAsJsonAsync("/auth/login", new LoginRequest("http_student", password));
    Check(loginPending.StatusCode == HttpStatusCode.Forbidden, "Unverified HTTP login allowed");
    Check((await http.PostAsJsonAsync("/auth/verify-email", new VerifyRequest(pending.VerificationTicket, email.LastCode!))).StatusCode == HttpStatusCode.NoContent, "HTTP verification failed");
    var loginHttp = await http.PostAsJsonAsync("/auth/login", new LoginRequest("http_student", password));
    Check(loginHttp.StatusCode == HttpStatusCode.OK, "HTTP login failed");
    var signedIn = (await loginHttp.Content.ReadFromJsonAsync<LoginResponse>())!;
    var loginJson = System.Text.Json.JsonDocument.Parse(await loginHttp.Content.ReadAsStringAsync());
    Check(loginJson.RootElement.TryGetProperty("credential", out _) && loginJson.RootElement.TryGetProperty("account", out _), "Login JSON contract changed");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Credential);
    var me = await http.GetAsync("/auth/me");
    Check(me.StatusCode == HttpStatusCode.OK, "HTTP credential rejected");
    Check((await me.Content.ReadFromJsonAsync<AccountDto>())!.Id == signedIn.Account.Id, "HTTP me identity mismatch");
    Check((await http.PostAsync("/auth/logout", null)).StatusCode == HttpStatusCode.NoContent, "HTTP logout failed");
    Check((await http.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Logged out HTTP credential accepted");
    http.DefaultRequestHeaders.Authorization = null;
    var unknownRecovery = await http.PostAsJsonAsync("/auth/forgot-password", new ForgotPasswordRequest("missing@example.test"));
    Check(unknownRecovery.StatusCode == HttpStatusCode.NotFound &&
        (await unknownRecovery.Content.ReadAsStringAsync()).Contains("email_not_found"), "Unknown recovery email contract changed");
    var recoveryRequest = await http.PostAsJsonAsync("/auth/forgot-password", new ForgotPasswordRequest("http@example.test"));
    Check(recoveryRequest.StatusCode == HttpStatusCode.Accepted, "HTTP recovery request failed");
    var recoveryResponse = (await recoveryRequest.Content.ReadFromJsonAsync<PasswordRecoveryResponse>())!;
    var recoveryVerified = await http.PostAsJsonAsync("/auth/verify-password-reset",
        new VerifyPasswordResetRequest(recoveryResponse.RecoveryTicket, email.LastCode!));
    Check(recoveryVerified.StatusCode == HttpStatusCode.OK, "HTTP recovery verification failed");
    var recoveryGrant = (await recoveryVerified.Content.ReadFromJsonAsync<PasswordResetGrant>())!;
    const string replacementPassword = "replacement HTTP password";
    Check((await http.PostAsJsonAsync("/auth/reset-password", new ResetPasswordRequest(recoveryGrant.ResetToken,
        replacementPassword, replacementPassword))).StatusCode == HttpStatusCode.NoContent, "HTTP password reset failed");
    Check((await http.PostAsJsonAsync("/auth/login", new LoginRequest("http_student", password))).StatusCode == HttpStatusCode.Unauthorized,
        "HTTP reset preserved the old password");
    Check((await http.PostAsJsonAsync("/auth/login", new LoginRequest("http_student", replacementPassword))).StatusCode == HttpStatusCode.OK,
        "HTTP reset rejected the new password");
    await app.StopAsync();
}
using (var smtpTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        Task<string> CaptureEmail() => Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(smtpTimeout.Token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            var body = new StringBuilder();
            await writer.WriteLineAsync("220 localhost test SMTP");
            while (await reader.ReadLineAsync(smtpTimeout.Token) is { } line)
            {
                if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End with a dot");
                    while (await reader.ReadLineAsync(smtpTimeout.Token) is { } data && data != ".") body.AppendLine(data);
                    await writer.WriteLineAsync("250 queued");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 localhost");
            }
            return body.ToString();
        }, smtpTimeout.Token);
        var mailConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Smtp:Host"] = "127.0.0.1", ["Smtp:Port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(),
            ["Smtp:Security"] = "None", ["Smtp:From"] = "TemanBicara <noreply@example.test>"
        }).Build();
        var captured = CaptureEmail();
        await new VerificationEmail(mailConfig).Send("student@example.test", "123456", smtpTimeout.Token);
        Check((await captured).Contains("123456", StringComparison.Ordinal), "MailKit SMTP code was not delivered to the local capture server");
        var recoveryCaptured = CaptureEmail();
        await new VerificationEmail(mailConfig).SendPasswordReset("student@example.test", "654321", smtpTimeout.Token);
        var recoveryMessage = await recoveryCaptured;
        Check(recoveryMessage.Contains("654321", StringComparison.Ordinal) &&
            recoveryMessage.Contains("Kode pemulihan password TemanBicara", StringComparison.Ordinal),
            "MailKit recovery subject or code was not delivered to the local capture server");
    }
    finally { listener.Stop(); }
}

var concurrencyAccount = await service.Register(new("race_user", password, password, "race@example.test", "Student"), default);
var raceCode = email.LastCode!;
async Task<bool> TryVerify(string ticketValue, string codeValue)
{
    try { await service.Verify(new(ticketValue, codeValue), default); return true; }
    catch (AuthProblem e) when (e.Status == 400) { return false; }
}
var verifyRace = await Task.WhenAll(TryVerify(concurrencyAccount.VerificationTicket, raceCode),
    TryVerify(concurrencyAccount.VerificationTicket, raceCode));
Check(verifyRace.Count(result => result) == 1, "Concurrent verification consumed the code more than once");
var raceLogin = await service.Login(new("race_user", password), default);
var raceUser = repository.ByName("race_user")!;
await Task.WhenAll(service.Logout(new(raceUser.Id, 0), default), service.Logout(new(raceUser.Id, 0), default));
Check(repository.ByName("race_user")!.AuthVersion == 1, "Concurrent logout advanced auth_version more than once");
var afterRaceLogout = await service.Login(new("race_user", password), default);
await service.Logout(new(raceUser.Id, 0), default);
await service.Authenticate(afterRaceLogout.Credential, default);
Check(true, "Stale concurrent logout preserved new login");
await db.Users.Where(u => u.Id == raceUser.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.Rolesid, 1L));
Check((await service.Authenticate(afterRaceLogout.Credential, default)).User.Role == "Teacher", "Authentication used a stale role");

var legacyCredential = DataProtectionProvider.Create(new DirectoryInfo(keyRing))
    .CreateProtector("TemanBicara.Authentication.v1")
    .Protect(System.Text.Json.JsonSerializer.Serialize(new {
        UserId = raceUser.Id, Version = 1L,
        PasswordStamp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raceUser.PasswordHash))),
        Nonce = "legacy-format-nonce"
    }));
Check((await service.Authenticate(legacyCredential, default)).User.Id == raceUser.Id,
    "Pre-migration credential payload was rejected");

var attemptChallenge = await service.Register(new("attempt_race", password, password, "attempt@example.test", "Student"), default);
var attemptCode = email.LastCode!;
var wrongCode = attemptCode == "000000" ? "000001" : "000000";
var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TryVerify(attemptChallenge.VerificationTicket, wrongCode)));
Check(attempts.All(result => !result), "Wrong code succeeded in a concurrent verification");
Check(await db.Users.Where(u => u.UserName == "attempt_race").Select(u => u.VerificationAttempts).SingleAsync() == 5,
    "Concurrent failed-attempt increments were lost");
Check(!await TryVerify(attemptChallenge.VerificationTicket, attemptCode), "Attempt limit was bypassed");
clock.Advance(TimeSpan.FromSeconds(61));
async Task<bool> TryResend(string value)
{
    try { await service.Resend(value, default); return true; }
    catch (AuthProblem e) when (e.Status == 429 || e.Error.Code == "verification_unavailable") { return false; }
}
var resendRace = await Task.WhenAll(TryResend(attemptChallenge.VerificationTicket), TryResend(attemptChallenge.VerificationTicket));
Check(resendRace.Count(result => result) == 1, "Concurrent resend bypassed cooldown");
Check(await db.Users.Where(u => u.UserName == "attempt_race").Select(u => u.VerificationAttempts).SingleAsync() == 0,
    "Resend did not reset failed attempts");

var lockChallenge = await service.Register(new("lock_race", password, password, "lock@example.test", "Student"), default);
var lockCode = email.LastCode!;
clock.Advance(TimeSpan.FromSeconds(61));
var verificationTask = TryVerify(lockChallenge.VerificationTicket, lockCode);
var resendTask = TryResend(lockChallenge.VerificationTicket);
await Task.WhenAll(verificationTask, resendTask);
Check(await verificationTask || await resendTask, "Verification/resend race made no progress");
if (await resendTask)
    Check(await TryVerify(lockChallenge.VerificationTicket, email.LastCode!), "Resent code was not usable after concurrent verification");
else
    Check(!await TryVerify(lockChallenge.VerificationTicket, lockCode), "Consumed code was replayed after concurrent resend");

await Reject(409, async () => { await service.Register(new("duplicate_email", password, password, "RACE@EXAMPLE.TEST", "Student"), default); });
var oldTicket = DataProtectionProvider.Create(new DirectoryInfo(keyRing))
    .CreateProtector("TemanBicara.EmailVerification.v1")
    .Protect(System.Text.Json.JsonSerializer.Serialize(new { UserId = raceUser.Id, ExpiresAt = clock.GetUtcNow().AddMinutes(30) }));
Check(protector.ReadVerification(oldTicket) == raceUser.Id, "Pre-migration verification payload was rejected");

assertions += await PasswordRecoveryChecks.Run(service, db, email, clock, hasher, protector, config);
Console.WriteLine($"PASS: {assertions} PostgreSQL/handler/HTTP/SMTP assertions (dedicated local database).");

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
sealed class CapturedEmail : IVerificationEmail
{
    public string? LastCode;
    public bool Fail;
    public Task SendPasswordReset(string email, string code, CancellationToken ct) => Send(email, code, ct);
    public Task Send(string email, string code, CancellationToken ct) {
        if (Fail) throw new IOException("Simulated SMTP failure");
        LastCode = code; return Task.CompletedTask;
    }
}

sealed class TestDatabase(DbContextOptions<TemanBicaraDbContext> options) : TemanBicaraDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Supabase-managed extensions and enum types are unrelated to the public application tables.
        foreach (var annotation in modelBuilder.Model.GetAnnotations().Where(a =>
            a.Name.StartsWith("Npgsql:Enum:") || a.Name.StartsWith("Npgsql:PostgresExtension:")).ToArray())
            modelBuilder.Model.RemoveAnnotation(annotation.Name);
    }
}

static class TestServices
{
    public static void Add(IServiceCollection services, TemanBicaraDbContext db, IPasswordHasher<UserAccount> hasher,
        CredentialProtector protector, CapturedEmail email, IConfiguration config, TimeProvider clock)
    {
        services.AddLogging();
        services.AddScoped<TemanBicaraDbContext>(_ => new TemanBicaraDbContext(
            new DbContextOptionsBuilder<TemanBicaraDbContext>().UseNpgsql(db.Database.GetConnectionString()!).Options));
        services.AddSingleton(hasher);
        services.AddSingleton(protector);
        services.AddSingleton<IVerificationEmail>(email);
        services.AddSingleton(config);
        services.AddSingleton(clock);
        services.AddScoped<CredentialAuthentication>();
        services.AddScoped<VerificationCodes>();
        services.AddScoped<PasswordRecovery>();
        services.AddMediatR(c => {
            c.RegisterServicesFromAssembly(typeof(TemanBicaraAPI.AssemblyMarker).Assembly);
            c.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(typeof(TemanBicaraAPI.AssemblyMarker).Assembly);
    }
}

sealed class HandlerClient : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IMediator mediator;
    public HandlerClient(TemanBicaraDbContext db, IPasswordHasher<UserAccount> hasher, CredentialProtector protector,
        CapturedEmail email, IConfiguration config, TimeProvider clock)
    {
        var services = new ServiceCollection();
        TestServices.Add(services, db, hasher, protector, email, config, clock);
        provider = services.BuildServiceProvider();
        mediator = provider.GetRequiredService<IMediator>();
    }
    public ValueTask DisposeAsync() => provider.DisposeAsync();
    private async Task<T> Send<T>(IRequest<T> request, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, ct);
    }
    public async Task Logout(LogoutRequest request, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, ct);
    }
    public Task<VerificationResponse> Register(RegisterRequest request, CancellationToken ct) => Send(request, ct);
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) => Send(request, ct);
    public async Task Verify(VerifyRequest request, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, ct);
    }
    public Task<VerificationResponse> Resend(string ticket, CancellationToken ct) => Send(new ResendRequest(ticket), ct);
    public Task<PasswordRecoveryResponse> ForgotPassword(string email, CancellationToken ct = default) => Send(new ForgotPasswordRequest(email), ct);
    public Task<PasswordResetGrant> VerifyPasswordReset(string ticket, string code, CancellationToken ct = default) =>
        Send(new VerifyPasswordResetRequest(ticket, code), ct);
    public async Task ResetPassword(string token, string password, string confirmation, CancellationToken ct = default)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ResetPasswordRequest(token, password, confirmation), ct);
    }
    public async Task<(UserAccount User, AuthTicket Ticket)> Authenticate(string value, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CredentialAuthentication>().Authenticate(value, ct);
    }
    public string HashCode(Guid id, string code) => provider.GetRequiredService<VerificationCodes>().HashCode(id, code);

}

sealed class DatabaseUsers(TemanBicaraDbContext db)
{
    public string? CodeHash => db.Users.AsNoTracking().Where(u => u.UserName == "pending").Select(u => u.VerificationCodeHash).SingleOrDefault();
    public UserAccount? ByName(string name) => db.Accounts().AsEnumerable().SingleOrDefault(u => u.Username.Equals(name, StringComparison.OrdinalIgnoreCase));
    public void Replace(UserAccount user)
    {
        db.Users.Where(u => u.Id == user.Id).ExecuteUpdate(set => set.SetProperty(u => u.Password, user.PasswordHash));
        db.ChangeTracker.Clear();
    }
    public async Task Revoke(Guid id, long version, CancellationToken ct) =>
        await new TemanBicaraAPI.RequestHandlers.Auth.LogoutHandler(db).Handle(new(id, version), ct);
}
