using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BotMain.Learning
{
    internal sealed class SqliteConsistencyStore : IDisposable
    {
        private static readonly string DefaultDbPath =
            Path.Combine(AppPaths.RootDirectory, "Data", "HsBoxTeacher", "consistency.db");

        private readonly SqliteConnection _conn;

        public SqliteConsistencyStore(string dbPath = null)
        {
            dbPath ??= DefaultDbPath;
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS consistency_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    dimension TEXT NOT NULL,
                    is_match INTEGER NOT NULL,
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
                CREATE INDEX IF NOT EXISTS idx_consistency_dim_id
                    ON consistency_records(dimension, id DESC);

                CREATE TABLE IF NOT EXISTS match_outcomes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    match_id TEXT NOT NULL,
                    is_win INTEGER NOT NULL,
                    learning_active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
            ";
            cmd.ExecuteNonQuery();
        }

        public void RecordConsistency(string dimension, bool isMatch)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO consistency_records(dimension, is_match) VALUES(@d, @m)";
            cmd.Parameters.AddWithValue("@d", dimension);
            cmd.Parameters.AddWithValue("@m", isMatch ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void RecordMatchOutcome(string matchId, bool isWin, bool learningActive)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO match_outcomes(match_id, is_win, learning_active) VALUES(@id, @w, @la)";
            cmd.Parameters.AddWithValue("@id", matchId);
            cmd.Parameters.AddWithValue("@w", isWin ? 1 : 0);
            cmd.Parameters.AddWithValue("@la", learningActive ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public List<bool> LoadRecentRecords(string dimension, int count)
        {
            var results = new List<bool>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT is_match FROM consistency_records
                WHERE dimension = @d
                ORDER BY id DESC LIMIT @c";
            cmd.Parameters.AddWithValue("@d", dimension);
            cmd.Parameters.AddWithValue("@c", count);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(reader.GetInt32(0) == 1);
            results.Reverse();
            return results;
        }

        public int GetTotalMatchCount()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM match_outcomes";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public double GetRecentWinRate(int count)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(AVG(CAST(is_win AS REAL)), 0)
                FROM (SELECT is_win FROM match_outcomes ORDER BY id DESC LIMIT @c)";
            cmd.Parameters.AddWithValue("@c", count);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToDouble(result) * 100.0 : 0.0;
        }

        public double GetLearningPhaseWinRate(int count)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(AVG(CAST(is_win AS REAL)), 0)
                FROM (SELECT is_win FROM match_outcomes WHERE learning_active = 1 ORDER BY id DESC LIMIT @c)";
            cmd.Parameters.AddWithValue("@c", count);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToDouble(result) * 100.0 : 0.0;
        }

        public void Dispose()
        {
            _conn?.Dispose();
        }
    }
}
