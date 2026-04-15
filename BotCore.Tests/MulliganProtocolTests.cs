using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class MulliganProtocolTests
    {
        [Fact]
        public void TryParseState_ParsesLegacyPayload()
        {
            Assert.True(MulliganProtocol.TryParseState("3|8|CORE_EX1_169,17;CORE_CS2_029,23", out var snapshot, out var error));

            Assert.Null(error);
            Assert.NotNull(snapshot);
            Assert.Equal(3, snapshot.OwnClass);
            Assert.Equal(8, snapshot.EnemyClass);
            Assert.False(snapshot.HasCoin);
            Assert.Collection(
                snapshot.Choices,
                choice =>
                {
                    Assert.Equal("CORE_EX1_169", choice.CardId);
                    Assert.Equal(17, choice.EntityId);
                },
                choice =>
                {
                    Assert.Equal("CORE_CS2_029", choice.CardId);
                    Assert.Equal(23, choice.EntityId);
                });
        }

        [Fact]
        public void TryParseState_ParsesHasCoinAndIgnoresLaterFields()
        {
            Assert.True(
                MulliganProtocol.TryParseState(
                    "2|4|CORE_EX1_169,17;CORE_CS2_029,23|1|detail=manager",
                    out var snapshot,
                    out var error));

            Assert.Null(error);
            Assert.NotNull(snapshot);
            Assert.Equal(2, snapshot.OwnClass);
            Assert.Equal(4, snapshot.EnemyClass);
            Assert.True(snapshot.HasCoin);
            Assert.Equal(2, snapshot.Choices.Count);
            Assert.Equal("CORE_EX1_169", snapshot.Choices[0].CardId);
            Assert.Equal(17, snapshot.Choices[0].EntityId);
            Assert.Equal("CORE_CS2_029", snapshot.Choices[1].CardId);
            Assert.Equal(23, snapshot.Choices[1].EntityId);
        }

        [Theory]
        [InlineData("FAIL:wait:mulligan_ready:starting_cards_cardid_not_ready")]
        [InlineData("FAIL:wait:choice_packet:choice_id_not_ready")]
        [InlineData("FAIL:wait:mouse_fallback:entity_set_changed")]
        [InlineData("FAIL:mulligan_manager:entity_not_found:17")]
        [InlineData("FAIL:gameplay_not_ready:post_animation_grace")]
        [InlineData("FAIL:mulligan_state_timeout")]
        [InlineData("FAIL:mulligan_state_unavailable")]
        [InlineData("FAIL:mulligan_choices_empty")]
        public void IsTransientFailure_TreatsNewWaitingSignalsAsRetryable(string result)
        {
            Assert.True(MulliganProtocol.IsTransientFailure(result));
        }

        [Fact]
        public void IsTransientFailure_DoesNotTreatChoiceSenderNotFoundAsRetryable()
        {
            Assert.False(MulliganProtocol.IsTransientFailure("FAIL:choice_sender_not_found"));
        }
    }
}
