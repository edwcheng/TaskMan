using Microsoft.AspNetCore.Mvc;
using TaskMan.Infrastructure.Persistence;
using TaskMan.Infrastructure.Storage;

namespace TaskMan.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(
    AppDbContext db,
    StoragePaths storagePaths) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        var databaseOk = await CanReachDatabaseAsync(cancellationToken);
        var storageOk = await CanWriteAttachmentsAsync(cancellationToken);
        var healthy = databaseOk && storageOk;

        var response = new HealthResponse(
            healthy ? "ok" : "unhealthy",
            DateTimeOffset.UtcNow,
            databaseOk ? "ok" : "unavailable",
            storageOk ? "ok" : "unavailable");

        return healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    private async Task<bool> CanReachDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<bool> CanWriteAttachmentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(storagePaths.AttachmentsPath);

            var probePath = Path.Combine(
                storagePaths.AttachmentsPath,
                $".health-{Guid.NewGuid():N}");

            await System.IO.File.WriteAllTextAsync(probePath, string.Empty, cancellationToken);
            System.IO.File.Delete(probePath);

            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

public sealed record HealthResponse(
    string Status,
    DateTimeOffset Utc,
    string Database,
    string Storage);
