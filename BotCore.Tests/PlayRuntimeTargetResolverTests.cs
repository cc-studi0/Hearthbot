using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class PlayRuntimeTargetResolverTests
    {
        [Fact]
        public void Resolve_WhenGeneralChoiceContainsHandCandidate_ReturnsHandTargetMode()
        {
            var resolution = PlayRuntimeTargetResolver.Resolve(
                new PlayRuntimeTargetHint
                {
                    OriginalTargetEntityId = 44,
                    CardId = "CATA_499",
                    ZonePosition = 2
                },
                new[]
                {
                    new PlayRuntimeTargetCandidate
                    {
                        EntityId = 91,
                        Zone = "PLAY",
                        ZonePosition = 2,
                        CardId = "CATA_499"
                    },
                    new PlayRuntimeTargetCandidate
                    {
                        EntityId = 132,
                        Zone = "HAND",
                        ZonePosition = 2,
                        CardId = "CATA_499"
                    }
                },
                explicitHandTarget: false,
                rawChoiceType: "GENERAL");

            Assert.Equal(PlayRuntimeTargetMode.HandTarget, resolution.Mode);
            Assert.Equal(132, resolution.ResolvedEntityId);
            Assert.Equal("card_slot", resolution.MatchReason);
        }

        [Fact]
        public void Resolve_WhenHandModeButNoUniqueHandMatch_DoesNotFallBackToBoardEntity()
        {
            var resolution = PlayRuntimeTargetResolver.Resolve(
                new PlayRuntimeTargetHint
                {
                    OriginalTargetEntityId = 91,
                    CardId = "CATA_499",
                    ZonePosition = 2
                },
                new[]
                {
                    new PlayRuntimeTargetCandidate
                    {
                        EntityId = 201,
                        Zone = "HAND",
                        ZonePosition = 1,
                        CardId = "OTHER_001"
                    }
                },
                explicitHandTarget: true,
                rawChoiceType: "GENERAL");

            Assert.Equal(PlayRuntimeTargetMode.HandTarget, resolution.Mode);
            Assert.False(resolution.HasResolvedEntity);
            Assert.Equal(0, resolution.ResolvedEntityId);
        }
    }
}
