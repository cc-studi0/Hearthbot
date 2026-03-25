using BotMain.Learning;
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
    }
}
