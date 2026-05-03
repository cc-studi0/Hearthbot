using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Assets;
using Blizzard.GameService.SDK.Client.Integration;
using Blizzard.T5.Core;
using Blizzard.T5.Core.Time;
using Blizzard.T5.MaterialService.Extensions;
using Cysharp.Threading.Tasks;
using Hearthstone;
using Hearthstone.Login;
using PegasusGame;
using Unity.Profiling;
using UnityEngine;

public class Gameplay : PegasusScene
{
	private static Gameplay s_instance;

	private bool m_unloading;

	private BnetErrorInfo m_lastFatalBnetErrorInfo;

	private bool m_handleLastFatalBnetErrorNow;

	private float m_boardProgress;

	private List<NameBanner> m_nameBanners = new List<NameBanner>();

	private NameBanner m_nameBannerGamePlayPhone;

	private int m_numBannersRequested;

	private Actor m_cardDrawStandIn;

	private BoardLayout m_boardLayout;

	private int m_baconFavoriteBoardSkin;

	private int m_baconTeammateFavoriteBoardSkin = 1;

	private bool m_loadingBaconBoard;

	private bool m_criticalAssetsLoaded;

	private Queue<List<Network.PowerHistory>> m_queuedPowerHistory = new Queue<List<Network.PowerHistory>>();

	private float? m_originalTimeScale;

	private Camera m_inputCamera;

	private PrefabInstanceLoadTracker.Context m_prefabContext = new PrefabInstanceLoadTracker.Context();

	private CancellationTokenSource m_taskTokenSource;

	private CancellationTokenSource m_pausePowerTokenSource;

	private CancellationTokenSource m_waitForOpponentTokenSource;

	private CancellationTokenSource m_stateTokenSource;

	private CancellationTokenSource m_lettuceAbilityTokenSource;

	private CheatMgr m_cheatManager;

	private PrefabInstanceLoadTracker m_prefabInstanceLoaderTracker;

	private static ProfilerMarker s_gameplayProcessNetworkMarker = new ProfilerMarker("Gameplay.Update.ProcessNetwork");

	private static ProfilerMarker s_gameplayGameStateUpdateMarker = new ProfilerMarker("Gameplay.Update.GamestateUpdate");

	private static Blizzard.T5.Core.ILogger GameNetLogger => Network.Get().GameNetLogger;

	public CancellationToken TaskToken
	{
		get
		{
			if (m_taskTokenSource == null)
			{
				m_taskTokenSource = new CancellationTokenSource();
			}
			return m_taskTokenSource.Token;
		}
	}

	public CancellationToken PausePowerToken
	{
		get
		{
			if (m_pausePowerTokenSource == null)
			{
				m_pausePowerTokenSource = new CancellationTokenSource();
			}
			return m_pausePowerTokenSource.Token;
		}
	}

	public CancellationToken LettuceAbilityToken
	{
		get
		{
			if (m_lettuceAbilityTokenSource == null)
			{
				m_lettuceAbilityTokenSource = new CancellationTokenSource();
			}
			return m_lettuceAbilityTokenSource.Token;
		}
	}

	public CancellationToken WaitForOpponentToken
	{
		get
		{
			if (m_waitForOpponentTokenSource == null)
			{
				m_waitForOpponentTokenSource = new CancellationTokenSource();
			}
			return m_waitForOpponentTokenSource.Token;
		}
	}

	private CancellationToken GameStateToken
	{
		get
		{
			if (m_stateTokenSource == null)
			{
				m_stateTokenSource = new CancellationTokenSource();
			}
			return m_stateTokenSource.Token;
		}
	}

	protected override void Awake()
	{
		Log.LoadingScreen.Print("Gameplay.Awake()");
		Debug.LogFormat("Gameplay.Awake() - CurrentMode={0}, PrevMode={1}", SceneMgr.Get().GetMode(), SceneMgr.Get().GetPrevMode());
		base.Awake();
		s_instance = this;
		GameState gameState = GameState.Initialize();
		if (ShouldHandleDisconnect())
		{
			GameNetLogger.Log(Blizzard.T5.Core.LogLevel.Debug, "Gameplay.Awake() - DISCONNECTED");
			Log.LoadingScreen.PrintWarning("Gameplay.Awake() - DISCONNECTED");
			HandleDisconnect();
			return;
		}
		m_cheatManager = CheatMgr.Get();
		m_cheatManager.RegisterCategory("gameplay:more");
		m_cheatManager.RegisterCheatHandler("saveme", OnProcessCheat_saveme);
		if (!HearthstoneApplication.IsPublic())
		{
			m_cheatManager.RegisterCheatHandler("entitycount", GameDebugDisplay.Get().ToggleEntityCount);
			m_cheatManager.RegisterCheatHandler("showtag", GameDebugDisplay.Get().AddTagToDisplay);
			m_cheatManager.RegisterCheatHandler("hidetag", GameDebugDisplay.Get().RemoveTagToDisplay);
			m_cheatManager.RegisterCheatHandler("hidetags", GameDebugDisplay.Get().RemoveAllTags);
			m_cheatManager.RegisterCheatHandler("hidezerotags", GameDebugDisplay.Get().ToggleHideZeroTags);
			m_cheatManager.RegisterCheatHandler("aidebug", AIDebugDisplay.Get().ToggleDebugDisplay);
			m_cheatManager.RegisterCheatHandler("ropedebug", RopeTimerDebugDisplay.Get().EnableDebugDisplay);
			m_cheatManager.RegisterCheatAlias("ropedebug", "ropetimerdebug", "timerdebug", "debugrope", "debugropetimer");
			m_cheatManager.RegisterCheatHandler("disableropedebug", RopeTimerDebugDisplay.Get().DisableDebugDisplay);
			m_cheatManager.RegisterCheatAlias("disableropedebug", "disableropetimerdebug", "disabletimerdebug", "disabledebugrope", "disabledebugropetimer");
			m_cheatManager.RegisterCheatHandler("showbugs", JiraBugDebugDisplay.Get().EnableDebugDisplay);
			m_cheatManager.RegisterCheatHandler("hidebugs", JiraBugDebugDisplay.Get().DisableDebugDisplay);
			m_cheatManager.RegisterCheatHandler("concede", OnProcessCheat_concede, "This is what happens when you Prep > Coin.");
			m_cheatManager.RegisterCheatHandler("showstatus", GameDebugDisplay.Get().ShowUIStatusDisplay);
			m_cheatManager.RegisterCheatHandler("hidestatus", GameDebugDisplay.Get().HideUIStatusDisplay);
			ZombeastDebugManager.Get();
			DrustvarHorrorDebugManager.Get();
			SmartDiscoverDebugManager.Get();
		}
		m_cheatManager.DefaultCategory();
		gameState.RegisterCreateGameListener(OnCreateGame);
		m_prefabInstanceLoaderTracker = PrefabInstanceLoadTracker.Get();
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "InputManager.prefab:909a8d3bcaaf7ea48a770ff400f4db32", OnInputManagerLoaded);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "MulliganManager.prefab:511d1cd9bce694c0a93778f083b47044", OnMulliganManagerLoaded);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "ThinkEmoteController.prefab:2163c9dc60486d74f8249ccf878b1742");
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "Card_Hidden.prefab:1a94649d257bc284ca6e2962f634a8b9", OnCardDrawStandinLoaded, null, AssetLoadingOptions.IgnorePrefabPosition);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "TurnStartManager.prefab:077d03854627944a695a7e86d67153ca", OnTurnStartManagerLoaded);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "TargetReticleManager.prefab:fcbd8bbbf8c5f4c0589fa9c1927bd018", OnTargetReticleManagerLoaded);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "RemoteActionHandler.prefab:69f5fe6e6c4af9e4aa51f7ffc10fb9b3", OnRemoteActionHandlerLoaded);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "ChoiceCardMgr.prefab:c78e5c81bb7cbaa4ca3f09e6dd732675", OnChoiceCardMgrLoaded);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "Actor_Tag_Visual_Table.prefab:7cbaaffc9f20b1e49a08e703944b1e04", OnTagVisualConfigurationLoaded);
		LoadingScreen.Get().RegisterFinishedTransitionListener(OnTransitionFinished);
		m_boardProgress = -1f;
		ProcessGameSetupPacket();
	}

	private void OnDestroy()
	{
		Log.LoadingScreen?.Print("Gameplay.OnDestroy()");
		m_prefabInstanceLoaderTracker?.DestroyContext(m_prefabContext);
		if (m_inputCamera != null)
		{
			if (PegUI.Get() != null)
			{
				PegUI.Get().RemoveInputCamera(m_inputCamera);
			}
			m_inputCamera = null;
		}
		RestoreOriginalTimeScale();
		TimeScaleMgr.Get().PopTemporarySpeedIncrease();
		if (m_cheatManager != null)
		{
			m_cheatManager.UnregisterCheatHandler("saveme", OnProcessCheat_saveme);
			if (HearthstoneApplication.Get() != null && !HearthstoneApplication.IsPublic())
			{
				GameDebugDisplay gameDebugDisplay = GameDebugDisplay.Get();
				AIDebugDisplay aIDebugDisplay = AIDebugDisplay.Get();
				RopeTimerDebugDisplay ropeTimerDebugDisplay = RopeTimerDebugDisplay.Get();
				JiraBugDebugDisplay jiraBugDebugDisplay = JiraBugDebugDisplay.Get();
				m_cheatManager.UnregisterCheatHandler("entitycount", gameDebugDisplay.ToggleEntityCount);
				m_cheatManager.UnregisterCheatHandler("showtag", gameDebugDisplay.AddTagToDisplay);
				m_cheatManager.UnregisterCheatHandler("hidetag", gameDebugDisplay.RemoveTagToDisplay);
				m_cheatManager.UnregisterCheatHandler("hidetags", gameDebugDisplay.RemoveAllTags);
				m_cheatManager.UnregisterCheatHandler("hidezerotags", gameDebugDisplay.ToggleHideZeroTags);
				m_cheatManager.UnregisterCheatHandler("aidebug", aIDebugDisplay.ToggleDebugDisplay);
				m_cheatManager.UnregisterCheatHandler("ropedebug", ropeTimerDebugDisplay.EnableDebugDisplay);
				m_cheatManager.UnregisterCheatHandler("disableropedebug", ropeTimerDebugDisplay.DisableDebugDisplay);
				m_cheatManager.UnregisterCheatHandler("showbugs", jiraBugDebugDisplay.EnableDebugDisplay);
				m_cheatManager.UnregisterCheatHandler("hidebugs", jiraBugDebugDisplay.DisableDebugDisplay);
				m_cheatManager.UnregisterCheatHandler("concede", OnProcessCheat_concede);
			}
		}
		ReleaseCancellationTokenSources();
		StopAllCoroutines();
		s_instance = null;
	}

	private void Start()
	{
		Log.LoadingScreen.Print("Gameplay.Start()");
		CheckBattleNetConnection();
		Network network = Network.Get();
		network.AddBnetErrorListener(OnBnetError);
		network.RegisterNetHandler(PowerHistory.PacketID.ID, OnPowerHistory);
		network.RegisterNetHandler(AllOptions.PacketID.ID, OnAllOptions);
		network.RegisterNetHandler(EntityChoices.PacketID.ID, OnEntityChoices);
		network.RegisterNetHandler(EntitiesChosen.PacketID.ID, OnEntitiesChosen);
		network.RegisterNetHandler(UserUI.PacketID.ID, OnUserUI);
		network.RegisterNetHandler(NAckOption.PacketID.ID, OnOptionRejected);
		network.RegisterNetHandler(PegasusGame.TurnTimer.PacketID.ID, OnTurnTimerUpdate);
		network.RegisterNetHandler(SpectatorNotify.PacketID.ID, OnSpectatorNotify);
		network.RegisterNetHandler(AIDebugInformation.PacketID.ID, OnAIDebugInformation);
		network.RegisterNetHandler(RopeTimerDebugInformation.PacketID.ID, OnRopeTimerDebugInformation);
		network.RegisterNetHandler(DebugMessage.PacketID.ID, OnDebugMessage);
		network.RegisterNetHandler(ScriptDebugInformation.PacketID.ID, OnScriptDebugInformation);
		network.RegisterNetHandler(GameRoundHistory.PacketID.ID, OnGameRoundHistory);
		network.RegisterNetHandler(GameRealTimeBattlefieldRaces.PacketID.ID, OnGameRealTimeBattlefieldRaces);
		network.RegisterNetHandler(PlayerRealTimeBattlefieldRaces.PacketID.ID, OnPlayerRealTimeBattlefieldRaces);
		network.RegisterNetHandler(BattlegroundsMinionPool.PacketID.ID, OnBattlegroundsMinionPool);
		network.RegisterNetHandler(GameGuardianVars.PacketID.ID, OnGameGuardianVars);
		network.RegisterNetHandler(ScriptLogMessage.PacketID.ID, OnScriptLogMessage);
		network.RegisterNetHandler(UpdateBattlegroundInfo.PacketID.ID, OnBattlegroundInfo);
		network.RegisterNetHandler(GetBattlegroundHeroArmorTierList.PacketID.ID, OnBattlegroundArmorTierList);
		network.RegisterNetHandler(GetBattlegroundsPlayerAnomaly.PacketID.ID, OnBattlegroundsPlayerAnomaly);
		network.RegisterNetHandler(TeammatesEntities.PacketID.ID, OnTeammateEntities);
		network.RegisterNetHandler(TeammatesChooseEntities.PacketID.ID, OnTeammatesChooseEntities);
		network.RegisterNetHandler(TeammatesEntitiesChosen.PacketID.ID, OnTeammatesEntitiesChosen);
		network.RegisterNetHandler(TeammateConcede.PacketID.ID, OnTeammateConcede);
		network.RegisterNetHandler(EntityPinged.PacketID.ID, OnEntityPinged);
		network.RegisterNetHandler(FakeConcede.PacketID.ID, OnFakeConcede);
		network.RegisterNetHandler(MulliganChooseOneTentativeSelection.PacketID.ID, OnMulliganChooseOneTentativeSelection);
		network.RegisterNetHandler(ReplaceBattlegroundMulliganHero.PacketID.ID, OnReplaceBattlegroundMulliganHero);
		network.RegisterNetHandler(ShopChoiceEnded.PacketID.ID, OnShopChoiceEnded);
		network.RegisterNetHandler(TeammateShopChoiceEnded.PacketID.ID, OnTeammateShopChoiceEnded);
		if (HearthstoneApplication.IsPublic() || !Cheats.Get().ShouldSkipSendingGetGameState())
		{
			network.GetGameState();
		}
	}

	private void CheckBattleNetConnection()
	{
		if (!Network.IsLoggedIn() && Network.ShouldBeConnectedToAurora())
		{
			OnBnetError(new BnetErrorInfo(BnetFeature.Bnet, BnetFeatureEvent.Bnet_OnDisconnected, BattleNetErrors.ERROR_RPC_DISCONNECT), null);
		}
	}

	private void Update()
	{
		CheckCriticalAssetLoads();
		Network.Get().ProcessNetwork();
		if (IsDoneUpdatingGame())
		{
			EndGameScreen endGameScreen = EndGameScreen.Get();
			if (!(endGameScreen != null) || (!endGameScreen.IsPlayingBlockingAnim() && !endGameScreen.IsScoreScreenShown()))
			{
				HandleLastFatalBnetError();
				PlayerMigrationManager playerMigrationManager = PlayerMigrationManager.Get();
				if (playerMigrationManager != null && playerMigrationManager.RestartRequired && !playerMigrationManager.IsShowingPlayerMigrationRelogPopup)
				{
					playerMigrationManager.ShowRestartAlert();
				}
			}
		}
		else if (!GameMgr.Get().IsFindingGame() && !m_unloading && !SceneMgr.Get().WillTransition() && AreCriticalAssetsLoaded() && GameState.Get() != null)
		{
			GameState.Get().Update();
		}
	}

	private void OnGUI()
	{
		LayoutProgressGUI();
	}

	private void LayoutProgressGUI()
	{
		if (!(m_boardProgress < 0f))
		{
			Vector2 vector = new Vector2(150f, 30f);
			Vector2 vector2 = new Vector2((float)Screen.width * 0.5f - vector.x * 0.5f, (float)Screen.height * 0.5f - vector.y * 0.5f);
			GUI.Box(new Rect(vector2.x, vector2.y, vector.x, vector.y), "");
			GUI.Box(new Rect(vector2.x, vector2.y, m_boardProgress * vector.x, vector.y), "");
			GUI.TextField(new Rect(vector2.x, vector2.y, vector.x, vector.y), $"{m_boardProgress * 100f:0}%");
		}
	}

	public static Gameplay Get()
	{
		return s_instance;
	}

	public void StopIncreaseWaitForOpponentReconnectPeriod()
	{
		m_waitForOpponentTokenSource?.Cancel();
		m_waitForOpponentTokenSource?.Dispose();
		m_waitForOpponentTokenSource = null;
	}

	public override void PreUnload()
	{
		m_unloading = true;
		if (Board.Get() != null && BoardCameras.Get() != null)
		{
			LoadingScreen.Get().SetFreezeFrameCamera(Camera.main);
			LoadingScreen.Get().SetTransitionAudioListener(BoardCameras.Get().GetAudioListener());
		}
	}

	public override bool IsUnloading()
	{
		return m_unloading;
	}

	public override void Unload()
	{
		Log.LoadingScreen.Print("Gameplay.Unload()");
		bool num = IsLeavingGameUnfinished();
		GameState.Shutdown();
		Network network = Network.Get();
		if (network != null)
		{
			network.RemoveBnetErrorListener(OnBnetError);
			network.RemoveNetHandler(PowerHistory.PacketID.ID, OnPowerHistory);
			network.RemoveNetHandler(AllOptions.PacketID.ID, OnAllOptions);
			network.RemoveNetHandler(EntityChoices.PacketID.ID, OnEntityChoices);
			network.RemoveNetHandler(EntitiesChosen.PacketID.ID, OnEntitiesChosen);
			network.RemoveNetHandler(UserUI.PacketID.ID, OnUserUI);
			network.RemoveNetHandler(NAckOption.PacketID.ID, OnOptionRejected);
			network.RemoveNetHandler(PegasusGame.TurnTimer.PacketID.ID, OnTurnTimerUpdate);
			network.RemoveNetHandler(SpectatorNotify.PacketID.ID, OnSpectatorNotify);
			network.RemoveNetHandler(AIDebugInformation.PacketID.ID, OnAIDebugInformation);
			network.RemoveNetHandler(RopeTimerDebugInformation.PacketID.ID, OnRopeTimerDebugInformation);
			network.RemoveNetHandler(DebugMessage.PacketID.ID, OnDebugMessage);
			network.RemoveNetHandler(ScriptDebugInformation.PacketID.ID, OnScriptDebugInformation);
			network.RemoveNetHandler(GameRoundHistory.PacketID.ID, OnGameRoundHistory);
			network.RemoveNetHandler(GameRealTimeBattlefieldRaces.PacketID.ID, OnGameRealTimeBattlefieldRaces);
			network.RemoveNetHandler(PlayerRealTimeBattlefieldRaces.PacketID.ID, OnPlayerRealTimeBattlefieldRaces);
			network.RemoveNetHandler(BattlegroundsMinionPool.PacketID.ID, OnBattlegroundsMinionPool);
			network.RemoveNetHandler(GameGuardianVars.PacketID.ID, OnGameGuardianVars);
			network.RemoveNetHandler(ScriptLogMessage.PacketID.ID, OnScriptLogMessage);
			network.RemoveNetHandler(UpdateBattlegroundInfo.PacketID.ID, OnBattlegroundInfo);
			network.RemoveNetHandler(GetBattlegroundHeroArmorTierList.PacketID.ID, OnBattlegroundArmorTierList);
			network.RemoveNetHandler(GetBattlegroundsPlayerAnomaly.PacketID.ID, OnBattlegroundsPlayerAnomaly);
			network.RemoveNetHandler(TeammatesEntities.PacketID.ID, OnTeammateEntities);
			network.RemoveNetHandler(TeammatesChooseEntities.PacketID.ID, OnTeammatesChooseEntities);
			network.RemoveNetHandler(TeammatesEntitiesChosen.PacketID.ID, OnTeammatesEntitiesChosen);
			network.RemoveNetHandler(TeammateConcede.PacketID.ID, OnTeammateConcede);
			network.RemoveNetHandler(EntityPinged.PacketID.ID, OnEntityPinged);
			network.RemoveNetHandler(FakeConcede.PacketID.ID, OnFakeConcede);
			network.RemoveNetHandler(ReplaceBattlegroundMulliganHero.PacketID.ID, OnReplaceBattlegroundMulliganHero);
			network.RemoveNetHandler(ShopChoiceEnded.PacketID.ID, OnShopChoiceEnded);
			network.RemoveNetHandler(TeammateShopChoiceEnded.PacketID.ID, OnTeammateShopChoiceEnded);
		}
		m_cheatManager?.UnregisterCheatHandler("saveme", OnProcessCheat_saveme);
		if (num)
		{
			if (GameMgr.Get() != null && GameMgr.Get().IsPendingAutoConcede())
			{
				Network.Get()?.AutoConcede();
				GameMgr.Get().SetPendingAutoConcede(pendingAutoConcede: false);
			}
			Network.Get()?.DisconnectFromGameServer(Network.DisconnectReason.Unload);
		}
		foreach (NameBanner nameBanner in m_nameBanners)
		{
			nameBanner.Unload();
		}
		if (m_nameBannerGamePlayPhone != null)
		{
			m_nameBannerGamePlayPhone.Unload();
		}
		if (Board.Get() != null && Board.Get().AreAllAssetsLoaded())
		{
			m_unloading = false;
		}
		else
		{
			Board.Get()?.RegisterAllAssetsLoadedCallback(OnBoardAssetsFinishedLoadingDuringGameplayUnload);
		}
	}

	private void OnBoardAssetsFinishedLoadingDuringGameplayUnload()
	{
		m_unloading = false;
	}

	private void ReleaseCancellationTokenSources()
	{
		m_taskTokenSource?.Cancel();
		m_taskTokenSource?.Dispose();
		m_taskTokenSource = null;
		m_pausePowerTokenSource?.Cancel();
		m_pausePowerTokenSource?.Dispose();
		m_pausePowerTokenSource = null;
		m_waitForOpponentTokenSource?.Cancel();
		m_waitForOpponentTokenSource?.Dispose();
		m_waitForOpponentTokenSource = null;
		m_stateTokenSource?.Cancel();
		m_stateTokenSource?.Dispose();
		m_stateTokenSource = null;
		m_lettuceAbilityTokenSource?.Cancel();
		m_lettuceAbilityTokenSource?.Dispose();
		m_lettuceAbilityTokenSource = null;
	}

	public void RemoveClassNames()
	{
		foreach (NameBanner nameBanner in m_nameBanners)
		{
			nameBanner.FadeOutSubtext();
			nameBanner.PositionNameText(shouldTween: true);
		}
	}

	public void RemoveNameBanners()
	{
		foreach (NameBanner nameBanner in m_nameBanners)
		{
			UnityEngine.Object.Destroy(nameBanner.gameObject);
		}
		m_nameBanners.Clear();
	}

	public void AddGamePlayNameBannerPhone()
	{
		if (m_nameBannerGamePlayPhone == null)
		{
			m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "NameBannerGamePlay_phone.prefab:947928a8ac849b2408a621c97d3b9fa6", OnPlayerBannerLoaded, Player.Side.OPPOSING);
			m_numBannersRequested++;
		}
	}

	public void RemoveGamePlayNameBannerPhone()
	{
		if (m_nameBannerGamePlayPhone != null)
		{
			m_nameBannerGamePlayPhone.Unload();
		}
	}

	public void UpdateFriendlySideMedalChange(MedalInfoTranslator medalInfo)
	{
		foreach (NameBanner nameBanner in m_nameBanners)
		{
			if (nameBanner.GetPlayerSide() == Player.Side.FRIENDLY)
			{
				nameBanner.UpdateMedalChange(medalInfo);
			}
		}
	}

	public void UpdateEnemySideNameBannerName(string newName)
	{
		foreach (NameBanner nameBanner in m_nameBanners)
		{
			if (nameBanner.GetPlayerSide() == Player.Side.OPPOSING)
			{
				nameBanner.SetName(newName);
			}
		}
	}

	public Actor GetCardDrawStandIn()
	{
		return m_cardDrawStandIn;
	}

	public NameBanner GetNameBannerForSide(Player.Side wantedSide)
	{
		if (m_nameBannerGamePlayPhone != null && m_nameBannerGamePlayPhone.GetPlayerSide() == wantedSide)
		{
			return m_nameBannerGamePlayPhone;
		}
		return m_nameBanners.Find((NameBanner x) => x.GetPlayerSide() == wantedSide);
	}

	public void SetGameStateBusy(bool busy, float delay)
	{
		if (delay <= Mathf.Epsilon)
		{
			GameState.Get().SetBusy(busy);
		}
		else
		{
			SetGameStateBusyWithDelay(busy, delay, GameStateToken).Forget();
		}
	}

	public void SwapCardBacks()
	{
		int cardBackId = GameState.Get().GetOpposingSidePlayer().GetCardBackId();
		int cardBackId2 = GameState.Get().GetFriendlySidePlayer().GetCardBackId();
		GameState.Get().GetOpposingSidePlayer().SetCardBackId(cardBackId2);
		GameState.Get().GetFriendlySidePlayer().SetCardBackId(cardBackId);
		CardBackManager.Get().SetGameCardBackIDs(cardBackId, cardBackId2);
	}

	public bool HasBattleNetFatalError()
	{
		return m_lastFatalBnetErrorInfo != null;
	}

	public BoardLayout GetBoardLayout()
	{
		return m_boardLayout;
	}

	private void ProcessGameSetupPacket()
	{
		Network.GameSetup gameSetup = GameMgr.Get().GetGameSetup();
		if (gameSetup == null)
		{
			Debug.LogError("Game Setup packet was null. Previous Scene=" + SceneMgr.Get().GetPrevMode());
			return;
		}
		LoadBoard(gameSetup);
		GameState.Get().OnGameSetup(gameSetup);
	}

	private bool IsHandlingNetworkProblem()
	{
		if (ShouldHandleDisconnect())
		{
			return true;
		}
		if (m_handleLastFatalBnetErrorNow)
		{
			return true;
		}
		return false;
	}

	private bool ShouldHandleDisconnect(bool onDisconnect = false)
	{
		if (Network.Get().IsConnectedToGameServer() && !onDisconnect)
		{
			return false;
		}
		if (Network.Get().WasGameConceded())
		{
			return false;
		}
		if (Network.Get().WasDisconnectRequested() && GameMgr.Get() != null && GameMgr.Get().IsSpectator() && !GameState.Get().IsGameOverNowOrPending())
		{
			return true;
		}
		if (GameState.Get() != null && GameState.Get().IsGameOverNowOrPending())
		{
			return false;
		}
		return true;
	}

	public void OnDisconnect(BattleNetErrors error)
	{
		GameNetLogger.Log(Blizzard.T5.Core.LogLevel.Debug, "Gameplay.OnDisconnect() - error:" + error);
		if (ShouldHandleDisconnect(onDisconnect: true))
		{
			PerformanceAnalytics.Get()?.DisconnectEvent(SceneMgr.Get().GetMode().ToString());
			GameServerInfo lastGameServerJoined = Network.Get().GetLastGameServerJoined();
			if (lastGameServerJoined != null)
			{
				TracertReporter.ReportTracertInfo(lastGameServerJoined.Address);
			}
			HandleDisconnect();
		}
	}

	private void HandleDisconnect()
	{
		GameNetLogger.Log(Blizzard.T5.Core.LogLevel.Debug, "Gameplay.HandleDisconnect() ");
		Log.GameMgr.PrintWarning("Gameplay is handling a game disconnect.");
		if (!ReconnectMgr.Get().ReconnectToGameFromGameplay() && !SpectatorManager.Get().HandleDisconnectFromGameplay())
		{
			DisconnectMgr.Get().DisconnectFromGameplay();
		}
	}

	private bool IsDoneUpdatingGame()
	{
		if (m_handleLastFatalBnetErrorNow)
		{
			return true;
		}
		if (Network.Get().IsConnectedToGameServer())
		{
			return false;
		}
		if (GameState.Get() != null)
		{
			if (GameState.Get().HasPowersToProcess())
			{
				return false;
			}
			if (!GameState.Get().IsGameOver())
			{
				return false;
			}
		}
		return true;
	}

	private bool OnBnetError(BnetErrorInfo info, object userData)
	{
		if (Network.Get().OnIgnorableBnetError(info))
		{
			return true;
		}
		if (m_handleLastFatalBnetErrorNow)
		{
			return true;
		}
		m_lastFatalBnetErrorInfo = info;
		BattleNetErrors error = info.GetError();
		if (error == BattleNetErrors.ERROR_PARENTAL_CONTROL_RESTRICTION || error == BattleNetErrors.ERROR_SESSION_DUPLICATE)
		{
			m_handleLastFatalBnetErrorNow = true;
		}
		return true;
	}

	private void OnBnetErrorResponse(AlertPopup.Response response, object userData)
	{
		if ((bool)HearthstoneApplication.AllowResetFromFatalError)
		{
			HearthstoneApplication.Get().Reset();
		}
		else
		{
			HearthstoneApplication.Get().Exit();
		}
	}

	private void HandleLastFatalBnetError()
	{
		if (m_lastFatalBnetErrorInfo == null)
		{
			return;
		}
		if (m_handleLastFatalBnetErrorNow)
		{
			Network.Get().OnFatalBnetError(m_lastFatalBnetErrorInfo);
			m_handleLastFatalBnetErrorNow = false;
		}
		else
		{
			string key = (HearthstoneApplication.AllowResetFromFatalError ? "GAMEPLAY_DISCONNECT_BODY_RESET" : "GAMEPLAY_DISCONNECT_BODY");
			if (GameMgr.Get().IsSpectator())
			{
				key = (HearthstoneApplication.AllowResetFromFatalError ? "GAMEPLAY_SPECTATOR_DISCONNECT_BODY_RESET" : "GAMEPLAY_SPECTATOR_DISCONNECT_BODY");
			}
			AlertPopup.PopupInfo popupInfo = new AlertPopup.PopupInfo();
			popupInfo.m_headerText = GameStrings.Get("GAMEPLAY_DISCONNECT_HEADER");
			popupInfo.m_text = GameStrings.Get(key);
			popupInfo.m_showAlertIcon = true;
			popupInfo.m_responseDisplay = AlertPopup.ResponseDisplay.OK;
			popupInfo.m_responseCallback = OnBnetErrorResponse;
			DialogManager.Get().ShowPopup(popupInfo);
		}
		m_lastFatalBnetErrorInfo = null;
	}

	private void OnPowerHistory()
	{
		List<Network.PowerHistory> powerHistory = Network.Get().GetPowerHistory();
		Log.LoadingScreen.Print("Gameplay.OnPowerHistory() - powerList={0}", powerHistory.Count);
		if (AreCriticalAssetsLoaded())
		{
			GameState.Get().OnPowerHistory(powerHistory);
		}
		else
		{
			m_queuedPowerHistory.Enqueue(powerHistory);
		}
	}

	private void OnAllOptions()
	{
		Network.Options options = Network.Get().GetOptions();
		Log.LoadingScreen.Print("Gameplay.OnAllOptions() - id={0}", options.ID);
		GameState.Get().OnAllOptions(options);
	}

	private void OnEntityChoices()
	{
		Network.EntityChoices entityChoices = Network.Get().GetEntityChoices();
		Log.LoadingScreen.Print("Gameplay.OnEntityChoices() - id={0}", entityChoices.ID);
		GameState.Get().OnEntityChoices(entityChoices);
	}

	private void OnEntitiesChosen()
	{
		Network.EntitiesChosen entitiesChosen = Network.Get().GetEntitiesChosen();
		GameState.Get().OnEntitiesChosen(entitiesChosen);
	}

	private void OnShopChoiceEnded()
	{
		if (ChoiceCardMgr.Get() != null)
		{
			ChoiceCardMgr.Get().OnShopChoiceEnded();
		}
	}

	private void OnMulliganChooseOneTentativeSelection()
	{
		Network.MulliganChooseOneTentativeSelection mulliganChooseOneTentativeSelection = Network.Get().GetMulliganChooseOneTentativeSelection();
		if (MulliganManager.Get() != null && MulliganManager.Get().IsMulliganActive())
		{
			MulliganManager.Get().OnMulliganChooseOneTentativeSelection(mulliganChooseOneTentativeSelection);
		}
		else if (ChoiceCardMgr.Get() != null && ChoiceCardMgr.Get().HasChoices())
		{
			ChoiceCardMgr.Get().OnChooseOneTentativeSelection(mulliganChooseOneTentativeSelection);
		}
		else
		{
			Debug.LogWarning("[Non-Mulligan or BG Trinket selection]On Choose One Tentative Selection");
		}
	}

	private void OnUserUI()
	{
		if ((bool)RemoteActionHandler.Get())
		{
			RemoteActionHandler.Get().HandleAction(Network.Get().GetUserUI());
		}
	}

	private void OnOptionRejected()
	{
		int nAckOption = Network.Get().GetNAckOption();
		GameState.Get().OnOptionRejected(nAckOption);
	}

	private void OnTurnTimerUpdate()
	{
		Network.TurnTimerInfo turnTimerInfo = Network.Get().GetTurnTimerInfo();
		GameState.Get().OnTurnTimerUpdate(turnTimerInfo);
	}

	private void OnSpectatorNotify()
	{
		SpectatorNotify spectatorNotify = Network.Get().GetSpectatorNotify();
		GameState.Get().OnSpectatorNotifyEvent(spectatorNotify);
	}

	private void OnAIDebugInformation()
	{
		AIDebugInformation aIDebugInformation = Network.Get().GetAIDebugInformation();
		AIDebugDisplay.Get().OnAIDebugInformation(aIDebugInformation);
	}

	private void OnRopeTimerDebugInformation()
	{
		RopeTimerDebugInformation ropeTimerDebugInformation = Network.Get().GetRopeTimerDebugInformation();
		RopeTimerDebugDisplay.Get().OnRopeTimerDebugInformation(ropeTimerDebugInformation);
	}

	private void OnDebugMessage()
	{
		DebugMessage debugMessage = Network.Get().GetDebugMessage();
		DebugMessageManager.Get().OnDebugMessage(debugMessage);
	}

	private void OnScriptDebugInformation()
	{
		ScriptDebugInformation scriptDebugInformation = Network.Get().GetScriptDebugInformation();
		ScriptDebugDisplay.Get().OnScriptDebugInfo(scriptDebugInformation);
	}

	private void OnGameRoundHistory()
	{
		GameRoundHistory gameRoundHistory = Network.Get().GetGameRoundHistory();
		if (PlayerLeaderboardManager.Get() != null)
		{
			PlayerLeaderboardManager.Get().UpdateRoundHistory(gameRoundHistory);
		}
	}

	private void OnGameRealTimeBattlefieldRaces()
	{
		GameRealTimeBattlefieldRaces gameRealTimeBattlefieldRaces = Network.Get().GetGameRealTimeBattlefieldRaces();
		GameState gameState = GameState.Get();
		if (gameState == null)
		{
			return;
		}
		List<TAG_RACE> availableRacesInBattlegroundsExcludingAmalgam = gameState.GetAvailableRacesInBattlegroundsExcludingAmalgam();
		List<TAG_RACE> missingRacesInBattlegrounds = gameState.GetMissingRacesInBattlegrounds();
		List<TAG_RACE> inactiveRacesInBattlegrounds = gameState.GetInactiveRacesInBattlegrounds();
		if (availableRacesInBattlegroundsExcludingAmalgam.Count > 0 || missingRacesInBattlegrounds.Count > 0)
		{
			return;
		}
		foreach (GameRealTimeRaceCount race2 in gameRealTimeBattlefieldRaces.Races)
		{
			int race = race2.Race;
			int count = race2.Count;
			if (race == 0 || race == 26 || !Enum.IsDefined(typeof(TAG_RACE), race))
			{
				continue;
			}
			if (count >= 0)
			{
				availableRacesInBattlegroundsExcludingAmalgam.Add((TAG_RACE)race);
				continue;
			}
			switch (count)
			{
			case -1:
				missingRacesInBattlegrounds.Add((TAG_RACE)race);
				break;
			case -2:
				inactiveRacesInBattlegrounds.Add((TAG_RACE)race);
				break;
			}
		}
		availableRacesInBattlegroundsExcludingAmalgam.Sort((TAG_RACE a, TAG_RACE b) => string.Compare(GameStrings.GetRaceNameBattlegrounds(a), GameStrings.GetRaceNameBattlegrounds(b), StringComparison.Ordinal));
		missingRacesInBattlegrounds.Sort((TAG_RACE a, TAG_RACE b) => string.Compare(GameStrings.GetRaceNameBattlegrounds(a), GameStrings.GetRaceNameBattlegrounds(b), StringComparison.Ordinal));
		if (MulliganManager.Get() != null && MulliganManager.Get().IsMulliganActive())
		{
			MulliganManager.Get().OnRealtimeAvailableRaces();
		}
	}

	private void OnPlayerRealTimeBattlefieldRaces()
	{
		PlayerRealTimeBattlefieldRaces playerRealTimeBattlefieldRaces = Network.Get().GetPlayerRealTimeBattlefieldRaces();
		if (PlayerLeaderboardManager.Get() != null)
		{
			PlayerLeaderboardManager.Get().UpdatePlayerRaces(playerRealTimeBattlefieldRaces);
		}
	}

	private void OnBattlegroundsMinionPool()
	{
		BattlegroundsMinionPool battlegroundsMinionPool = Network.Get().GetBattlegroundsMinionPool();
		if (BattlegroundsMinionGalleryManager.Get() != null)
		{
			BattlegroundsMinionGalleryManager.Get().SetPoolData(battlegroundsMinionPool);
		}
	}

	private void OnGameGuardianVars()
	{
		GameGuardianVars gameGuardianVars = Network.Get().GetGameGuardianVars();
		if (GameState.Get() != null)
		{
			GameState.Get().UpdateGameGuardianVars(gameGuardianVars);
		}
	}

	private void OnFakeConcede()
	{
		GameState.Get()?.FakeConceded();
	}

	private void OnBattlegroundInfo()
	{
		UpdateBattlegroundInfo battlegroundInfo = Network.Get().GetBattlegroundInfo();
		if (GameState.Get() != null)
		{
			GameState.Get().UpdateBattlegroundInfo(battlegroundInfo);
		}
	}

	private void OnBattlegroundArmorTierList()
	{
		GetBattlegroundHeroArmorTierList battlegroundHeroArmorTierList = Network.Get().GetBattlegroundHeroArmorTierList();
		if (GameState.Get() != null)
		{
			GameState.Get().UpdateBattlegroundArmorTierList(battlegroundHeroArmorTierList);
		}
	}

	private void OnBattlegroundsPlayerAnomaly()
	{
		GetBattlegroundsPlayerAnomaly battlegroundsPlayerAnomaly = Network.Get().GetBattlegroundsPlayerAnomaly();
		if (GameState.Get() != null)
		{
			GameState.Get().UpdateBattlegroundsPlayerAnomaly(battlegroundsPlayerAnomaly);
		}
	}

	private void OnTeammateEntities()
	{
		TeammatesEntities teammateEntities = Network.Get().GetTeammateEntities();
		if (TeammateBoardViewer.Get() != null)
		{
			TeammateBoardViewer.Get().UpdateTeammateEntities(teammateEntities);
		}
	}

	private void OnTeammatesChooseEntities()
	{
		TeammatesChooseEntities teammatesChooseEntities = Network.Get().GetTeammatesChooseEntities();
		if (TeammateBoardViewer.Get() != null)
		{
			TeammateBoardViewer.Get().UpdateTeammatesChooseEntities(teammatesChooseEntities);
		}
	}

	private void OnTeammatesEntitiesChosen()
	{
		TeammatesEntitiesChosen teammatesEntitiesChosen = Network.Get().GetTeammatesEntitiesChosen();
		if (TeammateBoardViewer.Get() != null)
		{
			TeammateBoardViewer.Get().UpdateTeammatesEntitiesChosen(teammatesEntitiesChosen);
		}
	}

	private void OnTeammateShopChoiceEnded()
	{
		if (TeammateBoardViewer.Get() != null)
		{
			TeammateBoardViewer.Get().TeammateShopChoiceEnded();
		}
	}

	private void OnTeammateConcede()
	{
		if (Network.Get().GetTeammateConcede().Penalized)
		{
			RegisterCoroutine(ShowTeammateConcedePopupWhenReady());
		}
	}

	private IEnumerator ShowTeammateConcedePopupWhenReady()
	{
		while (GameMenu.Get() == null || DialogManager.Get() == null)
		{
			yield return null;
		}
		ShowTeammateConcedePopup();
	}

	private void ShowTeammateConcedePopup()
	{
		GameMenu.Get().SetTeammateConceded(conceded: true);
		DialogManager.Get().ClearAllImmediatelyDontDestroy();
		AlertPopup.PopupInfo info = new AlertPopup.PopupInfo
		{
			m_headerText = GameStrings.Get("GLUE_TEAMMATE_CONCEDED_HEADER"),
			m_text = GameStrings.Get("GLUE_TEAMMATE_CONCEDED_BODY"),
			m_confirmText = GameStrings.Get("GLUE_TEAMMATE_CONCEDED_CONFIRM"),
			m_showAlertIcon = true,
			m_alertTextAlignmentAnchor = UberText.AnchorOptions.Middle,
			m_responseDisplay = AlertPopup.ResponseDisplay.CONFIRM
		};
		DialogManager.Get().ShowPopup(info);
	}

	private void OnReplaceBattlegroundMulliganHero()
	{
		ReplaceBattlegroundMulliganHero packet = Network.Get().ReplaceBattlegroundMulliganHero();
		MulliganManager mulliganManager = MulliganManager.Get();
		if (mulliganManager != null && mulliganManager.IsMulliganActive())
		{
			mulliganManager.ReplaceMulliganHero(packet);
		}
		else
		{
			Log.All.Print("OnReplaceBattlegroundMulliganHero - Mulligan is null or inactive");
		}
	}

	private void OnEntityPinged()
	{
		EntityPinged entityPinged = Network.Get().GetEntityPinged();
		Entity entity = null;
		TeammateBoardViewer teammateBoardViewer = TeammateBoardViewer.Get();
		if (teammateBoardViewer == null || !AreCriticalAssetsLoaded() || GameState.Get() == null)
		{
			return;
		}
		int playerId = GameState.Get().GetFriendlySidePlayer().GetTag(GAME_TAG.BACON_DUO_TEAMMATE_PLAYER_ID);
		if (!(EnemyEmoteHandler.Get() != null) || !EnemyEmoteHandler.Get().IsSquelched(playerId))
		{
			if (entityPinged.TeammateOwned)
			{
				entity = GameState.Get().GetEntity(entityPinged.EntityID);
			}
			else if (teammateBoardViewer != null)
			{
				entity = teammateBoardViewer.GetTeammateEntity(entityPinged.EntityID);
				teammateBoardViewer.PingPortal(entity, entityPinged.PingType);
			}
			if (entity != null && !(entity.GetCard() == null) && !(entity.GetCard().GetActor() == null))
			{
				entity.GetCard().GetActor().PingSelected((TEAMMATE_PING_TYPE)entityPinged.PingType);
			}
		}
	}

	private void OnScriptLogMessage()
	{
		ScriptLogMessage scriptLogMessage = Network.Get().GetScriptLogMessage();
		if (SceneDebugger.Get() != null)
		{
			SceneDebugger.Get().AddServerScriptLogMessage(scriptLogMessage);
		}
	}

	private bool AreCriticalAssetsLoaded()
	{
		return m_criticalAssetsLoaded;
	}

	private bool CheckCriticalAssetLoads()
	{
		if (m_criticalAssetsLoaded)
		{
			return true;
		}
		if (Board.Get() == null)
		{
			return false;
		}
		if (BaconBoard.Get() == null && m_loadingBaconBoard)
		{
			return false;
		}
		if (BoardCameras.Get() == null)
		{
			return false;
		}
		if (GetBoardLayout() == null)
		{
			return false;
		}
		if (GameMgr.Get().IsTraditionalTutorial() && BoardTutorial.Get() == null)
		{
			return false;
		}
		if (MulliganManager.Get() == null)
		{
			return false;
		}
		if (TurnStartManager.Get() == null)
		{
			return false;
		}
		if (TargetReticleManager.Get() == null)
		{
			return false;
		}
		if (GameplayErrorManager.Get() == null)
		{
			return false;
		}
		if (EndTurnButton.Get() == null)
		{
			return false;
		}
		if (BigCard.Get() == null)
		{
			return false;
		}
		if (CardTypeBanner.Get() == null)
		{
			return false;
		}
		if (TurnTimer.Get() == null)
		{
			return false;
		}
		if (CardColorSwitcher.Get() == null)
		{
			return false;
		}
		if (RemoteActionHandler.Get() == null)
		{
			return false;
		}
		if (ChoiceCardMgr.Get() == null)
		{
			return false;
		}
		if (InputManager.Get() == null)
		{
			return false;
		}
		m_criticalAssetsLoaded = true;
		ProcessQueuedPowerHistory();
		return true;
	}

	private void InitCardBacks()
	{
		int friendlyCardBackID = 0;
		Player player = GameState.Get()?.GetFriendlySidePlayer();
		if (player != null)
		{
			friendlyCardBackID = player.GetCardBackId();
		}
		int opponentCardBackID = 0;
		Player player2 = GameState.Get()?.GetOpposingSidePlayer();
		if (player2 != null)
		{
			opponentCardBackID = player2.GetCardBackId();
		}
		CardBackManager.Get().SetGameCardBackIDs(friendlyCardBackID, opponentCardBackID);
	}

	private void LoadBoard(Network.GameSetup setup)
	{
		AssetReference assetReference = null;
		BoardDbfRecord record = GameDbf.Board.GetRecord(setup.Board);
		m_baconFavoriteBoardSkin = setup.BaconFavoriteBoardSkin;
		m_baconTeammateFavoriteBoardSkin = setup.BaconTeammateFavoriteBoardSkin;
		if (record == null)
		{
			if (GameMgr.Get().IsBattlegrounds())
			{
				Debug.LogError($"Gameplay.LoadBoard() - FAILED to load board id: \"{setup.Board}\" for battelgrounds");
				record = GameDbf.Board.GetRecord((BoardDbfRecord r) => r.NoteDesc == "Bacon");
				UIStatus.Get().AddInfo($"Failed to Load board ID: {setup.Board}, defaulting back to Bacon {record.ID}.");
			}
			else
			{
				Debug.LogError($"Gameplay.LoadBoard() - FAILED to load board id: \"{setup.Board}\"");
				UIStatus.Get().AddInfo($"Failed to Load board ID: {setup.Board}, defaulting back to 1.");
				record = GameDbf.Board.GetRecord(1);
			}
		}
		assetReference = new AssetReference(record.Prefab);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, assetReference, OnBoardLoaded);
	}

	private async UniTaskVoid NotifyPlayersOfBoardLoad(CancellationToken token = default(CancellationToken))
	{
		while (GetBoardLayout() == null)
		{
			await UniTask.Yield(PlayerLoopTiming.Initialization, token);
		}
		foreach (Player value in GameState.Get().GetPlayerMap().Values)
		{
			value.OnBoardLoaded();
		}
	}

	private void OnBoardLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		m_boardProgress = -1f;
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnBoardLoaded() - FAILED to load board \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		go.GetComponent<Board>().SetBoardDbId(GameMgr.Get().GetGameSetup().Board);
		string text = (UniversalInputManager.UsePhoneUI ? "BoardCameras_phone.prefab:1e862adebb4fd4fca8b24249d32f8d86" : "BoardCameras.prefab:b4f3a6717904ff34985655c86149f06c");
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, text, OnBoardCamerasLoaded);
		if (GameMgr.Get().IsTraditionalTutorial())
		{
			m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "BoardTutorial.prefab:08bd830fc30e15e48a4b56bfc3abee15", OnBoardTutorialLoaded);
		}
		if (BaconBoard.Get() != null)
		{
			m_loadingBaconBoard = true;
			BaconBoard.Get().RegisterAllAssetsLoadedCallback(OnBaconFavoriteBoardLoaded);
			BaconBoard.Get().LoadInitialTavernBoard(m_baconFavoriteBoardSkin);
			if (GameMgr.Get().IsBattlegroundDuoGame())
			{
				BaconBoard.Get().LoadTeammateTavernBoard(m_baconTeammateFavoriteBoardSkin);
			}
		}
		Scenario.BoardLayout boardLayout = (Scenario.BoardLayout)GameMgr.Get().GetGameSetup().BoardLayout;
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, BoardLayout.GetBoardLayoutPrefab(boardLayout), OnBoardLayoutLoaded);
	}

	private void OnBaconFavoriteBoardLoaded()
	{
		m_loadingBaconBoard = false;
	}

	private void OnBoardCamerasLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnBoardCamerasLoaded() - FAILED to load \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		go.transform.parent = Board.Get().transform;
		m_inputCamera = Camera.main;
		PegUI.Get().AddInputCamera(m_inputCamera);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "CardTypeBanner.prefab:3b446c3c5a48357438d8aa969b5c377d", AssetLoadingOptions.IgnorePrefabPosition);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "BigCard.prefab:c938058e4609a1146b7ce8a115cc82df", AssetLoadingOptions.IgnorePrefabPosition);
	}

	private void OnBoardLayoutLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnBoardLayoutLoaded() - FAILED to load \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		m_boardLayout = go.GetComponent<BoardLayout>();
		go.transform.parent = Board.Get().transform;
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "EndTurnButton.prefab:313ebd8bcb770a944be3633ad928096b", OnEndTurnButtonLoaded, null, AssetLoadingOptions.IgnorePrefabPosition);
		m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "TurnTimer.prefab:aa1be1e4f5b36ca4aa6a38ac7d0538ce", OnTurnTimerLoaded, null, AssetLoadingOptions.IgnorePrefabPosition);
	}

	private void OnBoardTutorialLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnBoardTutorialLoaded() - FAILED to load \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		DoTraditionalTutorialPreload();
		go.transform.parent = Board.Get().transform;
	}

	private void OnEndTurnButtonLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnEndTurnButtonLoaded() - FAILED to load \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		EndTurnButton component = go.GetComponent<EndTurnButton>();
		if (component == null)
		{
			Debug.LogError($"Gameplay.OnEndTurnButtonLoaded() - ERROR \"{base.name}\" has no {typeof(EndTurnButton)} component");
			return;
		}
		component.transform.position = Board.Get().FindBone("EndTurnButton").position;
		Renderer[] componentsInChildren = go.GetComponentsInChildren<Renderer>();
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!renderer.gameObject.GetComponent<TextMesh>())
			{
				renderer.GetMaterial().color = Board.Get().m_EndTurnButtonColor;
			}
		}
	}

	private void OnTurnTimerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnTurnTimerLoaded() - FAILED to load \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		TurnTimer component = go.GetComponent<TurnTimer>();
		if (component == null)
		{
			Debug.LogError($"Gameplay.OnTurnTimerLoaded() - ERROR \"{base.name}\" has no {typeof(TurnTimer)} component");
		}
		else
		{
			component.transform.position = Board.Get().FindBone("TurnTimerBone").position;
		}
	}

	private void OnRemoteActionHandlerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnRemoteActionHandlerLoaded() - FAILED to load \"{assetRef}\"");
		}
		else if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
		}
		else
		{
			go.transform.parent = base.transform;
		}
	}

	private void OnTagVisualConfigurationLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnTagVisualConfigurationLoaded() - FAILED to load \"{assetRef}\"");
		}
		else if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
		}
		else
		{
			go.transform.parent = base.transform;
		}
	}

	private void OnChoiceCardMgrLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnChoiceCardMgrLoaded() - FAILED to load \"{assetRef}\"");
		}
		else if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
		}
		else
		{
			go.transform.parent = base.transform;
		}
	}

	private void OnInputManagerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnInputManagerLoaded() - FAILED to load \"{assetRef}\"");
		}
		else if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
		}
		else
		{
			go.transform.parent = base.transform;
		}
	}

	private void OnMulliganManagerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnMulliganManagerLoaded() - FAILED to load \"{assetRef}\"");
		}
		else if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
		}
		else
		{
			go.transform.parent = base.transform;
		}
	}

	private void OnTurnStartManagerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnTurnStartManagerLoaded() - FAILED to load \"{assetRef}\"");
		}
		else if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
		}
		else
		{
			go.transform.parent = base.transform;
		}
	}

	private void OnTargetReticleManagerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnTargetReticleManagerLoaded() - FAILED to load \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		go.transform.parent = base.transform;
		TargetReticleManager.Get().PreloadTargetArrows(m_prefabContext);
	}

	private void OnPlayerBannerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		Player.Side side = (Player.Side)callbackData;
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnPlayerBannerLoaded() - FAILED to load \"{assetRef}\" side={side.ToString()}");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		NameBanner component = go.GetComponent<NameBanner>();
		if (component == null)
		{
			Debug.LogError($"Gameplay.OnPlayerBannerLoaded() - FAILED to to find NameBanner component on \"{assetRef}\" side={side.ToString()}");
			return;
		}
		m_nameBanners.Add(component);
		m_numBannersRequested--;
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			if (base.name == "NameBannerGamePlay_phone")
			{
				m_nameBannerGamePlayPhone = component;
				m_nameBannerGamePlayPhone.Initialize(side);
			}
			else
			{
				component.Initialize(side);
			}
		}
		else
		{
			component.Initialize(side);
			if (GameState.Get() != null && GameState.Get().GetGameEntity() != null && !string.IsNullOrEmpty(GameState.Get().GetGameEntity().GetAlternatePlayerName()) && component.GetPlayerSide() == Player.Side.FRIENDLY)
			{
				component.UseLongName();
			}
		}
		ShowBannersWhenReady(GameStateToken).Forget();
	}

	private async UniTaskVoid ShowBannersWhenReady(CancellationToken token)
	{
		if (m_numBannersRequested > 0)
		{
			return;
		}
		foreach (NameBanner banner in m_nameBanners)
		{
			while (banner.IsWaitingForMedal)
			{
				await UniTask.Yield(PlayerLoopTiming.Update, token);
			}
		}
		foreach (NameBanner nameBanner in m_nameBanners)
		{
			nameBanner.Show();
		}
	}

	private void OnCardDrawStandinLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogError($"Gameplay.OnCardDrawStandinLoaded() - FAILED to load \"{assetRef}\"");
			return;
		}
		if (IsHandlingNetworkProblem())
		{
			m_prefabInstanceLoaderTracker.DestroyContext(m_prefabContext);
			return;
		}
		m_cardDrawStandIn = go.GetComponent<Actor>();
		go.GetComponentInChildren<CardBackDisplay>().SetCardBack(CardBackManager.CardBackSlot.FRIENDLY);
		m_cardDrawStandIn.Hide();
	}

	private void OnTransitionFinished(bool cutoff, object userData)
	{
		LoadingScreen.Get().UnregisterFinishedTransitionListener(OnTransitionFinished);
		if (cutoff || IsHandlingNetworkProblem())
		{
			return;
		}
		if (!GameMgr.Get().IsSpectator())
		{
			if (!GameMgr.Get().IsBattlegrounds())
			{
				BnetRecentPlayerMgr.Get().AddRecentPlayer(GameState.Get().GetOpposingPlayer()?.GetBnetPlayer(), BnetRecentPlayerMgr.RecentReason.CURRENT_OPPONENT);
			}
			else
			{
				Map<int, SharedPlayerInfo> playerInfoMap = GameState.Get().GetPlayerInfoMap();
				Player friendlySidePlayer = GameState.Get().GetFriendlySidePlayer();
				foreach (SharedPlayerInfo value in playerInfoMap.Values)
				{
					if (friendlySidePlayer.GetTag(GAME_TAG.BACON_DUO_TEAMMATE_PLAYER_ID) == value.GetPlayerId())
					{
						BnetRecentPlayerMgr.Get().AddRecentPlayer(value.GetBnetPlayer(), BnetRecentPlayerMgr.RecentReason.CURRENT_TEAMMATE);
					}
					else if (friendlySidePlayer.GetPlayerId() != value.GetPlayerId())
					{
						BnetRecentPlayerMgr.Get().AddRecentPlayer(value.GetBnetPlayer(), BnetRecentPlayerMgr.RecentReason.CURRENT_OPPONENT);
					}
				}
			}
		}
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			if (GameState.Get() != null && GameState.Get().IsMulliganPhase())
			{
				m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "NameBannerRight_phone.prefab:8712bbdedd6fa4a45b18dc88226d67b3", OnPlayerBannerLoaded, Player.Side.FRIENDLY);
				m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "NameBanner_phone.prefab:c919b2370a8d748d38e2cb4708e15398", OnPlayerBannerLoaded, Player.Side.OPPOSING);
				m_numBannersRequested += 2;
			}
			else if (GameMgr.Get() != null && !GameMgr.Get().IsTraditionalTutorial())
			{
				AddGamePlayNameBannerPhone();
			}
		}
		else
		{
			m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "NameBanner.prefab:f579c831653574d4da0437a5fcf0d58f", OnPlayerBannerLoaded, Player.Side.FRIENDLY);
			m_prefabInstanceLoaderTracker.InstantiatePrefab(m_prefabContext, "NameBanner.prefab:f579c831653574d4da0437a5fcf0d58f", OnPlayerBannerLoaded, Player.Side.OPPOSING);
			m_numBannersRequested += 2;
		}
	}

	private void DoTraditionalTutorialPreload()
	{
		BnetBar bnetBar = BnetBar.Get();
		if (!(bnetBar == null))
		{
			bnetBar.Dim();
			bnetBar.SetupSkipTutorialButton();
		}
	}

	public void DoTraditionalTutorialShow()
	{
		BnetBar bnetBar = BnetBar.Get();
		if (!(bnetBar == null))
		{
			bnetBar.Undim();
			bnetBar.ShowSkipTutorialButton();
		}
	}

	public void SkipTutorial()
	{
		bool num = GameUtils.HasEverCompletedTraditionalTutorial();
		Network.Get().Concede();
		GameUtils.CompleteTraditionalTutorial();
		GameState.Get().GetGameEntity()?.NotifyOfGameOver(TAG_PLAYSTATE.CONCEDED);
		BnetBar bnetBar = BnetBar.Get();
		if ((bool)bnetBar)
		{
			bnetBar.HideSkipTutorialButton();
		}
		if (!num)
		{
			CreateSkipHelper.QueueSkipScreenAtBox();
			Options.Get().SetBool(Option.SKIPPED_INITIAL_TUTORIAL, val: true);
		}
	}

	private void ProcessQueuedPowerHistory()
	{
		while (m_queuedPowerHistory.Count > 0)
		{
			List<Network.PowerHistory> powerList = m_queuedPowerHistory.Dequeue();
			GameState.Get().OnPowerHistory(powerList);
		}
	}

	private bool IsLeavingGameUnfinished()
	{
		if (GameState.Get() != null && GameState.Get().IsGameOver())
		{
			return false;
		}
		if (GameMgr.Get().IsReconnect())
		{
			return false;
		}
		if (SceneMgr.Get().IsModeRequested(SceneMgr.Mode.FATAL_ERROR))
		{
			return false;
		}
		return true;
	}

	private void OnCreateGame(GameState.CreateGamePhase phase, object userData)
	{
		switch (phase)
		{
		case GameState.CreateGamePhase.CREATING:
			InitCardBacks();
			NotifyPlayersOfBoardLoad(GameStateToken).Forget();
			break;
		case GameState.CreateGamePhase.CREATED:
			CardBackManager.Get().UpdateAllCardBacksInSceneWhenReady();
			break;
		}
	}

	private async UniTaskVoid SetGameStateBusyWithDelay(bool busy, float delay, CancellationToken token)
	{
		await UniTask.Delay(TimeSpan.FromSeconds(delay), ignoreTimeScale: false, PlayerLoopTiming.Update, token);
		GameState.Get().SetBusy(busy);
	}

	public void SaveOriginalTimeScale()
	{
		m_originalTimeScale = TimeScaleMgr.Get().GetGameTimeScale();
	}

	public void RestoreOriginalTimeScale()
	{
		if (m_originalTimeScale.HasValue)
		{
			TimeScaleMgr.Get().SetGameTimeScale(m_originalTimeScale.Value);
			m_originalTimeScale = null;
		}
	}

	public Coroutine RegisterCoroutine(IEnumerator routine)
	{
		return StartCoroutine(routine);
	}

	public void UnregisterCoroutine(Coroutine routine)
	{
		StopCoroutine(routine);
	}

	private bool OnProcessCheat_saveme(string func, string[] args, string rawArgs)
	{
		GameState.Get().DebugNukeServerBlocks();
		return true;
	}

	private bool OnProcessCheat_concede(string func, string[] args, string rawArgs)
	{
		GameState gameState = GameState.Get();
		if (gameState == null)
		{
			UIStatus.Get().AddInfo("No active game found!", 2f);
			return true;
		}
		gameState.Concede();
		return true;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '10.0.1.8346' (yours is '10.0.0.8330')
