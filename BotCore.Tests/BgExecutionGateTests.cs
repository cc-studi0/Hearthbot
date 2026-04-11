using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BgExecutionGateTests
    {
        private const string SampleState =
            "PHASE=RECRUIT|TURN=5|HERO=1,HERO_01,30,0|HP=2,HERO_POWER_01,1,2" +
            "|SHOP=100,BG_ABC,2,3,1,1,,3,4;101,BG_DEF,4,4,1,2,,3,4;102,BG_ABC,5,5,1,3,,3,4" +
            "|HAND=200,BG_HAND_A,3,3,1,1,,3,4" +
            "|BOARD=300,BG_BOARD_A,2,2,1,1,,3,4;301,BG_BOARD_B,3,3,1,2,,3,4";

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

        [Fact]
        public void Snapshot_ParsesShopHandBoard()
        {
            var snap = BgExecutionGate.ParseZones(SampleState);

            Assert.Equal(3, snap.Shop.Count);
            Assert.Equal(100, snap.Shop[1].EntityId);
            Assert.Equal("BG_ABC", snap.Shop[1].CardId);
            Assert.Equal(102, snap.Shop[3].EntityId);

            Assert.Single(snap.Hand);
            Assert.Equal(200, snap.Hand[1].EntityId);

            Assert.Equal(2, snap.Board.Count);
            Assert.Equal(301, snap.Board[2].EntityId);
        }

        [Fact]
        public void Snapshot_FindShopByCardId_PicksNearest()
        {
            var snap = BgExecutionGate.ParseZones(SampleState);
            var hit = snap.FindByCardId(BgZone.Shop, "BG_ABC", originalPosition: 2);
            Assert.True(hit.HasValue);
            Assert.Equal(1, hit.Value.Position);
            Assert.Equal(100, hit.Value.EntityId);
        }

        [Fact]
        public void Snapshot_FindShopByCardId_ReturnsNullWhenMissing()
        {
            var snap = BgExecutionGate.ParseZones(SampleState);
            var hit = snap.FindByCardId(BgZone.Shop, "BG_MISSING", originalPosition: 1);
            Assert.False(hit.HasValue);
        }

        [Fact]
        public void Snapshot_EmptyState_ReturnsEmptyZones()
        {
            var snap = BgExecutionGate.ParseZones("");
            Assert.Empty(snap.Shop);
            Assert.Empty(snap.Hand);
            Assert.Empty(snap.Board);
        }
    }
}
