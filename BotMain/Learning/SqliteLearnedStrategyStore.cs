using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BotMain.Learning
{
    internal sealed class SqliteLearnedStrategyStore : ILearnedStrategyStore
    {
        internal const string DefaultDatabasePath = @"H:\桌面\炉石脚本\Hearthbot\Data\HsBoxTeacher\teacher.db";

        private readonly object _sync = new object();
        private readonly string _databasePath;
        private bool _schemaEnsured;

        public SqliteLearnedStrategyStore(string databasePath = null)
        {
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? DefaultDatabasePath
                : Path.GetFullPath(databasePath);
        }

        public LearnedStrategySnapshot LoadSnapshot()
        {
            lock (_sync)
            {
                EnsureSchema();
                var snapshot = new LearnedStrategySnapshot();
                using (var connection = OpenConnection())
                {
                    using (var actionCommand = connection.CreateCommand())
                    {
                        actionCommand.CommandText =
                            "SELECT rule_key, deck_signature, board_bucket, scope, source_card_id, target_card_id, weight, sample_count FROM action_rules;";
                        using (var reader = actionCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                snapshot.ActionRules.Add(new LearnedActionRule
                                {
                                    RuleKey = reader.GetString(0),
                                    DeckSignature = reader.GetString(1),
                                    BoardBucket = reader.GetString(2),
                                    Scope = Enum.TryParse(reader.GetString(3), out LearnedActionScope scope)
                                        ? scope
                                        : LearnedActionScope.Unknown,
                                    SourceCardId = reader.GetString(4),
                                    TargetCardId = reader.GetString(5),
                                    Weight = reader.GetDouble(6),
                                    SampleCount = reader.GetInt32(7)
                                });
                            }
                        }
                    }

                    using (var mulliganCommand = connection.CreateCommand())
                    {
                        mulliganCommand.CommandText =
                            "SELECT rule_key, deck_signature, enemy_class, has_coin, card_id, context_card_id, weight, sample_count FROM mulligan_rules;";
                        using (var reader = mulliganCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                snapshot.MulliganRules.Add(new LearnedMulliganRule
                                {
                                    RuleKey = reader.GetString(0),
                                    DeckSignature = reader.GetString(1),
                                    EnemyClass = reader.GetInt32(2),
                                    HasCoin = reader.GetInt32(3) != 0,
                                    CardId = reader.GetString(4),
                                    ContextCardId = reader.GetString(5),
                                    Weight = reader.GetDouble(6),
                                    SampleCount = reader.GetInt32(7)
                                });
                            }
                        }
                    }

                    using (var choiceCommand = connection.CreateCommand())
                    {
                        choiceCommand.CommandText =
                            "SELECT rule_key, deck_signature, mode, origin_card_id, option_card_id, board_bucket, weight, sample_count FROM choice_rules;";
                        using (var reader = choiceCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                snapshot.ChoiceRules.Add(new LearnedChoiceRule
                                {
                                    RuleKey = reader.GetString(0),
                                    DeckSignature = reader.GetString(1),
                                    Mode = reader.GetString(2),
                                    OriginCardId = reader.GetString(3),
                                    OptionCardId = reader.GetString(4),
                                    BoardBucket = reader.GetString(5),
                                    Weight = reader.GetDouble(6),
                                    SampleCount = reader.GetInt32(7)
                                });
                            }
                        }
                    }
                }

                return snapshot;
            }
        }

        public bool TryStoreActionTraining(ActionTrainingRecord record, out string detail)
        {
            lock (_sync)
            {
                EnsureSchema();
                using (var connection = OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    if (!TryInsertSample(
                            connection,
                            transaction,
                            record?.SampleKey,
                            record?.MatchId,
                            "action",
                            record?.PayloadSignature,
                            record?.DeckSignature,
                            record?.BoardFingerprint,
                            string.Empty,
                            record?.RuleImpacts,
                            record?.CreatedAtMs ?? 0,
                            out detail))
                    {
                        transaction.Rollback();
                        return false;
                    }

                    foreach (var delta in record.Deltas)
                        UpsertActionRule(connection, transaction, delta, record.CreatedAtMs);

                    transaction.Commit();
                    detail = $"stored action_rules={record.Deltas.Count}";
                    return true;
                }
            }
        }

        public bool TryStoreMulliganTraining(MulliganTrainingRecord record, out string detail)
        {
            lock (_sync)
            {
                EnsureSchema();
                using (var connection = OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    if (!TryInsertSample(
                            connection,
                            transaction,
                            record?.SampleKey,
                            record?.MatchId,
                            "mulligan",
                            string.Empty,
                            record?.DeckSignature,
                            string.Empty,
                            record?.SnapshotSignature,
                            record?.RuleImpacts,
                            record?.CreatedAtMs ?? 0,
                            out detail))
                    {
                        transaction.Rollback();
                        return false;
                    }

                    foreach (var delta in record.Deltas)
                        UpsertMulliganRule(connection, transaction, delta, record.CreatedAtMs);

                    transaction.Commit();
                    detail = $"stored mulligan_rules={record.Deltas.Count}";
                    return true;
                }
            }
        }

        public bool TryStoreChoiceTraining(ChoiceTrainingRecord record, out string detail)
        {
            lock (_sync)
            {
                EnsureSchema();
                using (var connection = OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    if (!TryInsertSample(
                            connection,
                            transaction,
                            record?.SampleKey,
                            record?.MatchId,
                            "choice",
                            record?.PayloadSignature,
                            record?.DeckSignature,
                            record?.BoardFingerprint,
                            string.Empty,
                            record?.RuleImpacts,
                            record?.CreatedAtMs ?? 0,
                            out detail))
                    {
                        transaction.Rollback();
                        return false;
                    }

                    foreach (var delta in record.Deltas)
                        UpsertChoiceRule(connection, transaction, delta, record.CreatedAtMs);

                    transaction.Commit();
                    detail = $"stored choice_rules={record.Deltas.Count}";
                    return true;
                }
            }
        }

        public bool TryApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome, out string detail)
        {
            lock (_sync)
            {
                EnsureSchema();
                if (string.IsNullOrWhiteSpace(matchId))
                {
                    detail = "match_id_empty";
                    return false;
                }

                using (var connection = OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    var samples = new List<(string SampleKey, string RuleImpactsJson)>();
                    using (var selectCommand = connection.CreateCommand())
                    {
                        selectCommand.Transaction = transaction;
                        selectCommand.CommandText =
                            "SELECT sample_key, rule_impacts_json FROM samples WHERE match_id = $matchId AND outcome_applied = 0;";
                        selectCommand.Parameters.AddWithValue("$matchId", matchId);
                        using (var reader = selectCommand.ExecuteReader())
                        {
                            while (reader.Read())
                                samples.Add((reader.GetString(0), reader.GetString(1)));
                        }
                    }

                    if (samples.Count == 0)
                    {
                        transaction.Rollback();
                        detail = "no_pending_samples";
                        return false;
                    }

                    var outcomeScale = GetOutcomeScale(outcome);
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (Math.Abs(outcomeScale) > 0.0001)
                    {
                        foreach (var sample in samples)
                        {
                            var impacts = JsonConvert.DeserializeObject<List<LearnedRuleImpact>>(sample.RuleImpactsJson)
                                ?? new List<LearnedRuleImpact>();
                            foreach (var impact in impacts)
                            {
                                var signedDelta = Math.Sign(impact.Delta) * outcomeScale;
                                if (Math.Abs(signedDelta) <= 0.0001)
                                    continue;

                                ApplyOutcomeDelta(connection, transaction, impact.RuleKind, impact.RuleKey, signedDelta, now);
                            }
                        }
                    }

                    using (var updateCommand = connection.CreateCommand())
                    {
                        updateCommand.Transaction = transaction;
                        updateCommand.CommandText =
                            "UPDATE samples SET outcome_applied = 1 WHERE match_id = $matchId AND outcome_applied = 0;";
                        updateCommand.Parameters.AddWithValue("$matchId", matchId);
                        updateCommand.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    detail = $"outcome={outcome}, samples={samples.Count}";
                    return true;
                }
            }
        }

        private void EnsureSchema()
        {
            if (_schemaEnsured)
                return;

            var dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using (var connection = OpenConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        @"CREATE TABLE IF NOT EXISTS samples (
                            sample_key TEXT PRIMARY KEY,
                            match_id TEXT,
                            sample_kind TEXT NOT NULL,
                            payload_signature TEXT NOT NULL,
                            deck_signature TEXT NOT NULL,
                            board_fingerprint TEXT NOT NULL,
                            snapshot_signature TEXT NOT NULL,
                            rule_impacts_json TEXT NOT NULL,
                            outcome_applied INTEGER NOT NULL DEFAULT 0,
                            created_at_ms INTEGER NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS action_rules (
                            rule_key TEXT PRIMARY KEY,
                            deck_signature TEXT NOT NULL,
                            board_bucket TEXT NOT NULL,
                            scope TEXT NOT NULL,
                            source_card_id TEXT NOT NULL,
                            target_card_id TEXT NOT NULL,
                            weight REAL NOT NULL,
                            sample_count INTEGER NOT NULL,
                            updated_at_ms INTEGER NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS mulligan_rules (
                            rule_key TEXT PRIMARY KEY,
                            deck_signature TEXT NOT NULL,
                            enemy_class INTEGER NOT NULL,
                            has_coin INTEGER NOT NULL,
                            card_id TEXT NOT NULL,
                            context_card_id TEXT NOT NULL,
                            weight REAL NOT NULL,
                            sample_count INTEGER NOT NULL,
                            updated_at_ms INTEGER NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS choice_rules (
                            rule_key TEXT PRIMARY KEY,
                            deck_signature TEXT NOT NULL,
                            mode TEXT NOT NULL,
                            origin_card_id TEXT NOT NULL,
                            option_card_id TEXT NOT NULL,
                            board_bucket TEXT NOT NULL,
                            weight REAL NOT NULL,
                            sample_count INTEGER NOT NULL,
                            updated_at_ms INTEGER NOT NULL
                        );";
                    command.ExecuteNonQuery();
                }
            }

            _schemaEnsured = true;
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString());
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                pragma.ExecuteNonQuery();
            }

            return connection;
        }

        private static bool TryInsertSample(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sampleKey,
            string matchId,
            string sampleKind,
            string payloadSignature,
            string deckSignature,
            string boardFingerprint,
            string snapshotSignature,
            IReadOnlyList<LearnedRuleImpact> ruleImpacts,
            long createdAtMs,
            out string detail)
        {
            detail = "duplicate_sample";
            if (string.IsNullOrWhiteSpace(sampleKey))
            {
                detail = "sample_key_empty";
                return false;
            }

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    @"INSERT OR IGNORE INTO samples
                        (sample_key, match_id, sample_kind, payload_signature, deck_signature, board_fingerprint, snapshot_signature, rule_impacts_json, outcome_applied, created_at_ms)
                      VALUES
                        ($sampleKey, $matchId, $sampleKind, $payloadSignature, $deckSignature, $boardFingerprint, $snapshotSignature, $ruleImpactsJson, 0, $createdAtMs);";
                insert.Parameters.AddWithValue("$sampleKey", sampleKey);
                insert.Parameters.AddWithValue("$matchId", matchId ?? string.Empty);
                insert.Parameters.AddWithValue("$sampleKind", sampleKind ?? string.Empty);
                insert.Parameters.AddWithValue("$payloadSignature", payloadSignature ?? string.Empty);
                insert.Parameters.AddWithValue("$deckSignature", deckSignature ?? string.Empty);
                insert.Parameters.AddWithValue("$boardFingerprint", boardFingerprint ?? string.Empty);
                insert.Parameters.AddWithValue("$snapshotSignature", snapshotSignature ?? string.Empty);
                insert.Parameters.AddWithValue(
                    "$ruleImpactsJson",
                    JsonConvert.SerializeObject(ruleImpacts ?? Array.Empty<LearnedRuleImpact>()));
                insert.Parameters.AddWithValue("$createdAtMs", createdAtMs);
                var inserted = insert.ExecuteNonQuery() > 0;
                if (!inserted)
                    return false;
            }

            detail = "stored_sample";
            return true;
        }

        private static void UpsertActionRule(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ActionRuleDelta delta,
            long updatedAtMs)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    @"INSERT INTO action_rules
                        (rule_key, deck_signature, board_bucket, scope, source_card_id, target_card_id, weight, sample_count, updated_at_ms)
                      VALUES
                        ($ruleKey, $deckSignature, $boardBucket, $scope, $sourceCardId, $targetCardId, $weight, 1, $updatedAtMs)
                      ON CONFLICT(rule_key) DO UPDATE SET
                        weight = action_rules.weight + excluded.weight,
                        sample_count = action_rules.sample_count + 1,
                        updated_at_ms = excluded.updated_at_ms;";
                command.Parameters.AddWithValue("$ruleKey", delta.RuleKey);
                command.Parameters.AddWithValue("$deckSignature", delta.DeckSignature);
                command.Parameters.AddWithValue("$boardBucket", delta.BoardBucket);
                command.Parameters.AddWithValue("$scope", delta.Scope.ToString());
                command.Parameters.AddWithValue("$sourceCardId", delta.SourceCardId ?? string.Empty);
                command.Parameters.AddWithValue("$targetCardId", delta.TargetCardId ?? string.Empty);
                command.Parameters.AddWithValue("$weight", delta.WeightDelta);
                command.Parameters.AddWithValue("$updatedAtMs", updatedAtMs);
                command.ExecuteNonQuery();
            }
        }

        private static void UpsertMulliganRule(
            SqliteConnection connection,
            SqliteTransaction transaction,
            MulliganRuleDelta delta,
            long updatedAtMs)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    @"INSERT INTO mulligan_rules
                        (rule_key, deck_signature, enemy_class, has_coin, card_id, context_card_id, weight, sample_count, updated_at_ms)
                      VALUES
                        ($ruleKey, $deckSignature, $enemyClass, $hasCoin, $cardId, $contextCardId, $weight, 1, $updatedAtMs)
                      ON CONFLICT(rule_key) DO UPDATE SET
                        weight = mulligan_rules.weight + excluded.weight,
                        sample_count = mulligan_rules.sample_count + 1,
                        updated_at_ms = excluded.updated_at_ms;";
                command.Parameters.AddWithValue("$ruleKey", delta.RuleKey);
                command.Parameters.AddWithValue("$deckSignature", delta.DeckSignature);
                command.Parameters.AddWithValue("$enemyClass", delta.EnemyClass);
                command.Parameters.AddWithValue("$hasCoin", delta.HasCoin ? 1 : 0);
                command.Parameters.AddWithValue("$cardId", delta.CardId ?? string.Empty);
                command.Parameters.AddWithValue("$contextCardId", delta.ContextCardId ?? string.Empty);
                command.Parameters.AddWithValue("$weight", delta.WeightDelta);
                command.Parameters.AddWithValue("$updatedAtMs", updatedAtMs);
                command.ExecuteNonQuery();
            }
        }

        private static void UpsertChoiceRule(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ChoiceRuleDelta delta,
            long updatedAtMs)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    @"INSERT INTO choice_rules
                        (rule_key, deck_signature, mode, origin_card_id, option_card_id, board_bucket, weight, sample_count, updated_at_ms)
                      VALUES
                        ($ruleKey, $deckSignature, $mode, $originCardId, $optionCardId, $boardBucket, $weight, 1, $updatedAtMs)
                      ON CONFLICT(rule_key) DO UPDATE SET
                        weight = choice_rules.weight + excluded.weight,
                        sample_count = choice_rules.sample_count + 1,
                        updated_at_ms = excluded.updated_at_ms;";
                command.Parameters.AddWithValue("$ruleKey", delta.RuleKey);
                command.Parameters.AddWithValue("$deckSignature", delta.DeckSignature);
                command.Parameters.AddWithValue("$mode", delta.Mode ?? string.Empty);
                command.Parameters.AddWithValue("$originCardId", delta.OriginCardId ?? string.Empty);
                command.Parameters.AddWithValue("$optionCardId", delta.OptionCardId ?? string.Empty);
                command.Parameters.AddWithValue("$boardBucket", delta.BoardBucket ?? string.Empty);
                command.Parameters.AddWithValue("$weight", delta.WeightDelta);
                command.Parameters.AddWithValue("$updatedAtMs", updatedAtMs);
                command.ExecuteNonQuery();
            }
        }

        private static double GetOutcomeScale(LearnedMatchOutcome outcome)
        {
            switch (outcome)
            {
                case LearnedMatchOutcome.Win:
                    return 0.35;
                case LearnedMatchOutcome.Loss:
                    return -0.25;
                default:
                    return 0d;
            }
        }

        private static void ApplyOutcomeDelta(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string ruleKind,
            string ruleKey,
            double delta,
            long updatedAtMs)
        {
            if (string.IsNullOrWhiteSpace(ruleKind) || string.IsNullOrWhiteSpace(ruleKey))
                return;

            var table = ruleKind switch
            {
                "action" => "action_rules",
                "mulligan" => "mulligan_rules",
                "choice" => "choice_rules",
                _ => null
            };
            if (table == null)
                return;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"UPDATE {table} SET weight = weight + $delta, updated_at_ms = $updatedAtMs WHERE rule_key = $ruleKey;";
                command.Parameters.AddWithValue("$delta", delta);
                command.Parameters.AddWithValue("$updatedAtMs", updatedAtMs);
                command.Parameters.AddWithValue("$ruleKey", ruleKey);
                command.ExecuteNonQuery();
            }
        }
    }
}
