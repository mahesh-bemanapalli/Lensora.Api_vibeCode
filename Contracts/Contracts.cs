using System.ComponentModel.DataAnnotations;
namespace Lensora.Api.Contracts;

public sealed record RegisterRequest([Required, EmailAddress, StringLength(320)] string Email, [Required, MinLength(12)] string Password, [Required, StringLength(100)] string Name, [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")] string Slug);
public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public sealed record TokenResponse(string Token);
public sealed record CreateBookingRequest([Required, StringLength(120)] string ClientName, [Required, EmailAddress] string ClientEmail, DateOnly EventDate, [StringLength(2000)] string? Message, int? PackageId);
public sealed record UpdateBookingStatusRequest([Required, RegularExpression("^(Accepted|Rejected|Pending)$")] string Status);
public sealed record PublicProfileResponse(string Name, string Slug, string? Bio, string? Location, string? ProfileImageUrl, IReadOnlyList<PortfolioItemResponse> Portfolio, IReadOnlyList<GearItemResponse> Gear, IReadOnlyList<PackageResponse> Packages);
public sealed record PackageResponse(int Id, string Name, string Description, string ImageUrl, string? ImagePublicId, decimal Price, string Currency, int? CoverageHours, string? Deliverables, bool IsPublished, int DisplayOrder);
public sealed record UpsertPackageRequest([Required, StringLength(160)] string Name, [Required, StringLength(2000)] string Description, [Required, Url, StringLength(2048)] string ImageUrl, [StringLength(255)] string? ImagePublicId, [Range(0, 9999999999.99)] decimal Price, [Required, RegularExpression("^[A-Z]{3}$")] string Currency, [Range(1, 1000)] int? CoverageHours, [StringLength(2000)] string? Deliverables, bool IsPublished, [Range(0, 10000)] int DisplayOrder);
public sealed record PortfolioItemResponse(int Id, string ImageUrl, string? ImagePublicId, string? Title, string? Category, int DisplayOrder);
public sealed record GearItemResponse(int Id, string Name, string Category, string Brand, string Model, string? Description, string? ImageUrl, string? ImagePublicId, bool IsFeatured, int DisplayOrder);
public sealed record BookingResponse(int Id, string ClientName, string ClientEmail, DateOnly EventDate, string? Message, string Status, DateTime CreatedUtc, int? PackageId, string? PackageName);
public sealed record AdminProfileResponse(string Name, string Slug, string? Bio, string? Location, string? ProfileImageUrl, string? ProfileImagePublicId);
public sealed record UpdateProfileRequest([Required, StringLength(100)] string Name, [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")] string Slug, [StringLength(2000)] string? Bio, [StringLength(160)] string? Location, [Url, StringLength(2048)] string? ProfileImageUrl, [StringLength(255)] string? ProfileImagePublicId);
public sealed record CreatePortfolioItemRequest([Required, Url, StringLength(2048)] string ImageUrl, [StringLength(255)] string? ImagePublicId, [StringLength(200)] string? Title, [StringLength(100)] string? Category, [Range(0, 10000)] int DisplayOrder);
public sealed record UpdatePortfolioItemRequest([Required, Url, StringLength(2048)] string ImageUrl, [StringLength(255)] string? ImagePublicId, [StringLength(200)] string? Title, [StringLength(100)] string? Category, [Range(0, 10000)] int DisplayOrder);
public sealed record CreateGearItemRequest([Required, StringLength(160)] string Name, [Required, StringLength(60)] string Category, [Required, StringLength(100)] string Brand, [Required, StringLength(160)] string Model, [StringLength(1000)] string? Description, [Url, StringLength(2048)] string? ImageUrl, [StringLength(255)] string? ImagePublicId, bool IsFeatured, [Range(0, 10000)] int DisplayOrder);
public sealed record UpdateGearItemRequest([Required, StringLength(160)] string Name, [Required, StringLength(60)] string Category, [Required, StringLength(100)] string Brand, [Required, StringLength(160)] string Model, [StringLength(1000)] string? Description, [Url, StringLength(2048)] string? ImageUrl, [StringLength(255)] string? ImagePublicId, bool IsFeatured, [Range(0, 10000)] int DisplayOrder);
public sealed record UploadImageResponse(string Url, string PublicId);
public sealed class UploadImageRequest
{
    [Required]
    public IFormFile? File
    {
        get;
        init;
    }
}
