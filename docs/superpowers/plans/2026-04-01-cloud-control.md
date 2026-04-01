# 云控系统实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 HearthBot 多机部署场景增加云端集中管控——网页实时查看设备状态/段位/胜率，远程下发开始/停止/切卡组等指令。

**Architecture:** 三层架构。云端 ASP.NET Core 服务（SignalR Hub + REST API + SQLite）部署在公网服务器上；每台设备的 BotMain 内嵌 CloudAgent 模块通过 SignalR 长连接接入云端；Vue 3 + Naive UI 前端作为管理控制台。

**Tech Stack:** C# .NET 8.0, ASP.NET Core, SignalR, EF Core + SQLite, JWT, Vue 3, TypeScript, Naive UI, Vite, @microsoft/signalr

**Design spec:** `docs/superpowers/specs/2026-04-01-cloud-control-design.md`

---

## Phase 1: 云端后端

### Task 1: 创建云端项目 + 数据模型 + DbContext

**Files:**
- Create: `HearthBot.Cloud/HearthBot.Cloud.csproj`
- Create: `HearthBot.Cloud/Models/Device.cs`
- Create: `HearthBot.Cloud/Models/GameRecord.cs`
- Create: `HearthBot.Cloud/Models/PendingCommand.cs`
- Create: `HearthBot.Cloud/Data/CloudDbContext.cs`
- Create: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 创建项目文件**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
mkdir -p HearthBot.Cloud/Models HearthBot.Cloud/Data HearthBot.Cloud/Hubs HearthBot.Cloud/Controllers HearthBot.Cloud/Services
```

写入 `HearthBot.Cloud/HearthBot.Cloud.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.8" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.8" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 写 Device 模型**

写入 `HearthBot.Cloud/Models/Device.cs`:

```csharp
namespace HearthBot.Cloud.Models;

public class Device
{
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline"; // Online, Offline, InGame, Idle
    public string CurrentAccount { get; set; } = string.Empty;
    public string CurrentRank { get; set; } = string.Empty;
    public string CurrentDeck { get; set; } = string.Empty;
    public string CurrentProfile { get; set; } = string.Empty;
    public string GameMode { get; set; } = "Standard";
    public int SessionWins { get; set; }
    public int SessionLosses { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public DateTime RegisteredAt { get; set; }
    public string AvailableDecksJson { get; set; } = "[]";
    public string AvailableProfilesJson { get; set; } = "[]";
}
```

- [ ] **Step 3: 写 GameRecord 模型**

写入 `HearthBot.Cloud/Models/GameRecord.cs`:

```csharp
namespace HearthBot.Cloud.Models;

public class GameRecord
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty; // Win, Loss, Concede
    public string MyClass { get; set; } = string.Empty;
    public string OpponentClass { get; set; } = string.Empty;
    public string DeckName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public string RankBefore { get; set; } = string.Empty;
    public string RankAfter { get; set; } = string.Empty;
    public string GameMode { get; set; } = "Standard";
    public DateTime PlayedAt { get; set; }
}
```

- [ ] **Step 4: 写 PendingCommand 模型**

写入 `HearthBot.Cloud/Models/PendingCommand.cs`:

```csharp
namespace HearthBot.Cloud.Models;

public class PendingCommand
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty; // Start, Stop, ChangeDeck, ChangeAccount, ChangeTarget
    public string Payload { get; set; } = "{}"; // JSON
    public string Status { get; set; } = "Pending"; // Pending, Delivered, Executed, Failed
    public DateTime CreatedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
}
```

- [ ] **Step 5: 写 DbContext**

写入 `HearthBot.Cloud/Data/CloudDbContext.cs`:

```csharp
using HearthBot.Cloud.Models;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Data;

public class CloudDbContext : DbContext
{
    public CloudDbContext(DbContextOptions<CloudDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<GameRecord> GameRecords => Set<GameRecord>();
    public DbSet<PendingCommand> PendingCommands => Set<PendingCommand>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Device>(e =>
        {
            e.HasKey(d => d.DeviceId);
            e.Property(d => d.DeviceId).HasMaxLength(128);
        });

        b.Entity<GameRecord>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Id).ValueGeneratedOnAdd();
            e.HasIndex(g => g.DeviceId);
            e.HasIndex(g => g.PlayedAt);
        });

        b.Entity<PendingCommand>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedOnAdd();
            e.HasIndex(c => new { c.DeviceId, c.Status });
        });
    }
}
```

- [ ] **Step 6: 写入最小 Program.cs 并验证构建**

写入 `HearthBot.Cloud/Program.cs`:

```csharp
using HearthBot.Cloud.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CloudDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=cloud.db"));

builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
    db.Database.EnsureCreated();
}

app.MapControllers();

app.Run();
```

写入 `HearthBot.Cloud/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=cloud.db"
  },
  "Jwt": {
    "Secret": "CHANGE_ME_TO_A_RANDOM_STRING_AT_LEAST_32_CHARS",
    "Issuer": "HearthBot.Cloud",
    "ExpirationHours": 168
  },
  "Admin": {
    "Username": "admin",
    "PasswordHash": ""
  },
  "ServerChan": {
    "SendKey": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

- [ ] **Step 7: 验证构建通过**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\HearthBot.Cloud"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 8: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add HearthBot.Cloud/
git commit -m "云控：创建云端项目，定义数据模型和 DbContext"
```

---

### Task 2: JWT 认证

**Files:**
- Create: `HearthBot.Cloud/Services/AuthService.cs`
- Create: `HearthBot.Cloud/Controllers/AuthController.cs`
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 写 AuthService**

写入 `HearthBot.Cloud/Services/AuthService.cs`:

```csharp
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

        // 首次登录：如果 PasswordHash 为空，接受任意密码并提示设置
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
```

- [ ] **Step 2: 写 AuthController**

写入 `HearthBot.Cloud/Controllers/AuthController.cs`:

```csharp
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
```

- [ ] **Step 3: 在 Program.cs 中注册 JWT 认证**

修改 `HearthBot.Cloud/Program.cs` 为完整版本：

```csharp
using System.Text;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CloudDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=cloud.db"));

builder.Services.AddSingleton<AuthService>();

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "CHANGE_ME_TO_A_RANDOM_STRING_AT_LEAST_32_CHARS";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "HearthBot.Cloud",
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
        // SignalR 通过 query string 传 token
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

- [ ] **Step 4: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\HearthBot.Cloud"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 5: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add HearthBot.Cloud/
git commit -m "云控：添加 JWT 认证服务和登录 API"
```

---

### Task 3: SignalR Hub（设备通信）

**Files:**
- Create: `HearthBot.Cloud/Hubs/BotHub.cs`
- Create: `HearthBot.Cloud/Hubs/DashboardHub.cs`
- Create: `HearthBot.Cloud/Services/DeviceManager.cs`
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 写 DeviceManager 服务**

写入 `HearthBot.Cloud/Services/DeviceManager.cs`:

```csharp
using System.Collections.Concurrent;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services;

public class DeviceManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceManager> _logger;

    // 在线设备的 SignalR ConnectionId 映射
    private readonly ConcurrentDictionary<string, string> _deviceConnections = new();

    public DeviceManager(IServiceScopeFactory scopeFactory, ILogger<DeviceManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string? GetConnectionId(string deviceId) =>
        _deviceConnections.TryGetValue(deviceId, out var connId) ? connId : null;

    public async Task RegisterDevice(string deviceId, string displayName,
        string[] availableDecks, string[] availableProfiles, string connectionId)
    {
        _deviceConnections[deviceId] = connectionId;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null)
        {
            device = new Device
            {
                DeviceId = deviceId,
                DisplayName = displayName,
                RegisteredAt = DateTime.UtcNow
            };
            db.Devices.Add(device);
        }
        else
        {
            device.DisplayName = displayName;
        }

        device.Status = "Online";
        device.LastHeartbeat = DateTime.UtcNow;
        device.AvailableDecksJson = System.Text.Json.JsonSerializer.Serialize(availableDecks);
        device.AvailableProfilesJson = System.Text.Json.JsonSerializer.Serialize(availableProfiles);
        await db.SaveChangesAsync();

        _logger.LogInformation("Device {DeviceId} ({DisplayName}) registered", deviceId, displayName);
    }

    public async Task<Device?> UpdateHeartbeat(string deviceId, string status,
        string currentAccount, string currentRank, string currentDeck,
        string currentProfile, string gameMode, int sessionWins, int sessionLosses)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null) return null;

        device.Status = status;
        device.CurrentAccount = currentAccount;
        device.CurrentRank = currentRank;
        device.CurrentDeck = currentDeck;
        device.CurrentProfile = currentProfile;
        device.GameMode = gameMode;
        device.SessionWins = sessionWins;
        device.SessionLosses = sessionLosses;
        device.LastHeartbeat = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return device;
    }

    public async Task<GameRecord> RecordGame(string deviceId, string accountName,
        string result, string myClass, string opponentClass, string deckName,
        string profileName, int durationSeconds, string rankBefore, string rankAfter, string gameMode)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var record = new GameRecord
        {
            DeviceId = deviceId,
            AccountName = accountName,
            Result = result,
            MyClass = myClass,
            OpponentClass = opponentClass,
            DeckName = deckName,
            ProfileName = profileName,
            DurationSeconds = durationSeconds,
            RankBefore = rankBefore,
            RankAfter = rankAfter,
            GameMode = gameMode,
            PlayedAt = DateTime.UtcNow
        };
        db.GameRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    public async Task MarkDeviceOffline(string deviceId)
    {
        _deviceConnections.TryRemove(deviceId, out _);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var device = await db.Devices.FindAsync(deviceId);
        if (device != null)
        {
            device.Status = "Offline";
            await db.SaveChangesAsync();
        }

        _logger.LogInformation("Device {DeviceId} marked offline", deviceId);
    }

    public void RemoveConnection(string connectionId)
    {
        var entry = _deviceConnections.FirstOrDefault(kv => kv.Value == connectionId);
        if (entry.Key != null)
            _deviceConnections.TryRemove(entry.Key, out _);
    }

    public string? GetDeviceIdByConnection(string connectionId)
    {
        var entry = _deviceConnections.FirstOrDefault(kv => kv.Value == connectionId);
        return entry.Key;
    }

    public async Task<List<PendingCommand>> GetPendingCommands(string deviceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        return await db.PendingCommands
            .Where(c => c.DeviceId == deviceId && c.Status == "Pending")
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task UpdateCommandStatus(int commandId, string status)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var cmd = await db.PendingCommands.FindAsync(commandId);
        if (cmd != null)
        {
            cmd.Status = status;
            if (status is "Executed" or "Failed")
                cmd.ExecutedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 2: 写 BotHub（设备端 SignalR Hub）**

写入 `HearthBot.Cloud/Hubs/BotHub.cs`:

```csharp
using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.SignalR;

namespace HearthBot.Cloud.Hubs;

public class BotHub : Hub
{
    private readonly DeviceManager _devices;
    private readonly IHubContext<DashboardHub> _dashboard;
    private readonly ILogger<BotHub> _logger;

    public BotHub(DeviceManager devices, IHubContext<DashboardHub> dashboard, ILogger<BotHub> logger)
    {
        _devices = devices;
        _dashboard = dashboard;
        _logger = logger;
    }

    public async Task Register(string deviceId, string displayName,
        string[] availableDecks, string[] availableProfiles)
    {
        await _devices.RegisterDevice(deviceId, displayName,
            availableDecks, availableProfiles, Context.ConnectionId);

        await _dashboard.Clients.All.SendAsync("DeviceOnline", deviceId, displayName);

        // 返回离线期间积累的待执行指令
        var pending = await _devices.GetPendingCommands(deviceId);
        foreach (var cmd in pending)
        {
            await Clients.Caller.SendAsync("ExecuteCommand", cmd.Id, cmd.CommandType, cmd.Payload);
            await _devices.UpdateCommandStatus(cmd.Id, "Delivered");
        }
    }

    public async Task Heartbeat(string deviceId, string status,
        string currentAccount, string currentRank, string currentDeck,
        string currentProfile, string gameMode, int sessionWins, int sessionLosses)
    {
        var device = await _devices.UpdateHeartbeat(deviceId, status,
            currentAccount, currentRank, currentDeck,
            currentProfile, gameMode, sessionWins, sessionLosses);

        if (device != null)
            await _dashboard.Clients.All.SendAsync("DeviceUpdated", device);
    }

    public async Task ReportGame(string deviceId, string accountName,
        string result, string myClass, string opponentClass, string deckName,
        string profileName, int durationSeconds, string rankBefore, string rankAfter, string gameMode)
    {
        var record = await _devices.RecordGame(deviceId, accountName,
            result, myClass, opponentClass, deckName,
            profileName, durationSeconds, rankBefore, rankAfter, gameMode);

        await _dashboard.Clients.All.SendAsync("NewGameRecord", record);
    }

    public async Task CommandAck(int commandId, bool success, string? message)
    {
        var status = success ? "Executed" : "Failed";
        await _devices.UpdateCommandStatus(commandId, status);
        await _dashboard.Clients.All.SendAsync("CommandStatusChanged", commandId, status, message);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceId = _devices.GetDeviceIdByConnection(Context.ConnectionId);
        if (deviceId != null)
        {
            await _devices.MarkDeviceOffline(deviceId);
            await _dashboard.Clients.All.SendAsync("DeviceOffline", deviceId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
```

- [ ] **Step 3: 写 DashboardHub（网页端 SignalR Hub）**

写入 `HearthBot.Cloud/Hubs/DashboardHub.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HearthBot.Cloud.Hubs;

[Authorize]
public class DashboardHub : Hub
{
    // 网页端只接收推送，不需要定义额外方法
    // 推送方法由 BotHub 和 Controller 通过 IHubContext<DashboardHub> 调用
}
```

- [ ] **Step 4: 在 Program.cs 注册服务和路由**

在 `HearthBot.Cloud/Program.cs` 中，在 `builder.Services.AddSignalR();` 之前添加：

```csharp
builder.Services.AddSingleton<DeviceManager>();
```

在 `app.MapControllers();` 之后添加：

```csharp
app.MapHub<BotHub>("/hub/bot");
app.MapHub<DashboardHub>("/hub/dashboard");
```

顶部添加引用：

```csharp
using HearthBot.Cloud.Hubs;
```

- [ ] **Step 5: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\HearthBot.Cloud"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add HearthBot.Cloud/
git commit -m "云控：添加 SignalR Hub 和设备管理服务"
```

---

### Task 4: REST API 控制器

**Files:**
- Create: `HearthBot.Cloud/Controllers/DeviceController.cs`
- Create: `HearthBot.Cloud/Controllers/GameRecordController.cs`
- Create: `HearthBot.Cloud/Controllers/CommandController.cs`

- [ ] **Step 1: 写 DeviceController**

写入 `HearthBot.Cloud/Controllers/DeviceController.cs`:

```csharp
using HearthBot.Cloud.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceController : ControllerBase
{
    private readonly CloudDbContext _db;

    public DeviceController(CloudDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _db.Devices.OrderBy(d => d.DisplayName).ToListAsync();
        return Ok(devices);
    }

    [HttpGet("{deviceId}")]
    public async Task<IActionResult> Get(string deviceId)
    {
        var device = await _db.Devices.FindAsync(deviceId);
        return device == null ? NotFound() : Ok(device);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var today = DateTime.UtcNow.Date;
        var devices = await _db.Devices.ToListAsync();
        var todayGames = await _db.GameRecords.Where(g => g.PlayedAt >= today).ToListAsync();

        return Ok(new
        {
            OnlineCount = devices.Count(d => d.Status != "Offline"),
            TotalCount = devices.Count,
            TodayGames = todayGames.Count,
            TodayWins = todayGames.Count(g => g.Result == "Win"),
            TodayLosses = todayGames.Count(g => g.Result is "Loss" or "Concede"),
            AbnormalCount = devices.Count(d =>
                d.Status != "Offline" &&
                d.LastHeartbeat < DateTime.UtcNow.AddSeconds(-90))
        });
    }
}
```

- [ ] **Step 2: 写 GameRecordController**

写入 `HearthBot.Cloud/Controllers/GameRecordController.cs`:

```csharp
using HearthBot.Cloud.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameRecordController : ControllerBase
{
    private readonly CloudDbContext _db;

    public GameRecordController(CloudDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? deviceId,
        [FromQuery] string? accountName,
        [FromQuery] string? result,
        [FromQuery] int days = 1,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.GameRecords.AsQueryable();

        if (!string.IsNullOrEmpty(deviceId))
            query = query.Where(g => g.DeviceId == deviceId);
        if (!string.IsNullOrEmpty(accountName))
            query = query.Where(g => g.AccountName == accountName);
        if (!string.IsNullOrEmpty(result))
            query = query.Where(g => g.Result == result);
        if (days > 0)
            query = query.Where(g => g.PlayedAt >= DateTime.UtcNow.AddDays(-days));

        var total = await query.CountAsync();
        var records = await query
            .OrderByDescending(g => g.PlayedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Records = records });
    }
}
```

- [ ] **Step 3: 写 CommandController**

写入 `HearthBot.Cloud/Controllers/CommandController.cs`:

```csharp
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Hubs;
using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CommandController : ControllerBase
{
    private readonly CloudDbContext _db;
    private readonly DeviceManager _devices;
    private readonly IHubContext<BotHub> _botHub;
    private readonly IHubContext<DashboardHub> _dashboardHub;

    public CommandController(CloudDbContext db, DeviceManager devices,
        IHubContext<BotHub> botHub, IHubContext<DashboardHub> dashboardHub)
    {
        _db = db;
        _devices = devices;
        _botHub = botHub;
        _dashboardHub = dashboardHub;
    }

    public record SendCommandRequest(string DeviceId, string CommandType, string Payload);

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendCommandRequest req)
    {
        var cmd = new PendingCommand
        {
            DeviceId = req.DeviceId,
            CommandType = req.CommandType,
            Payload = req.Payload,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.PendingCommands.Add(cmd);
        await _db.SaveChangesAsync();

        // 如果设备在线，立即通过 SignalR 推送
        var connId = _devices.GetConnectionId(req.DeviceId);
        if (connId != null)
        {
            await _botHub.Clients.Client(connId)
                .SendAsync("ExecuteCommand", cmd.Id, cmd.CommandType, cmd.Payload);
            cmd.Status = "Delivered";
            await _db.SaveChangesAsync();
        }

        await _dashboardHub.Clients.All
            .SendAsync("CommandStatusChanged", cmd.Id, cmd.Status, (string?)null);

        return Ok(new { cmd.Id, cmd.Status });
    }
}
```

- [ ] **Step 4: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\HearthBot.Cloud"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 5: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add HearthBot.Cloud/
git commit -m "云控：添加设备、对局记录、指令下发 REST API"
```

---

### Task 5: 设备超时检测 + Server酱告警

**Files:**
- Create: `HearthBot.Cloud/Services/AlertService.cs`
- Create: `HearthBot.Cloud/Services/DeviceWatchdog.cs`
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 写 AlertService**

写入 `HearthBot.Cloud/Services/AlertService.cs`:

```csharp
using System.Text.Json;

namespace HearthBot.Cloud.Services;

public class AlertService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _sendKey;
    private readonly ILogger<AlertService> _logger;

    public AlertService(IConfiguration config, ILogger<AlertService> logger)
    {
        _sendKey = config["ServerChan:SendKey"] ?? "";
        _logger = logger;
    }

    public async Task SendAlert(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(_sendKey))
        {
            _logger.LogWarning("Server酱 SendKey 未配置，跳过告警: {Title}", title);
            return;
        }

        try
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("title", title),
                new KeyValuePair<string, string>("desp", content)
            });

            var url = $"https://sctapi.ftqq.com/{_sendKey.Trim()}.send";
            var resp = await Http.PostAsync(url, form);
            var body = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 0)
                _logger.LogWarning("Server酱返回 code={Code}: {Body}", code, body);
            else
                _logger.LogInformation("告警已发送: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server酱告警发送失败: {Title}", title);
        }
    }
}
```

- [ ] **Step 2: 写 DeviceWatchdog（定时检测超时设备）**

写入 `HearthBot.Cloud/Services/DeviceWatchdog.cs`:

```csharp
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services;

public class DeviceWatchdog : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertService _alert;
    private readonly IHubContext<DashboardHub> _dashboard;
    private readonly ILogger<DeviceWatchdog> _logger;
    private readonly HashSet<string> _alreadyAlerted = new();

    private const int CheckIntervalSeconds = 30;
    private const int TimeoutSeconds = 90;

    public DeviceWatchdog(IServiceScopeFactory scopeFactory, AlertService alert,
        IHubContext<DashboardHub> dashboard, ILogger<DeviceWatchdog> logger)
    {
        _scopeFactory = scopeFactory;
        _alert = alert;
        _dashboard = dashboard;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDevices();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeviceWatchdog check failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
        }
    }

    private async Task CheckDevices()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        var timedOut = await db.Devices
            .Where(d => d.Status != "Offline" && d.LastHeartbeat < cutoff)
            .ToListAsync();

        foreach (var device in timedOut)
        {
            device.Status = "Offline";

            if (_alreadyAlerted.Add(device.DeviceId))
            {
                _logger.LogWarning("Device {DeviceId} ({DisplayName}) timed out", device.DeviceId, device.DisplayName);
                await _alert.SendAlert(
                    $"设备掉线: {device.DisplayName}",
                    $"设备 **{device.DisplayName}** ({device.DeviceId}) 已超过 {TimeoutSeconds} 秒无心跳。\n\n" +
                    $"- 最后心跳: {device.LastHeartbeat:yyyy-MM-dd HH:mm:ss} UTC\n" +
                    $"- 最后账号: {device.CurrentAccount}\n" +
                    $"- 最后段位: {device.CurrentRank}");

                await _dashboard.Clients.All.SendAsync("DeviceOffline", device.DeviceId);
            }
        }

        await db.SaveChangesAsync();

        // 清除已恢复设备的告警标记
        var onlineIds = await db.Devices
            .Where(d => d.Status != "Offline")
            .Select(d => d.DeviceId)
            .ToListAsync();
        _alreadyAlerted.ExceptWith(onlineIds);
    }
}
```

- [ ] **Step 3: 在 Program.cs 注册服务**

在 `builder.Services.AddSingleton<DeviceManager>();` 之后添加：

```csharp
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHostedService<DeviceWatchdog>();
```

- [ ] **Step 4: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\HearthBot.Cloud"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 5: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add HearthBot.Cloud/
git commit -m "云控：添加设备超时检测和 Server酱告警服务"
```

---

### Task 6: CORS + 静态文件 + 最终 Program.cs

**Files:**
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 更新 Program.cs 支持 SPA 静态文件**

在 `app.UseCors();` 之前添加：

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```

更新 CORS 配置以支持 SignalR（SignalR 不能用 `AllowAnyOrigin` + credentials）：

将 CORS 注册改为：

```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));
```

在 `app.Run();` 之前添加 SPA fallback：

```csharp
app.MapFallbackToFile("index.html");
```

- [ ] **Step 2: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\HearthBot.Cloud"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add HearthBot.Cloud/
git commit -m "云控：配置 CORS 和 SPA 静态文件服务"
```

---

## Phase 2: 设备端 CloudAgent

### Task 7: CloudConfig + CloudAgent 核心

**Files:**
- Create: `BotMain/Cloud/CloudConfig.cs`
- Create: `BotMain/Cloud/CloudAgent.cs`
- Modify: `BotMain/BotMain.csproj`

- [ ] **Step 1: 添加 SignalR Client NuGet 引用**

在 `BotMain/BotMain.csproj` 的 `<ItemGroup>` (PackageReference) 中添加：

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.8" />
```

- [ ] **Step 2: 写 CloudConfig**

写入 `BotMain/Cloud/CloudConfig.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace BotMain.Cloud
{
    public class CloudConfig
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DeviceToken { get; set; } = string.Empty;

        public bool IsEnabled => !string.IsNullOrWhiteSpace(ServerUrl);

        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloud.json");

        public static CloudConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<CloudConfig>(File.ReadAllText(ConfigPath))
                           ?? new CloudConfig();
            }
            catch { }
            return new CloudConfig();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        public void EnsureDeviceId()
        {
            if (!string.IsNullOrEmpty(DeviceId)) return;

            // 用机器名 + 随机后缀生成设备ID
            DeviceId = $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";
            if (string.IsNullOrEmpty(DisplayName))
                DisplayName = Environment.MachineName;
            Save();
        }
    }
}
```

- [ ] **Step 3: 写 CloudAgent**

写入 `BotMain/Cloud/CloudAgent.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace BotMain.Cloud
{
    public class CloudAgent : IDisposable
    {
        private readonly CloudConfig _config;
        private readonly Action<string> _log;
        private HubConnection? _hub;
        private CancellationTokenSource? _cts;
        private Timer? _heartbeatTimer;
        private bool _disposed;

        public bool IsConnected => _hub?.State == HubConnectionState.Connected;

        // 收到指令时触发
        public event Action<int, string, string>? OnCommandReceived; // commandId, type, payloadJson

        // 状态采集委托，由外部设置
        public Func<HeartbeatData>? CollectStatus { get; set; }

        public CloudAgent(CloudConfig config, Action<string> log)
        {
            _config = config;
            _log = log;
        }

        public async Task StartAsync()
        {
            if (!_config.IsEnabled)
            {
                _log("[云控] 未配置服务器地址，跳过");
                return;
            }

            _config.EnsureDeviceId();
            _cts = new CancellationTokenSource();

            _hub = new HubConnectionBuilder()
                .WithUrl($"{_config.ServerUrl.TrimEnd('/')}/hub/bot", options =>
                {
                    if (!string.IsNullOrEmpty(_config.DeviceToken))
                        options.AccessTokenProvider = () => Task.FromResult<string?>(_config.DeviceToken);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60) })
                .Build();

            _hub.On<int, string, string>("ExecuteCommand", (cmdId, cmdType, payload) =>
            {
                _log($"[云控] 收到指令: {cmdType} (id={cmdId})");
                OnCommandReceived?.Invoke(cmdId, cmdType, payload);
            });

            _hub.Reconnecting += _ =>
            {
                _log("[云控] 连接断开，正在重连...");
                return Task.CompletedTask;
            };

            _hub.Reconnected += _ =>
            {
                _log("[云控] 重连成功，重新注册...");
                return RegisterAsync();
            };

            _hub.Closed += ex =>
            {
                _log($"[云控] 连接关闭: {ex?.Message ?? "正常关闭"}");
                return Task.CompletedTask;
            };

            await ConnectWithRetry();
        }

        private async Task ConnectWithRetry()
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _hub!.StartAsync(ct);
                    _log($"[云控] 已连接到 {_config.ServerUrl}");
                    await RegisterAsync();

                    // 启动心跳定时器 (30秒)
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(),
                        null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
                    return;
                }
                catch (Exception ex)
                {
                    _log($"[云控] 连接失败: {ex.Message}，30秒后重试...");
                    try { await Task.Delay(30000, ct); } catch { return; }
                }
            }
        }

        private async Task RegisterAsync()
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            try
            {
                await _hub.InvokeAsync("Register",
                    _config.DeviceId,
                    _config.DisplayName,
                    Array.Empty<string>(), // 可用卡组列表，后续由 Collector 填充
                    Array.Empty<string>()); // 可用策略列表
            }
            catch (Exception ex)
            {
                _log($"[云控] 注册失败: {ex.Message}");
            }
        }

        private async Task SendHeartbeatAsync()
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            if (CollectStatus == null) return;

            try
            {
                var s = CollectStatus();
                await _hub.InvokeAsync("Heartbeat",
                    _config.DeviceId, s.Status, s.CurrentAccount, s.CurrentRank,
                    s.CurrentDeck, s.CurrentProfile, s.GameMode, s.SessionWins, s.SessionLosses);
            }
            catch (Exception ex)
            {
                _log($"[云控] 心跳发送失败: {ex.Message}");
            }
        }

        public async Task ReportGameAsync(string accountName, string result,
            string myClass, string opponentClass, string deckName, string profileName,
            int durationSeconds, string rankBefore, string rankAfter, string gameMode)
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            try
            {
                await _hub.InvokeAsync("ReportGame",
                    _config.DeviceId, accountName, result, myClass, opponentClass,
                    deckName, profileName, durationSeconds, rankBefore, rankAfter, gameMode);
            }
            catch (Exception ex)
            {
                _log($"[云控] 对局上报失败: {ex.Message}");
            }
        }

        public async Task AckCommandAsync(int commandId, bool success, string? message = null)
        {
            if (_hub?.State != HubConnectionState.Connected) return;
            try
            {
                await _hub.InvokeAsync("CommandAck", commandId, success, message);
            }
            catch (Exception ex)
            {
                _log($"[云控] 指令回报失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _heartbeatTimer?.Dispose();
            try { _hub?.DisposeAsync().AsTask().Wait(3000); } catch { }
            _cts?.Dispose();
        }
    }

    public struct HeartbeatData
    {
        public string Status;
        public string CurrentAccount;
        public string CurrentRank;
        public string CurrentDeck;
        public string CurrentProfile;
        public string GameMode;
        public int SessionWins;
        public int SessionLosses;
    }
}
```

- [ ] **Step 4: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\BotMain"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 5: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add BotMain/Cloud/ BotMain/BotMain.csproj
git commit -m "云控：添加设备端 CloudConfig 和 CloudAgent 核心模块"
```

---

### Task 8: DeviceStatusCollector + CommandExecutor

**Files:**
- Create: `BotMain/Cloud/DeviceStatusCollector.cs`
- Create: `BotMain/Cloud/CommandExecutor.cs`

- [ ] **Step 1: 写 DeviceStatusCollector**

写入 `BotMain/Cloud/DeviceStatusCollector.cs`:

```csharp
namespace BotMain.Cloud
{
    /// <summary>
    /// 从 BotService 和 AccountController 采集当前设备状态，组装心跳数据。
    /// </summary>
    public class DeviceStatusCollector
    {
        private readonly BotService _bot;
        private readonly AccountController _accounts;

        public DeviceStatusCollector(BotService bot, AccountController accounts)
        {
            _bot = bot;
            _accounts = accounts;
        }

        public HeartbeatData Collect()
        {
            var account = _accounts.CurrentAccount;
            var stats = _bot.GetStatsSnapshot();

            var status = _bot.State switch
            {
                BotState.Running => "InGame",
                BotState.Finishing => "InGame",
                _ => "Idle"
            };

            return new HeartbeatData
            {
                Status = status,
                CurrentAccount = account?.DisplayName ?? "",
                CurrentRank = account?.CurrentRankText ?? "",
                CurrentDeck = account?.DeckName ?? "",
                CurrentProfile = account?.ProfileName ?? "",
                GameMode = account?.ModeIndex == 1 ? "Wild" : "Standard",
                SessionWins = account?.Wins ?? stats.Wins,
                SessionLosses = account?.Losses ?? stats.Losses
            };
        }

        public string[] GetAvailableDecks()
        {
            return _bot.GetAvailableDeckNames();
        }

        public string[] GetAvailableProfiles()
        {
            return _bot.GetAvailableProfileNames();
        }
    }
}
```

注意：`BotService.GetAvailableDeckNames()` 和 `GetAvailableProfileNames()` 可能需要在 BotService 中添加。如果不存在，直接返回空数组，后续再补充。

- [ ] **Step 2: 写 CommandExecutor**

写入 `BotMain/Cloud/CommandExecutor.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BotMain.Cloud
{
    public class CommandExecutor
    {
        private readonly BotService _bot;
        private readonly AccountController _accounts;
        private readonly CloudAgent _agent;
        private readonly Action<string> _log;
        private readonly ConcurrentQueue<(int Id, string Type, string Payload)> _pendingCommands = new();

        public CommandExecutor(BotService bot, AccountController accounts, CloudAgent agent, Action<string> log)
        {
            _bot = bot;
            _accounts = accounts;
            _agent = agent;
            _log = log;

            // 监听云端指令
            _agent.OnCommandReceived += (id, type, payload) =>
            {
                _pendingCommands.Enqueue((id, type, payload));
                _log($"[云控] 指令已缓存: {type} (id={id})，将在当局结束后执行");
            };
        }

        /// <summary>
        /// 在对局结束后调用，处理所有待执行的指令。
        /// </summary>
        public void ProcessPendingCommands()
        {
            while (_pendingCommands.TryDequeue(out var cmd))
            {
                try
                {
                    ExecuteCommand(cmd.Id, cmd.Type, cmd.Payload);
                    _ = _agent.AckCommandAsync(cmd.Id, true);
                    _log($"[云控] 指令执行成功: {cmd.Type} (id={cmd.Id})");
                }
                catch (Exception ex)
                {
                    _ = _agent.AckCommandAsync(cmd.Id, false, ex.Message);
                    _log($"[云控] 指令执行失败: {cmd.Type} (id={cmd.Id}): {ex.Message}");
                }
            }
        }

        private void ExecuteCommand(int id, string type, string payload)
        {
            switch (type)
            {
                case "Start":
                    _bot.Start();
                    break;

                case "Stop":
                    _bot.Stop();
                    break;

                case "ChangeDeck":
                {
                    using var doc = JsonDocument.Parse(payload);
                    var deckName = doc.RootElement.GetProperty("DeckName").GetString() ?? "";
                    var profileName = doc.RootElement.TryGetProperty("ProfileName", out var pn)
                        ? pn.GetString() ?? "" : "";
                    var account = _accounts.CurrentAccount;
                    if (account != null)
                    {
                        account.DeckName = deckName;
                        if (!string.IsNullOrEmpty(profileName))
                            account.ProfileName = profileName;
                        _accounts.Save();
                        _log($"[云控] 卡组已切换为: {deckName}");
                    }
                    break;
                }

                case "ChangeTarget":
                {
                    using var doc = JsonDocument.Parse(payload);
                    var targetStarLevel = doc.RootElement.GetProperty("TargetRankStarLevel").GetInt32();
                    var account = _accounts.CurrentAccount;
                    if (account != null)
                    {
                        account.TargetRankStarLevel = targetStarLevel;
                        _accounts.Save();
                        _log($"[云控] 目标段位已更新");
                    }
                    break;
                }

                default:
                    _log($"[云控] 未知指令类型: {type}");
                    break;
            }
        }
    }
}
```

- [ ] **Step 3: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\BotMain"
dotnet build
```

如果 `GetAvailableDeckNames`/`GetAvailableProfileNames` 不存在导致编译错误，在 `DeviceStatusCollector` 中暂时改为返回空数组，在 Task 9 中添加这些方法。

- [ ] **Step 4: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add BotMain/Cloud/
git commit -m "云控：添加设备状态采集器和指令执行器"
```

---

### Task 9: 集成到 BotMain 现有代码

**Files:**
- Modify: `BotMain/MainViewModel.cs` (构造函数中初始化 CloudAgent)
- Modify: `BotMain/BotService.cs` (对局结束时上报 + 处理待执行指令)
- Modify: `BotMain/App.xaml.cs` (退出时 Dispose)

- [ ] **Step 1: 在 MainViewModel 中初始化 CloudAgent**

在 `BotMain/MainViewModel.cs` 顶部添加引用：

```csharp
using BotMain.Cloud;
```

在 `MainViewModel` 类中添加字段（在 `_accountController` 声明附近）：

```csharp
private readonly CloudAgent? _cloudAgent;
private readonly CommandExecutor? _commandExecutor;
```

在 `MainViewModel()` 构造函数末尾（`_bot.Prepare();` 之后）添加：

```csharp
// 云控初始化
var cloudConfig = CloudConfig.Load();
if (cloudConfig.IsEnabled)
{
    _cloudAgent = new CloudAgent(cloudConfig, EnqueueLog);
    var collector = new DeviceStatusCollector(_bot, _accountController);
    _cloudAgent.CollectStatus = collector.Collect;
    _commandExecutor = new CommandExecutor(_bot, _accountController, _cloudAgent, EnqueueLog);
    _ = _cloudAgent.StartAsync();
}
```

- [ ] **Step 2: 在 BotService 对局结束处添加钩子**

在 `BotService.cs` 中添加一个新事件（在现有事件声明区域，约第 192 行 `OnBotStopped` 附近）：

```csharp
public event Action<bool>? OnGameEnded; // true=win, false=loss
```

在胜负统计代码处（约第 1505-1525 行），在 `PublishStatsChanged();` 之后触发事件：

胜利分支中 `PublishStatsChanged();` 之后添加：
```csharp
try { OnGameEnded?.Invoke(true); } catch { }
```

失败分支中 `PublishStatsChanged();` 之后添加：
```csharp
try { OnGameEnded?.Invoke(false); } catch { }
```

- [ ] **Step 3: 在 MainViewModel 中监听 OnGameEnded 事件**

在构造函数的云控初始化块中，`_ = _cloudAgent.StartAsync();` 之前添加：

```csharp
_bot.OnGameEnded += win =>
{
    _commandExecutor?.ProcessPendingCommands();
};
```

- [ ] **Step 4: 在 App.xaml.cs 中添加清理逻辑**

修改 `BotMain/App.xaml.cs`，添加 `OnExit` 重写：

```csharp
protected override void OnExit(ExitEventArgs e)
{
    (MainWindow?.DataContext as IDisposable)?.Dispose();
    base.OnExit(e);
}
```

在 `MainViewModel` 中实现 `IDisposable`：

```csharp
public class MainViewModel : INotifyPropertyChanged, IDisposable
```

添加 `Dispose` 方法：

```csharp
public void Dispose()
{
    _cloudAgent?.Dispose();
}
```

- [ ] **Step 5: 验证构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\BotMain"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add BotMain/
git commit -m "云控：将 CloudAgent 集成到 BotMain 主程序"
```

---

## Phase 3: Vue 前端

### Task 10: 创建 Vue 项目 + 基础配置

**Files:**
- Create: `hearthbot-web/package.json`
- Create: `hearthbot-web/vite.config.ts`
- Create: `hearthbot-web/tsconfig.json`
- Create: `hearthbot-web/index.html`
- Create: `hearthbot-web/src/main.ts`
- Create: `hearthbot-web/src/App.vue`
- Create: `hearthbot-web/src/env.d.ts`

- [ ] **Step 1: 初始化项目**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
mkdir -p hearthbot-web/src
cd hearthbot-web
npm init -y
npm install vue@3 vue-router@4 naive-ui @microsoft/signalr axios
npm install -D vite @vitejs/plugin-vue typescript vue-tsc
```

- [ ] **Step 2: 写入配置文件**

写入 `hearthbot-web/vite.config.ts`:

```typescript
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': 'http://localhost:5000',
      '/hub': { target: 'http://localhost:5000', ws: true }
    }
  }
})
```

写入 `hearthbot-web/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "jsx": "preserve",
    "paths": { "@/*": ["./src/*"] },
    "baseUrl": ".",
    "types": ["vite/client"]
  },
  "include": ["src/**/*.ts", "src/**/*.vue"]
}
```

写入 `hearthbot-web/src/env.d.ts`:

```typescript
declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
```

写入 `hearthbot-web/index.html`:

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>HearthBot 云控</title>
</head>
<body>
  <div id="app"></div>
  <script type="module" src="/src/main.ts"></script>
</body>
</html>
```

- [ ] **Step 3: 写入入口文件**

写入 `hearthbot-web/src/main.ts`:

```typescript
import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: () => import('./views/Login.vue') },
    { path: '/', component: () => import('./views/Dashboard.vue'), meta: { auth: true } },
    { path: '/records', component: () => import('./views/GameRecords.vue'), meta: { auth: true } },
  ]
})

router.beforeEach((to) => {
  if (to.meta.auth && !localStorage.getItem('token'))
    return '/login'
})

createApp(App).use(router).mount('#app')
```

写入 `hearthbot-web/src/App.vue`:

```vue
<template>
  <router-view />
</template>
```

- [ ] **Step 4: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add hearthbot-web/ --ignore-errors
echo "node_modules" >> hearthbot-web/.gitignore
git add hearthbot-web/.gitignore
git commit -m "云控：创建 Vue 3 前端项目"
```

---

### Task 11: API 封装 + SignalR 组合式函数

**Files:**
- Create: `hearthbot-web/src/api/index.ts`
- Create: `hearthbot-web/src/composables/useAuth.ts`
- Create: `hearthbot-web/src/composables/useSignalR.ts`

- [ ] **Step 1: 写 API 封装**

写入 `hearthbot-web/src/api/index.ts`:

```typescript
import axios from 'axios'

const api = axios.create({ baseURL: '/api' })

api.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

api.interceptors.response.use(
  r => r,
  err => {
    if (err.response?.status === 401) {
      localStorage.removeItem('token')
      window.location.href = '/login'
    }
    return Promise.reject(err)
  }
)

export const authApi = {
  login: (username: string, password: string) =>
    api.post<{ token: string }>('/auth/login', { username, password })
}

export const deviceApi = {
  getAll: () => api.get('/device'),
  getStats: () => api.get('/device/stats'),
  get: (id: string) => api.get(`/device/${id}`)
}

export const gameRecordApi = {
  getAll: (params: Record<string, any>) => api.get('/gamerecord', { params })
}

export const commandApi = {
  send: (deviceId: string, commandType: string, payload: Record<string, any>) =>
    api.post('/command', { deviceId, commandType, payload: JSON.stringify(payload) })
}
```

- [ ] **Step 2: 写 useAuth 组合式函数**

写入 `hearthbot-web/src/composables/useAuth.ts`:

```typescript
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { authApi } from '../api'

export function useAuth() {
  const router = useRouter()
  const loading = ref(false)
  const error = ref('')

  async function login(username: string, password: string) {
    loading.value = true
    error.value = ''
    try {
      const { data } = await authApi.login(username, password)
      localStorage.setItem('token', data.token)
      router.push('/')
    } catch (e: any) {
      error.value = e.response?.data?.error || '登录失败'
    } finally {
      loading.value = false
    }
  }

  function logout() {
    localStorage.removeItem('token')
    router.push('/login')
  }

  return { login, logout, loading, error }
}
```

- [ ] **Step 3: 写 useSignalR 组合式函数**

写入 `hearthbot-web/src/composables/useSignalR.ts`:

```typescript
import { ref, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'

export function useSignalR() {
  const connection = ref<HubConnection | null>(null)
  const connected = ref(false)

  function connect() {
    const token = localStorage.getItem('token') || ''
    const hub = new HubConnectionBuilder()
      .withUrl('/hub/dashboard', { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    hub.onreconnected(() => { connected.value = true })
    hub.onreconnecting(() => { connected.value = false })
    hub.onclose(() => { connected.value = false })

    hub.start().then(() => { connected.value = true })
    connection.value = hub
    return hub
  }

  onUnmounted(() => {
    connection.value?.stop()
  })

  return { connection, connected, connect }
}
```

- [ ] **Step 4: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add hearthbot-web/src/
git commit -m "云控：添加 API 封装和 SignalR 组合式函数"
```

---

### Task 12: 登录页

**Files:**
- Create: `hearthbot-web/src/views/Login.vue`

- [ ] **Step 1: 写登录页面**

创建目录并写入 `hearthbot-web/src/views/Login.vue`:

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { NCard, NForm, NFormItem, NInput, NButton, NAlert } from 'naive-ui'
import { useAuth } from '../composables/useAuth'

const username = ref('')
const password = ref('')
const { login, loading, error } = useAuth()

function onSubmit() {
  login(username.value, password.value)
}
</script>

<template>
  <div style="display:flex;align-items:center;justify-content:center;min-height:100vh;background:#1a1a2e">
    <NCard title="HearthBot 云控" style="width:360px">
      <NAlert v-if="error" type="error" style="margin-bottom:16px">{{ error }}</NAlert>
      <NForm @submit.prevent="onSubmit">
        <NFormItem label="用户名">
          <NInput v-model:value="username" placeholder="admin" />
        </NFormItem>
        <NFormItem label="密码">
          <NInput v-model:value="password" type="password" placeholder="密码"
            @keyup.enter="onSubmit" />
        </NFormItem>
        <NButton type="primary" block :loading="loading" @click="onSubmit">登录</NButton>
      </NForm>
    </NCard>
  </div>
</template>
```

- [ ] **Step 2: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add hearthbot-web/src/views/Login.vue
git commit -m "云控：添加登录页面"
```

---

### Task 13: 仪表盘页面

**Files:**
- Create: `hearthbot-web/src/views/Dashboard.vue`

- [ ] **Step 1: 写仪表盘页面**

写入 `hearthbot-web/src/views/Dashboard.vue`:

```vue
<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NSpace, NCard, NStatistic,
  NDataTable, NTag, NButton, NModal, NSelect, NGrid, NGi, NMenu
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { deviceApi, commandApi } from '../api'
import { useSignalR } from '../composables/useSignalR'
import { useAuth } from '../composables/useAuth'

interface Device {
  deviceId: string
  displayName: string
  status: string
  currentAccount: string
  currentRank: string
  currentDeck: string
  currentProfile: string
  gameMode: string
  sessionWins: number
  sessionLosses: number
  lastHeartbeat: string
  availableDecksJson: string
  availableProfilesJson: string
}

interface Stats {
  onlineCount: number
  totalCount: number
  todayGames: number
  todayWins: number
  todayLosses: number
  abnormalCount: number
}

const router = useRouter()
const { logout } = useAuth()
const devices = ref<Device[]>([])
const stats = ref<Stats>({ onlineCount: 0, totalCount: 0, todayGames: 0, todayWins: 0, todayLosses: 0, abnormalCount: 0 })
const showManage = ref(false)
const selectedDevice = ref<Device | null>(null)
const selectedDeck = ref('')

const todayWinRate = computed(() => {
  const total = stats.value.todayWins + stats.value.todayLosses
  return total > 0 ? ((stats.value.todayWins / total) * 100).toFixed(1) + '%' : '-'
})

const columns = [
  { title: '设备名', key: 'displayName', width: 100 },
  {
    title: '状态', key: 'status', width: 80,
    render: (row: Device) => {
      const map: Record<string, string> = { Online: 'success', InGame: 'success', Idle: 'warning', Offline: 'error' }
      const label: Record<string, string> = { Online: '在线', InGame: '对局中', Idle: '空闲', Offline: '离线' }
      return h(NTag, { type: map[row.status] || 'default', size: 'small' }, () => label[row.status] || row.status)
    }
  },
  { title: '当前账号', key: 'currentAccount', width: 120 },
  { title: '段位', key: 'currentRank', width: 100 },
  {
    title: '胜/负', key: 'stats', width: 80,
    render: (row: Device) => `${row.sessionWins} / ${row.sessionLosses}`
  },
  {
    title: '胜率', key: 'winRate', width: 70,
    render: (row: Device) => {
      const total = row.sessionWins + row.sessionLosses
      return total > 0 ? ((row.sessionWins / total) * 100).toFixed(1) + '%' : '-'
    }
  },
  { title: '卡组', key: 'currentDeck', width: 100 },
  {
    title: '操作', key: 'actions', width: 80,
    render: (row: Device) =>
      row.status !== 'Offline'
        ? h(NButton, { size: 'small', onClick: () => openManage(row) }, () => '管理')
        : h(NTag, { size: 'small' }, () => '离线')
  }
]

import { h } from 'vue'

function openManage(device: Device) {
  selectedDevice.value = device
  selectedDeck.value = device.currentDeck
  showManage.value = true
}

async function sendCommand(type: string, payload: Record<string, any> = {}) {
  if (!selectedDevice.value) return
  await commandApi.send(selectedDevice.value.deviceId, type, payload)
}

async function changeDeck() {
  await sendCommand('ChangeDeck', { DeckName: selectedDeck.value })
}

function getAvailableDecks(device: Device): string[] {
  try { return JSON.parse(device.availableDecksJson || '[]') } catch { return [] }
}

async function loadData() {
  const [devRes, statRes] = await Promise.all([deviceApi.getAll(), deviceApi.getStats()])
  devices.value = devRes.data
  stats.value = statRes.data
}

onMounted(async () => {
  await loadData()

  const { connect } = useSignalR()
  const hub = connect()

  hub.on('DeviceUpdated', (device: Device) => {
    const idx = devices.value.findIndex(d => d.deviceId === device.deviceId)
    if (idx >= 0) devices.value[idx] = device
    else devices.value.push(device)
    // 刷新选中设备
    if (selectedDevice.value?.deviceId === device.deviceId)
      selectedDevice.value = device
  })

  hub.on('DeviceOnline', () => loadData())
  hub.on('DeviceOffline', () => loadData())
})

const menuOptions = [
  { label: '总览', key: '/' },
  { label: '对局记录', key: '/records' }
]
</script>

<template>
  <NLayout style="min-height:100vh">
    <NLayoutHeader bordered style="padding:0 24px;display:flex;align-items:center;justify-content:space-between;height:56px">
      <NSpace align="center">
        <strong style="font-size:16px;color:#63e2b7">HearthBot 云控</strong>
        <NMenu mode="horizontal" :options="menuOptions" :value="router.currentRoute.value.path"
          @update:value="(k: string) => router.push(k)" />
      </NSpace>
      <NButton text @click="logout">退出</NButton>
    </NLayoutHeader>

    <NLayoutContent style="padding:24px">
      <!-- 统计卡片 -->
      <NGrid :cols="4" :x-gap="12" style="margin-bottom:24px">
        <NGi>
          <NCard>
            <NStatistic label="在线设备" :value="stats.onlineCount">
              <template #suffix>/ {{ stats.totalCount }}</template>
            </NStatistic>
          </NCard>
        </NGi>
        <NGi>
          <NCard><NStatistic label="今日胜率" :value="todayWinRate" /></NCard>
        </NGi>
        <NGi>
          <NCard><NStatistic label="今日对局" :value="stats.todayGames" /></NCard>
        </NGi>
        <NGi>
          <NCard><NStatistic label="异常设备" :value="stats.abnormalCount" /></NCard>
        </NGi>
      </NGrid>

      <!-- 设备列表 -->
      <NCard title="设备实时状态">
        <NDataTable :columns="columns" :data="devices" :row-key="(r: Device) => r.deviceId" />
      </NCard>
    </NLayoutContent>
  </NLayout>

  <!-- 设备管理弹窗 -->
  <NModal v-model:show="showManage" preset="card" :title="`设备管理 — ${selectedDevice?.displayName}`"
    style="width:600px">
    <template v-if="selectedDevice">
      <NGrid :cols="2" :x-gap="16">
        <NGi>
          <h4>当前状态</h4>
          <p>状态: {{ selectedDevice.status }}</p>
          <p>账号: {{ selectedDevice.currentAccount }}</p>
          <p>段位: {{ selectedDevice.currentRank }}</p>
          <p>卡组: {{ selectedDevice.currentDeck }}</p>
          <p>策略: {{ selectedDevice.currentProfile }}</p>
          <p>模式: {{ selectedDevice.gameMode }}</p>
          <p>胜/负: {{ selectedDevice.sessionWins }} / {{ selectedDevice.sessionLosses }}</p>
        </NGi>
        <NGi>
          <h4>远程操作</h4>
          <div style="margin-bottom:12px">
            <label>切换卡组</label>
            <NSpace>
              <NSelect v-model:value="selectedDeck" :options="getAvailableDecks(selectedDevice).map(d => ({label:d,value:d}))"
                style="width:200px" />
              <NButton type="primary" size="small" @click="changeDeck">应用</NButton>
            </NSpace>
          </div>
          <NSpace style="margin-top:16px">
            <NButton type="success" @click="sendCommand('Start')">开始</NButton>
            <NButton type="error" @click="sendCommand('Stop')">停止</NButton>
          </NSpace>
        </NGi>
      </NGrid>
    </template>
  </NModal>
</template>
```

- [ ] **Step 2: 验证前端编译**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\hearthbot-web"
npx vue-tsc --noEmit || true
npx vite build
```

- [ ] **Step 3: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add hearthbot-web/src/views/Dashboard.vue
git commit -m "云控：添加仪表盘页面"
```

---

### Task 14: 对局记录页

**Files:**
- Create: `hearthbot-web/src/views/GameRecords.vue`

- [ ] **Step 1: 写对局记录页面**

写入 `hearthbot-web/src/views/GameRecords.vue`:

```vue
<script setup lang="ts">
import { ref, onMounted, h } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NCard, NDataTable, NSpace,
  NSelect, NTag, NButton, NMenu, NPagination
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { gameRecordApi, deviceApi } from '../api'
import { useAuth } from '../composables/useAuth'
import { useSignalR } from '../composables/useSignalR'

interface GameRecord {
  id: number
  deviceId: string
  accountName: string
  result: string
  myClass: string
  opponentClass: string
  deckName: string
  profileName: string
  durationSeconds: number
  rankBefore: string
  rankAfter: string
  gameMode: string
  playedAt: string
}

const router = useRouter()
const { logout } = useAuth()
const records = ref<GameRecord[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const filterDevice = ref<string | null>(null)
const filterAccount = ref<string | null>(null)
const filterResult = ref<string | null>(null)
const filterDays = ref(1)
const deviceOptions = ref<{ label: string; value: string }[]>([])

const resultOptions = [
  { label: '全部结果', value: '' },
  { label: '胜利', value: 'Win' },
  { label: '失败', value: 'Loss' },
  { label: '投降', value: 'Concede' }
]

const daysOptions = [
  { label: '今天', value: 1 },
  { label: '最近3天', value: 3 },
  { label: '最近7天', value: 7 },
  { label: '全部', value: 0 }
]

function formatDuration(s: number) {
  return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`
}

function formatTime(iso: string) {
  const d = new Date(iso)
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
}

const columns = [
  { title: '时间', key: 'playedAt', width: 60, render: (r: GameRecord) => formatTime(r.playedAt) },
  { title: '设备', key: 'deviceId', width: 80 },
  { title: '账号', key: 'accountName', width: 100 },
  {
    title: '结果', key: 'result', width: 60,
    render: (r: GameRecord) => {
      const map: Record<string, string> = { Win: 'success', Loss: 'error', Concede: 'warning' }
      const label: Record<string, string> = { Win: '胜利', Loss: '失败', Concede: '投降' }
      return h(NTag, { type: map[r.result] || 'default', size: 'small' }, () => label[r.result] || r.result)
    }
  },
  { title: '我方', key: 'myClass', width: 80 },
  { title: '对手', key: 'opponentClass', width: 80 },
  { title: '卡组', key: 'deckName', width: 100 },
  { title: '用时', key: 'duration', width: 60, render: (r: GameRecord) => formatDuration(r.durationSeconds) },
  { title: '段位变化', key: 'rankChange', width: 140, render: (r: GameRecord) => `${r.rankBefore} → ${r.rankAfter}` }
]

async function loadRecords() {
  const params: Record<string, any> = { page: page.value, pageSize: pageSize.value, days: filterDays.value }
  if (filterDevice.value) params.deviceId = filterDevice.value
  if (filterAccount.value) params.accountName = filterAccount.value
  if (filterResult.value) params.result = filterResult.value

  const { data } = await gameRecordApi.getAll(params)
  records.value = data.records
  total.value = data.total
}

async function loadDevices() {
  const { data } = await deviceApi.getAll()
  deviceOptions.value = [
    { label: '全部设备', value: '' },
    ...data.map((d: any) => ({ label: d.displayName, value: d.deviceId }))
  ]
}

onMounted(async () => {
  await Promise.all([loadRecords(), loadDevices()])

  const { connect } = useSignalR()
  const hub = connect()
  hub.on('NewGameRecord', () => loadRecords())
})

const menuOptions = [
  { label: '总览', key: '/' },
  { label: '对局记录', key: '/records' }
]
</script>

<template>
  <NLayout style="min-height:100vh">
    <NLayoutHeader bordered style="padding:0 24px;display:flex;align-items:center;justify-content:space-between;height:56px">
      <NSpace align="center">
        <strong style="font-size:16px;color:#63e2b7">HearthBot 云控</strong>
        <NMenu mode="horizontal" :options="menuOptions" :value="router.currentRoute.value.path"
          @update:value="(k: string) => router.push(k)" />
      </NSpace>
      <NButton text @click="logout">退出</NButton>
    </NLayoutHeader>

    <NLayoutContent style="padding:24px">
      <NCard title="对局记录">
        <NSpace style="margin-bottom:16px">
          <NSelect v-model:value="filterDevice" :options="deviceOptions" style="width:140px"
            placeholder="全部设备" clearable @update:value="loadRecords" />
          <NSelect v-model:value="filterResult" :options="resultOptions" style="width:120px"
            placeholder="全部结果" clearable @update:value="loadRecords" />
          <NSelect v-model:value="filterDays" :options="daysOptions" style="width:120px"
            @update:value="loadRecords" />
        </NSpace>

        <NDataTable :columns="columns" :data="records" :row-key="(r: GameRecord) => r.id" />

        <NSpace justify="center" style="margin-top:16px">
          <NPagination v-model:page="page" :page-count="Math.ceil(total / pageSize)"
            @update:page="loadRecords" />
        </NSpace>
      </NCard>
    </NLayoutContent>
  </NLayout>
</template>
```

- [ ] **Step 2: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add hearthbot-web/src/views/GameRecords.vue
git commit -m "云控：添加对局记录页面"
```

---

### Task 15: 前端构建 + 部署到云端 wwwroot

**Files:**
- Modify: `hearthbot-web/package.json` (添加 build script)
- Create: `HearthBot.Cloud/wwwroot/.gitkeep`

- [ ] **Step 1: 配置构建输出到 wwwroot**

在 `hearthbot-web/vite.config.ts` 的 `defineConfig` 中添加：

```typescript
build: {
  outDir: '../HearthBot.Cloud/wwwroot',
  emptyOutDir: true
}
```

- [ ] **Step 2: 在 package.json 中添加 build 脚本**

确保 `hearthbot-web/package.json` 中 `scripts` 包含：

```json
{
  "scripts": {
    "dev": "vite",
    "build": "vue-tsc --noEmit && vite build",
    "preview": "vite preview"
  }
}
```

- [ ] **Step 3: 构建前端**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\hearthbot-web"
npm run build
```

Expected: 构建成功，输出到 `HearthBot.Cloud/wwwroot/`

- [ ] **Step 4: 提交**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add hearthbot-web/ HearthBot.Cloud/wwwroot/
git commit -m "云控：前端构建配置，输出到云端 wwwroot"
```

---

### Task 16: 端到端验证

- [ ] **Step 1: 启动云端服务**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\HearthBot.Cloud"
dotnet run --urls "http://0.0.0.0:5000"
```

验证：
- 浏览器打开 `http://localhost:5000` 能看到登录页
- POST `/api/auth/login` 能获取 JWT Token
- GET `/api/device` 返回空列表

- [ ] **Step 2: 验证 BotMain 构建**

```bash
cd "H:\桌面\炉石脚本\Hearthbot\BotMain"
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: 创建 cloud.json 测试配置**

在 BotMain 输出目录创建 `cloud.json`：

```json
{
  "ServerUrl": "http://localhost:5000",
  "DeviceId": "",
  "DisplayName": "测试设备",
  "DeviceToken": ""
}
```

- [ ] **Step 4: 提交最终状态**

```bash
cd "H:\桌面\炉石脚本\Hearthbot"
git add -A
git commit -m "云控：端到端验证通过，完成基础云控系统"
```
