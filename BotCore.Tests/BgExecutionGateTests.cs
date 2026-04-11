using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BgExecutionGateTests
    {
        [Fact]
        public void Parse_BuyWithCardId()
        {
            var spec = BgExecutionGate.ParseCommand("BG_BUY|100|2|BG_ABC");
            Assert.Equal(BgCommandKind.Buy, spec.Kind);
            Assert.Equal(100, spec.EntityId);
            Assert.Equal(2, spec.Position);
            Assert.Equal("BG_ABC", spec.ExpectedCardId);
        }

        [Fact]
        public void Parse_BuyLegacyNoCardId()
        {
            var spec = BgExecutionGate.ParseCommand("BG_BUY|100|2");
            Assert.Equal(BgCommandKind.Buy, spec.Kind);
            Assert.Equal(100, spec.EntityId);
            Assert.Equal(2, spec.Position);
            Assert.Equal("", spec.ExpectedCardId);
        }

        [Fact]
        public void Parse_SellWithCardId()
        {
            var spec = BgExecutionGate.ParseCommand("BG_SELL|177|BG_XYZ");
            Assert.Equal(BgCommandKind.Sell, spec.Kind);
            Assert.Equal(177, spec.EntityId);
            Assert.Equal("BG_XYZ", spec.ExpectedCardId);
        }

        [Fact]
        public void Parse_PlayWithCardId()
        {
            var spec = BgExecutionGate.ParseCommand("BG_PLAY|200|300|4|BG_CASTER");
            Assert.Equal(BgCommandKind.Play, spec.Kind);
            Assert.Equal(200, spec.EntityId);
            Assert.Equal(300, spec.TargetEntityId);
            Assert.Equal(4, spec.Position);
            Assert.Equal("BG_CASTER", spec.ExpectedCardId);
        }

        [Fact]
        public void Parse_OtherCommand()
        {
            var spec = BgExecutionGate.ParseCommand("BG_TAVERN_UP");
            Assert.Equal(BgCommandKind.Other, spec.Kind);
        }
    }
}
