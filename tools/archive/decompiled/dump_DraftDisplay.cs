using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets;
using Blizzard.T5.Configuration;
using Hearthstone;
using Hearthstone.DataModels;
using Hearthstone.UI;
using PegasusShared;
using UnityEngine;

[CustomEditClass]
public class DraftDisplay : MonoBehaviour
{
	public enum PhaseMessageType
	{
		Choose_Hero,
		Choose_Legendary,
		Choose_Card,
		Ready_Up,
		Redraft,
		Edit_Deck
	}

	public enum HeroAnimationState
	{
		NOT_ANIMATING,
		ANIMATING_ZOOM,
		ANIMATING_CANCEL,
		ANIMATING_SELECT
	}

	[Serializable]
	public class PhaseMessageDelayData
	{
		public ArenaClientStateType OptionalPreviousState;

		public ArenaClientStateType OptionalCurrentState;

		public PhaseMessageType PhaseMessage;

		public float Delay;
	}

	private class HeroLabelInstantiatedCallbackData
	{
		public Transform m_heroLabelBone;

		public DraftChoice m_choice;
	}

	private struct RedraftCardSlotParams
	{
		public Widget m_widget;

		public int m_index;
	}

	public enum DraftMode
	{
		INVALID,
		NO_ACTIVE_DRAFT,
		DRAFTING,
		ACTIVE_DRAFT_DECK,
		IN_REWARDS,
		REDRAFTING
	}

	public enum CardStatus
	{
		DEFAULT,
		LOCKED,
		BANNED
	}

	private class ChoiceCallback
	{
		public DefLoader.DisposableFullDef fullDef;

		public int choiceID;

		public int slot;

		public TAG_PREMIUM premium;

		public ChoiceCallback Copy()
		{
			return new ChoiceCallback
			{
				fullDef = fullDef?.Share(),
				choiceID = choiceID,
				slot = slot,
				premium = premium
			};
		}
	}

	private class DraftChoice
	{
		public string m_cardID = string.Empty;

		public TAG_PREMIUM m_premium;

		public Actor m_actor;

		public Actor m_subActor;

		public List<string> m_packageCardIds = new List<string>();

		public List<TAG_PREMIUM> m_packageCardPremiums = new List<TAG_PREMIUM>();
	}

	public VisualController m_arenaDraftScreenBroadcaster;

	public VisualController m_phasePopupVisualController;

	public WidgetInstance m_collectionPageAWidget;

	public WidgetInstance m_collectionPageBWidget;

	public List<WidgetInstance> m_redraftCardSlotWidgets = new List<WidgetInstance>();

	public List<WidgetInstance> m_manaCurveWidgets = new List<WidgetInstance>();

	public GameObject m_chooseHero;

	public GameObject m_chooseCard;

	public PegUIElement m_heroClickCatcher;

	public GameObject m_heroLabel;

	public Spell m_DeckCompleteSpell;

	public float m_DeckCardBarFlareUpDelay;

	public Spell m_heroPowerChosenFadeOut;

	public Spell m_heroPowerChosenFadeIn;

	public List<DraftPhoneDeckTray> m_draftDeckTrays;

	public GameObject m_rewardsWidgetBone;

	[CustomEditField(T = EditType.GAME_OBJECT)]
	public string m_rewardsWidgetPrefab;

	private Widget m_rewardsWidget;

	[CustomEditField(T = EditType.GAME_OBJECT)]
	public string m_rewardsBoxesPrefab;

	public WidgetInstance m_packageCardsPopupWidget;

	private List<HeroLabel> m_currentLabels = new List<HeroLabel>();

	[CustomEditField(Sections = "Misc", T = EditType.GAME_OBJECT)]
	public string m_heroLabelPrefab;

	public UberText m_cardCountText;

	public UberText m_cardCountTextPhoneRedraftCollection;

	[CustomEditField(Sections = "Buttons")]
	public WidgetInstance m_playButtonWidget;

	private PlayButton m_playButton;

	[CustomEditField(Sections = "Buttons")]
	public WidgetInstance m_doneButtonWidget;

	private UIBButton m_doneButton;

	public WidgetInstance m_retireButtonWidget;

	public WidgetInstance m_viewDeckButtonWidget;

	public GameObject m_midrunPhoneBackButton;

	[CustomEditField(Sections = "Bones")]
	public Transform m_bigHeroBone;

	[CustomEditField(Sections = "Bones")]
	public Transform m_bigHeroPowerBone;

	[CustomEditField(Sections = "Bones")]
	public Transform m_socketHeroBone;

	[CustomEditField(Sections = "Bones")]
	public Transform m_midDraftSocketHeroBone;

	[CustomEditField(Sections = "Bones")]
	public List<Transform> m_heroPowerBones = new List<Transform>();

	[CustomEditField(Sections = "Bones")]
	public Transform m_socketHeroPowerBone;

	[CustomEditField(Sections = "Bones")]
	public List<Transform> m_heroBones = new List<Transform>();

	[CustomEditField(Sections = "Bones")]
	public List<Transform> m_cardBones = new List<Transform>();

	[CustomEditField(Sections = "Bones")]
	public List<Transform> m_heroLabelBones = new List<Transform>();

	[CustomEditField(Sections = "Phone")]
	public GameObject m_PhonePlayButtonTray;

	[CustomEditField(Sections = "Phone")]
	public Transform m_PhoneBackButtonBone;

	[CustomEditField(Sections = "Phone")]
	public Transform m_PhoneDeckTrayHiddenBone;

	[CustomEditField(Sections = "Phone")]
	public GameObject m_Phone3WayButtonRoot;

	[CustomEditField(Sections = "Phone")]
	public GameObject m_PhoneChooseHero;

	[CustomEditField(Sections = "Phone")]
	public GameObject m_PhoneLargeViewDeckButton;

	[CustomEditField(Sections = "Phone")]
	public ArenaPhoneControl m_PhoneDeckControl;

	[CustomEditField(Sections = "Phone")]
	public UIBButton m_PhoneDraftBackButton;

	[CustomEditField(Sections = "Draft Text")]
	public GameObject HeroTitleText;

	[CustomEditField(Sections = "Draft Text")]
	public GameObject HeroLabelsText;

	[CustomEditField(Sections = "Draft Text")]
	public GameObject DraftTitleText;

	[CustomEditField(Sections = "Phase Message")]
	public GameObject m_PhasePopup;

	public List<PhaseMessageDelayData> m_phaseMessageDelayData = new List<PhaseMessageDelayData>();

	[CustomEditField(Sections = "Sounds", T = EditType.SOUND_PREFAB)]
	public string m_dropCardInSlotSound;

	[CustomEditField(Sections = "Sounds", T = EditType.SOUND_PREFAB)]
	public string m_removeCardInSlotSound;

	[CustomEditField(Sections = "Sounds", T = EditType.SOUND_PREFAB)]
	public string m_swapCardInSlotSound;

	private const string ALERTPOPUPID_FIRSTTIME = "arena_first_time";

	private const string CODE_START_DRAFT = "CODE_START_DRAFT";

	private const string CODE_START_REDRAFT = "CODE_START_REDRAFT";

	private const string CODE_START_MIDRUN = "CODE_START_MIDRUN";

	private const string CODE_SLIDEIN_COLLECTION = "CODE_SLIDEIN_COLLECTION";

	private const string CODE_SLIDEIN_MIDRUN = "CODE_SLIDEIN_MIDRUN";

	private const string CODE_SLIDEIN_DRAFT = "CODE_SLIDEIN_DRAFT";

	private const string CODE_SLIDEIN_REDRAFT = "CODE_SLIDEIN_REDRAFT";

	private const string CODE_MOBILE_DRAFTING_VIEWDECK = "SLIDEIN_MOBILE_DRAFTING_VIEWDECK";

	private const string CODE_NOT_IN_DRAFT_SELECTION = "CODE_NOT_IN_DRAFT_SELECTION";

	private const string CODE_IN_DRAFT_SELECTION = "CODE_IN_DRAFT_SELECTION";

	private const string CODE_CHOOSEHEROPHASE_POPUP = "CODE_CHOOSEHEROPHASE_POPUP";

	private const string CODE_DRAFTPHASE_POPUP = "CODE_DRAFTPHASE_POPUP";

	private const string CODE_READYPHASE_POPUP = "CODE_READYPHASE_POPUP";

	private const string CODE_PACKAGE_CARDS_POPUP = "CODE_LEGENDARYBUCKET_POPUP";

	private const string CODE_PACKAGE_CARDS_POPUP_CHOICE_CONFIRMED = "CHOICE_CONFIRMED";

	private const string CODE_REWARDS_FLOW = "CODE_REWARDS_FLOW";

	private const string CODE_REWARDCHEST_CLAIMED = "REWARDCHEST_CLAIMED";

	private const string CODE_REWARDS_DONE = "REWARDS_DONE";

	private const string CODE_RETIRE_PRESSED = "RETIRE_PRESSED";

	private const string CODE_EDITDECK_PRESSED = "EDITDECK_PRESSED";

	private const string CODE_VIEWDECK_PRESSED = "VIEWDECK_PRESSED";

	private const string CODE_START_VIEWDECK = "START_VIEWDECK";

	private const string CODE_MOVE_TO_LANDING = "MOVETO_LANDING";

	private const string CODE_PAGE_LEFT = "PAGE_LEFT";

	private const string CODE_PAGE_RIGHT = "PAGE_RIGHT";

	private const string CODE_ADD_CARD = "ADD_CARD";

	private const string CODE_DRAG_CARD = "DRAG_CARD";

	private const string CODE_REVERT_CHANGES_PRESSED = "REVERT_CHANGES_PRESSED";

	private const string CODE_MANAGER_START_DRAFTING = "START_DRAFTINGPAGE";

	private const string CODE_MANAGER_START_LANDING = "START_LANDINGPAGE";

	private const string CODE_START_NORMAL_ARENA = "START_NORMAL_ARENA";

	private const string CODE_START_UNDERGROUND_ARENA = "START_UNDERGROUND_ARENA";

	private const string REWARDS_FLOW_EVENT = "REWARDS_FLOW";

	private const string REWARDS_DONE_EVENT = "REWARDS_DONE";

	private const string PACKAGE_CARDS_DISMISSED_EVENT = "PACKAGE_CARDS_DISMISSED";

	private const string AUDIO_PAGE_FLIP_BACK_ASSET = "collection_manager_book_page_flip_back.prefab:371e496e1cd371144abfec472e72d9a9";

	private const string AUDIO_PAGE_FLIP_FORWARD_ASSET = "collection_manager_book_page_flip_forward.prefab:07282310dd70fee4ca2dfdb37c545acc";

	private const int CARDS_PER_ROW = 4;

	private const int ROWS_PER_PAGE = 2;

	private const int CARDS_PER_PAGE = 8;

	private const string PHASE_POPUP_HERO = "CHOOSE_HERO_PHASE_POPUP";

	private const string PHASE_POPUP_DRAFT = "DRAFT_PHASE_POPUP";

	private const string PHASE_POPUP_LEGENDARY = "CHOOSE_LEGENDARY_PHASE_POPUP";

	private const string PHASE_POPUP_READY = "READY_PHASE_POPUP";

	private const string PHASE_POPUP_REDRAFT = "REDRAFT_PHASE_POPUP";

	private const string PHASE_POPUP_EDITDECK = "EDIT_DECK_POPUP";

	private const string PHASE_POPUP_CLOSED = "PHASE_POPUP_CLOSED";

	private const string CHOOSE_DEFAULT = "DEFAULT";

	private const string CHOOSE_NORMAL_HEADER = "CHOOSE_NORMAL_HEADER";

	private const string CHOOSE_LEGENDARY_HEADER = "CHOOSE_LEGENDARY_HEADER";

	private const string EDIT_YOUR_DECK_HEADER = "EDIT_YOUR_DECK_HEADER";

	private const string PLAY_BUTTON_TEXT = "GLOBAL_PLAY";

	private const string REDRAFT_BUTTON_TEXT = "GLUE_ARENA_DRAFT_REDRAFT_HEADER";

	private const string EDIT_DECK_BUTTON_TEXT = "GLUE_EDIT_DECK";

	private static DraftDisplay s_instance;

	private DraftManager m_draftManager;

	private ArenaDraftScreen m_arenaDraftScreen;

	private Widget m_arenaDraftScreenWidget;

	private VisualController m_rewardsVisualController;

	private RewardBoxesDisplay m_rewardBoxesDisplay;

	private GameObject m_rewardsBoxesBone;

	private bool m_isHandlingChoiceDisplay;

	private bool m_hasSeenDraftChoices;

	private bool m_hasSeenRedraftChoices;

	private bool m_phaseMessageShowing;

	private List<DraftChoice> m_choices = new List<DraftChoice>();

	private Actor[] m_heroPowerCardActors = new Actor[3];

	private Actor[] m_mythicPowerCardActors = new Actor[3];

	private CornerReplacementSpellType[] m_mythicHeroTypes = new CornerReplacementSpellType[3];

	private DefLoader.DisposableFullDef[] m_heroPowerDefs = new DefLoader.DisposableFullDef[3];

	private DefLoader.DisposableFullDef[] m_subClassHeroPowerDefs = new DefLoader.DisposableFullDef[3];

	private DraftMode m_currentMode;

	private NormalButton m_confirmButton;

	private Actor m_heroPower;

	private Actor m_defaultHeroPowerSkin;

	private Actor m_goldenHeroPowerSkin;

	private bool m_netCacheReady;

	private Actor m_chosenHero;

	private Actor m_inPlayHeroPowerActor;

	private bool m_animationsComplete = true;

	private CardSoundSpell[] m_heroEmotes = new CardSoundSpell[3];

	private bool m_skipHeroEmotes;

	private HeroAnimationState m_heroAnimationState;

	private DraftCardVisual m_zoomedHero;

	private bool m_wasDrafting;

	private DialogBase m_firstTimeDialog;

	private bool m_fxActive;

	private string m_draggingCardId;

	private TAG_PREMIUM m_draggingCardPremium;

	private bool m_isDraggingCardFromCollection;

	private RedraftCardSlot m_draggingOriginCardSlot;

	private RedraftCardSlot m_hoveredCardSlot;

	private List<DraftManaCurve> m_manaCurves = new List<DraftManaCurve>();

	private List<Actor> m_subclassHeroClones = new List<Actor>();

	private Actor[] m_subclassHeroPowerActors = new Actor[3];

	private ScreenEffectsHandle m_screenEffectsHandle;

	private List<int> m_collectionCardIds = new List<int>();

	private CardListDataModel m_collectionCardDataModelList = new CardListDataModel();

	private int m_currentCollectionPageIndex;

	private PackageCardsPopup m_packageCardsPopup;

	private int m_currentlyViewedPackagePopupIndex = -1;

	private ArenaLandingPageManager m_arenaLandingPageManager;

	private Widget m_arenaLandingPageManagerWidget;

	private Widget m_thisWidget;

	private bool m_loadingComplete;

	private PageTurn m_pageTurn;

	private ArenaCollectionPage m_collectionPageA;

	private ArenaCollectionPage m_collectionPageB;

	private ArenaCollectionPage m_activeCollectionPage;

	private Vector3 m_initialCollectionPagePosition = Vector3.zero;

	private const float m_collectionPageOffset = 500f;

	private bool m_isPageTurning;

	private int m_deckCount;

	private int m_maxDeckCount;

	private int m_zoomedHeroIndex;

	private List<RedraftCardSlot> m_redraftCardSlots = new List<RedraftCardSlot>();

	private bool m_areDraftCardsBeingShown;

	private ArenaDraftScreenDataModel m_arenaDraftScreenDataModel;

	private ArenaChestDataModel m_arenaChestDataModel;

	public bool IsRewardsFlowPending { get; private set; }

	public Actor ChosenHero => m_chosenHero;

	private void Awake()
	{
		s_instance = this;
		m_draftManager = DraftManager.Get();
		if (m_draftManager != null)
		{
			m_draftManager.InitializeDataModelArenaStates();
		}
		AssetLoader.Get().InstantiatePrefab("DraftHeroChooseButton.prefab:7640de5f1d8e50e4caf8dccc55f28c6a", OnConfirmButtonLoaded, null, AssetLoadingOptions.IgnorePrefabPosition);
		AssetLoader.Get().LoadAsset<GameObject>(m_rewardsWidgetPrefab);
		AssetLoader.Get().InstantiatePrefab("History_HeroPower.prefab:e73edf8ccea2b11429093f7a448eef53", LoadHeroPowerCallback, null, AssetLoadingOptions.IgnorePrefabPosition);
		AssetLoader.Get().InstantiatePrefab(ActorNames.GetNameWithPremiumType(ActorNames.ACTOR_ASSET.HISTORY_HERO_POWER, TAG_PREMIUM.GOLDEN), LoadGoldenHeroPowerCallback, null, AssetLoadingOptions.IgnorePrefabPosition);
		m_draftManager.RegisterDisplayHandlers();
		SceneMgr.Get().RegisterScenePreUnloadEvent(OnScenePreUnload);
		if (string.IsNullOrEmpty(m_draftManager.GetSceneHeadlineText()))
		{
			GameStrings.Get("GLUE_TOOLTIP_BUTTON_FORGE_HEADLINE");
		}
		m_screenEffectsHandle = new ScreenEffectsHandle(this);
		m_draftManager?.RegisterDraftDeckSetListener(OnDraftDeckInitialized);
		m_draftManager.OnArenaClientStateUpdated += OnArenaClientStateUpdated;
	}

	private void OnDestroy()
	{
		m_draftManager?.RemoveDraftDeckSetListener(OnDraftDeckInitialized);
		m_draftManager.OnArenaClientStateUpdated -= OnArenaClientStateUpdated;
		DefLoader.DisposableFullDef[] heroPowerDefs = m_heroPowerDefs;
		for (int i = 0; i < heroPowerDefs.Length; i++)
		{
			heroPowerDefs[i]?.Dispose();
		}
		heroPowerDefs = m_subClassHeroPowerDefs;
		for (int i = 0; i < heroPowerDefs.Length; i++)
		{
			heroPowerDefs[i]?.Dispose();
		}
		FadeEffectsOut();
		s_instance = null;
	}

	private void Start()
	{
		Navigation.Push(OnNavigateBack);
		NetCache.Get().RegisterScreenForge(OnNetCacheReady);
		m_thisWidget = GetComponent<Widget>();
		if (m_thisWidget != null)
		{
			m_thisWidget.RegisterEventListener(HandleDraftDisplayEvent);
		}
		m_arenaDraftScreen = GetComponent<ArenaDraftScreen>();
		if (m_arenaDraftScreen != null)
		{
			Widget widget = m_arenaDraftScreen.GetWidget();
			if (widget != null)
			{
				m_arenaDraftScreenWidget = widget;
				m_arenaDraftScreenWidget.RegisterReadyListener(delegate
				{
					m_arenaDraftScreenWidget.RegisterEventListener(HandleArenaDraftScreenEvent);
					UpdateArenaDraftScreenDataModel();
				});
			}
			m_pageTurn = m_arenaDraftScreen.GetPageTurn();
		}
		if (m_draftManager == null)
		{
			return;
		}
		m_arenaLandingPageManager = GetComponentInParent<ArenaLandingPageManager>();
		if (m_arenaLandingPageManager != null)
		{
			m_arenaLandingPageManagerWidget = m_arenaLandingPageManager.GetWidget();
			m_arenaLandingPageManager.SetOnMoveToDraftingCallback(OnMoveFromLanding);
			m_arenaLandingPageManager.SetOnMoveToDeckCallback(OnMoveToDeck);
		}
		bool flag = ShouldShowRewards();
		if (SceneMgr.Get().GetPrevMode() == SceneMgr.Mode.GAMEPLAY && !flag)
		{
			if (m_draftManager.IsRedrafting())
			{
				TriggerRedraft();
			}
			BroadcastArenaDraftScreenEvent("CODE_START_MIDRUN");
			BroadcastArenaLandingPageManagerEvent("START_DRAFTINGPAGE");
		}
		else
		{
			BroadcastArenaLandingPageManagerEvent("START_LANDINGPAGE");
			BroadcastArenaDraftScreenEvent("CODE_START_DRAFT");
		}
		m_draftManager.SetClientState((!m_draftManager.IsUnderground()) ? ArenaClientStateType.Normal_Landing : ArenaClientStateType.Underground_Landing);
		if (m_playButtonWidget != null)
		{
			m_playButtonWidget.RegisterReadyListener(delegate
			{
				m_playButton = m_playButtonWidget.gameObject.GetComponentInChildren<PlayButton>();
				if (m_playButton != null)
				{
					m_playButton.AddEventListener(UIEventType.RELEASE, PlayButtonPress);
					DeterminePlayButtonText();
				}
			});
		}
		foreach (WidgetInstance manaCurveWidget in m_manaCurveWidgets)
		{
			WidgetInstance thisManaCurveWidget = manaCurveWidget;
			thisManaCurveWidget.RegisterReadyListener(delegate
			{
				List<DraftManaCurve> list = new List<DraftManaCurve>();
				thisManaCurveWidget.GetComponentsInChildren(includeInactive: true, list);
				foreach (DraftManaCurve item in list)
				{
					item.GetComponent<PegUIElement>().AddEventListener(UIEventType.ROLLOVER, ManaCurveOver);
					item.GetComponent<PegUIElement>().AddEventListener(UIEventType.ROLLOUT, ManaCurveOut);
				}
				m_manaCurves.AddRange(list);
				InitManaCurve();
			});
		}
		if (m_doneButtonWidget != null)
		{
			m_doneButtonWidget.RegisterReadyListener(delegate
			{
				m_doneButton = m_doneButtonWidget.gameObject.GetComponentInChildren<UIBButton>();
			});
		}
		if (flag)
		{
			AnimateRewards();
		}
		if (m_packageCardsPopupWidget != null)
		{
			m_packageCardsPopupWidget.RegisterReadyListener(delegate
			{
				m_packageCardsPopupWidget.RegisterEventListener(HandlePackageCardsPopupEvent);
				m_packageCardsPopup = m_packageCardsPopupWidget.gameObject.GetComponentInChildren<PackageCardsPopup>();
			});
		}
		if (m_collectionPageAWidget != null)
		{
			m_collectionPageAWidget.RegisterReadyListener(delegate
			{
				m_collectionPageA = m_collectionPageAWidget.GetComponentInChildren<ArenaCollectionPage>();
				m_initialCollectionPagePosition = m_collectionPageA.transform.localPosition;
			});
		}
		if (m_collectionPageBWidget != null)
		{
			m_collectionPageAWidget.RegisterReadyListener(delegate
			{
				m_collectionPageB = m_collectionPageBWidget.GetComponentInChildren<ArenaCollectionPage>();
			});
		}
		if (m_redraftCardSlotWidgets != null)
		{
			for (int num = 0; num < m_redraftCardSlotWidgets.Count; num++)
			{
				Widget widget2 = m_redraftCardSlotWidgets[num];
				RedraftCardSlotParams redraftCardSlotParams = new RedraftCardSlotParams
				{
					m_widget = widget2,
					m_index = num
				};
				widget2.RegisterReadyListener(OnRedraftCardSlotReady, redraftCardSlotParams);
			}
		}
		Network.Get().RequestDraftChoicesAndContents(m_draftManager.IsUnderground());
		Network.Get().RequestArenaRatingInfo();
		if (m_draftManager.IsUnderground())
		{
			MusicManager.Get().StartPlaylist(MusicPlaylistType.UI_ArenaUnderground);
		}
		else
		{
			MusicManager.Get().StartPlaylist(MusicPlaylistType.UI_Arena);
		}
		StartCoroutine(NotifySceneLoadedWhenReady());
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
			{
				draftDeckTray.gameObject.SetActive(value: true);
			}
		}
		if (HearthstoneApplication.IsInternal())
		{
			CheatMgr.Get().RegisterCategory("arena");
			CheatMgr.Get().RegisterCheatHandler("setarenaphasemessagesshown", OnProcessCheat_SetPhaseMessagesShown, "Set the phase messages to have been seen (1) or not (0)");
		}
		m_areDraftCardsBeingShown = false;
	}

	private bool ShouldShowRewards()
	{
		if (m_draftManager.GetLosses() != m_draftManager.GetMaxLosses() && m_draftManager.GetWins() != m_draftManager.GetMaxWins())
		{
			return m_draftManager.GetArenaState(m_draftManager.IsUnderground()) == ArenaSessionState.REWARDS;
		}
		return true;
	}

	public void ShowRewardsIfNecessary()
	{
		if (ShouldShowRewards())
		{
			m_draftManager.SetArenaState(ArenaSessionState.REWARDS, m_draftManager.IsUnderground());
			AnimateRewards();
		}
	}

	private IEnumerator CreateRewardsWidgetPrefab(Action animationCallback)
	{
		ArenaLandingPageManager.Get().SetUIButtonsEnabled(enabled: false);
		while (!m_loadingComplete)
		{
			yield return null;
		}
		while (Box.Get().GetState() != Box.State.OPEN)
		{
			yield return null;
		}
		while (Box.Get().IsBusy())
		{
			yield return null;
		}
		while (SceneMgr.Get().GetMode() == SceneMgr.Mode.GAMEPLAY)
		{
			yield return null;
		}
		while (LoadingScreen.Get().IsTransitioning())
		{
			yield return null;
		}
		while (m_draftManager.IsUnderground() != m_draftManager.IsCurrentRewardUnderground)
		{
			yield return null;
		}
		SetUpRewardBoxesDataModel();
		AssetLoader.Get().InstantiatePrefab(m_rewardsWidgetPrefab, OnRewardsWidgetPrefabInstantiated, animationCallback);
	}

	private void OnRewardsWidgetPrefabInstantiated(AssetReference assetRef, GameObject instance, object callbackData)
	{
		instance.transform.parent = m_rewardsWidgetBone.transform;
		instance.transform.localPosition = Vector3.zero;
		instance.transform.localRotation.SetEulerAngles(Vector3.zero);
		instance.transform.localScale = Vector3.one;
		m_rewardsWidget = instance.GetComponent<Widget>();
		if (m_rewardsWidget != null)
		{
			m_rewardsWidget.RegisterReadyListener(OnRewardsWidgetReady, callbackData);
		}
	}

	private void OnRewardsWidgetReady(object rewardsAnimationCallbackData)
	{
		m_rewardsVisualController = m_rewardsWidget.gameObject.GetComponentInChildren<VisualController>();
		ArenaRewardsClaimPopup componentInChildren = m_rewardsWidget.GetComponentInChildren<ArenaRewardsClaimPopup>();
		if (componentInChildren != null)
		{
			m_rewardsBoxesBone = componentInChildren.m_rewardsBoxesBone;
		}
		BindDataModelsToRewardsWidget();
		if (rewardsAnimationCallbackData is Action action)
		{
			action();
		}
	}

	private void BindDataModelsToRewardsWidget()
	{
		if (m_rewardsWidget != null && m_rewardsWidget.IsReady && m_arenaChestDataModel != null)
		{
			m_rewardsWidget.BindDataModel(m_arenaChestDataModel);
		}
	}

	private void DestroyRewardsWidget()
	{
		m_rewardBoxesDisplay = null;
		m_rewardsVisualController = null;
		UnityEngine.Object.Destroy(m_rewardsWidget.gameObject);
		m_rewardsWidget = null;
	}

	private bool OnProcessCheat_SetPhaseMessagesShown(string func, string[] args, string rawArgs)
	{
		if (args.Length < 1)
		{
			UIStatus.Get().AddInfo("Please specify 1 as a parameter to mark messages seen or 0 to mark them unseen", 10f);
			return true;
		}
		if (!int.TryParse(args[0], out var result) || (result != 0 && result != 1))
		{
			UIStatus.Get().AddInfo("Unable to parse ID, please input a valid value (0 or 1)", 10f);
			return true;
		}
		foreach (PhaseMessageType value in Enum.GetValues(typeof(PhaseMessageType)))
		{
			SetPhaseMessageShownValue(value, (result != 0) ? true : false);
		}
		return true;
	}

	private void Update()
	{
		Network.Get().ProcessNetwork();
	}

	public static DraftDisplay Get()
	{
		return s_instance;
	}

	public bool IsLoadingComplete()
	{
		return m_loadingComplete;
	}

	public void OnOpenRewardsComplete()
	{
		GameMgr.Get().CancelFindGame();
		Box.Get().SetToIgnoreFullScreenEffects(ignoreEffects: false);
		m_rewardBoxesDisplay = null;
		if (m_arenaLandingPageManager != null)
		{
			m_arenaLandingPageManager.HideInactiveLandingPage();
		}
		if (m_arenaLandingPageManagerWidget != null)
		{
			m_arenaLandingPageManagerWidget.TriggerEvent("NO_ACTIVE_RUN");
		}
		if (m_draftManager.ShouldStartDraft())
		{
			BroadcastArenaLandingPageManagerEvent("START_LANDINGPAGE");
			BroadcastArenaDraftScreenEvent("CODE_START_DRAFT");
			m_draftManager.SetClientState((!m_draftManager.IsUnderground()) ? ArenaClientStateType.Normal_Landing : ArenaClientStateType.Underground_Landing);
		}
		CollectionManager.Get()?.ClearEditedDeck();
		m_choices.Clear();
		IsRewardsFlowPending = false;
		ArenaLandingPageManager.Get().SetUIButtonsEnabled(enabled: true);
		SetDraftMode(DraftMode.NO_ACTIVE_DRAFT);
	}

	public void OnApplicationPause(bool pauseStatus)
	{
		if (GameMgr.Get().IsFindingGame())
		{
			CancelFindGame();
		}
	}

	public void Unload()
	{
		Box.Get().SetToIgnoreFullScreenEffects(ignoreEffects: false);
		if (m_confirmButton != null)
		{
			UnityEngine.Object.Destroy(m_confirmButton.gameObject);
		}
		if (m_heroPower != null)
		{
			m_heroPower.Destroy();
		}
		if (m_chosenHero != null)
		{
			m_chosenHero.Destroy();
		}
		foreach (Actor subclassHeroClone in m_subclassHeroClones)
		{
			if (subclassHeroClone != null)
			{
				subclassHeroClone.Destroy();
			}
		}
		m_subclassHeroClones.Clear();
		Actor[] subclassHeroPowerActors = m_subclassHeroPowerActors;
		foreach (Actor actor in subclassHeroPowerActors)
		{
			if (actor != null)
			{
				actor.Destroy();
			}
		}
		for (int j = 0; j < m_mythicPowerCardActors.Length; j++)
		{
			if (m_mythicPowerCardActors[j] != null)
			{
				m_mythicPowerCardActors[j].Destroy();
			}
		}
		m_draftManager.UnregisterDisplayHandlers();
	}

	public void AcceptNewChoices(List<Network.CardChoice> choices)
	{
		DestroyOldChoices();
		StartCoroutine(WaitForAnimsToFinishAndThenDisplayNewChoices(choices));
	}

	public void OnChoiceSelected(int chosenIndex, List<NetCache.CardDefinition> packageCardDefs = null)
	{
		DraftChoice draftChoice = m_choices[chosenIndex - 1];
		Actor actor = draftChoice.m_actor;
		if (actor.GetEntityDef().IsHeroSkin() || actor.GetEntityDef().IsHeroPower())
		{
			return;
		}
		if (!DraftManager.Get().IsRedrafting())
		{
			AddCardToManaCurve(actor.GetEntityDef());
			if (packageCardDefs != null)
			{
				foreach (NetCache.CardDefinition packageCardDef in packageCardDefs)
				{
					EntityDef entityDef = DefLoader.Get().GetEntityDef(GameUtils.TranslateCardIdToDbId(packageCardDef.Name));
					AddCardToManaCurve(entityDef);
				}
			}
			for (int i = 0; i < m_draftDeckTrays.Count; i++)
			{
				DraftPhoneDeckTray draftPhoneDeckTray = m_draftDeckTrays[i];
				if (i == 0)
				{
					draftPhoneDeckTray.GetCardsContent().UpdateCardList(draftChoice.m_cardID, updateHighlight: true, actor);
				}
				else
				{
					draftPhoneDeckTray.GetCardsContent().UpdateCardList(draftChoice.m_cardID);
				}
			}
			return;
		}
		for (int j = 0; j < m_draftDeckTrays.Count; j++)
		{
			ArenaDeckTrayCardListContent arenaDeckTrayCardListContent = m_draftDeckTrays[j].GetCardsContent() as ArenaDeckTrayCardListContent;
			if (arenaDeckTrayCardListContent != null)
			{
				if (j == 0)
				{
					arenaDeckTrayCardListContent.UpdateRedraftCardList(draftChoice.m_cardID, updateHighlight: true, actor);
				}
				else
				{
					arenaDeckTrayCardListContent.UpdateRedraftCardList(draftChoice.m_cardID);
				}
			}
		}
	}

	private IEnumerator WaitForAnimsToFinishAndThenDisplayNewChoices(List<Network.CardChoice> choices)
	{
		while (!m_animationsComplete)
		{
			yield return null;
		}
		while (IsHeroAnimating())
		{
			yield return null;
		}
		if (!UniversalInputManager.UsePhoneUI)
		{
			foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
			{
				while (draftDeckTray == null || !draftDeckTray.isActiveAndEnabled)
				{
					yield return null;
				}
			}
		}
		DestroyOldChoices();
		m_choices.Clear();
		DefLoader defLoader = DefLoader.Get();
		for (int i = 0; i < choices.Count; i++)
		{
			NetCache.CardDefinition cardDef = choices[i].CardDef;
			DraftChoice draftChoice = new DraftChoice
			{
				m_cardID = cardDef.Name,
				m_premium = cardDef.Premium,
				m_actor = null,
				m_packageCardIds = new List<string>(),
				m_packageCardPremiums = new List<TAG_PREMIUM>()
			};
			if (choices[i].PackageCardDefs != null)
			{
				foreach (NetCache.CardDefinition packageCardDef in choices[i].PackageCardDefs)
				{
					draftChoice.m_packageCardIds.Add(packageCardDef.Name);
					TAG_PREMIUM premium = packageCardDef.Premium;
					EntityDef entityDef = defLoader?.GetEntityDef(packageCardDef.Name);
					if (entityDef != null && entityDef.HasTag(GAME_TAG.IS_FABLED_BUNDLE_CARD))
					{
						premium = cardDef.Premium;
					}
					draftChoice.m_packageCardPremiums.Add(premium);
				}
			}
			m_choices.Add(draftChoice);
		}
		if (m_draftManager.GetSlotType() != DraftSlotType.DRAFT_SLOT_HERO && !m_draftManager.IsRedrafting())
		{
			while (m_chosenHero == null)
			{
				yield return null;
			}
		}
		m_skipHeroEmotes = false;
		for (int j = 0; j < m_choices.Count; j++)
		{
			DraftChoice draftChoice2 = m_choices[j];
			if (draftChoice2.m_cardID != null)
			{
				ChoiceCallback choiceCallback = new ChoiceCallback();
				choiceCallback.choiceID = j + 1;
				choiceCallback.slot = m_draftManager.GetSlot();
				choiceCallback.premium = draftChoice2.m_premium;
				DefLoader.Get().LoadFullDef(draftChoice2.m_cardID, OnFullDefLoaded, choiceCallback);
			}
		}
	}

	public void UpdateArenaRating(int rating, int undergroundRating)
	{
		if (m_arenaLandingPageManager != null)
		{
			m_arenaLandingPageManager.UpdateArenaRatingValues(rating, undergroundRating);
		}
	}

	public void BroadcastArenaDraftScreenEvent(string eventName)
	{
		if (m_arenaDraftScreenBroadcaster != null && m_arenaDraftScreenBroadcaster.HasState(eventName))
		{
			m_arenaDraftScreenBroadcaster.SetState(eventName);
		}
	}

	public void BroadcastArenaLandingPageManagerEvent(string eventName)
	{
		if (m_arenaLandingPageManagerWidget != null)
		{
			m_arenaLandingPageManagerWidget.TriggerEvent(eventName);
		}
	}

	private void OnRedraftCardSlotReady(object payload)
	{
		RedraftCardSlotParams redraftCardSlotParams = (RedraftCardSlotParams)payload;
		RedraftCardSlot cardSlot = redraftCardSlotParams.m_widget.GetComponentInChildren<RedraftCardSlot>();
		if (cardSlot != null)
		{
			cardSlot.Index = redraftCardSlotParams.m_index;
			m_redraftCardSlots.Add(cardSlot);
			redraftCardSlotParams.m_widget.RegisterEventListener(delegate(string s)
			{
				HandleRedraftSlotEvent(cardSlot, s);
			});
			cardSlot.UpdateCardSlotState();
		}
	}

	private void HandleDraftDisplayEvent(string eventName)
	{
		if (!(eventName == "REWARDS_DONE"))
		{
			if (!(eventName == "PACKAGE_CARDS_DISMISSED"))
			{
				return;
			}
			{
				foreach (DraftCardVisual cardVisual in GetCardVisuals())
				{
					cardVisual.SetChosenFlag(bOn: false);
				}
				return;
			}
		}
		if (m_rewardsWidget != null)
		{
			m_rewardsWidget.Hide();
		}
		DestroyRewardsWidget();
	}

	private IEnumerator OnRewardsFlowCoroutine()
	{
		while (m_rewardsWidget == null)
		{
			yield return null;
		}
		while (m_rewardsVisualController == null)
		{
			yield return null;
		}
		OnRewardsFlow(null);
	}

	private void OnRewardsFlow(object _)
	{
		if (m_rewardsWidget != null)
		{
			m_rewardsWidget.Show();
		}
		if (m_rewardsVisualController != null)
		{
			m_rewardsVisualController.SetState(m_draftManager.IsCurrentRewardCrowdsFavor ? "ENTER_CROWDS_FAVOR" : "ENTER");
		}
	}

	public void HandleArenaDraftScreenEvent(string eventName)
	{
		switch (eventName)
		{
		case "EDITDECK_PRESSED":
			OnEditDeckButtonReleased();
			break;
		case "VIEWDECK_PRESSED":
			OnViewDeckButtonReleased();
			break;
		case "START_VIEWDECK":
			OnStartViewDeck();
			break;
		case "RETIRE_PRESSED":
			RetireButtonPress();
			break;
		case "PAGE_LEFT":
			PageLeft();
			break;
		case "PAGE_RIGHT":
			PageRight();
			break;
		case "REWARDCHEST_CLAIMED":
			if (m_rewardBoxesDisplay != null)
			{
				m_rewardBoxesDisplay.AnimateRewards();
			}
			break;
		case "PHASE_POPUP_CLOSED":
			OnPhaseMessageClosed();
			break;
		case "MOVETO_LANDING":
			OnMovedToLanding();
			break;
		case "CODE_NOT_IN_DRAFT_SELECTION":
			m_areDraftCardsBeingShown = false;
			break;
		case "CODE_IN_DRAFT_SELECTION":
			m_areDraftCardsBeingShown = true;
			break;
		case "REVERT_CHANGES_PRESSED":
			RevertDraftDeck();
			break;
		case "SLIDEIN_MOBILE_DRAFTING_VIEWDECK":
		{
			foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
			{
				draftDeckTray.GetCardsContent().UpdateCardList(string.Empty, m_draftManager.GetDraftDeck());
			}
			break;
		}
		}
	}

	private void HandleRedraftSlotEvent(RedraftCardSlot slot, string eventName)
	{
		if (!(eventName == "ADD_CARD"))
		{
			if (eventName == "DRAG_CARD")
			{
				OnWidgetCardStartDrag(slot);
			}
		}
		else
		{
			AddCardToArenaDeck(slot);
		}
	}

	private void HandlePackageCardsPopupEvent(string eventName)
	{
		if (eventName == "CHOICE_CONFIRMED")
		{
			ConfirmPackageChoice();
		}
	}

	private void ConfirmPackageChoice()
	{
		if (m_currentlyViewedPackagePopupIndex == -1)
		{
			return;
		}
		m_packageCardsPopup.HideTray();
		List<int> list = new List<int>();
		foreach (TAG_PREMIUM packageCardPremium in m_choices[m_currentlyViewedPackagePopupIndex].m_packageCardPremiums)
		{
			list.Add((int)packageCardPremium);
		}
		DraftManager.Get().MakeChoice(m_currentlyViewedPackagePopupIndex + 1, m_choices[m_currentlyViewedPackagePopupIndex].m_premium, list, isConfirmingPackageCards: true);
		m_currentlyViewedPackagePopupIndex = -1;
	}

	public void OnRetire()
	{
		DestroyOldChoices();
	}

	public void OnChoiceSelectedClient()
	{
		HeroTitleText.SetActive(value: false);
		if (!m_hasSeenDraftChoices)
		{
			DraftTitleText.SetActive(value: false);
		}
	}

	public void TriggerRedraft(bool cheating = false)
	{
		if (m_draftManager != null)
		{
			bool isUnderground = m_draftManager.IsUnderground();
			if (cheating)
			{
				BroadcastArenaLandingPageManagerEvent("CODE_SLIDEIN_DRAFT");
			}
			else
			{
				BroadcastArenaLandingPageManagerEvent("START_DRAFTINGPAGE");
			}
			BroadcastArenaDraftScreenEvent("CODE_START_DRAFT");
			if (cheating)
			{
				m_draftManager.RequestRedraftBegin(isUnderground);
				m_draftManager.NewRedraftStarted();
			}
			else
			{
				m_draftManager.SetClientState(m_draftManager.IsUnderground() ? ArenaClientStateType.Underground_Redraft : ArenaClientStateType.Normal_Redraft);
				Network.Get().RequestDraftChoicesAndContents(isUnderground);
			}
			m_draftManager.OnRedraftBegin();
		}
	}

	public void SetDraftMode(DraftMode mode)
	{
		bool num = m_currentMode != mode;
		m_currentMode = mode;
		if (num)
		{
			Log.Arena.Print("SetDraftMode - " + m_currentMode);
			StartCoroutine(InitializeDraftScreen());
		}
	}

	public IEnumerator SetDraftModeCoroutine(DraftMode mode)
	{
		while (m_arenaDraftScreen == null || m_arenaDraftScreen.IsMidrunContainerActive())
		{
			yield return null;
		}
		SetDraftMode(mode);
	}

	public DraftMode GetDraftMode()
	{
		return m_currentMode;
	}

	public void CancelFindGame()
	{
		GameMgr.Get().CancelFindGame();
		HandleGameStartupFailure();
	}

	public bool IsHeroAnimating()
	{
		return m_heroAnimationState != HeroAnimationState.NOT_ANIMATING;
	}

	public void ZoomHeroCard(Actor hero, bool isDraftingHeroPower, int heroChoice)
	{
		SoundManager.Get().LoadAndPlay("tournament_screen_select_hero.prefab:2b9bdf587ac07084b8f7d5c4bce33ecf");
		m_heroAnimationState = HeroAnimationState.ANIMATING_ZOOM;
		m_zoomedHeroIndex = heroChoice;
		hero.SetUnlit();
		hero.UpdateCustomFrameDiamondMaterial();
		hero.gameObject.transform.rotation = m_bigHeroBone.transform.rotation;
		iTween.MoveTo(hero.gameObject, m_bigHeroBone.position, 0.25f);
		iTween.ScaleTo(hero.gameObject, m_bigHeroBone.localScale, 0.25f);
		SoundManager.Get().LoadAndPlay("forge_hero_portrait_plate_rises.prefab:bffebffeb579074418432f59870e854e");
		FadeEffectsIn();
		LayerUtils.SetLayer(hero.gameObject, GameLayer.IgnoreFullScreenEffects);
		UniversalInputManager.Get().SetGameDialogActive(active: true);
		if (m_confirmButton != null)
		{
			m_confirmButton.gameObject.SetActive(value: true);
			m_confirmButton.m_button.GetComponent<PlayMakerFSM>().SendEvent("Birth");
			m_confirmButton.AddEventListener(UIEventType.RELEASE, OnConfirmButtonClicked);
		}
		if (m_heroClickCatcher != null)
		{
			m_heroClickCatcher.AddEventListener(UIEventType.RELEASE, OnCancelButtonClicked);
			m_heroClickCatcher.gameObject.SetActive(value: true);
		}
		hero.TurnOffCollider();
		hero.SetActorState(ActorStateType.CARD_IDLE);
		if (isDraftingHeroPower)
		{
			Actor[] subclassHeroPowerActors = m_subclassHeroPowerActors;
			for (int i = 0; i < subclassHeroPowerActors.Length; i++)
			{
				subclassHeroPowerActors[i].Hide();
			}
		}
		if (isDraftingHeroPower || !m_draftManager.HasSlotType(DraftSlotType.DRAFT_SLOT_HERO_POWER))
		{
			StartCoroutine(ShowHeroPowerWhenDefIsLoaded(isDraftingHeroPower));
		}
	}

	public void OnHeroClicked(int heroChoice)
	{
		Actor actor = null;
		bool flag = false;
		if (m_draftManager.GetSlotType() == DraftSlotType.DRAFT_SLOT_HERO)
		{
			actor = m_choices[heroChoice - 1].m_actor;
		}
		else if (m_draftManager.GetSlotType() == DraftSlotType.DRAFT_SLOT_HERO_POWER)
		{
			flag = true;
			actor = m_subclassHeroClones[heroChoice - 1];
			Actor heroPower = m_heroPowerCardActors[heroChoice - 1];
			m_heroPower = heroPower;
			m_heroPower.Hide();
		}
		if (actor != null)
		{
			m_zoomedHero = actor.GetCollider().gameObject.GetComponent<DraftCardVisual>();
			ZoomHeroCard(actor, flag, heroChoice);
		}
		else
		{
			Log.Arena.PrintWarning("DraftDisplay.OnHeroClicked: ChosenHeroActor is null! HeroChoice={0}", heroChoice);
		}
		bool flag2 = true;
		if (!flag)
		{
			flag2 = IsHeroEmoteSpellReady(heroChoice - 1);
			StartCoroutine(WaitForSpellToLoadAndPlay(heroChoice - 1));
		}
		if (CanAutoDraft() && flag2)
		{
			OnConfirmButtonClicked(null);
		}
	}

	private void MakeHeroPowerGoldenIfPremium(DefLoader.DisposableFullDef heroPowerDef)
	{
		EntityDef entityDef = heroPowerDef.EntityDef;
		TAG_PREMIUM heroPremium = CollectionManager.Get().GetHeroPremium(entityDef.GetClass());
		int num = m_zoomedHero.GetChoiceNum() - 1;
		if (m_mythicHeroTypes[num] != CornerReplacementSpellType.NONE)
		{
			m_heroPower = m_mythicPowerCardActors[num];
		}
		else
		{
			m_heroPower = ((heroPremium == TAG_PREMIUM.GOLDEN) ? m_goldenHeroPowerSkin : m_defaultHeroPowerSkin);
		}
		m_heroPower.SetCardDef(heroPowerDef.DisposableCardDef);
		m_heroPower.SetEntityDef(entityDef);
		m_heroPower.SetPremium(heroPremium);
		m_heroPower.UpdateAllComponents();
	}

	private bool IsHeroEmoteSpellReady(int index)
	{
		if (!(m_heroEmotes[index] != null))
		{
			return m_skipHeroEmotes;
		}
		return true;
	}

	private IEnumerator WaitForSpellToLoadAndPlay(int index)
	{
		bool wasEmoteAlreadyReady = IsHeroEmoteSpellReady(index);
		while (!IsHeroEmoteSpellReady(index))
		{
			yield return null;
		}
		if (!m_skipHeroEmotes)
		{
			m_heroEmotes[index].Reactivate();
		}
		if (CanAutoDraft() && !wasEmoteAlreadyReady)
		{
			OnConfirmButtonClicked(null);
		}
	}

	public void ClickConfirmButton()
	{
		OnConfirmButtonClicked(null);
	}

	private void OnConfirmButtonClicked(UIEvent e)
	{
		if (GameUtils.IsAnyTransitionActive())
		{
			return;
		}
		m_choices.ForEach(delegate(DraftChoice choice)
		{
			if (choice.m_actor != null)
			{
				choice.m_actor.TurnOffCollider();
			}
		});
		if (m_draftManager.GetSlotType() == DraftSlotType.DRAFT_SLOT_HERO_POWER)
		{
			m_subclassHeroClones.ForEach(delegate(Actor choice)
			{
				choice.TurnOffCollider();
			});
		}
		DoHeroSelectAnimation();
	}

	private void OnCancelButtonClicked(UIEvent e)
	{
		if (IsInHeroSelectMode())
		{
			DoHeroCancelAnimation();
		}
		else
		{
			Navigation.GoBack();
		}
	}

	private void RemoveListeners()
	{
		if (m_confirmButton != null)
		{
			m_confirmButton.RemoveEventListener(UIEventType.RELEASE, OnConfirmButtonClicked);
			m_confirmButton.m_button.GetComponent<PlayMakerFSM>().SendEvent("Death");
			m_confirmButton.gameObject.SetActive(value: false);
		}
		if (m_heroClickCatcher != null)
		{
			m_heroClickCatcher.RemoveEventListener(UIEventType.RELEASE, OnCancelButtonClicked);
			m_heroClickCatcher.gameObject.SetActive(value: false);
		}
	}

	private void FadeEffectsIn()
	{
		if (!m_fxActive)
		{
			m_fxActive = true;
			ScreenEffectParameters blurVignetteDesaturatePerspective = ScreenEffectParameters.BlurVignetteDesaturatePerspective;
			blurVignetteDesaturatePerspective.Time = 0.4f;
			blurVignetteDesaturatePerspective.Blur = new BlurParameters(1f, 1f);
			blurVignetteDesaturatePerspective.Desaturate = new DesaturateParameters(0f);
			m_screenEffectsHandle.StartEffect(blurVignetteDesaturatePerspective);
		}
	}

	private void FadeEffectsOut()
	{
		if (m_fxActive)
		{
			m_fxActive = false;
			m_screenEffectsHandle.StopEffect(0f, OnFadeFinished);
		}
	}

	private void OnFadeFinished()
	{
		if (!(m_chosenHero == null))
		{
			LayerUtils.SetLayer(m_chosenHero.gameObject, GameLayer.Default);
		}
	}

	public void DoHeroCancelAnimation()
	{
		RemoveListeners();
		m_heroPower.Hide();
		if (m_zoomedHero == null || m_heroAnimationState == HeroAnimationState.ANIMATING_SELECT)
		{
			return;
		}
		m_heroAnimationState = HeroAnimationState.ANIMATING_CANCEL;
		Actor actor;
		if (m_draftManager.GetSlotType() == DraftSlotType.DRAFT_SLOT_HERO)
		{
			actor = m_choices[m_zoomedHero.GetChoiceNum() - 1].m_actor;
		}
		else
		{
			actor = m_subclassHeroClones[m_zoomedHero.GetChoiceNum() - 1];
			Actor[] subclassHeroPowerActors = m_subclassHeroPowerActors;
			foreach (Actor obj in subclassHeroPowerActors)
			{
				obj.Show();
				Spell componentInChildren = obj.GetComponentInChildren<Spell>();
				if (componentInChildren != null)
				{
					componentInChildren.Deactivate();
					componentInChildren.Activate();
				}
			}
		}
		LayerUtils.SetLayer(actor.gameObject, GameLayer.Default);
		actor.TurnOnCollider();
		FadeEffectsOut();
		UniversalInputManager.Get().SetGameDialogActive(active: false);
		if (m_PhoneDraftBackButton != null)
		{
			m_PhoneDraftBackButton.SetEnabled(enabled: false);
		}
		Vector3 position = m_heroBones[m_zoomedHeroIndex - 1].position;
		iTween.MoveTo(actor.gameObject, position, 0.25f);
		iTween.ScaleTo(actor.gameObject, iTween.Hash("scale", m_heroBones[m_zoomedHeroIndex - 1].localScale, "time", 0.25f, "oncomplete", "PhoneHeroAnimationFinished", "oncompletetarget", base.gameObject));
		m_zoomedHero = null;
	}

	public bool IsInHeroSelectMode()
	{
		return m_zoomedHero != null;
	}

	private void DoHeroSelectAnimation()
	{
		bool flag = m_draftManager.GetSlotType() == DraftSlotType.DRAFT_SLOT_HERO_POWER;
		RemoveListeners();
		m_heroPower.transform.parent = null;
		if (!flag)
		{
			m_heroPower.Hide();
		}
		FadeEffectsOut();
		UniversalInputManager.Get().SetGameDialogActive(active: false);
		m_chosenHero = (flag ? m_zoomedHero.GetSubActor() : m_zoomedHero.GetActor());
		m_zoomedHero.SetChosenFlag(bOn: true);
		m_draftManager.MakeChoice(m_zoomedHero.GetChoiceNum(), m_chosenHero.GetPremium());
		if (m_heroAnimationState == HeroAnimationState.ANIMATING_CANCEL)
		{
			return;
		}
		m_heroAnimationState = HeroAnimationState.ANIMATING_SELECT;
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			if (m_PhoneDraftBackButton != null)
			{
				m_PhoneDraftBackButton.SetEnabled(enabled: false);
			}
			Actor actor = null;
			if (!flag)
			{
				actor = m_zoomedHero.GetActor();
			}
			else
			{
				actor = m_zoomedHero.GetSubActor();
				m_inPlayHeroPowerActor = m_subclassHeroPowerActors[m_zoomedHero.GetChoiceNum() - 1];
				Actor actor2 = m_zoomedHero.GetActor();
				actor2.transform.parent = m_socketHeroPowerBone;
				iTween.MoveTo(actor2.gameObject, iTween.Hash("position", Vector3.zero, "time", 0.25f, "islocal", true, "easetype", iTween.EaseType.easeInCubic, "oncomplete", "PhoneHeroPowerAnimationFinished", "oncompletetarget", base.gameObject));
				iTween.ScaleTo(actor2.gameObject, iTween.Hash("scale", Vector3.one, "time", 0.25f, "easetype", iTween.EaseType.easeInCubic));
			}
			actor.transform.parent = m_socketHeroBone;
			iTween.MoveTo(actor.gameObject, iTween.Hash("position", Vector3.zero, "time", 0.25f, "islocal", true, "easetype", iTween.EaseType.easeInCubic, "oncomplete", "PhoneHeroAnimationFinished", "oncompletetarget", base.gameObject));
			iTween.ScaleTo(actor.gameObject, iTween.Hash("scale", Vector3.one, "time", 0.25f, "easetype", iTween.EaseType.easeInCubic));
		}
		else
		{
			m_zoomedHero.GetActor().ActivateSpellBirthState(SpellType.CONSTRUCT);
			if (flag)
			{
				m_zoomedHero.gameObject.SetActive(value: false);
			}
			m_zoomedHero = null;
			m_heroAnimationState = HeroAnimationState.NOT_ANIMATING;
		}
		SoundManager.Get().LoadAndPlay("forge_hero_portrait_plate_descend_and_impact.prefab:371e56744a872fc45a4bb3c043e684aa");
	}

	private void PhoneHeroAnimationFinished()
	{
		Log.Arena.Print("Phone Hero animation complete");
		m_zoomedHero = null;
		m_heroAnimationState = HeroAnimationState.NOT_ANIMATING;
		if (m_PhoneDraftBackButton != null)
		{
			m_PhoneDraftBackButton.SetEnabled(enabled: true);
		}
	}

	private void PhoneHeroPowerAnimationFinished()
	{
		Log.Arena.Print("Phone Hero Power animation complete");
		m_inPlayHeroPowerActor.transform.parent = m_socketHeroPowerBone;
		m_inPlayHeroPowerActor.transform.localPosition = Vector3.zero;
		m_inPlayHeroPowerActor.transform.localScale = Vector3.one;
		m_inPlayHeroPowerActor.Show();
		if (m_PhoneDraftBackButton != null)
		{
			m_PhoneDraftBackButton.SetEnabled(enabled: true);
		}
	}

	public void RevertManaCurve(CollectionDeck baseDraftDeck)
	{
		if (m_manaCurves.Count == 0)
		{
			Debug.LogWarning($"DraftDisplay.RevertManaCurve() - m_manaCurve is null");
			return;
		}
		foreach (DraftManaCurve manaCurf in m_manaCurves)
		{
			manaCurf.RevertManaCurve(baseDraftDeck);
		}
	}

	public void AddCardToManaCurve(EntityDef entityDef, bool shouldAnimate = true)
	{
		if (m_manaCurves.Count == 0)
		{
			Debug.LogWarning($"DraftDisplay.AddCardToManaCurve({entityDef}) - m_manaCurve is null");
			return;
		}
		foreach (DraftManaCurve manaCurf in m_manaCurves)
		{
			manaCurf.AddCardToManaCurve(entityDef, shouldAnimate);
		}
	}

	public void RemoveCardFromManaCurve(EntityDef entityDef, bool shouldAnimate = true)
	{
		if (m_manaCurves.Count == 0)
		{
			Debug.LogWarning($"DraftDisplay.AddCardToManaCurve({entityDef}) - m_manaCurve is null");
			return;
		}
		foreach (DraftManaCurve manaCurf in m_manaCurves)
		{
			manaCurf.RemoveCardFromManaCurve(entityDef, shouldAnimate);
		}
	}

	public void AddCardToManaCurve(string cardId, bool shouldAnimate = true)
	{
		EntityDef entityDef = DefLoader.Get().GetEntityDef(cardId);
		AddCardToManaCurve(entityDef, shouldAnimate);
	}

	public void RemoveCardFromManaCurve(string cardId, bool shouldAnimate = true)
	{
		EntityDef entityDef = DefLoader.Get().GetEntityDef(cardId);
		RemoveCardFromManaCurve(entityDef, shouldAnimate);
	}

	public void UpdateArenaDraftScreenDataModel()
	{
		if (m_draftManager == null)
		{
			return;
		}
		if (m_arenaDraftScreenDataModel == null)
		{
			m_arenaDraftScreenDataModel = new ArenaDraftScreenDataModel();
			if (m_thisWidget != null)
			{
				m_thisWidget.BindDataModel(m_arenaDraftScreenDataModel);
			}
		}
		int redraftSlot = m_draftManager.GetRedraftSlot();
		int num = m_draftManager.GetMaxRedraftSlot() + 1;
		m_arenaDraftScreenDataModel.currentRedraftCount = redraftSlot;
		m_arenaDraftScreenDataModel.maxRedraftCount = num;
		m_arenaDraftScreenDataModel.redraftCountLeft = num - redraftSlot;
	}

	public List<DraftCardVisual> GetCardVisuals()
	{
		List<DraftCardVisual> list = new List<DraftCardVisual>();
		foreach (DraftChoice choice in m_choices)
		{
			if (choice.m_actor == null)
			{
				return null;
			}
			DraftCardVisual component = choice.m_actor.GetCollider().gameObject.GetComponent<DraftCardVisual>();
			if (component != null)
			{
				list.Add(component);
				continue;
			}
			if (choice.m_subActor == null)
			{
				return null;
			}
			component = choice.m_subActor.GetCollider().gameObject.GetComponent<DraftCardVisual>();
			if (component != null)
			{
				list.Add(component);
				continue;
			}
			return null;
		}
		return list;
	}

	public void HandleGameStartupFailure()
	{
		if (m_playButton != null)
		{
			m_playButton.Enable();
		}
		SetRetireButtonEnabled(enabled: true);
		SetViewDeckButtonEnabled(enabled: true);
		SetBackButtonEnabled(enabled: true);
		if (PresenceMgr.Get().CurrentStatus == Global.PresenceStatus.ARENA_QUEUE)
		{
			PresenceMgr.Get().SetPrevStatus();
		}
	}

	private void SetRetireButtonEnabled(bool enabled)
	{
		if (!(m_retireButtonWidget != null))
		{
			return;
		}
		m_retireButtonWidget.RegisterReadyListener(delegate
		{
			Clickable componentInChildren = m_retireButtonWidget.GetComponentInChildren<Clickable>();
			if (componentInChildren != null)
			{
				componentInChildren.enabled = enabled;
			}
		});
	}

	private void SetViewDeckButtonEnabled(bool enabled)
	{
		if (!(m_viewDeckButtonWidget != null))
		{
			return;
		}
		m_viewDeckButtonWidget.RegisterReadyListener(delegate
		{
			Clickable componentInChildren = m_viewDeckButtonWidget.GetComponentInChildren<Clickable>();
			if (componentInChildren != null)
			{
				componentInChildren.enabled = enabled;
			}
		});
	}

	private void SetBackButtonEnabled(bool enabled)
	{
		if (m_midrunPhoneBackButton != null)
		{
			BoxCollider component = m_midrunPhoneBackButton.GetComponent<BoxCollider>();
			if (component != null)
			{
				component.enabled = enabled;
			}
		}
	}

	public void OnDraftingComplete(bool isRedraft)
	{
		DoDeckCompleteAnims();
		if (isRedraft)
		{
			m_draftManager.SetArenaState(ArenaSessionState.EDITING_DECK, m_draftManager.IsUnderground());
			GoToArenaEditDeck();
		}
		else
		{
			m_draftManager.SetArenaState(ArenaSessionState.MIDRUN, m_draftManager.IsUnderground());
			GoToArenaMidrun();
		}
	}

	public void GoToArenaEditDeck()
	{
		BroadcastArenaDraftScreenEvent("CODE_SLIDEIN_REDRAFT");
		if (m_draftManager != null)
		{
			StartCoroutine(SetDraftModeCoroutine(DraftMode.ACTIVE_DRAFT_DECK));
		}
		ChosenHero.Hide();
		SetupEditDeckTray();
		if (m_playButton != null)
		{
			m_playButton.SetText(GameStrings.Get("GLUE_EDIT_DECK"));
		}
	}

	private void SetupEditDeckTray()
	{
		int num = (UniversalInputManager.UsePhoneUI ? 1 : 0);
		for (int i = 0; i < m_draftDeckTrays.Count(); i++)
		{
			DraftPhoneDeckTray draftPhoneDeckTray = m_draftDeckTrays[i];
			if (draftPhoneDeckTray != null)
			{
				DeckTrayCardListContent cardsContent = draftPhoneDeckTray.GetCardsContent();
				if (i == num)
				{
					cardsContent.UnregisterCardTileReleaseListener(OnCardTileRelease);
					cardsContent.RegisterCardTileReleaseListener(OnCardTileRelease);
					cardsContent.UnregisterCardTileHeldListener(OnCardTileStartDrag);
					cardsContent.RegisterCardTileHeldListener(OnCardTileStartDrag);
				}
				cardsContent.UnregisterCardCountUpdated(OnCardCountUpdated);
				cardsContent.RegisterCardCountUpdated(OnCardCountUpdated);
			}
		}
		m_collectionCardIds.Clear();
		m_draftManager.PopuplateBaseDecks();
		m_draftManager.GetDraftDeck().AddCardsFrom(m_draftManager.GetRedraftDeck(), useNewTag: true);
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			ArenaDeckTrayCardListContent arenaDeckTrayCardListContent = draftDeckTray.GetCardsContent() as ArenaDeckTrayCardListContent;
			if (arenaDeckTrayCardListContent != null)
			{
				arenaDeckTrayCardListContent.UpdateRedraftCardList(string.Empty, new CollectionDeck());
				arenaDeckTrayCardListContent.UpdateCardList(string.Empty, m_draftManager.GetDraftDeck());
			}
		}
		InitManaCurve();
	}

	public void GoToArenaCollection()
	{
		DraftManager draftManager = DraftManager.Get();
		if (DraftManager.Get() != null)
		{
			CollectionDeck draftDeck = draftManager.GetDraftDeck();
			if (draftDeck != null)
			{
				ClearNewFlagsOnDeckTray(draftDeck);
				PopulateArenaCollection(draftDeck);
			}
		}
	}

	private void ClearNewFlagsOnDeckTray(CollectionDeck draftDeck)
	{
		draftDeck.ClearNewState();
		if (m_draftDeckTrays == null)
		{
			return;
		}
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			draftDeckTray.GetCardsContent().UpdateCardList();
		}
	}

	public void GoToArenaMidrun()
	{
		int num = (UniversalInputManager.UsePhoneUI ? 1 : 0);
		for (int i = 0; i < m_draftDeckTrays.Count(); i++)
		{
			DraftPhoneDeckTray draftPhoneDeckTray = m_draftDeckTrays[i];
			if (draftPhoneDeckTray != null)
			{
				DeckTrayCardListContent cardsContent = draftPhoneDeckTray.GetCardsContent();
				if (i == num)
				{
					cardsContent.UnregisterCardTileReleaseListener(OnCardTileRelease);
					cardsContent.RegisterCardTileReleaseListener(OnCardTileRelease);
					cardsContent.UnregisterCardTileHeldListener(OnCardTileStartDrag);
					cardsContent.RegisterCardTileHeldListener(OnCardTileStartDrag);
				}
				cardsContent.UnregisterCardCountUpdated(OnCardCountUpdated);
				cardsContent.RegisterCardCountUpdated(OnCardCountUpdated);
			}
		}
		BroadcastArenaDraftScreenEvent("CODE_SLIDEIN_MIDRUN");
		if (ChosenHero != null)
		{
			ChosenHero.Hide();
		}
		if (m_collectionPageA != null)
		{
			m_collectionPageA.transform.localPosition = Vector3.right * 500f;
		}
		if (m_collectionPageB != null)
		{
			m_collectionPageB.transform.localPosition = Vector3.right * 500f;
		}
		DeterminePlayButtonText();
	}

	public void DeterminePlayButtonText()
	{
		if (!(m_playButton == null))
		{
			bool isUnderground = m_draftManager.IsUnderground();
			string key = "GLOBAL_PLAY";
			switch (m_draftManager.GetArenaState(isUnderground))
			{
			case ArenaSessionState.REDRAFTING:
			case ArenaSessionState.MIDRUN_REDRAFT_PENDING:
				key = "GLUE_ARENA_DRAFT_REDRAFT_HEADER";
				break;
			case ArenaSessionState.EDITING_DECK:
				key = "GLUE_EDIT_DECK";
				break;
			}
			m_playButton.SetText(GameStrings.Get(key));
		}
	}

	public void PopulateArenaCollection(CollectionDeck draftDeck)
	{
		m_collectionCardIds.Clear();
		m_collectionCardDataModelList.Cards.Clear();
		if (draftDeck != null && draftDeck.GetCards() != null)
		{
			m_collectionCardIds.AddRange(draftDeck.GetCards());
		}
		foreach (CardWithPremiumStatus item in draftDeck.GetCardsWithPremiumStatus())
		{
			CardDataModel cardDataModel = CreateCardDataModelFromCardId((int)item.cardId, item.premium);
			if (GameUtils.IsBannedByArenaDenylist(cardDataModel.CardId))
			{
				cardDataModel.Status = CardStatus.BANNED;
			}
			m_collectionCardDataModelList.Cards.Add(cardDataModel);
		}
		m_currentCollectionPageIndex = 0;
		SetupArenaCollectionPage(m_currentCollectionPageIndex, m_collectionCardDataModelList);
	}

	public void PopulateArenaRedraftCollection()
	{
		if (m_draftManager == null)
		{
			return;
		}
		CollectionDeck redraftDeck = m_draftManager.GetRedraftDeck();
		m_collectionCardIds.Clear();
		m_collectionCardDataModelList.Cards.Clear();
		if (redraftDeck != null && redraftDeck.GetCards() != null)
		{
			m_collectionCardIds.AddRange(redraftDeck.GetCards());
		}
		foreach (CardWithPremiumStatus item in redraftDeck.GetCardsWithPremiumStatus())
		{
			CardDataModel cardDataModel = CreateCardDataModelFromCardId((int)item.cardId, item.premium);
			if (GameUtils.IsBannedByArenaDenylist(cardDataModel.CardId))
			{
				cardDataModel.Status = CardStatus.BANNED;
			}
			m_collectionCardDataModelList.Cards.Add(cardDataModel);
		}
		SetupRedraftOnLossCollectionScreen(m_collectionCardDataModelList);
	}

	public void UpdateArenaRedraftScreen(string card, TAG_PREMIUM premium, bool addedToDeck, int slotIndex)
	{
		int num = GameUtils.TranslateCardIdToDbId(card);
		if (num == 0 || !SlotIndexValid(slotIndex))
		{
			return;
		}
		if (addedToDeck)
		{
			RedraftCardSlot redraftCardSlot = m_redraftCardSlots[slotIndex];
			redraftCardSlot.HasCard = false;
			redraftCardSlot.IsNew = false;
			m_redraftCardSlotWidgets[slotIndex].UnbindDataModel(27);
			m_collectionCardIds.Remove(num);
			redraftCardSlot.UpdateCardSlotState();
		}
		else
		{
			RedraftCardSlot redraftCardSlot2 = m_redraftCardSlots[slotIndex];
			redraftCardSlot2.HasCard = true;
			redraftCardSlot2.IsNew = m_draftManager.IsRedraftCard(card, premium);
			CardDataModel dataModel = CreateCardDataModelFromCardId(num, premium);
			m_redraftCardSlotWidgets[slotIndex].BindDataModel(dataModel);
			m_collectionCardIds.Add(num);
			redraftCardSlot2.UpdateCardSlotState();
		}
		CollectionDeck currentDeck = (m_draftManager.IsRedrafting() ? m_draftManager.GetRedraftDeck() : new CollectionDeck());
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			ArenaDeckTrayCardListContent arenaDeckTrayCardListContent = draftDeckTray.GetCardsContent() as ArenaDeckTrayCardListContent;
			if (arenaDeckTrayCardListContent != null)
			{
				arenaDeckTrayCardListContent.UpdateRedraftCardList(card, currentDeck);
			}
		}
	}

	public void DisplayPackageCardsConfirmation(int choiceIndex)
	{
		if (choiceIndex < 0 || choiceIndex >= m_choices.Count || !(m_arenaDraftScreenBroadcaster != null))
		{
			return;
		}
		DraftChoice draftChoice = m_choices[choiceIndex];
		DataModelList<CardTileDataModel> dataModelList = new DataModelList<CardTileDataModel>();
		if (m_packageCardsPopup != null)
		{
			bool chooseButtonActive = ArePackageCardsValid(draftChoice.m_packageCardIds);
			m_packageCardsPopup.ClearAllCards();
			m_packageCardsPopup.ShowTray();
			m_packageCardsPopup.SetChooseButtonActive(chooseButtonActive);
		}
		for (int i = 0; i < draftChoice.m_packageCardIds.Count; i++)
		{
			CardTileDataModel cardTileDataModel = new CardTileDataModel
			{
				CardId = draftChoice.m_packageCardIds[i],
				Count = 1,
				Premium = draftChoice.m_packageCardPremiums[i],
				ForceGhostDisplayStyle = (GameUtils.IsBannedByArenaDenylist(draftChoice.m_packageCardIds[i]) ? CollectionDeckTileActor.GhostedState.RED : CollectionDeckTileActor.GhostedState.NONE)
			};
			dataModelList.Add(cardTileDataModel);
			if (m_packageCardsPopup != null)
			{
				m_packageCardsPopup.AddCard(cardTileDataModel.CardId, cardTileDataModel.Premium, offsetCardNameForRunes: false, cardTileDataModel.ForceGhostDisplayStyle);
			}
		}
		CardDataModel selectedCard = new CardDataModel
		{
			CardId = draftChoice.m_cardID,
			Premium = draftChoice.m_premium
		};
		RelatedCardsDetailsDataModel payload = new RelatedCardsDetailsDataModel
		{
			CardTiles = dataModelList,
			SelectedCard = selectedCard
		};
		EventDataModel eventDataModel = new EventDataModel();
		eventDataModel.Payload = payload;
		SendEventDownwardStateAction.SendEventDownward(m_arenaDraftScreenBroadcaster.gameObject, "CODE_LEGENDARYBUCKET_POPUP", SendEventDownwardStateAction.BubbleDownEventDepth.AllDescendants, eventDataModel);
		m_currentlyViewedPackagePopupIndex = choiceIndex;
	}

	private CardDataModel CreateCardDataModelFromCardId(int cardId, TAG_PREMIUM premium)
	{
		CardDbfRecord cardRecord = GameDbf.GetIndex().GetCardRecord(GameUtils.TranslateDbIdToCardId(cardId));
		EntityDef entityDef = DefLoader.Get().GetEntityDef(cardRecord.ID);
		string cardText = "";
		if (entityDef != null)
		{
			cardText = entityDef.GetCardTextInHand();
		}
		return new CardDataModel
		{
			CardId = cardRecord.NoteMiniGuid,
			Name = cardRecord.Name.GetString(),
			CardText = cardText,
			Premium = premium
		};
	}

	private void SetupRedraftOnLossCollectionScreen(CardListDataModel cardList)
	{
		if (m_redraftCardSlotWidgets == null || m_redraftCardSlotWidgets.Count == 0)
		{
			Log.Arena.PrintWarning("SetupRedraftOnLossCollectionScreen - m_redraftCardSlotWidgets is null or empty");
			return;
		}
		if (cardList == null || cardList.Cards.Count == 0)
		{
			Log.Arena.PrintWarning("SetupRedraftOnLossCollectionScreen - cardList is null or empty");
			return;
		}
		for (int i = 0; i < m_redraftCardSlotWidgets.Count; i++)
		{
			WidgetInstance widgetInstance = m_redraftCardSlotWidgets[i];
			widgetInstance.UnbindDataModel(27);
			m_redraftCardSlots[i].IsNew = true;
			m_redraftCardSlots[i].HasCard = true;
			m_redraftCardSlots[i].UpdateCardSlotState();
			if (i < cardList.Cards.Count)
			{
				widgetInstance.BindDataModel(cardList.Cards[i]);
			}
		}
	}

	private void SetupArenaCollectionPage(int pageIndex, CardListDataModel cardList)
	{
		if (m_collectionPageA == null || m_collectionPageB == null)
		{
			Log.Arena.PrintWarning("SetupAreanCollectionPage - m_collectionPageA or m_collectionPageB is null");
			return;
		}
		m_activeCollectionPage = m_collectionPageA;
		m_collectionPageA.transform.localPosition = m_initialCollectionPagePosition;
		m_collectionPageB.transform.localPosition = Vector3.right * 500f;
		RepopulateArenaPage(pageIndex, m_activeCollectionPage, cardList);
	}

	private void UpdateArenaCollectionPage(int pageIndex, CardListDataModel cardList)
	{
		if (!(m_collectionPageA == null) && !(m_collectionPageB == null) && cardList != null)
		{
			if (m_activeCollectionPage == m_collectionPageA)
			{
				m_activeCollectionPage = m_collectionPageB;
			}
			else
			{
				m_activeCollectionPage = m_collectionPageA;
			}
			m_activeCollectionPage.transform.localPosition = Vector3.right * 500f;
			RepopulateArenaPage(pageIndex, m_activeCollectionPage, cardList);
		}
	}

	private void RepopulateArenaPage(int pageIndex, ArenaCollectionPage page, CardListDataModel cardList)
	{
		CardListDataModel cardListDataModel = new CardListDataModel();
		CardListDataModel cardListDataModel2 = new CardListDataModel();
		int num = ((cardList.Cards.Count >= 8) ? (pageIndex * 8) : 0);
		cardListDataModel.Cards.AddRange(cardList.Cards.Skip(num).Take(4).ToList());
		int count = num + 4;
		cardListDataModel2.Cards.AddRange(cardList.Cards.Skip(count).Take(4).ToList());
		page.UpdatePageData(pageIndex + 1, cardListDataModel, cardListDataModel2);
		page.UpdatePageArrows(pageIndex > 0, num + 8 < cardList.Cards.Count);
	}

	private void OnPositionNewPage(object callbackData)
	{
		if (m_activeCollectionPage == m_collectionPageA)
		{
			m_collectionPageA.transform.localPosition = m_initialCollectionPagePosition;
			m_collectionPageB.transform.localPosition = Vector3.right * 500f;
		}
		else
		{
			m_collectionPageA.transform.localPosition = Vector3.right * 500f;
			m_collectionPageB.transform.localPosition = m_initialCollectionPagePosition;
		}
	}

	private void OnPageFlipComplete(object callbackData)
	{
		m_isPageTurning = false;
	}

	public void PageLeft()
	{
		if (!m_isPageTurning && !(m_pageTurn == null) && m_currentCollectionPageIndex - 1 >= 0)
		{
			ArenaCollectionPage arenaCollectionPage = ((m_activeCollectionPage == m_collectionPageA) ? m_collectionPageB : m_collectionPageA);
			m_pageTurn.TurnLeft(m_activeCollectionPage.gameObject, arenaCollectionPage.gameObject, OnPageFlipComplete, OnPositionNewPage, null);
			m_isPageTurning = true;
			SoundManager.Get().LoadAndPlay("collection_manager_book_page_flip_back.prefab:371e496e1cd371144abfec472e72d9a9");
			UpdateArenaCollectionPage(--m_currentCollectionPageIndex, m_collectionCardDataModelList);
		}
	}

	public void PageRight()
	{
		int num = Mathf.CeilToInt((float)m_collectionCardIds.Count / 8f);
		if (!m_isPageTurning && !(m_pageTurn == null) && m_currentCollectionPageIndex + 1 < num)
		{
			ArenaCollectionPage arenaCollectionPage = ((m_activeCollectionPage == m_collectionPageA) ? m_collectionPageB : m_collectionPageA);
			m_pageTurn.TurnRight(m_activeCollectionPage.gameObject, arenaCollectionPage.gameObject, OnPageFlipComplete, OnPositionNewPage, null);
			m_isPageTurning = true;
			SoundManager.Get().LoadAndPlay("collection_manager_book_page_flip_forward.prefab:07282310dd70fee4ca2dfdb37c545acc");
			UpdateArenaCollectionPage(++m_currentCollectionPageIndex, m_collectionCardDataModelList);
		}
	}

	public bool AddCardToArenaDeck(RedraftCardSlot slot)
	{
		if (m_draftManager.GetArenaState() != ArenaSessionState.EDITING_DECK)
		{
			return false;
		}
		if (slot == null || !slot.HasCard)
		{
			return false;
		}
		Widget widget = m_redraftCardSlotWidgets[slot.Index];
		if (widget == null)
		{
			return false;
		}
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			if (draftDeckTray == null)
			{
				return false;
			}
		}
		if (!(widget.GetDataModel<EventDataModel>()?.Payload is CardDataModel cardDataModel))
		{
			return false;
		}
		if (GameUtils.IsBannedByArenaDenylist(cardDataModel.CardId))
		{
			return false;
		}
		return AddCardtoArenaDeck(cardDataModel.CardId, cardDataModel.Premium, slot.Index);
	}

	private bool AddCardtoArenaDeck(string cardId, TAG_PREMIUM premium, int slotIndex)
	{
		if (m_draftManager.GetArenaState() != ArenaSessionState.EDITING_DECK)
		{
			return false;
		}
		if (m_draftManager.AddCardToArenaDeck(cardId, premium))
		{
			if (string.IsNullOrEmpty(m_draggingCardId))
			{
				if (m_dropCardInSlotSound != null)
				{
					SoundManager.Get()?.LoadAndPlay(m_dropCardInSlotSound);
				}
			}
			else if (m_removeCardInSlotSound != null)
			{
				SoundManager.Get()?.LoadAndPlay(m_removeCardInSlotSound);
			}
			AddCardToManaCurve(cardId);
			foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
			{
				if (draftDeckTray == null || string.IsNullOrEmpty(cardId) || !SlotIndexValid(slotIndex))
				{
					return false;
				}
				draftDeckTray.GetCardsContent().UpdateCardList(cardId, m_draftManager.GetDraftDeck());
			}
		}
		UpdateArenaRedraftScreen(cardId, premium, addedToDeck: true, slotIndex);
		return true;
	}

	private bool SwapCardFromDraftAndCollection(string cardIdDeck, TAG_PREMIUM premiumDeck, string cardIdCollection, TAG_PREMIUM premiumCollection, RedraftCardSlot slot)
	{
		if (m_draftManager.GetArenaState() != ArenaSessionState.EDITING_DECK)
		{
			return false;
		}
		int num = GameUtils.TranslateCardIdToDbId(cardIdDeck);
		int num2 = GameUtils.TranslateCardIdToDbId(cardIdCollection);
		if (num == 0 || num2 == 0)
		{
			return false;
		}
		Widget widget = slot.GetWidget();
		if (widget == null)
		{
			return false;
		}
		widget.UnbindDataModel(27);
		m_collectionCardIds.Remove(num2);
		CardDataModel dataModel = CreateCardDataModelFromCardId(num, premiumDeck);
		widget.BindDataModel(dataModel);
		m_collectionCardIds.Add(num);
		slot.HasCard = true;
		slot.IsNew = m_draftManager.IsRedraftCard(cardIdDeck, premiumCollection);
		slot.UpdateCardSlotState();
		m_draftManager.RemoveCardFromArenaDeck(cardIdDeck, premiumDeck);
		RemoveCardFromManaCurve(cardIdDeck);
		m_draftManager.AddCardToArenaDeck(cardIdCollection, premiumCollection);
		AddCardToManaCurve(cardIdCollection);
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			draftDeckTray.GetCardsContent().UpdateCardList(cardIdCollection, m_draftManager.GetDraftDeck());
		}
		SoundManager.Get()?.LoadAndPlay(m_swapCardInSlotSound);
		return true;
	}

	public bool RemoveCardFromArenaDeck(string cardId, TAG_PREMIUM premium)
	{
		if (m_draftManager.GetArenaState() != ArenaSessionState.EDITING_DECK)
		{
			return false;
		}
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			if (draftDeckTray == null)
			{
				return false;
			}
		}
		int num = -1;
		if (!m_isDraggingCardFromCollection && m_hoveredCardSlot != null)
		{
			num = m_hoveredCardSlot.Index;
			if (m_hoveredCardSlot.HasCard)
			{
				CardDataModel cardDataModel = m_hoveredCardSlot.GetWidget()?.GetDataModel<CardDataModel>();
				if (cardDataModel == null)
				{
					return false;
				}
				string cardId2 = cardDataModel.CardId;
				TAG_PREMIUM premium2 = cardDataModel.Premium;
				return SwapCardFromDraftAndCollection(cardId, premium, cardId2, premium2, m_hoveredCardSlot);
			}
		}
		else
		{
			for (int i = 0; i < m_redraftCardSlots.Count; i++)
			{
				if (!m_redraftCardSlots[i].HasCard)
				{
					num = i;
					break;
				}
			}
			if (num == -1)
			{
				return false;
			}
		}
		if (m_draftManager.RemoveCardFromArenaDeck(cardId, premium))
		{
			if (m_dropCardInSlotSound != null)
			{
				SoundManager.Get()?.LoadAndPlay(m_dropCardInSlotSound);
			}
			RemoveCardFromManaCurve(cardId);
			UpdateArenaRedraftScreen(cardId, premium, addedToDeck: false, num);
			foreach (DraftPhoneDeckTray draftDeckTray2 in m_draftDeckTrays)
			{
				draftDeckTray2.GetCardsContent().UpdateCardList(cardId, m_draftManager.GetDraftDeck());
			}
			return true;
		}
		return false;
	}

	private bool SlotIndexValid(int slotIndex)
	{
		if (m_redraftCardSlots == null)
		{
			return false;
		}
		if (slotIndex >= 0)
		{
			return slotIndex < m_redraftCardSlots.Count;
		}
		return false;
	}

	private void OnCardCountUpdated(int cardCount, int maxCount)
	{
		m_deckCount = cardCount;
		m_maxDeckCount = maxCount;
		if (!(m_cardCountText != null))
		{
			return;
		}
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			m_cardCountText.Text = GameStrings.Format("GLUE_DECK_TRAY_COUNT", m_deckCount, m_maxDeckCount);
		}
		if (m_deckCount > m_maxDeckCount)
		{
			m_cardCountText.TextColor = Color.red;
			if (m_cardCountTextPhoneRedraftCollection != null)
			{
				m_cardCountTextPhoneRedraftCollection.TextColor = Color.red;
			}
		}
		else
		{
			m_cardCountText.TextColor = Color.white;
			if (m_cardCountTextPhoneRedraftCollection != null)
			{
				m_cardCountTextPhoneRedraftCollection.TextColor = Color.white;
			}
		}
	}

	private void OnCardTileRelease(DeckTrayDeckTileVisual cardTile)
	{
		RemoveCardFromArenaDeck(cardTile.GetCardID(), cardTile.GetPremium());
	}

	private void OnCardTileStartDrag(DeckTrayDeckTileVisual cardTile)
	{
		if (m_draftManager.GetArenaState() == ArenaSessionState.EDITING_DECK)
		{
			ArenaInputManager arenaInputManager = ArenaInputManager.Get();
			if (arenaInputManager != null && arenaInputManager.GrabCardTile(cardTile, removeCard: false, OnCardEndDrag))
			{
				StartDragState(cardTile.GetCardID(), cardTile.GetPremium(), isFromCollection: false, null);
			}
		}
	}

	private void OnWidgetCardStartDrag(RedraftCardSlot slot)
	{
		if (m_draftManager.GetArenaState() != ArenaSessionState.EDITING_DECK || slot == null)
		{
			return;
		}
		if (!TryGetCardDataFromEvent(slot, out var cardDataModel, out var targetObject))
		{
			Log.Arena.PrintError("Card drag failed because the card context was not found!");
			return;
		}
		ArenaInputManager arenaInputManager = ArenaInputManager.Get();
		if (arenaInputManager != null)
		{
			Hearthstone.UI.Card componentInChildren = targetObject.GetComponentInChildren<Hearthstone.UI.Card>();
			if (arenaInputManager.GrabCard(componentInChildren, CollectionUtils.ViewMode.CARDS, OnCardEndDrag))
			{
				StartDragState(cardDataModel.CardId, cardDataModel.Premium, isFromCollection: true, slot);
			}
		}
	}

	private void OnCardEndDrag()
	{
		if (string.IsNullOrEmpty(m_draggingCardId))
		{
			ResetDragState();
			return;
		}
		bool flag = false;
		int num = (UniversalInputManager.UsePhoneUI ? 1 : 0);
		for (int i = 0; i < m_draftDeckTrays.Count(); i++)
		{
			DraftPhoneDeckTray draftPhoneDeckTray = m_draftDeckTrays[i];
			if (draftPhoneDeckTray != null && i == num)
			{
				flag = draftPhoneDeckTray.MouseIsOver();
			}
		}
		if (m_isDraggingCardFromCollection)
		{
			if (flag)
			{
				if (m_draggingOriginCardSlot == null)
				{
					return;
				}
				AddCardtoArenaDeck(m_draggingCardId, m_draggingCardPremium, m_draggingOriginCardSlot.Index);
			}
		}
		else if (!flag)
		{
			RemoveCardFromArenaDeck(m_draggingCardId, m_draggingCardPremium);
		}
		ResetDragState();
	}

	private void StartDragState(string cardId, TAG_PREMIUM premium, bool isFromCollection, RedraftCardSlot redraftSlot)
	{
		if (m_draftManager.GetArenaState() != ArenaSessionState.EDITING_DECK)
		{
			return;
		}
		m_draggingCardId = cardId;
		m_draggingCardPremium = premium;
		m_isDraggingCardFromCollection = isFromCollection;
		m_draggingOriginCardSlot = redraftSlot;
		if (!IsDraggingCardFromDeck())
		{
			return;
		}
		foreach (RedraftCardSlot redraftCardSlot in m_redraftCardSlots)
		{
			redraftCardSlot.StartDragFromDeck();
		}
	}

	private void ResetDragState()
	{
		if (IsDraggingCardFromDeck())
		{
			foreach (RedraftCardSlot redraftCardSlot in m_redraftCardSlots)
			{
				redraftCardSlot.StopDragFromDeck();
			}
		}
		m_draggingCardId = null;
		m_draggingCardPremium = TAG_PREMIUM.NORMAL;
		m_isDraggingCardFromCollection = false;
		m_draggingOriginCardSlot = null;
		m_hoveredCardSlot = null;
	}

	public bool IsDraggingCardFromDeck()
	{
		if (m_draggingCardId != null)
		{
			return !m_isDraggingCardFromCollection;
		}
		return false;
	}

	private bool TryGetCardDataFromEvent(RedraftCardSlot slot, out CardDataModel cardDataModel, out GameObject targetObject)
	{
		cardDataModel = null;
		targetObject = null;
		if (slot == null)
		{
			return false;
		}
		EventDataModel eventDataModel = slot.GetWidget()?.GetDataModel<EventDataModel>();
		if (eventDataModel == null)
		{
			return false;
		}
		if (!(eventDataModel.Payload is CardDataModel cardDataModel2))
		{
			return false;
		}
		cardDataModel = cardDataModel2;
		targetObject = eventDataModel.TargetObject.GameObject as GameObject;
		return true;
	}

	public void UpdateHoveredCardSlot(RedraftCardSlot slot)
	{
		m_hoveredCardSlot = slot;
	}

	private void DoDeckCompleteAnims()
	{
		SoundManager.Get().LoadAndPlay("forge_commit_deck.prefab:1e3ef554bb2848b48816f336f2f91569");
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			return;
		}
		m_DeckCompleteSpell.Activate();
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			draftDeckTray.GetCardsContent().ShowDeckCompleteEffects();
		}
	}

	public bool DraftAnimationIsComplete()
	{
		return m_animationsComplete;
	}

	private IEnumerator NotifySceneLoadedWhenReady()
	{
		while (m_confirmButton == null)
		{
			yield return null;
		}
		while (m_heroPower == null)
		{
			yield return null;
		}
		while (m_currentMode == DraftMode.INVALID)
		{
			yield return null;
		}
		while (!m_netCacheReady)
		{
			yield return null;
		}
		while (!AchieveManager.Get().IsReady())
		{
			yield return null;
		}
		InitManaCurve();
		int num = (UniversalInputManager.UsePhoneUI ? 1 : 0);
		if (num < m_draftDeckTrays.Count)
		{
			DraftPhoneDeckTray draftPhoneDeckTray = m_draftDeckTrays[num];
			if (draftPhoneDeckTray != null)
			{
				draftPhoneDeckTray.Initialize();
				TooltipZone tooltipZone = draftPhoneDeckTray.GetTooltipZone();
				if (tooltipZone != null)
				{
					PegUIElement component = tooltipZone.gameObject.GetComponent<PegUIElement>();
					component.AddEventListener(UIEventType.ROLLOVER, DeckHeaderOver);
					component.AddEventListener(UIEventType.ROLLOUT, DeckHeaderOut);
				}
			}
		}
		while (m_arenaLandingPageManagerWidget == null)
		{
			yield return null;
		}
		while (m_arenaLandingPageManagerWidget.IsChangingStates)
		{
			yield return null;
		}
		SceneMgr.Get().NotifySceneLoaded();
		ArenaClientStateType currentClientState = m_draftManager.GetCurrentClientState();
		if (currentClientState == ArenaClientStateType.Normal_Landing || currentClientState == ArenaClientStateType.Underground_Landing)
		{
			m_arenaLandingPageManager.DetermineToggleButtonStartingState();
		}
		m_loadingComplete = true;
	}

	private IEnumerator InitializeDraftScreen()
	{
		switch (m_currentMode)
		{
		case DraftMode.NO_ACTIVE_DRAFT:
			break;
		case DraftMode.DRAFTING:
			PresenceMgr.Get().SetStatus(Global.PresenceStatus.ARENA_FORGE);
			ShowCurrentlyDraftingScreen();
			break;
		case DraftMode.ACTIVE_DRAFT_DECK:
			PresenceMgr.Get().SetStatus(Global.PresenceStatus.ARENA_IDLE);
			StartCoroutine(ShowActiveDraftScreen());
			break;
		case DraftMode.IN_REWARDS:
			PresenceMgr.Get().SetStatus(Global.PresenceStatus.ARENA_REWARD);
			ShowDraftRewardsScreen();
			break;
		case DraftMode.REDRAFTING:
			PresenceMgr.Get().SetStatus(Global.PresenceStatus.ARENA_FORGE);
			ShowCurrentlyDraftingScreen();
			break;
		default:
			Debug.LogError($"DraftDisplay.InitializeDraftScreen(): don't know how to handle m_currentMode = {m_currentMode}");
			break;
		}
		yield break;
	}

	private void OnConfirmButtonLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		m_confirmButton = go.GetComponent<NormalButton>();
		m_confirmButton.SetText(GameStrings.Get("GLUE_CHOOSE"));
		m_confirmButton.gameObject.SetActive(value: false);
		LayerUtils.SetLayer(go, GameLayer.IgnoreFullScreenEffects);
	}

	private void OnHeroPowerActorLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogWarning($"DeckPickerTrayDisplay.OnHeroPowerActorLoaded() - FAILED to load actor \"{assetRef}\"");
			return;
		}
		go.transform.SetParent(base.transform, worldPositionStays: true);
		m_inPlayHeroPowerActor = go.GetComponent<Actor>();
		if (m_inPlayHeroPowerActor == null)
		{
			Debug.LogWarning($"DeckPickerTrayDisplay.OnHeroPowerActorLoaded() - ERROR actor \"{assetRef}\" has no Actor component");
			return;
		}
		m_inPlayHeroPowerActor.SetUnlit();
		m_inPlayHeroPowerActor.Hide();
	}

	private void LoadHeroPowerCallback(Actor actor)
	{
		if (actor == null)
		{
			Debug.LogWarning("DeckPickerTrayDisplay.LoadHeroPowerCallback() - ERROR actor null.");
			return;
		}
		actor.transform.SetParent(base.transform, worldPositionStays: true);
		actor.TurnOffCollider();
		LayerUtils.SetLayer(actor.gameObject, GameLayer.IgnoreFullScreenEffects);
		m_heroPower = actor;
		actor.Hide();
	}

	private void LoadHeroPowerCallback(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogWarning($"DeckPickerTrayDisplay.LoadHeroPowerCallback() - FAILED to load actor \"{assetRef}\"");
			return;
		}
		go.transform.SetParent(base.transform, worldPositionStays: true);
		Actor component = go.GetComponent<Actor>();
		if (component == null)
		{
			Debug.LogWarning($"DeckPickerTrayDisplay.LoadHeroPowerCallback() - ERROR actor \"{assetRef}\" has no Actor component");
			return;
		}
		component.TurnOffCollider();
		LayerUtils.SetLayer(component.gameObject, GameLayer.IgnoreFullScreenEffects);
		m_defaultHeroPowerSkin = component;
		m_heroPower = component;
		component.Hide();
	}

	private void LoadGoldenHeroPowerCallback(AssetReference assetRef, GameObject go, object callbackData)
	{
		go.transform.SetParent(base.transform, worldPositionStays: true);
		m_goldenHeroPowerSkin = go.GetComponent<Actor>();
	}

	private void ShowHeroPowerBigCard(bool isDraftingHeroPower)
	{
		if (!(m_heroPower == null) && !(m_bigHeroPowerBone == null))
		{
			LayerUtils.SetLayer(m_heroPower.gameObject, GameLayer.IgnoreFullScreenEffects);
			m_heroPower.gameObject.transform.position = m_bigHeroPowerBone.position;
			m_heroPower.gameObject.transform.rotation = m_bigHeroPowerBone.rotation;
			m_heroPower.gameObject.transform.localScale = m_bigHeroPowerBone.localScale;
		}
	}

	private void ShowHeroPower(Actor actor)
	{
		m_heroPower.SetFullDefFromActor(actor);
		m_heroPower.UpdateAllComponents();
		m_heroPower.Show();
	}

	private IEnumerator ShowHeroPowerWhenDefIsLoaded(bool isDraftingHeroPower = false)
	{
		if (m_zoomedHero == null)
		{
			yield break;
		}
		if (!isDraftingHeroPower)
		{
			while (m_heroPowerDefs[m_zoomedHero.GetChoiceNum() - 1] == null)
			{
				yield return null;
			}
			int zoomedHeroIdx = m_zoomedHero.GetChoiceNum() - 1;
			if (m_mythicHeroTypes[zoomedHeroIdx] != CornerReplacementSpellType.NONE)
			{
				while (m_mythicPowerCardActors[zoomedHeroIdx] == null)
				{
					yield return null;
				}
				m_heroPower = m_mythicPowerCardActors[zoomedHeroIdx];
			}
			DefLoader.DisposableFullDef disposableFullDef = m_heroPowerDefs[zoomedHeroIdx];
			MakeHeroPowerGoldenIfPremium(disposableFullDef);
			if (!GameUtils.IsVanillaHero(m_zoomedHero.GetActor().GetEntityDef().GetCardId()))
			{
				disposableFullDef.CardDef.m_AlwaysRenderPremiumPortrait = true;
			}
		}
		m_heroPower.Show();
		ShowHeroPowerBigCard(isDraftingHeroPower);
	}

	private IEnumerator WaitAndPositionHeroPower(bool shouldDestroy)
	{
		yield return new WaitForSeconds(0.35f);
		m_inPlayHeroPowerActor = m_subclassHeroPowerActors[m_draftManager.ChosenIndex - 1];
		m_inPlayHeroPowerActor.transform.localPosition = m_socketHeroPowerBone.transform.localPosition;
		m_inPlayHeroPowerActor.transform.localScale = m_socketHeroPowerBone.transform.localScale;
		SetupToDisplayHeroPowerTooltip(m_inPlayHeroPowerActor);
		Spell componentInChildren = m_inPlayHeroPowerActor.GetComponentInChildren<Spell>();
		if (componentInChildren != null)
		{
			componentInChildren.Activate();
		}
		m_inPlayHeroPowerActor.Show();
		DraftCardVisual componentInChildren2 = m_inPlayHeroPowerActor.GetComponentInChildren<DraftCardVisual>();
		if (componentInChildren2 != null && shouldDestroy)
		{
			UnityEngine.Object.Destroy(componentInChildren2);
		}
	}

	private void HideChosenHero()
	{
		if (m_chosenHero != null)
		{
			m_chosenHero.Hide();
		}
	}

	private void HideChoices()
	{
		CleanUpChoicesInternal(shouldDestroy: false);
	}

	private void DestroyOldChoices()
	{
		CleanUpChoicesInternal(shouldDestroy: true);
	}

	private void InitializeHeroLabels(List<DraftChoice> choices)
	{
		CleanupHeroLabels();
		for (int i = 0; i < m_heroLabelBones.Count; i++)
		{
			DraftChoice choice = choices[i];
			HeroLabelInstantiatedCallbackData callbackData = new HeroLabelInstantiatedCallbackData
			{
				m_heroLabelBone = m_heroLabelBones[i],
				m_choice = choice
			};
			AssetLoader.Get().InstantiatePrefab(m_heroLabelPrefab, OnHeroLabelInstantiated, callbackData, AssetLoadingOptions.IgnorePrefabPosition);
		}
	}

	private void OnHeroLabelInstantiated(AssetReference assetRef, GameObject go, object callbackData)
	{
		HeroLabel component = go.GetComponent<HeroLabel>();
		if (!(component == null) && callbackData is HeroLabelInstantiatedCallbackData { m_choice: { } choice, m_heroLabelBone: var heroLabelBone } && !(heroLabelBone == null))
		{
			m_currentLabels.Add(component);
			go.transform.parent = heroLabelBone;
			go.transform.localPosition = Vector3.zero;
			go.transform.localScale = Vector3.one;
			go.transform.localRotation.SetEulerAngles(Vector3.zero);
			Color overrideColor = Color.white;
			if (m_draftManager.GetDraftPaperTextColorOverride(ref overrideColor))
			{
				component.SetColor(overrideColor);
			}
			EntityDef entityDef = DefLoader.Get().GetEntityDef(choice.m_cardID);
			component.UpdateText(entityDef.GetName(), GameStrings.GetClassName(entityDef.GetClass()).ToUpper());
		}
	}

	private void CleanupHeroLabels()
	{
		foreach (HeroLabel currentLabel in m_currentLabels)
		{
			currentLabel.FadeOut();
		}
		m_currentLabels.Clear();
	}

	private void CleanUpChoicesInternal(bool shouldDestroy)
	{
		m_animationsComplete = false;
		for (int i = 1; i < m_choices.Count + 1; i++)
		{
			DraftChoice draftChoice = m_choices[i - 1];
			Actor actor = draftChoice.m_actor;
			if (actor == null)
			{
				continue;
			}
			Actor subActor = draftChoice.m_subActor;
			actor.TurnOffCollider();
			Spell spell = actor.GetSpell(GetSpellTypeForRarity(actor.GetEntityDef().GetRarity()));
			if (i == m_draftManager.ChosenIndex)
			{
				if (actor.GetEntityDef().IsHeroSkin())
				{
					CleanupHeroLabels();
					continue;
				}
				if (actor.GetEntityDef().IsHeroPower())
				{
					actor.transform.parent = null;
					LayerUtils.SetLayer(actor.gameObject, GameLayer.IgnoreFullScreenEffects);
					if (!UniversalInputManager.UsePhoneUI)
					{
						m_heroPower = actor.Clone();
						m_heroPower.Hide();
						StartCoroutine(WaitAndPositionHeroPower(shouldDestroy));
					}
					else
					{
						Actor[] subclassHeroPowerActors = m_subclassHeroPowerActors;
						for (int j = 0; j < subclassHeroPowerActors.Length; j++)
						{
							subclassHeroPowerActors[j].Hide();
						}
						SetupToDisplayHeroPowerTooltip(m_inPlayHeroPowerActor);
						m_heroPower.Hide();
					}
					CleanupHeroLabels();
					continue;
				}
				Spell spell2 = actor.GetSpell(SpellType.SUMMON_OUT_FORGE);
				if (spell2 == null)
				{
					Debug.LogError("DraftDisplay.DestroyOldChoices: The SUMMON_OUT_FORGE spell is missing from the spell table for this card.");
					continue;
				}
				if (shouldDestroy)
				{
					spell2.AddFinishedCallback(DestroyChoiceOnSpellFinish, actor);
				}
				actor.ActivateSpellBirthState(SpellType.SUMMON_OUT_FORGE);
				spell.ActivateState(SpellStateType.DEATH);
				SoundManager.Get().LoadAndPlay("forge_select_card_1.prefab:b770cd64bb913f0409902629f975421e");
				continue;
			}
			SoundManager.Get().LoadAndPlay("unselected_cards_dissipate.prefab:a68b6959b8e9ed4408bf2475f37fd97d");
			if (shouldDestroy)
			{
				Spell spell3 = actor.GetSpell(SpellType.BURN);
				if (spell3 != null)
				{
					spell3.AddFinishedCallback(DestroyChoiceOnSpellFinish, actor);
					actor.ActivateSpellBirthState(SpellType.BURN);
				}
				spell3 = ((subActor == null) ? null : subActor.GetSpell(SpellType.BURN));
				if (spell3 != null)
				{
					spell3.AddFinishedCallback(DestroyChoiceOnSpellFinish, subActor);
					subActor.ActivateSpellBirthState(SpellType.BURN);
				}
			}
			else
			{
				actor.Hide();
				if (subActor != null)
				{
					subActor.Hide();
				}
			}
			if (spell != null)
			{
				spell.ActivateState(SpellStateType.DEATH);
			}
		}
		StartCoroutine(CompleteAnims());
	}

	private void SetupToDisplayHeroPowerTooltip(Actor actor)
	{
		if (actor == null)
		{
			Log.Arena.PrintWarning("DraftDisplay.SetupToDisplayHeroPowerTooltip: Actor is null!");
			return;
		}
		PegUIElement pegUIElement = actor.gameObject.GetComponent<PegUIElement>();
		if (pegUIElement == null)
		{
			pegUIElement = actor.gameObject.AddComponent<PegUIElement>();
			pegUIElement.gameObject.AddComponent<BoxCollider>();
		}
		pegUIElement.AddEventListener(UIEventType.ROLLOVER, OnMouseOverHeroPower);
		pegUIElement.AddEventListener(UIEventType.ROLLOUT, OnMouseOutHeroPower);
		actor.Show();
	}

	private IEnumerator CompleteAnims()
	{
		yield return new WaitForSeconds(0.5f);
		m_animationsComplete = true;
	}

	private void CleanupChoicesOnSpellFinish_HeroPower(Spell spell, object actorObject)
	{
		foreach (Actor subclassHeroClone in m_subclassHeroClones)
		{
			subclassHeroClone.Hide();
		}
		Actor[] subclassHeroPowerActors = m_subclassHeroPowerActors;
		foreach (Actor actor in subclassHeroPowerActors)
		{
			if (actor != m_inPlayHeroPowerActor)
			{
				actor.Hide();
			}
		}
		DestroyChoiceOnSpellFinish(spell, actorObject);
	}

	private void DestroyChoiceOnSpellFinish(Spell spell, object actorObject)
	{
		Actor actor = (Actor)actorObject;
		StartCoroutine(DestroyObjectAfterDelay(actor.gameObject));
	}

	private IEnumerator DestroyObjectAfterDelay(GameObject gameObjectToDestroy)
	{
		yield return new WaitForSeconds(5f);
		UnityEngine.Object.Destroy(gameObjectToDestroy);
	}

	private void OnFullDefLoaded(string cardID, DefLoader.DisposableFullDef def, object userData)
	{
		using (def)
		{
			if (def == null)
			{
				Debug.LogErrorFormat("Unable to load FullDef for cardID={0}", cardID);
				return;
			}
			ChoiceCallback choiceCallback = (ChoiceCallback)userData;
			choiceCallback.fullDef = def;
			if (def.EntityDef.IsHeroSkin())
			{
				CornerReplacementSpellType cornerReplacementSpellType = choiceCallback.fullDef.EntityDef?.GetTag<CornerReplacementSpellType>(GAME_TAG.CORNER_REPLACEMENT_TYPE) ?? CornerReplacementSpellType.NONE;
				m_mythicHeroTypes[choiceCallback.choiceID - 1] = cornerReplacementSpellType;
				if (cornerReplacementSpellType != CornerReplacementSpellType.NONE)
				{
					string actor = CornerReplacementConfig.Get().GetActor(cornerReplacementSpellType, ActorNames.ACTOR_ASSET.HISTORY_HERO_POWER, choiceCallback.premium);
					if (string.IsNullOrEmpty(actor))
					{
						Debug.LogWarningFormat("Unable to find replacement History Hero Power Actor for {0}, defaulting to normal flow", cornerReplacementSpellType);
						m_mythicHeroTypes[choiceCallback.choiceID - 1] = CornerReplacementSpellType.NONE;
					}
					else
					{
						AssetLoader.Get().InstantiatePrefab(actor, OnMythicHeroPowerLoaded, choiceCallback.Copy(), AssetLoadingOptions.IgnorePrefabPosition);
					}
				}
				AssetLoader.Get().InstantiatePrefab(ActorNames.GetZoneActor(def.EntityDef, TAG_ZONE.PLAY), OnActorLoaded, choiceCallback.Copy(), AssetLoadingOptions.IgnorePrefabPosition);
				DefLoader.Get().LoadCardDef(def.EntityDef.GetCardId(), OnCardDefLoaded, choiceCallback.choiceID);
				string heroPowerCardIdFromHero = GameUtils.GetHeroPowerCardIdFromHero(def.EntityDef.GetCardId());
				DefLoader.Get().LoadFullDef(heroPowerCardIdFromHero, OnHeroPowerFullDefLoaded, choiceCallback.choiceID);
			}
			else if (def.EntityDef.IsHeroPower())
			{
				AssetLoader.Get().InstantiatePrefab(ActorNames.GetHandActor(def.EntityDef, choiceCallback.premium), OnActorLoaded, choiceCallback.Copy(), AssetLoadingOptions.IgnorePrefabPosition);
				AssetLoader.Get().InstantiatePrefab(ActorNames.GetZoneActor(def.EntityDef, TAG_ZONE.PLAY, choiceCallback.premium), OnSubClassActorLoaded, choiceCallback.Copy(), AssetLoadingOptions.IgnorePrefabPosition);
			}
			else
			{
				AssetLoader.Get().InstantiatePrefab(ActorNames.GetHandActor(def.EntityDef, choiceCallback.premium), OnActorLoaded, choiceCallback.Copy(), AssetLoadingOptions.IgnorePrefabPosition);
			}
		}
	}

	private void OnHeroPowerFullDefLoaded(string cardID, DefLoader.DisposableFullDef def, object userData)
	{
		int num = (int)userData;
		m_heroPowerDefs[num - 1]?.Dispose();
		m_heroPowerDefs[num - 1] = def;
	}

	private void SetUpRewardBoxesDataModel()
	{
		if (m_thisWidget != null)
		{
			DraftManager draftManager = DraftManager.Get();
			m_arenaChestDataModel = new ArenaChestDataModel
			{
				wins = draftManager.GetWins(),
				losses = draftManager.GetLosses(),
				isUnderground = draftManager.IsCurrentRewardUnderground,
				isCrowdsFavor = draftManager.IsCurrentRewardCrowdsFavor
			};
			m_thisWidget.BindDataModel(m_arenaChestDataModel);
			BindDataModelsToRewardsWidget();
		}
	}

	private void InitializeRewardBoxesDisplayIfNeeded(PrefabCallback<GameObject> callback, object callbackData = null)
	{
		if (m_rewardBoxesDisplay != null)
		{
			callback(null, m_rewardBoxesDisplay.gameObject, callbackData);
		}
		else
		{
			AssetLoader.Get().InstantiatePrefab(m_rewardsBoxesPrefab, callback, callbackData);
		}
	}

	public void AnimateRewards()
	{
		if (!IsRewardsFlowPending)
		{
			IsRewardsFlowPending = true;
			StartCoroutine(CreateRewardsWidgetPrefab(InitRewardBoxesDisplayToAnimateRewards));
		}
	}

	private void InitRewardBoxesDisplayToAnimateRewards()
	{
		InitializeRewardBoxesDisplayIfNeeded(AnimateRewardsOnActorLoaded);
	}

	private void AnimateRewardsOnActorLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (m_rewardBoxesDisplay == null && go != null)
		{
			m_rewardBoxesDisplay = go.GetComponentInChildren<RewardBoxesDisplay>();
		}
		if (m_rewardBoxesDisplay == null)
		{
			return;
		}
		m_rewardBoxesDisplay.transform.parent = m_rewardsBoxesBone.transform;
		m_rewardBoxesDisplay.transform.localPosition = Vector3.zero;
		m_rewardBoxesDisplay.transform.localRotation.SetEulerAngles(Vector3.zero);
		m_rewardBoxesDisplay.transform.localScale = Vector3.one;
		if (m_draftManager != null)
		{
			if (m_draftManager.IsRewardChestReady())
			{
				OnRewardChestReady();
				return;
			}
			m_draftManager.OnRewardChestReady -= OnRewardChestReady;
			m_draftManager.OnRewardChestReady += OnRewardChestReady;
		}
	}

	private void OnRewardChestReady()
	{
		m_draftManager.OnRewardChestReady -= OnRewardChestReady;
		List<RewardData> rewards = m_draftManager.GetRewards();
		m_rewardBoxesDisplay.SetRewards(rewards);
		m_rewardBoxesDisplay.RegisterDoneCallback(OnRewardBoxesDone);
	}

	private void OnRewardBoxesDone()
	{
		if (this == null || base.gameObject == null)
		{
			return;
		}
		DraftManager draftManager = DraftManager.Get();
		CollectionDeck draftDeck = draftManager.GetDraftDeck();
		if (draftDeck != null)
		{
			Network.Get().AckDraftRewards(draftDeck.ID, draftManager.GetSlot(), m_draftManager.IsUnderground());
			if (m_rewardsVisualController != null)
			{
				m_rewardsVisualController.SetState("EXIT");
			}
			OnOpenRewardsComplete();
		}
	}

	private void ShowCurrentlyDraftingScreen()
	{
		m_wasDrafting = true;
		LoadAndPositionHeroCard(m_socketHeroBone);
		NarrativeManager.Get().OnArenaDraftStarted();
	}

	private bool IsDeckValid()
	{
		DraftManager draftManager = DraftManager.Get();
		if (draftManager == null)
		{
			return false;
		}
		CollectionDeck draftDeck = draftManager.GetDraftDeck();
		if (draftDeck == null)
		{
			return false;
		}
		foreach (CollectionDeckSlot slot in draftDeck.GetSlots())
		{
			if (GameUtils.IsBannedByArenaDenylist(slot.GetEntityDef().GetCardId()))
			{
				return false;
			}
		}
		return true;
	}

	public bool IsChoiceValid(int choice)
	{
		int num = choice - 1;
		if (num > m_choices.Count - 1 || num < 0)
		{
			return false;
		}
		DraftChoice draftChoice = m_choices[num];
		EntityDef entityDef = DefLoader.Get().GetEntityDef(draftChoice.m_cardID);
		if (entityDef == null)
		{
			return false;
		}
		if (GameUtils.IsBannedByArenaDenylist(entityDef.GetCardId()))
		{
			return false;
		}
		return true;
	}

	private bool ArePackageCardsValid(List<string> packageCards)
	{
		if (packageCards == null)
		{
			return false;
		}
		if (packageCards.Count == 0)
		{
			return false;
		}
		for (int i = 0; i < packageCards.Count; i++)
		{
			if (GameUtils.IsBannedByArenaDenylist(packageCards[i]))
			{
				return false;
			}
		}
		return true;
	}

	private IEnumerator ShowActiveDraftScreen()
	{
		m_draftManager.GetLosses();
		DestroyOldChoices();
		if (m_playButton != null)
		{
			if (!IsDeckValid())
			{
				m_playButton.Disable();
			}
			else
			{
				m_playButton.Enable();
			}
		}
		if (m_wasDrafting)
		{
			yield return new WaitForSeconds(0.3f);
		}
	}

	private void ShowDraftRewardsScreen()
	{
		SetUpRewardBoxesDataModel();
		StartCoroutine(OnRewardsFlowCoroutine());
	}

	private void LastArenaWinsLabelLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		int num = (int)callbackData;
		go.GetComponent<UberText>().Text = "Last Arena: " + num + " Wins";
		go.transform.position = new Vector3(11.40591f, 1.341853f, 29.28797f);
		go.transform.localScale = new Vector3(15f, 15f, 15f);
	}

	private void LoadAndPositionHeroCard(Transform heroCardTransformParent)
	{
		CollectionDeck draftDeck = m_draftManager.GetDraftDeck();
		if (draftDeck == null)
		{
			Log.All.Print("bug 8052, null exception");
			return;
		}
		if (m_chosenHero != null && m_chosenHero.GetEntityDef().GetCardId() != draftDeck.HeroCardID)
		{
			m_chosenHero.Destroy();
			m_chosenHero = null;
		}
		if (m_chosenHero != null)
		{
			m_chosenHero.Show();
			return;
		}
		TAG_PREMIUM heroPremium = CollectionManager.Get().GetHeroPremium(draftDeck.GetClass());
		GameUtils.LoadAndPositionCardActor("Card_Play_Hero.prefab:42cbbd2c4969afb46b3887bb628de19d", draftDeck.HeroCardID, heroPremium, delegate(Actor actor)
		{
			OnHeroActorLoaded(actor, heroCardTransformParent);
		});
		string actorName;
		if (heroPremium == TAG_PREMIUM.GOLDEN)
		{
			actorName = "Card_Play_HeroPower_Premium.prefab:015ad985f9ec49e4db327d131fd79901";
			GameUtils.LoadAndPositionCardActor("History_HeroPower_Premium.prefab:081da807b95b8495e9f16825c5164787", draftDeck.HeroPowerCardID, heroPremium, LoadHeroPowerCallback);
		}
		else
		{
			actorName = "Card_Play_HeroPower.prefab:a3794839abb947146903a26be13e09af";
		}
		GameUtils.LoadAndPositionCardActor(actorName, draftDeck.HeroPowerCardID, heroPremium, OnHeroPowerActorLoaded);
	}

	private void OnNetCacheReady()
	{
		NetCache.Get().UnregisterNetCacheHandler(OnNetCacheReady);
		if (!NetCache.Get().GetNetObject<NetCache.NetCacheFeatures>().Games.Forge)
		{
			if (!SceneMgr.Get().IsModeRequested(SceneMgr.Mode.HUB))
			{
				SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
				Error.AddWarningLoc("GLOBAL_FEATURE_DISABLED_TITLE", "GLOBAL_FEATURE_DISABLED_MESSAGE_FORGE");
			}
		}
		else
		{
			m_netCacheReady = true;
		}
	}

	private void OnArenaClientStateUpdated(ArenaClientStateType newState)
	{
		switch (newState)
		{
		case ArenaClientStateType.Normal_DeckEdit:
		case ArenaClientStateType.Underground_DeckEdit:
			StartCoroutine(ShowPhaseMessageWithDelays(PhaseMessageType.Edit_Deck));
			break;
		case ArenaClientStateType.Normal_Ready:
		case ArenaClientStateType.Underground_Ready:
			StartCoroutine(ShowPhaseMessageWithDelays(PhaseMessageType.Ready_Up));
			break;
		case ArenaClientStateType.Normal_Landing:
		case ArenaClientStateType.Underground_Landing:
			ShowRewardsIfNecessary();
			break;
		}
	}

	private IEnumerator DisplayCurrentDraftChoices()
	{
		if (!m_isHandlingChoiceDisplay)
		{
			m_isHandlingChoiceDisplay = true;
			while (!m_draftManager.IsClientStateInAnyDrafting() || !m_areDraftCardsBeingShown)
			{
				yield return new WaitForEndOfFrame();
			}
			yield return StartCoroutine(HandlePreChoicesShownActions());
			while (!HaveActorsForAllChoices())
			{
				yield return null;
			}
			PositionAndShowChoices();
			yield return StartCoroutine(RunAutoDraftCheat());
			m_isHandlingChoiceDisplay = false;
		}
	}

	private void PositionAndShowChoices()
	{
		bool flag = false;
		for (int i = 0; i < m_choices.Count; i++)
		{
			DraftChoice draftChoice = m_choices[i];
			if (draftChoice.m_actor == null)
			{
				Debug.LogWarning($"DraftDisplay.PositionAndShowChoices(): WARNING found choice with null actor (cardID = {draftChoice.m_cardID}). Skipping...");
				continue;
			}
			EntityDef entityDef = DefLoader.Get().GetEntityDef(draftChoice.m_cardID);
			bool flag2 = entityDef.IsHeroSkin();
			bool flag3 = entityDef.IsHeroPower();
			TAG_RARITY tAG_RARITY = TAG_RARITY.COMMON;
			Actor actor = null;
			Actor heroPowerActor = null;
			if (flag3)
			{
				LayerUtils.SetLayer(m_chosenHero.gameObject, GameLayer.Default);
				actor = m_chosenHero.Clone();
				UberShaderController[] componentsInChildren = actor.GetComponentsInChildren<UberShaderController>(includeInactive: true);
				if (componentsInChildren != null)
				{
					foreach (UberShaderController uberShaderController in componentsInChildren)
					{
						if (uberShaderController.UberShaderAnimation != null)
						{
							uberShaderController.UberShaderAnimation = UnityEngine.Object.Instantiate(uberShaderController.UberShaderAnimation);
						}
					}
				}
				actor.Show();
				actor.ActivateSpellBirthState(SpellType.SUMMON_IN_FORGE);
				tAG_RARITY = actor.GetEntityDef().GetRarity();
				actor.ActivateSpellBirthState(GetSpellTypeForRarity(tAG_RARITY));
				m_subclassHeroClones.Add(actor);
				DraftCardVisual draftCardVisual = actor.GetCollider().gameObject.GetComponent<DraftCardVisual>();
				if (draftCardVisual == null)
				{
					draftCardVisual = actor.GetCollider().gameObject.AddComponent<DraftCardVisual>();
				}
				draftCardVisual.SetChoiceNum(i + 1);
				draftCardVisual.SetActor(draftChoice.m_actor);
				draftCardVisual.SetSubActor(actor);
				draftChoice.m_subActor = actor;
				actor.TurnOnCollider();
				heroPowerActor = m_subclassHeroPowerActors[i];
				if (i < m_heroPowerBones.Count && m_heroPowerBones[i] != null)
				{
					heroPowerActor.transform.position = m_heroPowerBones[i].position;
					heroPowerActor.transform.localScale = m_heroPowerBones[i].localScale;
				}
				draftCardVisual = heroPowerActor.GetCollider().gameObject.AddComponent<DraftCardVisual>();
				draftCardVisual.SetChoiceNum(i + 1);
				draftCardVisual.SetActor(draftChoice.m_actor);
				draftCardVisual.SetSubActor(actor);
				heroPowerActor.TurnOnCollider();
				DefLoader.DisposableFullDef disposableFullDef = m_subClassHeroPowerDefs[i];
				heroPowerActor.SetPremium(draftChoice.m_premium);
				heroPowerActor.SetCardDef(disposableFullDef.DisposableCardDef);
				heroPowerActor.SetEntityDef(disposableFullDef.EntityDef);
				heroPowerActor.UpdateAllComponents();
				heroPowerActor.Hide();
			}
			else
			{
				if (flag2)
				{
					if (m_heroLabelBones == null || i >= m_heroLabelBones.Count())
					{
						break;
					}
					if (!flag)
					{
						flag = true;
						InitializeHeroLabels(m_choices);
					}
				}
				SetCardTransform(i, flag2, draftChoice.m_actor);
				draftChoice.m_actor.Show();
				if (GameUtils.GetSignatureDisplayPreference() == SignatureDisplayPreference.RARELY)
				{
					if (!UniversalInputManager.Get().IsTouchMode())
					{
						draftChoice.m_actor.TurnSignatureTextOff(animate: false);
					}
					else
					{
						draftChoice.m_actor.TurnSignatureTextOn(animate: false);
					}
				}
				draftChoice.m_actor.TurnOnCollider();
				draftChoice.m_actor.ActivateSpellBirthState(SpellType.SUMMON_IN_FORGE);
				tAG_RARITY = draftChoice.m_actor.GetEntityDef().GetRarity();
			}
			switch (tAG_RARITY)
			{
			case TAG_RARITY.COMMON:
			case TAG_RARITY.FREE:
			case TAG_RARITY.RARE:
				SoundManager.Get().LoadAndPlay("forge_normal_card_appears.prefab:3e1223a4e6503f2469fb0090db8da67e");
				break;
			case TAG_RARITY.EPIC:
			case TAG_RARITY.LEGENDARY:
				SoundManager.Get().LoadAndPlay("forge_rarity_card_appears.prefab:4ecbc5de846e50746986849690c01e6a");
				break;
			}
			if (flag2)
			{
				draftChoice.m_actor.GetHealthObject().Hide();
				if (i < m_heroBones.Count && m_heroBones[i] != null)
				{
					draftChoice.m_actor.transform.position = m_heroBones[i].position;
					draftChoice.m_actor.transform.localScale = m_heroBones[i].localScale;
				}
			}
			else
			{
				if (!flag3)
				{
					continue;
				}
				actor.GetHealthObject().Hide();
				actor.GetSpell(SpellType.SUMMON_IN_FORGE).AddSpellEventCallback(delegate(string eventName, object eventData, object userData)
				{
					if (eventName == SummonInForge.ACTOR_VISIBLE_EVENT)
					{
						heroPowerActor.Show();
					}
				});
			}
		}
	}

	private IEnumerator HandlePreChoicesShownActions()
	{
		switch (m_draftManager.GetCurrentClientState())
		{
		case ArenaClientStateType.Normal_Draft:
		case ArenaClientStateType.Underground_Draft:
			if (m_choices.All((DraftChoice m) => DefLoader.Get().GetEntityDef(m.m_cardID).IsHero()) && !m_draftManager.IsRedrafting())
			{
				yield return StartCoroutine(ShowPhaseMessageWithDelays(PhaseMessageType.Choose_Hero));
			}
			else if (m_choices.All((DraftChoice m) => DefLoader.Get().GetEntityDef(m.m_cardID).GetRarity() == TAG_RARITY.LEGENDARY) && !m_draftManager.IsRedrafting())
			{
				yield return StartCoroutine(ShowPhaseMessageWithDelays(PhaseMessageType.Choose_Legendary));
			}
			else if (!m_hasSeenDraftChoices)
			{
				yield return StartCoroutine(ShowPhaseMessageWithDelays(PhaseMessageType.Choose_Card));
				m_hasSeenDraftChoices = true;
			}
			break;
		case ArenaClientStateType.Normal_Redraft:
		case ArenaClientStateType.Underground_Redraft:
			if (!m_hasSeenRedraftChoices)
			{
				yield return StartCoroutine(ShowPhaseMessageWithDelays(PhaseMessageType.Redraft));
				m_hasSeenRedraftChoices = true;
			}
			break;
		}
		if (!m_phaseMessageShowing)
		{
			UpdateDraftTextLabels();
		}
	}

	private IEnumerator ShowPhaseMessageWithDelays(PhaseMessageType phaseMessage)
	{
		if (!IsPhaseMessagesEnabled())
		{
			yield break;
		}
		StartCoroutine(ShowArenaHeader(phaseMessage));
		if (HasAlreadySeenPhaseMessage(phaseMessage) && IsPhaseMessageFTUXExperianceActiveOnGuardian())
		{
			yield break;
		}
		m_phaseMessageShowing = true;
		float delayForPhaseMessage = GetDelayForPhaseMessage(phaseMessage);
		yield return new WaitForSeconds(delayForPhaseMessage);
		if (!(m_phasePopupVisualController == null))
		{
			m_phasePopupVisualController.SetState(GetPhaseMessageStateFromEnum(phaseMessage));
			SetPhaseMessageShownValue(phaseMessage, value: true);
			while (m_phaseMessageShowing)
			{
				yield return new WaitForEndOfFrame();
			}
		}
	}

	private IEnumerator ShowArenaHeader(PhaseMessageType phaseMessage)
	{
		if (!HasAlreadySeenPhaseMessage(phaseMessage) && m_PhasePopup != null)
		{
			float delayForPhaseMessage = GetDelayForPhaseMessage(phaseMessage);
			yield return new WaitForSeconds(delayForPhaseMessage);
			yield return new WaitUntil(() => !m_PhasePopup.activeInHierarchy);
		}
		m_chooseHero.SetActive(phaseMessage == PhaseMessageType.Choose_Hero);
		m_chooseCard.SetActive(phaseMessage != PhaseMessageType.Choose_Hero);
		SetArenaHeader(phaseMessage);
	}

	private bool IsPhaseMessageFTUXExperianceActiveOnGuardian()
	{
		return NetCache.Get().GetNetObject<NetCache.NetCacheFeatures>().EnableArenaPhaseMessageFTUXTracking;
	}

	private bool IsPhaseMessagesEnabled()
	{
		return NetCache.Get().GetNetObject<NetCache.NetCacheFeatures>().EnableArenaPhaseMessages;
	}

	private bool HasAlreadySeenPhaseMessage(PhaseMessageType phaseMessage)
	{
		switch (phaseMessage)
		{
		case PhaseMessageType.Choose_Hero:
			return Options.Get().GetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_HERO);
		case PhaseMessageType.Choose_Legendary:
			return Options.Get().GetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_LEGENDARY);
		case PhaseMessageType.Choose_Card:
			return Options.Get().GetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_CARD);
		case PhaseMessageType.Ready_Up:
			return Options.Get().GetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_READY);
		case PhaseMessageType.Redraft:
			return Options.Get().GetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_REDRAFT);
		case PhaseMessageType.Edit_Deck:
			return Options.Get().GetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_EDITDECK);
		default:
			Log.Arena.PrintError("DraftDisplay: ShouldShowPhaseMessage - Unexpected state");
			return false;
		}
	}

	private void SetPhaseMessageShownValue(PhaseMessageType phaseMessage, bool value)
	{
		switch (phaseMessage)
		{
		case PhaseMessageType.Choose_Hero:
			Options.Get().SetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_HERO, value);
			break;
		case PhaseMessageType.Choose_Legendary:
			Options.Get().SetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_LEGENDARY, value);
			break;
		case PhaseMessageType.Choose_Card:
			Options.Get().SetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_CARD, value);
			break;
		case PhaseMessageType.Ready_Up:
			Options.Get().SetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_READY, value);
			break;
		case PhaseMessageType.Redraft:
			Options.Get().SetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_REDRAFT, value);
			break;
		case PhaseMessageType.Edit_Deck:
			Options.Get().SetBool(Option.HAS_SEEN_FORGE_PHASE_MESSAGE_EDITDECK, value);
			break;
		default:
			Log.Arena.PrintError("DraftDisplay: ShouldShowPhaseMessage - Unexpected state");
			break;
		}
	}

	private float GetDelayForPhaseMessage(PhaseMessageType phaseMessage)
	{
		foreach (PhaseMessageDelayData phaseMessageDelayDatum in m_phaseMessageDelayData)
		{
			if (IsPhaseDataValidRightNow(phaseMessage, phaseMessageDelayDatum))
			{
				return phaseMessageDelayDatum.Delay;
			}
		}
		return 0f;
	}

	private bool IsPhaseDataValidRightNow(PhaseMessageType phaseMessageToCheck, PhaseMessageDelayData data)
	{
		if (phaseMessageToCheck != data.PhaseMessage)
		{
			return false;
		}
		ArenaClientStateType previousClientState = m_draftManager.GetPreviousClientState();
		ArenaClientStateType currentClientState = m_draftManager.GetCurrentClientState();
		if (data.OptionalPreviousState != ArenaClientStateType.None && data.OptionalPreviousState != previousClientState)
		{
			return false;
		}
		if (data.OptionalCurrentState != ArenaClientStateType.None && data.OptionalCurrentState != currentClientState)
		{
			return false;
		}
		return true;
	}

	private string GetPhaseMessageStateFromEnum(PhaseMessageType phaseMessage)
	{
		switch (phaseMessage)
		{
		case PhaseMessageType.Choose_Hero:
			return "CHOOSE_HERO_PHASE_POPUP";
		case PhaseMessageType.Choose_Legendary:
			return "CHOOSE_LEGENDARY_PHASE_POPUP";
		case PhaseMessageType.Choose_Card:
			return "DRAFT_PHASE_POPUP";
		case PhaseMessageType.Ready_Up:
			return "READY_PHASE_POPUP";
		case PhaseMessageType.Redraft:
			return "REDRAFT_PHASE_POPUP";
		case PhaseMessageType.Edit_Deck:
			return "EDIT_DECK_POPUP";
		default:
			Log.Arena.PrintError("DraftDisplay: GetPhaseMessageStateFromEnum - Unexpected state");
			return "";
		}
	}

	private void SetArenaHeader(PhaseMessageType phaseMessage)
	{
		if (!(m_arenaDraftScreenWidget == null))
		{
			string eventName = "DEFAULT";
			switch (phaseMessage)
			{
			case PhaseMessageType.Choose_Card:
				eventName = "CHOOSE_NORMAL_HEADER";
				break;
			case PhaseMessageType.Choose_Legendary:
				eventName = "CHOOSE_LEGENDARY_HEADER";
				break;
			case PhaseMessageType.Edit_Deck:
				eventName = "EDIT_YOUR_DECK_HEADER";
				break;
			}
			m_arenaDraftScreenWidget.TriggerEvent(eventName);
		}
	}

	private void UpdateDraftTextLabels()
	{
		bool flag = false;
		if (m_choices.Count > 0 && m_choices.All((DraftChoice m) => DefLoader.Get().GetEntityDef(m.m_cardID).IsHero()))
		{
			flag = true;
			HeroLabelsText.SetActive(value: true);
			HeroTitleText.SetActive(value: true);
		}
		if (!flag)
		{
			DraftTitleText.SetActive(value: true);
		}
	}

	private void OnMoveFromLanding()
	{
		bool isUnderground = m_draftManager.IsUnderground();
		ArenaSessionState arenaState = m_draftManager.GetArenaState(isUnderground);
		if (arenaState == ArenaSessionState.MIDRUN || arenaState == ArenaSessionState.EDITING_DECK)
		{
			m_draftManager.SetClientState(m_draftManager.IsUnderground() ? ArenaClientStateType.Underground_Ready : ArenaClientStateType.Normal_Ready);
		}
		else
		{
			m_draftManager.SetClientState(m_draftManager.IsUnderground() ? ArenaClientStateType.Underground_Draft : ArenaClientStateType.Normal_Draft);
			BroadcastArenaDraftScreenEvent("CODE_START_DRAFT");
			LoadAndPositionHeroCard(m_socketHeroBone);
			StartCoroutine(DisplayCurrentDraftChoices());
		}
		if (m_draftManager.IsEditingDeck())
		{
			SetupEditDeckTray();
		}
	}

	public void OnMoveToDeck()
	{
		m_draftManager.SetClientState(m_draftManager.IsUnderground() ? ArenaClientStateType.Underground_DeckCollection : ArenaClientStateType.Normal_DeckCollection);
	}

	private void OnPhaseMessageClosed()
	{
		m_phaseMessageShowing = false;
	}

	private void OnMovedToLanding()
	{
		m_draftManager.SetClientState((!m_draftManager.IsUnderground()) ? ArenaClientStateType.Normal_Landing : ArenaClientStateType.Underground_Landing);
		m_hasSeenDraftChoices = false;
		HideChoices();
		HideChosenHero();
		if (m_collectionPageA != null)
		{
			m_collectionPageA.transform.localPosition = Vector3.right * 500f;
		}
		if (m_collectionPageB != null)
		{
			m_collectionPageB.transform.localPosition = Vector3.right * 500f;
		}
		m_arenaLandingPageManager.DetermineToggleButtonStartingState();
	}

	private void SetCardTransform(int cardIndex, bool isHeroSkin, Actor actor)
	{
		if (isHeroSkin)
		{
			if (cardIndex > m_heroBones.Count)
			{
				Debug.LogWarning($"DraftDisplay.GetCardPosition(): WARNING index is out of bounds");
				return;
			}
			actor.transform.position = m_heroBones[cardIndex].position;
			actor.transform.rotation = m_heroBones[cardIndex].rotation;
			actor.transform.localScale = m_heroBones[cardIndex].localScale;
		}
		else if (cardIndex > m_cardBones.Count)
		{
			Debug.LogWarning($"DraftDisplay.GetCardPosition(): WARNING index is out of bounds");
		}
		else
		{
			actor.transform.position = m_cardBones[cardIndex].position;
			actor.transform.rotation = m_cardBones[cardIndex].rotation;
			actor.transform.localScale = m_cardBones[cardIndex].localScale;
		}
	}

	private bool CanAutoDraft()
	{
		if (!HearthstoneApplication.IsInternal())
		{
			return false;
		}
		if (!Vars.Key("Arena.AutoDraft").GetBool(def: false))
		{
			return false;
		}
		return true;
	}

	public IEnumerator RunAutoDraftCheat()
	{
		if (!CanAutoDraft())
		{
			yield break;
		}
		int frameStart = Time.frameCount;
		while (GameUtils.IsAnyTransitionActive() && Time.frameCount - frameStart < 120)
		{
			yield return null;
		}
		List<DraftCardVisual> draftChoices = GetCardVisuals();
		if (draftChoices != null && draftChoices.Count > 0)
		{
			int pickedIndex = UnityEngine.Random.Range(0, draftChoices.Count - 1);
			DraftCardVisual visual = draftChoices[pickedIndex];
			frameStart = Time.frameCount;
			while (visual.GetActor() == null && Time.frameCount - frameStart < 120)
			{
				yield return null;
			}
			if (visual.GetActor() != null)
			{
				string message = $"autodraft'ing {visual.GetActor().GetEntityDef().GetName()}\nto stop, use cmd 'autodraft off'";
				UIStatus.Get().AddInfo(message, 2f);
				draftChoices[pickedIndex].ChooseThisCard();
			}
		}
	}

	public static SpellType GetSpellTypeForRarity(TAG_RARITY rarity)
	{
		return rarity switch
		{
			TAG_RARITY.RARE => SpellType.BURST_RARE, 
			TAG_RARITY.EPIC => SpellType.BURST_EPIC, 
			TAG_RARITY.LEGENDARY => SpellType.BURST_LEGENDARY, 
			_ => SpellType.BURST_COMMON, 
		};
	}

	public bool DoesChoiceHavePackageCards(int index)
	{
		if (m_choices.Count <= index)
		{
			return false;
		}
		DraftChoice draftChoice = m_choices[index];
		if (draftChoice == null)
		{
			return false;
		}
		if (draftChoice.m_packageCardIds != null)
		{
			return draftChoice.m_packageCardIds.Count > 0;
		}
		return false;
	}

	private void OnHeroActorLoaded(Actor actor, Transform transformParent)
	{
		actor.transform.SetParent(base.transform, worldPositionStays: true);
		m_chosenHero = actor;
		m_chosenHero.transform.parent = transformParent;
		m_chosenHero.transform.localPosition = Vector3.zero;
		m_chosenHero.transform.localScale = Vector3.one;
		m_chosenHero.transform.localRotation = Quaternion.identity;
	}

	private void OnHeroPowerActorLoaded(Actor actor)
	{
		actor.transform.SetParent(base.transform, worldPositionStays: true);
		m_inPlayHeroPowerActor = actor;
		SetupToDisplayHeroPowerTooltip(m_inPlayHeroPowerActor);
		m_inPlayHeroPowerActor.transform.parent = m_socketHeroPowerBone;
		m_inPlayHeroPowerActor.transform.localPosition = Vector3.zero;
		m_inPlayHeroPowerActor.transform.localScale = Vector3.one;
		m_inPlayHeroPowerActor.transform.localRotation = Quaternion.identity;
	}

	private void OnMouseOverHeroPower(UIEvent uiEvent)
	{
		if (m_inPlayHeroPowerActor != null)
		{
			ShowHeroPower(m_inPlayHeroPowerActor);
		}
	}

	private void OnMouseOutHeroPower(UIEvent uiEvent)
	{
		if (m_heroPower != null)
		{
			m_heroPower.Hide();
		}
	}

	private void OnMythicHeroPowerLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		if (go == null)
		{
			Debug.LogWarning($"DraftDisplay.OnActorLoaded() - FAILED to load actor \"{assetRef}\"");
			return;
		}
		go.transform.SetParent(base.transform, worldPositionStays: true);
		Actor component = go.GetComponent<Actor>();
		if (component == null)
		{
			Debug.LogWarning($"DraftDisplay.OnActorLoaded() - ERROR actor \"{assetRef}\" has no Actor component");
			return;
		}
		ChoiceCallback choiceCallback = (ChoiceCallback)callbackData;
		m_mythicPowerCardActors[choiceCallback.choiceID - 1] = component;
	}

	private void OnActorLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		ChoiceCallback choiceCallback = (ChoiceCallback)callbackData;
		DefLoader.DisposableFullDef fullDef = choiceCallback?.fullDef;
		try
		{
			if (go == null)
			{
				Debug.LogWarning($"DraftDisplay.OnActorLoaded() - FAILED to load actor \"{assetRef}\"");
				return;
			}
			go.transform.SetParent(base.transform, worldPositionStays: true);
			Actor component = go.GetComponent<Actor>();
			if (component == null)
			{
				Debug.LogWarning($"DraftDisplay.OnActorLoaded() - ERROR actor \"{assetRef}\" has no Actor component");
				return;
			}
			DraftChoice draftChoice = m_choices.Find((DraftChoice obj) => obj.m_cardID != null && obj.m_cardID.Equals(fullDef.EntityDef.GetCardId()));
			if (draftChoice == null)
			{
				Debug.LogWarningFormat("DraftDisplay.OnActorLoaded(): Could not find draft choice {0} (cardID = {1}) in m_choices.", fullDef.EntityDef.GetName(), fullDef.EntityDef.GetCardId());
				UnityEngine.Object.Destroy(go);
				return;
			}
			draftChoice.m_actor = component;
			draftChoice.m_actor.SetPremium(draftChoice.m_premium);
			draftChoice.m_actor.SetEntityDef(fullDef.EntityDef);
			draftChoice.m_actor.SetCardDef(fullDef.DisposableCardDef);
			draftChoice.m_actor.CreateBannedRibbon();
			draftChoice.m_actor.UpdateAllComponents();
			draftChoice.m_actor.gameObject.name = fullDef.CardDef.name + "_actor";
			draftChoice.m_actor.ContactShadow(visible: true);
			EntityDef entityDef = draftChoice.m_actor.GetEntityDef();
			if (entityDef == null)
			{
				Debug.LogWarning($"DraftDisplay.OnActorLoaded() - ERROR actor \"{assetRef}\" entityDef is null");
				return;
			}
			if (entityDef.IsHeroPower())
			{
				m_heroPowerCardActors[choiceCallback.choiceID - 1] = draftChoice.m_actor;
				if (HaveActorsForAllChoices() && HaveAllSubclassHeroPowerDefs())
				{
					StartCoroutine(DisplayCurrentDraftChoices());
				}
				else
				{
					draftChoice.m_actor.Hide();
				}
				return;
			}
			Collider collider = draftChoice.m_actor.GetCollider();
			if (collider != null)
			{
				DraftCardVisual draftCardVisual = collider.gameObject.AddComponent<DraftCardVisual>();
				draftCardVisual.SetActor(draftChoice.m_actor);
				draftCardVisual.SetChoiceNum(choiceCallback.choiceID);
			}
			if (HaveActorsForAllChoices())
			{
				StartCoroutine(DisplayCurrentDraftChoices());
			}
			else
			{
				draftChoice.m_actor.Hide();
			}
		}
		finally
		{
			if (fullDef != null)
			{
				((IDisposable)fullDef).Dispose();
			}
		}
	}

	private void OnSubClassActorLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		ChoiceCallback choiceCallback = (ChoiceCallback)callbackData;
		using DefLoader.DisposableFullDef disposableFullDef = choiceCallback.fullDef;
		if (go == null)
		{
			Debug.LogWarning($"DraftDisplay.OnDualClassActorLoaded() - FAILED to load actor \"{assetRef}\"");
			return;
		}
		go.transform.SetParent(base.transform, worldPositionStays: true);
		Actor component = go.GetComponent<Actor>();
		if (component == null)
		{
			Debug.LogWarning($"DraftDisplay.OnDualClassActorLoaded() - ERROR actor \"{assetRef}\" has no Actor component");
			return;
		}
		m_subClassHeroPowerDefs[choiceCallback.choiceID - 1]?.Dispose();
		m_subClassHeroPowerDefs[choiceCallback.choiceID - 1] = disposableFullDef.Share();
		m_subclassHeroPowerActors[choiceCallback.choiceID - 1] = component;
		if (HaveActorsForAllChoices() && HaveAllSubclassHeroPowerDefs())
		{
			StartCoroutine(DisplayCurrentDraftChoices());
		}
	}

	private void OnCardDefLoaded(string cardId, DefLoader.DisposableCardDef def, object callbackData)
	{
		using (def)
		{
			if (def == null)
			{
				return;
			}
			foreach (EmoteEntryDef item in def?.CardDef.m_EmoteDefs)
			{
				if (item.m_emoteType == EmoteType.PICKED)
				{
					AssetLoader.Get().InstantiatePrefab(item.m_emoteSoundSpellPath, OnStartEmoteLoaded, callbackData);
				}
			}
		}
	}

	private void OnStartEmoteLoaded(AssetReference assetRef, GameObject go, object callbackData)
	{
		CardSoundSpell cardSoundSpell = null;
		if (go != null)
		{
			cardSoundSpell = go.GetComponent<CardSoundSpell>();
			go.transform.SetParent(base.transform, worldPositionStays: true);
		}
		m_skipHeroEmotes |= cardSoundSpell == null;
		if (m_skipHeroEmotes)
		{
			UnityEngine.Object.Destroy(go);
			return;
		}
		int num = (int)callbackData;
		Spell spell = m_heroEmotes[num - 1];
		if (spell != null)
		{
			SpellManager.Get()?.ReleaseSpell(spell);
		}
		m_heroEmotes[num - 1] = cardSoundSpell;
	}

	private bool HaveActorsForAllChoices()
	{
		foreach (DraftChoice choice in m_choices)
		{
			if (!string.IsNullOrEmpty(choice.m_cardID) && choice.m_actor == null)
			{
				return false;
			}
		}
		return true;
	}

	private bool HaveAllSubclassHeroPowerDefs()
	{
		DefLoader.DisposableFullDef[] subClassHeroPowerDefs = m_subClassHeroPowerDefs;
		for (int i = 0; i < subClassHeroPowerDefs.Length; i++)
		{
			if (subClassHeroPowerDefs[i] == null)
			{
				return false;
			}
		}
		return true;
	}

	private void InitManaCurve()
	{
		CollectionDeck draftDeck = m_draftManager.GetDraftDeck();
		foreach (DraftManaCurve manaCurf in m_manaCurves)
		{
			manaCurf.ResetBars(shouldAnimate: false);
		}
		if (draftDeck == null)
		{
			foreach (DraftManaCurve manaCurf2 in m_manaCurves)
			{
				manaCurf2.UpdateBars();
			}
			return;
		}
		foreach (CollectionDeckSlot slot in draftDeck.GetSlots())
		{
			EntityDef entityDef = DefLoader.Get().GetEntityDef(slot.CardID);
			for (int i = 0; i < slot.Count; i++)
			{
				AddCardToManaCurve(entityDef, shouldAnimate: false);
			}
		}
		foreach (DraftManaCurve manaCurf3 in m_manaCurves)
		{
			manaCurf3.UpdateBars(shouldAnimate: false);
		}
	}

	private bool OnNavigateBack()
	{
		if (IsInHeroSelectMode())
		{
			DoHeroCancelAnimation();
			return false;
		}
		ExitDraftScene();
		return true;
	}

	private void BackButtonPress()
	{
		Navigation.GoBack();
	}

	private void ExitDraftScene()
	{
		GameMgr.Get().CancelFindGame();
		if (m_playButton != null)
		{
			m_playButton.Disable();
		}
		SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
		Box.Get().SetToIgnoreFullScreenEffects(ignoreEffects: false);
		if (m_arenaDraftScreen != null)
		{
			m_arenaDraftScreen.HideWidget();
		}
		if (m_arenaLandingPageManager != null)
		{
			m_arenaLandingPageManager.HideInactiveLandingPage();
		}
	}

	private void PlayButtonPress(UIEvent e)
	{
		if (SetRotationManager.Get().CheckForSetRotationRollover() || (PlayerMigrationManager.Get() != null && PlayerMigrationManager.Get().CheckForPlayerMigrationRequired()) || GameUtils.IsAnyTransitionActive())
		{
			return;
		}
		DraftDisplay draftDisplay = Get();
		if (!(draftDisplay != null) || !draftDisplay.IsRewardsFlowPending)
		{
			if (m_playButton != null)
			{
				m_playButton.Disable();
			}
			if (m_draftManager.IsRedrafting())
			{
				TriggerRedraft();
				StartCoroutine(DisplayCurrentDraftChoices());
				return;
			}
			SetRetireButtonEnabled(enabled: false);
			SetViewDeckButtonEnabled(enabled: false);
			SetBackButtonEnabled(enabled: false);
			m_draftManager.FindGame();
			PresenceMgr.Get().SetStatus(Global.PresenceStatus.ARENA_QUEUE);
		}
	}

	private void RetireButtonPress()
	{
		if (!GameUtils.IsAnyTransitionActive())
		{
			AlertPopup.PopupInfo popupInfo = new AlertPopup.PopupInfo();
			popupInfo.m_headerText = GameStrings.Get("GLUE_FORGE_RETIRE_WARNING_HEADER");
			popupInfo.m_text = GameStrings.Get("GLUE_FORGE_RETIRE_WARNING_DESC");
			popupInfo.m_showAlertIcon = false;
			popupInfo.m_confirmText = GameStrings.Get("GLUE_ARENA_RETIRE_POPUP_YES");
			popupInfo.m_responseDisplay = AlertPopup.ResponseDisplay.CONFIRM_CANCEL;
			popupInfo.m_responseCallback = OnRetirePopupResponse;
			DialogManager.Get().ShowPopup(popupInfo);
		}
	}

	private void OnRetirePopupResponse(AlertPopup.Response response, object userData)
	{
		if (response != AlertPopup.Response.CANCEL)
		{
			AnimateRewards();
			Network.Get().DraftRetire(m_draftManager.GetDraftDeck().ID, m_draftManager.GetSlot(), m_draftManager.CurrentSeasonId, m_draftManager.IsUnderground());
		}
	}

	private void OnRevertPopupResponse(AlertPopup.Response response, object userData)
	{
		if (response != AlertPopup.Response.CANCEL)
		{
			RevertDraftDeck();
		}
	}

	private void RevertDraftDeck()
	{
		m_draftManager.RevertDraftDeck(m_draftDeckTrays);
		RevertManaCurve(m_draftManager.GetBaseDraftDeck());
		PopulateArenaRedraftCollection();
	}

	private void OnEditDeckButtonReleased()
	{
		if (m_arenaDraftScreen == null || m_draftManager == null)
		{
			return;
		}
		bool isUnderground = m_draftManager.IsUnderground();
		if (m_draftManager.GetArenaState(isUnderground) != ArenaSessionState.EDITING_DECK)
		{
			GoToArenaMidrun();
			return;
		}
		if (m_deckCount != m_maxDeckCount)
		{
			AlertPopup.PopupInfo popupInfo = new AlertPopup.PopupInfo();
			popupInfo.m_headerText = GameStrings.Get("GLUE_FORGE_REVERT_HEADER");
			popupInfo.m_text = GameStrings.Get("GLUE_FORGE_REVERT_DESC");
			popupInfo.m_confirmText = GameStrings.Get("GLUE_ARENA_REVERT_BUTTON");
			popupInfo.m_cancelText = GameStrings.Get("GLUE_ARENA_KEEPEDITING_BUTTON");
			popupInfo.m_showAlertIcon = false;
			popupInfo.m_responseDisplay = AlertPopup.ResponseDisplay.CONFIRM_CANCEL;
			popupInfo.m_responseCallback = OnRevertPopupResponse;
			DialogManager.Get().ShowPopup(popupInfo);
			return;
		}
		CollectionDeckTray.SaveArenaDeck(m_draftManager.GetDraftDeck());
		foreach (DraftPhoneDeckTray draftDeckTray in m_draftDeckTrays)
		{
			(draftDeckTray.GetCardsContent() as ArenaDeckTrayCardListContent).ClearAllRedraftTags();
		}
		DraftManager.Get().OnRedraftEnd();
		m_draftManager.SetArenaState(ArenaSessionState.MIDRUN, isUnderground);
		GoToArenaMidrun();
	}

	private void OnViewDeckButtonReleased()
	{
		if (!(m_arenaDraftScreen == null) && m_draftManager != null && !GameUtils.IsAnyTransitionActive())
		{
			GoToArenaCollection();
			m_draftManager.SetClientState(m_draftManager.IsUnderground() ? ArenaClientStateType.Underground_DeckCollection : ArenaClientStateType.Normal_DeckCollection);
		}
	}

	private void OnStartViewDeck()
	{
		if (!(m_arenaDraftScreen == null) && m_draftManager != null)
		{
			GoToArenaCollection();
			m_draftManager.SetClientState(m_draftManager.IsUnderground() ? ArenaClientStateType.Underground_DeckCollection : ArenaClientStateType.Normal_DeckCollection);
		}
	}

	private void ManaCurveOver(UIEvent e)
	{
		DraftManaCurve component = e.GetElement().GetComponent<DraftManaCurve>();
		if (component != null && component.isActiveAndEnabled)
		{
			component.GetComponent<TooltipZone>().ShowTooltip(GameStrings.Get("GLUE_FORGE_MANATIP_HEADER"), GameStrings.Get("GLUE_FORGE_MANATIP_DESC"), TooltipPanel.FORGE_SCALE);
		}
	}

	private void ManaCurveOut(UIEvent e)
	{
		DraftManaCurve component = e.GetElement().GetComponent<DraftManaCurve>();
		if (component != null)
		{
			component.GetComponent<TooltipZone>().HideTooltip();
		}
	}

	private void DeckHeaderOver(UIEvent e)
	{
		int num = (UniversalInputManager.UsePhoneUI ? 1 : 0);
		if (num < m_draftDeckTrays.Count)
		{
			m_draftDeckTrays[num].GetTooltipZone().ShowTooltip(GameStrings.Get("GLUE_ARENA_DECK_TOOLTIP_HEADER"), GameStrings.Get("GLUE_ARENA_DECK_TOOLTIP"), TooltipPanel.FORGE_SCALE);
		}
	}

	private void DeckHeaderOut(UIEvent e)
	{
		int num = (UniversalInputManager.UsePhoneUI ? 1 : 0);
		if (num < m_draftDeckTrays.Count)
		{
			m_draftDeckTrays[num].GetTooltipZone().HideTooltip();
		}
	}

	private void OnScenePreUnload(SceneMgr.Mode prevMode, PegasusScene prevScene, object userData)
	{
		if (prevMode == SceneMgr.Mode.DRAFT)
		{
			DialogManager.Get().RemoveUniquePopupRequestFromQueue("arena_first_time");
			if (m_firstTimeDialog != null)
			{
				m_firstTimeDialog.Hide();
			}
			if (IsInHeroSelectMode())
			{
				m_zoomedHero.gameObject.SetActive(value: false);
				m_heroPower.gameObject.SetActive(value: false);
				m_confirmButton.gameObject.SetActive(value: false);
				UniversalInputManager.Get().SetGameDialogActive(active: false);
			}
		}
	}

	private void OnDraftDeckInitialized(CollectionDeck draftDeck)
	{
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			OnCardCountUpdated(draftDeck.GetTotalCardCount(), draftDeck.GetMaxCardCount());
			if (0 < m_draftDeckTrays.Count)
			{
				DraftPhoneDeckTray draftPhoneDeckTray = m_draftDeckTrays[0];
				if (draftPhoneDeckTray != null)
				{
					draftPhoneDeckTray.GetCardsContent().UnregisterCardCountUpdated(OnCardCountUpdated);
					draftPhoneDeckTray.GetCardsContent().RegisterCardCountUpdated(OnCardCountUpdated);
				}
			}
		}
		InitManaCurve();
	}
}
