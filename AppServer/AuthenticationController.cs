using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppServer;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(EntraConfiguration configuration) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("config")]
    public IActionResult Configuration()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new {
            configuration.Enabled,
            configuration.ClientId,
            configuration.Authority,
            configuration.Scopes,
            RedirectUri = Request.PathBase + "/oauth/callback",
            LogoutRedirectUri = Request.PathBase + "/oauth/logout",
            configuration.RequireSignIn
        });
    }
}
