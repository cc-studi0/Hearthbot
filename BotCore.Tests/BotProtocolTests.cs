using System;
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class BotProtocolTests
    {
        [Fact]
        public void IsSeedResponse_AcceptsEndgamePending()
        {
            Assert.True(BotProtocol.IsSeedResponse(BotProtocol.EndgamePending));
            Assert.True(BotProtocol.IsCrossCommandResponse(BotProtocol.EndgamePending));
            Assert.True(BotProtocol.IsEndgamePendingState(BotProtocol.EndgamePending));
        }

        [Fact]
        public void IsCrossCommandResponse_TreatsStatusRepliesAsCrossCommand()
        {
            Assert.True(BotProtocol.IsCrossCommandResponse("OK:center_multi"));
            Assert.True(BotProtocol.IsCrossCommandResponse("ERROR:coroutine_timeout"));
            Assert.True(BotProtocol.IsCrossCommandResponse("ENDGAME:1:Victory"));
            Assert.True(BotProtocol.IsCrossCommandResponse(BotProtocol.NoDialog));
            Assert.True(BotProtocol.IsCrossCommandResponse("DIALOG:AlertPopup:OK"));
        }

        [Fact]
        public void TryParseScene_RejectsNonSceneStatusReply()
        {
            Assert.False(BotProtocol.TryParseScene("OK:center_multi", out var scene));
            Assert.Null(scene);
        }

        [Fact]
        public void TryParseEndgameState_ParsesShownAndClass()
        {
            Assert.True(BotProtocol.TryParseEndgameState("ENDGAME:1:VictoryScreen", out var shown, out var endgameClass));
            Assert.True(shown);
            Assert.Equal("VictoryScreen", endgameClass);
        }

        [Fact]
        public void UpdatePostGameLobbyConfirmCount_RequiresStableLobbyAndClearsOnEndgame()
        {
            var count = BotProtocol.UpdatePostGameLobbyConfirmCount(0, "TOURNAMENT", endgameShown: false);
            Assert.Equal(1, count);

            count = BotProtocol.UpdatePostGameLobbyConfirmCount(count, "TOURNAMENT", endgameShown: false);
            Assert.Equal(2, count);

            count = BotProtocol.UpdatePostGameLobbyConfirmCount(count, "TOURNAMENT", endgameShown: true);
            Assert.Equal(0, count);
        }

        [Fact]
        public void IsPostGameNavigationDelayActive_ExpiresAfterGuardWindow()
        {
            var now = DateTime.UtcNow;
            Assert.True(BotProtocol.IsPostGameNavigationDelayActive(now.AddSeconds(-1), now, TimeSpan.FromSeconds(2)));
            Assert.False(BotProtocol.IsPostGameNavigationDelayActive(now.AddSeconds(-3), now, TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ShouldClickPostGameDismiss_WhenEndgameAlreadyPending()
        {
            Assert.True(BotProtocol.ShouldClickPostGameDismiss("GAMEPLAY", BotProtocol.EndgamePending, endgameShown: false));
            Assert.True(BotProtocol.ShouldClickPostGameDismiss("GAMEPLAY", BotProtocol.EndgamePending, endgameShown: true));
            Assert.True(BotProtocol.ShouldClickPostGameDismiss("GAMEPLAY", "NO_GAME", endgameShown: false));
            Assert.False(BotProtocol.ShouldClickPostGameDismiss("HUB", BotProtocol.EndgamePending, endgameShown: true));
        }

        [Fact]
        public void ShouldAbortPostGameDismiss_WhenGameplayResponsesResume()
        {
            Assert.True(BotProtocol.ShouldAbortPostGameDismiss("SEED:abc"));
            Assert.True(BotProtocol.ShouldAbortPostGameDismiss("MULLIGAN"));
            Assert.True(BotProtocol.ShouldAbortPostGameDismiss("NOT_OUR_TURN"));
            Assert.False(BotProtocol.ShouldAbortPostGameDismiss(BotProtocol.EndgamePending));
            Assert.False(BotProtocol.ShouldAbortPostGameDismiss("NO_GAME"));
        }

        [Fact]
        public void BlockingDialogResponses_ParseKnownPayload()
        {
            Assert.True(BotProtocol.IsBlockingDialogResponse(BotProtocol.NoDialog));
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:AlertPopup:OK"));
            Assert.True(BotProtocol.TryParseBlockingDialog("DIALOG:AlertPopup:OK", out var dialogType, out var buttonLabel));
            Assert.Equal("AlertPopup", dialogType);
            Assert.Equal("OK", buttonLabel);
            Assert.False(BotProtocol.TryParseBlockingDialog(BotProtocol.NoDialog, out _, out _));
        }

        [Fact]
        public void BlockingDialogButtonLabels_OnlyAllowSafeDismissButtons()
        {
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("OK"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("确认"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("确定"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("关闭"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("返回"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("取消"));

            Assert.True(BotProtocol.IsRetryBlockingDialogButtonLabel("Reconnect"));
            Assert.True(BotProtocol.IsRetryBlockingDialogButtonLabel("重试"));
            Assert.True(BotProtocol.IsRetryBlockingDialogButtonLabel("重新连接"));
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("Reconnect"));
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("重试"));
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("重新连接"));
        }

        [Fact]
        public void UpdateMatchmakingLobbyConfirmCount_OnlyCountsStableLobbyNoGameNotFinding()
        {
            var count = BotProtocol.UpdateMatchmakingLobbyConfirmCount(0, "TOURNAMENT", "NO_GAME", "NO");
            Assert.Equal(1, count);

            count = BotProtocol.UpdateMatchmakingLobbyConfirmCount(count, "HUB", "NO_GAME", "NO");
            Assert.Equal(2, count);

            count = BotProtocol.UpdateMatchmakingLobbyConfirmCount(count, "UNKNOWN", "NO_GAME", "NO");
            Assert.Equal(0, count);

            count = BotProtocol.UpdateMatchmakingLobbyConfirmCount(1, "TOURNAMENT", "SEED:abc", "NO");
            Assert.Equal(0, count);

            count = BotProtocol.UpdateMatchmakingLobbyConfirmCount(1, "TOURNAMENT", "NO_GAME", "YES");
            Assert.Equal(0, count);
        }

        [Fact]
        public void IsGameLoadingOrGameplayResponse_RecognizesOnlyInGameSignals()
        {
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse("SEED:abc"));
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse("MULLIGAN"));
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse("NOT_OUR_TURN"));
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse(BotProtocol.EndgamePending));
            Assert.False(BotProtocol.IsGameLoadingOrGameplayResponse("NO_GAME"));
            Assert.False(BotProtocol.IsGameLoadingOrGameplayResponse("UNKNOWN"));
        }
    }
}
