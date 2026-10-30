using TemanBicaraAPI.Contracts.ResponseModels.Common;
namespace TemanBicaraAPI.Commons.Exceptions;
public sealed class AuthProblem(int status, ApiError error) : Exception(error.Message)
{
    public int Status { get; } = status;
    public ApiError Error { get; } = error;
}
