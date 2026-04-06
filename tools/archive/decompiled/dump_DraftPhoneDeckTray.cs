using System.Collections;
using UnityEngine;

public class DraftPhoneDeckTray : BasePhoneDeckTray
{
	private static DraftPhoneDeckTray s_instance;

	private bool m_showDisablePremiumPrompt = true;

	protected override void Awake()
	{
		base.Awake();
		s_instance = this;
		DraftManager draftManager = DraftManager.Get();
		draftManager?.RegisterDraftDeckSetListener(OnDraftDeckInitialized);
		draftManager?.RegisterRedraftDeckSetListener(OnRedraftDeckInitialized);
		if (m_cardsContent != null)
		{
			m_cardsContent.RegisterCardTileHeldListener(OnCardTileHeld);
			m_cardsContent.RegisterCardTileReleaseListener(OnCardTileRelease);
			m_cardsContent.RegisterCardCountUpdated(OnCardCountUpdated);
		}
		CollectionInputMgr collectionInputMgr = CollectionInputMgr.Get();
		if (collectionInputMgr != null)
		{
			collectionInputMgr.SetScrollbar(m_scrollbar);
		}
		CollectionDeck collectionDeck = draftManager?.GetDraftDeck();
		if (collectionDeck != null)
		{
			OnDraftDeckInitialized(collectionDeck);
		}
		CollectionDeck collectionDeck2 = draftManager?.GetRedraftDeck();
		if (collectionDeck2 != null)
		{
			OnRedraftDeckInitialized(collectionDeck2);
		}
	}

	private void OnDestroy()
	{
		DraftManager draftManager = DraftManager.Get();
		draftManager?.RemoveDraftDeckSetListener(OnDraftDeckInitialized);
		draftManager?.RemoveRedraftDeckSetListener(OnRedraftDeckInitialized);
		CollectionManager.Get()?.ClearEditedDeck();
		s_instance = null;
	}

	public static DraftPhoneDeckTray Get()
	{
		return s_instance;
	}

	public void Initialize()
	{
		DraftManager draftManager = DraftManager.Get();
		if (draftManager != null)
		{
			CollectionDeck draftDeck = draftManager.GetDraftDeck();
			if (draftDeck != null)
			{
				OnDraftDeckInitialized(draftDeck);
			}
			CollectionDeck redraftDeck = draftManager.GetRedraftDeck();
			if (redraftDeck != null)
			{
				OnRedraftDeckInitialized(redraftDeck);
			}
		}
	}

	private void OnDraftDeckInitialized(CollectionDeck draftDeck)
	{
		if (draftDeck == null)
		{
			Debug.LogError("Draft deck is null.");
			return;
		}
		CollectionManager.Get().SetEditedDeck(draftDeck);
		OnCardCountUpdated(draftDeck.GetTotalCardCount(), draftDeck.GetMaxCardCount());
		if (m_cardsContent != null)
		{
			m_cardsContent.UpdateCardList(string.Empty, draftDeck);
		}
	}

	private void OnRedraftDeckInitialized(CollectionDeck redraftDeck)
	{
		if (redraftDeck == null)
		{
			Debug.LogError("Redraft deck is null.");
			return;
		}
		ArenaDeckTrayCardListContent arenaDeckTrayCardListContent = m_cardsContent as ArenaDeckTrayCardListContent;
		if (arenaDeckTrayCardListContent != null)
		{
			bool flag = DraftManager.Get().IsRedrafting();
			arenaDeckTrayCardListContent.UpdateRedraftCardList(string.Empty, flag ? redraftDeck : new CollectionDeck());
		}
	}

	protected override void ShowDeckBigCard(DeckTrayDeckTileVisual cardTile, float delay = 0f)
	{
		if (ArenaInputManager.Get() == null || !ArenaInputManager.Get().HasHeldCard())
		{
			base.ShowDeckBigCard(cardTile, delay);
		}
	}

	private IEnumerator ShowBigCard(DeckTrayDeckTileVisual cardTile, float delay)
	{
		ShowDeckBigCard(cardTile, delay);
		yield return new WaitForSeconds(delay);
		m_showDisablePremiumPrompt = false;
	}

	protected override void OnCardTilePress(DeckTrayDeckTileVisual cardTile)
	{
		if (UniversalInputManager.Get().IsTouchMode())
		{
			m_showDisablePremiumPrompt = true;
			StartCoroutine(ShowBigCard(cardTile, 0.2f));
		}
		else if (CollectionInputMgr.Get() != null)
		{
			HideDeckBigCard(cardTile);
		}
	}

	private void OnCardTileHeld(DeckTrayDeckTileVisual cardTile)
	{
		if (CollectionInputMgr.Get() != null && cardTile.GetActor().GetPremium() != TAG_PREMIUM.NORMAL)
		{
			CollectionInputMgr.Get().GrabCardTile(cardTile, removeCard: false, OnDeckTileDropped);
		}
	}

	private void OnCardTileRelease(DeckTrayDeckTileVisual cardTile)
	{
		if (!m_isScrolling)
		{
			StopCoroutine("ShowBigCard");
			HideDeckBigCard(cardTile, force: true);
			if (SceneMgr.Get().GetMode() == SceneMgr.Mode.DRAFT && cardTile.GetActor().GetPremium() != TAG_PREMIUM.NORMAL && m_showDisablePremiumPrompt)
			{
				DraftManager.Get().PromptToDisablePremium();
			}
		}
	}

	protected override void OnCardCountUpdated(int cardCount, int maxCount)
	{
		string empty = string.Empty;
		string empty2 = string.Empty;
		if (m_headerLabel != null)
		{
			m_headerLabel.SetActive(value: true);
		}
		empty = GameStrings.Get("GLUE_DECK_TRAY_CARD_COUNT_LABEL");
		empty2 = GameStrings.Format("GLUE_DECK_TRAY_COUNT", cardCount, maxCount);
		if (m_countLabelText != null)
		{
			m_countLabelText.Text = empty;
		}
		if (m_countText != null)
		{
			m_countText.Text = empty2;
		}
	}

	private void OnDeckTileDropped()
	{
		if (!m_isScrolling)
		{
			DraftManager.Get().PromptToDisablePremium();
		}
	}
}
