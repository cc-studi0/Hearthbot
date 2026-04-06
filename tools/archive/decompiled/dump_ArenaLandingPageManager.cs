using System;
using System.Collections;
using System.Collections.Generic;
using Assets;
using Blizzard.T5.Services;
using Hearthstone.DataModels;
using Hearthstone.UI;
using PegasusShared;
using PegasusUtil;
using UnityEngine;

public class ArenaLandingPageManager : MonoBehaviour
{
	private const string CODE_START_NORMAL_ARENA = "START_NORMAL_ARENA";

	private const string CODE_START_UNDERGROUND_ARENA = "START_UNDERGROUND_ARENA";

	private const string CODE_UNLOCK_UNDERGROUND = "UNLOCK_UNDERGROUND";

	private const string CODE_DRAFTING = "DRAFTING";

	private const string CODE_MOVE_TO_DRAFTING = "MOVETO_DRAFTING";

	private const string CODE_MOVE_TO_DECK = "MOVETO_DECK";

	private const string CODE_MOVE_TO_LANDING = "MOVETO_LANDING";

	private const string CODE_ARENA_TOGGLE_CLICKED = "CODE_ARENA_TOGGLE_CLICKED";

	private const string CODE_FIND_GAME = "CODE_FIND_GAME";

	private const string CODE_FTUX_RUN_NORMAL = "RUN_FTUE";

	private const string CODE_FTUX_RUN_UNDERGROUND = "RUN_FTUE_UG";

	private const string CODE_FTUX_RUN_RETURNING = "RUN_RETURNING";

	private const string CODE_WIN_STREAK = "WIN_STREAK";

	private const string CODE_EXIT_PRESSED = "EXIT_ARENA";

	private const string CODE_SPEND_TICKET = "SPEND_TICKET";

	private const string CODE_REWARD_INFO_BUTTON_CLICKED = "REWARD_INFO_BUTTON_CLICKED";

	private static ArenaLandingPageManager s_instance;

	private Widget m_widget;

	private DraftManager m_draftManager;

	private ArenaLandingPageDataModel m_arenaLandingPageDataModel;

	private ArenaLandingPageModeDataModel m_arenaNormalLandingPageDataModel;

	private ArenaLandingPageModeDataModel m_arenaUndergroundLandingPageDataModel;

	private ArenaRewardsDataModel m_arenaRewardsDataModel;

	private ArenaSessionDataModel m_normalSessionDataModel;

	private ArenaSessionDataModel m_undergroundSessionDataModel;

	private ArenaLandingPage m_normalLandingPage;

	private ArenaLandingPage m_undergroundLandingPage;

	[SerializeField]
	private WidgetInstance m_normalArenaInstancePC;

	[SerializeField]
	private WidgetInstance m_normalArenaInstancePhone;

	private WidgetInstance m_normalArenaWidget;

	[SerializeField]
	private WidgetInstance m_undergroundArenaInstancePC;

	[SerializeField]
	private WidgetInstance m_undergroundArenaInstancePhone;

	private WidgetInstance m_undergroundArenaWidget;

	[SerializeField]
	private WidgetInstance m_draftCollectionInstancePC;

	[SerializeField]
	private WidgetInstance m_draftCollectionInstancePhone;

	private WidgetInstance m_draftCollectionWidget;

	[SerializeField]
	private WidgetInstance m_modeToggleButtonPC;

	[SerializeField]
	private WidgetInstance m_modeToggleButtonPhone;

	private WidgetInstance m_modeToggleButtonWidget;

	private ArenaModeToggleButton m_modeToggleButton;

	[SerializeField]
	private WidgetInstance m_arenaRewardListPopupWidget;

	[SerializeField]
	private TavernTicketDisplay m_tavernTicketDisplay;

	[SerializeField]
	private WidgetInstance m_infoButtonWidget;

	[SerializeField]
	private WidgetInstance m_backButtonWidget;

	[SerializeField]
	private GameObject[] m_enableObjectsOnBoxTransitionFinish;

	private Action OnMoveFromLandingCallback;

	private Action OnMoveToDeckCallback;

	private bool m_isNormalWidgetLoaded;

	private bool m_isUndergroundWidgetLoaded;

	private bool m_isWidgetDoneInitialStateChanging;

	private bool m_loadingComplete;

	private Coroutine m_FTUECoroutine;

	private void UpdateArenaSessionDataModel(ArenaSessionDataModel dataModel, bool isUnderground)
	{
		dataModel.is_underground = isUnderground;
		dataModel.is_session_active = m_draftManager.HasCurrentlyActiveRun(isUnderground);
		dataModel.wins = m_draftManager.GetCurrentWins(isUnderground);
		dataModel.losses = m_draftManager.GetCurrentLosses(isUnderground);
		bool flag = false;
		if (dataModel.is_session_active)
		{
			CollectionDeck draftDeck = m_draftManager.GetDraftDeck(isUnderground);
			if (draftDeck != null)
			{
				flag = true;
				dataModel.HeroCard.CardId = draftDeck.HeroCardID;
			}
		}
		if (!flag)
		{
			dataModel.HeroCard.CardId = string.Empty;
		}
	}

	private void OnArenaSessionUpdated(bool isUnderground)
	{
		UpdateArenaSessionDataModel(isUnderground ? m_undergroundSessionDataModel : m_normalSessionDataModel, isUnderground);
		UpdateArenaLandingPageModeViewModels();
	}

	public void Awake()
	{
		if (s_instance == null)
		{
			s_instance = this;
		}
		m_draftManager = DraftManager.Get();
		m_widget = GetComponent<Widget>();
		m_normalArenaWidget = (UniversalInputManager.UsePhoneUI ? m_normalArenaInstancePhone : m_normalArenaInstancePC);
		m_undergroundArenaWidget = (UniversalInputManager.UsePhoneUI ? m_undergroundArenaInstancePhone : m_undergroundArenaInstancePC);
		m_draftCollectionWidget = (UniversalInputManager.UsePhoneUI ? m_draftCollectionInstancePhone : m_draftCollectionInstancePC);
		m_modeToggleButtonWidget = (UniversalInputManager.UsePhoneUI ? m_modeToggleButtonPhone : m_modeToggleButtonPC);
		m_arenaLandingPageDataModel = new ArenaLandingPageDataModel();
		m_arenaLandingPageDataModel.arenaSeasonId = m_draftManager.GetArenaSeasonId();
		m_arenaLandingPageDataModel.isUndergroundUnlocked = GameModeUtils.HasUnlockedMode(Global.UnlockableGameMode.UNDERGROUND_ARENA);
		m_arenaNormalLandingPageDataModel = new ArenaLandingPageModeDataModel();
		m_arenaUndergroundLandingPageDataModel = new ArenaLandingPageModeDataModel();
		UpdateArenaLandingPageModeViewModels();
		m_arenaRewardsDataModel = new ArenaRewardsDataModel();
		m_normalSessionDataModel = new ArenaSessionDataModel
		{
			HeroCard = new CardDataModel()
		};
		m_undergroundSessionDataModel = new ArenaSessionDataModel
		{
			HeroCard = new CardDataModel()
		};
		UpdateArenaSessionDataModel(m_normalSessionDataModel, isUnderground: false);
		UpdateArenaSessionDataModel(m_undergroundSessionDataModel, isUnderground: true);
		if (m_draftManager != null)
		{
			if (m_draftManager.IsUnderground())
			{
				StartUnderground();
			}
			else
			{
				StartNormal();
			}
		}
		PopulateArenaRewardsDataModel();
		if (m_widget != null)
		{
			m_widget.RegisterReadyListener(delegate
			{
				m_widget.RegisterDoneChangingStatesListener(delegate
				{
					m_isWidgetDoneInitialStateChanging = true;
					if (!m_draftManager.IsUnderground() && m_FTUECoroutine == null)
					{
						m_FTUECoroutine = StartCoroutine(CheckForFTUXPopup());
					}
					CheckForWinStreak();
				}, null, callImmediatelyIfSet: true, doOnce: true);
				if (m_arenaLandingPageDataModel != null)
				{
					m_widget.BindDataModel(m_arenaLandingPageDataModel);
					m_widget.BindDataModel(m_draftManager.IsUnderground() ? m_undergroundSessionDataModel : m_normalSessionDataModel);
				}
			});
		}
		if (m_normalArenaWidget != null)
		{
			m_normalArenaWidget.RegisterReadyListener(delegate
			{
				m_normalArenaWidget.BindDataModel(m_arenaNormalLandingPageDataModel);
				m_isNormalWidgetLoaded = true;
				ArenaLandingPage componentInChildren = m_normalArenaWidget.GetComponentInChildren<ArenaLandingPage>();
				if (componentInChildren != null)
				{
					m_normalLandingPage = componentInChildren;
					componentInChildren.IsUnderground = false;
				}
				m_normalArenaWidget.BindDataModel(m_normalSessionDataModel);
			});
		}
		if (m_undergroundArenaWidget != null)
		{
			m_undergroundArenaWidget.RegisterReadyListener(delegate
			{
				m_undergroundArenaWidget.BindDataModel(m_arenaUndergroundLandingPageDataModel);
				m_isUndergroundWidgetLoaded = true;
				ArenaLandingPage componentInChildren = m_undergroundArenaWidget.GetComponentInChildren<ArenaLandingPage>();
				if (componentInChildren != null)
				{
					m_undergroundLandingPage = componentInChildren;
					componentInChildren.IsUnderground = true;
				}
				m_undergroundArenaWidget.BindDataModel(m_undergroundSessionDataModel);
			});
		}
		if (m_draftCollectionWidget != null)
		{
			m_draftCollectionWidget.RegisterReadyListener(delegate
			{
			});
		}
		if (m_arenaRewardListPopupWidget != null)
		{
			m_arenaRewardListPopupWidget.RegisterReadyListener(delegate
			{
				m_widget.BindDataModel(m_arenaRewardsDataModel);
			});
		}
		if (m_modeToggleButtonWidget != null)
		{
			m_modeToggleButtonWidget.RegisterReadyListener(delegate
			{
				m_modeToggleButton = m_modeToggleButtonWidget.GetComponentInChildren<ArenaModeToggleButton>();
			});
		}
		m_draftManager.OnArenaSessionUpdated += OnArenaSessionUpdated;
		CollectionManager.Get().RegisterCollectionChangedListener(OnCollectionChanged);
		CosmeticCoinManager.Get().OnCoinCollectionChanged += OnCoinCollectionChanged;
		NetCache.Get().CardBacksChanged += OnCardbackCollectionChanged;
		Box box = Box.Get();
		if (box != null)
		{
			box.AddTransitionFinishedListener(OnBoxFinishedAnim);
		}
	}

	public void UpdateArenaLandingPageModeViewModels()
	{
		m_arenaNormalLandingPageDataModel.inUnderground = false;
		m_arenaNormalLandingPageDataModel.theme = 0;
		m_arenaNormalLandingPageDataModel.ticketCost = 1;
		m_arenaNormalLandingPageDataModel.hasActiveRun = m_draftManager.HasCurrentlyActiveRun(isUnderground: false);
		m_arenaNormalLandingPageDataModel.wins = m_draftManager.GetCurrentWins(isUnderground: false);
		m_arenaNormalLandingPageDataModel.losses = m_draftManager.GetCurrentLosses(isUnderground: false);
		m_arenaNormalLandingPageDataModel.modeMaxWins = m_draftManager.GetMaxWins(isUnderground: false);
		m_arenaNormalLandingPageDataModel.modeMaxLosses = m_draftManager.GetMaxLosses(isUnderground: false);
		m_arenaUndergroundLandingPageDataModel.inUnderground = true;
		m_arenaUndergroundLandingPageDataModel.theme = 1;
		m_arenaUndergroundLandingPageDataModel.ticketCost = 2;
		m_arenaUndergroundLandingPageDataModel.hasActiveRun = m_draftManager.HasCurrentlyActiveRun(isUnderground: true);
		m_arenaUndergroundLandingPageDataModel.wins = m_draftManager.GetCurrentWins(isUnderground: true);
		m_arenaUndergroundLandingPageDataModel.losses = m_draftManager.GetCurrentLosses(isUnderground: true);
		m_arenaUndergroundLandingPageDataModel.modeMaxWins = m_draftManager.GetMaxWins(isUnderground: true);
		m_arenaUndergroundLandingPageDataModel.modeMaxLosses = m_draftManager.GetMaxLosses(isUnderground: true);
	}

	public void OnDestroy()
	{
		if (m_draftManager != null)
		{
			m_draftManager.OnArenaSessionUpdated -= OnArenaSessionUpdated;
		}
		CollectionManager.Get()?.RemoveCollectionChangedListener(OnCollectionChanged);
		if (CosmeticCoinManager.Get() != null)
		{
			CosmeticCoinManager.Get().OnCoinCollectionChanged -= OnCoinCollectionChanged;
		}
		if (NetCache.Get() != null)
		{
			NetCache.Get().CardBacksChanged -= OnCardbackCollectionChanged;
		}
		Box box = Box.Get();
		if (box != null)
		{
			box.RemoveTransitionFinishedListener(OnBoxFinishedAnim);
		}
	}

	private void OnCollectionChanged()
	{
		PopulateArenaRewardsDataModel();
		if (m_widget != null && m_widget.IsReady)
		{
			m_widget.BindDataModel(m_arenaRewardsDataModel);
		}
	}

	private void OnCoinCollectionChanged()
	{
		PopulateArenaRewardsDataModel();
		if (m_widget != null && m_widget.IsReady)
		{
			m_widget.BindDataModel(m_arenaRewardsDataModel);
		}
	}

	private void OnCardbackCollectionChanged()
	{
		PopulateArenaRewardsDataModel();
		if (m_widget != null && m_widget.IsReady)
		{
			m_widget.BindDataModel(m_arenaRewardsDataModel);
		}
	}

	public void Start()
	{
		Navigation.Push(OnNavigateBack);
		NetCache.Get().RegisterScreenForge(OnNetCacheReady);
		if (m_widget != null)
		{
			m_widget.RegisterReadyListener(delegate
			{
				m_widget.RegisterEventListener(HandleArenaLandingPageEvent);
			});
		}
		StartCoroutine(NotifySceneLoadedWhenReady());
	}

	public static ArenaLandingPageManager Get()
	{
		return s_instance;
	}

	public Widget GetWidget()
	{
		return m_widget;
	}

	public bool IsLoadingComplete()
	{
		return m_loadingComplete;
	}

	public void DetermineToggleButtonStartingState()
	{
		if (!(m_modeToggleButtonWidget == null))
		{
			m_modeToggleButtonWidget.RegisterReadyListener(delegate
			{
				m_modeToggleButtonWidget.GetComponentInChildren<ArenaModeToggleButton>()?.DetermineStartingState();
			});
		}
	}

	public void SetOnMoveToDraftingCallback(Action callback)
	{
		OnMoveFromLandingCallback = callback;
	}

	public void SetOnMoveToDeckCallback(Action callback)
	{
		OnMoveToDeckCallback = callback;
	}

	public void UpdateArenaRatingValues(int rating, int undergroundRating)
	{
		if (m_arenaLandingPageDataModel != null)
		{
			m_arenaLandingPageDataModel.rating = rating.ToString();
			m_arenaLandingPageDataModel.undergroundRating = undergroundRating.ToString();
			if (m_arenaNormalLandingPageDataModel != null && m_arenaUndergroundLandingPageDataModel != null)
			{
				m_arenaNormalLandingPageDataModel.rating = rating.ToString();
				m_arenaUndergroundLandingPageDataModel.rating = undergroundRating.ToString();
				UpdateArenaLandingPageModeViewModels();
			}
		}
	}

	public void UpdateArenaSessionState(ArenaSessionState sessionState, bool isUnderground)
	{
		if (m_arenaLandingPageDataModel != null)
		{
			if (isUnderground)
			{
				m_arenaLandingPageDataModel.undergroundSessionState = sessionState;
			}
			else
			{
				m_arenaLandingPageDataModel.normalSessionState = sessionState;
			}
			if (isUnderground && m_arenaUndergroundLandingPageDataModel != null)
			{
				m_arenaUndergroundLandingPageDataModel.sessionState = sessionState;
			}
			else if (m_arenaNormalLandingPageDataModel != null)
			{
				m_arenaNormalLandingPageDataModel.sessionState = sessionState;
			}
		}
	}

	public void UpdateArenaShouldBeDrafting(bool shouldBeDrafting)
	{
		if (m_arenaLandingPageDataModel != null)
		{
			m_arenaLandingPageDataModel.shouldBeDrafting = shouldBeDrafting;
			m_arenaNormalLandingPageDataModel.shouldBeDrafting = shouldBeDrafting;
			m_arenaUndergroundLandingPageDataModel.shouldBeDrafting = shouldBeDrafting;
		}
	}

	public void UpdateArenaTicketCosts(int normalTicketCost, int undergroundTicketCost)
	{
		if (m_arenaNormalLandingPageDataModel != null && m_arenaUndergroundLandingPageDataModel != null)
		{
			m_arenaNormalLandingPageDataModel.ticketCost = normalTicketCost;
			m_arenaUndergroundLandingPageDataModel.ticketCost = undergroundTicketCost;
		}
	}

	private bool OnNavigateBack()
	{
		ExitDraftScene();
		return true;
	}

	private void OnStoreBackButtonPressed(bool authorizationBackButtonPressed, object userData)
	{
		ExitDraftScene();
	}

	private void BackButtonPress()
	{
		Navigation.GoBack();
		ExitDraftScene();
	}

	private void ExitDraftScene()
	{
		SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
		Box.Get().SetToIgnoreFullScreenEffects(ignoreEffects: false);
		if (m_widget != null)
		{
			m_widget.Hide();
		}
	}

	private void OnNetCacheReady()
	{
		NetCache.Get().UnregisterNetCacheHandler(OnNetCacheReady);
		if (!NetCache.Get().GetNetObject<NetCache.NetCacheFeatures>().Games.Forge && !SceneMgr.Get().IsModeRequested(SceneMgr.Mode.HUB))
		{
			SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
			Error.AddWarningLoc("GLOBAL_FEATURE_DISABLED_TITLE", "GLOBAL_FEATURE_DISABLED_MESSAGE_FORGE");
		}
	}

	private void SpendTicket()
	{
		if (m_draftManager != null)
		{
			int numTicketsOwned = m_draftManager.GetNumTicketsOwned();
			int num = ((!m_draftManager.IsUnderground()) ? 1 : 2);
			if (numTicketsOwned >= num)
			{
				m_widget.TriggerEvent("MOVETO_DRAFTING");
				m_widget.TriggerEvent("DRAFTING");
				m_draftManager.RequestDraftBegin();
			}
			else if (m_tavernTicketDisplay != null)
			{
				m_tavernTicketDisplay.OpenTavernTicketProductPage(arenaPlayButtonClicked: true);
			}
		}
	}

	private void ShowArenaDisclaimer()
	{
		ExternalUrlService externalUrlService = ServiceManager.Get<ExternalUrlService>();
		if (externalUrlService != null)
		{
			Application.OpenURL(externalUrlService.GetArenaDisclaimerLink());
		}
	}

	public void HandleArenaLandingPageEvent(string eventName)
	{
		switch (eventName)
		{
		case "EXIT_ARENA":
			BackButtonPress();
			break;
		case "SPEND_TICKET":
			SpendTicket();
			break;
		case "REWARD_INFO_BUTTON_CLICKED":
			ShowArenaDisclaimer();
			break;
		case "CODE_ARENA_TOGGLE_CLICKED":
			ToggleUndergroundState();
			break;
		case "MOVETO_DRAFTING":
			OnMoveFromLanding();
			break;
		case "MOVETO_DECK":
			OnMoveToDeck();
			break;
		case "CODE_FIND_GAME":
			StartMatchmaking();
			break;
		case "UNLOCK_UNDERGROUND":
			OnUnlockUnderground();
			break;
		}
	}

	private void OnUnlockUnderground()
	{
		if (m_arenaLandingPageDataModel != null)
		{
			m_arenaLandingPageDataModel.isUndergroundUnlocked = true;
		}
	}

	public void ToggleUndergroundState()
	{
		if (GameModeUtils.HasUnlockedMode(Global.UnlockableGameMode.UNDERGROUND_ARENA))
		{
			bool flag = !m_draftManager.IsUnderground();
			m_draftManager.SetUndergroundState(flag);
			if (m_arenaLandingPageDataModel != null)
			{
				m_arenaLandingPageDataModel.inUnderground = flag;
			}
			UnhideLandingPage(flag);
			if (m_widget != null)
			{
				m_widget.TriggerEvent("ARENA_TOGGLE_CLICKED", new TriggerEventParameters(null, flag));
				m_widget.BindDataModel(flag ? m_undergroundSessionDataModel : m_normalSessionDataModel);
			}
			Network.Get().RequestDraftChoicesAndContents(m_draftManager.IsUnderground());
			if (m_draftManager.IsUnderground())
			{
				MusicManager.Get().StartPlaylist(MusicPlaylistType.UI_ArenaUnderground);
			}
			else
			{
				MusicManager.Get().StartPlaylist(MusicPlaylistType.UI_Arena);
			}
			CheckForWinStreak();
			if (m_FTUECoroutine == null)
			{
				m_FTUECoroutine = StartCoroutine(CheckForFTUXPopup());
			}
			DraftDisplay draftDisplay = DraftDisplay.Get();
			if (draftDisplay != null)
			{
				draftDisplay.ShowRewardsIfNecessary();
			}
		}
	}

	public void OnMoveFromLanding()
	{
		OnMoveFromLandingCallback?.Invoke();
	}

	public void OnMoveToDeck()
	{
		OnMoveToDeckCallback?.Invoke();
	}

	private void StartMatchmaking()
	{
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (!(draftDisplay != null) || !draftDisplay.IsRewardsFlowPending)
		{
			m_draftManager.FindGame();
			SetUIButtonsEnabled(enabled: false);
			PresenceMgr.Get().SetStatus(Global.PresenceStatus.ARENA_QUEUE);
		}
	}

	public void MatchmakingCancled()
	{
		SetUIButtonsEnabled(enabled: true);
	}

	public void SetUIButtonsEnabled(bool enabled)
	{
		SetLandingPagePlayButtonEnabledState(enabled);
		SetModeToggleButtonEnabled(enabled);
		SetBackButtonEnabled(enabled);
		SetInfoButtonEnabled(enabled);
	}

	private void SetLandingPagePlayButtonEnabledState(bool enabled)
	{
		ArenaLandingPage arenaLandingPage = (m_draftManager.IsUnderground() ? m_undergroundLandingPage : m_normalLandingPage);
		if (arenaLandingPage != null)
		{
			arenaLandingPage.SetActiveRunPlayButtonEnabledState(enabled);
		}
	}

	private void SetModeToggleButtonEnabled(bool enabled)
	{
		if (!(m_modeToggleButtonWidget != null))
		{
			return;
		}
		m_modeToggleButtonWidget.RegisterDoneChangingStatesListener(delegate
		{
			Clickable componentInChildren = m_modeToggleButtonWidget.GetComponentInChildren<Clickable>();
			if (componentInChildren != null)
			{
				componentInChildren.enabled = enabled;
			}
		});
	}

	private void SetBackButtonEnabled(bool enabled)
	{
		if (!(m_backButtonWidget != null))
		{
			return;
		}
		m_backButtonWidget.RegisterReadyListener(delegate
		{
			Clickable componentInChildren = m_backButtonWidget.GetComponentInChildren<Clickable>();
			if (componentInChildren != null)
			{
				componentInChildren.enabled = enabled;
			}
		});
	}

	private void SetInfoButtonEnabled(bool enabled)
	{
		if (!(m_infoButtonWidget != null))
		{
			return;
		}
		m_infoButtonWidget.RegisterReadyListener(delegate
		{
			Clickable componentInChildren = m_infoButtonWidget.GetComponentInChildren<Clickable>();
			if (componentInChildren != null)
			{
				componentInChildren.enabled = enabled;
			}
		});
	}

	public void StartNormal()
	{
		if (m_draftCollectionWidget != null)
		{
			m_draftCollectionWidget.RegisterReadyListener(delegate
			{
				if (m_widget != null)
				{
					m_widget.TriggerEvent("START_NORMAL_ARENA");
				}
			});
		}
		if (m_arenaLandingPageDataModel != null)
		{
			m_arenaLandingPageDataModel.inUnderground = false;
		}
	}

	public void StartUnderground()
	{
		if (m_draftCollectionWidget != null)
		{
			m_draftCollectionWidget.RegisterReadyListener(delegate
			{
				if (m_widget != null)
				{
					m_widget.TriggerEvent("START_UNDERGROUND_ARENA");
				}
			});
		}
		if (m_arenaLandingPageDataModel != null)
		{
			m_arenaLandingPageDataModel.inUnderground = true;
		}
	}

	private static TAG_RARITY ArenaRewardRarityToTagRarity(ArenaRewardCardRarity inRarity)
	{
		return inRarity switch
		{
			ArenaRewardCardRarity.COMMON => TAG_RARITY.COMMON, 
			ArenaRewardCardRarity.RARE => TAG_RARITY.RARE, 
			ArenaRewardCardRarity.EPIC => TAG_RARITY.EPIC, 
			ArenaRewardCardRarity.LEGENDARY => TAG_RARITY.LEGENDARY, 
			_ => TAG_RARITY.INVALID, 
		};
	}

	private static bool IsSpecificCardRewardValid(string cardId, TAG_PREMIUM premium, int quantity)
	{
		EntityDef entityDef = DefLoader.Get().GetEntityDef(cardId);
		if (entityDef == null)
		{
			return false;
		}
		CollectionManager.Get().GetOwnedCardCount(cardId, out var normal, out var golden, out var signature, out var diamond);
		int num = quantity;
		switch (premium)
		{
		case TAG_PREMIUM.NORMAL:
			num += normal;
			break;
		case TAG_PREMIUM.GOLDEN:
			num += golden;
			break;
		case TAG_PREMIUM.DIAMOND:
			num += diamond;
			break;
		case TAG_PREMIUM.SIGNATURE:
			num += signature;
			break;
		default:
			Debug.LogError("Unhandled card premium " + premium.ToString() + " found while validating specific card rewrad for card ID = " + cardId);
			break;
		}
		int num2 = (entityDef.IsElite() ? 1 : 2);
		return num <= num2;
	}

	private static bool IsCosmeticCoinRewardValid(string coinCardId)
	{
		return !CosmeticCoinManager.Get().IsOwnedCoinCard(coinCardId);
	}

	private static bool IsHeroSkinRewardValid(string heroSkinCardId)
	{
		return CollectionManager.Get().GetTotalNumCopiesInCollection(heroSkinCardId) == 0;
	}

	private static bool IsCardBackRewardValid(int cardBackId)
	{
		return !CardBackManager.Get().IsCardBackOwned(cardBackId);
	}

	private static void PopulateRewardsSlotDataModel(ArenaRewardsSlotDataModel rewardSlotDataModel, ArenaRewardSlot rewardSlot)
	{
		foreach (ArenaRewardSpecificBoosterSlot specificBoosterReward in rewardSlot.SpecificBoosterRewards)
		{
			rewardSlotDataModel.rewards.Add(RewardUtils.RewardDataToRewardItemDataModel(new BoosterPackRewardData(specificBoosterReward.BoosterType, specificBoosterReward.MinBoosterCount)));
		}
		foreach (ArenaRewardRandomCard randomCardReward in rewardSlot.RandomCardRewards)
		{
			RewardItemDataModel item = new RewardItemDataModel
			{
				AssetId = 0,
				Quantity = 1,
				ItemType = RewardItemType.RANDOM_CARD,
				ItemId = randomCardReward.BoosterCardSet,
				RandomCard = new RandomCardDataModel
				{
					Premium = (TAG_PREMIUM)randomCardReward.Premium,
					Rarity = ArenaRewardRarityToTagRarity(randomCardReward.Rarity),
					Count = randomCardReward.Quantity
				}
			};
			rewardSlotDataModel.rewards.Add(item);
		}
		foreach (ArenaRewardSpecificCard specificCardReward in rewardSlot.SpecificCardRewards)
		{
			string text = GameUtils.TranslateDbIdToCardId(specificCardReward.Card.Asset);
			TAG_PREMIUM premium = (TAG_PREMIUM)specificCardReward.Card.Premium;
			if (IsSpecificCardRewardValid(text, premium, specificCardReward.Quantity))
			{
				rewardSlotDataModel.rewards.Add(RewardUtils.RewardDataToRewardItemDataModel(new CardRewardData(text, premium, specificCardReward.Quantity)));
			}
		}
		foreach (int cosmeticCoinReward in rewardSlot.CosmeticCoinRewards)
		{
			CosmeticCoinDbfRecord record = GameDbf.CosmeticCoin.GetRecord(cosmeticCoinReward);
			if (record != null)
			{
				string text2 = GameUtils.TranslateDbIdToCardId(record.CardId);
				if (IsCosmeticCoinRewardValid(text2))
				{
					rewardSlotDataModel.rewards.Add(RewardUtils.RewardDataToRewardItemDataModel(new CardRewardData(text2, TAG_PREMIUM.NORMAL, 1)));
				}
			}
		}
		foreach (ArenaRewardCurrencySlot currencyReward in rewardSlot.CurrencyRewards)
		{
			switch (currencyReward.CurrencyType)
			{
			case ArenaRewardCurrencyType.GOLD:
				rewardSlotDataModel.rewards.Add(RewardUtils.RewardDataToRewardItemDataModel(new GoldRewardData(currencyReward.MinAmount)));
				break;
			case ArenaRewardCurrencyType.DUST:
				rewardSlotDataModel.rewards.Add(RewardUtils.RewardDataToRewardItemDataModel(new ArcaneDustRewardData(currencyReward.MinAmount)));
				break;
			case ArenaRewardCurrencyType.TAVERN_TICKET:
			{
				RewardItemDataModel rewardItemDataModel = new RewardItemDataModel();
				rewardItemDataModel.ItemType = RewardItemType.ARENA_TICKET;
				rewardItemDataModel.Quantity = currencyReward.MinAmount;
				rewardSlotDataModel.rewards.Add(rewardItemDataModel);
				break;
			}
			default:
				Debug.LogError($"Unknown currency type {currencyReward.CurrencyType} found in arnea reward slot");
				break;
			}
		}
		foreach (int heroSkinReward in rewardSlot.HeroSkinRewards)
		{
			if (IsHeroSkinRewardValid(GameUtils.TranslateDbIdToCardId(heroSkinReward)))
			{
				RewardItemDataModel item2 = new RewardItemDataModel
				{
					ItemType = RewardItemType.HERO_SKIN,
					ItemId = heroSkinReward
				};
				RewardUtils.InitializeRewardItemDataModel(item2, TAG_RARITY.INVALID, TAG_PREMIUM.NORMAL, out var _);
				rewardSlotDataModel.rewards.Add(item2);
			}
		}
		foreach (int cardBackReward in rewardSlot.CardBackRewards)
		{
			if (IsCardBackRewardValid(cardBackReward))
			{
				rewardSlotDataModel.rewards.Add(RewardUtils.RewardDataToRewardItemDataModel(new CardBackRewardData(cardBackReward)));
			}
		}
	}

	private static void PopulateArenaRewardLevelFromNetworkLevel(ArenaRewardLevel networkRewardLevel, ArenaRewardsRowDataModel arenaRewardsRowDataModel)
	{
		arenaRewardsRowDataModel.wins = networkRewardLevel.Wins;
		arenaRewardsRowDataModel.losses = networkRewardLevel.Losses;
		arenaRewardsRowDataModel.rewards = new DataModelList<ArenaRewardsSlotDataModel>();
		arenaRewardsRowDataModel.crowdsFavorChance = networkRewardLevel.CrowdsFavorChance * 100f;
		foreach (ArenaRewardSlot reward in networkRewardLevel.Rewards)
		{
			ArenaRewardsSlotDataModel arenaRewardsSlotDataModel = new ArenaRewardsSlotDataModel();
			arenaRewardsSlotDataModel.rewards = new DataModelList<RewardItemDataModel>();
			PopulateRewardsSlotDataModel(arenaRewardsSlotDataModel, reward);
			if (arenaRewardsSlotDataModel.rewards.Count == 0 && reward.HasFallbackReward)
			{
				ArenaRewardSlot fallbackReward = reward.FallbackReward;
				PopulateRewardsSlotDataModel(arenaRewardsSlotDataModel, fallbackReward);
			}
			arenaRewardsRowDataModel.rewards.Add(arenaRewardsSlotDataModel);
		}
	}

	private static void PopulateArenaRewardsRowsFromNetworkRewards(List<ArenaRewardLevel> networkRewardLevels, ArenaRewardsRowsListDataModel arenaRewardsRowsDataModel)
	{
		foreach (ArenaRewardLevel networkRewardLevel in networkRewardLevels)
		{
			ArenaRewardsRowDataModel arenaRewardsRowDataModel = new ArenaRewardsRowDataModel();
			PopulateArenaRewardLevelFromNetworkLevel(networkRewardLevel, arenaRewardsRowDataModel);
			arenaRewardsRowsDataModel.rows.Add(arenaRewardsRowDataModel);
		}
		arenaRewardsRowsDataModel.rows.Sort((ArenaRewardsRowDataModel lhs, ArenaRewardsRowDataModel rhs) => (lhs.wins == rhs.wins) ? (rhs.losses - lhs.losses) : (lhs.wins - rhs.wins));
	}

	private void PopulateArenaRewardsDataModel()
	{
		ArenaSeasonInfo currentSeason = DraftManager.Get().CurrentSeason;
		m_arenaRewardsDataModel.NormalRewards = new ArenaRewardsRowsListDataModel
		{
			rows = new DataModelList<ArenaRewardsRowDataModel>()
		};
		PopulateArenaRewardsRowsFromNetworkRewards(currentSeason.NormalRewards, m_arenaRewardsDataModel.NormalRewards);
		m_arenaRewardsDataModel.NormalCrowdsFavorRewards = new ArenaRewardsRowsListDataModel
		{
			rows = new DataModelList<ArenaRewardsRowDataModel>()
		};
		PopulateArenaRewardsRowsFromNetworkRewards(currentSeason.NormalCrowdsFavorRewards, m_arenaRewardsDataModel.NormalCrowdsFavorRewards);
		m_arenaRewardsDataModel.UndergroundRewards = new ArenaRewardsRowsListDataModel
		{
			rows = new DataModelList<ArenaRewardsRowDataModel>()
		};
		PopulateArenaRewardsRowsFromNetworkRewards(currentSeason.UndergroundRewards, m_arenaRewardsDataModel.UndergroundRewards);
		m_arenaRewardsDataModel.UndergroundCrowdsFavorRewards = new ArenaRewardsRowsListDataModel
		{
			rows = new DataModelList<ArenaRewardsRowDataModel>()
		};
		PopulateArenaRewardsRowsFromNetworkRewards(currentSeason.UndergroundCrowdsFavorRewards, m_arenaRewardsDataModel.UndergroundCrowdsFavorRewards);
		m_arenaRewardsDataModel.CrowdsFavorRewards = new ArenaRewardsRowDataModel();
		if (currentSeason.CrowdsFavorRewards != null && currentSeason.CrowdsFavorRewards.Rewards != null && currentSeason.CrowdsFavorRewards.Rewards.Count > 0)
		{
			PopulateArenaRewardLevelFromNetworkLevel(currentSeason.CrowdsFavorRewards, m_arenaRewardsDataModel.CrowdsFavorRewards);
		}
	}

	public IEnumerator CheckForFTUXPopup()
	{
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (draftDisplay == null)
		{
			m_FTUECoroutine = null;
			yield break;
		}
		while (draftDisplay.IsRewardsFlowPending)
		{
			yield return null;
		}
		bool flag = Options.Get().GetBool(Option.HAS_SEEN_FORGE_REVAMP_UNDERGROUND, defaultVal: false);
		if (m_draftManager.IsUnderground() && !flag)
		{
			m_undergroundArenaWidget.TriggerEvent("RUN_FTUE_UG");
			Options.Get().SetBool(Option.HAS_SEEN_FORGE_REVAMP_UNDERGROUND, val: true);
			m_FTUECoroutine = null;
			yield break;
		}
		if (Options.Get().GetBool(Option.HAS_SEEN_FORGE_REVAMP, defaultVal: false))
		{
			m_FTUECoroutine = null;
			yield break;
		}
		if (Options.Get().GetBool(Option.HAS_SEEN_FORGE, defaultVal: false))
		{
			m_normalArenaWidget.TriggerEvent("RUN_RETURNING");
		}
		else
		{
			m_normalArenaWidget.TriggerEvent("RUN_FTUE");
		}
		Options.Get().SetBool(Option.HAS_SEEN_FORGE_REVAMP, val: true);
		Options.Get().SetBool(Option.HAS_SEEN_FORGE, val: true);
		m_FTUECoroutine = null;
	}

	public void CheckForWinStreak()
	{
		if (m_draftManager.GetLosses() == 0 && m_draftManager.GetWins() >= 1)
		{
			if (m_draftManager.IsUnderground())
			{
				m_undergroundArenaWidget.TriggerEvent("WIN_STREAK");
			}
			else
			{
				m_normalArenaWidget.TriggerEvent("WIN_STREAK");
			}
		}
	}

	public void HideInactiveLandingPage()
	{
		if (m_draftManager.IsUnderground())
		{
			m_normalArenaWidget.Hide();
		}
		else
		{
			m_undergroundArenaWidget.Hide();
		}
	}

	public void UnhideLandingPage(bool isUnderground)
	{
		if (isUnderground)
		{
			m_undergroundArenaWidget.Show();
		}
		else
		{
			m_normalArenaWidget.Show();
		}
	}

	private IEnumerator NotifySceneLoadedWhenReady()
	{
		while (!m_isNormalWidgetLoaded)
		{
			yield return null;
		}
		while (!m_isUndergroundWidgetLoaded)
		{
			yield return null;
		}
		while (!m_isWidgetDoneInitialStateChanging)
		{
			yield return null;
		}
		m_loadingComplete = true;
	}

	private void OnBoxFinishedAnim(object unused)
	{
		GameObject[] enableObjectsOnBoxTransitionFinish = m_enableObjectsOnBoxTransitionFinish;
		foreach (GameObject gameObject in enableObjectsOnBoxTransitionFinish)
		{
			if (!(gameObject == null))
			{
				gameObject.SetActive(value: true);
			}
		}
	}
}
