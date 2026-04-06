using System;
using System.Collections;
using System.Collections.Generic;
using Blizzard.T5.Core;
using Blizzard.T5.Game.Spells;
using UnityEngine;

[CustomEditClass]
public class ChoiceCardMgr : MonoBehaviour
{
	[Serializable]
	public class CommonData
	{
		public float m_FriendlyCardWidth = 2.85f;

		public float m_FriendlyCardWidthTrinket = 2.4f;

		public float m_OpponentCardWidth = 1.5f;

		public int m_MaxCardsBeforeAdjusting = 3;

		public PlatformDependentValue<float> m_FourCardScale = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 1f,
			Tablet = 1f,
			Phone = 0.8f
		};

		public PlatformDependentValue<float> m_FiveCardScale = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 0.85f,
			Tablet = 0.85f,
			Phone = 0.65f
		};

		public PlatformDependentValue<float> m_SixPlusCardScale = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 0.7f,
			Tablet = 0.7f,
			Phone = 0.55f
		};

		public PlatformDependentValue<float> m_TrinketCardAdditionalScale = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 1f,
			Tablet = 1f,
			Phone = 0.8f
		};
	}

	[Serializable]
	public class ChoiceData
	{
		public string m_FriendlyBoneName = "FriendlyChoice";

		public string m_BGTrinketFriendlyBoneName = "BGTrinketFriendlyChoice";

		public string m_OpponentBoneName = "OpponentChoice";

		public string m_BannerBoneName = "ChoiceBanner";

		public string m_ToggleChoiceButtonBoneName = "ToggleChoiceButton";

		public string m_ConfirmChoiceButtonBoneName = "ConfirmChoiceButton";

		public string m_BGTrinketConfirmChoiceButtonBoneName = "BGTrinketConfirmChoiceButton";

		public string m_BGTrinketToggleChoiceButtonBoneName = "BGTrinketToggleChoiceButton";

		public float m_MinShowTime = 1f;

		public Banner m_BannerPrefab;

		[CustomEditField(T = EditType.GAME_OBJECT)]
		public string m_ButtonPrefab;

		public GameObject m_MagicItemShopBackgroundPrefab;

		public GameObject m_xPrefab;

		public float m_CardShowTime = 0.2f;

		public float m_CardHideTime = 0.2f;

		public float m_UiShowTime = 0.5f;

		public float m_HorizontalPadding = 0.75f;

		public PlatformDependentValue<float> m_HorizontalPaddingTrinketMultiplier = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 1f,
			Tablet = 1f,
			Phone = 0.8f
		};

		public PlatformDependentValue<float> m_HorizontalPaddingFourCards = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 0.6f,
			Tablet = 0.5f,
			Phone = 0.4f
		};

		public PlatformDependentValue<float> m_HorizontalPaddingFiveCards = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 0.3f,
			Tablet = 0.3f,
			Phone = 0.3f
		};

		public PlatformDependentValue<float> m_HorizontalPaddingSixPlusCards = new PlatformDependentValue<float>(PlatformCategory.Screen)
		{
			PC = 0.2f,
			Tablet = 0.2f,
			Phone = 0.2f
		};
	}

	[Serializable]
	public class SubOptionData
	{
		public string m_BoneName = "SubOption";

		public float m_AdjacentCardXOffset = 0.75f;

		public float m_PhoneMaxAdjacentCardXOffset = 0.1f;

		public float m_MinionParentXOffset = 0.9f;

		public float m_CardShowTime = 0.2f;
	}

	[Serializable]
	public class ChoiceEffectData
	{
		public bool m_AlwaysPlayEffect;

		public bool m_PlayOncePerCard;

		public Spell m_Spell;
	}

	[Serializable]
	public class TagSpecificChoiceEffect
	{
		public GAME_TAG m_Tag;

		public List<TagValueSpecificChoiceEffect> m_ValueSpellMap;
	}

	[Serializable]
	public class TagValueSpecificChoiceEffect
	{
		public int m_Value;

		public ChoiceEffectData m_ChoiceEffectData;
	}

	[Serializable]
	public class CardSpecificChoiceEffect
	{
		public string m_CardID;

		public ChoiceEffectData m_ChoiceEffectData;
	}

	[Serializable]
	public class TagPostChoiceEffect
	{
		public GAME_TAG m_Tag;

		public Spell m_SpellSelectedCards;

		public Spell m_SpellUnselectedCards;
	}

	[Serializable]
	public class StarshipUIData
	{
		public const string STARSHIP_LAUNCH_CARDID = "GDB_905";

		public Vector3 m_RotationPC;

		public Vector3 m_ScalePC;

		[Tooltip("this is relative to the bone ToggleChoiceButton")]
		public Vector3 m_PositionPC;

		public Vector3 m_RotationMobile;

		public Vector3 m_ScaleMobile;

		[Tooltip("this is relative to the bone ToggleChoiceButton_phone")]
		public Vector3 m_PositionMobile;
	}

	[Serializable]
	public class ShopChoiceUIData
	{
		[CustomEditField(T = EditType.SPELL)]
		public string m_ShopVignetteSpellPath;

		public int m_BartenderDBID;
	}

	private class SubOptionState
	{
		public List<Card> m_cards = new List<Card>();

		public Card m_parentCard;
	}

	public struct TransformData
	{
		public Vector3 Position { get; set; }

		public Vector3 RotationAngles { get; set; }

		public Vector3 LocalScale { get; set; }
	}

	public class ChoiceState
	{
		public int m_choiceID;

		public bool m_isFriendly;

		public List<Card> m_cards = new List<Card>();

		public List<TransformData> m_cardTransforms = new List<TransformData>();

		public bool m_waitingToStart;

		public bool m_hasBeenRevealed;

		public bool m_hasBeenConcealed;

		public bool m_hideChosen;

		public int m_choiceActor;

		public PowerTaskList m_preTaskList;

		public int m_sourceEntityId;

		public List<Entity> m_chosenEntities;

		public Map<int, GameObject> m_xObjs;

		public List<Spell> m_choiceEffectSpells = new List<Spell>();

		public List<Spell> m_postChoiceSpells = new List<Spell>();

		public bool m_showFromDeck;

		public bool m_hideChoiceUI;

		public bool m_isSubOptionChoice;

		public bool m_isTitanAbility;

		public bool m_isMagicItemDiscover;

		public bool m_isShopChoice;

		public bool m_isLaunchpadAbility;

		public bool m_isRewindChoice;
	}

	public CommonData m_CommonData = new CommonData();

	public ChoiceData m_ChoiceData = new ChoiceData();

	public StarshipUIData m_StarshipUIData = new StarshipUIData();

	public ShopChoiceUIData m_ShopChoiceUIData = new ShopChoiceUIData();

	public SubOptionData m_SubOptionData = new SubOptionData();

	public List<TagSpecificChoiceEffect> m_TagSpecificChoiceEffectData = new List<TagSpecificChoiceEffect>();

	public List<CardSpecificChoiceEffect> m_CardSpecificChoiceEffectData = new List<CardSpecificChoiceEffect>();

	public List<TagPostChoiceEffect> m_TagPostChoiceEffectData = new List<TagPostChoiceEffect>();

	private ChoiceEffectData m_DiscoverChoiceEffectData = new ChoiceEffectData();

	private ChoiceEffectData m_AdaptChoiceEffectData = new ChoiceEffectData();

	private ChoiceEffectData m_GearsChoiceEffectData = new ChoiceEffectData();

	private ChoiceEffectData m_DragonChoiceEffectData = new ChoiceEffectData();

	private ChoiceEffectData m_TrinketChoiceEffectData = new ChoiceEffectData();

	private ISpell m_customChoiceRevealSpell;

	private static readonly Vector3 INVISIBLE_SCALE = new Vector3(0.0001f, 0.0001f, 0.0001f);

	private static ChoiceCardMgr s_instance;

	private SubOptionState m_subOptionState;

	private SubOptionState m_pendingCancelSubOptionState;

	private Dictionary<int, ChoiceState> m_choiceStateMap = new Dictionary<int, ChoiceState>();

	private List<int> m_lastChosenEntityIds;

	private Banner m_choiceBanner;

	private GameObject m_magicItemShopBackground;

	private NormalButton m_toggleChoiceButton;

	private NormalButton m_confirmChoiceButton;

	private bool m_friendlyChoicesShown;

	private bool m_restoreEnlargedHand;

	private ChoiceState m_lastShownChoiceState;

	private const string ALGALONS_VISION = "TTN_717t";

	private const float ALGALONS_CHOICE_CARDS_OFFSET_X = 2f;

	private List<int> m_starshipPiecesToView;

	private StarshipHUDManager m_starshipHUDManager;

	private RewindUIManager m_rewindHUDManager;

	public const string REWIND_CHOICE_CARDID = "TIME_000tb";

	public const string KEEP_CHOICE_CARDID = "TIME_000ta";

	private BattlegroundsShopChoice m_shopChoice = new BattlegroundsShopChoice();

	public event Action<bool> ChoiceUIStateChanged;

	private void Awake()
	{
		s_instance = this;
		foreach (TagSpecificChoiceEffect tagSpecificChoiceEffectDatum in m_TagSpecificChoiceEffectData)
		{
			switch (tagSpecificChoiceEffectDatum.m_Tag)
			{
			case GAME_TAG.USE_DISCOVER_VISUALS:
				if (tagSpecificChoiceEffectDatum.m_ValueSpellMap.Count > 0)
				{
					m_DiscoverChoiceEffectData = tagSpecificChoiceEffectDatum.m_ValueSpellMap[0].m_ChoiceEffectData;
				}
				break;
			case GAME_TAG.ADAPT:
				if (tagSpecificChoiceEffectDatum.m_ValueSpellMap.Count > 0)
				{
					m_AdaptChoiceEffectData = tagSpecificChoiceEffectDatum.m_ValueSpellMap[0].m_ChoiceEffectData;
				}
				break;
			case GAME_TAG.GEARS:
				if (tagSpecificChoiceEffectDatum.m_ValueSpellMap.Count > 0)
				{
					m_GearsChoiceEffectData = tagSpecificChoiceEffectDatum.m_ValueSpellMap[0].m_ChoiceEffectData;
				}
				break;
			case GAME_TAG.GOOD_OL_GENERIC_FRIENDLY_DRAGON_DISCOVER_VISUALS:
				if (tagSpecificChoiceEffectDatum.m_ValueSpellMap.Count > 0)
				{
					m_DragonChoiceEffectData = tagSpecificChoiceEffectDatum.m_ValueSpellMap[0].m_ChoiceEffectData;
				}
				break;
			case GAME_TAG.BACON_IS_MAGIC_ITEM_DISCOVER:
				if (tagSpecificChoiceEffectDatum.m_ValueSpellMap.Count > 0)
				{
					m_TrinketChoiceEffectData = tagSpecificChoiceEffectDatum.m_ValueSpellMap[0].m_ChoiceEffectData;
				}
				break;
			}
		}
		m_starshipPiecesToView = new List<int>();
		m_lastChosenEntityIds = new List<int>();
	}

	private void OnDestroy()
	{
		s_instance = null;
	}

	private void Start()
	{
		if (GameState.Get() == null)
		{
			Debug.LogError($"ChoiceCardMgr.Start() - GameState already Shutdown before ChoiceCardMgr was loaded.");
			return;
		}
		GameState.Get().RegisterEntityChoicesReceivedListener(OnEntityChoicesReceived);
		GameState.Get().RegisterEntitiesChosenReceivedListener(OnEntitiesChosenReceived);
		GameState.Get().RegisterGameOverListener(OnGameOver);
	}

	public static ChoiceCardMgr Get()
	{
		return s_instance;
	}

	public bool RestoreEnlargedHandAfterChoice()
	{
		return m_restoreEnlargedHand;
	}

	public Banner GetChoiceBanner()
	{
		return m_choiceBanner;
	}

	public GameObject GetChoiceBackground()
	{
		return m_magicItemShopBackground;
	}

	public NormalButton GetToggleButton()
	{
		return m_toggleChoiceButton;
	}

	public List<Card> GetFriendlyCards()
	{
		if (m_subOptionState != null)
		{
			return m_subOptionState.m_cards;
		}
		int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
		if (m_choiceStateMap.TryGetValue(friendlyPlayerId, out var value))
		{
			return value.m_cards;
		}
		return null;
	}

	private void EnableConfirmButton(bool enable)
	{
		if (m_confirmChoiceButton == null)
		{
			Log.All.PrintWarning("Enable Confirm Button called when confirm button is null");
			return;
		}
		PlayMakerFSM componentInChildren = m_confirmChoiceButton.GetComponentInChildren<PlayMakerFSM>();
		if (componentInChildren != null && componentInChildren.FsmVariables.GetFsmBool("enabled").Value != enable)
		{
			componentInChildren.FsmVariables.GetFsmBool("enabled").Value = enable;
			componentInChildren.SendEvent("Birth");
		}
	}

	public void ChooseBGTrinket(Entity entity)
	{
		if (!IsFriendlyMagicItemDiscover())
		{
			return;
		}
		foreach (Entity chosenEntity in GameState.Get().GetChosenEntities())
		{
			ManaCrystalMgr.Get().CancelAllProposedMana(chosenEntity);
		}
		GameState.Get().ClearFriendlyChoicesList();
		List<Card> friendlyCards = GetFriendlyCards();
		if (friendlyCards != null)
		{
			foreach (Card item in friendlyCards)
			{
				Actor actor = item.GetActor();
				if (!(actor == null))
				{
					ActorStateMgr actorStateMgr = actor.GetActorStateMgr();
					if (actorStateMgr != null)
					{
						actorStateMgr.ChangeState((item.GetEntity() == entity) ? ActorStateType.CARD_SELECTED : ActorStateType.NONE);
					}
				}
			}
		}
		Network.Get().SendMulliganChooseOneTentativeSelect(entity.GetEntityId(), isConfirmation: false);
		if (m_magicItemShopBackground != null)
		{
			BaconTrinketBacker component = m_magicItemShopBackground.GetComponent<BaconTrinketBacker>();
			if (component != null)
			{
				component.UpdateCoinText(entity.GetTag(GAME_TAG.COST));
			}
		}
		ManaCrystalMgr.Get().ProposeManaCrystalUsage(entity);
		EnableConfirmButton(entity != null);
	}

	public void OnChooseOneTentativeSelection(Network.MulliganChooseOneTentativeSelection selection)
	{
		Debug.LogWarning($"On ChooseOneTentativeSelection {selection}");
	}

	public bool CardIsFirstChoice(Card card)
	{
		List<Card> friendlyCards = GetFriendlyCards();
		if (friendlyCards == null)
		{
			string text = ((card == null) ? "" : card.name);
			Debug.LogErrorFormat("ChoiceCardMgr.CardIsFirstChoice() - choices is null. card parameter = '" + text + "'");
			return false;
		}
		if (friendlyCards.Count == 0)
		{
			return false;
		}
		Card card2 = friendlyCards[0];
		if (card2 == null)
		{
			string text2 = ((card == null) ? "" : card.name);
			Debug.LogErrorFormat("ChoiceCardMgr.CardIsFirstChoice() - firstChoice is null. card parameter = '" + text2 + "'");
			return false;
		}
		if (card == null)
		{
			Debug.LogErrorFormat("ChoiceCardMgr.CardIsFirstChoice() - card parameter is null. firstChoice = '" + card2.name + "'");
			return false;
		}
		return card2.GetEntity()?.GetEntityId() == card.GetEntity()?.GetEntityId();
	}

	public bool IsShown()
	{
		if (m_subOptionState != null)
		{
			return true;
		}
		if (m_choiceStateMap.Count > 0)
		{
			return true;
		}
		return false;
	}

	public bool IsFriendlyShown()
	{
		if (m_subOptionState != null)
		{
			return true;
		}
		int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
		if (m_choiceStateMap.ContainsKey(friendlyPlayerId))
		{
			return true;
		}
		return false;
	}

	public bool HasSubOption()
	{
		return m_subOptionState != null;
	}

	public Card GetSubOptionParentCard()
	{
		if (m_subOptionState != null)
		{
			return m_subOptionState.m_parentCard;
		}
		return null;
	}

	public void ClearSubOptions()
	{
		int valueOrDefault = (m_subOptionState?.m_parentCard?.GetEntity()?.GetControllerId()).GetValueOrDefault();
		m_subOptionState = null;
		ChoiceState choiceStateForPlayer = GetChoiceStateForPlayer(valueOrDefault);
		DeactivateChoiceEffects(choiceStateForPlayer);
		OnFinishedConcealChoices(valueOrDefault);
	}

	public bool ShowSubOptions(Card parentCard, List<int> dynamicSubOptionEntitIds = null)
	{
		Entity entity = parentCard.GetEntity();
		if (entity != null && !entity.IsControlledByFriendlySidePlayer())
		{
			return false;
		}
		if (dynamicSubOptionEntitIds != null)
		{
			for (int i = 0; i < dynamicSubOptionEntitIds.Count; i++)
			{
				Entity entity2 = GameState.Get().GetEntity(dynamicSubOptionEntitIds[i]);
				if (entity2 != null)
				{
					entity.AddSubCard(entity2);
				}
			}
		}
		m_subOptionState = new SubOptionState();
		m_subOptionState.m_parentCard = parentCard;
		if (entity != null && !parentCard.GetEntity().IsMinion())
		{
			parentCard.ActivateChooseOneEffects();
		}
		StartCoroutine(WaitThenShowSubOptions());
		return true;
	}

	public void QuenePendingCancelSubOptions()
	{
		m_pendingCancelSubOptionState = m_subOptionState;
	}

	public bool HasPendingCancelSubOptions()
	{
		if (m_pendingCancelSubOptionState != null)
		{
			return m_pendingCancelSubOptionState == m_subOptionState;
		}
		return false;
	}

	public void ClearPendingCancelSubOptions()
	{
		m_pendingCancelSubOptionState = null;
	}

	public void ForceUpdateAllSubcards()
	{
		GameState.Get()?.ForceUpdateAllSubcards();
	}

	public bool IsWaitingToShowSubOptions()
	{
		if (!HasSubOption())
		{
			return false;
		}
		Entity entity = m_subOptionState.m_parentCard.GetEntity();
		Player controller = entity.GetController();
		Zone zone = m_subOptionState.m_parentCard.GetZone();
		if (entity.IsMinion())
		{
			if (GameUtils.IsProcessingSubcardsForEntity(entity))
			{
				return true;
			}
			if (zone.m_ServerTag == TAG_ZONE.SETASIDE)
			{
				return false;
			}
			ZonePlay battlefieldZone = controller.GetBattlefieldZone();
			ZoneHand handZone = controller.GetHandZone();
			if (zone != battlefieldZone && zone != handZone)
			{
				return true;
			}
			if (m_subOptionState.m_parentCard.GetZonePosition() == 0)
			{
				return true;
			}
		}
		if (entity.IsHero())
		{
			ZoneHero heroZone = controller.GetHeroZone();
			if (zone != heroZone)
			{
				return true;
			}
			if (!m_subOptionState.m_parentCard.IsActorReady())
			{
				return true;
			}
		}
		if (!entity.HasSubCards())
		{
			ForceUpdateAllSubcards();
			return true;
		}
		return false;
	}

	public void CancelSubOptions()
	{
		if (!HasSubOption())
		{
			return;
		}
		Entity entity = m_subOptionState.m_parentCard.GetEntity();
		Card card = entity.GetCard();
		for (int i = 0; i < m_subOptionState.m_cards.Count; i++)
		{
			Spell subOptionSpell = card.GetSubOptionSpell(i, 0, loadIfNeeded: false);
			if ((bool)subOptionSpell)
			{
				SpellStateType activeState = subOptionSpell.GetActiveState();
				if (activeState != SpellStateType.NONE && activeState != SpellStateType.CANCEL)
				{
					subOptionSpell.ActivateState(SpellStateType.CANCEL);
				}
			}
		}
		card.ActivateHandStateSpells();
		if (entity.IsHeroPower() || entity.IsGameModeButton())
		{
			entity.SetTagAndHandleChange(GAME_TAG.EXHAUSTED, 0);
		}
		HideSubOptions();
	}

	public void OnSubOptionClicked(Entity chosenEntity)
	{
		if (HasSubOption())
		{
			HideSubOptions(chosenEntity);
		}
	}

	public bool HasChoices()
	{
		return m_choiceStateMap.Count > 0;
	}

	public bool HasChoices(int playerId)
	{
		return m_choiceStateMap.ContainsKey(playerId);
	}

	public ChoiceState GetFriendlyChoiceState()
	{
		Player friendlySidePlayer = GameState.Get().GetFriendlySidePlayer();
		if (friendlySidePlayer == null)
		{
			return null;
		}
		return GetChoiceStateForPlayer(friendlySidePlayer.GetPlayerId());
	}

	public bool IsFriendlyMagicItemDiscover()
	{
		return GetFriendlyChoiceState()?.m_isMagicItemDiscover ?? false;
	}

	public bool IsFriendlyShopChoice()
	{
		return GetFriendlyChoiceState()?.m_isShopChoice ?? false;
	}

	public void NotifyShopChoiceCardHeld(Entity heldEntity)
	{
		m_shopChoice.NotifyHoldingChoice(heldEntity);
	}

	public void NotifyShopChoiceCardDroped(Entity heldEntity)
	{
		m_shopChoice.NotifyDropChoice(heldEntity);
	}

	public ChoiceState GetChoiceStateForPlayer(int playerId)
	{
		if (!HasChoices(playerId))
		{
			return null;
		}
		return m_choiceStateMap[playerId];
	}

	public bool HasFriendlyChoices()
	{
		int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
		return HasChoices(friendlyPlayerId);
	}

	public PowerTaskList GetPreChoiceTaskList(int playerId)
	{
		if (m_choiceStateMap.TryGetValue(playerId, out var value))
		{
			return value.m_preTaskList;
		}
		return null;
	}

	public PowerTaskList GetFriendlyPreChoiceTaskList()
	{
		int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
		return GetPreChoiceTaskList(friendlyPlayerId);
	}

	public bool IsWaitingToStartChoices(int playerId)
	{
		if (m_choiceStateMap.TryGetValue(playerId, out var value))
		{
			return value.m_waitingToStart;
		}
		return false;
	}

	public bool IsFriendlyWaitingToStartChoices()
	{
		int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
		return IsWaitingToStartChoices(friendlyPlayerId);
	}

	public void OnSendChoices(Network.EntityChoices choicePacket, List<Entity> chosenEntities)
	{
		if (choicePacket.ChoiceType == CHOICE_TYPE.GENERAL)
		{
			int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
			if (!m_choiceStateMap.TryGetValue(friendlyPlayerId, out var value))
			{
				Error.AddDevFatal("ChoiceCardMgr.OnSendChoices() - there is no ChoiceState for friendly player {0}", friendlyPlayerId);
			}
			else if (value.m_isShopChoice)
			{
				m_shopChoice.SelectedChoice(chosenEntities[0]);
				PlayBuyingChoiceLine(chosenEntities[0]);
				value.m_chosenEntities = new List<Entity>(chosenEntities);
			}
			else
			{
				value.m_chosenEntities = new List<Entity>(chosenEntities);
				ConcealChoicesFromInput(friendlyPlayerId, value);
			}
		}
	}

	public void OnChosenEntityAdded(Entity entity)
	{
		if (entity == null)
		{
			Log.Gameplay.PrintError("ChoiceCardMgr.OnChosenEntityAdded(): null entity passed!");
			return;
		}
		Network.EntityChoices friendlyEntityChoices = GameState.Get().GetFriendlyEntityChoices();
		if (friendlyEntityChoices == null || friendlyEntityChoices.IsSingleChoice() || !m_choiceStateMap.ContainsKey(GameState.Get().GetFriendlyPlayerId()))
		{
			return;
		}
		ChoiceState choiceState = m_choiceStateMap[GameState.Get().GetFriendlyPlayerId()];
		if (choiceState.m_xObjs == null)
		{
			Log.Gameplay.PrintError("ChoiceCardMgr.OnChosenEntityAdded(): ChoiceState does not have an m_xObjs map!");
		}
		else if (!choiceState.m_xObjs.ContainsKey(entity.GetEntityId()))
		{
			Card card = entity.GetCard();
			if (card == null)
			{
				Log.Gameplay.PrintError("ChoiceCardMgr.OnChosenEntityAdded(): Entity does not have a card!");
				return;
			}
			GameObject gameObject = UnityEngine.Object.Instantiate(m_ChoiceData.m_xPrefab);
			TransformUtil.AttachAndPreserveLocalTransform(gameObject.transform, card.transform);
			gameObject.transform.localRotation = Quaternion.identity;
			gameObject.transform.localPosition = Vector3.zero;
			choiceState.m_xObjs.Add(entity.GetEntityId(), gameObject);
		}
	}

	public void OnChosenEntityRemoved(Entity entity)
	{
		if (entity == null)
		{
			Log.Gameplay.PrintError("ChoiceCardMgr.OnChosenEntityRemoved(): null entity passed!");
			return;
		}
		Network.EntityChoices friendlyEntityChoices = GameState.Get().GetFriendlyEntityChoices();
		if (friendlyEntityChoices == null || friendlyEntityChoices.IsSingleChoice() || !m_choiceStateMap.ContainsKey(GameState.Get().GetFriendlyPlayerId()))
		{
			return;
		}
		ChoiceState choiceState = m_choiceStateMap[GameState.Get().GetFriendlyPlayerId()];
		if (choiceState.m_xObjs == null)
		{
			Log.Gameplay.PrintError("ChoiceCardMgr.OnChosenEntityRemoved(): ChoiceState does not have an m_xObjs map!");
			return;
		}
		int entityId = entity.GetEntityId();
		if (choiceState.m_xObjs.ContainsKey(entityId))
		{
			GameObject obj = choiceState.m_xObjs[entityId];
			choiceState.m_xObjs.Remove(entityId);
			UnityEngine.Object.Destroy(obj);
		}
	}

	private void OnEntityChoicesReceived(Network.EntityChoices choices, PowerTaskList preChoiceTaskList, object userData)
	{
		if (choices.ChoiceType == CHOICE_TYPE.GENERAL)
		{
			if (choices == null)
			{
				string text = $"ChoiceCardMgr.OnEntityChoicesReceived() - choices is null.";
				TelemetryManager.Client().SendLiveIssue("Gameplay_ChoiceCardMgr", text);
				Log.Power.Print(text);
			}
			else if (WaitThenStartChoices(choices, preChoiceTaskList) != null)
			{
				StartCoroutine(WaitThenStartChoices(choices, preChoiceTaskList));
			}
			else
			{
				string text2 = $"ChoiceCardMgr.OnEntityChoicesReceived() - WaitThenStartChoices failed (returned null).";
				TelemetryManager.Client().SendLiveIssue("Gameplay_ChoiceCardMgr", text2);
				Log.Power.Print(text2);
			}
		}
	}

	private bool OnEntitiesChosenReceived(Network.EntitiesChosen chosen, object userData)
	{
		if (chosen.ChoiceType != CHOICE_TYPE.GENERAL)
		{
			return false;
		}
		m_lastChosenEntityIds.Clear();
		for (int i = 0; i < chosen.Entities.Count; i++)
		{
			m_lastChosenEntityIds.Add(chosen.Entities[i]);
		}
		StartCoroutine(WaitThenConcealChoicesFromPacket(chosen));
		return true;
	}

	private void OnGameOver(TAG_PLAYSTATE playState, object userData)
	{
		StopAllCoroutines();
		CancelSubOptions();
		CancelChoices();
		HideLastChosenEntitesOnGameOver();
	}

	private IEnumerator WaitThenStartChoices(Network.EntityChoices choices, PowerTaskList preChoiceTaskList)
	{
		int playerId = choices.PlayerId;
		ChoiceState state = new ChoiceState();
		if (m_choiceStateMap.ContainsKey(playerId))
		{
			m_choiceStateMap[playerId] = state;
		}
		else
		{
			m_choiceStateMap.Add(playerId, state);
		}
		state.m_waitingToStart = true;
		state.m_hasBeenConcealed = false;
		state.m_hasBeenRevealed = false;
		state.m_choiceID = choices.ID;
		state.m_hideChosen = choices.HideChosen;
		state.m_sourceEntityId = choices.Source;
		state.m_preTaskList = preChoiceTaskList;
		state.m_xObjs = new Map<int, GameObject>();
		Entity entity = GameState.Get().GetEntity(choices.Source);
		if (entity != null)
		{
			state.m_showFromDeck = entity.HasTag(GAME_TAG.SHOW_DISCOVER_FROM_DECK);
			state.m_isMagicItemDiscover = entity.HasTag(GAME_TAG.BACON_IS_MAGIC_ITEM_DISCOVER);
			state.m_isShopChoice = entity.HasTag(GAME_TAG.IS_SHOP_CHOICE);
		}
		if (state.m_isMagicItemDiscover)
		{
			m_ChoiceData.m_CardHideTime = 0f;
			m_ChoiceData.m_CardShowTime = 0f;
		}
		else
		{
			m_ChoiceData.m_CardHideTime = 0.2f;
			m_ChoiceData.m_CardShowTime = 0.2f;
		}
		PowerProcessor powerProcessor = GameState.Get().GetPowerProcessor();
		if (powerProcessor.HasTaskList(state.m_preTaskList) && (GameState.Get()?.GameScenarioAllowsPowerPrinting() ?? true))
		{
			Log.Power.Print("ChoiceCardMgr.WaitThenShowChoices() - id={0} WAIT for taskList {1}", choices.ID, preChoiceTaskList.GetId());
		}
		while (powerProcessor.HasTaskList(state.m_preTaskList))
		{
			yield return null;
		}
		HistoryManager historyManager = HistoryManager.Get();
		if (historyManager != null && historyManager.HasBigCard() && historyManager.GetCurrentBigCard().GetEntity().GetEntityId() == state.m_sourceEntityId)
		{
			historyManager.HandleClickOnBigCard(historyManager.GetCurrentBigCard());
		}
		if (GameState.Get()?.GameScenarioAllowsPowerPrinting() ?? true)
		{
			Log.Power.Print("ChoiceCardMgr.WaitThenShowChoices() - id={0} BEGIN", choices.ID);
		}
		List<Card> linkedChoiceCards = new List<Card>();
		Entity source = GameState.Get().GetEntity(state.m_sourceEntityId);
		for (int i = 0; i < choices.Entities.Count; i++)
		{
			int id = choices.Entities[i];
			Entity entity2 = GameState.Get().GetEntity(id);
			if (entity2 == null)
			{
				string text = $"ChoiceCardMgr.OnEntityChoicesReceived() - entity in choices was null.";
				TelemetryManager.Client().SendLiveIssue("Gameplay_ChoiceCardMgr", text);
				Error.AddDevFatal(text, entity2, i);
				continue;
			}
			Card card = entity2.GetCard();
			if (card == null)
			{
				Error.AddDevFatal("ChoiceCardMgr.WaitThenShowChoices() - Entity {0} (option {1}) has no Card", entity2, i);
				continue;
			}
			if (entity2.HasTag(GAME_TAG.LINKED_ENTITY))
			{
				int realTimeLinkedEntityId = entity2.GetRealTimeLinkedEntityId();
				Entity entity3 = GameState.Get().GetEntity(realTimeLinkedEntityId);
				if (entity3 != null && entity3.GetCard() != null)
				{
					linkedChoiceCards.Add(entity3.GetCard());
				}
			}
			state.m_cards.Add(card);
			StartCoroutine(LoadChoiceCardActors(source, entity2, card, state));
		}
		for (int j = 0; j < choices.UnchoosableEntities.Count; j++)
		{
			int id2 = choices.UnchoosableEntities[j];
			Entity entity4 = GameState.Get().GetEntity(id2);
			if (entity4 == null)
			{
				string text2 = $"ChoiceCardMgr.OnEntityChoicesReceived() - entity in UnchoosableEntities was null.";
				TelemetryManager.Client().SendLiveIssue("Gameplay_ChoiceCardMgr", text2);
				Error.AddDevFatal(text2, entity4, j);
			}
			else
			{
				Card card2 = entity4.GetCard();
				if (!(card2 == null))
				{
					state.m_cards.Add(card2);
					StartCoroutine(LoadChoiceCardActors(source, entity4, card2, state, choosable: false));
				}
			}
		}
		int i2 = 0;
		while (i2 < linkedChoiceCards.Count)
		{
			Card linkedCard = linkedChoiceCards[i2];
			while (linkedCard != null && !linkedCard.IsActorReady())
			{
				yield return null;
			}
			int num = i2 + 1;
			i2 = num;
		}
		i2 = 0;
		while (i2 < state.m_cards.Count)
		{
			Card linkedCard = state.m_cards[i2];
			while (!IsChoiceCardReady(linkedCard))
			{
				yield return null;
			}
			int num = i2 + 1;
			i2 = num;
		}
		int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
		bool friendly = playerId == friendlyPlayerId;
		if (friendly)
		{
			while (GameState.Get().IsTurnStartManagerBlockingInput())
			{
				if (GameState.Get().IsTurnStartManagerActive())
				{
					TurnStartManager.Get().NotifyOfStartOfTurnChoice();
				}
				yield return null;
			}
		}
		state.m_isFriendly = friendly;
		state.m_waitingToStart = false;
		if (DoesChoiceContainRewindOptions(state.m_cards))
		{
			state.m_isRewindChoice = true;
			UpdateHistoryOnRewindChoice(source);
			ShowRewindHUD(state);
		}
		else if (state.m_isShopChoice)
		{
			StartCoroutine(StartShopChoice(state));
		}
		else
		{
			PopulateTransformDatas(state);
			StartChoices(state);
		}
	}

	private IEnumerator LoadChoiceCardActors(Entity source, Entity entity, Card card, ChoiceState state, bool choosable = true)
	{
		while (!IsEntityReady(entity))
		{
			yield return null;
		}
		card.HideCard();
		while (!IsCardReady(card))
		{
			yield return null;
		}
		if (state.m_isShopChoice)
		{
			card.SetupPlayActorForShopChoice();
			yield break;
		}
		CHOICE_ACTOR cHOICE_ACTOR = CHOICE_ACTOR.CARD;
		if (source.HasTag(GAME_TAG.CHOICE_ACTOR_TYPE))
		{
			cHOICE_ACTOR = (CHOICE_ACTOR)source.GetTag(GAME_TAG.CHOICE_ACTOR_TYPE);
		}
		if ((uint)cHOICE_ACTOR > 1u && cHOICE_ACTOR == CHOICE_ACTOR.HERO)
		{
			LoadHeroChoiceCardActor(source, entity, card);
			card.ActivateHandStateSpells();
		}
		else
		{
			card.ForceLoadHandActor();
			card.ActivateHandStateSpells();
		}
	}

	private void LoadHeroChoiceCardActor(Entity source, Entity entity, Card card)
	{
		GameObject gameObject = AssetLoader.Get().InstantiatePrefab("Choose_Hero.prefab:1834beb8747ef06439f3a1b86a35ff3d", AssetLoadingOptions.IgnorePrefabPosition);
		if (gameObject == null)
		{
			Log.Gameplay.PrintWarning(string.Format("ChoiceCardManager.LoadHeroChoiceActor() - FAILED to load actor \"{0}\"", "Choose_Hero.prefab:1834beb8747ef06439f3a1b86a35ff3d"));
			return;
		}
		Actor component = gameObject.GetComponent<Actor>();
		if (component == null)
		{
			Log.Gameplay.PrintWarning(string.Format("ChoiceCardManager.LoadHeroChoiceActor() - ERROR actor \"{0}\" has no Actor component", "Choose_Hero.prefab:1834beb8747ef06439f3a1b86a35ff3d"));
			return;
		}
		if (card.GetActor() != null)
		{
			card.GetActor().Destroy();
		}
		card.SetActor(component);
		component.SetCard(card);
		component.SetCardDefFromCard(card);
		component.SetPremium(card.GetPremium());
		component.UpdateAllComponents();
		component.SetEntity(entity);
		component.UpdateAllComponents();
		component.SetUnlit();
		LayerUtils.SetLayer(component.gameObject, base.gameObject.layer);
		component.GetMeshRenderer().gameObject.layer = 8;
		ConfigureHeroChoiceActor(source, entity, component as HeroChoiceActor);
	}

	private void ConfigureHeroChoiceActor(Entity source, Entity entity, HeroChoiceActor actor)
	{
		if (actor == null)
		{
			return;
		}
		if (entity == null || source == null)
		{
			actor.SetNameTextActive(active: false);
			return;
		}
		CHOICE_NAME_DISPLAY cHOICE_NAME_DISPLAY = CHOICE_NAME_DISPLAY.INVALID;
		if (source.HasTag(GAME_TAG.CHOICE_NAME_DISPLAY_TYPE))
		{
			cHOICE_NAME_DISPLAY = (CHOICE_NAME_DISPLAY)source.GetTag(GAME_TAG.CHOICE_NAME_DISPLAY_TYPE);
		}
		switch (cHOICE_NAME_DISPLAY)
		{
		case CHOICE_NAME_DISPLAY.HERO:
			actor.SetNameText(entity.GetName());
			actor.SetNameTextActive(active: true);
			break;
		case CHOICE_NAME_DISPLAY.PLAYER:
		{
			int num = entity.GetTag(GAME_TAG.PLAYER_ID);
			if (num == 0)
			{
				num = entity.GetTag(GAME_TAG.PLAYER_ID_LOOKUP);
			}
			actor.SetNameText(GameState.Get().GetGameEntity().GetBestNameForPlayer(num));
			actor.SetNameTextActive(active: true);
			break;
		}
		default:
			actor.SetNameTextActive(active: false);
			break;
		}
	}

	private bool IsChoiceCardReady(Card card)
	{
		Entity entity = card.GetEntity();
		if (!IsEntityReady(entity))
		{
			return false;
		}
		if (!IsCardReady(card))
		{
			return false;
		}
		if (!IsCardActorReady(card))
		{
			return false;
		}
		return true;
	}

	public void PopulateTransformDatas(ChoiceState state)
	{
		bool isFriendly = state.m_isFriendly;
		state.m_cardTransforms.Clear();
		int count = state.m_cards.Count;
		float num = m_ChoiceData.m_HorizontalPadding;
		if (isFriendly && count > m_CommonData.m_MaxCardsBeforeAdjusting)
		{
			num = GetPaddingForCardCount(count);
		}
		if (state.m_isMagicItemDiscover)
		{
			num *= (float)m_ChoiceData.m_HorizontalPaddingTrinketMultiplier;
		}
		float num2 = ((!isFriendly) ? m_CommonData.m_OpponentCardWidth : (state.m_isMagicItemDiscover ? m_CommonData.m_FriendlyCardWidthTrinket : m_CommonData.m_FriendlyCardWidth));
		float num3 = 1f;
		if (isFriendly && count > m_CommonData.m_MaxCardsBeforeAdjusting)
		{
			num3 = GetScaleForCardCount(count);
			if (state.m_isMagicItemDiscover)
			{
				num3 *= (float)m_CommonData.m_TrinketCardAdditionalScale;
			}
			num2 *= num3;
		}
		float num4 = 0.5f * num2;
		float num5 = num2 * (float)count + num * (float)(count - 1);
		float num6 = 0.5f * num5;
		string text = ((!isFriendly) ? m_ChoiceData.m_OpponentBoneName : (state.m_isMagicItemDiscover ? m_ChoiceData.m_BGTrinketFriendlyBoneName : m_ChoiceData.m_FriendlyBoneName));
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			text += "_phone";
		}
		Transform obj = Board.Get().FindBone(text);
		Vector3 position = obj.position;
		Vector3 eulerAngles = obj.rotation.eulerAngles;
		Vector3 localScale = obj.localScale;
		float num7 = position.x - num6 + num4;
		Entity entity = GameState.Get().GetEntity(state.m_sourceEntityId);
		string text2 = "";
		if (entity != null)
		{
			text2 = entity.GetCardId();
		}
		for (int i = 0; i < count; i++)
		{
			TransformData item = default(TransformData);
			Vector3 position2 = new Vector3
			{
				x = num7
			};
			if ((bool)UniversalInputManager.UsePhoneUI && text2 == "TTN_717t")
			{
				position2.x = num7 + 2f;
			}
			position2.y = position.y;
			position2.z = position.z;
			item.Position = position2;
			Vector3 localScale2 = localScale;
			localScale2.x *= num3;
			localScale2.y *= num3;
			localScale2.z *= num3;
			item.LocalScale = localScale2;
			item.RotationAngles = eulerAngles;
			state.m_cardTransforms.Add(item);
			num7 += num2 + num;
		}
	}

	private float GetScaleForCardCount(int cardCount)
	{
		if (cardCount <= m_CommonData.m_MaxCardsBeforeAdjusting)
		{
			return 1f;
		}
		return cardCount switch
		{
			4 => m_CommonData.m_FourCardScale, 
			5 => m_CommonData.m_FiveCardScale, 
			_ => m_CommonData.m_SixPlusCardScale, 
		};
	}

	private float GetPaddingForCardCount(int cardCount)
	{
		if (cardCount <= m_CommonData.m_MaxCardsBeforeAdjusting)
		{
			return m_ChoiceData.m_HorizontalPadding;
		}
		return cardCount switch
		{
			4 => m_ChoiceData.m_HorizontalPaddingFourCards, 
			5 => m_ChoiceData.m_HorizontalPaddingFiveCards, 
			_ => m_ChoiceData.m_HorizontalPaddingSixPlusCards, 
		};
	}

	public ChoiceState GetLastChoiceState()
	{
		return m_lastShownChoiceState;
	}

	public string GetToggleButtonBoneName()
	{
		return m_ChoiceData.m_ToggleChoiceButtonBoneName;
	}

	private void StartChoices(ChoiceState state)
	{
		m_lastShownChoiceState = state;
		int count = state.m_cards.Count;
		for (int i = 0; i < count; i++)
		{
			Card card = state.m_cards[i];
			TransformData transformData = state.m_cardTransforms[i];
			card.transform.position = transformData.Position;
			card.transform.rotation = Quaternion.Euler(transformData.RotationAngles);
			card.transform.localScale = transformData.LocalScale;
		}
		RevealChoiceCards(state);
	}

	private void RevealChoiceCards(ChoiceState state)
	{
		ISpell customChoiceRevealSpell = GetCustomChoiceRevealSpell(state);
		if (customChoiceRevealSpell != null)
		{
			m_customChoiceRevealSpell = customChoiceRevealSpell;
			RevealChoiceCardsUsingCustomSpell(customChoiceRevealSpell, state);
		}
		else
		{
			DefaultRevealChoiceCards(state);
		}
		CorpseCounter.UpdateTextAll();
	}

	private void DefaultRevealChoiceCards(ChoiceState choiceState)
	{
		bool isFriendly = choiceState.m_isFriendly;
		if (isFriendly)
		{
			ShowChoiceUi(choiceState);
		}
		ShowChoiceCards(choiceState, isFriendly);
		choiceState.m_hasBeenRevealed = true;
	}

	private void ShowChoiceCards(ChoiceState state, bool friendly)
	{
		StartCoroutine(PlayCardAnimation(state, friendly));
	}

	private void GetDeckTransform(ZoneDeck deckZone, out Vector3 startPos, out Vector3 startRot, out Vector3 startScale)
	{
		Actor thicknessForLayout = deckZone.GetThicknessForLayout();
		startPos = thicknessForLayout.GetMeshRenderer().bounds.center + Card.IN_DECK_OFFSET;
		startRot = Card.IN_DECK_ANGLES;
		startScale = Card.IN_DECK_SCALE;
	}

	private IEnumerator StartShopChoice(ChoiceState state)
	{
		m_lastShownChoiceState = state;
		m_shopChoice.SetShopChoiceCards(state.m_cards);
		if (!m_shopChoice.IsShopChoiceActive())
		{
			m_shopChoice.LoadShopChoice(m_ShopChoiceUIData.m_ShopVignetteSpellPath, m_ShopChoiceUIData.m_BartenderDBID);
			m_shopChoice.StartShopChoices();
			PlayShopGreetingLine();
		}
		else
		{
			m_shopChoice.StartShopChoicesFromActiveShop();
		}
		ShowChoiceUi(state);
		state.m_hasBeenRevealed = true;
		yield break;
	}

	public void OnShopChoiceEnded()
	{
		StartCoroutine(EndShopChoice());
	}

	private IEnumerator EndShopChoice()
	{
		ChoiceState state = GetFriendlyChoiceState();
		if (state != null)
		{
			while (state.m_waitingToStart)
			{
				yield return null;
			}
		}
		if (m_shopChoice.GetGuideConfig() != null)
		{
			string randomPostShopGeneralLine = m_shopChoice.GetGuideConfig().GetRandomPostShopGeneralLine();
			StartCoroutine(PlayShopChoiceVO(randomPostShopGeneralLine, Notification.SpeechBubbleDirection.None));
		}
		m_shopChoice.EndShopChoice();
	}

	private void PlayShopGreetingLine()
	{
		if (m_shopChoice.GetGuideConfig() == null)
		{
			return;
		}
		string voHeroSpecificLine = null;
		Entity friendlySidePlayer = GameState.Get().GetFriendlySidePlayer();
		Entity hero = friendlySidePlayer.GetHero();
		string cardId = hero.GetCardId();
		string battlegroundsBaseHeroCardId = CollectionManager.Get().GetBattlegroundsBaseHeroCardId(cardId);
		GameEntity gameEntity = GameState.Get()?.GetGameEntity();
		int num = 1;
		if (gameEntity != null)
		{
			num = gameEntity.GetTag(GAME_TAG.BACON_TIMES_VISITED_ALT_TAVERN);
		}
		if (num <= 1 && m_shopChoice.GetGuideConfig().CheckHeroSpecificLine(battlegroundsBaseHeroCardId, out voHeroSpecificLine))
		{
			StartCoroutine(PlayShopChoiceVO(voHeroSpecificLine));
			return;
		}
		if (!(UnityEngine.Random.Range(0f, 1f) > 0.25f))
		{
			voHeroSpecificLine = ((friendlySidePlayer.GetTag(GAME_TAG.BACON_WON_LAST_COMBAT) != 0) ? m_shopChoice.GetGuideConfig().GetRandomPostCombatWinLine() : m_shopChoice.GetGuideConfig().GetRandomPostCombatLoseLine());
		}
		else
		{
			int num2 = hero.GetTag(GAME_TAG.PLAYER_LEADERBOARD_PLACE);
			voHeroSpecificLine = ((num2 == 1) ? m_shopChoice.GetGuideConfig().GetRandomPostShopIsFirstLine() : ((num2 > 4) ? m_shopChoice.GetGuideConfig().GetRandomPostShopLoseLine() : m_shopChoice.GetGuideConfig().GetRandomPostShopWinLine()));
		}
		StartCoroutine(PlayShopChoiceVO(voHeroSpecificLine));
	}

	private void PlayBuyingChoiceLine(Entity boughtEntity)
	{
		if (!(m_shopChoice.GetGuideConfig() == null))
		{
			Entity friendlySidePlayer = GameState.Get().GetFriendlySidePlayer();
			if (friendlySidePlayer.GetTag(GAME_TAG.BACON_ALT_TAVERN_COIN) - friendlySidePlayer.GetTag(GAME_TAG.BACON_ALT_TAVERN_COIN_USED) - boughtEntity.GetTag(GAME_TAG.BACON_OVERRIDE_BG_COST) > 0)
			{
				string randomRecruitLargeLine = m_shopChoice.GetGuideConfig().GetRandomRecruitLargeLine();
				StartCoroutine(PlayShopChoiceVO(randomRecruitLargeLine));
			}
		}
	}

	public void PlayShopChoiceIdleLine()
	{
		if (!(m_shopChoice.GetGuideConfig() == null))
		{
			string randomIdleLine = m_shopChoice.GetGuideConfig().GetRandomIdleLine();
			StartCoroutine(PlayShopChoiceVO(randomIdleLine));
		}
	}

	private IEnumerator PlayShopChoiceVO(string voLine, Notification.SpeechBubbleDirection direction = Notification.SpeechBubbleDirection.BottomLeft)
	{
		if (GameState.Get()?.GetGameEntity() is TB_BaconShop tB_BaconShop)
		{
			yield return tB_BaconShop.PlayShopChoiceVOLine(voLine, m_shopChoice.GetBartenderActor(), direction);
		}
	}

	private IEnumerator PlayCardAnimation(ChoiceState state, bool friendly)
	{
		if (state.m_showFromDeck)
		{
			state.m_showFromDeck = false;
			ZoneDeck deckZone = GameState.Get().GetEntity(state.m_sourceEntityId).GetController()
				.GetDeckZone();
			GetDeckTransform(deckZone, out var deckPos, out var deckRot, out var deckScale);
			float timingBonus = 0.1f;
			int cardCount = state.m_cards.Count;
			int i = 0;
			while (i < cardCount)
			{
				Card card = state.m_cards[i];
				card.ShowCard();
				if (GameUtils.GetSignatureDisplayPreference() == SignatureDisplayPreference.RARELY && !UniversalInputManager.Get().IsTouchMode())
				{
					card.GetActor()?.TurnSignatureTextOff(animate: false);
				}
				GameObject cardObject = card.gameObject;
				cardObject.transform.position = deckPos;
				cardObject.transform.rotation = Quaternion.Euler(deckRot);
				cardObject.transform.localScale = deckScale;
				TransformData transformData = state.m_cardTransforms[i];
				iTween.Stop(cardObject);
				Vector3[] array = new Vector3[3]
				{
					cardObject.transform.position,
					new Vector3(cardObject.transform.position.x, cardObject.transform.position.y + 3.6f, cardObject.transform.position.z),
					transformData.Position
				};
				iTween.MoveTo(cardObject, iTween.Hash("path", array, "time", MulliganManager.ANIMATION_TIME_DEAL_CARD, "easetype", iTween.EaseType.easeInSineOutExpo));
				iTween.ScaleTo(cardObject, MulliganManager.FRIENDLY_PLAYER_CARD_SCALE, MulliganManager.ANIMATION_TIME_DEAL_CARD);
				iTween.RotateTo(cardObject, iTween.Hash("rotation", new Vector3(0f, 0f, 0f), "time", MulliganManager.ANIMATION_TIME_DEAL_CARD, "delay", MulliganManager.ANIMATION_TIME_DEAL_CARD / 16f));
				yield return new WaitForSeconds(0.04f);
				SoundManager.Get().LoadAndPlay("FX_GameStart09_CardsOntoTable.prefab:da502e035813b5742a04d2ef4f588255", cardObject);
				yield return new WaitForSeconds(0.05f + timingBonus);
				timingBonus = 0f;
				int num = i + 1;
				i = num;
			}
		}
		else
		{
			int count = state.m_cards.Count;
			SignatureDisplayPreference signatureDisplayPreference = GameUtils.GetSignatureDisplayPreference();
			for (int j = 0; j < count; j++)
			{
				Card card2 = state.m_cards[j];
				TransformData transformData2 = state.m_cardTransforms[j];
				card2.ShowCard();
				if (signatureDisplayPreference == SignatureDisplayPreference.RARELY && !UniversalInputManager.Get().IsTouchMode())
				{
					card2.GetActor()?.TurnSignatureTextOff(animate: false);
				}
				card2.transform.localScale = INVISIBLE_SCALE;
				iTween.Stop(card2.gameObject);
				iTween.RotateTo(card2.gameObject, transformData2.RotationAngles, m_ChoiceData.m_CardShowTime);
				iTween.ScaleTo(card2.gameObject, transformData2.LocalScale, m_ChoiceData.m_CardShowTime);
				iTween.MoveTo(card2.gameObject, transformData2.Position, m_ChoiceData.m_CardShowTime);
				ActivateChoiceCardStateSpells(card2);
			}
		}
		PlayChoiceEffects(state, friendly);
	}

	private void PlayChoiceEffects(ChoiceState state, bool friendly)
	{
		if (!friendly)
		{
			return;
		}
		Entity entity = GameState.Get().GetEntity(state.m_sourceEntityId);
		if (entity == null)
		{
			return;
		}
		ChoiceEffectData choiceEffectDataForCard = GetChoiceEffectDataForCard(entity.GetCard());
		if (choiceEffectDataForCard == null || choiceEffectDataForCard.m_Spell == null || (state.m_hasBeenRevealed && !choiceEffectDataForCard.m_AlwaysPlayEffect))
		{
			return;
		}
		ISpellCallbackHandler<Spell>.StateFinishedCallback callback = delegate(Spell spell3, SpellStateType prevStateType, object userData)
		{
			if (spell3.GetActiveState() == SpellStateType.NONE)
			{
				SpellManager.Get().ReleaseSpell(spell3);
			}
		};
		if (choiceEffectDataForCard.m_PlayOncePerCard)
		{
			foreach (Card card in state.m_cards)
			{
				Spell spell = SpellManager.Get().GetSpell(choiceEffectDataForCard.m_Spell);
				TransformUtil.AttachAndPreserveLocalTransform(spell.transform, card.GetActor().transform);
				spell.AddStateFinishedCallback(callback);
				spell.Activate();
				state.m_choiceEffectSpells.Add(spell);
			}
			return;
		}
		Spell spell2 = SpellManager.Get().GetSpell(choiceEffectDataForCard.m_Spell);
		spell2.AddStateFinishedCallback(callback);
		spell2.Activate();
		state.m_choiceEffectSpells.Add(spell2);
	}

	private void ActivateChoiceCardStateSpells(Card card)
	{
		Actor actor = card.GetActor();
		if (!(actor != null))
		{
			return;
		}
		Entity entity = card.GetEntity();
		if (entity.HasTag(GAME_TAG.BACON_SHOW_COST_ON_DISCOVER))
		{
			actor.SetShowCostOverride();
			actor.UpdateTextComponents(entity);
		}
		bool flag = actor.UseTechLevelManaGem();
		bool flag2 = entity.HasTag(GAME_TAG.BACON_SHOW_COST_ON_DISCOVER) || !flag;
		if (flag)
		{
			Spell techLevelSpell = actor.GetTechLevelSpell();
			if (techLevelSpell != null && entity != null)
			{
				techLevelSpell.GetComponent<PlayMakerFSM>().FsmVariables.GetFsmInt("TechLevel").Value = entity.GetTechLevel();
				techLevelSpell.ActivateState(SpellStateType.BIRTH);
			}
		}
		if (flag2)
		{
			actor.ReleaseTechLevelSpells();
			if (actor.UseCoinManaGemForChoiceCard())
			{
				actor.ActivateSpellBirthState(SpellType.COIN_MANA_GEM);
			}
			else
			{
				actor.ReleaseSpell(SpellType.COIN_MANA_GEM);
			}
		}
		bool flag3 = true;
		ChoiceState choiceStateForPlayer = GetChoiceStateForPlayer(GameState.Get().GetFriendlyPlayerId());
		if (choiceStateForPlayer != null)
		{
			Entity entity2 = GameState.Get().GetEntity(choiceStateForPlayer.m_sourceEntityId);
			if (entity2 != null)
			{
				flag3 = !entity2.HasTag(GAME_TAG.BACON_DONT_SHOW_PAIR_TRIPLE_DISCOVER_VFX);
			}
		}
		if (flag3)
		{
			if (entity.HasTag(GAME_TAG.BACON_TRIPLE_CANDIDATE))
			{
				actor.ActivateSpellBirthState(SpellType.BACON_TRIPLE_CANDIDATE);
			}
			if (entity.HasTag(GAME_TAG.BACON_PAIR_CANDIDATE))
			{
				actor.ActivateSpellBirthState(SpellType.BACON_PAIR_CANDIDATE);
			}
			if (entity.HasTag(GAME_TAG.BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE))
			{
				actor.ActivateSpellBirthState(SpellType.BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE);
			}
			if (entity.HasTag(GAME_TAG.BACON_DUO_PAIR_CANDIDATE_TEAMMATE))
			{
				actor.ActivateSpellBirthState(SpellType.BACON_DUO_PAIR_CANDIDATE_TEAMMATE);
			}
		}
	}

	private void DeactivateChoiceCardStateSpells(Card card)
	{
		Actor actor = card.GetActor();
		if (actor != null)
		{
			if (actor.UseCoinManaGemForChoiceCard())
			{
				actor.ReleaseSpell(SpellType.COIN_MANA_GEM);
			}
			if (actor.UseTechLevelManaGem())
			{
				actor.ReleaseTechLevelSpells();
			}
			actor.ReleaseSpell(SpellType.BACON_TRIPLE_CANDIDATE);
			actor.ReleaseSpell(SpellType.BACON_PAIR_CANDIDATE);
			actor.ReleaseSpell(SpellType.BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE);
			actor.ReleaseSpell(SpellType.BACON_DUO_PAIR_CANDIDATE_TEAMMATE);
			actor.ReleaseSpell(SpellType.TEAMMATE_PING);
			actor.ReleaseSpell(SpellType.TEAMMATE_PING_WHEEL);
		}
	}

	private void DeactivateChoiceEffects(ChoiceState state)
	{
		if (state == null)
		{
			return;
		}
		foreach (Spell choiceEffectSpell in state.m_choiceEffectSpells)
		{
			if (!(choiceEffectSpell == null) && choiceEffectSpell.HasUsableState(SpellStateType.DEATH))
			{
				choiceEffectSpell.ActivateState(SpellStateType.DEATH);
			}
		}
		state.m_choiceEffectSpells.Clear();
	}

	private TagPostChoiceEffect GetTagPostChoiceEffect(ChoiceState choiceState)
	{
		Entity entity = GameState.Get().GetEntity(choiceState.m_sourceEntityId);
		if (entity == null)
		{
			Log.Gameplay.PrintWarning("ChoiceCardMgr - GetTagPostChoiceEffect - sourceEntity is null");
			return null;
		}
		foreach (TagPostChoiceEffect tagPostChoiceEffectDatum in m_TagPostChoiceEffectData)
		{
			if (entity.HasTag(tagPostChoiceEffectDatum.m_Tag))
			{
				return tagPostChoiceEffectDatum;
			}
		}
		return null;
	}

	private void ApplyPostChoiceEffects(TagPostChoiceEffect postChoiceEffect, ChoiceState choiceState, Network.EntitiesChosen chosen)
	{
		ISpellCallbackHandler<Spell>.StateFinishedCallback callback = delegate(Spell spell3, SpellStateType prevStateType, object userData)
		{
			if (spell3.GetActiveState() == SpellStateType.NONE)
			{
				SpellManager.Get().ReleaseSpell(spell3);
			}
		};
		if (postChoiceEffect != null)
		{
			List<Card> cards = choiceState.m_cards;
			for (int num = 0; num < cards.Count; num++)
			{
				Card card = cards[num];
				Spell spell = (WasCardChosen(card, chosen.Entities) ? postChoiceEffect.m_SpellSelectedCards : postChoiceEffect.m_SpellUnselectedCards);
				Spell spell2 = SpellManager.Get().GetSpell(spell);
				TransformUtil.AttachAndPreserveLocalTransform(spell2.transform, card.GetActor().transform);
				spell2.AddStateFinishedCallback(callback);
				spell2.ActivateState(SpellStateType.DEATH);
				choiceState.m_postChoiceSpells.Add(spell2);
			}
		}
	}

	private bool HavePostChoiceEffectsFinished(ChoiceState choiceState)
	{
		foreach (Spell postChoiceSpell in choiceState.m_postChoiceSpells)
		{
			if (postChoiceSpell != null && !postChoiceSpell.IsFinished())
			{
				return false;
			}
		}
		return true;
	}

	private ChoiceEffectData GetChoiceEffectDataForCard(Card sourceCard)
	{
		if (sourceCard == null)
		{
			return null;
		}
		foreach (CardSpecificChoiceEffect cardSpecificChoiceEffectDatum in m_CardSpecificChoiceEffectData)
		{
			if (cardSpecificChoiceEffectDatum.m_CardID == sourceCard.GetEntity().GetCardId())
			{
				return cardSpecificChoiceEffectDatum.m_ChoiceEffectData;
			}
		}
		foreach (TagSpecificChoiceEffect tagSpecificChoiceEffectDatum in m_TagSpecificChoiceEffectData)
		{
			if (!sourceCard.GetEntity().HasTag(tagSpecificChoiceEffectDatum.m_Tag))
			{
				continue;
			}
			foreach (TagValueSpecificChoiceEffect item in tagSpecificChoiceEffectDatum.m_ValueSpellMap)
			{
				if (item.m_Value == sourceCard.GetEntity().GetTag(tagSpecificChoiceEffectDatum.m_Tag))
				{
					return item.m_ChoiceEffectData;
				}
			}
		}
		if (sourceCard.GetEntity().HasTag(GAME_TAG.USE_DISCOVER_VISUALS))
		{
			return m_DiscoverChoiceEffectData;
		}
		if (sourceCard.GetEntity().HasReferencedTag(GAME_TAG.ADAPT))
		{
			return m_AdaptChoiceEffectData;
		}
		if (sourceCard.GetEntity().HasTag(GAME_TAG.GEARS))
		{
			return m_GearsChoiceEffectData;
		}
		if (sourceCard.GetEntity().HasTag(GAME_TAG.GOOD_OL_GENERIC_FRIENDLY_DRAGON_DISCOVER_VISUALS))
		{
			return m_DragonChoiceEffectData;
		}
		if (sourceCard.GetEntity().HasTag(GAME_TAG.BACON_IS_MAGIC_ITEM_DISCOVER))
		{
			return m_TrinketChoiceEffectData;
		}
		return null;
	}

	private IEnumerator WaitThenConcealChoicesFromPacket(Network.EntitiesChosen chosen)
	{
		if (chosen == null)
		{
			yield break;
		}
		bool allowedToPrintPowers = GameState.Get()?.GameScenarioAllowsPowerPrinting() ?? true;
		int playerId = chosen.PlayerId;
		if (m_choiceStateMap.TryGetValue(playerId, out var choiceState))
		{
			if (choiceState.m_waitingToStart || !choiceState.m_hasBeenRevealed)
			{
				if (allowedToPrintPowers)
				{
					Log.Power.Print("ChoiceCardMgr.WaitThenHideChoicesFromPacket() - id={0} BEGIN WAIT for EntityChoice", chosen.ID);
				}
				while (choiceState.m_waitingToStart)
				{
					yield return null;
				}
				while (!choiceState.m_hasBeenRevealed)
				{
					yield return null;
				}
				yield return new WaitForSeconds(m_ChoiceData.m_MinShowTime);
			}
		}
		else if (m_lastShownChoiceState != null && m_lastShownChoiceState.m_choiceID == chosen.ID)
		{
			choiceState = m_lastShownChoiceState;
		}
		if (choiceState == null && allowedToPrintPowers)
		{
			Log.Power.Print("ChoiceCardMgr.WaitThenHideChoicesFromPacket(): Unable to find ChoiceState corresponding to EntitiesChosen packet with ID %d.", chosen.ID);
			Log.Power.Print("ChoiceCardMgr.WaitThenHideChoicesFromPacket() - id={0} END WAIT", chosen.ID);
			GameState.Get().OnEntitiesChosenProcessed(chosen);
			yield break;
		}
		ResolveConflictBetweenLocalChoiceAndServerPacket(choiceState, chosen);
		ActivateCustomChoiceDeathState();
		if (choiceState.m_isFriendly)
		{
			TagPostChoiceEffect tagPostChoiceEffect = GetTagPostChoiceEffect(choiceState);
			ApplyPostChoiceEffects(tagPostChoiceEffect, choiceState, chosen);
			while (!HavePostChoiceEffectsFinished(choiceState))
			{
				yield return null;
			}
		}
		if (allowedToPrintPowers)
		{
			Log.Power.Print("ChoiceCardMgr.WaitThenHideChoicesFromPacket() - id={0} END WAIT", chosen.ID);
		}
		ConcealChoicesFromPacket(playerId, choiceState, chosen);
	}

	private void ResolveConflictBetweenLocalChoiceAndServerPacket(ChoiceState choiceState, Network.EntitiesChosen chosen)
	{
		if (DoesLocalChoiceMatchPacket(choiceState.m_chosenEntities, chosen.Entities))
		{
			return;
		}
		choiceState.m_chosenEntities = new List<Entity>();
		foreach (int entity2 in chosen.Entities)
		{
			Entity entity = GameState.Get().GetEntity(entity2);
			if (entity != null)
			{
				choiceState.m_chosenEntities.Add(entity);
			}
		}
		if (!choiceState.m_hasBeenConcealed)
		{
			return;
		}
		foreach (Card card in choiceState.m_cards)
		{
			card.ShowCard();
		}
		choiceState.m_hasBeenConcealed = false;
	}

	private void ActivateCustomChoiceDeathState()
	{
		if (m_customChoiceRevealSpell != null)
		{
			m_customChoiceRevealSpell.ActivateState(SpellStateType.DEATH);
			m_customChoiceRevealSpell = null;
		}
	}

	private bool DoesLocalChoiceMatchPacket(List<Entity> localChoices, List<int> packetChoices)
	{
		if (localChoices == null || packetChoices == null)
		{
			GameState gameState = GameState.Get();
			if (gameState == null || gameState.GameScenarioAllowsPowerPrinting())
			{
				Log.Power.Print($"ChoiceCardMgr.DoesLocalChoiceMatchPacket(): Null list passed in! localChoices={localChoices}, packetChoices={packetChoices}.");
			}
			return false;
		}
		if (localChoices.Count != packetChoices.Count)
		{
			return false;
		}
		for (int i = 0; i < packetChoices.Count; i++)
		{
			int id = packetChoices[i];
			Entity entity = GameState.Get().GetEntity(id);
			if (!localChoices.Contains(entity))
			{
				return false;
			}
		}
		return true;
	}

	private void ConcealChoicesFromPacket(int playerId, ChoiceState choiceState, Network.EntitiesChosen chosen)
	{
		if (choiceState.m_isFriendly)
		{
			HideChoiceUI();
			HideRewindHUD();
		}
		ISpell customChoiceConcealSpell = GetCustomChoiceConcealSpell(choiceState);
		if (customChoiceConcealSpell != null)
		{
			ConcealChoiceCardsUsingCustomSpell(customChoiceConcealSpell, choiceState, chosen);
		}
		else
		{
			DefaultConcealChoicesFromPacket(playerId, choiceState, chosen);
		}
	}

	private void DefaultConcealChoicesFromPacket(int playerId, ChoiceState choiceState, Network.EntitiesChosen chosen)
	{
		if (!choiceState.m_hasBeenConcealed)
		{
			List<Card> cards = choiceState.m_cards;
			bool hideChosen = choiceState.m_hideChosen;
			for (int i = 0; i < cards.Count; i++)
			{
				Card card = cards[i];
				if (!choiceState.m_isShopChoice && (hideChosen || !WasCardChosen(card, chosen.Entities)))
				{
					card.DeactivateHandStateSpells(card.GetActor());
					DeactivateChoiceCardStateSpells(card);
					card.HideCard();
				}
			}
			DeactivateChoiceEffects(choiceState);
			choiceState.m_hasBeenConcealed = true;
		}
		OnFinishedConcealChoices(playerId);
		GameState.Get().OnEntitiesChosenProcessed(chosen);
	}

	private bool WasCardChosen(Card card, List<int> chosenEntityIds)
	{
		Entity entity = card.GetEntity();
		int entityId = entity.GetEntityId();
		return chosenEntityIds.FindIndex((int currEntityId) => entityId == currEntityId) >= 0;
	}

	private void ConcealChoicesFromInput(int playerId, ChoiceState choiceState)
	{
		if (choiceState.m_isFriendly)
		{
			HideChoiceUI();
		}
		ISpell customChoiceConcealSpell = GetCustomChoiceConcealSpell(choiceState);
		TagPostChoiceEffect tagPostChoiceEffect = GetTagPostChoiceEffect(choiceState);
		if (customChoiceConcealSpell != null || tagPostChoiceEffect != null)
		{
			return;
		}
		for (int i = 0; i < choiceState.m_cards.Count; i++)
		{
			Card card = choiceState.m_cards[i];
			Entity entity = card.GetEntity();
			card.GetActor()?.SetShowCostOverride(0);
			if (choiceState.m_hideChosen || !choiceState.m_chosenEntities.Contains(entity))
			{
				card.HideCard();
				card.DeactivateHandStateSpells(card.GetActor());
				DeactivateChoiceCardStateSpells(card);
			}
		}
		DeactivateChoiceEffects(choiceState);
		choiceState.m_hasBeenConcealed = true;
		OnFinishedConcealChoices(playerId);
	}

	private void OnFinishedConcealChoices(int playerId)
	{
		if (!m_choiceStateMap.ContainsKey(playerId))
		{
			return;
		}
		foreach (GameObject value in m_choiceStateMap[playerId].m_xObjs.Values)
		{
			UnityEngine.Object.Destroy(value);
		}
		m_choiceStateMap.Remove(playerId);
	}

	private void HideChoiceCards(ChoiceState state)
	{
		for (int i = 0; i < state.m_cards.Count; i++)
		{
			Card card = state.m_cards[i];
			HideChoiceCard(card);
		}
		DeactivateChoiceEffects(state);
		CorpseCounter.UpdateTextAll();
	}

	private void HideChoiceCard(Card card)
	{
		Action<object> value = delegate(object userData)
		{
			((Card)userData).HideCard();
		};
		iTween.Stop(card.gameObject);
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("scale", INVISIBLE_SCALE);
		tweenHashTable.Add("time", m_ChoiceData.m_CardHideTime);
		tweenHashTable.Add("oncomplete", value);
		tweenHashTable.Add("oncompleteparams", card);
		tweenHashTable.Add("oncompletetarget", base.gameObject);
		iTween.ScaleTo(card.gameObject, tweenHashTable);
	}

	private void ShowChoiceUi(ChoiceState choiceState)
	{
		ShowChoiceBanner(choiceState);
		ShowChoiceButtons(choiceState);
		ShowMagicItemShopChoiceBackground(choiceState);
		HideEnlargedHand();
		this.ChoiceUIStateChanged?.Invoke(obj: true);
	}

	private void HideChoiceUI()
	{
		HideChoiceBanner();
		HideChoiceButtons();
		HideMagicItemShopChoiceBackground(playSound: true);
		ActivateCustomChoiceDeathState();
		RestoreEnlargedHand();
		this.ChoiceUIStateChanged?.Invoke(obj: false);
	}

	private void ShowChoiceBanner(ChoiceState choiceState)
	{
		HideChoiceBanner();
		if ((!choiceState.m_isSubOptionChoice || choiceState.m_isTitanAbility) && !choiceState.m_isMagicItemDiscover && !choiceState.m_isShopChoice && m_ChoiceData != null && !(m_ChoiceData.m_BannerPrefab == null))
		{
			Transform transform = Board.Get().FindBone(m_ChoiceData.m_BannerBoneName);
			m_choiceBanner = UnityEngine.Object.Instantiate(m_ChoiceData.m_BannerPrefab, transform.position, transform.rotation);
			m_choiceBanner.SetupBanner(choiceState.m_sourceEntityId, choiceState.m_cards, choiceState.m_isSubOptionChoice);
			if (!(GameState.Get().GetEntity(choiceState.m_sourceEntityId).GetCardId() != "TTN_717t"))
			{
				Vector3 localScale = m_choiceBanner.transform.localScale;
				m_choiceBanner.transform.localScale = INVISIBLE_SCALE;
				Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
				tweenHashTable.Add("scale", localScale);
				tweenHashTable.Add("time", m_ChoiceData.m_UiShowTime);
				iTween.ScaleTo(m_choiceBanner.gameObject, tweenHashTable);
			}
		}
	}

	private void ShowStarshipLaunchpadChoiceBanner(ChoiceState choiceState)
	{
		HideChoiceBanner();
		Transform transform = Board.Get().FindBone(m_ChoiceData.m_BannerBoneName);
		m_choiceBanner = UnityEngine.Object.Instantiate(m_ChoiceData.m_BannerPrefab, transform.position, transform.rotation);
		m_choiceBanner.SetupBanner(choiceState.m_sourceEntityId, choiceState.m_cards, choiceState.m_isSubOptionChoice);
		Vector3 localScale = m_choiceBanner.transform.localScale;
		m_choiceBanner.transform.localScale = INVISIBLE_SCALE;
		Hashtable tweenHashTable = iTweenManager.Get().GetTweenHashTable();
		tweenHashTable.Add("scale", localScale);
		tweenHashTable.Add("time", m_ChoiceData.m_UiShowTime);
		iTween.ScaleTo(m_choiceBanner.gameObject, tweenHashTable);
	}

	private void ShowMagicItemShopChoiceBackground(ChoiceState choiceState)
	{
		HideMagicItemShopChoiceBackground();
		if (choiceState.m_isMagicItemDiscover)
		{
			m_magicItemShopBackground = UnityEngine.Object.Instantiate(m_ChoiceData.m_MagicItemShopBackgroundPrefab);
			ToggleMagicItemShopBackgroundVisibility(visible: true);
		}
	}

	private void HideMagicItemShopChoiceBackground(bool playSound = false)
	{
		if ((bool)m_magicItemShopBackground)
		{
			if (playSound)
			{
				PlayTrinketShopEvent("hide");
			}
			UnityEngine.Object.Destroy(m_magicItemShopBackground);
		}
	}

	private void HideChoiceBanner()
	{
		if ((bool)m_choiceBanner)
		{
			UnityEngine.Object.Destroy(m_choiceBanner.gameObject);
		}
	}

	private void ShowChoiceButtons(ChoiceState choiceState)
	{
		HideChoiceButtons();
		if (choiceState.m_isShopChoice)
		{
			return;
		}
		string text = (choiceState.m_isMagicItemDiscover ? m_ChoiceData.m_BGTrinketToggleChoiceButtonBoneName : m_ChoiceData.m_ToggleChoiceButtonBoneName);
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			text += "_phone";
		}
		if (choiceState.m_isSubOptionChoice)
		{
			m_toggleChoiceButton = CreateChoiceButton(text, ChoiceButton_OnPress, CancelChoiceButton_OnRelease, GameStrings.Get("GLOBAL_CANCEL"));
		}
		else
		{
			m_toggleChoiceButton = CreateChoiceButton(text, ChoiceButton_OnPress, ToggleChoiceButton_OnRelease, GameStrings.Get("GLOBAL_HIDE"));
		}
		Network.EntityChoices friendlyEntityChoices = GameState.Get().GetFriendlyEntityChoices();
		if (friendlyEntityChoices != null && (!friendlyEntityChoices.IsSingleChoice() || choiceState.m_isMagicItemDiscover))
		{
			text = (choiceState.m_isMagicItemDiscover ? m_ChoiceData.m_BGTrinketConfirmChoiceButtonBoneName : m_ChoiceData.m_ConfirmChoiceButtonBoneName);
			if ((bool)UniversalInputManager.UsePhoneUI)
			{
				text += "_phone";
			}
			m_confirmChoiceButton = CreateChoiceButton(text, ChoiceButton_OnPress, ConfirmChoiceButton_OnRelease, GameStrings.Get("GLOBAL_CONFIRM"));
			UpdateConfirmButtonVFX(choiceState);
		}
	}

	public void ShowStarshipPiecesForOpposingPlayer(Entity OpposingPlayerStarship)
	{
		if (OpposingPlayerStarship == null || !OpposingPlayerStarship.HasSubCards())
		{
			return;
		}
		GameState gameState = GameState.Get();
		if (gameState == null)
		{
			return;
		}
		List<int> subCardIDs = OpposingPlayerStarship.GetSubCardIDs();
		ChoiceState choiceState = new ChoiceState();
		List<Card> list = new List<Card>();
		for (int i = 0; i < subCardIDs.Count; i++)
		{
			Entity entity = gameState.GetEntity(subCardIDs[i]);
			if (entity != null)
			{
				list.Add(entity.GetCard());
			}
		}
		choiceState.m_cards = list;
		choiceState.m_isLaunchpadAbility = OpposingPlayerStarship.IsLaunchpad();
		choiceState.m_isFriendly = false;
		choiceState.m_sourceEntityId = OpposingPlayerStarship.GetEntityId();
		choiceState.m_isSubOptionChoice = true;
		ShowStarshipHUD(choiceState);
	}

	public void HideStarshipPiecesForOpposingPlayer()
	{
		HideChoiceUI();
	}

	private void ShowStarshipHUD(ChoiceState choiceState)
	{
		if (!choiceState.m_isLaunchpadAbility || (m_starshipHUDManager != null && m_starshipHUDManager.IsWaitingOnDestroy()))
		{
			return;
		}
		m_starshipHUDManager = StarshipHUDManager.Get();
		GameObject gameObject = m_starshipHUDManager.transform.gameObject;
		ShowStarshipLaunchpadChoiceBanner(choiceState);
		string text = m_ChoiceData.m_ToggleChoiceButtonBoneName;
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			text += "_phone";
		}
		Transform source = Board.Get().FindBone(text);
		TransformUtil.CopyWorld(gameObject, source);
		BigCard.Get().Hide();
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			gameObject.transform.localRotation = Quaternion.Euler(m_StarshipUIData.m_RotationMobile);
			gameObject.transform.localScale = m_StarshipUIData.m_ScaleMobile;
			gameObject.transform.localPosition = m_StarshipUIData.m_PositionMobile;
		}
		else
		{
			gameObject.transform.localRotation = Quaternion.Euler(m_StarshipUIData.m_RotationPC);
			gameObject.transform.localScale = m_StarshipUIData.m_ScalePC;
			gameObject.transform.localPosition = m_StarshipUIData.m_PositionPC;
		}
		GameState.Get();
		Entity launchChoiceEntity = null;
		m_starshipPiecesToView.Clear();
		for (int i = 0; i < choiceState.m_cards.Count; i++)
		{
			Card card = choiceState.m_cards[i];
			Entity entity = card.GetEntity();
			if (card.GetEntity().GetCardId() == "GDB_905")
			{
				launchChoiceEntity = entity;
			}
			else if (entity.HasTag(GAME_TAG.STARSHIP_PIECE))
			{
				m_starshipPiecesToView.Add(entity.GetEntityId());
			}
		}
		if (choiceState.m_isFriendly)
		{
			Player friendlySidePlayer = GameState.Get().GetFriendlySidePlayer();
			m_starshipHUDManager.SetupLaunchAndAbortButtons(delegate
			{
				Player friendlySidePlayer2 = GameState.Get().GetFriendlySidePlayer();
				int num = GameUtils.StarshipLaunchCost(friendlySidePlayer2);
				GameState gameState = GameState.Get();
				InputManager inputManager = InputManager.Get();
				if (friendlySidePlayer2.GetNumAvailableResources() < num)
				{
					PlayErrors.DisplayPlayError(gameState.GetErrorType(launchChoiceEntity), gameState.GetErrorParam(launchChoiceEntity), launchChoiceEntity);
					inputManager.HidePlayerStarshipUI();
				}
				else if (inputManager != null)
				{
					inputManager.HandleClickOnSubOption(launchChoiceEntity);
					inputManager.HidePlayerStarshipUI();
				}
			}, friendlySidePlayer);
		}
		else
		{
			m_starshipHUDManager.SetupButtonsOpponentStarship();
		}
		m_starshipHUDManager.SetupSubcards(m_starshipPiecesToView);
	}

	private void UpdateConfirmButtonVFX(ChoiceState state)
	{
		if (m_confirmChoiceButton != null && state.m_isMagicItemDiscover)
		{
			EnableConfirmButton(GameState.Get().GetChosenEntities().Count > 0);
		}
	}

	public NormalButton CreateChoiceButton(string boneName, UIEvent.Handler OnPressHandler, UIEvent.Handler OnReleaseHandler, string buttonText)
	{
		NormalButton component = AssetLoader.Get().InstantiatePrefab(m_ChoiceData.m_ButtonPrefab, AssetLoadingOptions.IgnorePrefabPosition).GetComponent<NormalButton>();
		component.GetButtonUberText().TextAlpha = 1f;
		Transform source = Board.Get().FindBone(boneName);
		TransformUtil.CopyWorld(component, source);
		m_friendlyChoicesShown = true;
		component.AddEventListener(UIEventType.PRESS, OnPressHandler);
		component.AddEventListener(UIEventType.RELEASE, OnReleaseHandler);
		component.SetText(buttonText);
		component.m_button.GetComponent<Spell>().ActivateState(SpellStateType.BIRTH);
		return component;
	}

	private void HideChoiceButtons()
	{
		if (m_toggleChoiceButton != null)
		{
			UnityEngine.Object.Destroy(m_toggleChoiceButton.gameObject);
			m_toggleChoiceButton = null;
		}
		if (m_confirmChoiceButton != null)
		{
			UnityEngine.Object.Destroy(m_confirmChoiceButton.gameObject);
			m_confirmChoiceButton = null;
		}
		if (m_starshipHUDManager != null)
		{
			m_starshipHUDManager.AnimateAndDestroyHUD();
			m_starshipHUDManager = null;
		}
	}

	private void HideEnlargedHand()
	{
		ZoneHand handZone = GameState.Get().GetFriendlySidePlayer().GetHandZone();
		if (handZone.HandEnlarged())
		{
			m_restoreEnlargedHand = true;
			handZone.SetHandEnlarged(enlarged: false);
		}
	}

	private void RestoreEnlargedHand()
	{
		if (!m_restoreEnlargedHand)
		{
			return;
		}
		m_restoreEnlargedHand = false;
		if (!GameState.Get().IsInTargetMode())
		{
			ZoneHand handZone = GameState.Get().GetFriendlySidePlayer().GetHandZone();
			if (!handZone.HandEnlarged())
			{
				handZone.SetHandEnlarged(enlarged: true);
			}
		}
	}

	private void ChoiceButton_OnPress(UIEvent e)
	{
		SoundManager.Get().LoadAndPlay("UI_MouseClick_01.prefab:fa537702a0db1c3478c989967458788b");
	}

	private void CancelChoiceButton_OnRelease(UIEvent e)
	{
		InputManager.Get().CancelSubOptionMode();
	}

	private void ToggleChoiceButton_OnRelease(UIEvent e)
	{
		int friendlyPlayerId = GameState.Get().GetFriendlyPlayerId();
		ChoiceState state = m_choiceStateMap[friendlyPlayerId];
		if (m_friendlyChoicesShown)
		{
			m_toggleChoiceButton.SetText(GameStrings.Get("GLOBAL_SHOW"));
			HideChoiceCards(state);
			m_friendlyChoicesShown = false;
		}
		else
		{
			m_toggleChoiceButton.SetText(GameStrings.Get("GLOBAL_HIDE"));
			ShowChoiceCards(state, friendly: true);
			m_friendlyChoicesShown = true;
		}
		ToggleMagicItemShopBackgroundVisibility(m_friendlyChoicesShown);
		ToggleConfirmButtonVisibility(m_friendlyChoicesShown);
		ToggleChoiceBannerVisibility(m_friendlyChoicesShown);
	}

	private void ToggleChoiceBannerVisibility(bool visible)
	{
		if ((bool)m_choiceBanner)
		{
			m_choiceBanner.gameObject.SetActive(visible);
		}
	}

	private void ToggleConfirmButtonVisibility(bool visible)
	{
		if ((bool)m_confirmChoiceButton)
		{
			m_confirmChoiceButton.gameObject.SetActive(visible);
			if (visible)
			{
				m_confirmChoiceButton.m_button.GetComponent<Spell>().ActivateState(SpellStateType.BIRTH);
			}
		}
	}

	private void PlayTrinketShopEvent(string playmakerEvent)
	{
		BaconTrinketBacker component = m_magicItemShopBackground.GetComponent<BaconTrinketBacker>();
		if (component != null && component.m_playmaker != null)
		{
			component.m_playmaker.SendEvent(playmakerEvent);
		}
	}

	private void ToggleMagicItemShopBackgroundVisibility(bool visible)
	{
		if (!(m_magicItemShopBackground == null))
		{
			BaconTrinketBacker component = m_magicItemShopBackground.GetComponent<BaconTrinketBacker>();
			if (component != null)
			{
				component.Show(visible);
			}
		}
	}

	private void ConfirmChoiceButton_OnRelease(UIEvent e)
	{
		GameState.Get().SendChoices();
	}

	private void CancelChoices()
	{
		HideChoiceUI();
		foreach (ChoiceState value in m_choiceStateMap.Values)
		{
			for (int i = 0; i < value.m_cards.Count; i++)
			{
				Card card = value.m_cards[i];
				card.HideCard();
				card.DeactivateHandStateSpells(card.GetActor());
				DeactivateChoiceCardStateSpells(card);
			}
		}
		m_choiceStateMap.Clear();
	}

	private IEnumerator WaitThenShowSubOptions()
	{
		while (IsWaitingToShowSubOptions())
		{
			yield return null;
			if (m_subOptionState == null)
			{
				yield break;
			}
		}
		ShowSubOptions();
	}

	private void ShowSubOptions()
	{
		GameState gameState = GameState.Get();
		Card parentCard = m_subOptionState.m_parentCard;
		Entity entity = m_subOptionState.m_parentCard.GetEntity();
		int playerId = entity.GetController().GetPlayerId();
		int teamId = entity.GetController().GetTeamId();
		ChoiceState choiceState = new ChoiceState();
		if (m_choiceStateMap.ContainsKey(playerId))
		{
			m_choiceStateMap[playerId] = choiceState;
		}
		else
		{
			m_choiceStateMap.Add(playerId, choiceState);
		}
		choiceState.m_waitingToStart = false;
		choiceState.m_hasBeenConcealed = false;
		choiceState.m_hasBeenRevealed = false;
		choiceState.m_choiceID = 0;
		choiceState.m_hideChosen = true;
		choiceState.m_sourceEntityId = entity.GetEntityId();
		choiceState.m_preTaskList = null;
		choiceState.m_xObjs = new Map<int, GameObject>();
		choiceState.m_isFriendly = teamId == gameState.GetFriendlySideTeamId();
		choiceState.m_hideChoiceUI = true;
		choiceState.m_isSubOptionChoice = true;
		choiceState.m_isTitanAbility = entity.IsTitan();
		choiceState.m_isMagicItemDiscover = entity.HasTag(GAME_TAG.BACON_IS_MAGIC_ITEM_DISCOVER);
		choiceState.m_isLaunchpadAbility = entity.HasTag(GAME_TAG.LAUNCHPAD);
		choiceState.m_isRewindChoice = false;
		string text = m_SubOptionData.m_BoneName;
		if ((bool)UniversalInputManager.UsePhoneUI)
		{
			text += "_phone";
		}
		Transform transform = Board.Get().FindBone(text);
		float friendlyCardWidth = m_CommonData.m_FriendlyCardWidth;
		float x = transform.position.x;
		ZonePlay battlefieldZone = entity.GetController().GetBattlefieldZone();
		List<int> subCardIDs = entity.GetSubCardIDs();
		if (entity.IsMinion() && !UniversalInputManager.UsePhoneUI && subCardIDs.Count <= 2)
		{
			int zonePosition = parentCard.GetZonePosition();
			x = battlefieldZone.GetCardPosition(parentCard).x;
			if (zonePosition > 5)
			{
				friendlyCardWidth += m_SubOptionData.m_AdjacentCardXOffset;
				x -= m_CommonData.m_FriendlyCardWidth * 1.5f + m_SubOptionData.m_AdjacentCardXOffset + m_SubOptionData.m_MinionParentXOffset;
			}
			else if (zonePosition == 1 && battlefieldZone.GetCards().Count > 6)
			{
				friendlyCardWidth += m_SubOptionData.m_AdjacentCardXOffset;
				x += m_CommonData.m_FriendlyCardWidth / 2f + m_SubOptionData.m_MinionParentXOffset;
			}
			else
			{
				friendlyCardWidth += m_SubOptionData.m_MinionParentXOffset * 2f;
				x -= m_CommonData.m_FriendlyCardWidth / 2f + m_SubOptionData.m_MinionParentXOffset;
			}
		}
		else
		{
			int count = subCardIDs.Count;
			friendlyCardWidth += ((count > m_CommonData.m_MaxCardsBeforeAdjusting) ? m_SubOptionData.m_PhoneMaxAdjacentCardXOffset : m_SubOptionData.m_AdjacentCardXOffset);
			x -= friendlyCardWidth / 2f * (float)(count - 1);
		}
		ISpell spell = GetCustomChoiceRevealSpell(choiceState);
		if (entity.IsTitan())
		{
			spell = null;
		}
		if (choiceState.m_isFriendly && entity.IsLaunchpad())
		{
			for (int i = 0; i < subCardIDs.Count; i++)
			{
				int id = subCardIDs[i];
				Card card = gameState.GetEntity(id).GetCard();
				m_subOptionState.m_cards.Add(card);
				choiceState.m_cards.Add(card);
			}
			ShowStarshipHUD(choiceState);
			HideEnlargedHand();
			return;
		}
		bool forceRevealed = GameMgr.Get().IsBattlegrounds();
		if (spell != null)
		{
			for (int j = 0; j < subCardIDs.Count; j++)
			{
				int id2 = subCardIDs[j];
				Card card2 = gameState.GetEntity(id2).GetCard();
				if (!(card2 == null))
				{
					choiceState.m_cards.Add(card2);
					m_subOptionState.m_cards.Add(card2);
					card2.ForceLoadHandActor(forceRevealed);
					card2.GetActor().Hide();
					card2.transform.position = parentCard.transform.position;
					iTween.MoveTo(position: new Vector3
					{
						x = x + (float)j * friendlyCardWidth,
						y = transform.position.y,
						z = transform.position.z
					}, target: card2.gameObject, time: m_SubOptionData.m_CardShowTime);
					Vector3 localScale = transform.localScale;
					if (subCardIDs.Count > m_CommonData.m_MaxCardsBeforeAdjusting)
					{
						float scaleForCardCount = GetScaleForCardCount(subCardIDs.Count);
						localScale.x *= scaleForCardCount;
						localScale.y *= scaleForCardCount;
						localScale.z *= scaleForCardCount;
					}
					card2.transform.localScale = localScale;
				}
			}
			PopulateTransformDatas(choiceState);
			RevealChoiceCardsUsingCustomSpell(spell, choiceState);
		}
		else
		{
			for (int k = 0; k < subCardIDs.Count; k++)
			{
				int id3 = subCardIDs[k];
				Entity entity2 = gameState.GetEntity(id3);
				Card card3 = entity2.GetCard();
				if (card3 == null)
				{
					continue;
				}
				choiceState.m_cards.Add(card3);
				if (entity2.GetCardType() == TAG_CARDTYPE.LETTUCE_ABILITY)
				{
					Transform[] componentsInChildren = card3.gameObject.GetComponentsInChildren<Transform>();
					for (int l = 0; l < componentsInChildren.Length; l++)
					{
						componentsInChildren[l].position = default(Vector3);
					}
				}
				m_subOptionState.m_cards.Add(card3);
				card3.ForceLoadHandActor(forceRevealed);
				card3.transform.position = parentCard.transform.position;
				card3.transform.localScale = INVISIBLE_SCALE;
				iTween.MoveTo(position: new Vector3
				{
					x = x + (float)k * friendlyCardWidth,
					y = transform.position.y,
					z = transform.position.z
				}, target: card3.gameObject, time: m_SubOptionData.m_CardShowTime);
				Vector3 localScale2 = transform.localScale;
				if (subCardIDs.Count > m_CommonData.m_MaxCardsBeforeAdjusting)
				{
					float scaleForCardCount2 = GetScaleForCardCount(subCardIDs.Count);
					localScale2.x *= scaleForCardCount2;
					localScale2.y *= scaleForCardCount2;
					localScale2.z *= scaleForCardCount2;
				}
				iTween.ScaleTo(card3.gameObject, localScale2, m_SubOptionData.m_CardShowTime);
				card3.ActivateHandStateSpells();
				ActivateChoiceCardStateSpells(card3);
			}
		}
		if (choiceState.m_isFriendly && entity.IsTitan())
		{
			ShowChoiceUi(choiceState);
		}
		HideEnlargedHand();
		if (GameMgr.Get().IsBattlegroundDuoGame())
		{
			Network.Get().SendNotifyTeammateChooseOne(subCardIDs);
		}
	}

	private void HideSubOptions(Entity chosenEntity = null)
	{
		for (int i = 0; i < m_subOptionState.m_cards.Count; i++)
		{
			Card card = m_subOptionState.m_cards[i];
			card.DeactivateHandStateSpells();
			DeactivateChoiceCardStateSpells(card);
			Entity entity = card.GetEntity();
			if (entity != chosenEntity || !entity.IsControlledByFriendlySidePlayer())
			{
				card.HideCard();
			}
		}
		HideChoiceUI();
		RestoreEnlargedHand();
		if (GameMgr.Get().IsBattlegroundDuoGame())
		{
			Network.Get().SendNotifyTeammateChooseOne(new List<int>());
		}
	}

	private bool IsEntityReady(Entity entity)
	{
		if (entity.GetZone() == TAG_ZONE.INVALID)
		{
			return false;
		}
		if (entity.IsBusy())
		{
			return false;
		}
		return true;
	}

	private bool IsCardReady(Card card)
	{
		return card.HasCardDef;
	}

	private bool IsCardActorReady(Card card)
	{
		return card.IsActorReady();
	}

	private ISpell GetCustomChoiceRevealSpell(ChoiceState choiceState)
	{
		Entity entity = GameState.Get().GetEntity(choiceState.m_sourceEntityId);
		if (entity == null)
		{
			return null;
		}
		Card card = entity.GetCard();
		if (card == null)
		{
			return null;
		}
		return card.GetCustomChoiceRevealSpell();
	}

	private ISpell GetCustomChoiceConcealSpell(ChoiceState choiceState)
	{
		Entity entity = GameState.Get().GetEntity(choiceState.m_sourceEntityId);
		if (entity == null)
		{
			return null;
		}
		Card card = entity.GetCard();
		if (card == null)
		{
			return null;
		}
		return card.GetCustomChoiceConcealSpell();
	}

	private void RevealChoiceCardsUsingCustomSpell(ISpell customChoiceRevealSpell, ChoiceState state)
	{
		ShowMagicItemShopChoiceBackground(state);
		CustomChoiceSpell customChoiceSpell = customChoiceRevealSpell as CustomChoiceSpell;
		if (customChoiceSpell != null)
		{
			customChoiceSpell.SetChoiceState(state);
		}
		customChoiceSpell?.AddFinishedCallback(OnCustomChoiceRevealSpellFinished, state);
		customChoiceRevealSpell.Activate();
	}

	private void OnCustomChoiceRevealSpellFinished(ISpell spell, object userData)
	{
		ChoiceState choiceState = userData as ChoiceState;
		if (choiceState == null)
		{
			Log.Power.PrintError("userData passed to ChoiceCardMgr.OnCustomChoiceRevealSpellFinished() is not of type ChoiceState.");
		}
		if (choiceState.m_isFriendly && !choiceState.m_hideChoiceUI)
		{
			ShowChoiceUi(choiceState);
		}
		SignatureDisplayPreference signatureDisplayPreference = GameUtils.GetSignatureDisplayPreference();
		foreach (Card card in choiceState.m_cards)
		{
			card.ShowCard();
			if (signatureDisplayPreference == SignatureDisplayPreference.RARELY && !UniversalInputManager.Get().IsTouchMode())
			{
				Actor actor = card.GetActor();
				if (actor != null)
				{
					actor.TurnSignatureTextOff(animate: true, actor.SIGNATURE_TEXT_FADE_OUT_DELAY_CUSTOM_DISCOVER);
				}
			}
			ActivateChoiceCardStateSpells(card);
		}
		PlayChoiceEffects(choiceState, choiceState.m_isFriendly);
		choiceState.m_hasBeenRevealed = true;
	}

	private void ConcealChoiceCardsUsingCustomSpell(ISpell customChoiceConcealSpell, ChoiceState choiceState, Network.EntitiesChosen chosen)
	{
		if (customChoiceConcealSpell.IsActive())
		{
			Log.Power.PrintError("ChoiceCardMgr.HideChoicesFromPacket(): CustomChoiceConcealSpell is already active!");
		}
		CustomChoiceSpell customChoiceSpell = customChoiceConcealSpell as CustomChoiceSpell;
		if (customChoiceSpell != null)
		{
			customChoiceSpell.SetChoiceState(choiceState);
		}
		DeactivateChoiceEffects(choiceState);
		choiceState.m_hasBeenConcealed = true;
		customChoiceSpell?.AddFinishedCallback(OnCustomChoiceConcealSpellFinished, chosen);
		customChoiceConcealSpell.Activate();
	}

	private void OnCustomChoiceConcealSpellFinished(Spell spell, object userData)
	{
		Network.EntitiesChosen entitiesChosen = userData as Network.EntitiesChosen;
		OnFinishedConcealChoices(entitiesChosen.PlayerId);
		GameState.Get().OnEntitiesChosenProcessed(entitiesChosen);
	}

	private void HideLastChosenEntitesOnGameOver()
	{
		if (m_lastChosenEntityIds == null)
		{
			return;
		}
		for (int i = 0; i < m_lastChosenEntityIds.Count; i++)
		{
			Entity entity = GameState.Get().GetEntity(m_lastChosenEntityIds[i]);
			if (entity != null && entity.GetCard() != null)
			{
				entity.GetCard().HideCard();
			}
		}
	}

	private void UpdateHistoryOnRewindChoice(Entity sourceEntity)
	{
		if (sourceEntity != null)
		{
			HistoryManager historyManager = HistoryManager.Get();
			if (!(historyManager == null) && sourceEntity.HasTag(GAME_TAG.REWIND))
			{
				historyManager.MarkCurrentHistoryEntryAsCompleted();
			}
		}
	}

	private bool DoesChoiceContainRewindOptions(List<Card> choiceCards)
	{
		if (choiceCards.Count == 0)
		{
			return false;
		}
		HashSet<string> hashSet = new HashSet<string> { "TIME_000tb", "TIME_000ta" };
		for (int i = 0; i < choiceCards.Count; i++)
		{
			string cardId = choiceCards[i].GetEntity().GetCardId();
			if (hashSet.Contains(cardId))
			{
				hashSet.Remove(cardId);
			}
		}
		if (hashSet.Count != 0)
		{
			return false;
		}
		return true;
	}

	private void ShowRewindHUD(ChoiceState choiceState)
	{
		if (!choiceState.m_isFriendly || !GameState.Get().IsFriendlySidePlayerTurn())
		{
			return;
		}
		m_lastShownChoiceState = choiceState;
		RewindUIManager.Cleanup();
		m_rewindHUDManager = RewindUIManager.Get();
		Entity rewindChoiceEntity = null;
		Entity keepChoiceEntity = null;
		for (int i = 0; i < choiceState.m_cards.Count; i++)
		{
			Card card = choiceState.m_cards[i];
			Entity entity = card.GetEntity();
			if (card.GetEntity().GetCardId() == "TIME_000tb")
			{
				rewindChoiceEntity = entity;
			}
			else if (entity.GetCardId() == "TIME_000ta")
			{
				keepChoiceEntity = entity;
			}
		}
		m_rewindHUDManager.SetupRewindAndKeepButtons(rewindChoiceEntity, keepChoiceEntity);
		m_rewindHUDManager.ShowHud();
		HideEnlargedHand();
		choiceState.m_hasBeenRevealed = true;
	}

	private void HideRewindHUD()
	{
		if (m_rewindHUDManager != null)
		{
			m_rewindHUDManager.HideHud();
		}
	}
}
