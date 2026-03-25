using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BotMain.Learning
{
    internal sealed class SqliteTeacherDatasetStore : ITeacherDatasetStore
    {
        internal const string DefaultDatabasePath = @"H:\桌面\炉石脚本\Hearthbot\Data\HsBoxTeacher\dataset.db";

        private readonly object _sync = new object();
        private readonly string _databasePath;
        private bool _schemaEnsured;

        public SqliteTeacherDatasetStore(string databasePath = null)
        {
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? DefaultDatabasePath
                : Path.GetFullPath(databasePath);
        }

        public bool TryStoreActionDecision(
            TeacherActionDecisionRecord decision,
            IReadOnlyList<TeacherActionCandidateRecord> candidates,
            out string detail)
        {
            lock (_sync)
            {
                EnsureSchema();
                if (string.IsNullOrWhiteSpace(decision?.DecisionId))
                {
                    detail = "decision_id_empty";
                    return false;
                }

                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                try
                {
                    if (!UpsertMatch(connection, transaction, decision.MatchId, decision.CreatedAtMs, allowOutcomeRefresh: false))
                    {
                        transaction.Rollback();
                        detail = "match_upsert_failed";
                        return false;
                    }

                    var createdAtMs = decision.CreatedAtMs > 0
                        ? decision.CreatedAtMs
                        : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    using (var insertDecision = connection.CreateCommand())
                    {
                        insertDecision.Transaction = transaction;
                        insertDecision.CommandText =
                            @"INSERT OR IGNORE INTO action_decisions
                                (decision_id, match_id, payload_signature, seed, teacher_action_command, board_snapshot_json, context_snapshot_json, mapping_status, outcome, created_at_ms, teacher_mapped_candidate_id)
                              VALUES
                                ($decisionId, $matchId, $payloadSignature, $seed, $teacherActionCommand, $boardSnapshotJson, $contextSnapshotJson, $mappingStatus, $outcome, $createdAtMs, $teacherMappedCandidateId);";
                        insertDecision.Parameters.AddWithValue("$decisionId", decision.DecisionId);
                        insertDecision.Parameters.AddWithValue("$matchId", decision.MatchId ?? string.Empty);
                        insertDecision.Parameters.AddWithValue("$payloadSignature", decision.PayloadSignature ?? string.Empty);
                        insertDecision.Parameters.AddWithValue("$seed", decision.Seed ?? string.Empty);
                        insertDecision.Parameters.AddWithValue("$teacherActionCommand", decision.TeacherActionCommand ?? string.Empty);
                        insertDecision.Parameters.AddWithValue("$boardSnapshotJson", decision.BoardSnapshotJson ?? string.Empty);
                        insertDecision.Parameters.AddWithValue("$contextSnapshotJson", decision.ContextSnapshotJson ?? string.Empty);
                        insertDecision.Parameters.AddWithValue("$mappingStatus", (int)decision.MappingStatus);
                        insertDecision.Parameters.AddWithValue("$outcome", (int)LearnedMatchOutcome.Unknown);
                        insertDecision.Parameters.AddWithValue("$createdAtMs", createdAtMs);
                        insertDecision.Parameters.AddWithValue("$teacherMappedCandidateId", decision.TeacherMappedCandidateId ?? string.Empty);
                        var inserted = insertDecision.ExecuteNonQuery() > 0;
                        if (!inserted)
                        {
                            transaction.Rollback();
                            detail = "duplicate_decision";
                            return false;
                        }
                    }

                    var candidateList = candidates ?? Array.Empty<TeacherActionCandidateRecord>();
                    for (var index = 0; index < candidateList.Count; index++)
                    {
                        var candidate = candidateList[index] ?? new TeacherActionCandidateRecord();
                        var candidateId = string.IsNullOrWhiteSpace(candidate.CandidateId)
                            ? LearnedStrategyFeatureExtractor.HashComposite(
                                decision.DecisionId,
                                candidate.ActionCommand ?? string.Empty,
                                index.ToString())
                            : candidate.CandidateId;
                        using var insertCandidate = connection.CreateCommand();
                        insertCandidate.Transaction = transaction;
                        insertCandidate.CommandText =
                            @"INSERT INTO action_candidates
                                (candidate_id, decision_id, action_command, action_type, source_card_id, target_card_id, candidate_snapshot_json, candidate_features_json, is_teacher_pick)
                              VALUES
                                ($candidateId, $decisionId, $actionCommand, $actionType, $sourceCardId, $targetCardId, $candidateSnapshotJson, $candidateFeaturesJson, $isTeacherPick);";
                        insertCandidate.Parameters.AddWithValue("$candidateId", candidateId);
                        insertCandidate.Parameters.AddWithValue("$decisionId", decision.DecisionId);
                        insertCandidate.Parameters.AddWithValue("$actionCommand", candidate.ActionCommand ?? string.Empty);
                        insertCandidate.Parameters.AddWithValue("$actionType", candidate.ActionType ?? string.Empty);
                        insertCandidate.Parameters.AddWithValue("$sourceCardId", candidate.SourceCardId ?? string.Empty);
                        insertCandidate.Parameters.AddWithValue("$targetCardId", candidate.TargetCardId ?? string.Empty);
                        insertCandidate.Parameters.AddWithValue("$candidateSnapshotJson", candidate.CandidateSnapshotJson ?? string.Empty);
                        insertCandidate.Parameters.AddWithValue("$candidateFeaturesJson", candidate.CandidateFeaturesJson ?? string.Empty);
                        insertCandidate.Parameters.AddWithValue("$isTeacherPick", candidate.IsTeacherPick ? 1 : 0);
                        insertCandidate.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    detail = $"stored_action_decision candidates={candidateList.Count}";
                    return true;
                }
                catch (SqliteException ex) when (
                    ex.SqliteExtendedErrorCode == 1555
                    || ex.SqliteExtendedErrorCode == 2067
                    || ex.SqliteErrorCode == 19)
                {
                    transaction.Rollback();
                    detail = "duplicate_candidate";
                    return false;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public bool TryStoreChoiceDecision(TeacherChoiceDecisionRecord decision, out string detail)
        {
            lock (_sync)
            {
                EnsureSchema();
                if (string.IsNullOrWhiteSpace(decision?.DecisionId))
                {
                    detail = "decision_id_empty";
                    return false;
                }

                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                UpsertMatch(connection, transaction, decision.MatchId, decision.CreatedAtMs, allowOutcomeRefresh: false);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    @"INSERT OR IGNORE INTO choice_decisions
                        (decision_id, match_id, payload_signature, deck_signature, mode, origin_card_id, pending_origin_json, options_snapshot_json, teacher_selected_entity_ids_json, local_selected_entity_ids_json, board_snapshot_json, context_snapshot_json, outcome, created_at_ms)
                      VALUES
                        ($decisionId, $matchId, $payloadSignature, $deckSignature, $mode, $originCardId, $pendingOriginJson, $optionsSnapshotJson, $teacherSelectedEntityIdsJson, $localSelectedEntityIdsJson, $boardSnapshotJson, $contextSnapshotJson, $outcome, $createdAtMs);";
                command.Parameters.AddWithValue("$decisionId", decision.DecisionId);
                command.Parameters.AddWithValue("$matchId", decision.MatchId ?? string.Empty);
                command.Parameters.AddWithValue("$payloadSignature", decision.PayloadSignature ?? string.Empty);
                command.Parameters.AddWithValue("$deckSignature", decision.DeckSignature ?? string.Empty);
                command.Parameters.AddWithValue("$mode", decision.Mode ?? string.Empty);
                command.Parameters.AddWithValue("$originCardId", decision.OriginCardId ?? string.Empty);
                command.Parameters.AddWithValue("$pendingOriginJson", decision.PendingOriginJson ?? string.Empty);
                command.Parameters.AddWithValue("$optionsSnapshotJson", decision.OptionsSnapshotJson ?? string.Empty);
                command.Parameters.AddWithValue("$teacherSelectedEntityIdsJson", decision.TeacherSelectedEntityIdsJson ?? string.Empty);
                command.Parameters.AddWithValue("$localSelectedEntityIdsJson", decision.LocalSelectedEntityIdsJson ?? string.Empty);
                command.Parameters.AddWithValue("$boardSnapshotJson", decision.BoardSnapshotJson ?? string.Empty);
                command.Parameters.AddWithValue("$contextSnapshotJson", decision.ContextSnapshotJson ?? string.Empty);
                command.Parameters.AddWithValue("$outcome", (int)LearnedMatchOutcome.Unknown);
                command.Parameters.AddWithValue(
                    "$createdAtMs",
                    decision.CreatedAtMs > 0 ? decision.CreatedAtMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var inserted = command.ExecuteNonQuery() > 0;
                if (!inserted)
                {
                    transaction.Rollback();
                    detail = "duplicate_decision";
                    return false;
                }

                transaction.Commit();
                detail = "stored_choice_decision";
                return true;
            }
        }

        public bool TryStoreMulliganDecision(TeacherMulliganDecisionRecord decision, out string detail)
        {
            lock (_sync)
            {
                EnsureSchema();
                if (string.IsNullOrWhiteSpace(decision?.DecisionId))
                {
                    detail = "decision_id_empty";
                    return false;
                }

                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                UpsertMatch(connection, transaction, decision.MatchId, decision.CreatedAtMs, allowOutcomeRefresh: false);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    @"INSERT OR IGNORE INTO mulligan_decisions
                        (decision_id, match_id, deck_signature, seed, offered_cards_json, teacher_keeps_json, local_keeps_json, board_snapshot_json, context_snapshot_json, outcome, created_at_ms)
                      VALUES
                        ($decisionId, $matchId, $deckSignature, $seed, $offeredCardsJson, $teacherKeepsJson, $localKeepsJson, $boardSnapshotJson, $contextSnapshotJson, $outcome, $createdAtMs);";
                command.Parameters.AddWithValue("$decisionId", decision.DecisionId);
                command.Parameters.AddWithValue("$matchId", decision.MatchId ?? string.Empty);
                command.Parameters.AddWithValue("$deckSignature", decision.DeckSignature ?? string.Empty);
                command.Parameters.AddWithValue("$seed", decision.Seed ?? string.Empty);
                command.Parameters.AddWithValue("$offeredCardsJson", decision.OfferedCardsJson ?? string.Empty);
                command.Parameters.AddWithValue("$teacherKeepsJson", decision.TeacherKeepsJson ?? string.Empty);
                command.Parameters.AddWithValue("$localKeepsJson", decision.LocalKeepsJson ?? string.Empty);
                command.Parameters.AddWithValue("$boardSnapshotJson", decision.BoardSnapshotJson ?? string.Empty);
                command.Parameters.AddWithValue("$contextSnapshotJson", decision.ContextSnapshotJson ?? string.Empty);
                command.Parameters.AddWithValue("$outcome", (int)LearnedMatchOutcome.Unknown);
                command.Parameters.AddWithValue(
                    "$createdAtMs",
                    decision.CreatedAtMs > 0 ? decision.CreatedAtMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var inserted = command.ExecuteNonQuery() > 0;
                if (!inserted)
                {
                    transaction.Rollback();
                    detail = "duplicate_decision";
                    return false;
                }

                transaction.Commit();
                detail = "stored_mulligan_decision";
                return true;
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

                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var affected = 0;

                affected += UpdateOutcome(
                    connection,
                    transaction,
                    "UPDATE matches SET outcome = $outcome, updated_at_ms = $updatedAtMs WHERE match_id = $matchId AND outcome = $unknown;",
                    matchId,
                    outcome);
                affected += UpdateOutcome(
                    connection,
                    transaction,
                    "UPDATE action_decisions SET outcome = $outcome WHERE match_id = $matchId AND outcome = $unknown;",
                    matchId,
                    outcome);
                affected += UpdateOutcome(
                    connection,
                    transaction,
                    "UPDATE choice_decisions SET outcome = $outcome WHERE match_id = $matchId AND outcome = $unknown;",
                    matchId,
                    outcome);
                affected += UpdateOutcome(
                    connection,
                    transaction,
                    "UPDATE mulligan_decisions SET outcome = $outcome WHERE match_id = $matchId AND outcome = $unknown;",
                    matchId,
                    outcome);

                if (affected <= 0)
                {
                    transaction.Rollback();
                    detail = "no_pending_rows";
                    return false;
                }

                transaction.Commit();
                detail = $"outcome={outcome}, rows={affected}";
                return true;
            }
        }

        private void EnsureSchema()
        {
            if (_schemaEnsured)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"CREATE TABLE IF NOT EXISTS matches (
                    match_id TEXT PRIMARY KEY,
                    outcome INTEGER NOT NULL DEFAULT 0,
                    created_at_ms INTEGER NOT NULL,
                    updated_at_ms INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS action_decisions (
                    decision_id TEXT PRIMARY KEY,
                    match_id TEXT NOT NULL,
                    payload_signature TEXT NOT NULL,
                    seed TEXT NOT NULL,
                    teacher_action_command TEXT NOT NULL,
                    board_snapshot_json TEXT NOT NULL,
                    context_snapshot_json TEXT NOT NULL,
                    mapping_status INTEGER NOT NULL,
                    outcome INTEGER NOT NULL DEFAULT 0,
                    created_at_ms INTEGER NOT NULL,
                    teacher_mapped_candidate_id TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS action_candidates (
                    candidate_id TEXT PRIMARY KEY,
                    decision_id TEXT NOT NULL,
                    action_command TEXT NOT NULL,
                    action_type TEXT NOT NULL,
                    source_card_id TEXT NOT NULL,
                    target_card_id TEXT NOT NULL,
                    candidate_snapshot_json TEXT NOT NULL,
                    candidate_features_json TEXT NOT NULL,
                    is_teacher_pick INTEGER NOT NULL,
                    FOREIGN KEY(decision_id) REFERENCES action_decisions(decision_id)
                );
                CREATE TABLE IF NOT EXISTS choice_decisions (
                    decision_id TEXT PRIMARY KEY,
                    match_id TEXT NOT NULL,
                    payload_signature TEXT NOT NULL,
                    deck_signature TEXT NOT NULL,
                    mode TEXT NOT NULL,
                    origin_card_id TEXT NOT NULL,
                    pending_origin_json TEXT NOT NULL,
                    options_snapshot_json TEXT NOT NULL,
                    teacher_selected_entity_ids_json TEXT NOT NULL,
                    local_selected_entity_ids_json TEXT NOT NULL,
                    board_snapshot_json TEXT NOT NULL,
                    context_snapshot_json TEXT NOT NULL,
                    outcome INTEGER NOT NULL DEFAULT 0,
                    created_at_ms INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS mulligan_decisions (
                    decision_id TEXT PRIMARY KEY,
                    match_id TEXT NOT NULL,
                    deck_signature TEXT NOT NULL,
                    seed TEXT NOT NULL,
                    offered_cards_json TEXT NOT NULL,
                    teacher_keeps_json TEXT NOT NULL,
                    local_keeps_json TEXT NOT NULL,
                    board_snapshot_json TEXT NOT NULL,
                    context_snapshot_json TEXT NOT NULL,
                    outcome INTEGER NOT NULL DEFAULT 0,
                    created_at_ms INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_action_decisions_match_id ON action_decisions(match_id);
                CREATE INDEX IF NOT EXISTS idx_choice_decisions_match_id ON choice_decisions(match_id);
                CREATE INDEX IF NOT EXISTS idx_mulligan_decisions_match_id ON mulligan_decisions(match_id);
                CREATE INDEX IF NOT EXISTS idx_action_candidates_decision_id ON action_candidates(decision_id);";
            command.ExecuteNonQuery();

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
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
            return connection;
        }

        private static bool UpsertMatch(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string matchId,
            long createdAtMs,
            bool allowOutcomeRefresh)
        {
            if (string.IsNullOrWhiteSpace(matchId))
            {
                return true;
            }

            var now = createdAtMs > 0 ? createdAtMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = allowOutcomeRefresh
                ? @"INSERT INTO matches(match_id, outcome, created_at_ms, updated_at_ms)
                        VALUES ($matchId, $outcome, $createdAtMs, $updatedAtMs)
                    ON CONFLICT(match_id) DO UPDATE SET
                        outcome = excluded.outcome,
                        updated_at_ms = excluded.updated_at_ms;"
                : @"INSERT OR IGNORE INTO matches(match_id, outcome, created_at_ms, updated_at_ms)
                        VALUES ($matchId, $outcome, $createdAtMs, $updatedAtMs);";
            command.Parameters.AddWithValue("$matchId", matchId);
            command.Parameters.AddWithValue("$outcome", (int)LearnedMatchOutcome.Unknown);
            command.Parameters.AddWithValue("$createdAtMs", now);
            command.Parameters.AddWithValue("$updatedAtMs", now);
            command.ExecuteNonQuery();
            return true;
        }

        private static int UpdateOutcome(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            string matchId,
            LearnedMatchOutcome outcome)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$matchId", matchId);
            command.Parameters.AddWithValue("$outcome", (int)outcome);
            command.Parameters.AddWithValue("$unknown", (int)LearnedMatchOutcome.Unknown);
            command.Parameters.AddWithValue("$updatedAtMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return command.ExecuteNonQuery();
        }
    }
}
