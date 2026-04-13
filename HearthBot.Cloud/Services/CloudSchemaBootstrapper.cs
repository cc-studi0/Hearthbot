using System.Data;
using System.Data.Common;
using HearthBot.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services;

public static class CloudSchemaBootstrapper
{
    private static readonly (string Name, string Definition)[] DeviceColumns =
    {
        ("OrderNumber", "TEXT NOT NULL DEFAULT ''"),
        ("OrderAccountName", "TEXT NOT NULL DEFAULT ''"),
        ("TargetRank", "TEXT NOT NULL DEFAULT ''"),
        ("StartRank", "TEXT NOT NULL DEFAULT ''"),
        ("StartedAt", "TEXT"),
        ("StatusChangedAt", "TEXT"),
        ("CurrentOpponent", "TEXT NOT NULL DEFAULT ''"),
        ("IsCompleted", "INTEGER NOT NULL DEFAULT 0"),
        ("CompletedAt", "TEXT"),
        ("CompletedRank", "TEXT NOT NULL DEFAULT ''")
    };

    public static async Task EnsureSchemaAsync(CloudDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        foreach (var (name, definition) in DeviceColumns)
            await EnsureColumnAsync(db, "Devices", name, definition, cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS CompletedOrderSnapshots (
                Id INTEGER NOT NULL CONSTRAINT PK_CompletedOrderSnapshots PRIMARY KEY AUTOINCREMENT,
                DeviceId TEXT NOT NULL DEFAULT '',
                DisplayName TEXT NOT NULL DEFAULT '',
                OrderNumber TEXT NOT NULL DEFAULT '',
                AccountName TEXT NOT NULL DEFAULT '',
                StartRank TEXT NOT NULL DEFAULT '',
                TargetRank TEXT NOT NULL DEFAULT '',
                CompletedRank TEXT NOT NULL DEFAULT '',
                DeckName TEXT NOT NULL DEFAULT '',
                ProfileName TEXT NOT NULL DEFAULT '',
                GameMode TEXT NOT NULL DEFAULT '',
                Wins INTEGER NOT NULL DEFAULT 0,
                Losses INTEGER NOT NULL DEFAULT 0,
                CompletedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                DeletedAt TEXT NULL
            )
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS HiddenDeviceEntries (
                Id INTEGER NOT NULL CONSTRAINT PK_HiddenDeviceEntries PRIMARY KEY AUTOINCREMENT,
                DeviceId TEXT NOT NULL DEFAULT '',
                CurrentAccount TEXT NOT NULL DEFAULT '',
                OrderNumber TEXT NOT NULL DEFAULT '',
                HiddenAt TEXT NOT NULL
            )
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_HiddenDeviceEntries_DeviceId_CurrentAccount_OrderNumber
            ON HiddenDeviceEntries (DeviceId, CurrentAccount, OrderNumber)
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS GameRecords (
                Id INTEGER NOT NULL CONSTRAINT PK_GameRecords PRIMARY KEY AUTOINCREMENT,
                DeviceId TEXT NOT NULL DEFAULT '',
                AccountName TEXT NOT NULL DEFAULT '',
                Result TEXT NOT NULL DEFAULT '',
                MyClass TEXT NOT NULL DEFAULT '',
                OpponentClass TEXT NOT NULL DEFAULT '',
                DeckName TEXT NOT NULL DEFAULT '',
                ProfileName TEXT NOT NULL DEFAULT '',
                DurationSeconds INTEGER NOT NULL DEFAULT 0,
                RankBefore TEXT NOT NULL DEFAULT '',
                RankAfter TEXT NOT NULL DEFAULT '',
                GameMode TEXT NOT NULL DEFAULT '',
                PlayedAt TEXT NOT NULL
            )
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_GameRecords_DeviceId ON GameRecords (DeviceId)",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_GameRecords_PlayedAt ON GameRecords (PlayedAt)",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_GameRecords_AccountName ON GameRecords (AccountName)",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS PendingCommands (
                Id INTEGER NOT NULL CONSTRAINT PK_PendingCommands PRIMARY KEY AUTOINCREMENT,
                DeviceId TEXT NOT NULL DEFAULT '',
                CommandType TEXT NOT NULL DEFAULT '',
                Payload TEXT NOT NULL DEFAULT '{{}}',
                Status TEXT NOT NULL DEFAULT 'Pending',
                CreatedAt TEXT NOT NULL,
                ExecutedAt TEXT NULL
            )
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_PendingCommands_DeviceId_Status
            ON PendingCommands (DeviceId, Status)
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_CompletedOrderSnapshots_DeviceId ON CompletedOrderSnapshots (DeviceId)",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_CompletedOrderSnapshots_CompletedAt ON CompletedOrderSnapshots (CompletedAt)",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_CompletedOrderSnapshots_ExpiresAt ON CompletedOrderSnapshots (ExpiresAt)",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_HiddenDeviceEntries_DeviceId ON HiddenDeviceEntries (DeviceId)",
            cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        CloudDbContext db,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        var columns = await GetColumnNamesAsync(db.Database.GetDbConnection(), tableName, cancellationToken);
        if (columns.Contains(columnName))
            return;

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"" + tableName + "\" ADD COLUMN \"" + columnName + "\" " + definition,
            cancellationToken);
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{tableName}')";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(1));

            return columns;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
