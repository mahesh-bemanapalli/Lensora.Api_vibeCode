using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Lensora.Api.Controllers;
using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

void Check(bool value, string message) { if (!value) throw new Exception(message); }
var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("appsettings.json")).AddUserSecrets(typeof(Lensora.Api.Services.JwtTokenService).Assembly, optional: true).AddEnvironmentVariables().Build();
var connection = configuration.GetConnectionString("Lensora") ?? configuration.GetConnectionString("DefaultConnection");
await using var db = new LensoraDbContext(new DbContextOptionsBuilder<LensoraDbContext>().UseSqlServer(connection).Options);
var policy = (AuthorizeAttribute)Attribute.GetCustomAttribute(typeof(PlatformPhotographersController), typeof(AuthorizeAttribute))!;
Check(policy.Roles == "SuperAdmin", "Platform endpoints must require SuperAdmin");
Check(typeof(AuthController).GetMethod("Register")!.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Any(x=>x.Roles=="SuperAdmin"), "Registration must require SuperAdmin");
Check(typeof(EventLogsController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Any(x=>x.Roles=="SuperAdmin"), "Event logs must require SuperAdmin");
ControllerContext Context(int id) => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, "SuperAdmin") }, "test")) } };
var platform = new PlatformPhotographersController(db) { ControllerContext = Context(0) };
Check(await platform.List() is ForbidResult, "Unknown actor must be denied");
var user = new User { Email = $"design-check-{Guid.NewGuid():N}@example.invalid", PasswordHash = "test-only-no-login", Role = "Photographer" };
var profile = new Photographer { User = user, Name = "Temporary design check", Slug = $"design-check-{Guid.NewGuid():N}" };
db.Photographers.Add(profile);
await db.SaveChangesAsync();
try {
    platform.ControllerContext = Context(user.Id);
    Check(await platform.List() is ForbidResult, "Forged/stale SuperAdmin token must be denied using database role");
    Check(await platform.Assign(profile.Slug, new("minimal-gallery")) is ForbidResult, "Photographer assignment must be denied");
    Check(await platform.Preview(profile.Slug) is ForbidResult, "Photographer preview must be denied");
    var adminId = await db.Users.Where(x => x.Role == "SuperAdmin").Select(x => x.Id).FirstAsync();
    platform.ControllerContext = Context(adminId);
    Check(await platform.List() is OkObjectResult, "SuperAdmin can list photographers");
    Check(await platform.Preview(profile.Slug) is OkObjectResult, "SuperAdmin can preview actual profile");
    Check(await platform.Assign(profile.Slug, new("minimal-gallery")) is OkObjectResult, "SuperAdmin assignment succeeds");
    Check(profile.TemplateKey == "minimal-gallery", "Saved template changes");
    var audit = await db.TemplateAssignments.SingleAsync(x => x.PhotographerId == profile.Id);
    Check(audit.AssignedByUserId == adminId && audit.PreviousTemplateKey == "editorial" && audit.TemplateKey == "minimal-gallery", "Audit records actor and before/after designs");
    await platform.Assign(profile.Slug, new("minimal-gallery"));
    Check(await db.TemplateAssignments.CountAsync(x => x.PhotographerId == profile.Id) == 1, "Repeat assignment is idempotent");
    var content = new AdminContentController(db, NullLogger<AdminContentController>.Instance) { ControllerContext = Context(user.Id) };
    var response = await content.UpdateProfile(new UpdateProfileRequest(profile.Name, profile.Slug, null, null, null, null, 50, 50, "editorial"));
    Check(response is ObjectResult { StatusCode: 403 }, "Profile endpoint cannot bypass assignment restrictions");
    foreach (var key in new[] { "", "unknown", "EDITORIAL" }) {
        var request = new AssignDesignRequest(key);
        var attributes = typeof(AssignDesignRequest).GetConstructors()[0].GetParameters()[0].GetCustomAttributes(typeof(ValidationAttribute), true).Cast<ValidationAttribute>();
        Check(attributes.Any(attribute => !attribute.IsValid(key)), "Invalid key rejected");
    }
    Console.WriteLine("PASS: SuperAdmin policy; forged/stale role denial; preview authorization; assignment persistence; audit; idempotency; profile bypass denial; invalid designs.");
} finally {
    db.TemplateAssignments.RemoveRange(await db.TemplateAssignments.Where(x => x.PhotographerId == profile.Id).ToListAsync());
    db.Photographers.Remove(profile); db.Users.Remove(user); await db.SaveChangesAsync();
}
