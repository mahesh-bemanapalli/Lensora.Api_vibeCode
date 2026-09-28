namespace Lensora.Api.Domain;

public sealed class User
{
    public int Id
    {
        get;
        set;
    }
    public required string Email
    {
        get;
        set;
    }
    public required string PasswordHash
    {
        get;
        set;
    }
    public required string Role
    {
        get;
        set;
    }
    = "Photographer";
    public Photographer? Photographer
    {
        get;
        set;
    }
}
public sealed class Photographer
{
    public int Id
    {
        get;
        set;
    }
    public int UserId
    {
        get;
        set;
    }
    public required string Name
    {
        get;
        set;
    }
    public required string Slug
    {
        get;
        set;
    }
    public string? Bio
    {
        get;
        set;
    }
    public string? Location
    {
        get;
        set;
    }
    public string? ProfileImageUrl
    {
        get;
        set;
    }
    public string? ProfileImagePublicId
    {
        get;
        set;
    }
    public User? User
    {
        get;
        set;
    }
    public ICollection<PortfolioItem> PortfolioItems
    {
        get;
        set;
    }
    = [];
    public ICollection<GearItem> GearItems
    {
        get;
        set;
    }
    = [];
    public ICollection<Package> Packages { get; set; } = [];
}
public sealed class Package
{
    public int Id { get; set; }
    public int PhotographerId { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string ImageUrl { get; set; }
    public string? ImagePublicId { get; set; }
    public decimal Price { get; set; }
    public required string Currency { get; set; }
    public int? CoverageHours { get; set; }
    public string? Deliverables { get; set; }
    public bool IsPublished { get; set; }
    public int DisplayOrder { get; set; }
    public Photographer? Photographer { get; set; }
}
public sealed class PortfolioItem
{
    public int Id
    {
        get;
        set;
    }
    public int PhotographerId
    {
        get;
        set;
    }
    public required string ImageUrl
    {
        get;
        set;
    }
    public string? ImagePublicId
    {
        get;
        set;
    }
    public string? Title
    {
        get;
        set;
    }
    public string? Category
    {
        get;
        set;
    }
    public int DisplayOrder
    {
        get;
        set;
    }
    public Photographer? Photographer
    {
        get;
        set;
    }
}
public sealed class GearItem
{
    public int Id
    {
        get;
        set;
    }
    public int PhotographerId
    {
        get;
        set;
    }
    public required string Name
    {
        get;
        set;
    }
    public required string Category
    {
        get;
        set;
    }
    public required string Brand
    {
        get;
        set;
    }
    public required string Model
    {
        get;
        set;
    }
    public string? Description
    {
        get;
        set;
    }
    public string? ImageUrl
    {
        get;
        set;
    }
    public string? ImagePublicId
    {
        get;
        set;
    }
    public bool IsFeatured
    {
        get;
        set;
    }
    public int DisplayOrder
    {
        get;
        set;
    }
    public Photographer? Photographer
    {
        get;
        set;
    }
}
public sealed class Booking
{
    public int Id
    {
        get;
        set;
    }
    public int PhotographerId
    {
        get;
        set;
    }
    public int? PackageId { get; set; }
    public Package? Package { get; set; }
    public required string ClientName
    {
        get;
        set;
    }
    public required string ClientEmail
    {
        get;
        set;
    }
    public string? ClientPhone { get; set; }
    public bool WhatsAppOptIn { get; set; }
    public DateTime? WhatsAppOptInUtc { get; set; }
    public string? WhatsAppConsentVersion { get; set; }
    public string? InternalNotes { get; set; }
    public DateTime? FollowUpUtc { get; set; }
    public decimal? AgreedAmount { get; set; }
    public decimal AmountReceived { get; set; }
    public string Currency { get; set; } = "INR";
    public ICollection<NotificationOutbox> Notifications { get; set; } = [];
    public DateOnly EventDate
    {
        get;
        set;
    }
    public string? Message
    {
        get;
        set;
    }
    public required string Status
    {
        get;
        set;
    }
    = "Pending";
    public DateTime CreatedUtc
    {
        get;
        set;
    }
    public Photographer? Photographer
    {
        get;
        set;
    }
}

public sealed class NotificationOutbox
{
    public long Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public required string EventType { get; set; }
    public required string Channel { get; set; }
    public required string Recipient { get; set; }
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? SentUtc { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastError { get; set; }
}
