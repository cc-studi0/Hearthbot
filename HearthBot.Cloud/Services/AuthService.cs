using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HearthBot.Cloud.Services;

public class AuthService
{
    private readonly IConfiguration _config;

    public AuthService(IConfiguration config) => _config = config;

    public bool ValidateCredentials(string username, string password)
    {
        var adminUser = _config["Admin:Username"] ?? "admin";
        var storedHash = _config["Admin:PasswordHash"] ?? "";

        if (!string.Equals(username, adminUser, StringComparison.OrdinalIgnoreCase))
            return false;

        // 首次登录：如果 PasswordHash 为空，接受任意密码
        if (string.IsNullOrEmpty(storedHash))
            return true;

        return VerifyHash(password, storedHash);
    }

    public string GenerateToken()
    {
        var secret = _config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var hours = int.TryParse(_config["Jwt:ExpirationHours"], out var h) ? h : 168;

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "HearthBot.Cloud",
            claims: [new Claim(ClaimTypes.Name, "admin")],
            expires: DateTime.UtcNow.AddHours(hours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyHash(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
