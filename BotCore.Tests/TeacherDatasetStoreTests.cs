using System;
using System.IO;
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

        private static long ExecuteLongScalar(string dbPath, string sql)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
