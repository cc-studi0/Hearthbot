using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HearthBot.Cloud.Services.Learning;

public class MachineTokenService
{
    private readonly IConfiguration _config;
    private readonly LearningDbContext _db;

    public MachineTokenService(IConfiguration config, LearningDbContext db)
    {
        _config = config;
        _db = db;
    }

    public async Task<string> GenerateAsync(string machineId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(machineId))
            throw new ArgumentException("machineId required", nameof(machineId));

        var secret = _config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "HearthBot.Cloud",
            claims: new[]
            {
                new Claim("role", "machine"),
                new Claim("machine_id", machineId)
            },
            expires: DateTime.UtcNow.AddDays(365),
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenString)));
        var now = DateTime.UtcNow.ToString("o");
        var existing = await _db.Machines.FirstOrDefaultAsync(m => m.MachineId == machineId, ct);
        if (existing == null)
        {
            _db.Machines.Add(new Machine
            {
                MachineId = machineId,
                TokenHash = hash,
                CreatedAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            existing.TokenHash = hash;
            existing.LastSeenAt = now;
        }
        await _db.SaveChangesAsync(ct);

        return tokenString;
    }

    public async Task RevokeAsync(string machineId, CancellationToken ct = default)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.MachineId == machineId, ct);
        if (machine == null) return;
        machine.TokenHash = string.Empty;
        await _db.SaveChangesAsync(ct);
    }

    public static string? ExtractMachineId(ClaimsPrincipal user)
    {
        if (user.FindFirst("role")?.Value != "machine") return null;
        return user.FindFirst("machine_id")?.Value;
    }
}
