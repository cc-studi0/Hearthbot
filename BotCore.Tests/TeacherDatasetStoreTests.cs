using System;
using System.IO;
using System.Threading;
using BotMain.Learning;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BotCore.Tests
{
    public class TeacherDatasetStoreTests
    {
        [Fact]
        public void DecisionRecord_UsesStableSampleKey()
        {
            var record = new TeacherActionDecisionRecord
            {
                MatchId = "match-1",
                PayloadSignature = "payload-1",
                TeacherActionCommand = "ATTACK|101|202",
                Seed = "seed-1"
            };

            Assert.False(string.IsNullOrWhiteSpace(record.BuildSampleKey()));
            Assert.Equal(record.BuildSampleKey(), record.BuildSampleKey());
            var initialKey = record.BuildSampleKey();

            record.Seed = "seed-2";
            var updatedKey = record.BuildSampleKey();

            Assert.NotEqual(initialKey, updatedKey);
        }

        [Fact]
        public void CandidateRecord_FlagsTeacherPick()
        {
            var candidate = new TeacherActionCandidateRecord
            {
                ActionCommand = "PLAY|101|0|0",
                IsTeacherPick = true
            };

            Assert.True(candidate.IsTeacherPick);
            var defaultCandidate = new TeacherActionCandidateRecord();
            Assert.False(defaultCandidate.IsTeacherPick);
        }

        [Fact]
        public void SqliteTeacherDatasetStore_PersistsDecisionAndCandidates()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "dataset-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var store = new SqliteTeacherDatasetStore(dbPath);
                var decision = new TeacherActionDecisionRecord
                {
                    MatchId = "match-1",
                    DecisionId = "decision-1",
                    PayloadSignature = "payload-1",
                    TeacherActionCommand = "ATTACK|101|202",
                    MappingStatus = TeacherActionMappingStatus.Mapped
                };
                var candidates = new[]
                {
                    new TeacherActionCandidateRecord
                    {
                        CandidateId = "c1",
                        DecisionId = "decision-1",
                        ActionCommand = "ATTACK|101|202",
                        ActionType = "Attack",
                        IsTeacherPick = true
                    }
                };

                Assert.True(store.TryStoreActionDecision(decision, candidates, out _));
                Assert.False(store.TryStoreActionDecision(decision, candidates, out _));

                Assert.Equal(1L, ExecuteLongScalar(dbPath, "SELECT COUNT(1) FROM action_decisions;"));
                Assert.Equal(1L, ExecuteLongScalar(dbPath, "SELECT COUNT(1) FROM action_candidates;"));
                Assert.Equal(1L, ExecuteLongScalar(dbPath, "SELECT COUNT(1) FROM matches;"));
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        [Fact]
        public void SqliteTeacherDatasetStore_RejectsDuplicateCandidateIdWithoutOverwriting()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "dataset-candidate-dup-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var store = new SqliteTeacherDatasetStore(dbPath);
                var firstDecision = new TeacherActionDecisionRecord
                {
                    MatchId = "match-dup",
                    DecisionId = "decision-dup-1",
                    PayloadSignature = "payload-dup-1",
                    TeacherActionCommand = "ATTACK|101|202",
                    MappingStatus = TeacherActionMappingStatus.Mapped
                };
                var secondDecision = new TeacherActionDecisionRecord
                {
                    MatchId = "match-dup",
                    DecisionId = "decision-dup-2",
                    PayloadSignature = "payload-dup-2",
                    TeacherActionCommand = "PLAY|101|0|0",
                    MappingStatus = TeacherActionMappingStatus.Mapped
                };

                Assert.True(store.TryStoreActionDecision(
                    firstDecision,
                    new[]
                    {
                        new TeacherActionCandidateRecord
                        {
                            CandidateId = "dup-candidate",
                            DecisionId = firstDecision.DecisionId,
                            ActionCommand = "ATTACK|101|202",
                            ActionType = "Attack",
                            IsTeacherPick = true
                        }
                    },
                    out _));

                Assert.False(store.TryStoreActionDecision(
                    secondDecision,
                    new[]
                    {
                        new TeacherActionCandidateRecord
                        {
                            CandidateId = "dup-candidate",
                            DecisionId = secondDecision.DecisionId,
                            ActionCommand = "PLAY|101|0|0",
                            ActionType = "Play",
                            IsTeacherPick = true
                        }
                    },
                    out _));

                Assert.Equal(1L, ExecuteLongScalar(dbPath, "SELECT COUNT(1) FROM action_decisions;"));
                Assert.Equal("decision-dup-1", ExecuteStringScalar(dbPath, "SELECT decision_id FROM action_candidates WHERE candidate_id = 'dup-candidate';"));
                Assert.Equal("ATTACK|101|202", ExecuteStringScalar(dbPath, "SELECT action_command FROM action_candidates WHERE candidate_id = 'dup-candidate';"));
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        [Fact]
        public void SqliteTeacherDatasetStore_PersistsChoiceAndMulliganDecisions()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "dataset-choice-mulligan-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var store = new SqliteTeacherDatasetStore(dbPath);
                var choiceDecision = new TeacherChoiceDecisionRecord
                {
                    DecisionId = "choice-1",
                    MatchId = "match-choice-mulligan",
                    PayloadSignature = "payload-choice-1",
                    DeckSignature = "deck-1",
                    Mode = "DISCOVER",
                    OriginCardId = "SRC_001"
                };
                var mulliganDecision = new TeacherMulliganDecisionRecord
                {
                    DecisionId = "mulligan-1",
                    MatchId = "match-choice-mulligan",
                    DeckSignature = "deck-1",
                    Seed = "seed-1",
                    OfferedCardsJson = "[\"CARD_A\",\"CARD_B\"]"
                };

                Assert.True(store.TryStoreChoiceDecision(choiceDecision, out _));
                Assert.True(store.TryStoreMulliganDecision(mulliganDecision, out _));

                Assert.Equal(1L, ExecuteLongScalar(dbPath, "SELECT COUNT(1) FROM choice_decisions;"));
                Assert.Equal(1L, ExecuteLongScalar(dbPath, "SELECT COUNT(1) FROM mulligan_decisions;"));
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        [Fact]
        public void SqliteTeacherDatasetStore_AppliesOutcomeToMatchRows()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "dataset-outcome-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var store = new SqliteTeacherDatasetStore(dbPath);
                var decision = new TeacherActionDecisionRecord
                {
                    MatchId = "match-2",
                    DecisionId = "decision-2",
                    PayloadSignature = "payload-2",
                    TeacherActionCommand = "PLAY|101|0|0",
                    MappingStatus = TeacherActionMappingStatus.NoMatch
                };

                Assert.True(store.TryStoreActionDecision(decision, Array.Empty<TeacherActionCandidateRecord>(), out _));
                Assert.True(store.TryApplyMatchOutcome("match-2", LearnedMatchOutcome.Win, out _));
                Assert.False(store.TryApplyMatchOutcome("match-2", LearnedMatchOutcome.Win, out _));

                Assert.Equal((long)LearnedMatchOutcome.Win, ExecuteLongScalar(dbPath, "SELECT outcome FROM matches WHERE match_id = 'match-2';"));
                Assert.Equal((long)LearnedMatchOutcome.Win, ExecuteLongScalar(dbPath, "SELECT outcome FROM action_decisions WHERE decision_id = 'decision-2';"));
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        [Fact]
        public void SqliteTeacherDatasetStore_AppliesOutcomeToChoiceAndMulliganRows()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "dataset-outcome-choice-mulligan-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var store = new SqliteTeacherDatasetStore(dbPath);
                var choiceDecision = new TeacherChoiceDecisionRecord
                {
                    DecisionId = "choice-outcome-1",
                    MatchId = "match-outcome-choice-mulligan",
                    PayloadSignature = "payload-choice-outcome",
                    DeckSignature = "deck-1",
                    Mode = "DISCOVER",
                    OriginCardId = "SRC_001"
                };
                var mulliganDecision = new TeacherMulliganDecisionRecord
                {
                    DecisionId = "mulligan-outcome-1",
                    MatchId = "match-outcome-choice-mulligan",
                    DeckSignature = "deck-1",
                    Seed = "seed-1",
                    OfferedCardsJson = "[\"CARD_A\",\"CARD_B\"]"
                };

                Assert.True(store.TryStoreChoiceDecision(choiceDecision, out _));
                Assert.True(store.TryStoreMulliganDecision(mulliganDecision, out _));
                Assert.True(store.TryApplyMatchOutcome("match-outcome-choice-mulligan", LearnedMatchOutcome.Loss, out _));
                Assert.False(store.TryApplyMatchOutcome("match-outcome-choice-mulligan", LearnedMatchOutcome.Loss, out _));

                Assert.Equal((long)LearnedMatchOutcome.Loss, ExecuteLongScalar(dbPath, "SELECT outcome FROM matches WHERE match_id = 'match-outcome-choice-mulligan';"));
                Assert.Equal((long)LearnedMatchOutcome.Loss, ExecuteLongScalar(dbPath, "SELECT outcome FROM choice_decisions WHERE decision_id = 'choice-outcome-1';"));
                Assert.Equal((long)LearnedMatchOutcome.Loss, ExecuteLongScalar(dbPath, "SELECT outcome FROM mulligan_decisions WHERE decision_id = 'mulligan-outcome-1';"));
            }
            finally
            {
                TryDelete(dbPath);
            }
        }

        private static long ExecuteLongScalar(string dbPath, string sql)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        private static string ExecuteStringScalar(string dbPath, string sql)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        private static void TryDelete(string path)
        {
            SqliteConnection.ClearAllPools();
            foreach (var target in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            {
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    try
                    {
                        if (File.Exists(target))
                        {
                            File.Delete(target);
                        }

                        break;
                    }
                    catch
                    {
                        Thread.Sleep(25);
                    }
                }
            }
        }
    }
}
