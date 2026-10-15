using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
namespace Lensora.Api.Services;

public class CloudinaryMediaService(IConfiguration configuration)
{
    private Cloudinary Client() => new(new Account(Required("Cloudinary:CloudName"), Required("Cloudinary:ApiKey"), Required("Cloudinary:ApiSecret")));
    public virtual void CheckConfiguration() => _ = Client();

    public virtual async Task<string> UploadAsset(IFormFile file, string publicId, string kind, CancellationToken cancellationToken)
    {
        var client = Client();
        await using var stream = file.OpenReadStream();
        var description = new FileDescription(Path.GetFileName(file.FileName), stream);
        if (kind == "image")
        {
            var result = await client.UploadAsync(new ImageUploadParams { File = description, PublicId = publicId, Overwrite = false }, cancellationToken);
            if (result.Error is not null || result.SecureUrl is null) throw new InvalidOperationException("Media provider rejected the image.");
            return result.SecureUrl.ToString();
        }
        if (kind is "video" or "audio")
        {
            var result = await client.UploadAsync(new VideoUploadParams { File = description, PublicId = publicId, Overwrite = false }, cancellationToken);
            if (result.Error is not null || result.SecureUrl is null) throw new InvalidOperationException("Media provider rejected the video or audio.");
            return result.SecureUrl.ToString();
        }
        var raw = await client.UploadAsync(new RawUploadParams { File = description, PublicId = publicId, Overwrite = false }, "raw", cancellationToken);
        if (raw.Error is not null || raw.SecureUrl is null) throw new InvalidOperationException("Media provider rejected the document.");
        return raw.SecureUrl.ToString();
    }

    public virtual async Task<(bool Missing, long Bytes, string Url)> InspectAsset(string publicId, string kind, CancellationToken cancellationToken)
    {
        var result = await Client().GetResourceAsync(new GetResourceParams(publicId) {
            ResourceType = kind == "image" ? ResourceType.Image : kind is "video" or "audio" ? ResourceType.Video : ResourceType.Raw
        }, cancellationToken);
        if (result.StatusCode == System.Net.HttpStatusCode.NotFound) return (true, 0, "");
        if (result.Error is not null || string.IsNullOrWhiteSpace(result.SecureUrl)) throw new InvalidOperationException("Unable to read stored media metadata.");
        return (false, result.Bytes, result.SecureUrl);
    }

    public bool IsManagedUrl(Uri uri) => uri.Scheme == "https" && uri.Host == "res.cloudinary.com"
        && uri.AbsolutePath.StartsWith($"/{Required("Cloudinary:CloudName")}/", StringComparison.Ordinal);
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
