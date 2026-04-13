using HearthBot.Cloud.Data;
using HearthBot.Cloud.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BotCore.Tests.Cloud;

public sealed class CloudSchemaBootstrapperTests
{
    [Fact]
    public async Task EnsureSchemaAsync_AddsMissingDeviceColumns_ForLegacyDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText =
                """
                CREATE TABLE Devices (
                    DeviceId TEXT NOT NULL CONSTRAINT PK_Devices PRIMARY KEY,
                    DisplayName TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Offline',
                    CurrentAccount TEXT NOT NULL DEFAULT '',
                    CurrentRank TEXT NOT NULL DEFAULT '',
                    CurrentDeck TEXT NOT NULL DEFAULT '',
                    CurrentProfile TEXT NOT NULL DEFAULT '',
                    GameMode TEXT NOT NULL DEFAULT 'Standard',
                    SessionWins INTEGER NOT NULL DEFAULT 0,
                    SessionLosses INTEGER NOT NULL DEFAULT 0,
                    LastHeartbeat TEXT NOT NULL,
                    RegisteredAt TEXT NOT NULL,
                    AvailableDecksJson TEXT NOT NULL DEFAULT '[]',
                    AvailableProfilesJson TEXT NOT NULL DEFAULT '[]'
                );
                """;
            await setup.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<CloudDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new CloudDbContext(options);
        await CloudSchemaBootstrapper.EnsureSchemaAsync(db);

        var columns = new List<string>();
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info('Devices');";
            await using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(1));
        }

        Assert.Contains("StatusChangedAt", columns);
        Assert.Contains("OrderNumber", columns);
        Assert.Contains("OrderAccountName", columns);
        Assert.Contains("TargetRank", columns);
        Assert.Contains("StartRank", columns);
        Assert.Contains("StartedAt", columns);
        Assert.Contains("CurrentOpponent", columns);
        Assert.Contains("IsCompleted", columns);
        Assert.Contains("CompletedAt", columns);
        Assert.Contains("CompletedRank", columns);

        var tables = new List<string>();
        await using (var tableQuery = connection.CreateCommand())
        {
            tableQuery.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
            await using var reader = await tableQuery.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        Assert.Contains("CompletedOrderSnapshots", tables);
        Assert.Contains("HiddenDeviceEntries", tables);
    }
}
