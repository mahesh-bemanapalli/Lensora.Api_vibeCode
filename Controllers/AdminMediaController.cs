using System.Security.Claims;
using System.Security.Cryptography;
using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Lensora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Controllers;

[ApiController, Authorize, Route("api/admin/media")]
public sealed class AdminMediaController(CloudinaryMediaService media, LensoraDbContext db, ILogger<AdminMediaController> logger) : ControllerBase
{
    private async Task<int?> Owner(CancellationToken ct) {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(subject, out var userId) ? await db.Photographers.Where(x => x.UserId == userId).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct) : null;
    }
    private Task Lock(int id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @result int; EXEC @result=sp_getapplock @Resource={"media-owner-" + id}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result<0 THROW 50002, 'Media library is busy. Try again.', 1;", ct);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) {
        var id = await Owner(ct); if(id is null) return Forbid();
        var limit = await db.Photographers.Where(x => x.Id == id).Select(x => x.StorageLimitBytes).SingleAsync(ct);
        var items = await db.MediaAssets.AsNoTracking().Where(x => x.PhotographerId == id).OrderByDescending(x => x.Id).ToListAsync(ct);
        return Ok(new { limitBytes = limit, usedBytes = items.Where(x => x.Status != "External").Sum(x => x.SizeBytes ?? 0), reservedBytes = items.Where(x => x.Status is "Pending" or "Retryable").Sum(x => x.SizeBytes ?? 0), unknownCount = items.Count(x => x.Status != "External" && x.SizeBytes == null), items });
    }

    [HttpPost("images")]
    [HttpPost("files")]
    [RequestSizeLimit(51_000_000), RequestFormLimits(MultipartBodyLengthLimit = 51_000_000)]
    public async Task<IActionResult> Upload([FromForm] UploadImageRequest request, CancellationToken ct) {
        var id = await Owner(ct); if(id is null) return Forbid();
        if(request.File is not { } file) return BadRequest(new { message = "Select a file." });
        try {
            var type = await MediaFilePolicy.Validate(file, ct);
            if(Request.Path.Value?.EndsWith("/images") == true && type.Kind != "image") return BadRequest(new { message = "Select an image for this field." });
            await using var stream = file.OpenReadStream();
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            MediaAsset asset;
            await using(var transaction = await db.Database.BeginTransactionAsync(ct)) {
                await Lock(id.Value, ct);
                var existing = await db.MediaAssets.FirstOrDefaultAsync(x => x.PhotographerId == id && x.ContentHash == hash, ct);
                if(existing is not null && existing.Status != "Retryable") return existing.Status == "Ready" ? Ok(new { existing.Id, existing.Url, existing.PublicId, isDuplicate = true }) : Conflict(new { message = "This file is being uploaded or awaiting reconciliation. Use Sync existing media in your library before retrying." });
                if(existing is not null) {
                    media.CheckConfiguration();
                    asset = existing; asset.Status = "Pending"; asset.CreatedUtc = DateTime.UtcNow;
                } else {
                if(await db.MediaAssets.AnyAsync(x => x.PhotographerId == id && x.Status != "External" && x.SizeBytes == null, ct)) return Conflict(new { message = "Sync existing media in your library before uploading so storage usage can be measured." });
                var used = await db.MediaAssets.Where(x => x.PhotographerId == id && x.Status != "External").SumAsync(x => x.SizeBytes ?? 0, ct);
                var limit = await db.Photographers.Where(x => x.Id == id).Select(x => x.StorageLimitBytes).SingleAsync(ct);
                if(file.Length > limit - used) return StatusCode(413, new { message = "Your storage allowance would be exceeded. Contact the platform administrator." });
                media.CheckConfiguration();
                var publicId = $"lensora/photographers/{id}/{hash.ToLowerInvariant()}" + (type.Kind == "document" ? ".pdf" : "");
                asset = new MediaAsset { PhotographerId = id.Value, FileName = Path.GetFileName(file.FileName)[..Math.Min(Path.GetFileName(file.FileName).Length,255)], Url = "", PublicId = publicId, ContentHash = hash, Kind = type.Kind, ContentType = type.ContentType, SizeBytes = file.Length, Status = "Pending", CreatedUtc = DateTime.UtcNow };
                db.MediaAssets.Add(asset); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                }
                if(existing is not null) { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            }
            // Reservation is durable before sending bytes. Even uncertain provider failures keep
            // their reservation until reconciliation; concurrent uploads cannot exceed quota.
            using var uploadDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            uploadDeadline.CancelAfter(TimeSpan.FromMinutes(5));
            var url = await media.UploadAsset(file, asset.PublicId!, asset.Kind, uploadDeadline.Token);
            await using(var transaction = await db.Database.BeginTransactionAsync(ct)) {
                await Lock(id.Value, ct);
                await db.Entry(asset).ReloadAsync(ct);
                asset.Url = url; asset.Status = "Ready";
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            }
            return Ok(new { asset.Id, asset.Url, asset.PublicId, isDuplicate = false });
        } catch(ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
          catch(Exception ex) when(ex is not OperationCanceledException || !ct.IsCancellationRequested) { logger.LogError(ex,"Media upload failed for photographer {PhotographerId}",id); return Problem(statusCode:503,title:"Upload could not be completed. Check provider configuration, then use Sync existing media to reconcile any reserved space before retrying."); }
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct) {
        var id = await Owner(ct); if(id is null) return Forbid();
        var items = await db.MediaAssets.Where(x => x.PhotographerId == id && x.PublicId != null && x.Status != "Missing" && (x.SizeBytes == null || (x.ContentHash == null && x.SizeBytes <= MediaFilePolicy.MaximumBytes) || x.Status == "Pending"))
            .Where(x => x.Status != "Pending" || x.CreatedUtc <= DateTime.UtcNow.AddMinutes(-10))
            .OrderBy(x => x.Id).Take(10).ToListAsync(ct);
        int synced=0, missing=0;
        try {
            foreach(var asset in items) {
                var inspectedStatus = asset.Status;
                var inspectedCreatedUtc = asset.CreatedUtc;
                // Never release a reservation while its uploader may still be running.
                if(asset.Status == "Pending" && asset.CreatedUtc > DateTime.UtcNow.AddMinutes(-10)) continue;
                var info = await media.InspectAsset(asset.PublicId!,asset.Kind,ct);
                if(info.Missing) {
                    {
                        await using var retryTransaction = await db.Database.BeginTransactionAsync(ct);
                        await Lock(id.Value,ct); await db.Entry(asset).ReloadAsync(ct);
                        if(asset.Status == inspectedStatus && asset.CreatedUtc == inspectedCreatedUtc) {
                            asset.Status = asset.Status == "Pending" ? "Retryable" : "Missing";
                            await db.SaveChangesAsync(ct);
                        }
                        await retryTransaction.CommitAsync(ct);
                    }
                    missing++; continue;
                }
                string? hash = asset.ContentHash;
                if(hash is null && info.Bytes <= MediaFilePolicy.MaximumBytes) {
                    var uri = new Uri(info.Url);
                    if(!media.IsManagedUrl(uri)) throw new InvalidOperationException("Unexpected asset host.");
                    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
                    using var response = await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
                    response.EnsureSuccessStatusCode();
                    await using var content = await response.Content.ReadAsStreamAsync(ct);
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(content,ct));
                }
                await using var transaction=await db.Database.BeginTransactionAsync(ct);
                await Lock(id.Value,ct); await db.Entry(asset).ReloadAsync(ct);
                if (asset.Status != inspectedStatus || asset.CreatedUtc != inspectedCreatedUtc) continue;
                asset.SizeBytes=info.Bytes; asset.Url=info.Url; asset.ContentHash=hash; asset.Status="Ready";
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); synced++;
            }
            return Ok(new { synced, missing, message = missing > 0 ? "Some provider assets were not found; their usage remains unresolved. Contact the administrator." : "Media synchronized. Run again if more items remain. Pending uploads can be reconciled after 10 minutes." });
        } catch(Exception ex) when(ex is not OperationCanceledException) { logger.LogError(ex,"Media sync failed for photographer {PhotographerId}",id); return Problem(statusCode:503,title:"Unable to synchronize all media. Check Cloudinary configuration and try again; completed items are saved."); }
    }
}
