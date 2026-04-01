using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.Mvc;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    public record LoginRequest(string Username, string Password);
    public record LoginResponse(string Token);

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        if (!_auth.ValidateCredentials(req.Username, req.Password))
            return Unauthorized(new { error = "用户名或密码错误" });

        var token = _auth.GenerateToken();
        return Ok(new LoginResponse(token));
    }
}
