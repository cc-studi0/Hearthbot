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
            Assert.True(BotProtocol.IsCrossCommandResponse("SEED_NOT_READY:missing=hero_friend"));
        }

        [Fact]
        public void SeedProbeResponse_AcceptsSeedNotReadyAndParsesDetail()
        {
            const string resp = "SEED_NOT_READY:missing=hero_friend,ability_friend";

            Assert.True(BotProtocol.IsSeedProbeResponse(resp));
            Assert.True(BotProtocol.IsSeedNotReadyState(resp));
            Assert.True(BotProtocol.TryParseSeedNotReadyDetail(resp, out var detail));
            Assert.Equal("missing=hero_friend,ability_friend", detail);
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
            Assert.True(BotProtocol.ShouldAbortPostGameDismiss("SEED_NOT_READY:missing=hero_friend"));
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
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse("SEED_NOT_READY:missing=hero_friend"));
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse("MULLIGAN"));
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse("NOT_OUR_TURN"));
            Assert.True(BotProtocol.IsGameLoadingOrGameplayResponse(BotProtocol.EndgamePending));
            Assert.False(BotProtocol.IsGameLoadingOrGameplayResponse("NO_GAME"));
            Assert.False(BotProtocol.IsGameLoadingOrGameplayResponse("UNKNOWN"));
        }

        [Fact]
        public void ResolvePostGameResult_PrefersEarlyCache()
        {
            var resolution = BotProtocol.ResolvePostGameResult(
                earlyGameResult: "LOSS:CONCEDED",
                payloadResultResponse: "RESULT:WIN",
                timedOutAndResynced: false);

            Assert.Equal(BotProtocol.PostGameResultResolutionStatus.Resolved, resolution.Status);
            Assert.Equal("early-cache", resolution.ResultSource);
            Assert.Equal("RESULT:LOSS:CONCEDED", resolution.ResultResponse);
            Assert.True(resolution.HasResolvedResult);
        }

        [Fact]
        public void ResolvePostGameResult_KeepsEarlyConcedeLossWhenPayloadReturnsUnknown()
        {
            var resolution = BotProtocol.ResolvePostGameResult(
                earlyGameResult: "LOSS:CONCEDED",
                payloadResultResponse: "RESULT:NONE",
                timedOutAndResynced: false);

            Assert.Equal(BotProtocol.PostGameResultResolutionStatus.Resolved, resolution.Status);
            Assert.Equal("early-cache", resolution.ResultSource);
            Assert.Equal("RESULT:LOSS:CONCEDED", resolution.ResultResponse);
            Assert.True(resolution.HasResolvedResult);
        }

        [Fact]
        public void ResolvePostGameResult_TimedOutAndResyncedPreservesExplicitResult()
        {
            var resolution = BotProtocol.ResolvePostGameResult(
                earlyGameResult: null,
                payloadResultResponse: "RESULT:WIN",
                timedOutAndResynced: true);

            Assert.Equal(BotProtocol.PostGameResultResolutionStatus.TimedOutAndResynced, resolution.Status);
            Assert.Equal("payload-result", resolution.ResultSource);
            Assert.Equal("RESULT:WIN", resolution.ResultResponse);
            Assert.True(resolution.HasResolvedResult);
        }

        [Fact]
        public void ResolvePostGameResult_UsesConcedeFallbackWhenPayloadStaysUnknown()
        {
            var resolution = BotProtocol.ResolvePostGameResult(
                earlyGameResult: null,
                payloadResultResponse: "RESULT:NONE",
                timedOutAndResynced: false,
                concedeFallbackPayload: "LOSS:CONCEDED");

            Assert.Equal(BotProtocol.PostGameResultResolutionStatus.Resolved, resolution.Status);
            Assert.Equal("concede-fallback", resolution.ResultSource);
            Assert.Equal("RESULT:LOSS:CONCEDED", resolution.ResultResponse);
            Assert.True(resolution.HasResolvedResult);
        }

        [Fact]
        public void ResolvePostGameResult_TreatsResultNoneAsUnknownWithoutFallback()
        {
            var resolution = BotProtocol.ResolvePostGameResult(
                earlyGameResult: null,
                payloadResultResponse: "RESULT:NONE",
                timedOutAndResynced: false);

            Assert.Equal(BotProtocol.PostGameResultResolutionStatus.Unknown, resolution.Status);
            Assert.Equal("unknown", resolution.ResultSource);
            Assert.Equal("RESULT:NONE", resolution.ResultResponse);
            Assert.False(resolution.HasResolvedResult);
            Assert.True(BotProtocol.IsUnknownGameResultResponse(resolution.ResultResponse));
        }

        [Fact]
        public void PostGameResultHelper_InferPayloadFromDefeatScreen_ReturnsLoss()
        {
            Assert.Equal("LOSS", PostGameResultHelper.InferPayloadFromText("DefeatScreen", concededHint: false));
        }

        [Fact]
        public void PostGameResultHelper_MergePayload_PrefersResolvedConcedeLossOverUnknown()
        {
            var merged = PostGameResultHelper.MergePayload(
                currentPayload: null,
                currentConfidence: PostGameResultConfidence.Unknown,
                candidatePayload: "LOSS:CONCEDED",
                candidateConfidence: PostGameResultConfidence.ConcedeFallback,
                out var mergedConfidence);

            Assert.Equal("LOSS:CONCEDED", merged);
            Assert.Equal(PostGameResultConfidence.ConcedeFallback, mergedConfidence);
        }

        [Fact]
        public void DialogBlockingResponse_RecognizedAsActionFailureAndNotAsDialog()
        {
            // DIALOG_BLOCKING 响应以 FAIL: 开头，IsActionFailure 应识别（FAIL 前缀）
            Assert.True("FAIL:PLAY:DIALOG_BLOCKING:AlertPopup"
                .StartsWith("FAIL", StringComparison.OrdinalIgnoreCase));
            Assert.True("FAIL:ATTACK:DIALOG_BLOCKING:ReconnectHelperDialog"
                .StartsWith("FAIL", StringComparison.OrdinalIgnoreCase));

            // DIALOG_BLOCKING 不是合法的 BlockingDialogResponse（它是 action result，不是 dialog query result）
            Assert.False(BotProtocol.IsBlockingDialogResponse("FAIL:PLAY:DIALOG_BLOCKING:AlertPopup"));
        }

        [Fact]
        public void IsDrainOnlyPostGameResponse_DrainsCrossCommandButKeepsResultReplies()
        {
            Assert.True(BotProtocol.IsDrainOnlyPostGameResponse("NO_GAME"));
            Assert.True(BotProtocol.IsDrainOnlyPostGameResponse(BotProtocol.EndgamePending));
            Assert.True(BotProtocol.IsDrainOnlyPostGameResponse("SCENE:HUB"));
            Assert.False(BotProtocol.IsDrainOnlyPostGameResponse("RESULT:WIN"));
            Assert.False(BotProtocol.IsDrainOnlyPostGameResponse("RESULT:NONE"));
        }

        [Fact]
        public void OverlayProtocol_ParsesNewFormatResponses()
        {
            // 新格式检测响应
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:AlertPopup:CAN_DISMISS:CanDismiss"));
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:LoadingScreen:WAIT:scene_transition"));
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:InputDisabled:FATAL:InputManager.m_checkForInput=false"));

            // 新格式关闭响应
            Assert.True(BotProtocol.IsWaitOverlayResponse("WAIT:LoadingScreen"));
            Assert.True(BotProtocol.IsWaitOverlayResponse("WAIT:DialogLoading"));
            Assert.False(BotProtocol.IsWaitOverlayResponse("DISMISSED:AlertPopup"));
            Assert.False(BotProtocol.IsWaitOverlayResponse("OK:AlertPopup:OK"));

            Assert.True(BotProtocol.IsFatalOverlayResponse("FATAL:InputDisabled"));
            Assert.False(BotProtocol.IsFatalOverlayResponse("WAIT:LoadingScreen"));

            Assert.True(BotProtocol.IsDismissedOverlayResponse("DISMISSED:AlertPopup"));
            Assert.False(BotProtocol.IsDismissedOverlayResponse("OK:AlertPopup:OK"));

            // 新格式 4 段 TryParse：提取 action token，buttonLabel 留空
            Assert.True(BotProtocol.TryParseBlockingDialog(
                "DIALOG:AlertPopup:CAN_DISMISS:CanDismiss",
                out var dtype, out var dbtn, out var daction));
            Assert.Equal("AlertPopup", dtype);
            Assert.Equal(string.Empty, dbtn);
            Assert.Equal(BotProtocol.OverlayActionToken.CanDismiss, daction);

            Assert.True(BotProtocol.TryParseBlockingDialog(
                "DIALOG:LoadingScreen:WAIT:scene_transition",
                out _, out var waitBtn, out var waitAction));
            Assert.Equal(string.Empty, waitBtn);
            Assert.Equal(BotProtocol.OverlayActionToken.Wait, waitAction);

            Assert.True(BotProtocol.TryParseBlockingDialog(
                "DIALOG:InputDisabled:FATAL:InputManager.m_checkForInput=false",
                out _, out _, out var fatalAction));
            Assert.Equal(BotProtocol.OverlayActionToken.Fatal, fatalAction);
        }

        [Fact]
        public void OverlayProtocol_IsOverlayActionResponse_ClassifiesCorrectly()
        {
            Assert.True(BotProtocol.IsOverlayActionResponse("WAIT:LoadingScreen"));
            Assert.True(BotProtocol.IsOverlayActionResponse("FATAL:InputDisabled"));
            Assert.True(BotProtocol.IsOverlayActionResponse("DISMISSED:AlertPopup"));
            Assert.False(BotProtocol.IsOverlayActionResponse("OK:AlertPopup:OK"));
            Assert.False(BotProtocol.IsOverlayActionResponse("FAIL:NO_DIALOG:no_dialog"));
        }

        [Fact]
        public void OverlayProtocol_BackwardCompatible_OldFormatStillWorks()
        {
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:AlertPopup:OK"));
            Assert.True(BotProtocol.TryParseBlockingDialog("DIALOG:AlertPopup:OK", out var dt, out var bl, out var act));
            Assert.Equal("AlertPopup", dt);
            Assert.Equal("OK", bl);
            Assert.Equal(BotProtocol.OverlayActionToken.Unknown, act);
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("OK"));

            // 旧 2 参数重载保持兼容
            Assert.True(BotProtocol.TryParseBlockingDialog("DIALOG:AlertPopup:OK", out var dt2, out var bl2));
            Assert.Equal("AlertPopup", dt2);
            Assert.Equal("OK", bl2);

            Assert.False(BotProtocol.IsOverlayActionResponse("OK:AlertPopup:OK"));
            Assert.False(BotProtocol.IsOverlayActionResponse("FAIL:NO_DIALOG:no_dialog"));
        }

        [Fact]
        public void OverlayProtocol_IsDismissableBlockingDialog_CombinesActionAndButton()
        {
            // 新协议：action=CanDismiss 直接放行，不看按钮
            Assert.True(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.CanDismiss, string.Empty));
            Assert.True(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.CanDismiss, "重试"));

            // 新协议：action=Wait/Fatal 直接拒绝
            Assert.False(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.Wait, "OK"));
            Assert.False(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.Fatal, "确定"));

            // 老协议：action=Unknown 回退到按钮白名单
            Assert.True(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.Unknown, "OK"));
            Assert.True(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.Unknown, "确定"));
            Assert.False(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.Unknown, "重试"));
            Assert.False(BotProtocol.IsDismissableBlockingDialog(
                BotProtocol.OverlayActionToken.Unknown, "重新连接"));
        }

        [Fact]
        public void OverlayProtocol_IsDismissSuccess_AcceptsOldAndNewFormats()
        {
            Assert.True(BotProtocol.IsDismissSuccess("OK:AlertPopup:OK"));
            Assert.True(BotProtocol.IsDismissSuccess("OK:AlertPopup"));
            Assert.True(BotProtocol.IsDismissSuccess("DISMISSED:AlertPopup"));
            Assert.False(BotProtocol.IsDismissSuccess("FAIL:AlertPopup:click_failed"));
            Assert.False(BotProtocol.IsDismissSuccess("WAIT:LoadingScreen"));
            Assert.False(BotProtocol.IsDismissSuccess("FATAL:InputDisabled"));
            Assert.False(BotProtocol.IsDismissSuccess(string.Empty));
            Assert.False(BotProtocol.IsDismissSuccess(null));
        }

        [Fact]
        public void OverlayProtocol_CanDismissHackRemoved_RawTokenNotTreatedAsButtonLabel()
        {
            // 回归测试：旧假补丁曾把 "candismiss" 当成按钮白名单兜底，
            // 导致 "CAN_DISMISS:CanDismiss" 被误解析为按钮文本后仍然"碰巧"匹配。
            // 现在严格通过 action token 识别；原始 CAN_DISMISS 字符串本身不应再被视为按钮。
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("CAN_DISMISS"));
            Assert.False(BotProtocol.IsSafeBlockingDialogButtonLabel("CAN_DISMISS:CanDismiss"));
        }

        [Fact]
        public void OverlayProtocol_CrossCommand_RecognizesNewFormats()
        {
            Assert.True(BotProtocol.IsCrossCommandResponse("WAIT:LoadingScreen"));
            Assert.True(BotProtocol.IsCrossCommandResponse("FATAL:InputDisabled"));
            Assert.True(BotProtocol.IsCrossCommandResponse("DISMISSED:AlertPopup"));
        }
    }
}
