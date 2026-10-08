using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
namespace Lensora.Api.Services;

public sealed class CloudinaryMediaService(IConfiguration configuration)
{
    private const long MaximumFileSize = 20 * 1024 * 1024;
    public async Task<(string Url, string PublicId)> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumFileSize) throw new ArgumentException("Image files must be between 1 byte and 20 MB.");
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only image files are allowed.");
        var cloudinary = new Cloudinary(new Account(Required("Cloudinary:CloudName"), Required("Cloudinary:ApiKey"), Required("Cloudinary:ApiSecret")));
        await using var stream = file.OpenReadStream();
        var result = await cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = configuration["Cloudinary:Folder"] ?? "lensora",
            UseFilename = false,
            UniqueFilename = true
        }
        , cancellationToken);
        if (result.Error is not null || result.SecureUrl is null || string.IsNullOrWhiteSpace(result.PublicId)) throw new InvalidOperationException("Cloudinary could not upload the image.");
        return (result.SecureUrl.ToString(), result.PublicId);
    }
    private string Required(string key) => !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]! : throw new InvalidOperationException($"{key} has not been configured.");
}
