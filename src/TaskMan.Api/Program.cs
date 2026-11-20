using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using TaskMan.Api.Middleware;
using TaskMan.Api.Security;
using TaskMan.Infrastructure;
using TaskMan.Infrastructure.Auth;
using TaskMan.Infrastructure.Http;
using TaskMan.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

var storagePaths = StoragePaths.Create(
    builder.Configuration,
    builder.Environment.ContentRootPath);

var authOptions = builder.Configuration
    .GetSection(AuthOptions.SectionName)
    .Get<AuthOptions>() ?? new AuthOptions();

authOptions.Validate();

var swaggerEnabled = builder.Configuration.GetValue<bool?>("Swagger:Enabled")
    ?? builder.Environment.IsDevelopment();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

if (swaggerEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration, storagePaths);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    ForwardedHeadersConfiguration.Apply(options, builder.Configuration);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var value)
            ? value
            : TimeSpan.FromSeconds(authOptions.LoginWindowSeconds);

        context.HttpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);

        context.HttpContext.Response.ContentType = "application/problem+json";

        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Too many login attempts. Try again later.",
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    };

    options.AddPolicy(RateLimitPolicies.Login, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authOptions.LoginPermitLimit,
                Window = TimeSpan.FromSeconds(authOptions.LoginWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.RoutePrefix = "swagger");
}

app.UseRouting();
app.UseRateLimiter();

app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
