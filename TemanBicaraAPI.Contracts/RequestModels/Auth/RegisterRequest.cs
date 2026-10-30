using MediatR;
using TemanBicaraAPI.Contracts.DTOs.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;

namespace TemanBicaraAPI.Contracts.RequestModels.Auth;

public sealed record RegisterRequest(string Username, string Password, string ConfirmPassword, string Email, string Role) : IRequest<VerificationResponse>;
