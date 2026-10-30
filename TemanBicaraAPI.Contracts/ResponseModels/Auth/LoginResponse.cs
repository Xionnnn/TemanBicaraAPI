using TemanBicaraAPI.Contracts.DTOs.Auth;
namespace TemanBicaraAPI.Contracts.ResponseModels.Auth;
public sealed record LoginResponse(string Credential, AccountDto Account);
