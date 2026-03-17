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
            Assert.True(BotProtocol.IsCrossCommandResponse("ENDGAME:1:VictoryScreen"));
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
        public void HubButtonsResponse_ParsesStructuredLobbyButtons()
        {
            var resp = "HUB_BUTTONS:"
                + "scene=HUB|buttonKey=traditional|label=%E4%BC%A0%E7%BB%9F%E5%AF%B9%E6%88%98|enabled=1|screenX=120|screenY=340|detail=Box.m_PlayButton"
                + ";scene=HUB|buttonKey=arena|label=%E7%AB%9E%E6%8A%80%E6%A8%A1%E5%BC%8F|enabled=0|screenX=0|screenY=0|detail=Box.m_ArenaButton%3Amissing";

            Assert.True(BotProtocol.IsHubButtonsResponse(resp));
            Assert.True(BotProtocol.IsCrossCommandResponse(resp));
            Assert.True(BotProtocol.TryParseHubButtons(resp, out var buttons));
            Assert.Equal(2, buttons.Count);

            Assert.Equal("HUB", buttons[0].Scene);
            Assert.Equal("traditional", buttons[0].ButtonKey);
            Assert.Equal("传统对战", buttons[0].Label);
            Assert.True(buttons[0].Enabled);
            Assert.Equal(120, buttons[0].ScreenX);
            Assert.Equal(340, buttons[0].ScreenY);
            Assert.Equal("Box.m_PlayButton", buttons[0].Detail);

            Assert.Equal("arena", buttons[1].ButtonKey);
            Assert.False(buttons[1].Enabled);
            Assert.Equal("Box.m_ArenaButton:missing", buttons[1].Detail);
        }

        [Fact]
        public void OtherModeButtonsResponse_ParsesStructuredModeList()
        {
            var resp = "OTHER_MODE_BUTTONS:"
                + "gameModeRecordId=101|name=%E4%B9%B1%E6%96%97%E6%A8%A1%E5%BC%8F|description=%E6%AF%8F%E5%91%A8%E4%B8%80%E4%B9%B1%E6%96%97|linkedScene=TAVERN_BRAWL|modeKey=TAVERN_BRAWL|isDownloadRequired=0|isDownloading=0"
                + ";gameModeRecordId=202|name=%E4%BD%A3%E5%85%B5%E6%88%98%E7%BA%AA|description=%E7%BB%A7%E7%BB%AD%E5%89%8D%E8%BF%9B|linkedScene=LETTUCE_VILLAGE|modeKey=MERCENARIES|isDownloadRequired=1|isDownloading=1";

            Assert.True(BotProtocol.IsOtherModeButtonsResponse(resp));
            Assert.True(BotProtocol.IsCrossCommandResponse(resp));
            Assert.True(BotProtocol.TryParseOtherModeButtons(resp, out var buttons));
            Assert.Equal(2, buttons.Count);

            Assert.Equal(101, buttons[0].GameModeRecordId);
            Assert.Equal("乱斗模式", buttons[0].Name);
            Assert.Equal("每周一乱斗", buttons[0].Description);
            Assert.Equal("TAVERN_BRAWL", buttons[0].LinkedScene);
            Assert.Equal("TAVERN_BRAWL", buttons[0].ModeKey);
            Assert.False(buttons[0].IsDownloadRequired);
            Assert.False(buttons[0].IsDownloading);

            Assert.Equal(202, buttons[1].GameModeRecordId);
            Assert.Equal("佣兵战纪", buttons[1].Name);
            Assert.True(buttons[1].IsDownloadRequired);
            Assert.True(buttons[1].IsDownloading);
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
        public void ShouldClickVisiblePostGameDismiss_RequiresVisibleEndgameScreen()
        {
            Assert.True(BotProtocol.ShouldClickVisiblePostGameDismiss("GAMEPLAY", endgameShown: true));
            Assert.False(BotProtocol.ShouldClickVisiblePostGameDismiss("GAMEPLAY", endgameShown: false));
            Assert.False(BotProtocol.ShouldClickVisiblePostGameDismiss("HUB", endgameShown: true));
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

            Assert.True(BotProtocol.TryParseBlockingDialog(
                $"DIALOG:{BotProtocol.StartupRatingsDialogType}:{BotProtocol.StartupRatingsButtonLabel}",
                out dialogType,
                out buttonLabel));
            Assert.Equal(BotProtocol.StartupRatingsDialogType, dialogType);
            Assert.Equal(BotProtocol.StartupRatingsButtonLabel, buttonLabel);

            Assert.False(BotProtocol.TryParseBlockingDialog(BotProtocol.NoDialog, out _, out _));
        }

        [Fact]
        public void BlockingDialogButtonLabels_OnlyAllowSafeDismissButtons()
        {
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("OK"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("\u786e\u8ba4"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("\u786e\u5b9a"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("\u5173\u95ed"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("\u8fd4\u56de"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("\u53d6\u6d88"));
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel(BotProtocol.StartupRatingsButtonLabel));

            Assert.True(BotProtocol.IsRetryBlockingDialogButtonLabel("Reconnect"));
            Assert.True(BotProtocol.IsRetryBlockingDialogButtonLabel("\u91cd\u8bd5"));
            Assert.True(BotProtocol.IsRetryBlockingDialogButtonLabel("\u91cd\u65b0\u8fde\u63a5"));
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("Reconnect"));
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("\u91cd\u8bd5"));
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("\u91cd\u65b0\u8fde\u63a5"));
        }

        [Fact]
        public void NavigationBlockedScenes_BlockStartupAndLoginUntilLobbyReady()
        {
            Assert.True(BotProtocol.IsNavigationBlockedScene("STARTUP"));
            Assert.True(BotProtocol.IsNavigationBlockedScene("LOGIN"));
            Assert.False(BotProtocol.IsNavigationBlockedScene("HUB"));
            Assert.False(BotProtocol.IsStableLobbyScene("LOGIN"));
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
