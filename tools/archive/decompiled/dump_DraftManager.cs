using System;
using System.Collections.Generic;
using System.Linq;
using Blizzard.T5.Jobs;
using Blizzard.T5.Services;
using Hearthstone;
using PegasusClient;
using PegasusShared;
using PegasusUtil;
using UnityEngine;

public class DraftManager : IService
{
	public delegate void DraftDeckSet(CollectionDeck deck);

	public delegate void ArenaSessionUpdated(bool isUnderground);

	public delegate void ArenaClientStateUpdated(ArenaClientStateType state);

	private CollectionDeck m_draftDeck = CreateBlankCollectionDeck(0L);

	private CollectionDeck m_baseDraftDeck = CreateBlankCollectionDeck(0L);

	private CollectionDeck m_undergroundDraftDeck = CreateBlankCollectionDeck(0L);

	private CollectionDeck m_baseUndergroundDraftDeck = CreateBlankCollectionDeck(0L);

	private CollectionDeck m_redraftDeck = CreateBlankCollectionDeck(0L);

	private CollectionDeck m_baseRedraftDeck = CreateBlankCollectionDeck(0L);

	private CollectionDeck m_undergroundRedraftDeck = CreateBlankCollectionDeck(0L);

	private CollectionDeck m_baseUndergroundRedraftDeck = CreateBlankCollectionDeck(0L);

	private bool m_hasReceivedSessionWinsLosses;

	private int m_currentSlot;

	private int m_currentUndergroundSlot;

	private int m_currentRedraftSlot;

	private int m_currentUndergroundRedraftSlot;

	private DraftSlotType m_currentSlotType;

	private DraftSlotType m_currentUndergroundSlotType;

	private List<DraftSlotType> m_uniqueDraftSlotTypesForDeck = new List<DraftSlotType>();

	private List<DraftSlotType> m_uniqueDraftSlotTypesForUndergroundDeck = new List<DraftSlotType>();

	private int m_maxSlot;

	private int m_maxUndergroundSlot;

	private int m_maxRedraftSlot;

	private int m_losses;

	private int m_wins;

	private int m_undergroundLosses;

	private int m_undergroundWins;

	private int m_maxWins = int.MaxValue;

	private int m_maxLosses = int.MaxValue;

	private int m_maxUndergroundWins = int.MaxValue;

	private int m_maxUndergroundLosses = int.MaxValue;

	private bool m_deckActiveDuringSession;

	private Network.RewardChest m_chest;

	private bool m_inRewards;

	private bool m_isCurrentRewardUnderground;

	private ArenaSession m_currentNormalSession;

	private ArenaSession m_currentUndergroundSession;

	private bool m_isCurrentRewardCrowdsFavor;

	private List<DraftDeckSet> m_draftDeckSetListeners = new List<DraftDeckSet>();

	private List<DraftDeckSet> m_redraftDeckSetListeners = new List<DraftDeckSet>();

	private bool m_pendingRequestToDisablePremiums;

	private ArenaSessionState m_normalSessionState = ArenaSessionState.INVALID;

	private ArenaSessionState m_undergroundSessionState = ArenaSessionState.INVALID;

	private bool m_undergroundActive;

	private bool m_shouldStartNewRedraft;

	private ArenaClientStateType m_currentClientState;

	private ArenaClientStateType m_previousClientState;

	private int m_chosenIndex;

	private ArenaSeasonInfo m_currentSeason;

	private static readonly AssetReference DEFAULT_DRAFT_PAPER_TEXTURE = "Forge_Main_Paper.psd:64b6646e1c591d545885572fccd74259";

	private static readonly AssetReference DEFAULT_DRAFT_PAPER_TEXTURE_PHONE = "Forge_Main_Paper_phone.psd:ab59053fdba3ebd40bfd6ced4fd246bc";

	public ArenaSeasonInfo CurrentSeason => m_currentSeason;

	public ulong SecondsUntilEndOfSeason
	{
		get
		{
			if (m_currentSeason != null)
			{
				return m_currentSeason.Season.GameContentSeason.EndSecondsFromNow;
			}
			return 0uL;
		}
	}

	public int CurrentSeasonId
	{
		get
		{
			if (m_currentSeason != null)
			{
				return m_currentSeason.Season.GameContentSeason.SeasonId;
			}
			return 0;
		}
	}

	public bool HasActiveRun => HasCurrentlyActiveRun(m_undergroundActive);

	public int ChosenIndex => m_chosenIndex;

	public bool IsCurrentRewardUnderground => m_isCurrentRewardUnderground;

	public bool IsCurrentRewardCrowdsFavor => m_isCurrentRewardCrowdsFavor;

	public int MaxRedraftSlot => m_maxRedraftSlot;

	public bool CanShowWinsLosses => m_hasReceivedSessionWinsLosses;

	public event ArenaSessionUpdated OnArenaSessionUpdated;

	public event ArenaClientStateUpdated OnArenaClientStateUpdated;

	public event Action OnRewardChestReady;

	public IEnumerator<IAsyncJobResult> Initialize(ServiceLocator serviceLocator)
	{
		HearthstoneApplication.Get().WillReset += WillReset;
		serviceLocator.Get<GameMgr>().RegisterFindGameEvent(OnFindGameEvent);
		Network network = serviceLocator.Get<Network>();
		network.RegisterNetHandler(ArenaSessionResponse.PacketID.ID, OnArenaSessionResponse);
		network.RegisterNetHandler(DraftRewardsAcked.PacketID.ID, OnAckRewards);
		network.RegisterNetHandler(DraftError.PacketID.ID, OnError);
		network.RegisterNetHandler(DraftRemovePremiumsResponse.PacketID.ID, OnDraftRemovePremiumsResponse);
		network.RegisterNetHandler(SaveArenaDeckResponse.PacketID.ID, OnSaveArenaDeckResponse);
		network.RegisterNetHandler(ArenaRatingInfoResponse.PacketID.ID, OnRatingsInfoResponce);
		yield break;
	}

	public Type[] GetDependencies()
	{
		return new Type[2]
		{
			typeof(Network),
			typeof(GameMgr)
		};
	}

	private void WillReset()
	{
		ClearDeckInfo();
		ClearSession();
	}

	public void Shutdown()
	{
	}

	public static DraftManager Get()
	{
		return ServiceManager.Get<DraftManager>();
	}

	public void OnLoggedIn()
	{
		SceneMgr.Get().RegisterSceneLoadedEvent(OnSceneLoaded);
	}

	public void RegisterDisplayHandlers()
	{
		Network network = Network.Get();
		network.RegisterNetHandler(DraftBeginning.PacketID.ID, OnBegin);
		network.RegisterNetHandler(DraftRetired.PacketID.ID, OnRetire);
		network.RegisterNetHandler(DraftChoicesAndContents.PacketID.ID, OnChoicesAndContents);
		network.RegisterNetHandler(DraftChosen.PacketID.ID, OnChosen);
	}

	public void UnregisterDisplayHandlers()
	{
		Network network = Network.Get();
		network.RemoveNetHandler(DraftBeginning.PacketID.ID, OnBegin);
		network.RemoveNetHandler(DraftRetired.PacketID.ID, OnRetire);
		network.RemoveNetHandler(DraftChoicesAndContents.PacketID.ID, OnChoicesAndContents);
		network.RemoveNetHandler(DraftChosen.PacketID.ID, OnChosen);
	}

	public void RegisterDraftDeckSetListener(DraftDeckSet dlg)
	{
		m_draftDeckSetListeners.Add(dlg);
	}

	public void RemoveDraftDeckSetListener(DraftDeckSet dlg)
	{
		m_draftDeckSetListeners.Remove(dlg);
	}

	public void RegisterRedraftDeckSetListener(DraftDeckSet dlg)
	{
		m_redraftDeckSetListeners.Add(dlg);
	}

	public void RemoveRedraftDeckSetListener(DraftDeckSet dlg)
	{
		m_redraftDeckSetListeners.Remove(dlg);
	}

	public bool HasCurrentlyActiveRun(bool isUnderground)
	{
		if (isUnderground)
		{
			if (m_currentUndergroundSession != null && m_currentUndergroundSession.HasIsActive)
			{
				return m_currentUndergroundSession.IsActive;
			}
			return false;
		}
		if (m_currentNormalSession != null && m_currentNormalSession.HasIsActive)
		{
			return m_currentNormalSession.IsActive;
		}
		return false;
	}

	public void RefreshCurrentSessionFromServer()
	{
		Network.Get().SendArenaSessionRequest();
	}

	public bool ShouldStartDraft()
	{
		ArenaSessionState arenaState = GetArenaState(m_undergroundActive);
		bool flag = arenaState == ArenaSessionState.DRAFTING || arenaState == ArenaSessionState.REDRAFTING || arenaState == ArenaSessionState.NO_RUN;
		ArenaLandingPageManager arenaLandingPageManager = ArenaLandingPageManager.Get();
		if (arenaLandingPageManager != null)
		{
			arenaLandingPageManager.UpdateArenaShouldBeDrafting(flag);
		}
		return flag;
	}

	public CollectionDeck GetDraftDeck()
	{
		return GetDraftDeck(m_undergroundActive);
	}

	public CollectionDeck GetDraftDeck(bool isUnderground)
	{
		if (!isUnderground)
		{
			return m_draftDeck;
		}
		return m_undergroundDraftDeck;
	}

	public CollectionDeck GetBaseDraftDeck()
	{
		if (!m_undergroundActive)
		{
			return m_baseDraftDeck;
		}
		return m_baseUndergroundDraftDeck;
	}

	public CollectionDeck GetRedraftDeck()
	{
		if (!m_undergroundActive)
		{
			return m_redraftDeck;
		}
		return m_undergroundRedraftDeck;
	}

	public int GetSlot()
	{
		if (!m_undergroundActive)
		{
			return m_currentSlot;
		}
		return m_currentUndergroundSlot;
	}

	public int GetMaxSlot()
	{
		if (!m_undergroundActive)
		{
			return m_maxSlot;
		}
		return m_maxUndergroundSlot;
	}

	private void SetSlot(int newSlot)
	{
		if (m_undergroundActive)
		{
			m_currentUndergroundSlot = newSlot;
		}
		else
		{
			m_currentSlot = newSlot;
		}
	}

	public int GetRedraftSlot()
	{
		if (!m_undergroundActive)
		{
			return m_currentRedraftSlot;
		}
		return m_currentUndergroundRedraftSlot;
	}

	public int GetMaxRedraftSlot()
	{
		return m_maxRedraftSlot;
	}

	private void SetRedraftSlot(int newSlot)
	{
		if (m_undergroundActive)
		{
			m_currentUndergroundRedraftSlot = newSlot;
		}
		else
		{
			m_currentRedraftSlot = newSlot;
		}
	}

	public DraftSlotType GetSlotType()
	{
		if (!m_undergroundActive)
		{
			return m_currentSlotType;
		}
		return m_currentUndergroundSlotType;
	}

	public void SetSlotType(DraftSlotType slotType)
	{
		if (m_undergroundActive)
		{
			m_currentUndergroundSlotType = slotType;
		}
		else
		{
			m_currentSlotType = slotType;
		}
	}

	public bool HasSlotType(DraftSlotType slotType)
	{
		if (m_undergroundActive)
		{
			return m_uniqueDraftSlotTypesForUndergroundDeck.Contains(slotType);
		}
		return m_uniqueDraftSlotTypesForDeck.Contains(slotType);
	}

	public int GetLosses()
	{
		return GetCurrentLosses(m_undergroundActive);
	}

	public int GetWins()
	{
		return GetCurrentWins(m_undergroundActive);
	}

	public int GetMaxWins()
	{
		if (!m_undergroundActive)
		{
			return m_maxWins;
		}
		return m_maxUndergroundWins;
	}

	public int GetMaxWins(bool isUnderground)
	{
		if (!isUnderground)
		{
			return m_maxWins;
		}
		return m_maxUndergroundWins;
	}

	public int GetMaxLosses()
	{
		if (!m_undergroundActive)
		{
			return m_maxLosses;
		}
		return m_maxUndergroundLosses;
	}

	public int GetMaxLosses(bool isUnderground)
	{
		if (!isUnderground)
		{
			return m_maxLosses;
		}
		return m_maxUndergroundLosses;
	}

	public int GetCurrentLosses(bool isUnderground)
	{
		if (!isUnderground)
		{
			return m_losses;
		}
		return m_undergroundLosses;
	}

	public int GetCurrentWins(bool isUnderground)
	{
		if (!isUnderground)
		{
			return m_wins;
		}
		return m_undergroundWins;
	}

	public int GetNumTicketsOwned()
	{
		return (int)ServiceManager.Get<CurrencyManager>().GetBalance(CurrencyType.TAVERN_TICKET);
	}

	public int GetArenaSeasonId()
	{
		if (!m_currentSeason.HasCurrentSeasonDisplayId)
		{
			return -1;
		}
		return m_currentSeason.CurrentSeasonDisplayId;
	}

	public AssetReference GetDraftPaperTexture()
	{
		string text = null;
		if (m_currentSeason != null)
		{
			text = ((!UniversalInputManager.UsePhoneUI) ? m_currentSeason.Season.DraftPaperTexture : m_currentSeason.Season.DraftPaperTexturePhone);
		}
		if (!string.IsNullOrEmpty(text))
		{
			return new AssetReference(text);
		}
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			return DEFAULT_DRAFT_PAPER_TEXTURE_PHONE;
		}
		return DEFAULT_DRAFT_PAPER_TEXTURE;
	}

	public bool GetDraftPaperTextColorOverride(ref Color overrideColor)
	{
		if (m_currentSeason != null && !string.IsNullOrEmpty(m_currentSeason.Season.DraftPaperTextColor))
		{
			return ColorUtility.TryParseHtmlString(m_currentSeason.Season.DraftPaperTextColor, out overrideColor);
		}
		return false;
	}

	public AssetReference GetRewardPaperPrefab()
	{
		string text = null;
		if (m_currentSeason != null)
		{
			text = ((!UniversalInputManager.UsePhoneUI) ? m_currentSeason.Season.RewardPaperPrefab : m_currentSeason.Season.RewardPaperPrefabPhone);
		}
		if (!string.IsNullOrEmpty(text))
		{
			return new AssetReference(text);
		}
		return ArenaRewardPaper.GetDefaultRewardPaper();
	}

	public string GetSceneHeadlineText()
	{
		if (m_currentSeason != null && m_currentSeason.Season.Strings.Count > 0)
		{
			return GameStrings.FormatStringWithPlurals(m_currentSeason.Season.Strings, "SCENE_HEADLINE");
		}
		return string.Empty;
	}

	public bool IsRewardChestReady()
	{
		return m_chest != null;
	}

	public List<RewardData> GetRewards()
	{
		if (m_chest != null)
		{
			return m_chest.Rewards;
		}
		return new List<RewardData>();
	}

	public void MakeChoice(int choiceNum, TAG_PREMIUM choicePremium, List<int> packagePremiums = null, bool isConfirmingPackageCards = false)
	{
		m_chosenIndex = choiceNum;
		if (!m_undergroundActive && m_draftDeck == null)
		{
			Debug.LogWarning("DraftManager.MakeChoice(): Trying to make a draft choice while the draft deck is null");
			return;
		}
		if (m_undergroundActive && m_undergroundDraftDeck == null)
		{
			Debug.LogWarning("DraftManager.MakeChoice(): Trying to make a draft choice while the underground draft deck is null");
			return;
		}
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (draftDisplay == null)
		{
			return;
		}
		int num = choiceNum - 1;
		if (!isConfirmingPackageCards && draftDisplay.DoesChoiceHavePackageCards(num))
		{
			draftDisplay.DisplayPackageCardsConfirmation(num);
			return;
		}
		bool flag = IsRedrafting();
		long num2 = 0L;
		int num3 = 0;
		if (m_undergroundActive)
		{
			num2 = (flag ? m_undergroundRedraftDeck.ID : m_undergroundDraftDeck.ID);
			num3 = (flag ? m_currentUndergroundRedraftSlot : m_currentUndergroundSlot);
		}
		else
		{
			num2 = (flag ? m_redraftDeck.ID : m_draftDeck.ID);
			num3 = (flag ? m_currentRedraftSlot : m_currentSlot);
		}
		if (packagePremiums == null)
		{
			packagePremiums = new List<int>();
		}
		Network.Get().MakeDraftChoice(num2, num3, choiceNum, (int)choicePremium, flag, m_undergroundActive, packagePremiums);
		draftDisplay.OnChoiceSelectedClient();
		if (flag)
		{
			draftDisplay.UpdateArenaDraftScreenDataModel();
		}
	}

	public void FindTestGame(int scenarioId, GameType gameType)
	{
		CollectionDeck draftDeck = GetDraftDeck();
		GameMgr.Get().FindGame(gameType, FormatType.FT_WILD, scenarioId, 0, seasonId: CurrentSeasonId, deckId: draftDeck.ID, aiDeck: null, restoreSavedGameState: false, snapshot: null, lettuceMapNodeId: null, lettuceTeamId: 0L);
	}

	public void FindGame()
	{
		int num = m_currentSeason.Season.GameContentSeason.Scenarios.FirstOrDefault()?.ScenarioId ?? 2;
		CollectionDeck draftDeck = GetDraftDeck();
		if (!m_undergroundActive)
		{
			GameMgr.Get().FindGame(GameType.GT_ARENA, FormatType.FT_WILD, num, 0, seasonId: CurrentSeasonId, deckId: draftDeck.ID, aiDeck: null, restoreSavedGameState: false, snapshot: null, lettuceMapNodeId: null, lettuceTeamId: 0L);
		}
		else
		{
			GameMgr.Get().FindGame(GameType.GT_UNDERGROUND_ARENA, FormatType.FT_WILD, num, 0, seasonId: CurrentSeasonId, deckId: draftDeck.ID, aiDeck: null, restoreSavedGameState: false, snapshot: null, lettuceMapNodeId: null, lettuceTeamId: 0L);
		}
		if (draftDeck != null)
		{
			Log.Decks.PrintInfo("Starting Arena Game With Deck:");
			draftDeck.LogDeckStringInformation();
		}
	}

	public TAG_PREMIUM GetDraftPremium(string cardId, int numSignatureShowingAlready = 0, int numGoldenAlreadyShowing = 0)
	{
		CollectionManager collectionManager = CollectionManager.Get();
		if (collectionManager == null)
		{
			return TAG_PREMIUM.NORMAL;
		}
		bool num = collectionManager.GetNumCopiesInCollection(cardId, TAG_PREMIUM.DIAMOND) > 0;
		CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
		if (num && collectionDeck != null && collectionDeck.GetCardIdCount(cardId) == 0)
		{
			return TAG_PREMIUM.DIAMOND;
		}
		CollectibleCard card = collectionManager.GetCard(cardId, TAG_PREMIUM.NORMAL);
		bool isLegendary = card != null && card.Rarity == TAG_RARITY.LEGENDARY;
		if (IsBestValidPremium(cardId, TAG_PREMIUM.SIGNATURE, isLegendary, numSignatureShowingAlready))
		{
			return TAG_PREMIUM.SIGNATURE;
		}
		if (IsBestValidPremium(cardId, TAG_PREMIUM.GOLDEN, isLegendary, numGoldenAlreadyShowing))
		{
			return TAG_PREMIUM.GOLDEN;
		}
		return TAG_PREMIUM.NORMAL;
	}

	private bool IsBestValidPremium(string cardId, TAG_PREMIUM premium, bool isLegendary, int numAlreadyInUse)
	{
		int numCopiesInCollection = CollectionManager.Get().GetNumCopiesInCollection(cardId, premium);
		if (numCopiesInCollection <= 0)
		{
			return false;
		}
		if (isLegendary)
		{
			return true;
		}
		CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
		CollectibleCard card = CollectionManager.Get().GetCard(cardId, premium);
		if (numCopiesInCollection >= card.DefaultMaxCopiesPerDeck || collectionDeck.GetCardCountAllMatchingSlots(cardId, premium) < numCopiesInCollection - numAlreadyInUse)
		{
			return true;
		}
		return false;
	}

	public void PromptToDisablePremium()
	{
		if (IsRemovePremiumEnabled() && !m_pendingRequestToDisablePremiums && !Options.Get().GetBool(Option.HAS_DISABLED_PREMIUMS_THIS_DRAFT) && !m_inRewards)
		{
			AlertPopup.PopupInfo popupInfo = new AlertPopup.PopupInfo();
			popupInfo.m_headerText = GameStrings.Get("GLUE_DRAFT_REMOVE_PREMIUMS_DIALOG_TITLE");
			popupInfo.m_text = GameStrings.Get("GLUE_DRAFT_REMOVE_PREMIUMS_DIALOG_BODY");
			popupInfo.m_alertTextAlignment = UberText.AlignmentOptions.Center;
			popupInfo.m_showAlertIcon = false;
			popupInfo.m_responseDisplay = AlertPopup.ResponseDisplay.CONFIRM_CANCEL;
			popupInfo.m_confirmText = GameStrings.Get("GLOBAL_BUTTON_YES");
			popupInfo.m_cancelText = GameStrings.Get("GLOBAL_BUTTON_NO");
			popupInfo.m_responseCallback = OnDisablePremiumsConfirmationResponse;
			DialogManager.Get().ShowPopup(popupInfo);
			m_pendingRequestToDisablePremiums = true;
		}
	}

	private void OnDisablePremiumsConfirmationResponse(AlertPopup.Response response, object userData)
	{
		if (IsRemovePremiumEnabled())
		{
			m_pendingRequestToDisablePremiums = false;
			if (response == AlertPopup.Response.CONFIRM)
			{
				Network.Get().DraftRequestDisablePremiums();
			}
		}
	}

	private void OnDraftRemovePremiumsResponse()
	{
		if (!IsRemovePremiumEnabled())
		{
			return;
		}
		Options.Get().SetBool(Option.HAS_DISABLED_PREMIUMS_THIS_DRAFT, val: true);
		Network.DraftChoicesAndContents draftRemovePremiumsResponse = Network.Get().GetDraftRemovePremiumsResponse();
		CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
		collectionDeck.GetSlots().Clear();
		foreach (Network.CardUserData card in draftRemovePremiumsResponse.DeckInfo.Cards)
		{
			string text = ((card.DbId == 0) ? string.Empty : GameUtils.TranslateDbIdToCardId(card.DbId));
			for (int i = 0; i < card.Count; i++)
			{
				if (!collectionDeck.AddCard(text, card.Premium, false, null))
				{
					Debug.LogWarning($"DraftManager.OnDraftRemovePremiumsResponse() - Card {text} could not be added to draft deck");
				}
				else
				{
					collectionDeck.UpdateNewState(text, card.Premium, IsRedraftCard(text, card.Premium));
				}
			}
		}
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (draftDisplay != null && draftDisplay.m_draftDeckTrays != null)
		{
			foreach (DraftPhoneDeckTray draftDeckTray in draftDisplay.m_draftDeckTrays)
			{
				draftDeckTray.GetCardsContent().UpdateCardList();
			}
		}
		InformDraftDisplayOfChoices(draftRemovePremiumsResponse.Choices);
	}

	private void OnSaveArenaDeckResponse()
	{
		Options.Get().SetBool(Option.HAS_DISABLED_PREMIUMS_THIS_DRAFT, val: true);
		SaveArenaDeckResponse saveArenaDeckResponse = Network.Get().GetSaveArenaDeckResponse();
		CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
		collectionDeck.GetSlots().Clear();
		foreach (DeckCardData card in saveArenaDeckResponse.Decks.Cards)
		{
			int asset = card.Def.Asset;
			string text = ((asset == 0) ? string.Empty : GameUtils.TranslateDbIdToCardId(asset));
			for (int i = 0; i < card.Qty; i++)
			{
				TAG_PREMIUM premium = (TAG_PREMIUM)card.Def.Premium;
				if (!collectionDeck.AddCard(text, premium, false, null))
				{
					Debug.LogWarning($"DraftManager.OnSaveArenaDeckResponse() - Card {text} could not be added to draft deck");
				}
				else
				{
					collectionDeck.UpdateNewState(text, premium, IsRedraftCard(text, premium));
				}
			}
		}
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (!(draftDisplay != null) || draftDisplay.m_draftDeckTrays == null)
		{
			return;
		}
		foreach (DraftPhoneDeckTray draftDeckTray in draftDisplay.m_draftDeckTrays)
		{
			draftDeckTray.GetCardsContent().UpdateCardList();
		}
	}

	public bool ShowArenaPopup_SeasonEndingSoon(long secondsToCurrentSeasonEnd, Action popupClosedCallback)
	{
		if (m_currentSeason == null || !m_currentSeason.HasSeasonEndingSoonPrefab || string.IsNullOrEmpty(m_currentSeason.SeasonEndingSoonPrefab) || !m_currentSeason.HasSeason || m_currentSeason.Season == null || m_currentSeason.Season.Strings.Count == 0)
		{
			Error.AddDevWarning("No Season Data", "Cannot show 'Ending Soon' dialog - the current Arena season={0} does not have the ENDING_SOON_PREFAB data or header/body strings.", CurrentSeasonId);
			return false;
		}
		TimeUtils.ElapsedStringSet stringSet = new TimeUtils.ElapsedStringSet
		{
			m_seconds = null,
			m_minutes = null,
			m_hours = "GLUE_ARENA_POPUP_ENDING_SOON_HEADER_HOURS",
			m_yesterday = null,
			m_days = "GLUE_ARENA_POPUP_ENDING_SOON_HEADER_DAYS",
			m_weeks = "GLUE_ARENA_POPUP_ENDING_SOON_HEADER_WEEKS",
			m_monthAgo = "GLUE_ARENA_POPUP_ENDING_SOON_HEADER_MONTHS"
		};
		BasicPopup.PopupInfo popupInfo = new BasicPopup.PopupInfo();
		popupInfo.m_prefabAssetRefs.Add(m_currentSeason.SeasonEndingSoonPrefab);
		popupInfo.m_prefabAssetRefs.Add(m_currentSeason.SeasonEndingSoonPrefabExtra);
		popupInfo.m_headerText = TimeUtils.GetElapsedTimeString(secondsToCurrentSeasonEnd, stringSet, roundUp: true);
		popupInfo.m_bodyText = GameStrings.FormatStringWithPlurals(m_currentSeason.Season.Strings, "ENDING_SOON_BODY");
		popupInfo.m_responseUserData = CurrentSeasonId;
		popupInfo.m_blurWhenShown = true;
		popupInfo.m_responseCallback = delegate
		{
			if (popupClosedCallback != null)
			{
				popupClosedCallback();
			}
		};
		return DialogManager.Get().ShowArenaSeasonPopup(UserAttentionBlocker.NONE, popupInfo);
	}

	public bool ShowArenaPopup_SeasonComingSoon(long secondsToNextSeasonStart, Action popupClosedCallback)
	{
		if (m_currentSeason == null || !m_currentSeason.HasNextSeasonComingSoonPrefab || string.IsNullOrEmpty(m_currentSeason.NextSeasonComingSoonPrefab) || m_currentSeason.NextSeasonStrings == null || m_currentSeason.NextSeasonStrings.Count == 0)
		{
			Error.AddDevWarning("No Season Data", "Cannot show 'Coming Soon' dialog - the season after current Arena season={0} does not have the COMING_SOON_PREFAB data or header/body strings.", CurrentSeasonId);
			return false;
		}
		TimeUtils.ElapsedStringSet stringSet = new TimeUtils.ElapsedStringSet
		{
			m_seconds = null,
			m_minutes = null,
			m_hours = "GLUE_ARENA_POPUP_COMING_SOON_HEADER_HOURS",
			m_yesterday = null,
			m_days = "GLUE_ARENA_POPUP_COMING_SOON_HEADER_DAYS",
			m_weeks = "GLUE_ARENA_POPUP_COMING_SOON_HEADER_WEEKS",
			m_monthAgo = "GLUE_ARENA_POPUP_COMING_SOON_HEADER_MONTHS"
		};
		BasicPopup.PopupInfo popupInfo = new BasicPopup.PopupInfo();
		popupInfo.m_prefabAssetRefs.Add(m_currentSeason.NextSeasonComingSoonPrefab);
		popupInfo.m_prefabAssetRefs.Add(m_currentSeason.NextSeasonComingSoonPrefabExtra);
		popupInfo.m_headerText = TimeUtils.GetElapsedTimeString(secondsToNextSeasonStart, stringSet, roundUp: true);
		popupInfo.m_bodyText = GameStrings.FormatStringWithPlurals(m_currentSeason.NextSeasonStrings, "COMING_SOON_BODY");
		popupInfo.m_blurWhenShown = true;
		popupInfo.m_responseUserData = m_currentSeason.NextSeasonId;
		popupInfo.m_responseCallback = delegate
		{
			if (popupClosedCallback != null)
			{
				popupClosedCallback();
			}
		};
		return DialogManager.Get().ShowArenaSeasonPopup(UserAttentionBlocker.NONE, popupInfo);
	}

	public bool ShowNextArenaPopup(Action popupClosedCallback)
	{
		if (m_currentSeason == null || PopupDisplayManager.Get().IsShowing)
		{
			return false;
		}
		if (ReturningPlayerMgr.Get().SuppressOldPopups)
		{
			return false;
		}
		if (SceneMgr.Get().GetMode() == SceneMgr.Mode.LOGIN && HasActiveRun)
		{
			bool flag = ShowSeasonEnding(popupClosedCallback);
			if (!flag)
			{
				return ShowSeasonStarting(popupClosedCallback);
			}
			return flag;
		}
		return false;
	}

	public bool IsRedrafting()
	{
		ArenaSessionState arenaState = GetArenaState(m_undergroundActive);
		if (arenaState != ArenaSessionState.REDRAFTING)
		{
			return arenaState == ArenaSessionState.MIDRUN_REDRAFT_PENDING;
		}
		return true;
	}

	public bool IsEditingDeck()
	{
		return GetArenaState(m_undergroundActive) == ArenaSessionState.EDITING_DECK;
	}

	public void NewRedraftStarted()
	{
		m_shouldStartNewRedraft = false;
	}

	public bool IsRedraftOnLossEnabled()
	{
		return NetCache.Get().GetNetObject<NetCache.NetCacheFeatures>().ArenaRedraftOnLossEnabled;
	}

	public bool IsRemovePremiumEnabled()
	{
		return NetCache.Get().GetNetObject<NetCache.NetCacheFeatures>().ArenaRemovePremiumEnabled;
	}

	public int WinsNeededToUnlockUnderground()
	{
		return NetCache.Get().GetNetObject<NetCache.NetCacheFeatures>().WinsNeededToUnlockUndergroundArena;
	}

	public bool AddCardToArenaDeck(string cardId, TAG_PREMIUM premium)
	{
		CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
		if (collectionDeck.AddCard(cardId, premium, false, null))
		{
			collectionDeck.UpdateNewState(cardId, premium, IsRedraftCard(cardId, premium));
			return true;
		}
		return false;
	}

	public bool RemoveCardFromArenaDeck(string cardId, TAG_PREMIUM premium)
	{
		return (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck).RemoveCard(cardId, premium, valid: true, enforceRemainingDeckRuleset: false, reconcileOwnership: false);
	}

	private bool ShowSeasonEnding(Action popupClosedCallback)
	{
		int num = Options.Get().GetInt(Option.LATEST_SEEN_ARENA_SEASON_ENDING);
		long? num2 = ((!m_currentSeason.HasSeason) ? ((long?)null) : new long?((long)m_currentSeason.Season.GameContentSeason.EndSecondsFromNow));
		if (num2.HasValue && m_currentSeason.HasSeasonEndingSoonDays && num2.Value <= m_currentSeason.SeasonEndingSoonDays * 86400 && num < CurrentSeasonId)
		{
			int seasonIdEnding = CurrentSeasonId;
			Action popupClosedCallback2 = delegate
			{
				Options.Get().SetInt(Option.LATEST_SEEN_ARENA_SEASON_ENDING, seasonIdEnding);
				if (popupClosedCallback != null)
				{
					popupClosedCallback();
				}
			};
			return ShowArenaPopup_SeasonEndingSoon(num2.Value, popupClosedCallback2);
		}
		return false;
	}

	private bool ShowSeasonStarting(Action popupClosedCallback)
	{
		int num = Options.Get().GetInt(Option.LATEST_SEEN_ARENA_SEASON_STARTING);
		long? num2 = ((!m_currentSeason.HasNextStartSecondsFromNow) ? ((long?)null) : new long?((long)m_currentSeason.NextStartSecondsFromNow));
		if (num2.HasValue && m_currentSeason.HasNextSeasonComingSoonDays && num2.Value <= m_currentSeason.NextSeasonComingSoonDays * 86400 && m_currentSeason.HasNextSeasonId && num < m_currentSeason.NextSeasonId)
		{
			int seasonIdStarting = m_currentSeason.NextSeasonId;
			Action popupClosedCallback2 = delegate
			{
				Options.Get().SetInt(Option.LATEST_SEEN_ARENA_SEASON_STARTING, seasonIdStarting);
				if (popupClosedCallback != null)
				{
					popupClosedCallback();
				}
			};
			return ShowArenaPopup_SeasonComingSoon(num2.Value, popupClosedCallback2);
		}
		return false;
	}

	public void ClearAllInnkeeperPopups()
	{
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_HERO_CHOICE);
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_CARD_CHOICE);
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_CARD_CHOICE2);
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_PLAY_MODE);
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_1WIN);
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_2LOSS);
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_RETIRE);
		Options.Get().DeleteOption(Option.HAS_SEEN_FORGE_MAX_WIN);
	}

	public void ClearAllSeenPopups()
	{
		Options.Get().DeleteOption(Option.LATEST_SEEN_SCHEDULED_ENTERED_ARENA_DRAFT);
		Options.Get().DeleteOption(Option.HAS_SEEN_FREE_ARENA_WIN_DIALOG_THIS_DRAFT);
		Options.Get().DeleteOption(Option.LATEST_SEEN_ARENA_SEASON_ENDING);
		Options.Get().DeleteOption(Option.LATEST_SEEN_ARENA_SEASON_STARTING);
	}

	private void ClearDeckInfo()
	{
		if (m_undergroundActive)
		{
			m_undergroundDraftDeck = CreateBlankCollectionDeck(0L);
			m_undergroundRedraftDeck = CreateBlankCollectionDeck(0L);
			m_baseUndergroundDraftDeck = CreateBlankCollectionDeck(0L);
			m_baseUndergroundRedraftDeck = CreateBlankCollectionDeck(0L);
			m_hasReceivedSessionWinsLosses = false;
			m_undergroundLosses = 0;
			m_undergroundWins = 0;
			m_chest = null;
			m_isCurrentRewardUnderground = true;
			m_isCurrentRewardCrowdsFavor = false;
			m_deckActiveDuringSession = false;
		}
		else
		{
			m_draftDeck = CreateBlankCollectionDeck(0L);
			m_redraftDeck = CreateBlankCollectionDeck(0L);
			m_baseDraftDeck = CreateBlankCollectionDeck(0L);
			m_baseRedraftDeck = CreateBlankCollectionDeck(0L);
			m_hasReceivedSessionWinsLosses = false;
			m_losses = 0;
			m_wins = 0;
			m_chest = null;
			m_isCurrentRewardUnderground = false;
			m_isCurrentRewardCrowdsFavor = false;
			m_deckActiveDuringSession = false;
		}
		Options.Get().SetBool(Option.HAS_SEEN_FREE_ARENA_WIN_DIALOG_THIS_DRAFT, val: false);
		Options.Get().SetBool(Option.HAS_DISABLED_PREMIUMS_THIS_DRAFT, val: false);
		this.OnArenaSessionUpdated?.Invoke(m_undergroundActive);
	}

	private void ClearSession()
	{
		if (m_undergroundActive)
		{
			m_currentUndergroundSession = null;
		}
		else
		{
			m_currentNormalSession = null;
		}
	}

	private void OnBegin()
	{
		if (IsRedrafting())
		{
			OnRedraftBegin();
			return;
		}
		SetArenaState(ArenaSessionState.DRAFTING, m_undergroundActive);
		SetClientState(m_undergroundActive ? ArenaClientStateType.Underground_Draft : ArenaClientStateType.Normal_Draft);
		Options.Get().SetBool(Option.HAS_SEEN_FREE_ARENA_WIN_DIALOG_THIS_DRAFT, val: false);
		if (SceneMgr.Get().GetMode() == SceneMgr.Mode.DRAFT && (!SceneMgr.Get().IsTransitionNowOrPending() || SceneMgr.Get().GetPrevMode() != SceneMgr.Mode.DRAFT))
		{
			m_hasReceivedSessionWinsLosses = true;
			Network.BeginDraft beginDraft = Network.Get().GetBeginDraft();
			if (m_undergroundActive)
			{
				m_undergroundDraftDeck = new CollectionDeck
				{
					ID = beginDraft.DeckID,
					Type = DeckType.DRAFT_DECK,
					FormatType = FormatType.FT_WILD
				};
				m_undergroundWins = beginDraft.Wins;
				m_undergroundLosses = 0;
				m_currentUndergroundSlot = 0;
				m_currentUndergroundRedraftSlot = -1;
				m_currentUndergroundSlotType = beginDraft.SlotType;
				m_uniqueDraftSlotTypesForUndergroundDeck = beginDraft.UniqueSlotTypesForDraft;
				m_maxUndergroundSlot = beginDraft.MaxSlot;
				m_chest = null;
				m_inRewards = false;
				m_isCurrentRewardUnderground = true;
				m_isCurrentRewardCrowdsFavor = false;
				m_currentUndergroundSession = beginDraft.Session;
			}
			else
			{
				m_draftDeck = new CollectionDeck
				{
					ID = beginDraft.DeckID,
					Type = DeckType.DRAFT_DECK,
					FormatType = FormatType.FT_WILD
				};
				m_wins = beginDraft.Wins;
				m_losses = 0;
				m_currentSlot = 0;
				m_currentRedraftSlot = -1;
				m_currentSlotType = beginDraft.SlotType;
				m_uniqueDraftSlotTypesForDeck = beginDraft.UniqueSlotTypesForDraft;
				m_maxSlot = beginDraft.MaxSlot;
				m_chest = null;
				m_inRewards = false;
				m_isCurrentRewardUnderground = false;
				m_isCurrentRewardCrowdsFavor = false;
				m_currentNormalSession = beginDraft.Session;
			}
			Options.Get().SetBool(Option.HAS_DISABLED_PREMIUMS_THIS_DRAFT, val: false);
			SessionRecord sessionRecord = new SessionRecord();
			sessionRecord.Wins = (uint)beginDraft.Wins;
			sessionRecord.Losses = 0u;
			sessionRecord.RunFinished = false;
			sessionRecord.SessionRecordType = SessionRecordType.ARENA;
			BnetPresenceMgr.Get().SetGameFieldBlob(22u, sessionRecord);
			CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
			Log.Arena.Print($"DraftManager.OnBegin - Got new draft deck with ID: {collectionDeck.ID}");
			InformDraftDisplayOfChoices(beginDraft.Heroes);
			FireDraftDeckSetEvent();
			FireRedraftDeckSetEvent();
			this.OnArenaSessionUpdated?.Invoke(m_undergroundActive);
		}
	}

	public void PopuplateBaseDecks()
	{
		if (m_undergroundActive)
		{
			m_baseUndergroundDraftDeck.CopyFrom(m_undergroundDraftDeck);
			m_baseUndergroundRedraftDeck.CopyFrom(m_undergroundRedraftDeck);
		}
		else
		{
			m_baseDraftDeck.CopyFrom(m_draftDeck);
			m_baseRedraftDeck.CopyFrom(m_redraftDeck);
		}
	}

	public void OnRedraftBegin()
	{
		if (IsRedraftOnLossEnabled())
		{
			FireDraftDeckSetEvent();
			FireRedraftDeckSetEvent();
			if (m_undergroundActive)
			{
				m_currentUndergroundRedraftSlot = 0;
				Log.Arena.Print($"DraftManager.OnRedraftBegin - Got new redraft deck with ID: {m_undergroundRedraftDeck.ID}");
			}
			else
			{
				m_currentRedraftSlot = 0;
				Log.Arena.Print($"DraftManager.OnRedraftBegin - Got new redraft deck with ID: {m_redraftDeck.ID}");
			}
			PopuplateBaseDecks();
		}
	}

	public void OnRedraftEnd()
	{
		if (IsRedraftOnLossEnabled())
		{
			if (m_undergroundActive)
			{
				m_undergroundRedraftDeck = CreateBlankCollectionDeck(0L);
				m_currentUndergroundRedraftSlot = -1;
			}
			else
			{
				m_redraftDeck = CreateBlankCollectionDeck(0L);
				m_currentRedraftSlot = -1;
			}
			SetArenaState(ArenaSessionState.EDITING_DECK, m_undergroundActive);
		}
	}

	public void RevertDraftDeck(List<DraftPhoneDeckTray> draftDeckTrays)
	{
		if (m_undergroundActive)
		{
			m_undergroundDraftDeck.CopyFrom(m_baseUndergroundDraftDeck);
			m_undergroundRedraftDeck.CopyFrom(m_baseUndergroundRedraftDeck);
		}
		else
		{
			m_draftDeck.CopyFrom(m_baseDraftDeck);
			m_redraftDeck.CopyFrom(m_baseRedraftDeck);
		}
		foreach (DraftPhoneDeckTray draftDeckTray in draftDeckTrays)
		{
			draftDeckTray.GetCardsContent().UpdateCardList(string.Empty, GetDraftDeck());
		}
	}

	private void OnRetire()
	{
		Network.DraftRetired retiredDraft = Network.Get().GetRetiredDraft();
		Log.Arena.Print($"DraftManager.OnRetire deckID={retiredDraft.Deck}");
		m_chest = retiredDraft.Chest;
		m_inRewards = true;
		m_isCurrentRewardUnderground = retiredDraft.IsUnderground;
		m_isCurrentRewardCrowdsFavor = retiredDraft.IsCrowdsFavor;
		if (m_chest != null)
		{
			m_isCurrentRewardCrowdsFavor = m_isCurrentRewardCrowdsFavor || m_chest.Rewards.Any((RewardData reward) => reward.IsCrowdsFavor);
		}
		SetArenaState(ArenaSessionState.REWARDS, m_undergroundActive);
		InformDraftDisplayOfChoices(new List<Network.CardChoice>());
		this.OnRewardChestReady?.Invoke();
	}

	private void OnAckRewards()
	{
		SessionRecord sessionRecord = new SessionRecord();
		sessionRecord.Wins = (uint)(m_undergroundActive ? m_undergroundWins : m_wins);
		sessionRecord.Losses = (uint)(m_undergroundActive ? m_undergroundLosses : m_losses);
		sessionRecord.RunFinished = true;
		sessionRecord.SessionRecordType = SessionRecordType.ARENA;
		BnetPresenceMgr.Get().SetGameFieldBlob(22u, sessionRecord);
		if (!Options.Get().GetBool(Option.HAS_ACKED_ARENA_REWARDS, defaultVal: false))
		{
			Options.Get().SetBool(Option.HAS_ACKED_ARENA_REWARDS, val: true);
		}
		Network.Get().GetRewardsAckDraftID();
		ClearDeckInfo();
		SetArenaState(ArenaSessionState.NO_RUN, m_undergroundActive);
		if (m_undergroundActive && m_currentUndergroundSession != null)
		{
			m_currentUndergroundSession.IsActive = false;
		}
		if (!m_undergroundActive && m_currentNormalSession != null)
		{
			m_currentNormalSession.IsActive = false;
		}
		ArenaLandingPageManager arenaLandingPageManager = ArenaLandingPageManager.Get();
		if (arenaLandingPageManager != null)
		{
			if (!m_undergroundActive)
			{
				arenaLandingPageManager.DetermineToggleButtonStartingState();
			}
			arenaLandingPageManager.UpdateArenaLandingPageModeViewModels();
		}
	}

	private void OnChoicesAndContents()
	{
		Network.DraftChoicesAndContents draftChoicesAndContents = Network.Get().GetDraftChoicesAndContents();
		m_hasReceivedSessionWinsLosses = true;
		if (m_undergroundActive)
		{
			m_currentUndergroundSlot = draftChoicesAndContents.Slot;
			m_currentUndergroundRedraftSlot = draftChoicesAndContents.RedraftSlot;
			m_currentUndergroundSlotType = draftChoicesAndContents.SlotType;
			m_uniqueDraftSlotTypesForUndergroundDeck = draftChoicesAndContents.UniqueSlotTypesForDraft;
			m_maxUndergroundSlot = draftChoicesAndContents.MaxSlot;
			m_maxRedraftSlot = draftChoicesAndContents.MaxRedraftSlot;
			m_undergroundDraftDeck = new CollectionDeck
			{
				ID = draftChoicesAndContents.DeckInfo.Deck,
				Type = DeckType.DRAFT_DECK,
				HeroCardID = draftChoicesAndContents.Hero.Name,
				HeroPowerCardID = draftChoicesAndContents.HeroPower.Name,
				FormatType = FormatType.FT_WILD
			};
			m_undergroundRedraftDeck = new CollectionDeck
			{
				ID = draftChoicesAndContents.RedraftDeckId,
				Type = DeckType.DRAFT_DECK,
				HeroCardID = draftChoicesAndContents.Hero.Name,
				HeroPowerCardID = draftChoicesAndContents.HeroPower.Name,
				FormatType = FormatType.FT_WILD
			};
			m_undergroundLosses = draftChoicesAndContents.Losses;
			m_undergroundWins = draftChoicesAndContents.Wins;
			m_maxUndergroundWins = draftChoicesAndContents.MaxWins;
			m_maxUndergroundLosses = draftChoicesAndContents.MaxLosses;
			m_chest = draftChoicesAndContents.Chest;
			m_isCurrentRewardUnderground = draftChoicesAndContents.IsUnderground;
			m_isCurrentRewardCrowdsFavor = draftChoicesAndContents.IsCrowdsFavor;
			m_inRewards = m_chest != null;
			if (m_chest != null)
			{
				m_isCurrentRewardCrowdsFavor = m_isCurrentRewardCrowdsFavor || m_chest.Rewards.Any((RewardData reward) => reward.IsCrowdsFavor);
			}
			m_currentUndergroundSession = draftChoicesAndContents.Session;
		}
		else
		{
			m_currentSlot = draftChoicesAndContents.Slot;
			m_currentRedraftSlot = draftChoicesAndContents.RedraftSlot;
			m_currentSlotType = draftChoicesAndContents.SlotType;
			m_uniqueDraftSlotTypesForDeck = draftChoicesAndContents.UniqueSlotTypesForDraft;
			m_maxSlot = draftChoicesAndContents.MaxSlot;
			m_maxRedraftSlot = draftChoicesAndContents.MaxRedraftSlot;
			m_draftDeck = new CollectionDeck
			{
				ID = draftChoicesAndContents.DeckInfo.Deck,
				Type = DeckType.DRAFT_DECK,
				HeroCardID = draftChoicesAndContents.Hero.Name,
				HeroPowerCardID = draftChoicesAndContents.HeroPower.Name,
				FormatType = FormatType.FT_WILD
			};
			m_redraftDeck = new CollectionDeck
			{
				ID = draftChoicesAndContents.RedraftDeckId,
				Type = DeckType.DRAFT_DECK,
				HeroCardID = draftChoicesAndContents.Hero.Name,
				HeroPowerCardID = draftChoicesAndContents.HeroPower.Name,
				FormatType = FormatType.FT_WILD
			};
			m_losses = draftChoicesAndContents.Losses;
			m_wins = draftChoicesAndContents.Wins;
			m_maxWins = draftChoicesAndContents.MaxWins;
			m_maxLosses = draftChoicesAndContents.MaxLosses;
			m_chest = draftChoicesAndContents.Chest;
			m_isCurrentRewardUnderground = draftChoicesAndContents.IsUnderground;
			m_isCurrentRewardCrowdsFavor = draftChoicesAndContents.IsCrowdsFavor;
			if (m_chest != null)
			{
				m_isCurrentRewardCrowdsFavor = m_isCurrentRewardCrowdsFavor || m_chest.Rewards.Any((RewardData reward) => reward.IsCrowdsFavor);
			}
			m_inRewards = m_chest != null;
			m_currentNormalSession = draftChoicesAndContents.Session;
		}
		CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
		Log.Arena.Print($"DraftManager.OnChoicesAndContents - Draft Deck ID: {collectionDeck.ID}, Hero Card = {collectionDeck.HeroCardID}");
		foreach (Network.CardUserData card in draftChoicesAndContents.DeckInfo.Cards)
		{
			string text = ((card.DbId == 0) ? string.Empty : GameUtils.TranslateDbIdToCardId(card.DbId));
			Log.Arena.Print($"DraftManager.OnChoicesAndContents - Draft deck contains card {text}");
			for (int num = 0; num < card.Count; num++)
			{
				if (!collectionDeck.AddCard(text, card.Premium, false, null))
				{
					Debug.LogWarning($"DraftManager.OnChoicesAndContents() - Card {text} could not be added to draft deck");
				}
				else
				{
					collectionDeck.UpdateNewState(text, card.Premium, IsRedraftCard(text, card.Premium));
				}
			}
		}
		CollectionDeck collectionDeck2 = (m_undergroundActive ? m_undergroundRedraftDeck : m_redraftDeck);
		collectionDeck2.ClearSlotContents();
		foreach (Network.CardUserData card2 in draftChoicesAndContents.RedraftDeckInfo.Cards)
		{
			string text2 = ((card2.DbId == 0) ? string.Empty : GameUtils.TranslateDbIdToCardId(card2.DbId));
			Log.Arena.Print($"DraftManager.OnChoicesAndContents - Draft deck contains card {text2}");
			for (int num2 = 0; num2 < card2.Count; num2++)
			{
				if (!collectionDeck2.AddCard(text2, card2.Premium, false, null))
				{
					Debug.LogWarning($"DraftManager.OnChoicesAndContents() - Card {text2} could not be added to draft deck");
				}
				else
				{
					collectionDeck2.UpdateNewState(text2, card2.Premium, isNew: true);
				}
			}
		}
		InformDraftDisplayOfChoices(draftChoicesAndContents.Choices);
		FireDraftDeckSetEvent();
		FireRedraftDeckSetEvent();
		this.OnArenaSessionUpdated?.Invoke(m_undergroundActive);
		if (m_chest != null)
		{
			this.OnRewardChestReady?.Invoke();
		}
	}

	private void InformDraftDisplayOfChoices(List<NetCache.CardDefinition> choices)
	{
		List<Network.CardChoice> list = new List<Network.CardChoice>(choices.Count);
		foreach (NetCache.CardDefinition choice in choices)
		{
			Network.CardChoice item = new Network.CardChoice
			{
				CardDef = choice,
				PackageCardDefs = new List<NetCache.CardDefinition>()
			};
			list.Add(item);
		}
		InformDraftDisplayOfChoices(list);
	}

	private void InformDraftDisplayOfChoices(List<Network.CardChoice> choices)
	{
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (draftDisplay == null)
		{
			return;
		}
		if (m_inRewards)
		{
			draftDisplay.OnRetire();
			draftDisplay.SetDraftMode(DraftDisplay.DraftMode.IN_REWARDS);
			return;
		}
		if (choices.Count == 0)
		{
			m_deckActiveDuringSession = true;
			draftDisplay.SetDraftMode(DraftDisplay.DraftMode.ACTIVE_DRAFT_DECK);
			return;
		}
		if (!Options.Get().GetBool(Option.HAS_DISABLED_PREMIUMS_THIS_DRAFT) && GetSlotType() != DraftSlotType.DRAFT_SLOT_HERO_POWER)
		{
			foreach (Network.CardChoice choice in choices)
			{
				choice.CardDef.Premium = GetDraftPremium(choice.CardDef.Name);
				for (int i = 0; i < choice.PackageCardDefs.Count; i++)
				{
					NetCache.CardDefinition cardDefinition = choice.PackageCardDefs[i];
					int num = 0;
					int num2 = 0;
					for (int num3 = i - 1; num3 >= 0; num3--)
					{
						NetCache.CardDefinition cardDefinition2 = choice.PackageCardDefs[num3];
						if (cardDefinition2.Name == cardDefinition.Name)
						{
							switch (cardDefinition2.Premium)
							{
							case TAG_PREMIUM.SIGNATURE:
								num++;
								break;
							case TAG_PREMIUM.GOLDEN:
								num2++;
								break;
							}
						}
					}
					cardDefinition.Premium = GetDraftPremium(cardDefinition.Name, num, num2);
				}
			}
		}
		draftDisplay.UpdateArenaDraftScreenDataModel();
		if (IsRedrafting())
		{
			draftDisplay.SetDraftMode(DraftDisplay.DraftMode.REDRAFTING);
		}
		else
		{
			draftDisplay.SetDraftMode(DraftDisplay.DraftMode.DRAFTING);
		}
		draftDisplay.AcceptNewChoices(choices);
	}

	private void InformDraftDisplayOfSelectedChoice(int chosenIndex, List<NetCache.CardDefinition> packageCardDefs)
	{
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (!(draftDisplay == null))
		{
			draftDisplay.OnChoiceSelected(chosenIndex, packageCardDefs);
		}
	}

	private void OnChosen()
	{
		Network.DraftChosen draftChosen = Network.Get().GetDraftChosen();
		if (string.IsNullOrEmpty(draftChosen.ChosenCard.CardDef.Name) || GameUtils.IsBannedByArenaDenylist(draftChosen.ChosenCard.CardDef.Name))
		{
			return;
		}
		if (!IsRedrafting())
		{
			CollectionDeck collectionDeck = (m_undergroundActive ? m_undergroundDraftDeck : m_draftDeck);
			switch (m_undergroundActive ? m_currentUndergroundSlotType : m_currentSlotType)
			{
			case DraftSlotType.DRAFT_SLOT_HERO:
				Log.Arena.Print("DraftManager.OnChosen(): hero=" + draftChosen.ChosenCard.CardDef.Name);
				collectionDeck.HeroCardID = draftChosen.ChosenCard.CardDef.Name;
				this.OnArenaSessionUpdated?.Invoke(m_undergroundActive);
				break;
			case DraftSlotType.DRAFT_SLOT_CARD:
				AddCardAndPackageCardsToDeck(collectionDeck, draftChosen.ChosenCard);
				break;
			}
			int num = (m_undergroundActive ? m_currentUndergroundSlot : m_currentSlot);
			num += 1 + draftChosen.ChosenCard.PackageCardDefs.Count;
			SetSlot(num);
			if (num > GetMaxSlot() && DraftDisplay.Get() != null)
			{
				DraftDisplay.Get().OnDraftingComplete(isRedraft: false);
			}
		}
		else
		{
			CollectionDeck collectionDeck2 = (m_undergroundActive ? m_undergroundRedraftDeck : m_redraftDeck);
			int num2 = (m_undergroundActive ? m_currentUndergroundRedraftSlot : m_currentRedraftSlot);
			AddCardAndPackageCardsToDeck(collectionDeck2, draftChosen.ChosenCard);
			num2 += 1 + draftChosen.ChosenCard.PackageCardDefs.Count;
			SetRedraftSlot(num2);
			if (num2 > m_maxRedraftSlot && DraftDisplay.Get() != null)
			{
				CollectionDeck obj = (m_undergroundActive ? m_baseUndergroundRedraftDeck : m_baseRedraftDeck);
				obj.ClearSlotContents();
				obj.AddCardsFrom(collectionDeck2);
				DraftDisplay.Get().OnDraftingComplete(isRedraft: true);
			}
		}
		SetSlotType(draftChosen.SlotType);
		InformDraftDisplayOfSelectedChoice(m_chosenIndex, draftChosen.ChosenCard.PackageCardDefs);
		InformDraftDisplayOfChoices(draftChosen.NextChoices);
	}

	private void OnRatingsInfoResponce()
	{
		ArenaRatingInfoResponse arenaRatingInfoResponse = Network.Get().GetArenaRatingInfoResponse();
		if (arenaRatingInfoResponse != null)
		{
			m_wins = arenaRatingInfoResponse.PlayerInfo.NormalWins;
			m_losses = arenaRatingInfoResponse.PlayerInfo.NormalLosses;
			m_maxWins = arenaRatingInfoResponse.PlayerInfo.NormalMaxWins;
			m_maxLosses = arenaRatingInfoResponse.PlayerInfo.NormalMaxLosses;
			m_undergroundWins = arenaRatingInfoResponse.PlayerInfo.UndergroundWins;
			m_undergroundLosses = arenaRatingInfoResponse.PlayerInfo.UndergroundLosses;
			m_maxUndergroundWins = arenaRatingInfoResponse.PlayerInfo.UndergroundMaxWins;
			m_maxUndergroundLosses = arenaRatingInfoResponse.PlayerInfo.UndergroundMaxLosses;
			DraftDisplay draftDisplay = DraftDisplay.Get();
			if (draftDisplay != null)
			{
				draftDisplay.UpdateArenaRating(arenaRatingInfoResponse.PlayerInfo.Rating, arenaRatingInfoResponse.PlayerInfo.UndergroundRating);
			}
		}
	}

	private void AddCardAndPackageCardsToDeck(CollectionDeck deck, Network.CardChoice chosenCard)
	{
		deck.AddCard(chosenCard.CardDef.Name, chosenCard.CardDef.Premium, false, null);
		deck.UpdateNewState(chosenCard.CardDef.Name, chosenCard.CardDef.Premium, IsRedraftCard(chosenCard.CardDef.Name, chosenCard.CardDef.Premium));
		foreach (NetCache.CardDefinition packageCardDef in chosenCard.PackageCardDefs)
		{
			deck.AddCard(packageCardDef.Name, packageCardDef.Premium, false, null);
			deck.UpdateNewState(packageCardDef.Name, packageCardDef.Premium, IsRedraftCard(packageCardDef.Name, packageCardDef.Premium));
		}
	}

	private void OnError()
	{
		if (!SceneMgr.Get().IsModeRequested(SceneMgr.Mode.DRAFT))
		{
			return;
		}
		DraftError draftError = Network.Get().GetDraftError();
		DraftDisplay draftDisplay = DraftDisplay.Get();
		switch (draftError.ErrorCode_)
		{
		case DraftError.ErrorCode.DE_SEASON_INCREMENTED:
			Error.AddWarningLoc("GLOBAL_ERROR_GENERIC_HEADER", "GLOBAL_ARENA_SEASON_ERROR_NOT_ACTIVE");
			RefreshCurrentSessionFromServer();
			if (SceneMgr.Get().GetMode() == SceneMgr.Mode.DRAFT)
			{
				Navigation.GoBack();
			}
			break;
		case DraftError.ErrorCode.DE_NOT_IN_DRAFT_BUT_COULD_BE:
			DraftDisplay.Get().SetDraftMode(DraftDisplay.DraftMode.NO_ACTIVE_DRAFT);
			break;
		case DraftError.ErrorCode.DE_NOT_IN_DRAFT:
			if (draftDisplay != null)
			{
				draftDisplay.SetDraftMode(DraftDisplay.DraftMode.NO_ACTIVE_DRAFT);
			}
			break;
		case DraftError.ErrorCode.DE_NO_LICENSE:
			Debug.LogWarning("DraftManager.OnError - No License.  What does this mean???");
			break;
		case DraftError.ErrorCode.DE_RETIRE_FIRST:
			Debug.LogError("DraftManager.OnError - You cannot start a new draft while one is in progress.");
			break;
		case DraftError.ErrorCode.DE_FEATURE_DISABLED:
			Debug.LogError("DraftManager.OnError - The Arena is currently disabled. Returning to the hub.");
			if (!SceneMgr.Get().IsModeRequested(SceneMgr.Mode.HUB))
			{
				SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
				Error.AddWarningLoc("GLOBAL_FEATURE_DISABLED_TITLE", "GLOBAL_FEATURE_DISABLED_MESSAGE_FORGE");
			}
			break;
		case DraftError.ErrorCode.DE_NOT_ENOUGH_CLASSES:
			Debug.LogError("DraftManager.OnError - You cannot start a new draft with less than three heroes unlocked");
			SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
			Error.AddWarningLoc("GLOBAL_ERROR_GENERIC_HEADER", "GLOBAL_ARENA_ERROR_NOT_ENOUGH_CLASSES");
			break;
		case DraftError.ErrorCode.DE_UNKNOWN:
			Debug.LogError("DraftManager.OnError - UNKNOWN EXCEPTION - See server logs for more info.");
			break;
		default:
			Debug.LogErrorFormat("DraftManager.onError - UNHANDLED ERROR - See server logs for more info. ERROR: {0}", draftError.ErrorCode_);
			break;
		}
	}

	private void OnArenaSessionResponse()
	{
		ArenaSessionResponse arenaSessionResponse = Network.Get().GetArenaSessionResponse();
		OnArenaSessionResponsePacket(arenaSessionResponse);
		GameSaveDataManager gameSaveDataManager = GameSaveDataManager.Get();
		if (gameSaveDataManager != null && gameSaveDataManager.IsDataReady(GameSaveKeyId.PLAYER_FLAGS))
		{
			gameSaveDataManager.GetSubkeyValue(GameSaveKeyId.PLAYER_FLAGS, GameSaveKeySubkeyId.PLAYER_FLAGS_SHOULD_START_UNDERGROUND_ARENA, out long value);
			m_undergroundActive = value == 1;
		}
	}

	public void OnArenaSessionResponsePacket(ArenaSessionResponse response)
	{
		OnArenaSessionResponsePacket(response, isUnderground: false);
		OnArenaSessionResponsePacket(response, isUnderground: true);
	}

	public void OnArenaSessionResponsePacket(ArenaSessionResponse response, bool isUnderground)
	{
		if (response != null && response.ErrorCode == ErrorCode.ERROR_OK && (!isUnderground || response.HasUndergroundSession) && (isUnderground || response.HasSession))
		{
			ArenaSession arenaSession = (isUnderground ? response.UndergroundSession : response.Session);
			m_hasReceivedSessionWinsLosses = true;
			if (isUnderground)
			{
				m_undergroundWins = arenaSession.Wins;
				m_undergroundLosses = arenaSession.Losses;
				m_currentUndergroundSession = arenaSession;
				m_undergroundSessionState = (arenaSession.HasState ? arenaSession.State : ArenaSessionState.INVALID);
			}
			else
			{
				m_wins = arenaSession.Wins;
				m_losses = arenaSession.Losses;
				m_currentNormalSession = arenaSession;
				m_normalSessionState = (arenaSession.HasState ? arenaSession.State : ArenaSessionState.INVALID);
			}
			if (response.HasCurrentSeason)
			{
				m_currentSeason = response.CurrentSeason;
			}
			if (isUnderground == m_undergroundActive && (GameMgr.Get().IsArena() || GameMgr.Get().IsNextArena()))
			{
				SessionRecord sessionRecord = new SessionRecord();
				sessionRecord.Wins = (uint)(m_undergroundActive ? m_undergroundWins : m_wins);
				sessionRecord.Losses = (uint)(m_undergroundActive ? m_undergroundLosses : m_losses);
				sessionRecord.RunFinished = false;
				sessionRecord.SessionRecordType = SessionRecordType.ARENA;
				BnetPresenceMgr.Get().SetGameFieldBlob(22u, sessionRecord);
			}
			this.OnArenaSessionUpdated?.Invoke(isUnderground);
		}
	}

	private bool OnFindGameEvent(FindGameEventData eventData, object userData)
	{
		switch (eventData.m_state)
		{
		case FindGameState.CLIENT_CANCELED:
		case FindGameState.CLIENT_ERROR:
		case FindGameState.BNET_QUEUE_CANCELED:
		case FindGameState.BNET_ERROR:
		case FindGameState.SERVER_GAME_CANCELED:
			if (DraftDisplay.Get() != null)
			{
				DraftDisplay.Get().HandleGameStartupFailure();
			}
			ArenaLandingPageManager.Get()?.MatchmakingCancled();
			break;
		case FindGameState.SERVER_GAME_CONNECTING:
			if (GameMgr.Get().IsNextArena() && !m_hasReceivedSessionWinsLosses)
			{
				RefreshCurrentSessionFromServer();
			}
			break;
		}
		return false;
	}

	public void RequestDraftBegin()
	{
		Network.Get().DraftBegin(m_undergroundActive);
	}

	public void RequestRedraftBegin(bool isUnderground)
	{
		if (IsRedraftOnLossEnabled())
		{
			Network.Get().RedraftBegin(isUnderground);
		}
	}

	public void SetArenaState(ArenaSessionState sessionState, bool isUnderground)
	{
		if (isUnderground)
		{
			m_undergroundSessionState = sessionState;
		}
		else
		{
			m_normalSessionState = sessionState;
		}
		ArenaLandingPageManager arenaLandingPageManager = ArenaLandingPageManager.Get();
		if (arenaLandingPageManager != null)
		{
			arenaLandingPageManager.UpdateArenaSessionState(sessionState, isUnderground);
		}
		switch (sessionState)
		{
		case ArenaSessionState.MIDRUN:
		case ArenaSessionState.MIDRUN_REDRAFT_PENDING:
			SetClientState(m_undergroundActive ? ArenaClientStateType.Underground_Ready : ArenaClientStateType.Normal_Ready);
			break;
		case ArenaSessionState.EDITING_DECK:
			SetClientState(m_undergroundActive ? ArenaClientStateType.Underground_DeckEdit : ArenaClientStateType.Normal_DeckEdit);
			break;
		case ArenaSessionState.DRAFTING:
			SetClientState(m_undergroundActive ? ArenaClientStateType.Underground_Draft : ArenaClientStateType.Normal_Draft);
			break;
		}
	}

	public void SetUndergroundState(bool isUnderground)
	{
		if (isUnderground != m_undergroundActive)
		{
			m_undergroundActive = isUnderground;
			m_chosenIndex = 0;
			FireDraftDeckSetEvent();
			FireRedraftDeckSetEvent();
			UpdateUndergroundGSDState(isUnderground);
		}
		SetClientState((!m_undergroundActive) ? ArenaClientStateType.Normal_Landing : ArenaClientStateType.Underground_Landing);
	}

	private void UpdateUndergroundGSDState(bool isUnderground)
	{
		GameSaveDataManager gameSaveDataManager = GameSaveDataManager.Get();
		if (gameSaveDataManager != null && gameSaveDataManager.IsDataReady(GameSaveKeyId.PLAYER_FLAGS))
		{
			long num = (isUnderground ? 1 : 0);
			gameSaveDataManager.SaveSubkey(new GameSaveDataManager.SubkeySaveRequest(GameSaveKeyId.PLAYER_FLAGS, GameSaveKeySubkeyId.PLAYER_FLAGS_SHOULD_START_UNDERGROUND_ARENA, num));
		}
	}

	public bool IsUnderground()
	{
		return m_undergroundActive;
	}

	public void InitializeDataModelArenaStates()
	{
		ArenaLandingPageManager arenaLandingPageManager = ArenaLandingPageManager.Get();
		if (arenaLandingPageManager != null)
		{
			arenaLandingPageManager.UpdateArenaSessionState(m_undergroundSessionState, isUnderground: true);
			arenaLandingPageManager.UpdateArenaSessionState(m_normalSessionState, isUnderground: false);
		}
	}

	public ArenaSessionState GetArenaState(bool isUnderground)
	{
		if (!isUnderground)
		{
			return m_normalSessionState;
		}
		return m_undergroundSessionState;
	}

	public ArenaSessionState GetArenaState()
	{
		return GetArenaState(m_undergroundActive);
	}

	private void FireDraftDeckSetEvent()
	{
		DraftDeckSet[] array = m_draftDeckSetListeners.ToArray();
		CollectionDeck draftDeck = GetDraftDeck();
		DraftDeckSet[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i](draftDeck);
		}
	}

	private void FireRedraftDeckSetEvent()
	{
		DraftDeckSet[] array = m_redraftDeckSetListeners.ToArray();
		CollectionDeck redraftDeck = GetRedraftDeck();
		DraftDeckSet[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i](redraftDeck);
		}
	}

	private void OnSceneLoaded(SceneMgr.Mode mode, PegasusScene scene, object userData)
	{
		GameMgr gameMgr = GameMgr.Get();
		if ((gameMgr.IsArena() || gameMgr.IsUndergroundArena()) && mode == SceneMgr.Mode.GAMEPLAY)
		{
			GameState.Get().RegisterGameOverListener(OnGameOver);
		}
	}

	private void OnGameOver(TAG_PLAYSTATE playState, object userData)
	{
		SpectatorManager spectatorManager = SpectatorManager.Get();
		if (spectatorManager != null && spectatorManager.IsInSpectatorMode())
		{
			return;
		}
		m_undergroundActive = GameMgr.Get().IsUndergroundArena();
		UpdateUndergroundGSDState(m_undergroundActive);
		switch (playState)
		{
		case TAG_PLAYSTATE.WON:
		{
			NetCache.NetCacheProfileProgress netObject = NetCache.Get().GetNetObject<NetCache.NetCacheProfileProgress>();
			if (netObject == null || GetWins() >= netObject.BestForgeWins)
			{
				NetCache.Get().RefreshNetObject<NetCache.NetCacheProfileProgress>();
			}
			SetArenaState(ArenaSessionState.MIDRUN, m_undergroundActive);
			break;
		}
		case TAG_PLAYSTATE.LOST:
			SetArenaState(m_undergroundActive ? ArenaSessionState.MIDRUN_REDRAFT_PENDING : ArenaSessionState.MIDRUN, m_undergroundActive);
			break;
		case TAG_PLAYSTATE.TIED:
			SetArenaState(ArenaSessionState.MIDRUN, m_undergroundActive);
			break;
		}
	}

	public void ForceStartRedraft()
	{
		m_shouldStartNewRedraft = true;
		GetRedraftDeck()?.RemoveAllCards();
		Network.Get().RequestDraftChoicesAndContents(isUnderground: true);
		DraftDisplay draftDisplay = DraftDisplay.Get();
		if (draftDisplay != null)
		{
			draftDisplay.TriggerRedraft(cheating: true);
		}
		SetClientState(m_undergroundActive ? ArenaClientStateType.Underground_Redraft : ArenaClientStateType.Normal_Redraft);
	}

	public bool IsRedraftCard(string cardId, TAG_PREMIUM premium)
	{
		CollectionDeck redraftDeck = GetRedraftDeck();
		if (redraftDeck != null && redraftDeck.GetCardCountAllMatchingSlots(cardId, premium) > 0)
		{
			return true;
		}
		return false;
	}

	public bool IsClientStateInAnyDrafting()
	{
		if (m_currentClientState != ArenaClientStateType.Normal_Draft && m_currentClientState != ArenaClientStateType.Normal_Redraft && m_currentClientState != ArenaClientStateType.Underground_Draft)
		{
			return m_currentClientState == ArenaClientStateType.Underground_Redraft;
		}
		return true;
	}

	public ArenaClientStateType GetCurrentClientState()
	{
		return m_currentClientState;
	}

	public ArenaClientStateType GetPreviousClientState()
	{
		return m_previousClientState;
	}

	public void SetClientState(ArenaClientStateType state)
	{
		if (m_currentClientState != state)
		{
			if (state == ArenaClientStateType.Normal_Landing || state == ArenaClientStateType.Underground_Landing)
			{
				m_chosenIndex = 0;
			}
			m_previousClientState = m_currentClientState;
			m_currentClientState = state;
			this.OnArenaClientStateUpdated?.Invoke(m_currentClientState);
		}
	}

	private static CollectionDeck CreateBlankCollectionDeck(long deckId = 0L)
	{
		return new CollectionDeck
		{
			ID = deckId,
			Type = DeckType.DRAFT_DECK,
			FormatType = FormatType.FT_WILD
		};
	}
}
