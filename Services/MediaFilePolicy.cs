using System.Text;
namespace Lensora.Api.Services;

public static class MediaFilePolicy
{
    public const long MaximumBytes = 50_000_000;
    public static async Task<(string Kind, string ContentType)> Validate(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaximumBytes) throw new ArgumentException("Files must be between 1 byte and 50 MB.");
        await using var stream = file.OpenReadStream();
        var bytes = new byte[16];
        var count = await stream.ReadAtLeastAsync(bytes, 12, false, ct);
        var hex = Convert.ToHexString(bytes.AsSpan(0, count));
        var text = Encoding.ASCII.GetString(bytes, 0, count);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        (string Kind, string Type)? type = ext switch {
            ".jpg" or ".jpeg" when hex.StartsWith("FFD8FF") => ("image", "image/jpeg"),
            ".png" when hex.StartsWith("89504E470D0A1A0A") => ("image", "image/png"),
            ".gif" when text.StartsWith("GIF87a") || text.StartsWith("GIF89a") => ("image", "image/gif"),
            ".webp" when count >= 12 && text.StartsWith("RIFF") && text.Substring(8,4) == "WEBP" => ("image", "image/webp"),
            ".mp4" or ".mov" when count >= 12 && text.Substring(4,4) == "ftyp" => ("video", ext == ".mov" ? "video/quicktime" : "video/mp4"),
            ".webm" when hex.StartsWith("1A45DFA3") => ("video", "video/webm"),
            ".mp3" when text.StartsWith("ID3") || (count >= 2 && bytes[0] == 255 && (bytes[1] & 224) == 224) => ("audio", "audio/mpeg"),
            ".wav" when count >= 12 && text.StartsWith("RIFF") && text.Substring(8,4) == "WAVE" => ("audio", "audio/wav"),
            ".ogg" when text.StartsWith("OggS") => ("audio", "audio/ogg"),
            ".pdf" when text.StartsWith("%PDF-") => ("document", "application/pdf"),
            _ => null
        };
        if (type is null) throw new ArgumentException("Supported files: JPG, PNG, GIF, WebP, MP4, MOV, WebM, MP3, WAV, OGG and PDF. The file content must match its extension.");
        if (type.Value.Kind == "image" && file.Length > 20_000_000) throw new ArgumentException("Images must be no larger than 20 MB.");
        return (type.Value.Kind, type.Value.Type);
    }
}
