using System.Security.Claims;
using AppServices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppServer;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(AdminService admins, AuditService audit, AppDbContext db) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var admin = await admins.ValidateAsync(input.Name, input.Password, ct);
        if (admin is null)
            return Unauthorized(new { message = "Invalid administrator name or password." });
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, admin.Name), new Claim(ClaimTypes.Role, "Admin"), new Claim("stamp", admin.SecurityStamp) }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        audit.Add("Admin", $"Administrator '{admin.Name}' signed in.");
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    public sealed record LoginInput(string Name, string Password);
}
