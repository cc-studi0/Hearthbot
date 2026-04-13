using System;
using System.Collections.Generic;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BgExecutionGateTests
    {
        private sealed class FakeState
        {
            public string Current;
            public FakeState(string initial) { Current = initial; }
        }

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
        public void Parse_RerollStillUsesOtherKind()
        {
            var spec = BgExecutionGate.ParseCommand("BG_REROLL");
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

        [Fact]
        public void Resolve_BuyHappyPath_ReturnsAsIs()
        {
            var spec = BgExecutionGate.ParseCommand("BG_BUY|100|1|BG_ABC");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.AsIs, res.Outcome);
            Assert.Equal("BG_BUY|100|1|BG_ABC", res.RewrittenCommand);
        }

        [Fact]
        public void Resolve_BuyStaleEntityId_Retargets()
        {
            var spec = BgExecutionGate.ParseCommand("BG_BUY|999|1|BG_ABC");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.Retargeted, res.Outcome);
            Assert.Equal("BG_BUY|100|1|BG_ABC", res.RewrittenCommand);
        }

        [Fact]
        public void Resolve_BuyPositionShifted_Retargets()
        {
            var spec = BgExecutionGate.ParseCommand("BG_BUY|101|1|BG_DEF");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.Retargeted, res.Outcome);
            Assert.Equal("BG_BUY|101|2|BG_DEF", res.RewrittenCommand);
        }

        [Fact]
        public void Resolve_BuyDuplicateCard_PicksNearestToOriginalPosition()
        {
            // BG_ABC 在 pos=1 和 pos=3；原推荐 pos=2，距离相同取低位 pos=1
            var spec = BgExecutionGate.ParseCommand("BG_BUY|9999|2|BG_ABC");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.Retargeted, res.Outcome);
            Assert.Equal("BG_BUY|100|1|BG_ABC", res.RewrittenCommand);
        }

        [Fact]
        public void Resolve_BuyCardIdMissing_Aborts()
        {
            var spec = BgExecutionGate.ParseCommand("BG_BUY|999|2|BG_GONE");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.Aborted, res.Outcome);
        }

        [Fact]
        public void Resolve_BuyLegacyNoCardId_Passes()
        {
            var spec = BgExecutionGate.ParseCommand("BG_BUY|100|1");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.AsIs, res.Outcome);
            Assert.Equal("BG_BUY|100|1", res.RewrittenCommand);
        }

        [Fact]
        public void Resolve_SellHappyPath()
        {
            var spec = BgExecutionGate.ParseCommand("BG_SELL|300|BG_BOARD_A");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.AsIs, res.Outcome);
        }

        [Fact]
        public void Resolve_SellCardIdMissing_Aborts()
        {
            var spec = BgExecutionGate.ParseCommand("BG_SELL|300|BG_SOLD_ALREADY");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.Aborted, res.Outcome);
        }

        [Fact]
        public void Resolve_PlayHappyPath()
        {
            var spec = BgExecutionGate.ParseCommand("BG_PLAY|200|0|1|BG_HAND_A");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.AsIs, res.Outcome);
        }

        [Fact]
        public void Resolve_PlayHandCardMissing_Aborts()
        {
            var spec = BgExecutionGate.ParseCommand("BG_PLAY|200|0|1|BG_NOT_IN_HAND");
            var snap = BgExecutionGate.ParseZones(SampleState);
            var res = BgExecutionGate.Resolve(spec, snap);
            Assert.Equal(BgResolutionOutcome.Aborted, res.Outcome);
        }

        [Fact]
        public void Execute_BuyHappyPath_ReturnsCompleted()
        {
            var state = new FakeState(SampleState);
            var sendLog = new List<string>();
            Func<string, string> send = cmd =>
            {
                sendLog.Add(cmd);
                state.Current = state.Current.Replace("100,BG_ABC,2,3,1,1,,3,4;", "");
                return "OK";
            };
            Func<string> read = () => state.Current;

            var gate = new BgExecutionGateRunner(send, read, probeTimeoutMs: 200, probeIntervalMs: 5, fallbackSleepMs: 50, sleep: _ => { });
            var result = gate.Execute("BG_BUY|100|1|BG_ABC");

            Assert.Equal(BgGateOutcome.Completed, result.Outcome);
            Assert.Equal("BG_BUY|100|1|BG_ABC", result.ExecutedCommand);
            Assert.Single(sendLog);
        }

        [Fact]
        public void Execute_BuyStaleEntityId_RewritesAndSucceeds()
        {
            var state = new FakeState(SampleState);
            var sendLog = new List<string>();
            Func<string, string> send = cmd =>
            {
                sendLog.Add(cmd);
                state.Current = state.Current.Replace("100,BG_ABC,2,3,1,1,,3,4;", "");
                return "OK";
            };
            Func<string> read = () => state.Current;

            var gate = new BgExecutionGateRunner(send, read, 200, 5, 50, _ => { });
            var result = gate.Execute("BG_BUY|9999|1|BG_ABC");

            Assert.Equal(BgGateOutcome.Retargeted, result.Outcome);
            Assert.Equal("BG_BUY|100|1|BG_ABC", result.ExecutedCommand);
            Assert.Equal("BG_BUY|100|1|BG_ABC", sendLog[0]);
        }

        [Fact]
        public void Execute_CardIdMissing_Aborts()
        {
            var state = new FakeState(SampleState);
            var sendLog = new List<string>();
            Func<string, string> send = cmd => { sendLog.Add(cmd); return "OK"; };
            Func<string> read = () => state.Current;

            var gate = new BgExecutionGateRunner(send, read, 200, 5, 50, _ => { });
            var result = gate.Execute("BG_BUY|100|1|BG_GONE");

            Assert.Equal(BgGateOutcome.Aborted, result.Outcome);
            Assert.Empty(sendLog);
        }

        [Fact]
        public void Execute_SendFails_ReturnsFailed()
        {
            var state = new FakeState(SampleState);
            Func<string, string> send = cmd => "FAIL:bg_test";
            Func<string> read = () => state.Current;

            var gate = new BgExecutionGateRunner(send, read, 200, 5, 50, _ => { });
            var result = gate.Execute("BG_BUY|100|1|BG_ABC");

            Assert.Equal(BgGateOutcome.Failed, result.Outcome);
        }

        [Fact]
        public void Execute_ProbeTimeout_ReturnsFallbackAndSleeps()
        {
            var state = new FakeState(SampleState);
            var sendLog = new List<string>();
            var sleeps = new List<int>();
            Func<string, string> send = cmd => { sendLog.Add(cmd); return "OK"; };
            Func<string> read = () => state.Current; // 永不变

            var gate = new BgExecutionGateRunner(send, read, probeTimeoutMs: 50, probeIntervalMs: 5, fallbackSleepMs: 50, sleep: ms => sleeps.Add(ms));
            var result = gate.Execute("BG_BUY|100|1|BG_ABC");

            Assert.Equal(BgGateOutcome.CompletedWithFallback, result.Outcome);
            Assert.Contains(50, sleeps);
        }

        [Fact]
        public void Execute_OtherCommand_StateHashChanges_Completed()
        {
            var state = new FakeState(SampleState);
            Func<string, string> send = cmd =>
            {
                state.Current += "|TAVERN_UP=1";
                return "OK";
            };
            Func<string> read = () => state.Current;

            var gate = new BgExecutionGateRunner(send, read, 200, 5, 50, _ => { });
            var result = gate.Execute("BG_TAVERN_UP");

            Assert.Equal(BgGateOutcome.Completed, result.Outcome);
        }
    }
}
