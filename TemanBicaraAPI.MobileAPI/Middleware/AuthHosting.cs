using Microsoft.AspNetCore.Diagnostics;

using FluentValidation;
using TemanBicaraAPI.Commons.Exceptions;
using TemanBicaraAPI.Contracts.ResponseModels.Common;
namespace TemanBicaraAPI.MobileAPI.Middleware;

public static class AuthHosting
{
    public static void UseAuthErrors(this WebApplication app) => app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is AuthProblem problem)
        {
            context.Response.StatusCode = problem.Status;
            if (problem.Error.ResendAfter is { } wait) context.Response.Headers.RetryAfter = wait.ToString();
            await context.Response.WriteAsJsonAsync(problem.Error);
            return;
        }
        if (exception is ValidationException validation)
        {
            context.Response.StatusCode = 400;
            var mismatch = validation.Errors.FirstOrDefault(e => e.ErrorCode == "password_mismatch");
            if (mismatch is not null && validation.Errors.All(e => e.ErrorCode == "password_mismatch"))
                await context.Response.WriteAsJsonAsync(new ApiError("password_mismatch", mismatch.ErrorMessage));
            else
                await context.Response.WriteAsJsonAsync(new { code = "invalid_input", message = "Periksa kembali data yang Anda masukkan.",
                    errors = validation.Errors.GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()) });
            return;
        }
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new ApiError("service_unavailable", "Layanan belum tersedia. Coba lagi nanti."));
    }));
}
