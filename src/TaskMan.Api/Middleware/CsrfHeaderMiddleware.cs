using Microsoft.AspNetCore.Mvc;
using TaskMan.Infrastructure.Auth;

namespace TaskMan.Api.Middleware;

public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !IsSafeMethod(request.Method)
            && !string.Equals(
                request.Headers[AuthDefaults.CsrfHeaderName],
                AuthDefaults.CsrfHeaderValue,
                StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Missing security header",
                    Detail = $"Non-GET API requests must include {AuthDefaults.CsrfHeaderName}: {AuthDefaults.CsrfHeaderValue}.",
                },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);

            return;
        }

        await next(context);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method)
        || HttpMethods.IsHead(method)
        || HttpMethods.IsOptions(method)
        || HttpMethods.IsTrace(method);
}
