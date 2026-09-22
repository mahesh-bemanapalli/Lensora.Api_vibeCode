using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Lensora.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Lensora.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(LensoraDbContext db, JwtTokenService tokens, ILogger<AuthController> logger, IHostEnvironment environment) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request)
    {
        try
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var slug = request.Slug.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(x => x.Email == email) || await db.Photographers.AnyAsync(x => x.Slug == slug)) return Conflict(new
            {
                message = "Email or photographer URL is already in use."
            }
            );
            var user = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = "Photographer"
            }
            ;
            db.Users.Add(user);
            db.Photographers.Add(new Photographer
            {
                User = user,
                Name = request.Name.Trim(),
                Slug = slug
            }
            );
            await db.SaveChangesAsync();
            return Created("", new TokenResponse(tokens.Create(user)));
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Database failed while registering a photographer.");
            return ServerError("Registration could not be saved. Check the database connection and schema.", exception);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected registration failure.");
            return ServerError("Registration failed unexpectedly.", exception);
        }
    }
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request)
    {
        try
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)) return Unauthorized(new
            {
                message = "Invalid email or password."
            }
            );
            return Ok(new TokenResponse(tokens.Create(user)));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Login failed for the submitted email address.");
            return ServerError("Login could not be completed. Check the database connection and schema.", exception);
        }
    }
    private ObjectResult ServerError(string title, Exception exception) => Problem(statusCode: StatusCodes.Status500InternalServerError, title: title, detail: environment.IsDevelopment() ? exception.GetBaseException().Message : null);
}
