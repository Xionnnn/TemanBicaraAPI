namespace TemanBicaraAPI.Commons.Services;
public interface IVerificationEmail
{
    Task Send(string email, string code, CancellationToken ct);
}
