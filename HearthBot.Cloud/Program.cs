using System.IO.Compression;
using System.Text;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Hubs;
using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.ResponseCompression;
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

builder.Services.AddSingleton<DeviceManager>();
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHostedService<DeviceWatchdog>();

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/javascript", "text/css", "application/json"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:5000")
     .AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
    db.Database.EnsureCreated();
    // 兼容已有数据库：尝试添加新列
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN OrderNumber TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN OrderAccountName TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN TargetRank TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN StartRank TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN StartedAt TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN CurrentOpponent TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN CompletedAt TEXT"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN CompletedRank TEXT NOT NULL DEFAULT ''"); } catch { }
}

app.UseResponseCompression();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BotHub>("/hub/bot");
app.MapHub<DashboardHub>("/hub/dashboard");
app.MapFallbackToFile("index.html");

app.Run();
