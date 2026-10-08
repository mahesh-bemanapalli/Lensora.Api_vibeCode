using Lensora.Api.Contracts;
using Lensora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Lensora.Api.Controllers;

[ApiController, Authorize, Route("api/admin/media")]
public sealed class AdminMediaController(
    CloudinaryMediaService media,
    ILogger<AdminMediaController> logger) : ControllerBase
{
    private const long MaximumRequestSize = 21 * 1024 * 1024;

    [HttpPost("images")]
    [RequestSizeLimit(MaximumRequestSize)]
    public async Task<ActionResult<UploadImageResponse>> Upload([FromForm] UploadImageRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null) return BadRequest(new
        {
            message = "Select an image to upload."
        }
        );
        try
        {
            var uploaded = await media.UploadImage(request.File, cancellationToken);
            return Ok(new UploadImageResponse(uploaded.Url, uploaded.PublicId));
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Rejected image upload request.");
            return BadRequest(new
            {
                message = exception.Message
            }
            );
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Cloudinary image upload failed.");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: exception.Message);
        }
    }
}
