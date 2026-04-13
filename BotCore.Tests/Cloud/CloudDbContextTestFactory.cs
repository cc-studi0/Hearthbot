using HearthBot.Cloud.Data;
using HearthBot.Cloud.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BotCore.Tests.Cloud;

internal sealed class CloudTestEnvironment : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly AsyncServiceScope _seedScope;

    private CloudTestEnvironment(SqliteConnection connection, ServiceProvider serviceProvider, AsyncServiceScope seedScope)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
        _seedScope = seedScope;
        Db = seedScope.ServiceProvider.GetRequiredService<CloudDbContext>();
    }

    public CloudDbContext Db { get; }

    public static async Task<CloudTestEnvironment> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CloudDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<DeviceDisplayStateEvaluator>();
        services.AddSingleton<DeviceDashboardProjectionService>();
        services.AddSingleton<DeviceManager>();

        var serviceProvider = services.BuildServiceProvider();
        var seedScope = serviceProvider.CreateAsyncScope();
        var db = seedScope.ServiceProvider.GetRequiredService<CloudDbContext>();
        await db.Database.EnsureCreatedAsync();

        return new CloudTestEnvironment(connection, serviceProvider, seedScope);
    }

    public DeviceManager CreateDeviceManager() =>
        _serviceProvider.GetRequiredService<DeviceManager>();

    public DeviceDashboardProjectionService CreateProjectionService() =>
        _serviceProvider.GetRequiredService<DeviceDashboardProjectionService>();

    public CompletedOrderService CreateCompletedOrders() => new(Db);

    public HiddenDeviceService CreateHiddenDevices() => new(Db);

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _seedScope.DisposeAsync();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
