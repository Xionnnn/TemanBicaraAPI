using MediatR;
using TemanBicaraAPI.Contracts.DTOs.Auth;
using TemanBicaraAPI.Contracts.ResponseModels.Auth;

namespace TemanBicaraAPI.Contracts.RequestModels.Auth;

public sealed record LoginRequest(string Username, string Password) : IRequest<LoginResponse>;
