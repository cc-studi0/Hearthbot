using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Assets;
using Blizzard.GameService.SDK.Client.Integration;
using Blizzard.T5.Core;
using Blizzard.T5.Core.Utils;
using Blizzard.T5.Jobs;
using Blizzard.T5.Services;
using Blizzard.Telemetry.WTCG.Client;
using BobNetProto;
using Hearthstone;
using Hearthstone.BreakingNews;
using Hearthstone.Core;
using Hearthstone.Progression;
using Hearthstone.Streaming;
using PegasusLettuce;
using PegasusShared;
using PegasusUtil;
using UnityEngine;

public class NetCache : IService, IHasUpdate
{
	public delegate void DelNewNoticesListener(List<ProfileNotice> newNotices, bool isInitialNoticeList);

	public delegate void DelGoldBalanceListener(NetCacheGoldBalance balance);

	public delegate void DelRenownBalanceListener(NetCacheRenownBalance balance);

	public delegate void DelBattlegroundsTokenBalanceListener(NetCacheBattlegroundsTokenBalance balance);

	public delegate void DelFavoriteCardBackChangedListener(int newFavoriteCardBackID, bool isFavorite);

	public delegate void DelFavoriteBattlegroundsHeroSkinChangedListener(int baseHeroCardId, int battlegroundsHeroSkinId);

	public delegate void DelFavoriteBattlegroundsGuideSkinChangedListener(BattlegroundsGuideSkinId? newFavoriteBattlegroundsGuideSkinID);

	public delegate void DelFavoriteBattlegroundsBoardSkinChangedListener(BattlegroundsBoardSkinId? newFavoriteBattlegroundsBoardSkinID);

	public delegate void DelFavoriteBattlegroundsFinisherChangedListener(BattlegroundsFinisherId? newFavoriteBattlegroundsFinisherID);

	public delegate void DelBattlegroundsEmoteLoadoutChangedListener(Hearthstone.BattlegroundsEmoteLoadout newLoadout);

	public delegate void DelFavoriteCoinChangedListener(int newFavoriteCoinID, bool isFavorite);

	public delegate void DelOwnedBattlegroundsSkinsChanged();

	public delegate void DelFavoritePetChangedListener(int newFavoritePetID, bool isHsFavorite, bool isBgFavorite);

	public delegate void DelFavoritePetVariantChangedListener(int newFavoritePetVariantID, bool isHsFavorite, bool isBgFavorite);

	public delegate void DelOwnedPetsChanged();

	public delegate void DelCardBacksChanged();

	public delegate void DelPetsXpLevelChanged(int petId, int previousLevel, int newLevel, int previousXp, int newXp);

	public class NetCacheGamesPlayed
	{
		public int GamesStarted { get; set; }

		public int GamesWon { get; set; }

		public int GamesLost { get; set; }
	}

	public class NetCacheFeatures
	{
		public class CacheMisc
		{
			public int ClientOptionsUpdateIntervalSeconds { get; set; }

			public bool AllowLiveFPSGathering { get; set; }
		}

		public class CacheGames
		{
			public enum FeatureFlags
			{
				Invalid,
				Tournament,
				Practice,
				Casual,
				Forge,
				Friendly,
				TavernBrawl,
				Battlegrounds,
				BattlegroundsFriendlyChallenge,
				BattlegroundsTutorial,
				Duels,
				PaidDuels,
				Mercenaries,
				MercenariesAI,
				MercenariesCoOp,
				MercenariesFriendly,
				MercenariesMythic,
				BattlegroundsDuos
			}

			public bool Tournament { get; set; }

			public bool Practice { get; set; }

			public bool Casual { get; set; }

			public bool Forge { get; set; }

			public bool Friendly { get; set; }

			public bool TavernBrawl { get; set; }

			public bool Battlegrounds { get; set; }

			public bool BattlegroundsFriendlyChallenge { get; set; }

			public bool BattlegroundsTutorial { get; set; }

			public bool BattlegroundsDuos { get; set; }

			public int ShowUserUI { get; set; }

			public bool Duels { get; set; }

			public bool PaidDuels { get; set; }

			public bool Mercenaries { get; set; }

			public bool MercenariesAI { get; set; }

			public bool MercenariesCoOp { get; set; }

			public bool MercenariesFriendly { get; set; }

			public bool MercenariesMythic { get; set; }

			public bool GetFeatureFlag(FeatureFlags flag)
			{
				return flag switch
				{
					FeatureFlags.Tournament => Tournament, 
					FeatureFlags.Practice => Practice, 
					FeatureFlags.Casual => Casual, 
					FeatureFlags.Forge => Forge, 
					FeatureFlags.Friendly => Friendly, 
					FeatureFlags.TavernBrawl => TavernBrawl, 
					FeatureFlags.Battlegrounds => Battlegrounds, 
					FeatureFlags.BattlegroundsFriendlyChallenge => BattlegroundsFriendlyChallenge, 
					FeatureFlags.BattlegroundsTutorial => BattlegroundsTutorial, 
					FeatureFlags.BattlegroundsDuos => BattlegroundsDuos, 
					FeatureFlags.Duels => Duels, 
					FeatureFlags.PaidDuels => PaidDuels, 
					FeatureFlags.Mercenaries => Mercenaries, 
					FeatureFlags.MercenariesAI => MercenariesAI, 
					FeatureFlags.MercenariesCoOp => MercenariesCoOp, 
					FeatureFlags.MercenariesFriendly => MercenariesFriendly, 
					FeatureFlags.MercenariesMythic => MercenariesMythic, 
					_ => false, 
				};
			}
		}

		public class CacheCollection
		{
			[CompilerGenerated]
			private bool <Crafting>k__BackingField;

			public bool Manager { get; set; }

			public bool Crafting
			{
				[CompilerGenerated]
				set
				{
					<Crafting>k__BackingField = value;
				}
			}

			public bool DeckReordering { get; set; }

			public bool MultipleFavoriteCardBacks { get; set; }

			public bool CosmeticsRenderingEnabled { get; set; }
		}

		public class CacheStore
		{
			[CompilerGenerated]
			private int <NumClassicPacksUntilDeprioritize>k__BackingField;

			public bool Store { get; set; }

			public bool BattlePay { get; set; }

			public bool BuyWithGold { get; set; }

			public bool SimpleCheckout { get; set; }

			public bool SoftAccountPurchasing { get; set; }

			public bool VirtualCurrencyEnabled { get; set; }

			public int NumClassicPacksUntilDeprioritize
			{
				[CompilerGenerated]
				set
				{
					<NumClassicPacksUntilDeprioritize>k__BackingField = value;
				}
			}

			public bool SimpleCheckoutIOS { get; set; }

			public bool SimpleCheckoutAndroidAmazon { get; set; }

			public bool SimpleCheckoutAndroidGoogle { get; set; }

			public bool SimpleCheckoutAndroidGlobal { get; set; }

			public bool SimpleCheckoutWin { get; set; }

			public bool SimpleCheckoutMac { get; set; }

			public int BoosterRotatingSoonWarnDaysWithoutSale { get; set; }

			public int BoosterRotatingSoonWarnDaysWithSale { get; set; }

			public bool BuyCardBacksFromCollectionManager { get; set; }

			public bool BuyHeroSkinsFromCollectionManager { get; set; }

			public bool LargeItemBundleDetailsEnabled { get; set; }

			public bool HideLowPriorityProducts { get; set; }

			public bool TavernBrawlGoldPurchaseConfirm { get; set; }

			public bool EnableVirtualCurrencyMenu { get; set; }
		}

		public class CacheHeroes
		{
			[CompilerGenerated]
			private bool <Hunter>k__BackingField;

			[CompilerGenerated]
			private bool <Mage>k__BackingField;

			[CompilerGenerated]
			private bool <Paladin>k__BackingField;

			[CompilerGenerated]
			private bool <Priest>k__BackingField;

			[CompilerGenerated]
			private bool <Rogue>k__BackingField;

			[CompilerGenerated]
			private bool <Shaman>k__BackingField;

			[CompilerGenerated]
			private bool <Warlock>k__BackingField;

			[CompilerGenerated]
			private bool <Warrior>k__BackingField;

			public bool Hunter
			{
				[CompilerGenerated]
				set
				{
					<Hunter>k__BackingField = value;
				}
			}

			public bool Mage
			{
				[CompilerGenerated]
				set
				{
					<Mage>k__BackingField = value;
				}
			}

			public bool Paladin
			{
				[CompilerGenerated]
				set
				{
					<Paladin>k__BackingField = value;
				}
			}

			public bool Priest
			{
				[CompilerGenerated]
				set
				{
					<Priest>k__BackingField = value;
				}
			}

			public bool Rogue
			{
				[CompilerGenerated]
				set
				{
					<Rogue>k__BackingField = value;
				}
			}

			public bool Shaman
			{
				[CompilerGenerated]
				set
				{
					<Shaman>k__BackingField = value;
				}
			}

			public bool Warlock
			{
				[CompilerGenerated]
				set
				{
					<Warlock>k__BackingField = value;
				}
			}

			public bool Warrior
			{
				[CompilerGenerated]
				set
				{
					<Warrior>k__BackingField = value;
				}
			}
		}

		public class CacheMercenaries
		{
			public int FullyUpgradedStatBoostAttack { get; set; }

			public int FullyUpgradedStatBoostHealth { get; set; }

			public float AttackBoostPerMythicLevel { get; set; }

			public float HealthBoostPerMythicLevel { get; set; }

			public int MythicAbilityRenownScaleAssetId { get; set; }

			public int MythicEquipmentRenownScaleAssetId { get; set; }

			public int MythicTreasureRenownScaleAssetId { get; set; }
		}

		public class CacheTraceroute
		{
			public int MaxHops { get; set; }

			public int MessageSize { get; set; }

			public int MaxRetries { get; set; }

			public int TimeoutMs { get; set; }

			public bool ResolveHost { get; set; }
		}

		public class CacheReturningPlayer
		{
			public bool LoginCountNoticeSupressionEnabled { get; set; }

			public int NoticeSuppressionLoginThreshold { get; set; }
		}

		public class Defaults
		{
			public static readonly float TutorialPreviewVideosTimeout = 7f;
		}

		public bool CaisEnabledNonMobile;

		public bool CaisEnabledMobileChina;

		public bool CaisEnabledMobileSouthKorea;

		public bool SendTelemetryPresence;

		[CompilerGenerated]
		private float <SpecialEventTimingMod>k__BackingField;

		[CompilerGenerated]
		private uint <PVPDRClosedToNewSessionsSeconds>k__BackingField;

		[CompilerGenerated]
		private int <BattlegroundsEarlyAccessLicense>k__BackingField;

		[CompilerGenerated]
		private uint <DuelsEarlyAccessLicense>k__BackingField;

		[CompilerGenerated]
		private string <BattlegroundsLuckyDrawDisabledCountryCode>k__BackingField;

		[CompilerGenerated]
		private List<int> <PetSkinsDenylist>k__BackingField;

		[CompilerGenerated]
		private string <DebugLuckyDrawIgnoredEventTimings>k__BackingField;

		public CacheMisc Misc { get; set; }

		public CacheGames Games { get; set; }

		public CacheCollection Collection { get; set; }

		public CacheStore Store { get; set; }

		public CacheHeroes Heroes { get; set; }

		public CacheMercenaries Mercenaries { get; set; }

		public CacheTraceroute Traceroute { get; set; }

		public CacheReturningPlayer ReturningPlayer { get; set; }

		public int XPSoloLimit { get; set; }

		public int MaxHeroLevel { get; set; }

		public float SpecialEventTimingMod
		{
			[CompilerGenerated]
			set
			{
				<SpecialEventTimingMod>k__BackingField = value;
			}
		}

		public int FriendWeekConcederMaxDefense { get; set; }

		public int FriendWeekConcededGameMinTotalTurns { get; set; }

		public bool FriendWeekAllowsTavernBrawlRecordUpdate { get; set; }

		public uint ArenaClosedToNewSessionsSeconds { get; set; }

		public uint PVPDRClosedToNewSessionsSeconds
		{
			[CompilerGenerated]
			set
			{
				<PVPDRClosedToNewSessionsSeconds>k__BackingField = value;
			}
		}

		public bool QuickOpenEnabled { get; set; }

		public bool ForceIosLowRes { get; set; }

		public bool EnableSmartDeckCompletion { get; set; }

		public bool AllowOfflineClientActivity { get; set; }

		public bool AllowOfflineClientDeckDeletion { get; set; }

		public int BattlegroundsEarlyAccessLicense
		{
			[CompilerGenerated]
			set
			{
				<BattlegroundsEarlyAccessLicense>k__BackingField = value;
			}
		}

		public int BattlegroundsMaxRankedPartySize { get; set; }

		public bool JournalButtonDisabled { get; set; }

		public bool AchievementToastDisabled { get; set; }

		public uint DuelsEarlyAccessLicense
		{
			[CompilerGenerated]
			set
			{
				<DuelsEarlyAccessLicense>k__BackingField = value;
			}
		}

		public bool ContentstackEnabled { get; set; }

		public bool PersonalizedMessagesEnabled { get; set; }

		public bool AppRatingEnabled { get; set; }

		public float AppRatingSamplingPercentage { get; set; }

		public List<int> DuelsCardDenylist { get; set; }

		public List<int> ConstructedCardDenylist { get; set; }

		public List<int> SideboardCardDenylist { get; set; }

		public List<int> TwistCardDenylist { get; set; }

		public List<int> StandardCardDenylist { get; set; }

		public List<int> WildCardDenylist { get; set; }

		public List<int> TavernBrawlCardDenylist { get; set; }

		public List<int> ArenaCardDenylist { get; set; }

		public List<int> TwistDeckTemplateDenylist { get; set; }

		public List<string> VFXDenylist { get; set; }

		public int TwistSeasonOverride { get; set; }

		public int TwistScenarioOverride { get; set; }

		public Dictionary<CardDefinition, int> TwistHeroicDeckHeroHealthOverrides { get; set; }

		public bool RankedPlayEnableScenarioOverrides { get; set; }

		public bool BattlegroundsSkinsEnabled { get; set; }

		public bool BattlegroundsGuideSkinsEnabled { get; set; }

		public bool BattlegroundsBoardSkinsEnabled { get; set; }

		public bool BattlegroundsFinishersEnabled { get; set; }

		public bool BattlegroundsEmotesEnabled { get; set; }

		public bool BattlegroundsRewardTrackEnabled { get; set; }

		public bool TutorialPreviewVideosEnabled { get; set; }

		public float TutorialPreviewVideosTimeout { get; set; }

		public bool MercenariesEnableVillages { get; set; }

		public bool MercenariesPackOpeningEnabled { get; set; }

		public int MercenariesTeamMaxSize { get; set; }

		public int MinHPForProgressAfterConcede { get; set; }

		public int MinTurnsForProgressAfterConcede { get; set; }

		public float BGDuosLeaverRatingPenalty { get; set; }

		public int BGMinTurnsForProgressAfterConcede { get; set; }

		public bool BGCombatSpeedDisabled { get; set; }

		public int BGGuideDisableStatePC { get; set; }

		public int BGGuideDisableStateMobile { get; set; }

		public bool EnablePlayingFromMiniHand { get; set; }

		public bool BattlegroundsMedalFriendListDisplayEnabled { get; set; }

		public bool EnableUpgradeToGolden { get; set; }

		public bool ShouldPrevalidatePastedDeckCodes { get; set; }

		public bool EnableClickToFixDeck { get; set; }

		public bool RecentFriendListDisplayEnabled { get; set; }

		public bool OvercappedDecksEnabled { get; set; }

		public bool ReportPlayerEnabled { get; set; }

		public bool LuckyDrawEnabled { get; set; }

		public bool ContinuousQuickOpenEnabled { get; set; }

		public bool LegacyCardValueCacheEnabled { get; set; }

		public bool BattlenetBillingFlowDisableOverride { get; set; }

		public string BattlegroundsLuckyDrawDisabledCountryCode
		{
			[CompilerGenerated]
			set
			{
				<BattlegroundsLuckyDrawDisabledCountryCode>k__BackingField = value;
			}
		}

		public bool SkippableTutorialEnabled { get; set; }

		public bool EnableNDERerollSpecialCases { get; set; }

		public bool ShopButtonOnPackOpeningScreenEnabled { get; set; }

		public bool MassPackOpeningEnabled { get; set; }

		public int MassPackOpeningPackLimit { get; set; }

		public int MassPackOpeningGoldenPackLimit { get; set; }

		public int MassPackOpeningHooverChunkSize { get; set; }

		public bool MassCatchupPackOpeningEnabled { get; set; }

		public int MassCatchupPackOpeningPackLimit { get; set; }

		public bool CancelMatchmakingDuringLoanerDeckGrant { get; set; }

		public bool AllowBGInviteWhileInNPPG { get; set; }

		public int CardValueOverrideServerRegion { get; set; }

		public bool ArenaRedraftOnLossEnabled { get; set; }

		public int CallSDKInterval { get; set; }

		public bool EnableAllOptionsIDCheck { get; set; }

		public bool EnableInGameWebview { get; set; }

		public bool BoxProductBannerEnabled { get; set; }

		public bool PetSkinsHSEnabled { get; set; }

		public bool PetSkinsBGEnabled { get; set; }

		public int MaxPetTreatsAllowedPerGame { get; set; }

		public List<int> PetSkinsDenylist
		{
			[CompilerGenerated]
			set
			{
				<PetSkinsDenylist>k__BackingField = value;
			}
		}

		public List<int> PetEventDenylist { get; set; }

		public bool ArenaRemovePremiumEnabled { get; set; }

		public int WinsNeededToUnlockUndergroundArena { get; set; }

		public bool EnableArenaPhaseMessageFTUXTracking { get; set; }

		public bool EnableArenaPhaseMessages { get; set; }

		public bool TracerouteEnabled { get; set; }

		public float TrialCardChangedRequestCollectionCacheJitter { get; set; }

		public string DebugLuckyDrawIgnoredEventTimings
		{
			[CompilerGenerated]
			set
			{
				<DebugLuckyDrawIgnoredEventTimings>k__BackingField = value;
			}
		}

		public NetCacheFeatures()
		{
			Misc = new CacheMisc();
			Games = new CacheGames();
			Collection = new CacheCollection();
			Store = new CacheStore();
			Heroes = new CacheHeroes();
			Mercenaries = new CacheMercenaries();
			Traceroute = new CacheTraceroute();
			ReturningPlayer = new CacheReturningPlayer();
		}
	}

	public class NetCacheArcaneDustBalance
	{
		public long Balance { get; set; }
	}

	public class NetCacheGoldBalance
	{
		public long CappedBalance { get; set; }

		public long BonusBalance { get; set; }

		public long GetTotal()
		{
			return CappedBalance + BonusBalance;
		}
	}

	public class NetCacheRenownBalance
	{
		public long Balance { get; set; }
	}

	public class NetCacheBattlegroundsTokenBalance
	{
		public long Balance { get; set; }
	}

	public class NetPlayerArenaTickets
	{
		public int Balance { get; set; }
	}

	public class HeroLevel
	{
		public class LevelInfo
		{
			public int Level { get; set; }

			public int MaxLevel { get; set; }

			public long XP { get; set; }

			public long MaxXP { get; set; }

			public LevelInfo()
			{
				Level = 0;
				MaxLevel = 60;
				XP = 0L;
				MaxXP = 0L;
			}

			public bool IsMaxLevel()
			{
				return Level == MaxLevel;
			}

			public override string ToString()
			{
				return $"[LevelInfo: Level={Level}, XP={XP}, MaxXP={MaxXP}]";
			}
		}

		public TAG_CLASS Class { get; set; }

		public LevelInfo PrevLevel { get; set; }

		public LevelInfo CurrentLevel { get; set; }

		public HeroLevel()
		{
			Class = TAG_CLASS.INVALID;
			PrevLevel = null;
			CurrentLevel = new LevelInfo();
		}

		public override string ToString()
		{
			return $"[HeroLevel: Class={Class}, PrevLevel={PrevLevel}, CurrentLevel={CurrentLevel}]";
		}
	}

	public class NetCacheHeroLevels
	{
		public List<HeroLevel> Levels { get; set; }

		public NetCacheHeroLevels()
		{
			Levels = new List<HeroLevel>();
		}

		public override string ToString()
		{
			string text = "[START NetCacheHeroLevels]\n";
			foreach (HeroLevel level in Levels)
			{
				text += $"{level}\n";
			}
			return text + "[END NetCacheHeroLevels]";
		}
	}

	public class NetCacheProfileProgress
	{
		public TutorialProgress CampaignProgress { get; set; }

		public int BestForgeWins { get; set; }

		public long LastForgeDate { get; set; }

		public bool TutorialComplete { get; set; }
	}

	public class NetCacheDisplayBanner
	{
		public int Id { get; set; }
	}

	public class NetCacheCardBacks
	{
		public HashSet<int> FavoriteCardBacks { get; set; }

		public HashSet<int> CardBacks { get; set; }

		public NetCacheCardBacks()
		{
			FavoriteCardBacks = new HashSet<int>();
			CardBacks = new HashSet<int>();
		}
	}

	public class NetCacheCoins
	{
		public HashSet<int> FavoriteCoins { get; set; }

		public HashSet<int> Coins { get; set; }

		public NetCacheCoins()
		{
			Coins = new HashSet<int>();
			FavoriteCoins = new HashSet<int>();
		}
	}

	public class BoosterStack
	{
		public int Id { get; set; }

		public int Count { get; set; }

		public int EverGrantedCount { get; set; }
	}

	public class NetCacheBoosters
	{
		public List<BoosterStack> BoosterStacks { get; set; }

		public NetCacheBoosters()
		{
			BoosterStacks = new List<BoosterStack>();
		}

		public BoosterStack GetBoosterStack(int id)
		{
			return BoosterStacks.Find((BoosterStack obj) => obj.Id == id);
		}

		public int GetTotalNumBoosters()
		{
			int num = 0;
			foreach (BoosterStack boosterStack in BoosterStacks)
			{
				num += boosterStack.Count;
			}
			return num;
		}
	}

	public class DeckHeader
	{
		public long ID { get; set; }

		public string Name { get; set; }

		public int? CardBack { get; set; }

		public int? CosmeticCoin { get; set; }

		public bool RandomCoinUseFavorite { get; set; }

		public string Hero { get; set; }

		public string UIHeroOverride { get; set; }

		public TAG_PREMIUM UIHeroOverridePremium { get; set; }

		public string HeroPower { get; set; }

		public DeckType Type { get; set; }

		public bool HeroOverridden { get; set; }

		public bool RandomHeroUseFavorite { get; set; }

		public int SeasonId { get; set; }

		public int BrawlLibraryItemId { get; set; }

		public bool NeedsName { get; set; }

		public long SortOrder { get; set; }

		public RuneType Rune1 { get; set; }

		public RuneType Rune2 { get; set; }

		public RuneType Rune3 { get; set; }

		public PegasusShared.FormatType FormatType { get; set; }

		public bool Locked { get; set; }

		public DeckSourceType SourceType { get; set; }

		public DateTime? CreateDate { get; set; }

		public DateTime? LastModified { get; set; }

		public int? Pet { get; set; }

		public int? PetVariant { get; set; }

		public bool RandomPetUseFavorite { get; set; }

		public override string ToString()
		{
			return $"[DeckHeader: ID={ID} Name={Name} Hero={Hero} HeroPower={HeroPower} DeckType={Type} " + $"CardBack={CardBack} CosmeticCoin={CosmeticCoin} RandomCoinUseFavorite={RandomCoinUseFavorite} " + $"HeroOverridden={HeroOverridden} RandomHeroUseFavorite={RandomHeroUseFavorite} " + $"Pet={Pet} PetVariant={PetVariant} RandomHeroUseFavorite={RandomPetUseFavorite}" + $"NeedsName={NeedsName} SortOrder={SortOrder} SourceType={SourceType} Rune1={Rune1} Rune2={Rune2} Rune3={Rune3}";
		}
	}

	public class NetCacheDecks
	{
		public List<DeckHeader> Decks { get; set; }

		public NetCacheDecks()
		{
			Decks = new List<DeckHeader>();
		}
	}

	public class CardDefinition
	{
		public string Name { get; set; }

		public TAG_PREMIUM Premium { get; set; }

		public override bool Equals(object obj)
		{
			if (obj is CardDefinition cardDefinition && Premium == cardDefinition.Premium)
			{
				return Name.Equals(cardDefinition.Name);
			}
			return false;
		}

		public override int GetHashCode()
		{
			return (int)(Name.GetHashCode() + Premium);
		}

		public override string ToString()
		{
			return $"[CardDefinition: Name={Name}, Premium={Premium}]";
		}
	}

	public class CardValue
	{
		public int BaseBuyValue { get; set; }

		public int BaseSellValue { get; set; }

		public int BaseUpgradeValue { get; set; }

		public int BuyValueOverride { get; set; }

		public int SellValueOverride { get; set; }

		public EventTimingType OverrideEvent { get; set; }

		public int GetBuyValue()
		{
			if (!IsOverrideActive() || BuyValueOverride <= 0)
			{
				return BaseBuyValue;
			}
			return BuyValueOverride;
		}

		public int GetSellValue()
		{
			if (!IsOverrideActive() || SellValueOverride <= 0)
			{
				return BaseSellValue;
			}
			return SellValueOverride;
		}

		public int GetUpgradeValue()
		{
			return BaseUpgradeValue;
		}

		public bool IsOverrideActive()
		{
			return EventTimingManager.Get().IsEventActive(OverrideEvent);
		}
	}

	public class NetCacheCardValues
	{
		public Dictionary<CardDefinition, CardValue> Values { get; set; }

		public NetCacheCardValues()
		{
			Values = new Dictionary<CardDefinition, CardValue>();
		}

		public NetCacheCardValues(int initialSize)
		{
			Values = new Dictionary<CardDefinition, CardValue>(initialSize);
		}
	}

	public class NetCacheDisconnectedGame
	{
		public GameServerInfo ServerInfo { get; set; }

		public PegasusShared.GameType GameType { get; set; }

		public PegasusShared.FormatType FormatType { get; set; }

		public bool LoadGameState { get; set; }
	}

	public class BoosterCard
	{
		[CompilerGenerated]
		private long <Date>k__BackingField;

		public CardDefinition Def { get; set; }

		public long Date
		{
			[CompilerGenerated]
			set
			{
				<Date>k__BackingField = value;
			}
		}

		public BoosterCard()
		{
			Def = new CardDefinition();
		}
	}

	public class CardStack
	{
		public CardDefinition Def { get; set; }

		public long Date { get; set; }

		public int Count { get; set; }

		public int NumSeen { get; set; }

		public CardStack()
		{
			Def = new CardDefinition();
		}
	}

	public class NetCacheCollection
	{
		public int TotalCardsOwned;

		public Map<TAG_CLASS, HashSet<string>> CoreCardsUnlockedPerClass = new Map<TAG_CLASS, HashSet<string>>();

		public List<CardStack> Stacks { get; set; }

		public NetCacheCollection()
		{
			Stacks = new List<CardStack>();
			foreach (TAG_CLASS value in Enum.GetValues(typeof(TAG_CLASS)))
			{
				CoreCardsUnlockedPerClass[value] = new HashSet<string>();
			}
		}
	}

	public class PlayerRecord
	{
		[CompilerGenerated]
		private int <Ties>k__BackingField;

		public PegasusShared.GameType RecordType { get; set; }

		public int Data { get; set; }

		public int Wins { get; set; }

		public int Losses { get; set; }

		public int Ties
		{
			[CompilerGenerated]
			set
			{
				<Ties>k__BackingField = value;
			}
		}
	}

	public class NetCachePlayerRecords
	{
		public List<PlayerRecord> Records { get; set; }

		public NetCachePlayerRecords()
		{
			Records = new List<PlayerRecord>();
		}
	}

	public class NetCacheRewardProgress
	{
		public int Season { get; set; }

		public long SeasonEndDate { get; set; }

		public long NextQuestCancelDate { get; set; }
	}

	public class NetCacheMedalInfo
	{
		public Map<PegasusShared.FormatType, MedalInfoData> MedalData = new Map<PegasusShared.FormatType, MedalInfoData>();

		private static Map<PegasusShared.FormatType, int> m_cheatLocalOverrideStarLevelData = new Map<PegasusShared.FormatType, int>();

		private static Map<PegasusShared.FormatType, int> m_cheatLocalOverrideLegendRankData = new Map<PegasusShared.FormatType, int>();

		public NetCacheMedalInfo PreviousMedalInfo { get; set; }

		public NetCacheMedalInfo()
		{
		}

		public NetCacheMedalInfo(MedalInfo packet)
		{
			foreach (MedalInfoData medalDatum in packet.MedalData)
			{
				MedalData.Add(medalDatum.FormatType, medalDatum);
			}
			foreach (KeyValuePair<PegasusShared.FormatType, int> cheatLocalOverrideStarLevelDatum in m_cheatLocalOverrideStarLevelData)
			{
				MedalData[cheatLocalOverrideStarLevelDatum.Key].StarLevel = cheatLocalOverrideStarLevelDatum.Value;
			}
			foreach (KeyValuePair<PegasusShared.FormatType, int> cheatLocalOverrideLegendRankDatum in m_cheatLocalOverrideLegendRankData)
			{
				MedalData[cheatLocalOverrideLegendRankDatum.Key].LegendRank = cheatLocalOverrideLegendRankDatum.Value;
			}
		}

		public NetCacheMedalInfo Clone()
		{
			NetCacheMedalInfo netCacheMedalInfo = new NetCacheMedalInfo();
			foreach (KeyValuePair<PegasusShared.FormatType, MedalInfoData> medalDatum in MedalData)
			{
				netCacheMedalInfo.MedalData.Add(medalDatum.Key, CloneMedalInfoData(medalDatum.Value));
			}
			return netCacheMedalInfo;
		}

		public MedalInfoData GetMedalInfoData(PegasusShared.FormatType formatType)
		{
			if (!MedalData.TryGetValue(formatType, out var value))
			{
				Debug.LogError("NetCacheMedalInfo.GetMedalInfoData failed to find data for the format type " + formatType.ToString() + ". Returning null");
			}
			return value;
		}

		public void CheatLocalOverrideStarLevel(PegasusShared.FormatType formatType, int starLevel)
		{
			m_cheatLocalOverrideStarLevelData[formatType] = starLevel;
			MedalData[formatType].StarLevel = starLevel;
		}

		public void CheatLocalOverrideLegendRank(PegasusShared.FormatType formatType, int legendRank)
		{
			m_cheatLocalOverrideLegendRankData[formatType] = legendRank;
			MedalData[formatType].LegendRank = legendRank;
		}

		public static void CheatLocalOverrideClear()
		{
			m_cheatLocalOverrideStarLevelData.Clear();
			m_cheatLocalOverrideLegendRankData.Clear();
		}

		public static MedalInfoData CloneMedalInfoData(MedalInfoData original)
		{
			MedalInfoData medalInfoData = new MedalInfoData();
			medalInfoData.LeagueId = original.LeagueId;
			medalInfoData.SeasonWins = original.SeasonWins;
			medalInfoData.Stars = original.Stars;
			medalInfoData.Streak = original.Streak;
			medalInfoData.StarLevel = original.StarLevel;
			medalInfoData.HasLegendRank = original.HasLegendRank;
			medalInfoData.LegendRank = original.LegendRank;
			medalInfoData.HasBestStarLevel = original.HasBestStarLevel;
			medalInfoData.BestStarLevel = original.BestStarLevel;
			medalInfoData.HasSeasonGames = original.HasSeasonGames;
			medalInfoData.SeasonGames = original.SeasonGames;
			medalInfoData.StarsPerWin = original.StarsPerWin;
			if (original.HasRatingId)
			{
				medalInfoData.RatingId = original.RatingId;
			}
			if (original.HasSeasonId)
			{
				medalInfoData.SeasonId = original.SeasonId;
			}
			if (original.HasRating)
			{
				medalInfoData.Rating = original.Rating;
			}
			if (original.HasVariance)
			{
				medalInfoData.Variance = original.Variance;
			}
			if (original.HasBestStars)
			{
				medalInfoData.BestStars = original.BestStars;
			}
			if (original.HasBestEverLeagueId)
			{
				medalInfoData.BestEverLeagueId = original.BestEverLeagueId;
			}
			if (original.HasBestEverStarLevel)
			{
				medalInfoData.BestEverStarLevel = original.BestEverStarLevel;
			}
			if (original.HasBestRating)
			{
				medalInfoData.BestRating = original.BestRating;
			}
			if (original.HasPublicRating)
			{
				medalInfoData.PublicRating = original.PublicRating;
			}
			if (original.HasFormatType)
			{
				medalInfoData.FormatType = original.FormatType;
			}
			return medalInfoData;
		}

		public override string ToString()
		{
			return $"[NetCacheMedalInfo] \n MedalData={MedalData.ToString()}";
		}
	}

	public class NetCacheBaconRatingInfo
	{
		public int Rating { get; set; }

		public int DuosRating { get; set; }

		public override string ToString()
		{
			return string.Format("[NetCacheBaconRatingInfo] \n Rating={0} \n DuosRating={0}", Rating, DuosRating);
		}
	}

	public class NetCachePVPDRStatsInfo
	{
		public int Rating { get; set; }

		public int PaidRating { get; set; }

		public int HighWatermark { get; set; }

		public override string ToString()
		{
			return $"[NetCachePVPDRStatsInfo] \n Rating={Rating} PaidRating={PaidRating} HighWatermark={HighWatermark}";
		}
	}

	public class NetCacheMercenariesPlayerInfo
	{
		public class BountyInfo
		{
			public int FewestTurns { get; set; }

			public int Completions { get; set; }

			public bool IsComplete { get; set; }

			public bool IsAcknowledged { get; set; }

			public List<uint> BossCardIds { get; set; }

			public int MaxMythicLevel { get; set; }

			public DateTime? UnlockTime { get; set; }

			public BountyInfo Clone()
			{
				BountyInfo bountyInfo = new BountyInfo();
				bountyInfo.FewestTurns = FewestTurns;
				bountyInfo.Completions = Completions;
				bountyInfo.IsComplete = IsComplete;
				bountyInfo.IsAcknowledged = IsAcknowledged;
				if (BossCardIds != null)
				{
					bountyInfo.BossCardIds = new List<uint>();
					bountyInfo.BossCardIds.AddRange(BossCardIds);
				}
				bountyInfo.MaxMythicLevel = MaxMythicLevel;
				bountyInfo.UnlockTime = UnlockTime;
				return bountyInfo;
			}
		}

		public Dictionary<MercenaryBuilding.Mercenarybuildingtype, bool> BuildingEnabledMap;

		public List<int> DisabledMercenaryList;

		public HashSet<int> DisabledVisitorList;

		public List<int> DisabledBuildingTierUpgradeList;

		public int PvpRating { get; set; }

		public uint PvpRewardChestWinsProgress { get; set; }

		public uint PvpRewardChestWinsRequired { get; set; }

		public Dictionary<int, BountyInfo> BountyInfoMap { get; set; }

		public int PvpSeasonHighestRating { get; set; }

		public int PvpSeasonId { get; set; }

		public int CurrentMythicBountyLevel { get; set; }

		public int MinMythicBountyLevel { get; set; }

		public int MaxMythicBountyLevel { get; set; }

		public DateTime GeneratedBountyResetTime { get; set; }

		public override string ToString()
		{
			return $"[NetCacheMercenariesPlayerInfo] \n PvpRating={PvpRating}, PvpRewardChestWinsProgress={PvpRewardChestWinsProgress}, PvpRewardChestWinsRequired={PvpRewardChestWinsRequired}";
		}
	}

	public class NetCacheMercenariesVillageInfo
	{
		[CompilerGenerated]
		private List<MercenariesBuildingState> <LastBuildingUpdate>k__BackingField;

		private readonly List<int> m_emptyTierList = new List<int>();

		private Dictionary<int, List<int>> m_tierTreeCache = new Dictionary<int, List<int>>();

		private Dictionary<int, int> m_unbuiltTierLookup = new Dictionary<int, int>();

		private Dictionary<TAG_RARITY, int> m_renownConversionLookup = new Dictionary<TAG_RARITY, int>();

		public bool Initialized { get; set; }

		public List<MercenariesBuildingState> BuildingStates { get; set; }

		public List<MercenariesBuildingState> LastBuildingUpdate
		{
			[CompilerGenerated]
			set
			{
				<LastBuildingUpdate>k__BackingField = value;
			}
		}

		public List<MercenariesRenownConvertRate> ConversionRates { get; private set; }

		public int UnlockedBountyDifficultyLevel { get; private set; }

		public void TrySetDifficultyUnlock(MercenariesBuildingState bldgState)
		{
			if (GameDbf.MercenaryBuilding.GetRecord(bldgState.BuildingId).MercenaryBuildingType != MercenaryBuilding.Mercenarybuildingtype.PVEZONES)
			{
				return;
			}
			foreach (TierPropertiesDbfRecord mercenaryBuildingTierProperty in GameDbf.BuildingTier.GetRecord(bldgState.CurrentTierId).MercenaryBuildingTierProperties)
			{
				if (mercenaryBuildingTierProperty.TierPropertyType == TierProperties.Buildingtierproperty.PVEMODE)
				{
					UnlockedBountyDifficultyLevel = mercenaryBuildingTierProperty.TierPropertyValue;
					break;
				}
			}
		}

		public List<int> GetNextTierListByTierId(int tierId)
		{
			if (m_tierTreeCache.TryGetValue(tierId, out var value))
			{
				return value;
			}
			return m_emptyTierList;
		}

		public bool BuildingIsBuilt(MercenariesBuildingState bldgState)
		{
			if (m_unbuiltTierLookup.TryGetValue(bldgState.BuildingId, out var value))
			{
				return bldgState.CurrentTierId != value;
			}
			return false;
		}

		public void CacheTierTree()
		{
			if (m_tierTreeCache.Count > 0)
			{
				m_tierTreeCache.Clear();
			}
			foreach (MercenaryBuildingDbfRecord bldg in GameDbf.MercenaryBuilding.GetRecords())
			{
				BuildingTierDbfRecord record = GameDbf.BuildingTier.GetRecord((BuildingTierDbfRecord r) => r.MercenaryBuildingId == bldg.ID);
				m_unbuiltTierLookup.Add(bldg.ID, record.ID);
				AddTierToTierTreeCache(bldg.DefaultTier);
			}
		}

		private void AddTierToTierTreeCache(int tierId)
		{
			if (m_tierTreeCache.ContainsKey(tierId))
			{
				return;
			}
			List<int> list = new List<int>();
			m_tierTreeCache.Add(tierId, list);
			List<NextTiersDbfRecord> records = GameDbf.NextTiers.GetRecords((NextTiersDbfRecord r) => r.BuildingTierId == tierId);
			if (records == null || records.Count == 0)
			{
				return;
			}
			foreach (NextTiersDbfRecord item in records)
			{
				list.Add(item.NextTierId);
				AddTierToTierTreeCache(item.NextTierId);
			}
		}

		public void CacheRenownConversionRates(List<MercenariesRenownConvertRate> conversionRates)
		{
			ConversionRates = conversionRates;
			m_renownConversionLookup.Clear();
			if (ConversionRates == null || ConversionRates.Count == 0)
			{
				return;
			}
			foreach (MercenariesRenownConvertRate conversionRate in conversionRates)
			{
				TAG_RARITY coinRarityId = (TAG_RARITY)conversionRate.CoinRarityId;
				if (m_renownConversionLookup.ContainsKey(coinRarityId))
				{
					Log.Lettuce.PrintError($"Duplicate rarity {coinRarityId} in renown conversion rates - Skipping value");
				}
				else if (conversionRate.CoinConversionRate != 0)
				{
					m_renownConversionLookup[coinRarityId] = (int)conversionRate.CoinConversionRate;
				}
			}
		}

		public bool TryGetRenownRate(TAG_RARITY rarity, out int conversionRate)
		{
			return m_renownConversionLookup.TryGetValue(rarity, out conversionRate);
		}
	}

	public class NetCacheMercenariesVillageVisitorInfo
	{
		public List<MercenariesVisitorState> VisitorStates { get; set; }

		public int[] VisitingMercenaries { get; set; }

		public List<MercenariesTaskState> CompletedTasks { get; set; }

		public List<MercenariesCompletedVisitorState> CompletedVisitorStates { get; set; }

		public List<MercenariesRenownOfferData> ActiveRenownOffers { get; set; }
	}

	public class NetCacheMercenariesMythicTreasureInfo
	{
		public class MythicTreasureScalar
		{
			public int TreasureId { get; set; }

			public int Scalar { get; set; }
		}

		public Dictionary<int, MythicTreasureScalar> MythicTreasureScalarMap;
	}

	public abstract class ProfileNotice
	{
		public enum NoticeType
		{
			GAINED_MEDAL = 1,
			REWARD_BOOSTER = 2,
			REWARD_CARD = 3,
			DISCONNECTED_GAME = 4,
			PRECON_DECK = 5,
			REWARD_DUST = 6,
			REWARD_MOUNT = 7,
			REWARD_FORGE = 8,
			REWARD_CURRENCY = 9,
			PURCHASE = 10,
			REWARD_CARD_BACK = 11,
			BONUS_STARS = 12,
			ADVENTURE_PROGRESS = 14,
			HERO_LEVEL_UP = 15,
			ACCOUNT_LICENSE = 16,
			TAVERN_BRAWL_REWARDS = 17,
			TAVERN_BRAWL_TICKET = 18,
			EVENT = 19,
			GENERIC_REWARD_CHEST = 20,
			LEAGUE_PROMOTION_REWARDS = 21,
			CARD_REPLACEMENT = 22,
			DISCONNECTED_GAME_NEW = 23,
			DECK_REMOVED = 25,
			DECK_GRANTED = 26,
			MINI_SET_GRANTED = 27,
			SELLABLE_DECK_GRANTED = 28,
			REWARD_BATTLEGROUNDS_GUIDE = 29,
			REWARD_BATTLEGROUNDS_HERO = 30,
			MERCENARIES_REWARDS_CURRENCY = 31,
			MERCENARIES_REWARDS_EXPERIENCE = 32,
			MERCENARIES_REWARDS_EQUIPMENT = 33,
			MERCENARIES_REWARDS = 34,
			MERCENARIES_ABILITY_UNLOCK = 35,
			MERCENARIES_MERC_FULL_UPGRADE = 36,
			MERCENARIES_MERC_LICENSE = 37,
			MERCENARIES_CURRENCY_LICENSE = 38,
			MERCENARIES_BOOSTER_LICENSE = 39,
			MERCENARIES_RANDOM_REWARD_LICENSE = 40,
			MERCENARIES_SEASON_ROLL = 41,
			MERCENARIES_SEASON_REWARDS = 42,
			MERCENARIES_ZONE_UNLOCK = 43,
			REWARD_BATTLEGROUNDS_BOARD_SKIN = 44,
			REWARD_BATTLEGROUNDS_FINISHER = 45,
			REWARD_BATTLEGROUNDS_EMOTE = 46,
			REWARD_LUCKY_DRAW = 47,
			REDUNDANT_NDE_REROLL = 48,
			REDUNDANT_NDE_REROLL_RESULT = 49,
			REWARD_PET = 50,
			REDUNDANT_NDE_REROLL_DUPLCIATE_SIGNATURE = 51
		}

		public enum NoticeOrigin
		{
			UNKNOWN = -1,
			SEASON = 1,
			BETA_REIMBURSE = 2,
			FORGE = 3,
			TOURNEY = 4,
			PRECON_DECK = 5,
			ACK = 6,
			ACHIEVEMENT = 7,
			LEVEL_UP = 8,
			PURCHASE_COMPLETE = 10,
			PURCHASE_FAILED = 11,
			PURCHASE_CANCELED = 12,
			BLIZZCON = 13,
			EVENT = 14,
			DISCONNECTED_GAME = 15,
			OUT_OF_BAND_LICENSE = 16,
			IGR = 17,
			ADVENTURE_PROGRESS = 18,
			ADVENTURE_FLAGS = 19,
			TAVERN_BRAWL_REWARD = 20,
			ACCOUNT_LICENSE_FLAGS = 21,
			FROM_PURCHASE = 22,
			HOF_COMPENSATION = 23,
			GENERIC_REWARD_CHEST_ACHIEVE = 24,
			GENERIC_REWARD_CHEST = 25,
			LEAGUE_PROMOTION = 26,
			CARD_REPLACEMENT = 27,
			NOTICE_ORIGIN_LEVEL_UP_MULTIPLE = 28,
			NOTICE_ORIGIN_DUELS = 29,
			NOTICE_ORIGIN_MERCENARIES = 30,
			NOTICE_ORIGIN_LUCKY_DRAW = 31,
			NOTICE_ORIGIN_NDE_REDUNDANT_REROLL = 32,
			NOTICE_ORIGIN_NDE_REDUNDANT_REROLL_DUPLICATE_SIGNATURE_CARD = 33
		}

		private NoticeType m_type;

		public long NoticeID { get; set; }

		public NoticeType Type => m_type;

		public NoticeOrigin Origin { get; set; }

		public long OriginData { get; set; }

		public long Date { get; set; }

		protected ProfileNotice(NoticeType init)
		{
			m_type = init;
			NoticeID = 0L;
			Origin = NoticeOrigin.UNKNOWN;
			OriginData = 0L;
			Date = 0L;
		}

		public override string ToString()
		{
			return $"[{GetType()}: NoticeID={NoticeID}, Type={Type}, Origin={Origin}, OriginData={OriginData}, Date={Date}]";
		}
	}

	public class ProfileNoticeMedal : ProfileNotice
	{
		public int LeagueId { get; set; }

		public int StarLevel { get; set; }

		public int LegendRank { get; set; }

		public int BestStarLevel { get; set; }

		public PegasusShared.FormatType FormatType { get; set; }

		public Network.RewardChest Chest { get; set; }

		public bool WasLimitedByBestEverStarLevel { get; set; }

		public ProfileNoticeMedal()
			: base(NoticeType.GAINED_MEDAL)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [LeagueId={LeagueId} StarLevel={StarLevel}, LegendRank={LegendRank}, BestStarLevel={BestStarLevel}, FormatType={FormatType}, Chest={Chest}, WasLimitedByBestEverStarLevel={WasLimitedByBestEverStarLevel}]";
		}
	}

	public class ProfileNoticeRewardBooster : ProfileNotice
	{
		public int Id { get; set; }

		public int Count { get; set; }

		public ProfileNoticeRewardBooster()
			: base(NoticeType.REWARD_BOOSTER)
		{
			Id = 0;
			Count = 0;
		}

		public override string ToString()
		{
			return $"{base.ToString()} [Id={Id}, Count={Count}]";
		}
	}

	public class ProfileNoticeRewardCard : ProfileNotice
	{
		public string CardID { get; set; }

		public TAG_PREMIUM Premium { get; set; }

		public int Quantity { get; set; }

		public ProfileNoticeRewardCard()
			: base(NoticeType.REWARD_CARD)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [CardID={CardID}, Premium={Premium}, Quantity={Quantity}]";
		}
	}

	public class ProfileNoticeRewardBattlegroundsGuideSkin : ProfileNotice
	{
		public string CardID { get; set; }

		public int FixedRewardMapID { get; set; }

		public ProfileNoticeRewardBattlegroundsGuideSkin()
			: base(NoticeType.REWARD_BATTLEGROUNDS_GUIDE)
		{
		}

		public override string ToString()
		{
			return string.Format("{0}", base.ToString(), CardID);
		}
	}

	public class ProfileNoticeRewardBattlegroundsHeroSkin : ProfileNotice
	{
		public string CardID { get; set; }

		public int FixedRewardMapID { get; set; }

		public ProfileNoticeRewardBattlegroundsHeroSkin()
			: base(NoticeType.REWARD_BATTLEGROUNDS_HERO)
		{
		}

		public override string ToString()
		{
			return string.Format("{0}", base.ToString(), CardID);
		}
	}

	public class ProfileNoticePreconDeck : ProfileNotice
	{
		public long DeckID { get; set; }

		public int HeroAsset { get; set; }

		public ProfileNoticePreconDeck()
			: base(NoticeType.PRECON_DECK)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [DeckID={DeckID}, HeroAsset={HeroAsset}]";
		}
	}

	public class ProfileNoticeDeckRemoved : ProfileNotice
	{
		public long DeckID { get; set; }

		public ProfileNoticeDeckRemoved()
			: base(NoticeType.DECK_REMOVED)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [DeckID={DeckID}]";
		}
	}

	public class ProfileNoticeDeckGranted : ProfileNotice
	{
		public int DeckDbiID { get; set; }

		public int ClassId { get; set; }

		public long PlayerDeckID { get; set; }

		public ProfileNoticeDeckGranted()
			: base(NoticeType.DECK_GRANTED)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [DeckDbiID={DeckDbiID}, ClassId={ClassId}]";
		}
	}

	public class ProfileNoticeMiniSetGranted : ProfileNotice
	{
		public int MiniSetID { get; set; }

		public int Premium { get; set; }

		public ProfileNoticeMiniSetGranted()
			: base(NoticeType.MINI_SET_GRANTED)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [CardsRewardID={MiniSetID}]";
		}
	}

	public class ProfileNoticeSellableDeckGranted : ProfileNotice
	{
		public int SellableDeckID { get; set; }

		public long PlayerDeckID { get; set; }

		public TAG_PREMIUM Premium { get; set; }

		public ProfileNoticeSellableDeckGranted()
			: base(NoticeType.SELLABLE_DECK_GRANTED)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [SellableDeckID={SellableDeckID}]";
		}
	}

	public class ProfileNoticeRewardDust : ProfileNotice
	{
		public int Amount { get; set; }

		public ProfileNoticeRewardDust()
			: base(NoticeType.REWARD_DUST)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [Amount={Amount}]";
		}
	}

	public class ProfileNoticeRewardMount : ProfileNotice
	{
		public int MountID { get; set; }

		public ProfileNoticeRewardMount()
			: base(NoticeType.REWARD_MOUNT)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [MountID={MountID}]";
		}
	}

	public class ProfileNoticeRewardForge : ProfileNotice
	{
		public int Quantity { get; set; }

		public ProfileNoticeRewardForge()
			: base(NoticeType.REWARD_FORGE)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [Quantity={Quantity}]";
		}
	}

	public class ProfileNoticeRewardCurrency : ProfileNotice
	{
		public int Amount { get; set; }

		public PegasusShared.CurrencyType CurrencyType { get; set; }

		public ProfileNoticeRewardCurrency()
			: base(NoticeType.REWARD_CURRENCY)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [CurrencyType={CurrencyType.ToString()}, Amount={Amount}]";
		}
	}

	public class ProfileNoticePurchase : ProfileNotice
	{
		public long? PMTProductID { get; set; }

		public string CurrencyCode { get; set; }

		public long Data { get; set; }

		public ProfileNoticePurchase()
			: base(NoticeType.PURCHASE)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticePurchase: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} PMTProductID='{PMTProductID}', Data={Data} Currency={CurrencyCode}]";
		}
	}

	public class ProfileNoticeRewardCardBack : ProfileNotice
	{
		public int CardBackID { get; set; }

		public ProfileNoticeRewardCardBack()
			: base(NoticeType.REWARD_CARD_BACK)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticePurchase: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} CardBackID={CardBackID}]";
		}
	}

	public class ProfileNoticeBonusStars : ProfileNotice
	{
		public int StarLevel { get; set; }

		public int Stars { get; set; }

		public ProfileNoticeBonusStars()
			: base(NoticeType.BONUS_STARS)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [StarLevel={StarLevel}, Stars={Stars}]";
		}
	}

	public class ProfileNoticeEvent : ProfileNotice
	{
		public int EventType { get; }
	}

	public class ProfileNoticeDisconnectedGame : ProfileNotice
	{
		public PegasusShared.GameType GameType { get; set; }

		public PegasusShared.FormatType FormatType { get; set; }

		public int MissionId { get; set; }

		public ProfileNoticeDisconnectedGameResult.GameResult GameResult { get; set; }

		public ProfileNoticeDisconnectedGameResult.PlayerResult YourResult { get; set; }

		public ProfileNoticeDisconnectedGameResult.PlayerResult OpponentResult { get; set; }

		public int PlayerIndex { get; set; }

		public ProfileNoticeDisconnectedGame()
			: base(NoticeType.DISCONNECTED_GAME)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [GameType={GameType}, FormatType={FormatType}, MissionId={MissionId} GameResult={GameResult}, YourResult={YourResult}, OpponentResult={OpponentResult}, PlayerIndex={PlayerIndex}]";
		}
	}

	public class ProfileNoticeAdventureProgress : ProfileNotice
	{
		public int Wing { get; set; }

		public int? Progress { get; set; }

		public ulong? Flags { get; set; }

		public ProfileNoticeAdventureProgress()
			: base(NoticeType.ADVENTURE_PROGRESS)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [Wing={Wing}, Progress={Progress}, Flags={Flags}]";
		}
	}

	public class ProfileNoticeLevelUp : ProfileNotice
	{
		public int HeroClass { get; set; }

		public int NewLevel { get; set; }

		public int TotalLevel { get; set; }

		public ProfileNoticeLevelUp()
			: base(NoticeType.HERO_LEVEL_UP)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [HeroClass={HeroClass}, NewLevel={NewLevel}], TotalLevel={TotalLevel}";
		}
	}

	public class ProfileNoticeAcccountLicense : ProfileNotice
	{
		public long License { get; set; }

		public long CasID { get; set; }

		public ProfileNoticeAcccountLicense()
			: base(NoticeType.ACCOUNT_LICENSE)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [License={License}, CasID={CasID}]";
		}
	}

	public class ProfileNoticeTavernBrawlRewards : ProfileNotice
	{
		public RewardChest Chest { get; set; }

		public int Wins { get; set; }

		public TavernBrawlMode Mode { get; set; }

		public ProfileNoticeTavernBrawlRewards()
			: base(NoticeType.TAVERN_BRAWL_REWARDS)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [Chest={Chest}, Wins={Wins}, Mode={Mode}]";
		}
	}

	public class ProfileNoticeTavernBrawlTicket : ProfileNotice
	{
		[CompilerGenerated]
		private int <TicketType>k__BackingField;

		[CompilerGenerated]
		private int <Quantity>k__BackingField;

		public int TicketType
		{
			[CompilerGenerated]
			set
			{
				<TicketType>k__BackingField = value;
			}
		}

		public int Quantity
		{
			[CompilerGenerated]
			set
			{
				<Quantity>k__BackingField = value;
			}
		}

		public ProfileNoticeTavernBrawlTicket()
			: base(NoticeType.TAVERN_BRAWL_TICKET)
		{
		}
	}

	public class ProfileNoticeGenericRewardChest : ProfileNotice
	{
		public int RewardChestAssetId { get; set; }

		public RewardChest RewardChest { get; set; }

		public uint RewardChestByteSize { get; set; }

		public byte[] RewardChestHash { get; set; }

		public ProfileNoticeGenericRewardChest()
			: base(NoticeType.GENERIC_REWARD_CHEST)
		{
		}
	}

	public class NetCacheProfileNotices
	{
		public List<ProfileNotice> Notices { get; set; }

		public NetCacheProfileNotices()
		{
			Notices = new List<ProfileNotice>();
		}
	}

	public class ProfileNoticeLeaguePromotionRewards : ProfileNotice
	{
		public RewardChest Chest { get; set; }

		public int LeagueId { get; set; }

		public ProfileNoticeLeaguePromotionRewards()
			: base(NoticeType.LEAGUE_PROMOTION_REWARDS)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [Chest={Chest}, LeagueId={LeagueId}]";
		}
	}

	public class ProfileNoticeMercenariesRewards : ProfileNotice
	{
		public PegasusShared.ProfileNoticeMercenariesRewards.RewardType RewardType { get; set; }

		public RewardChest Chest { get; set; }

		public ProfileNoticeMercenariesRewards()
			: base(NoticeType.MERCENARIES_REWARDS)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [Chest={Chest}]";
		}
	}

	public class ProfileNoticeMercenariesAbilityUnlock : ProfileNotice
	{
		public int MercenaryId { get; set; }

		public int AbilityId { get; set; }

		public ProfileNoticeMercenariesAbilityUnlock()
			: base(NoticeType.MERCENARIES_ABILITY_UNLOCK)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeMercenariesAbilityUnlock: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} MercenaryId={MercenaryId} AbilityId={AbilityId}]";
		}
	}

	public class ProfileNoticeMercenariesZoneUnlock : ProfileNotice
	{
		public int ZoneId { get; set; }

		public ProfileNoticeMercenariesZoneUnlock()
			: base(NoticeType.MERCENARIES_ZONE_UNLOCK)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeMercenariesZoneUnlock: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} ZoneId={ZoneId}]";
		}
	}

	public class ProfileNoticeRewardBattlegroundsBoard : ProfileNotice
	{
		public long BoardSkinID { get; set; }

		public int FixedRewardMapID { get; set; }

		public ProfileNoticeRewardBattlegroundsBoard()
			: base(NoticeType.REWARD_BATTLEGROUNDS_BOARD_SKIN)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [BoardSkinID={BoardSkinID}]";
		}
	}

	public class ProfileNoticeRewardBattlegroundsFinisher : ProfileNotice
	{
		public long FinisherID { get; set; }

		public int FixedRewardMapID { get; set; }

		public ProfileNoticeRewardBattlegroundsFinisher()
			: base(NoticeType.REWARD_BATTLEGROUNDS_FINISHER)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [FinisherID={FinisherID}]";
		}
	}

	public class ProfileNoticeRewardBattlegroundsEmote : ProfileNotice
	{
		public long EmoteID { get; set; }

		public int FixedRewardMapID { get; set; }

		public ProfileNoticeRewardBattlegroundsEmote()
			: base(NoticeType.REWARD_BATTLEGROUNDS_EMOTE)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [EmoteID={EmoteID}]";
		}
	}

	public class ProfileNoticeMercenariesSeasonRoll : ProfileNotice
	{
		public int EndedSeasonId { get; set; }

		public int HighestSeasonRating { get; set; }

		public ProfileNoticeMercenariesSeasonRoll()
			: base(NoticeType.MERCENARIES_SEASON_ROLL)
		{
		}
	}

	public class ProfileNoticeMercenariesBoosterLicense : ProfileNotice
	{
		public int Count { get; set; }

		public ProfileNoticeMercenariesBoosterLicense()
			: base(NoticeType.MERCENARIES_BOOSTER_LICENSE)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeMercenariesBoosterLicense: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} Count={Count}]";
		}
	}

	public class ProfileNoticeMercenariesCurrencyLicense : ProfileNotice
	{
		public int MercenaryId { get; set; }

		public long CurrencyAmount { get; set; }

		public ProfileNoticeMercenariesCurrencyLicense()
			: base(NoticeType.MERCENARIES_CURRENCY_LICENSE)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeMercenariesBoosterLicense: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} MercenaryId={MercenaryId} CurrencyAmount={CurrencyAmount}]";
		}
	}

	public class ProfileNoticeMercenariesMercenaryLicense : ProfileNotice
	{
		public int MercenaryId { get; set; }

		public int ArtVariationId { get; set; }

		public uint ArtVariationPremium { get; set; }

		public long CurrencyAmount { get; set; }

		public ProfileNoticeMercenariesMercenaryLicense()
			: base(NoticeType.MERCENARIES_MERC_LICENSE)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeMercenariesMercenaryLicense: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} MercenaryId={MercenaryId}, ArtVariationId={ArtVariationId}, ArtVariationPremium={ArtVariationPremium} CurrencyAmount={CurrencyAmount}]";
		}
	}

	public class ProfileNoticeMercenariesRandomRewardLicense : ProfileNotice
	{
		public int MercenaryId { get; set; }

		public int ArtVariationId { get; set; }

		public uint ArtVariationPremium { get; set; }

		public long CurrencyAmount { get; set; }

		public bool IsConvertedMercenary { get; set; }

		public ProfileNoticeMercenariesRandomRewardLicense()
			: base(NoticeType.MERCENARIES_RANDOM_REWARD_LICENSE)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeMercenariesRandomRewardLicense: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} MercenaryId={MercenaryId}, ArtVariationId={ArtVariationId}, ArtVariationPremium={ArtVariationPremium} CurrencyAmount={CurrencyAmount}]";
		}
	}

	public class ProfileNoticeMercenariesMercenaryFullyUpgraded : ProfileNotice
	{
		public int MercenaryId { get; set; }

		public ProfileNoticeMercenariesMercenaryFullyUpgraded()
			: base(NoticeType.MERCENARIES_MERC_FULL_UPGRADE)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeMercenariesAbilityUnlock: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} MercenaryId={MercenaryId}";
		}
	}

	public class ProfileNoticeMercenariesSeasonRewards : ProfileNotice
	{
		public RewardChest Chest { get; set; }

		public int RewardAssetId { get; set; }

		public ProfileNoticeMercenariesSeasonRewards()
			: base(NoticeType.MERCENARIES_SEASON_REWARDS)
		{
		}

		public override string ToString()
		{
			return $"[Chest={Chest}, RewardAssetId={RewardAssetId}]";
		}
	}

	public class ProfileNoticeLuckyDrawReward : ProfileNotice
	{
		public int LuckyDrawRewardId { get; set; }

		public PegasusShared.ProfileNoticeLuckyDrawReward.OriginType LuckyDrawOrigin { get; set; }

		public ProfileNoticeLuckyDrawReward()
			: base(NoticeType.REWARD_LUCKY_DRAW)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticeLuckyDrawReward: LuckyDrawRewardAssetId={LuckyDrawRewardId}, LuckyDrawOrigin={LuckyDrawOrigin}]";
		}
	}

	public class ProfileNoticeRedundantNDEReroll : ProfileNotice
	{
		public string CardID { get; set; }

		public TAG_PREMIUM Premium { get; set; }

		public TAG_PREMIUM RerollPremiumOverride { get; set; }

		public ProfileNoticeRedundantNDEReroll()
			: base(NoticeType.REDUNDANT_NDE_REROLL)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()} [CardID={CardID}, Premium={Premium}]";
		}
	}

	public class ProfileNoticeRedundantNDERerollResult : ProfileNotice
	{
		public int RerolledCardID { get; set; }

		public int GrantedCardID { get; set; }

		public TAG_PREMIUM Premium { get; set; }

		public ProfileNoticeRedundantNDERerollResult()
			: base(NoticeType.REDUNDANT_NDE_REROLL_RESULT)
		{
		}

		public override string ToString()
		{
			return $"{base.ToString()}, [RerolledCardID={RerolledCardID}, GrantedCardID={GrantedCardID}, Premium={Premium}]";
		}
	}

	public class ProfileNoticeRewardPet : ProfileNotice
	{
		public int PetId { get; set; }

		public int PetVariantId { get; set; }

		public ProfileNoticeRewardPet()
			: base(NoticeType.REWARD_PET)
		{
		}

		public override string ToString()
		{
			return $"[ProfileNoticePurchase: NoticeID={base.NoticeID}, Type={base.Type}, Origin={base.Origin}, OriginData={base.OriginData}, Date={base.Date} PetID={PetId} PetVariantID={PetVariantId}]";
		}
	}

	public class ProfileNoticeRedundantNDERerollDuplicateSignature : ProfileNotice
	{
		public int DuplicateSignaureCardID { get; set; }

		public ProfileNoticeRedundantNDERerollDuplicateSignature()
			: base(NoticeType.REDUNDANT_NDE_REROLL_DUPLCIATE_SIGNATURE)
		{
		}
	}

	public abstract class ClientOptionBase : ICloneable
	{
		public abstract void PopulateIntoPacket(ServerOption type, SetOptions packet);

		public override bool Equals(object other)
		{
			if (other == null)
			{
				return false;
			}
			if (other.GetType() != GetType())
			{
				return false;
			}
			return true;
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		public object Clone()
		{
			return MemberwiseClone();
		}
	}

	public class ClientOptionInt : ClientOptionBase
	{
		public int OptionValue { get; set; }

		public ClientOptionInt(int val)
		{
			OptionValue = val;
		}

		public override void PopulateIntoPacket(ServerOption type, SetOptions packet)
		{
			PegasusUtil.ClientOption clientOption = new PegasusUtil.ClientOption();
			clientOption.Index = (int)type;
			clientOption.AsInt32 = OptionValue;
			packet.Options.Add(clientOption);
		}

		public override bool Equals(object other)
		{
			if (base.Equals(other) && ((ClientOptionInt)other).OptionValue == OptionValue)
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return OptionValue.GetHashCode();
		}
	}

	public class ClientOptionLong : ClientOptionBase
	{
		public long OptionValue { get; set; }

		public ClientOptionLong(long val)
		{
			OptionValue = val;
		}

		public override void PopulateIntoPacket(ServerOption type, SetOptions packet)
		{
			PegasusUtil.ClientOption clientOption = new PegasusUtil.ClientOption();
			clientOption.Index = (int)type;
			clientOption.AsInt64 = OptionValue;
			packet.Options.Add(clientOption);
		}

		public override bool Equals(object other)
		{
			if (base.Equals(other) && ((ClientOptionLong)other).OptionValue == OptionValue)
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return OptionValue.GetHashCode();
		}
	}

	public class ClientOptionFloat : ClientOptionBase
	{
		public float OptionValue { get; set; }

		public ClientOptionFloat(float val)
		{
			OptionValue = val;
		}

		public override void PopulateIntoPacket(ServerOption type, SetOptions packet)
		{
			PegasusUtil.ClientOption clientOption = new PegasusUtil.ClientOption();
			clientOption.Index = (int)type;
			clientOption.AsFloat = OptionValue;
			packet.Options.Add(clientOption);
		}

		public override bool Equals(object other)
		{
			if (base.Equals(other) && ((ClientOptionFloat)other).OptionValue == OptionValue)
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return OptionValue.GetHashCode();
		}
	}

	public class ClientOptionULong : ClientOptionBase
	{
		public ulong OptionValue { get; set; }

		public ClientOptionULong(ulong val)
		{
			OptionValue = val;
		}

		public override void PopulateIntoPacket(ServerOption type, SetOptions packet)
		{
			PegasusUtil.ClientOption clientOption = new PegasusUtil.ClientOption();
			clientOption.Index = (int)type;
			clientOption.AsUint64 = OptionValue;
			packet.Options.Add(clientOption);
		}

		public override bool Equals(object other)
		{
			if (base.Equals(other) && ((ClientOptionULong)other).OptionValue == OptionValue)
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return OptionValue.GetHashCode();
		}
	}

	public class NetCacheClientOptions
	{
		private DateTime? m_mostRecentDispatchToServer;

		private DateTime? m_currentScheduledDispatchTime;

		private int ClientOptionsUpdateIntervalSeconds
		{
			get
			{
				NetCacheFeatures netObject = Get().GetNetObject<NetCacheFeatures>();
				if (netObject != null && netObject.Misc != null)
				{
					return netObject.Misc.ClientOptionsUpdateIntervalSeconds;
				}
				return 180;
			}
		}

		public Map<ServerOption, ClientOptionBase> ClientState { get; private set; }

		private Map<ServerOption, ClientOptionBase> ServerState { get; set; }

		public NetCacheClientOptions()
		{
			ClientState = new Map<ServerOption, ClientOptionBase>();
			ServerState = new Map<ServerOption, ClientOptionBase>();
		}

		public void UpdateServerState()
		{
			foreach (KeyValuePair<ServerOption, ClientOptionBase> item in ClientState)
			{
				if (item.Value != null)
				{
					ServerState[item.Key] = (ClientOptionBase)item.Value.Clone();
				}
				else
				{
					ServerState[item.Key] = null;
				}
			}
		}

		public void OnUpdateIntervalElasped(object userData)
		{
			m_currentScheduledDispatchTime = null;
			DispatchClientOptionsToServer();
		}

		public void CancelScheduledDispatchToServer()
		{
			Processor.CancelScheduledCallback(OnUpdateIntervalElasped);
			m_currentScheduledDispatchTime = null;
		}

		public void DispatchClientOptionsToServer()
		{
			CancelScheduledDispatchToServer();
			bool flag = false;
			SetOptions setOptions = new SetOptions();
			foreach (KeyValuePair<ServerOption, ClientOptionBase> item in ClientState)
			{
				if (ServerState.TryGetValue(item.Key, out var value))
				{
					if (item.Value != null || value != null)
					{
						if ((item.Value == null && value != null) || (item.Value != null && value == null))
						{
							flag = true;
							break;
						}
						if (!value.Equals(item.Value))
						{
							flag = true;
							break;
						}
					}
					continue;
				}
				flag = true;
				break;
			}
			if (!flag)
			{
				return;
			}
			foreach (KeyValuePair<ServerOption, ClientOptionBase> item2 in ClientState)
			{
				if (item2.Value != null)
				{
					item2.Value.PopulateIntoPacket(item2.Key, setOptions);
				}
			}
			Network.Get().SetClientOptions(setOptions);
			m_mostRecentDispatchToServer = DateTime.UtcNow;
			UpdateServerState();
		}

		public void RemoveInvalidOptions()
		{
			List<ServerOption> list = new List<ServerOption>();
			foreach (KeyValuePair<ServerOption, ClientOptionBase> item in ClientState)
			{
				ServerOption key = item.Key;
				ClientOptionBase value = item.Value;
				Type serverOptionType = Options.Get().GetServerOptionType(key);
				if (value != null)
				{
					Type type = value.GetType();
					if (serverOptionType == typeof(int))
					{
						if (type == typeof(ClientOptionInt))
						{
							continue;
						}
					}
					else if (serverOptionType == typeof(long))
					{
						if (type == typeof(ClientOptionLong))
						{
							continue;
						}
					}
					else if (serverOptionType == typeof(float))
					{
						if (type == typeof(ClientOptionFloat))
						{
							continue;
						}
					}
					else if (serverOptionType == typeof(ulong) && type == typeof(ClientOptionULong))
					{
						continue;
					}
					if (serverOptionType == null)
					{
						Log.Net.Print("NetCacheClientOptions.RemoveInvalidOptions() - Option {0} has type {1}, but value is type {2}. Removing it.", key, serverOptionType, type);
					}
					else
					{
						Log.Net.Print("NetCacheClientOptions.RemoveInvalidOptions() - Option {0} has type {1}, but value is type {2}. Removing it.", EnumUtils.GetString(key), serverOptionType, type);
					}
				}
				list.Add(key);
			}
			foreach (ServerOption item2 in list)
			{
				ClientState.Remove(item2);
				ServerState.Remove(item2);
			}
		}

		public void CheckForDispatchToServer()
		{
			float num = ClientOptionsUpdateIntervalSeconds;
			if (num <= 0f)
			{
				return;
			}
			DateTime utcNow = DateTime.UtcNow;
			bool flag = false;
			bool flag2 = false;
			if (!m_mostRecentDispatchToServer.HasValue)
			{
				flag = true;
			}
			else if (!m_currentScheduledDispatchTime.HasValue)
			{
				TimeSpan timeSpan = utcNow - m_mostRecentDispatchToServer.Value;
				if (timeSpan.TotalSeconds >= (double)num)
				{
					flag = true;
				}
				else
				{
					flag2 = true;
					num -= (float)timeSpan.TotalSeconds;
				}
			}
			if (!flag && !flag2 && m_currentScheduledDispatchTime.HasValue && (m_currentScheduledDispatchTime.Value - utcNow).TotalSeconds > (double)num)
			{
				flag2 = true;
			}
			if (flag || flag2)
			{
				float secondsToWait = (flag ? 0f : num);
				m_currentScheduledDispatchTime = utcNow;
				Processor.CancelScheduledCallback(OnUpdateIntervalElasped);
				Processor.ScheduleCallback(secondsToWait, realTime: true, OnUpdateIntervalElasped);
			}
		}
	}

	public class NetCacheFavoriteHeroes
	{
		public List<(TAG_CLASS, CardDefinition)> FavoriteHeroes { get; set; }

		public NetCacheFavoriteHeroes()
		{
			FavoriteHeroes = new List<(TAG_CLASS, CardDefinition)>();
		}
	}

	public class NetCacheAccountLicenses
	{
		public Map<long, AccountLicenseInfo> AccountLicenses { get; set; }

		public NetCacheAccountLicenses()
		{
			AccountLicenses = new Map<long, AccountLicenseInfo>();
		}
	}

	public class NetCacheBattlegroundsHeroSkins
	{
		public Map<int, HashSet<int>> BattlegroundsFavoriteHeroSkins { get; set; }

		public HashSet<BattlegroundsHeroSkinId> OwnedBattlegroundsSkins { get; }

		public HashSet<BattlegroundsHeroSkinId> UnseenSkinIds { get; }

		public NetCacheBattlegroundsHeroSkins()
		{
			OwnedBattlegroundsSkins = new HashSet<BattlegroundsHeroSkinId>();
			BattlegroundsFavoriteHeroSkins = new Map<int, HashSet<int>>();
			UnseenSkinIds = new HashSet<BattlegroundsHeroSkinId>();
		}
	}

	public class NetCacheBattlegroundsGuideSkins
	{
		public HashSet<BattlegroundsGuideSkinId> Favorites { get; set; }

		public HashSet<BattlegroundsGuideSkinId> Owned { get; }

		public HashSet<BattlegroundsGuideSkinId> Unseen { get; }

		public NetCacheBattlegroundsGuideSkins()
		{
			Owned = new HashSet<BattlegroundsGuideSkinId>();
			Favorites = new HashSet<BattlegroundsGuideSkinId>();
			Unseen = new HashSet<BattlegroundsGuideSkinId>();
		}

		public BattlegroundsGuideSkinId GetRandomFavorite()
		{
			if (Favorites.Count > 0)
			{
				int index = UnityEngine.Random.Range(0, Favorites.Count);
				return Favorites.ToList()[index];
			}
			return BattlegroundsGuideSkinId.DEFAULT;
		}
	}

	public class NetCacheBattlegroundsBoardSkins
	{
		public HashSet<BattlegroundsBoardSkinId> BattlegroundsFavoriteBoardSkins { get; set; }

		public HashSet<BattlegroundsBoardSkinId> OwnedBattlegroundsBoardSkins { get; set; }

		public HashSet<BattlegroundsBoardSkinId> UnseenSkinIds { get; }

		public NetCacheBattlegroundsBoardSkins()
		{
			OwnedBattlegroundsBoardSkins = new HashSet<BattlegroundsBoardSkinId>();
			BattlegroundsFavoriteBoardSkins = new HashSet<BattlegroundsBoardSkinId>();
			UnseenSkinIds = new HashSet<BattlegroundsBoardSkinId>();
		}
	}

	public class NetCacheBattlegroundsFinishers
	{
		public HashSet<BattlegroundsFinisherId> BattlegroundsFavoriteFinishers { get; set; }

		public HashSet<BattlegroundsFinisherId> OwnedBattlegroundsFinishers { get; set; }

		public HashSet<BattlegroundsFinisherId> UnseenSkinIds { get; }

		public NetCacheBattlegroundsFinishers()
		{
			OwnedBattlegroundsFinishers = new HashSet<BattlegroundsFinisherId>();
			BattlegroundsFavoriteFinishers = new HashSet<BattlegroundsFinisherId>();
			UnseenSkinIds = new HashSet<BattlegroundsFinisherId>();
		}
	}

	public class NetCacheBattlegroundsEmotes
	{
		private Hearthstone.BattlegroundsEmoteLoadout _currentLoadout = new Hearthstone.BattlegroundsEmoteLoadout();

		public HashSet<BattlegroundsEmoteId> OwnedBattlegroundsEmotes { get; set; }

		public HashSet<BattlegroundsEmoteId> UnseenEmoteIds { get; }

		public Hearthstone.BattlegroundsEmoteLoadout CurrentLoadout
		{
			get
			{
				return new Hearthstone.BattlegroundsEmoteLoadout(_currentLoadout);
			}
			set
			{
				_currentLoadout = new Hearthstone.BattlegroundsEmoteLoadout(value);
			}
		}

		public NetCacheBattlegroundsEmotes()
		{
			OwnedBattlegroundsEmotes = new HashSet<BattlegroundsEmoteId>();
			UnseenEmoteIds = new HashSet<BattlegroundsEmoteId>();
			CurrentLoadout = new Hearthstone.BattlegroundsEmoteLoadout();
		}
	}

	public class PetInfo
	{
		public int PetId { get; set; }

		public int CurrentLevel { get; set; }

		public int CurrentXp { get; set; }

		public bool IsHsFavorite { get; set; }

		public bool IsBgFavorite { get; set; }

		public HashSet<int> VariantIDs { get; }

		public PetInfo()
		{
			VariantIDs = new HashSet<int>();
		}

		public static PetInfo FromProto(Pet pet)
		{
			return new PetInfo
			{
				PetId = pet.PetId,
				CurrentLevel = pet.CurrentLevel,
				CurrentXp = pet.CurrentXp,
				IsHsFavorite = pet.IsHsFavorite,
				IsBgFavorite = pet.IsBgFavorite
			};
		}
	}

	public class PetVariantInfo
	{
		public int PetVariantId { get; set; }

		public bool IsHsFavorite { get; set; }

		public bool IsBgFavorite { get; set; }

		public static PetVariantInfo FromProto(PetVariant petVariant)
		{
			return new PetVariantInfo
			{
				PetVariantId = petVariant.PetVariantId,
				IsHsFavorite = petVariant.IsHsFavorite,
				IsBgFavorite = petVariant.IsBgFavorite
			};
		}
	}

	public class NetCachePets
	{
		public Dictionary<int, PetInfo> Pets { get; }

		public Dictionary<int, PetVariantInfo> PetVariants { get; }

		public NetCachePets()
		{
			Pets = new Dictionary<int, PetInfo>();
			PetVariants = new Dictionary<int, PetVariantInfo>();
		}
	}

	public class NetCacheLettuceMap
	{
		public PegasusLettuce.LettuceMap Map { get; set; }

		public NetCacheLettuceMap()
		{
			Map = null;
		}
	}

	public delegate void ErrorCallback(ErrorInfo info);

	public enum ErrorCode
	{
		NONE,
		TIMEOUT,
		SERVER
	}

	public class ErrorInfo
	{
		[CompilerGenerated]
		private uint <ServerError>k__BackingField;

		public ErrorCode Error { get; set; }

		public uint ServerError
		{
			[CompilerGenerated]
			set
			{
				<ServerError>k__BackingField = value;
			}
		}

		public RequestFunc RequestingFunction { get; set; }

		public Map<Type, Request> RequestedTypes { get; set; }

		public string RequestStackTrace { get; set; }
	}

	public delegate void NetCacheCallback();

	public delegate void RequestFunc(NetCacheCallback callback, ErrorCallback errorCallback);

	public enum RequestResult
	{
		UNKNOWN,
		PENDING,
		IN_PROCESS,
		GENERIC_COMPLETE,
		DATA_COMPLETE,
		ERROR,
		MIGRATION_REQUIRED
	}

	public class Request
	{
		public Type m_type;

		public bool m_reload;

		public RequestResult m_result;

		public Request(Type rt, bool rl = false)
		{
			m_type = rt;
			m_reload = rl;
			m_result = RequestResult.UNKNOWN;
		}
	}

	private class NetCacheBatchRequest
	{
		public Map<Type, Request> m_requests = new Map<Type, Request>();

		public NetCacheCallback m_callback;

		public ErrorCallback m_errorCallback;

		public bool m_canTimeout = true;

		public float m_timeAdded = Time.realtimeSinceStartup;

		public RequestFunc m_requestFunc;

		public string m_requestStackTrace;

		public NetCacheBatchRequest(NetCacheCallback reply, ErrorCallback errorCallback, RequestFunc requestFunc)
		{
			m_callback = reply;
			m_errorCallback = errorCallback;
			m_requestFunc = requestFunc;
			m_requestStackTrace = Environment.StackTrace;
		}

		public void AddRequests(List<Request> requests)
		{
			foreach (Request request in requests)
			{
				AddRequest(request);
			}
		}

		public void AddRequest(Request r)
		{
			if (!m_requests.ContainsKey(r.m_type))
			{
				m_requests.Add(r.m_type, r);
			}
		}
	}

	private static readonly Map<Type, GetAccountInfo.Request> m_getAccountInfoTypeMap = new Map<Type, GetAccountInfo.Request>
	{
		{
			typeof(NetCacheDecks),
			GetAccountInfo.Request.DECK_LIST
		},
		{
			typeof(NetCacheMedalInfo),
			GetAccountInfo.Request.MEDAL_INFO
		},
		{
			typeof(NetCacheCardBacks),
			GetAccountInfo.Request.CARD_BACKS
		},
		{
			typeof(NetCachePlayerRecords),
			GetAccountInfo.Request.PLAYER_RECORD
		},
		{
			typeof(NetCacheGamesPlayed),
			GetAccountInfo.Request.GAMES_PLAYED
		},
		{
			typeof(NetCacheProfileProgress),
			GetAccountInfo.Request.CAMPAIGN_INFO
		},
		{
			typeof(NetCacheCardValues),
			GetAccountInfo.Request.CARD_VALUES
		},
		{
			typeof(NetCacheFeatures),
			GetAccountInfo.Request.FEATURES
		},
		{
			typeof(NetCacheRewardProgress),
			GetAccountInfo.Request.REWARD_PROGRESS
		},
		{
			typeof(NetCacheHeroLevels),
			GetAccountInfo.Request.HERO_XP
		},
		{
			typeof(NetCacheFavoriteHeroes),
			GetAccountInfo.Request.FAVORITE_HEROES
		},
		{
			typeof(NetCacheAccountLicenses),
			GetAccountInfo.Request.ACCOUNT_LICENSES
		},
		{
			typeof(NetCacheCoins),
			GetAccountInfo.Request.COINS
		},
		{
			typeof(NetCacheBattlegroundsHeroSkins),
			GetAccountInfo.Request.BATTLEGROUNDS_SKINS
		},
		{
			typeof(NetCacheBattlegroundsGuideSkins),
			GetAccountInfo.Request.BATTLEGROUNDS_GUIDE_SKINS
		},
		{
			typeof(NetCacheBattlegroundsBoardSkins),
			GetAccountInfo.Request.BATTLEGROUNDS_BOARD_SKINS
		},
		{
			typeof(NetCacheBattlegroundsFinishers),
			GetAccountInfo.Request.BATTLEGROUNDS_FINISHERS
		},
		{
			typeof(NetCacheBattlegroundsEmotes),
			GetAccountInfo.Request.BATTLEGROUNDS_EMOTES
		},
		{
			typeof(NetCachePets),
			GetAccountInfo.Request.PETS
		}
	};

	private static readonly Map<Type, int> m_genericRequestTypeMap = new Map<Type, int> { 
	{
		typeof(ClientStaticAssetsResponse),
		340
	} };

	private static readonly List<Type> m_ServerInitiatedAccountInfoTypes = new List<Type>
	{
		typeof(NetCacheCollection),
		typeof(NetCacheClientOptions),
		typeof(NetCacheArcaneDustBalance),
		typeof(NetCacheGoldBalance),
		typeof(NetCacheProfileNotices),
		typeof(NetCacheBoosters),
		typeof(NetCacheDecks),
		typeof(NetCacheRenownBalance),
		typeof(NetCacheBattlegroundsTokenBalance)
	};

	private static readonly Map<GetAccountInfo.Request, Type> m_requestTypeMap = GetInvertTypeMap();

	private Map<Type, object> m_netCache = new Map<Type, object>();

	private NetCacheHeroLevels m_prevHeroLevels;

	private NetCacheMedalInfo m_previousMedalInfo;

	private List<DelNewNoticesListener> m_newNoticesListeners = new List<DelNewNoticesListener>();

	private List<DelGoldBalanceListener> m_goldBalanceListeners = new List<DelGoldBalanceListener>();

	private List<DelRenownBalanceListener> m_renownBalanceListeners = new List<DelRenownBalanceListener>();

	private List<DelBattlegroundsTokenBalanceListener> m_battlegroundsTokenBalanceListeners = new List<DelBattlegroundsTokenBalanceListener>();

	private Map<Type, HashSet<Action>> m_updatedListeners = new Map<Type, HashSet<Action>>();

	private Map<Type, int> m_changeRequests = new Map<Type, int>();

	private bool m_receivedInitialClientState;

	private HashSet<long> m_ackedNotices = new HashSet<long>();

	private List<ProfileNotice> m_queuedProfileNotices = new List<ProfileNotice>();

	private bool m_receivedInitialProfileNotices;

	private long m_currencyVersion;

	private long m_initialCollectionVersion;

	public string RedPointAppSecret;

	private HashSet<long> m_expectedCardModifications = new HashSet<long>();

	private HashSet<long> m_handledCardModifications = new HashSet<long>();

	[CompilerGenerated]
	private DelBattlegroundsEmoteLoadoutChangedListener BattlegroundsEmoteLoadoutChangedListener;

	[CompilerGenerated]
	private DelOwnedPetsChanged OwnedPetsChanged;

	private long m_lastForceCheckedSeason;

	private List<NetCacheBatchRequest> m_cacheRequests = new List<NetCacheBatchRequest>();

	private List<NetCacheBatchRequest> m_cacheRequestScratchList = new List<NetCacheBatchRequest>();

	private List<Type> m_inTransitRequests = new List<Type>();

	private static bool m_fatalErrorCodeSet = false;

	public bool HasReceivedInitialClientState => m_receivedInitialClientState;

	public InitialClientState InitialClientState { get; set; }

	public event DelFavoriteCardBackChangedListener FavoriteCardBackChanged;

	public event DelFavoriteBattlegroundsHeroSkinChangedListener FavoriteBattlegroundsHeroSkinChanged;

	public event DelFavoriteBattlegroundsGuideSkinChangedListener FavoriteBattlegroundsGuideSkinChanged;

	public event DelFavoriteBattlegroundsBoardSkinChangedListener FavoriteBattlegroundsBoardSkinChanged;

	public event DelFavoriteBattlegroundsFinisherChangedListener FavoriteBattlegroundsFinisherChanged;

	public event DelFavoriteCoinChangedListener FavoriteCoinChanged;

	public event DelOwnedBattlegroundsSkinsChanged OwnedBattlegroundsSkinsChanged;

	public event DelFavoritePetChangedListener FavoritePetChanged;

	public event DelFavoritePetVariantChangedListener FavoritePetVariantChanged;

	public event DelCardBacksChanged CardBacksChanged;

	public event DelPetsXpLevelChanged PetsXpLevelChanged;

	private static Map<GetAccountInfo.Request, Type> GetInvertTypeMap()
	{
		Map<GetAccountInfo.Request, Type> map = new Map<GetAccountInfo.Request, Type>();
		foreach (KeyValuePair<Type, GetAccountInfo.Request> item in m_getAccountInfoTypeMap)
		{
			map[item.Value] = item.Key;
		}
		return map;
	}

	public IEnumerator<IAsyncJobResult> Initialize(ServiceLocator serviceLocator)
	{
		serviceLocator.Get<Network>().RegisterThrottledPacketListener(OnPacketThrottled);
		RegisterNetCacheHandlers();
		yield break;
	}

	public Type[] GetDependencies()
	{
		return new Type[1] { typeof(Network) };
	}

	public void Shutdown()
	{
	}

	public static NetCache Get()
	{
		return ServiceManager.Get<NetCache>();
	}

	public T GetNetObject<T>()
	{
		Type typeFromHandle = typeof(T);
		object value = GetTestData(typeFromHandle);
		if (value != null)
		{
			return (T)value;
		}
		if (m_netCache.TryGetValue(typeof(T), out value) && value is T)
		{
			return (T)value;
		}
		return default(T);
	}

	public bool IsNetObjectAvailable<T>()
	{
		return GetNetObject<T>() != null;
	}

	private object GetTestData(Type type)
	{
		if (type == typeof(NetCacheBoosters) && GameUtils.IsFakePackOpeningEnabled())
		{
			NetCacheBoosters netCacheBoosters = new NetCacheBoosters();
			int fakePackCount = GameUtils.GetFakePackCount();
			BoosterStack item = new BoosterStack
			{
				Id = 1,
				Count = fakePackCount
			};
			netCacheBoosters.BoosterStacks.Add(item);
			return netCacheBoosters;
		}
		return null;
	}

	public void UnloadNetObject<T>()
	{
		Type typeFromHandle = typeof(T);
		m_netCache[typeFromHandle] = null;
	}

	public void ReloadNetObject<T>()
	{
		NetCacheReload_Internal(null, typeof(T));
	}

	public void RefreshNetObject<T>()
	{
		RequestNetCacheObject(typeof(T));
	}

	public long GetArcaneDustBalance()
	{
		NetCacheArcaneDustBalance netObject = GetNetObject<NetCacheArcaneDustBalance>();
		if (netObject == null)
		{
			return 0L;
		}
		if (CraftingManager.IsInitialized)
		{
			return netObject.Balance + CraftingManager.Get().GetUnCommitedArcaneDustChanges();
		}
		return netObject.Balance;
	}

	public long GetGoldBalance()
	{
		return GetNetObject<NetCacheGoldBalance>()?.GetTotal() ?? 0;
	}

	public long GetRenownBalance()
	{
		return GetNetObject<NetCacheRenownBalance>()?.Balance ?? 0;
	}

	public long GetBattlegroundsTokenBalance()
	{
		return GetNetObject<NetCacheBattlegroundsTokenBalance>()?.Balance ?? 0;
	}

	public int GetArenaTicketBalance()
	{
		return GetNetObject<NetPlayerArenaTickets>()?.Balance ?? 0;
	}

	private bool GetOption<T>(ServerOption type, out T ret) where T : ClientOptionBase
	{
		ret = null;
		NetCacheClientOptions netObject = Get().GetNetObject<NetCacheClientOptions>();
		if (!ClientOptionExists(type))
		{
			return false;
		}
		if (!(netObject.ClientState[type] is T val))
		{
			return false;
		}
		ret = val;
		return true;
	}

	public int GetIntOption(ServerOption type)
	{
		ClientOptionInt ret = null;
		if (!GetOption<ClientOptionInt>(type, out ret))
		{
			return 0;
		}
		return ret.OptionValue;
	}

	public bool GetIntOption(ServerOption type, out int ret)
	{
		ret = 0;
		ClientOptionInt ret2 = null;
		if (!GetOption<ClientOptionInt>(type, out ret2))
		{
			return false;
		}
		ret = ret2.OptionValue;
		return true;
	}

	public long GetLongOption(ServerOption type)
	{
		ClientOptionLong ret = null;
		if (!GetOption<ClientOptionLong>(type, out ret))
		{
			return 0L;
		}
		return ret.OptionValue;
	}

	public bool GetLongOption(ServerOption type, out long ret)
	{
		ret = 0L;
		ClientOptionLong ret2 = null;
		if (!GetOption<ClientOptionLong>(type, out ret2))
		{
			return false;
		}
		ret = ret2.OptionValue;
		return true;
	}

	public float GetFloatOption(ServerOption type)
	{
		ClientOptionFloat ret = null;
		if (!GetOption<ClientOptionFloat>(type, out ret))
		{
			return 0f;
		}
		return ret.OptionValue;
	}

	public bool GetFloatOption(ServerOption type, out float ret)
	{
		ret = 0f;
		ClientOptionFloat ret2 = null;
		if (!GetOption<ClientOptionFloat>(type, out ret2))
		{
			return false;
		}
		ret = ret2.OptionValue;
		return true;
	}

	public ulong GetULongOption(ServerOption type)
	{
		ClientOptionULong ret = null;
		if (!GetOption<ClientOptionULong>(type, out ret))
		{
			return 0uL;
		}
		return ret.OptionValue;
	}

	public bool GetULongOption(ServerOption type, out ulong ret)
	{
		ret = 0uL;
		ClientOptionULong ret2 = null;
		if (!GetOption<ClientOptionULong>(type, out ret2))
		{
			return false;
		}
		ret = ret2.OptionValue;
		return true;
	}

	public void RegisterUpdatedListener(Type type, Action listener)
	{
		if (listener != null)
		{
			if (!m_updatedListeners.TryGetValue(type, out var value))
			{
				value = new HashSet<Action>();
				m_updatedListeners[type] = value;
			}
			m_updatedListeners[type].Add(listener);
		}
	}

	public void RemoveUpdatedListener(Type type, Action listener)
	{
		if (listener != null && m_updatedListeners.TryGetValue(type, out var value))
		{
			value.Remove(listener);
		}
	}

	public void RegisterNewNoticesListener(DelNewNoticesListener listener)
	{
		if (!m_newNoticesListeners.Contains(listener))
		{
			m_newNoticesListeners.Add(listener);
		}
	}

	public void RemoveNewNoticesListener(DelNewNoticesListener listener)
	{
		m_newNoticesListeners.Remove(listener);
	}

	public bool RemoveNotice(long ID)
	{
		if (!(m_netCache[typeof(NetCacheProfileNotices)] is NetCacheProfileNotices netCacheProfileNotices))
		{
			Debug.LogWarning($"NetCache.RemoveNotice({ID}) - profileNotices is null");
			return false;
		}
		if (netCacheProfileNotices.Notices == null)
		{
			Debug.LogWarning($"NetCache.RemoveNotice({ID}) - profileNotices.Notices is null");
			return false;
		}
		ProfileNotice profileNotice = netCacheProfileNotices.Notices.Find((ProfileNotice obj) => obj.NoticeID == ID);
		if (profileNotice == null)
		{
			return false;
		}
		if (!netCacheProfileNotices.Notices.Contains(profileNotice))
		{
			Debug.LogWarning($"NetCache.RemoveNotice({ID}) - profileNotices.Notices does not contain notice to be removed");
			return false;
		}
		netCacheProfileNotices.Notices.Remove(profileNotice);
		m_ackedNotices.Add(profileNotice.NoticeID);
		return true;
	}

	public void NetCacheChanged<T>()
	{
		Type typeFromHandle = typeof(T);
		int value = 0;
		m_changeRequests.TryGetValue(typeFromHandle, out value);
		value++;
		m_changeRequests[typeFromHandle] = value;
		if (value <= 1)
		{
			while (m_changeRequests[typeFromHandle] > 0)
			{
				NetCacheChangedImpl<T>();
				m_changeRequests[typeFromHandle] -= 1;
			}
		}
	}

	private void NetCacheChangedImpl<T>()
	{
		NetCacheBatchRequest[] array = m_cacheRequests.ToArray();
		foreach (NetCacheBatchRequest netCacheBatchRequest in array)
		{
			foreach (KeyValuePair<Type, Request> request in netCacheBatchRequest.m_requests)
			{
				if (!(request.Key != typeof(T)))
				{
					NetCacheCheckRequest(netCacheBatchRequest);
					break;
				}
			}
		}
	}

	public void CheckSeasonForRoll()
	{
		if (GetNetObject<NetCacheProfileNotices>() == null)
		{
			return;
		}
		NetCacheRewardProgress netObject = GetNetObject<NetCacheRewardProgress>();
		if (netObject != null)
		{
			DateTime utcNow = DateTime.UtcNow;
			DateTime dateTime = DateTime.FromFileTimeUtc(netObject.SeasonEndDate);
			if (!(dateTime >= utcNow) && m_lastForceCheckedSeason != netObject.Season)
			{
				m_lastForceCheckedSeason = netObject.Season;
				Log.Net.Print("NetCache.CheckSeasonForRoll oldSeason = {0} season end = {1} utc now = {2}", m_lastForceCheckedSeason, dateTime, utcNow);
			}
		}
	}

	public void RegisterGoldBalanceListener(DelGoldBalanceListener listener)
	{
		if (!m_goldBalanceListeners.Contains(listener))
		{
			m_goldBalanceListeners.Add(listener);
		}
	}

	public void RemoveGoldBalanceListener(DelGoldBalanceListener listener)
	{
		m_goldBalanceListeners.Remove(listener);
	}

	public void RegisterRenownBalanceListener(DelRenownBalanceListener listener)
	{
		if (!m_renownBalanceListeners.Contains(listener))
		{
			m_renownBalanceListeners.Add(listener);
		}
	}

	public void RemoveRenownBalanceListener(DelRenownBalanceListener listener)
	{
		m_renownBalanceListeners.Remove(listener);
	}

	public void RegisterBattlegroundsTokenBalanceListener(DelBattlegroundsTokenBalanceListener listener)
	{
		if (!m_battlegroundsTokenBalanceListeners.Contains(listener))
		{
			m_battlegroundsTokenBalanceListeners.Add(listener);
		}
	}

	public void RemoveBattlegroundsTokenBalanceListener(DelBattlegroundsTokenBalanceListener listener)
	{
		m_battlegroundsTokenBalanceListeners.Remove(listener);
	}

	public static void DefaultErrorHandler(ErrorInfo info)
	{
		if (info.Error == ErrorCode.TIMEOUT)
		{
			BreakingNews breakingNews = ServiceManager.Get<BreakingNews>();
			if (breakingNews != null && breakingNews.ShouldShowForCurrentPlatform)
			{
				string error = "GLOBAL_ERROR_NETWORK_UTIL_TIMEOUT";
				Network.Get().ShowBreakingNewsOrError(error);
			}
			else
			{
				ShowError(info, "GLOBAL_ERROR_NETWORK_UTIL_TIMEOUT");
			}
		}
		else
		{
			ShowError(info, "GLOBAL_ERROR_NETWORK_GENERIC");
		}
	}

	public static void ShowError(ErrorInfo info, string localizationKey, params object[] localizationArgs)
	{
		Error.AddFatal(FatalErrorReason.NET_CACHE, localizationKey, localizationArgs);
		Debug.LogError(GetInternalErrorMessage(info));
	}

	public static string GetInternalErrorMessage(ErrorInfo info, bool includeStackTrace = true)
	{
		Map<Type, object> netCache = Get().m_netCache;
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendFormat("NetCache Error: {0}", info.Error);
		stringBuilder.AppendFormat("\nFrom: {0}", info.RequestingFunction.Method.Name);
		stringBuilder.AppendFormat("\nRequested Data ({0}):", info.RequestedTypes.Count);
		foreach (KeyValuePair<Type, Request> requestedType in info.RequestedTypes)
		{
			object value = null;
			netCache.TryGetValue(requestedType.Key, out value);
			if (value == null)
			{
				stringBuilder.AppendFormat("\n[{0}] MISSING", requestedType.Key);
			}
			else
			{
				stringBuilder.AppendFormat("\n[{0}]", requestedType.Key);
			}
		}
		if (includeStackTrace)
		{
			stringBuilder.AppendFormat("\nStack Trace:\n{0}", info.RequestStackTrace);
		}
		return stringBuilder.ToString();
	}

	private void NetCacheMakeBatchRequest(NetCacheBatchRequest batchRequest)
	{
		List<GetAccountInfo.Request> list = new List<GetAccountInfo.Request>();
		List<GenericRequest> list2 = null;
		foreach (KeyValuePair<Type, Request> request in batchRequest.m_requests)
		{
			Request value = request.Value;
			if (value == null)
			{
				Debug.LogError($"NetUseBatchRequest Null request for {value.m_type.Name}...SKIP");
				continue;
			}
			if (m_ServerInitiatedAccountInfoTypes.Contains(value.m_type))
			{
				if (value.m_reload)
				{
					Log.All.PrintWarning("Attempting to reload server-initiated NetCache request {0}. This is not valid - the server sends this data when it changes!", value.m_type.FullName);
				}
				continue;
			}
			if (value.m_reload)
			{
				m_netCache[value.m_type] = null;
			}
			if ((m_netCache.ContainsKey(value.m_type) && m_netCache[value.m_type] != null) || m_inTransitRequests.Contains(value.m_type))
			{
				continue;
			}
			value.m_result = RequestResult.PENDING;
			m_inTransitRequests.Add(value.m_type);
			int value3;
			if (m_getAccountInfoTypeMap.TryGetValue(value.m_type, out var value2))
			{
				list.Add(value2);
			}
			else if (m_genericRequestTypeMap.TryGetValue(value.m_type, out value3))
			{
				if (list2 == null)
				{
					list2 = new List<GenericRequest>();
				}
				GenericRequest genericRequest = new GenericRequest();
				genericRequest.RequestId = value3;
				list2.Add(genericRequest);
			}
			else
			{
				Log.Net.Print("NetCache: Unable to make request for type={0}", value.m_type.FullName);
			}
		}
		if (list.Count > 0 || list2 != null)
		{
			Network.Get().RequestNetCacheObjectList(list, list2);
		}
		if (m_cacheRequests.FindIndex((NetCacheBatchRequest o) => o.m_callback != null && o.m_callback == batchRequest.m_callback) >= 0)
		{
			Log.Net.PrintError("NetCache: detected multiple registrations for same callback! {0}.{1}", batchRequest.m_callback.Target.GetType().Name, batchRequest.m_callback.Method.Name);
		}
		m_cacheRequests.Add(batchRequest);
		NetCacheCheckRequest(batchRequest);
	}

	private void NetCacheUse_Internal(NetCacheBatchRequest request, Type type)
	{
		if (request != null && request.m_requests.ContainsKey(type))
		{
			Log.Net.Print($"NetCache ...SKIP {type.Name}");
			return;
		}
		if (m_netCache.ContainsKey(type) && m_netCache[type] != null)
		{
			Log.Net.Print($"NetCache ...USE {type.Name}");
			return;
		}
		Log.Net.Print($"NetCache <<<GET {type.Name}");
		RequestNetCacheObject(type);
	}

	private void RequestNetCacheObject(Type type)
	{
		if (!m_inTransitRequests.Contains(type))
		{
			m_inTransitRequests.Add(type);
			Network.Get().RequestNetCacheObject(m_getAccountInfoTypeMap[type]);
		}
	}

	private void NetCacheReload_Internal(NetCacheBatchRequest request, Type type)
	{
		m_netCache[type] = null;
		if (type == typeof(NetCacheProfileNotices))
		{
			Debug.LogError("NetCacheReload_Internal - tried to issue request with type NetCacheProfileNotices - this is no longer allowed!");
		}
		else
		{
			NetCacheUse_Internal(request, type);
		}
	}

	private void NetCacheCheckRequest(NetCacheBatchRequest request)
	{
		foreach (KeyValuePair<Type, Request> request2 in request.m_requests)
		{
			if (!m_netCache.ContainsKey(request2.Key) || m_netCache[request2.Key] == null)
			{
				return;
			}
		}
		request.m_canTimeout = false;
		if (request.m_callback != null)
		{
			request.m_callback();
		}
	}

	private void UpdateRequestNeedState(Type type, RequestResult result)
	{
		foreach (NetCacheBatchRequest cacheRequest in m_cacheRequests)
		{
			if (cacheRequest.m_requests.ContainsKey(type))
			{
				cacheRequest.m_requests[type].m_result = result;
			}
		}
	}

	private void OnNetCacheObjReceived<T>(T netCacheObject)
	{
		Type typeFromHandle = typeof(T);
		Log.Net.Print($"OnNetCacheObjReceived SAVE --> {typeFromHandle.Name}");
		UpdateRequestNeedState(typeFromHandle, RequestResult.DATA_COMPLETE);
		m_netCache[typeFromHandle] = netCacheObject;
		m_inTransitRequests.Remove(typeFromHandle);
		NetCacheChanged<T>();
		if (m_updatedListeners.TryGetValue(typeFromHandle, out var value))
		{
			Action[] array = value.ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				array[i]();
			}
		}
	}

	public void Clear()
	{
		Log.Net.PrintDebug("Clearing NetCache");
		m_netCache.Clear();
		m_prevHeroLevels = null;
		m_previousMedalInfo = null;
		m_changeRequests.Clear();
		m_cacheRequests.Clear();
		m_inTransitRequests.Clear();
		m_receivedInitialClientState = false;
		m_ackedNotices.Clear();
		m_queuedProfileNotices.Clear();
		m_receivedInitialProfileNotices = false;
		m_currencyVersion = 0L;
		m_initialCollectionVersion = 0L;
		m_expectedCardModifications.Clear();
		m_handledCardModifications.Clear();
		if (HearthstoneApplication.IsInternal() && ServiceManager.TryGet<SceneDebugger>(out var service))
		{
			service.SetPlayerId(null);
		}
	}

	public void ClearForNewAuroraConnection()
	{
		m_cacheRequests.Clear();
		m_inTransitRequests.Clear();
		m_receivedInitialClientState = false;
	}

	public void UnregisterNetCacheHandler(NetCacheCallback handler)
	{
		m_cacheRequests.RemoveAll((NetCacheBatchRequest o) => o.m_callback == handler);
	}

	public void Update()
	{
		if (!Network.IsRunning())
		{
			return;
		}
		m_cacheRequestScratchList.Clear();
		m_cacheRequestScratchList.AddRange(m_cacheRequests);
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		foreach (NetCacheBatchRequest cacheRequestScratch in m_cacheRequestScratchList)
		{
			if (!cacheRequestScratch.m_canTimeout || realtimeSinceStartup - cacheRequestScratch.m_timeAdded < Network.GetMaxDeferredWait() || Network.Get().HaveUnhandledPackets())
			{
				continue;
			}
			cacheRequestScratch.m_canTimeout = false;
			if (m_fatalErrorCodeSet)
			{
				continue;
			}
			ErrorInfo errorInfo = new ErrorInfo();
			errorInfo.Error = ErrorCode.TIMEOUT;
			errorInfo.RequestingFunction = cacheRequestScratch.m_requestFunc;
			errorInfo.RequestedTypes = new Map<Type, Request>(cacheRequestScratch.m_requests);
			errorInfo.RequestStackTrace = cacheRequestScratch.m_requestStackTrace;
			string text = "CT";
			int num = 0;
			foreach (KeyValuePair<Type, Request> request in cacheRequestScratch.m_requests)
			{
				RequestResult result = request.Value.m_result;
				if ((uint)(result - 3) > 1u)
				{
					string[] array = request.Value.m_type.ToString().Split('+');
					if (array.GetLength(0) != 0)
					{
						string text2 = array[array.GetLength(0) - 1];
						string[] obj = new string[5] { text, ";", text2, "=", null };
						int result2 = (int)request.Value.m_result;
						obj[4] = result2.ToString();
						text = string.Concat(obj);
						num++;
					}
				}
				if (num >= 3)
				{
					break;
				}
			}
			FatalErrorMgr.Get().SetErrorCode("HS", text);
			m_fatalErrorCodeSet = true;
			cacheRequestScratch.m_errorCallback(errorInfo);
		}
		CheckSeasonForRoll();
	}

	private void OnGenericResponse()
	{
		Network.GenericResponse genericResponse = Network.Get().GetGenericResponse();
		if (genericResponse == null)
		{
			Debug.LogError($"NetCache - GenericResponse parse error");
		}
		else
		{
			if ((long)genericResponse.RequestId != 201)
			{
				return;
			}
			if (!m_requestTypeMap.TryGetValue((GetAccountInfo.Request)genericResponse.RequestSubId, out var value))
			{
				Debug.LogError($"NetCache - Ignoring unexpected requestId={genericResponse.RequestId}:{genericResponse.RequestSubId}");
				return;
			}
			NetCacheBatchRequest[] array = m_cacheRequests.ToArray();
			foreach (NetCacheBatchRequest netCacheBatchRequest in array)
			{
				if (!netCacheBatchRequest.m_requests.ContainsKey(value))
				{
					continue;
				}
				switch (genericResponse.ResultCode)
				{
				case Network.GenericResponse.Result.RESULT_REQUEST_IN_PROCESS:
					if (RequestResult.PENDING == netCacheBatchRequest.m_requests[value].m_result)
					{
						netCacheBatchRequest.m_requests[value].m_result = RequestResult.IN_PROCESS;
					}
					continue;
				case Network.GenericResponse.Result.RESULT_REQUEST_COMPLETE:
					netCacheBatchRequest.m_requests[value].m_result = RequestResult.GENERIC_COMPLETE;
					Debug.LogWarning($"GenericResponse Success for requestId={genericResponse.RequestId}:{genericResponse.RequestSubId}");
					continue;
				case Network.GenericResponse.Result.RESULT_DATA_MIGRATION_REQUIRED:
					netCacheBatchRequest.m_requests[value].m_result = RequestResult.MIGRATION_REQUIRED;
					Debug.LogWarning($"GenericResponse player migration required code={(int)genericResponse.ResultCode} {genericResponse.ResultCode.ToString()} for requestId={genericResponse.RequestId}:{genericResponse.RequestSubId}");
					continue;
				}
				Debug.LogError($"Unhandled failure code={(int)genericResponse.ResultCode} {genericResponse.ResultCode.ToString()} for requestId={genericResponse.RequestId}:{genericResponse.RequestSubId}");
				netCacheBatchRequest.m_requests[value].m_result = RequestResult.ERROR;
				ErrorInfo errorInfo = new ErrorInfo();
				errorInfo.Error = ErrorCode.SERVER;
				errorInfo.ServerError = (uint)genericResponse.ResultCode;
				errorInfo.RequestingFunction = netCacheBatchRequest.m_requestFunc;
				errorInfo.RequestedTypes = new Map<Type, Request>(netCacheBatchRequest.m_requests);
				errorInfo.RequestStackTrace = netCacheBatchRequest.m_requestStackTrace;
				FatalErrorMgr.Get().SetErrorCode("HS", "CG" + genericResponse.ResultCode, genericResponse.RequestId.ToString(), genericResponse.RequestSubId.ToString());
				netCacheBatchRequest.m_errorCallback(errorInfo);
			}
		}
	}

	private void OnDBAction()
	{
		Network.DBAction dbAction = Network.Get().GetDbAction();
		if (Network.DBAction.ResultType.SUCCESS != dbAction.Result)
		{
			Debug.LogError($"Unhandled dbAction {dbAction.Action} with error {dbAction.Result}");
		}
	}

	private void OnInitialClientState()
	{
		InitialClientState initialClientState = Network.Get().GetInitialClientState();
		if (initialClientState == null)
		{
			return;
		}
		m_receivedInitialClientState = true;
		if (initialClientState.HasGuardianVars)
		{
			OnGuardianVars(initialClientState.GuardianVars);
		}
		if (initialClientState.HasPlayerProfileProgress)
		{
			NetCacheProfileProgress netCacheObject = new NetCacheProfileProgress
			{
				CampaignProgress = (TutorialProgress)initialClientState.PlayerProfileProgress.Progress,
				BestForgeWins = initialClientState.PlayerProfileProgress.BestForge,
				LastForgeDate = (initialClientState.PlayerProfileProgress.HasLastForge ? TimeUtils.PegDateToFileTimeUtc(initialClientState.PlayerProfileProgress.LastForge) : 0),
				TutorialComplete = initialClientState.PlayerProfileProgress.TutorialComplete
			};
			OnNetCacheObjReceived(netCacheObject);
		}
		if (initialClientState.GameSaveData != null)
		{
			GameSaveDataManager.Get().ApplyGameSaveDataFromInitialClientState();
		}
		if (initialClientState.SpecialEventTiming.Count > 0)
		{
			long devTimeOffsetSeconds = (initialClientState.HasDevTimeOffsetSeconds ? initialClientState.DevTimeOffsetSeconds : 0);
			EventTimingManager.Get().InitEventTimingsFromServer(devTimeOffsetSeconds, initialClientState.SpecialEventTiming);
		}
		if (initialClientState.HasClientOptions)
		{
			OnClientOptions(initialClientState.ClientOptions);
		}
		OfflineDataCache.OfflineData data = OfflineDataCache.ReadOfflineDataFromFile();
		if (initialClientState.HasCollection)
		{
			OnCollection(ref data, initialClientState.Collection);
		}
		else
		{
			OnCollection(ref data, data.Collection);
		}
		if (initialClientState.HasAchievements)
		{
			AchieveManager.Get().OnInitialAchievements(initialClientState.Achievements);
		}
		if (initialClientState.HasNotices)
		{
			OnInitialClientState_ProfileNotices(initialClientState.Notices);
		}
		if (initialClientState.HasGameCurrencyStates)
		{
			OnCurrencyState(initialClientState.GameCurrencyStates);
		}
		if (initialClientState.HasBoosters)
		{
			OnBoosters(initialClientState.Boosters);
		}
		if (initialClientState.HasPlayerDraftTickets)
		{
			OnPlayerDraftTickets(initialClientState.PlayerDraftTickets);
		}
		foreach (TavernBrawlInfo tavernBrawls in initialClientState.TavernBrawlsList)
		{
			PegasusPacket packet = new PegasusPacket(316, 0, tavernBrawls);
			Network.Get().SimulateReceivedPacketFromServer(packet);
		}
		if (initialClientState.HasDisconnectedGame)
		{
			OnDisconnectedGame(initialClientState.DisconnectedGame);
		}
		if (initialClientState.HasArenaSession)
		{
			PegasusPacket packet2 = new PegasusPacket(351, 0, initialClientState.ArenaSession);
			Network.Get().SimulateReceivedPacketFromServer(packet2);
		}
		if (initialClientState.HasDisplayBanner)
		{
			OnDisplayBanner(initialClientState.DisplayBanner);
		}
		if (initialClientState.Decks != null)
		{
			OnReceivedDeckHeaders_InitialClientState(ref data, initialClientState.Decks, initialClientState.DeckContents, initialClientState.ValidCachedDeckIds);
		}
		OfflineDataCache.WriteOfflineDataToFile(data);
		if (initialClientState.MedalInfo != null)
		{
			OnMedalInfo(initialClientState.MedalInfo);
		}
		if (HearthstoneApplication.IsInternal() && initialClientState.HasPlayerId)
		{
			if (!ServiceManager.TryGet<SceneDebugger>(out var service))
			{
				return;
			}
			service.SetPlayerId(initialClientState.PlayerId);
		}
		if (Network.Get() != null)
		{
			Network.Get().OnInitialClientStateProcessed();
		}
	}

	public void OnCollection(ref OfflineDataCache.OfflineData data, Collection collection)
	{
		m_initialCollectionVersion = collection.CollectionVersion;
		if (CollectionManager.Get() != null)
		{
			OnNetCacheObjReceived(CollectionManager.Get().OnInitialCollectionReceived(collection));
		}
		OfflineDataCache.CacheCollection(ref data, collection);
	}

	private void OnBoosters(Boosters boosters)
	{
		NetCacheBoosters netCacheBoosters = new NetCacheBoosters();
		for (int i = 0; i < boosters.List.Count; i++)
		{
			BoosterInfo boosterInfo = boosters.List[i];
			BoosterStack item = new BoosterStack
			{
				Id = boosterInfo.Type,
				Count = boosterInfo.Count,
				EverGrantedCount = boosterInfo.EverGrantedCount
			};
			netCacheBoosters.BoosterStacks.Add(item);
		}
		OnNetCacheObjReceived(netCacheBoosters);
	}

	public void OnPlayerDraftTickets(PlayerDraftTickets playerDraftTickets)
	{
		NetPlayerArenaTickets netPlayerArenaTickets = new NetPlayerArenaTickets();
		netPlayerArenaTickets.Balance = playerDraftTickets.UnusedTicketBalance;
		OnNetCacheObjReceived(netPlayerArenaTickets);
	}

	private void OnDisconnectedGame(GameConnectionInfo packet)
	{
		if (packet.HasAddress)
		{
			NetCacheDisconnectedGame netCacheDisconnectedGame = new NetCacheDisconnectedGame();
			netCacheDisconnectedGame.ServerInfo = new GameServerInfo();
			netCacheDisconnectedGame.ServerInfo.Address = packet.Address;
			netCacheDisconnectedGame.ServerInfo.GameHandle = (uint)packet.GameHandle;
			netCacheDisconnectedGame.ServerInfo.ClientHandle = packet.ClientHandle;
			netCacheDisconnectedGame.ServerInfo.Port = (uint)packet.Port;
			netCacheDisconnectedGame.ServerInfo.AuroraPassword = packet.AuroraPassword;
			netCacheDisconnectedGame.ServerInfo.Mission = packet.Scenario;
			netCacheDisconnectedGame.ServerInfo.BrawlLibraryItemId = packet.BrawlLibraryItemId;
			netCacheDisconnectedGame.ServerInfo.Version = BattleNet.GetVersion();
			netCacheDisconnectedGame.GameType = packet.GameType;
			netCacheDisconnectedGame.FormatType = packet.FormatType;
			if (packet.HasLoadGameState)
			{
				netCacheDisconnectedGame.LoadGameState = packet.LoadGameState;
			}
			else
			{
				netCacheDisconnectedGame.LoadGameState = false;
			}
			OnNetCacheObjReceived(netCacheDisconnectedGame);
		}
	}

	private void OnDisplayBanner(int displayBanner)
	{
		NetCacheDisplayBanner netCacheDisplayBanner = new NetCacheDisplayBanner();
		netCacheDisplayBanner.Id = displayBanner;
		OnNetCacheObjReceived(netCacheDisplayBanner);
	}

	private void OnReceivedDeckHeaders()
	{
		NetCacheDecks deckHeaders = Network.Get().GetDeckHeaders();
		OnNetCacheObjReceived(deckHeaders);
	}

	private void OnReceivedDeckHeaders_InitialClientState(ref OfflineDataCache.OfflineData data, List<DeckInfo> deckHeaders, List<DeckContents> deckContents, List<long> validCachedDeckIds)
	{
		foreach (DeckInfo fakeDeckInfo in OfflineDataCache.GetFakeDeckInfos(data))
		{
			deckHeaders.Add(fakeDeckInfo);
		}
		NetCacheDecks deckHeaders2 = Network.GetDeckHeaders(deckHeaders);
		OnNetCacheObjReceived(deckHeaders2);
		Network.Get().ReconcileDeckContentsForChangedOfflineDecks(ref data, deckHeaders, deckContents, validCachedDeckIds);
		CollectionManager.Get().OnInitialClientStateDeckContents(deckHeaders2, data.LocalDeckContents);
	}

	public List<DeckInfo> GetDeckListFromNetCache()
	{
		List<DeckInfo> list = new List<DeckInfo>();
		if (IsNetObjectAvailable<NetCacheDecks>())
		{
			foreach (DeckHeader deck in GetNetObject<NetCacheDecks>().Decks)
			{
				list.Add(Network.GetDeckInfoFromDeckHeader(deck));
			}
		}
		return list;
	}

	private void OnCardValues()
	{
		NetCacheCardValues netCacheCardValues = Get().GetNetObject<NetCacheCardValues>();
		CardValues cardValues = Network.Get().GetCardValues();
		if (cardValues != null)
		{
			if (netCacheCardValues == null)
			{
				netCacheCardValues = new NetCacheCardValues(cardValues.Cards.Count);
			}
			EventTimingManager eventTimingManager = EventTimingManager.Get();
			foreach (PegasusUtil.CardValue card in cardValues.Cards)
			{
				string text = GameUtils.TranslateDbIdToCardId(card.Card.Asset);
				if (text == null)
				{
					Log.All.PrintError("NetCache.OnCardValues(): Cannot find card '{0}' in card manifest.  Confirm your card manifest matches your game server's database.", card.Card.Asset);
					continue;
				}
				CardDefinition key = new CardDefinition
				{
					Name = text,
					Premium = (TAG_PREMIUM)card.Card.Premium
				};
				CardValue value = new CardValue
				{
					BaseBuyValue = card.Buy,
					BaseSellValue = card.Sell,
					BaseUpgradeValue = card.Upgrade,
					BuyValueOverride = (card.HasBuyValueOverride ? card.BuyValueOverride : 0),
					SellValueOverride = (card.HasSellValueOverride ? card.SellValueOverride : 0),
					OverrideEvent = (card.HasOverrideEventName ? eventTimingManager.GetEventType(card.OverrideEventName) : EventTimingType.SPECIAL_EVENT_NEVER)
				};
				if (netCacheCardValues.Values.ContainsKey(key))
				{
					Log.All.PrintError("NetCache.OnCardValues(): An item with the same key has already been added with cardId '{0}', premium '{1}'.", text, card.Card.Premium);
				}
				else
				{
					netCacheCardValues.Values.Add(key, value);
				}
			}
		}
		else if (netCacheCardValues == null)
		{
			netCacheCardValues = new NetCacheCardValues();
		}
		OnNetCacheObjReceived(netCacheCardValues);
	}

	private void OnMedalInfo()
	{
		NetCacheMedalInfo medalInfo = Network.Get().GetMedalInfo();
		if (m_previousMedalInfo != null)
		{
			medalInfo.PreviousMedalInfo = m_previousMedalInfo.Clone();
		}
		m_previousMedalInfo = medalInfo;
		OnNetCacheObjReceived(medalInfo);
	}

	private void OnMedalInfo(MedalInfo packet)
	{
		NetCacheMedalInfo netCacheMedalInfo = new NetCacheMedalInfo(packet);
		if (m_previousMedalInfo != null)
		{
			netCacheMedalInfo.PreviousMedalInfo = m_previousMedalInfo.Clone();
		}
		m_previousMedalInfo = netCacheMedalInfo;
		OnNetCacheObjReceived(netCacheMedalInfo);
	}

	private void OnBaconRatingInfo()
	{
		NetCacheBaconRatingInfo baconRatingInfo = Network.Get().GetBaconRatingInfo();
		OnNetCacheObjReceived(baconRatingInfo);
	}

	private void OnPVPDRStatsInfo()
	{
		NetCachePVPDRStatsInfo pVPDRStatsInfo = Network.Get().GetPVPDRStatsInfo();
		OnNetCacheObjReceived(pVPDRStatsInfo);
	}

	private void OnLettuceMapResponse()
	{
		LettuceMapResponse lettuceMapResponse = Network.Get().GetLettuceMapResponse();
		NetCacheLettuceMap netCacheObject = new NetCacheLettuceMap
		{
			Map = lettuceMapResponse.Map
		};
		OnNetCacheObjReceived(netCacheObject);
	}

	private Dictionary<MercenaryBuilding.Mercenarybuildingtype, bool> MakeBuildingEnabledMap(MercenariesOperabilityData opData)
	{
		if (opData == null)
		{
			return new Dictionary<MercenaryBuilding.Mercenarybuildingtype, bool>
			{
				{
					MercenaryBuilding.Mercenarybuildingtype.BUILDINGMANAGER,
					false
				},
				{
					MercenaryBuilding.Mercenarybuildingtype.COLLECTION,
					false
				},
				{
					MercenaryBuilding.Mercenarybuildingtype.MAILBOX,
					false
				},
				{
					MercenaryBuilding.Mercenarybuildingtype.PVEZONES,
					false
				},
				{
					MercenaryBuilding.Mercenarybuildingtype.PVP,
					false
				},
				{
					MercenaryBuilding.Mercenarybuildingtype.SHOP,
					false
				},
				{
					MercenaryBuilding.Mercenarybuildingtype.TASKBOARD,
					false
				},
				{
					MercenaryBuilding.Mercenarybuildingtype.TRAININGHALL,
					false
				}
			};
		}
		return new Dictionary<MercenaryBuilding.Mercenarybuildingtype, bool>
		{
			{
				MercenaryBuilding.Mercenarybuildingtype.BUILDINGMANAGER,
				!opData.HasBuildingManagementEnabled || opData.BuildingManagementEnabled
			},
			{
				MercenaryBuilding.Mercenarybuildingtype.COLLECTION,
				!opData.HasCollectionPortalEnabled || opData.CollectionPortalEnabled
			},
			{
				MercenaryBuilding.Mercenarybuildingtype.MAILBOX,
				!opData.HasInGameMessagingEnabled || opData.InGameMessagingEnabled
			},
			{
				MercenaryBuilding.Mercenarybuildingtype.PVEZONES,
				!opData.HasPvePortalEnabled || opData.PvePortalEnabled
			},
			{
				MercenaryBuilding.Mercenarybuildingtype.PVP,
				!opData.HasPvpPortalEnabled || opData.PvpPortalEnabled
			},
			{
				MercenaryBuilding.Mercenarybuildingtype.SHOP,
				!opData.HasShopPortalEnabled || opData.ShopPortalEnabled
			},
			{
				MercenaryBuilding.Mercenarybuildingtype.TASKBOARD,
				!opData.HasTasksEnabled || opData.TasksEnabled
			},
			{
				MercenaryBuilding.Mercenarybuildingtype.TRAININGHALL,
				!opData.HasTrainingHallEnabled || opData.TrainingHallEnabled
			}
		};
	}

	private void OnMercenariesPlayerInfoResponse()
	{
		MercenariesPlayerInfoResponse mercenariesPlayerInfoResponse = Network.Get().MercenariesPlayerInfoResponse();
		if (mercenariesPlayerInfoResponse == null)
		{
			Log.CollectionManager.PrintError("OnMercenariesPlayerInfoResponse(): No response received.");
			return;
		}
		if (!mercenariesPlayerInfoResponse.HasPvpRewardChestWinsProgress)
		{
			Log.CollectionManager.PrintError("OnMercenariesPlayerInfoResponse(): No pvp reward chest wins progress received.");
			return;
		}
		if (!mercenariesPlayerInfoResponse.HasPvpRewardChestWinsRequired)
		{
			Log.CollectionManager.PrintError("OnMercenariesPlayerInfoResponse(): No pvp reward chest wins required received.");
			return;
		}
		Dictionary<int, NetCacheMercenariesPlayerInfo.BountyInfo> dictionary = new Dictionary<int, NetCacheMercenariesPlayerInfo.BountyInfo>();
		foreach (MercenariesPlayerBountyInfo item in mercenariesPlayerInfoResponse.BountyInfoList.BountyInfo)
		{
			NetCacheMercenariesPlayerInfo.BountyInfo value = new NetCacheMercenariesPlayerInfo.BountyInfo
			{
				FewestTurns = (int)item.FewestTurns,
				Completions = (int)item.Completions,
				IsComplete = item.IsComplete,
				IsAcknowledged = item.Acknowledged,
				BossCardIds = (item.HasBossCards ? item.BossCards.BossCardId : null),
				MaxMythicLevel = (int)item.MaxMythicLevel,
				UnlockTime = (item.HasUnlockUnixTime ? new DateTime?(TimeUtils.UnixTimeStampToDateTimeUtc(item.UnlockUnixTime)) : ((DateTime?)null))
			};
			dictionary.Add((int)item.BountyId, value);
		}
		MercenariesOperabilityData mercenariesOperabilityData = (mercenariesPlayerInfoResponse.HasOperabilityData ? mercenariesPlayerInfoResponse.OperabilityData : null);
		NetCacheMercenariesPlayerInfo netCacheObject = new NetCacheMercenariesPlayerInfo
		{
			PvpRating = mercenariesPlayerInfoResponse.PvpRating,
			PvpRewardChestWinsProgress = mercenariesPlayerInfoResponse.PvpRewardChestWinsProgress,
			PvpRewardChestWinsRequired = mercenariesPlayerInfoResponse.PvpRewardChestWinsRequired,
			BountyInfoMap = dictionary,
			BuildingEnabledMap = MakeBuildingEnabledMap(mercenariesOperabilityData),
			DisabledMercenaryList = (mercenariesOperabilityData?.DisabledMercenaryId ?? new List<int>()),
			DisabledVisitorList = new HashSet<int>(mercenariesOperabilityData?.DisabledVisitorId ?? new List<int>()),
			DisabledBuildingTierUpgradeList = (mercenariesOperabilityData?.DisabledBuildingTierUpgradeId ?? new List<int>()),
			PvpSeasonHighestRating = mercenariesPlayerInfoResponse.PvpSeasonHighestRating,
			PvpSeasonId = mercenariesPlayerInfoResponse.PvpSeasonId,
			CurrentMythicBountyLevel = (int)(mercenariesPlayerInfoResponse.MythicBountyLevelInfo?.CurrentMythicBountyLevel ?? 0),
			MinMythicBountyLevel = (int)(mercenariesPlayerInfoResponse.MythicBountyLevelInfo?.MinMythicBountyLevel ?? 0),
			MaxMythicBountyLevel = (int)(mercenariesPlayerInfoResponse.MythicBountyLevelInfo?.MaxMythicBountyLevel ?? 0),
			GeneratedBountyResetTime = TimeUtils.UnixTimeStampToDateTimeUtc(mercenariesPlayerInfoResponse.GeneratedBountyResetTime)
		};
		OnNetCacheObjReceived(netCacheObject);
	}

	public void UpdateNetCachePlayerInfoAcknowledgedBounties(List<int> bountiesToAcknowledge)
	{
		NetCacheMercenariesPlayerInfo netObject = Get().GetNetObject<NetCacheMercenariesPlayerInfo>();
		foreach (int item in bountiesToAcknowledge)
		{
			if (netObject.BountyInfoMap.ContainsKey(item))
			{
				netObject.BountyInfoMap[item].IsAcknowledged = true;
				continue;
			}
			NetCacheMercenariesPlayerInfo.BountyInfo bountyInfo = new NetCacheMercenariesPlayerInfo.BountyInfo();
			bountyInfo.IsAcknowledged = true;
			bountyInfo.IsComplete = false;
			bountyInfo.Completions = 0;
			bountyInfo.FewestTurns = 0;
			bountyInfo.BossCardIds = null;
			netObject.BountyInfoMap[item] = bountyInfo;
		}
		OnNetCacheObjReceived(netObject);
	}

	private void OnMercenariesPvPWinUpdate()
	{
		MercenariesPvPWinUpdate mercenariesPvPWinUpdate = Network.Get().MercenariesPvPWinUpdate();
		if (mercenariesPvPWinUpdate == null)
		{
			Log.CollectionManager.PrintError("OnMercenariesPvPWinUpdate(): No response received.");
			return;
		}
		if (!mercenariesPvPWinUpdate.HasPvpRewardChestWinsProgress)
		{
			Log.CollectionManager.PrintError("OnMercenariesPvPWinUpdate(): No pvp reward chest wins progress received.");
			return;
		}
		if (!mercenariesPvPWinUpdate.HasPvpRewardChestWinsRequired)
		{
			Log.CollectionManager.PrintError("OnMercenariesPvPWinUpdate(): No pvp reward chest wins required received.");
			return;
		}
		NetCacheMercenariesPlayerInfo netObject = Get().GetNetObject<NetCacheMercenariesPlayerInfo>();
		if (netObject != null)
		{
			netObject.PvpRewardChestWinsProgress = mercenariesPvPWinUpdate.PvpRewardChestWinsProgress;
			netObject.PvpRewardChestWinsRequired = mercenariesPvPWinUpdate.PvpRewardChestWinsRequired;
			OnNetCacheObjReceived(netObject);
		}
	}

	private void OnMercenariesPlayerBountyInfoUpdate()
	{
		MercenariesPlayerBountyInfoUpdate mercenariesPlayerBountyInfoUpdate = Network.Get().MercenariesPlayerBountyInfoUpdate();
		if (mercenariesPlayerBountyInfoUpdate == null)
		{
			Log.CollectionManager.PrintError("OnMercenariesPlayerBountyInfoUpdate(): No response received.");
			return;
		}
		NetCacheMercenariesPlayerInfo netObject = Get().GetNetObject<NetCacheMercenariesPlayerInfo>();
		if (netObject == null)
		{
			Log.CollectionManager.PrintError("OnMercenariesPlayerBountyInfoUpdate(): No player info.");
			return;
		}
		if (netObject.BountyInfoMap == null)
		{
			netObject.BountyInfoMap = new Dictionary<int, NetCacheMercenariesPlayerInfo.BountyInfo>();
		}
		if (mercenariesPlayerBountyInfoUpdate.BountyInfo == null)
		{
			Log.CollectionManager.PrintError("OnMercenariesPlayerBountyInfoUpdate(): No bounty info.");
			return;
		}
		netObject.BountyInfoMap.TryGetValue((int)mercenariesPlayerBountyInfoUpdate.BountyInfo.BountyId, out var value);
		if (value != null)
		{
			if (value.FewestTurns == 0)
			{
				value.FewestTurns = (int)mercenariesPlayerBountyInfoUpdate.BountyInfo.FewestTurns;
			}
			else if (mercenariesPlayerBountyInfoUpdate.BountyInfo.FewestTurns != 0)
			{
				value.FewestTurns = Math.Min(value.FewestTurns, (int)mercenariesPlayerBountyInfoUpdate.BountyInfo.FewestTurns);
			}
			value.Completions = Math.Max(value.Completions, (int)mercenariesPlayerBountyInfoUpdate.BountyInfo.Completions);
			value.IsComplete = value.IsComplete || mercenariesPlayerBountyInfoUpdate.BountyInfo.IsComplete;
			value.IsAcknowledged = value.IsAcknowledged || mercenariesPlayerBountyInfoUpdate.BountyInfo.IsAcknowledged;
			if (mercenariesPlayerBountyInfoUpdate.BountyInfo.HasBossCards)
			{
				value.BossCardIds = mercenariesPlayerBountyInfoUpdate.BountyInfo.BossCards.BossCardId;
			}
			value.MaxMythicLevel = (int)mercenariesPlayerBountyInfoUpdate.BountyInfo.MaxMythicLevel;
			value.UnlockTime = (mercenariesPlayerBountyInfoUpdate.BountyInfo.HasUnlockUnixTime ? new DateTime?(TimeUtils.UnixTimeStampToDateTimeUtc(mercenariesPlayerBountyInfoUpdate.BountyInfo.UnlockUnixTime)) : ((DateTime?)null));
		}
		else
		{
			netObject.BountyInfoMap[(int)mercenariesPlayerBountyInfoUpdate.BountyInfo.BountyId] = new NetCacheMercenariesPlayerInfo.BountyInfo
			{
				FewestTurns = (int)mercenariesPlayerBountyInfoUpdate.BountyInfo.FewestTurns,
				Completions = (int)mercenariesPlayerBountyInfoUpdate.BountyInfo.Completions,
				IsComplete = mercenariesPlayerBountyInfoUpdate.BountyInfo.IsComplete,
				IsAcknowledged = mercenariesPlayerBountyInfoUpdate.BountyInfo.IsAcknowledged,
				BossCardIds = (mercenariesPlayerBountyInfoUpdate.BountyInfo.HasBossCards ? mercenariesPlayerBountyInfoUpdate.BountyInfo.BossCards.BossCardId : null),
				MaxMythicLevel = (int)mercenariesPlayerBountyInfoUpdate.BountyInfo.MaxMythicLevel,
				UnlockTime = (mercenariesPlayerBountyInfoUpdate.BountyInfo.HasUnlockUnixTime ? new DateTime?(TimeUtils.UnixTimeStampToDateTimeUtc(mercenariesPlayerBountyInfoUpdate.BountyInfo.UnlockUnixTime)) : ((DateTime?)null))
			};
		}
		OnNetCacheObjReceived(netObject);
	}

	private static int CompareVisitorStates(MercenariesVisitorState x, MercenariesVisitorState y)
	{
		MercenaryVisitorDbfRecord visitorRecordByID = LettuceVillageDataUtil.GetVisitorRecordByID(x.VisitorId);
		MercenaryVisitorDbfRecord visitorRecordByID2 = LettuceVillageDataUtil.GetVisitorRecordByID(y.VisitorId);
		if (visitorRecordByID == null || visitorRecordByID2 == null)
		{
			return 0;
		}
		if (visitorRecordByID.VisitorType > visitorRecordByID2.VisitorType)
		{
			return -1;
		}
		if (visitorRecordByID2.VisitorType > visitorRecordByID.VisitorType)
		{
			return 1;
		}
		long value = TimeUtils.PegDateToFileTimeUtc(x.LastArrivalDate);
		return TimeUtils.PegDateToFileTimeUtc(y.LastArrivalDate).CompareTo(value);
	}

	private void OnMercenariesBountyAcknowledgeResponse()
	{
		Network.Get().AcknowledgeBountiesResponse();
	}

	private void OnVillageDataResponse()
	{
		MercenariesGetVillageResponse mercenariesGetVillageResponse = Network.Get().MercenariesVillageStatusResponse();
		NetCacheMercenariesVillageInfo netCacheMercenariesVillageInfo = new NetCacheMercenariesVillageInfo();
		NetCacheMercenariesVillageVisitorInfo netCacheMercenariesVillageVisitorInfo = new NetCacheMercenariesVillageVisitorInfo();
		netCacheMercenariesVillageInfo.Initialized = true;
		if (mercenariesGetVillageResponse == null)
		{
			Log.CollectionManager.PrintError("OnVillageDataResponse(): No response received.");
			OnNetCacheObjReceived(netCacheMercenariesVillageInfo);
			OnNetCacheObjReceived(netCacheMercenariesVillageVisitorInfo);
			return;
		}
		if (!mercenariesGetVillageResponse.Success)
		{
			Debug.LogError("Failed to load village data");
		}
		netCacheMercenariesVillageVisitorInfo.VisitorStates = mercenariesGetVillageResponse.Visitor ?? new List<MercenariesVisitorState>();
		netCacheMercenariesVillageVisitorInfo.VisitorStates.Sort(CompareVisitorStates);
		netCacheMercenariesVillageVisitorInfo.CompletedTasks = new List<MercenariesTaskState>();
		netCacheMercenariesVillageVisitorInfo.CompletedVisitorStates = mercenariesGetVillageResponse.CompletedVisitor ?? new List<MercenariesCompletedVisitorState>();
		netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers = mercenariesGetVillageResponse.RenownOffer ?? new List<MercenariesRenownOfferData>();
		CollectVisitingMercenariesFromVisitorStates(netCacheMercenariesVillageVisitorInfo);
		netCacheMercenariesVillageInfo.BuildingStates = new List<MercenariesBuildingState>();
		foreach (MercenariesBuildingState item in mercenariesGetVillageResponse.Building)
		{
			if (IsBuildingStateValid(item))
			{
				netCacheMercenariesVillageInfo.TrySetDifficultyUnlock(item);
				netCacheMercenariesVillageInfo.BuildingStates.Add(item);
			}
		}
		netCacheMercenariesVillageInfo.CacheTierTree();
		netCacheMercenariesVillageInfo.CacheRenownConversionRates(mercenariesGetVillageResponse.RenownConversionRate);
		OnNetCacheObjReceived(netCacheMercenariesVillageInfo);
		OnNetCacheObjReceived(netCacheMercenariesVillageVisitorInfo);
		NarrativeManager.Get().PreloadDialogForActiveVillageBuildings();
		GameSaveDataManager.Get().ApplyGameSaveDataUpdate(mercenariesGetVillageResponse.GameSaveData);
	}

	private void OnVillageVisitorStateUpdated()
	{
		MercenariesVisitorStateUpdate mercenariesVisitorStateUpdate = Network.Get().MercenariesVisitorStateUpdate();
		if (mercenariesVisitorStateUpdate == null)
		{
			Log.CollectionManager.PrintError("OnVillageVisitorStateUpdated(): No response received.");
			return;
		}
		NetCacheMercenariesVillageVisitorInfo netCacheMercenariesVillageVisitorInfo = Get().GetNetObject<NetCacheMercenariesVillageVisitorInfo>();
		if (netCacheMercenariesVillageVisitorInfo == null)
		{
			netCacheMercenariesVillageVisitorInfo = new NetCacheMercenariesVillageVisitorInfo
			{
				VisitorStates = new List<MercenariesVisitorState>(),
				CompletedVisitorStates = new List<MercenariesCompletedVisitorState>(),
				ActiveRenownOffers = new List<MercenariesRenownOfferData>()
			};
		}
		if (mercenariesVisitorStateUpdate.Visitor != null)
		{
			foreach (MercenariesVisitorState stateUpdate in mercenariesVisitorStateUpdate.Visitor)
			{
				if (!netCacheMercenariesVillageVisitorInfo.VisitorStates.Exists((MercenariesVisitorState state) => state.VisitorId == stateUpdate.VisitorId))
				{
					netCacheMercenariesVillageVisitorInfo.VisitorStates.Add(stateUpdate);
				}
				else
				{
					for (int num = netCacheMercenariesVillageVisitorInfo.VisitorStates.Count - 1; num >= 0; num--)
					{
						MercenariesVisitorState mercenariesVisitorState = netCacheMercenariesVillageVisitorInfo.VisitorStates[num];
						if (mercenariesVisitorState.VisitorId == stateUpdate.VisitorId)
						{
							if (stateUpdate.ActiveTaskState == null || stateUpdate.ActiveTaskState.TaskId == 0)
							{
								if (mercenariesVisitorState.ActiveTaskState != null)
								{
									VisitorTaskChainDbfRecord currentTaskChainByVisitorState = LettuceVillageDataUtil.GetCurrentTaskChainByVisitorState(mercenariesVisitorState);
									if (currentTaskChainByVisitorState != null && stateUpdate.TaskChainProgress >= currentTaskChainByVisitorState.TaskList.Count)
									{
										netCacheMercenariesVillageVisitorInfo.CompletedVisitorStates.Add(new MercenariesCompletedVisitorState
										{
											VisitorId = stateUpdate.VisitorId,
											CompletedTaskChainId = currentTaskChainByVisitorState.ID
										});
									}
								}
								netCacheMercenariesVillageVisitorInfo.VisitorStates.RemoveAt(num);
							}
							else
							{
								netCacheMercenariesVillageVisitorInfo.VisitorStates[num] = stateUpdate;
							}
						}
					}
				}
				if (stateUpdate.HasActiveTaskState && stateUpdate.ActiveTaskState.Status_ == MercenariesTaskState.Status.COMPLETE)
				{
					if (netCacheMercenariesVillageVisitorInfo.CompletedTasks == null)
					{
						netCacheMercenariesVillageVisitorInfo.CompletedTasks = new List<MercenariesTaskState>();
					}
					netCacheMercenariesVillageVisitorInfo.CompletedTasks.Add(stateUpdate.ActiveTaskState);
				}
			}
			netCacheMercenariesVillageVisitorInfo.VisitorStates.Sort(CompareVisitorStates);
		}
		if (mercenariesVisitorStateUpdate.UpdatedRenownOffer != null && mercenariesVisitorStateUpdate.UpdatedRenownOffer.Count > 0)
		{
			foreach (MercenariesRenownOfferData item in mercenariesVisitorStateUpdate.UpdatedRenownOffer)
			{
				bool flag = false;
				for (int num2 = netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers.Count - 1; num2 >= 0; num2--)
				{
					if (netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers[num2].RenownOfferId == item.RenownOfferId)
					{
						netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers[num2] = item;
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					if (netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers == null)
					{
						netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers = new List<MercenariesRenownOfferData>();
					}
					netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers.Add(item);
				}
			}
		}
		if (mercenariesVisitorStateUpdate.RemovedRenownOfferId != null && mercenariesVisitorStateUpdate.RemovedRenownOfferId.Count > 0 && netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers != null)
		{
			for (int num3 = netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers.Count - 1; num3 >= 0; num3--)
			{
				MercenariesRenownOfferData mercenariesRenownOfferData = netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers[num3];
				if (mercenariesVisitorStateUpdate.RemovedRenownOfferId.Contains(mercenariesRenownOfferData.RenownOfferId))
				{
					netCacheMercenariesVillageVisitorInfo.ActiveRenownOffers.RemoveAt(num3);
				}
			}
		}
		GameSaveDataManager.Get().ApplyGameSaveDataUpdate(mercenariesVisitorStateUpdate.GameSaveData);
		CollectVisitingMercenariesFromVisitorStates(netCacheMercenariesVillageVisitorInfo);
		OnNetCacheObjReceived(netCacheMercenariesVillageVisitorInfo);
	}

	private void CollectVisitingMercenariesFromVisitorStates(NetCacheMercenariesVillageVisitorInfo visitorInfo)
	{
		HashSet<int> hashSet = new HashSet<int>();
		NetCacheMercenariesPlayerInfo netObject = GetNetObject<NetCacheMercenariesPlayerInfo>();
		if (netObject == null)
		{
			Debug.LogError("Null playerInfo in CollectVisitingMercenariesFromVisitorStates");
			return;
		}
		HashSet<int> hashSet2 = netObject.DisabledVisitorList;
		if (hashSet2 == null)
		{
			Debug.LogError("Null disabledVisitors in CollectVisitingMercenariesFromVisitorStates");
			hashSet2 = new HashSet<int>();
		}
		if (visitorInfo.VisitorStates != null)
		{
			foreach (MercenariesVisitorState visitorState in visitorInfo.VisitorStates)
			{
				if (hashSet2.Contains(visitorState.VisitorId) || visitorState.ActiveTaskState == null)
				{
					continue;
				}
				MercenaryVisitorDbfRecord visitorRecordByID = LettuceVillageDataUtil.GetVisitorRecordByID(visitorState.VisitorId);
				if (visitorRecordByID == null)
				{
					Debug.LogError($"mercRecord {visitorState.VisitorId} not found in CollectVisitingMercenariesFromVisitorStates");
				}
				else
				{
					if (visitorRecordByID.VisitorType == MercenaryVisitor.VillageVisitorType.STANDARD)
					{
						continue;
					}
					VisitorTaskDbfRecord taskRecordByID = LettuceVillageDataUtil.GetTaskRecordByID(visitorState.ActiveTaskState.TaskId);
					if (visitorRecordByID != null && taskRecordByID != null)
					{
						if (visitorRecordByID.VisitorType == MercenaryVisitor.VillageVisitorType.PROCEDURAL)
						{
							hashSet.Add(visitorState.ProceduralMercenaryId);
						}
						else
						{
							hashSet.Add(LettuceVillageDataUtil.GetMercenaryIdForVisitor(visitorRecordByID, taskRecordByID));
						}
					}
				}
			}
		}
		if (visitorInfo.ActiveRenownOffers != null)
		{
			foreach (MercenariesRenownOfferData activeRenownOffer in visitorInfo.ActiveRenownOffers)
			{
				if (activeRenownOffer.MercenaryId != 0)
				{
					hashSet.Add(activeRenownOffer.MercenaryId);
				}
			}
		}
		visitorInfo.VisitingMercenaries = hashSet.ToArray();
	}

	private void OnRefreshVillageDataResponse()
	{
		MercenariesRefreshVillageResponse mercenariesRefreshVillageResponse = Network.Get().MercenariesVillageRefreshResponse();
		if (mercenariesRefreshVillageResponse == null || !mercenariesRefreshVillageResponse.Success)
		{
			Debug.LogError($"Failed to refresh village data");
		}
		NetCacheMercenariesPlayerInfo netObject = GetNetObject<NetCacheMercenariesPlayerInfo>();
		bool flag = false;
		if (mercenariesRefreshVillageResponse.HasGeneratedBountyResetTime)
		{
			netObject.GeneratedBountyResetTime = TimeUtils.UnixTimeStampToDateTimeUtc(mercenariesRefreshVillageResponse.GeneratedBountyResetTime);
			flag = true;
		}
		if (mercenariesRefreshVillageResponse.HasMythicBountyLevelInfo)
		{
			netObject.CurrentMythicBountyLevel = (int)mercenariesRefreshVillageResponse.MythicBountyLevelInfo.CurrentMythicBountyLevel;
			netObject.MaxMythicBountyLevel = (int)mercenariesRefreshVillageResponse.MythicBountyLevelInfo.MaxMythicBountyLevel;
			netObject.MinMythicBountyLevel = (int)mercenariesRefreshVillageResponse.MythicBountyLevelInfo.MinMythicBountyLevel;
			flag = true;
		}
		if (mercenariesRefreshVillageResponse.HasBountyInfoList)
		{
			foreach (MercenariesPlayerBountyInfo item in mercenariesRefreshVillageResponse.BountyInfoList.BountyInfo)
			{
				netObject.BountyInfoMap.TryGetValue((int)item.BountyId, out var value);
				if (value != null)
				{
					value.FewestTurns = (int)item.FewestTurns;
					value.Completions = (int)item.Completions;
					value.IsComplete = item.IsComplete;
					value.IsAcknowledged = item.IsAcknowledged;
					if (item.HasBossCards)
					{
						value.BossCardIds = item.BossCards.BossCardId;
					}
					value.MaxMythicLevel = (int)item.MaxMythicLevel;
					value.UnlockTime = (item.HasUnlockUnixTime ? new DateTime?(TimeUtils.UnixTimeStampToDateTimeUtc(item.UnlockUnixTime)) : ((DateTime?)null));
				}
				else
				{
					netObject.BountyInfoMap[(int)item.BountyId] = new NetCacheMercenariesPlayerInfo.BountyInfo
					{
						FewestTurns = (int)item.FewestTurns,
						Completions = (int)item.Completions,
						IsComplete = item.IsComplete,
						IsAcknowledged = item.IsAcknowledged,
						BossCardIds = (item.HasBossCards ? item.BossCards.BossCardId : null),
						MaxMythicLevel = (int)item.MaxMythicLevel,
						UnlockTime = (item.HasUnlockUnixTime ? new DateTime?(TimeUtils.UnixTimeStampToDateTimeUtc(item.UnlockUnixTime)) : ((DateTime?)null))
					};
				}
			}
			flag = true;
		}
		if (flag)
		{
			OnNetCacheObjReceived(netObject);
		}
	}

	private bool IsBuildingStateValid(MercenariesBuildingState bldgState)
	{
		if (bldgState == null)
		{
			return false;
		}
		MercenaryBuildingDbfRecord bldgRecord = GameDbf.MercenaryBuilding.GetRecord((MercenaryBuildingDbfRecord r) => r.ID == bldgState.BuildingId);
		if (bldgRecord == null)
		{
			return false;
		}
		if (GameDbf.BuildingTier.GetRecords((BuildingTierDbfRecord r) => r.MercenaryBuildingId == bldgRecord.ID && r.ID == bldgState.CurrentTierId) == null)
		{
			return false;
		}
		return true;
	}

	private void OnVillageBuildingStateUpdated()
	{
		MercenariesBuildingStateUpdate mercenariesBuildingStateUpdate = Network.Get().MercenariesBuildingStateUpdate();
		NetCacheMercenariesVillageInfo netObject = Get().GetNetObject<NetCacheMercenariesVillageInfo>();
		foreach (MercenariesBuildingState item in mercenariesBuildingStateUpdate.Building)
		{
			if (!IsBuildingStateValid(item))
			{
				continue;
			}
			bool flag = false;
			for (int i = 0; i < netObject.BuildingStates.Count; i++)
			{
				if (netObject.BuildingStates[i].BuildingId == item.BuildingId)
				{
					netObject.BuildingStates[i] = item;
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				netObject.BuildingStates.Add(item);
			}
			netObject.TrySetDifficultyUnlock(item);
		}
		GameSaveDataManager.Get().ApplyGameSaveDataUpdate(mercenariesBuildingStateUpdate.GameSaveData);
		netObject.LastBuildingUpdate = mercenariesBuildingStateUpdate.Building;
		OnNetCacheObjReceived(netObject);
	}

	private void OnGuardianVars()
	{
		GuardianVars guardianVars = Network.Get().GetGuardianVars();
		if (guardianVars != null)
		{
			OnGuardianVars(guardianVars);
		}
	}

	private void OnGuardianVars(GuardianVars packet)
	{
		NetCacheFeatures netCacheFeatures = new NetCacheFeatures();
		netCacheFeatures.Games.Tournament = !packet.HasTourney || packet.Tourney;
		netCacheFeatures.Games.Practice = !packet.HasPractice || packet.Practice;
		netCacheFeatures.Games.Casual = !packet.HasCasual || packet.Casual;
		netCacheFeatures.Games.Forge = !packet.HasForge || packet.Forge;
		netCacheFeatures.Games.Friendly = !packet.HasFriendly || packet.Friendly;
		netCacheFeatures.Games.TavernBrawl = !packet.HasTavernBrawl || packet.TavernBrawl;
		netCacheFeatures.Games.Battlegrounds = !packet.HasBattlegrounds || packet.Battlegrounds;
		netCacheFeatures.Games.BattlegroundsFriendlyChallenge = !packet.HasBattlegroundsFriendlyChallenge || packet.BattlegroundsFriendlyChallenge;
		netCacheFeatures.Games.BattlegroundsTutorial = !packet.HasBattlegroundsTutorial || packet.BattlegroundsTutorial;
		netCacheFeatures.Games.BattlegroundsDuos = !packet.HasBattlegroundsDuos || packet.BattlegroundsDuos;
		netCacheFeatures.Games.ShowUserUI = (packet.HasShowUserUI ? packet.ShowUserUI : 0);
		netCacheFeatures.Games.Duels = !packet.HasDuels || packet.Duels;
		netCacheFeatures.Games.PaidDuels = !packet.HasPaidDuels || packet.PaidDuels;
		netCacheFeatures.Games.Mercenaries = !packet.HasMercenaries || packet.Mercenaries;
		netCacheFeatures.Games.MercenariesAI = !packet.HasMercenariesAi || packet.MercenariesAi;
		netCacheFeatures.Games.MercenariesCoOp = !packet.HasMercenariesCoop || packet.MercenariesCoop;
		netCacheFeatures.Games.MercenariesFriendly = !packet.HasMercenariesFriendlyChallenge || packet.MercenariesFriendlyChallenge;
		netCacheFeatures.Games.MercenariesMythic = !packet.HasMercenariesMythic || packet.MercenariesMythic;
		netCacheFeatures.Collection.Manager = !packet.HasManager || packet.Manager;
		netCacheFeatures.Collection.Crafting = !packet.HasCrafting || packet.Crafting;
		netCacheFeatures.Collection.DeckReordering = !packet.HasDeckReordering || packet.DeckReordering;
		netCacheFeatures.Collection.MultipleFavoriteCardBacks = !packet.HasMultipleFavoriteCardBacks || packet.MultipleFavoriteCardBacks;
		netCacheFeatures.Collection.CosmeticsRenderingEnabled = packet.CosmeticsRenderingEnabled;
		netCacheFeatures.Store.Store = !packet.HasStore || packet.Store;
		netCacheFeatures.Store.BattlePay = !packet.HasBattlePay || packet.BattlePay;
		netCacheFeatures.Store.BuyWithGold = !packet.HasBuyWithGold || packet.BuyWithGold;
		netCacheFeatures.Store.SimpleCheckout = !packet.HasSimpleCheckout || packet.SimpleCheckout;
		netCacheFeatures.Store.SoftAccountPurchasing = !packet.HasSoftAccountPurchasing || packet.SoftAccountPurchasing;
		netCacheFeatures.Store.VirtualCurrencyEnabled = packet.HasVirtualCurrencyEnabled && packet.VirtualCurrencyEnabled;
		netCacheFeatures.Store.NumClassicPacksUntilDeprioritize = (packet.HasNumClassicPacksUntilDeprioritize ? packet.NumClassicPacksUntilDeprioritize : (-1));
		netCacheFeatures.Store.SimpleCheckoutIOS = !packet.HasSimpleCheckoutIos || packet.SimpleCheckoutIos;
		netCacheFeatures.Store.SimpleCheckoutAndroidAmazon = !packet.HasSimpleCheckoutAndroidAmazon || packet.SimpleCheckoutAndroidAmazon;
		netCacheFeatures.Store.SimpleCheckoutAndroidGoogle = !packet.HasSimpleCheckoutAndroidGoogle || packet.SimpleCheckoutAndroidGoogle;
		netCacheFeatures.Store.SimpleCheckoutAndroidGlobal = !packet.HasSimpleCheckoutAndroidGlobal || packet.SimpleCheckoutAndroidGlobal;
		netCacheFeatures.Store.SimpleCheckoutWin = !packet.HasSimpleCheckoutWin || packet.SimpleCheckoutWin;
		netCacheFeatures.Store.SimpleCheckoutMac = !packet.HasSimpleCheckoutMac || packet.SimpleCheckoutMac;
		netCacheFeatures.Store.BoosterRotatingSoonWarnDaysWithoutSale = (packet.HasBoosterRotatingSoonWarnDaysWithoutSale ? packet.BoosterRotatingSoonWarnDaysWithoutSale : 0);
		netCacheFeatures.Store.BoosterRotatingSoonWarnDaysWithSale = (packet.HasBoosterRotatingSoonWarnDaysWithSale ? packet.BoosterRotatingSoonWarnDaysWithSale : 0);
		netCacheFeatures.Store.BuyCardBacksFromCollectionManager = !packet.HasBuyCardBacksFromCollectionManagerEnabled || packet.BuyCardBacksFromCollectionManagerEnabled;
		netCacheFeatures.Store.BuyHeroSkinsFromCollectionManager = !packet.HasBuyHeroSkinsFromCollectionManagerEnabled || packet.BuyHeroSkinsFromCollectionManagerEnabled;
		netCacheFeatures.Store.LargeItemBundleDetailsEnabled = !packet.HasLargeItemBundleDetailsEnabled || packet.LargeItemBundleDetailsEnabled;
		netCacheFeatures.Store.HideLowPriorityProducts = packet.HasHideLowPriorityProducts && packet.HideLowPriorityProducts;
		netCacheFeatures.Store.TavernBrawlGoldPurchaseConfirm = packet.HasTavernBrawlGoldPurchaseConfirm && packet.TavernBrawlGoldPurchaseConfirm;
		netCacheFeatures.Store.EnableVirtualCurrencyMenu = packet.HasEnableVirtualCurrencyMenu && packet.EnableVirtualCurrencyMenu;
		netCacheFeatures.Heroes.Hunter = !packet.HasHunter || packet.Hunter;
		netCacheFeatures.Heroes.Mage = !packet.HasMage || packet.Mage;
		netCacheFeatures.Heroes.Paladin = !packet.HasPaladin || packet.Paladin;
		netCacheFeatures.Heroes.Priest = !packet.HasPriest || packet.Priest;
		netCacheFeatures.Heroes.Rogue = !packet.HasRogue || packet.Rogue;
		netCacheFeatures.Heroes.Shaman = !packet.HasShaman || packet.Shaman;
		netCacheFeatures.Heroes.Warlock = !packet.HasWarlock || packet.Warlock;
		netCacheFeatures.Heroes.Warrior = !packet.HasWarrior || packet.Warrior;
		netCacheFeatures.Misc.ClientOptionsUpdateIntervalSeconds = (packet.HasClientOptionsUpdateIntervalSeconds ? packet.ClientOptionsUpdateIntervalSeconds : 0);
		netCacheFeatures.Misc.AllowLiveFPSGathering = packet.HasAllowLiveFpsGathering && packet.AllowLiveFpsGathering;
		netCacheFeatures.CancelMatchmakingDuringLoanerDeckGrant = packet.HasCancelMatchmakingDuringLoanerDeckGrant && packet.CancelMatchmakingDuringLoanerDeckGrant;
		netCacheFeatures.AllowBGInviteWhileInNPPG = packet.HasAllowBgInviteInNppg && packet.AllowBgInviteInNppg;
		netCacheFeatures.ArenaRedraftOnLossEnabled = packet.HasArenaRedraftOnLossEnabled && packet.ArenaRedraftOnLossEnabled;
		netCacheFeatures.BoxProductBannerEnabled = packet.HasBoxProductBannerEnabled && packet.BoxProductBannerEnabled;
		netCacheFeatures.CaisEnabledNonMobile = !packet.HasCaisEnabledNonMobile || packet.CaisEnabledNonMobile;
		netCacheFeatures.CaisEnabledMobileChina = packet.HasCaisEnabledMobileChina && packet.CaisEnabledMobileChina;
		netCacheFeatures.CaisEnabledMobileSouthKorea = packet.HasCaisEnabledMobileSouthKorea && packet.CaisEnabledMobileSouthKorea;
		netCacheFeatures.SendTelemetryPresence = packet.HasSendTelemetryPresence && packet.SendTelemetryPresence;
		netCacheFeatures.XPSoloLimit = packet.XpSoloLimit;
		netCacheFeatures.MaxHeroLevel = packet.MaxHeroLevel;
		netCacheFeatures.SpecialEventTimingMod = packet.EventTimingMod;
		netCacheFeatures.FriendWeekConcederMaxDefense = packet.FriendWeekConcederMaxDefense;
		netCacheFeatures.FriendWeekConcededGameMinTotalTurns = packet.FriendWeekConcededGameMinTotalTurns;
		netCacheFeatures.FriendWeekAllowsTavernBrawlRecordUpdate = packet.FriendWeekAllowsTavernBrawlRecordUpdate;
		netCacheFeatures.ArenaClosedToNewSessionsSeconds = (packet.HasArenaClosedToNewSessionsSeconds ? packet.ArenaClosedToNewSessionsSeconds : 0u);
		netCacheFeatures.PVPDRClosedToNewSessionsSeconds = (packet.HasPvpdrClosedToNewSessionsSeconds ? packet.PvpdrClosedToNewSessionsSeconds : 0u);
		netCacheFeatures.QuickOpenEnabled = packet.HasQuickOpenEnabled && packet.QuickOpenEnabled;
		netCacheFeatures.ForceIosLowRes = packet.HasAllowIosHighres && !packet.AllowIosHighres;
		netCacheFeatures.AllowOfflineClientActivity = packet.HasAllowOfflineClientActivityDesktop && packet.AllowOfflineClientActivityDesktop;
		netCacheFeatures.EnableSmartDeckCompletion = packet.HasEnableSmartDeckCompletion && packet.EnableSmartDeckCompletion;
		netCacheFeatures.AllowOfflineClientDeckDeletion = packet.HasAllowOfflineClientDeckDeletion && packet.AllowOfflineClientDeckDeletion;
		netCacheFeatures.BattlegroundsEarlyAccessLicense = (packet.HasBattlegroundsEarlyAccessLicense ? packet.BattlegroundsEarlyAccessLicense : 0);
		netCacheFeatures.BattlegroundsMaxRankedPartySize = (packet.HasBattlegroundsMaxRankedPartySize ? packet.BattlegroundsMaxRankedPartySize : PartyManager.BATTLEGROUNDS_MAX_RANKED_PARTY_SIZE_FALLBACK);
		netCacheFeatures.JournalButtonDisabled = packet.JournalButtonDisabled;
		netCacheFeatures.AchievementToastDisabled = packet.AchievementToastDisabled;
		netCacheFeatures.DuelsEarlyAccessLicense = (packet.HasDuelsEarlyAccessLicense ? packet.DuelsEarlyAccessLicense : 0u);
		netCacheFeatures.ContentstackEnabled = !packet.HasContentstackEnabled || packet.ContentstackEnabled;
		netCacheFeatures.PersonalizedMessagesEnabled = !packet.HasPersonalizeMessagesEnabled || packet.PersonalizeMessagesEnabled;
		netCacheFeatures.AppRatingEnabled = !packet.HasAppRatingEnabled || packet.AppRatingEnabled;
		netCacheFeatures.AppRatingSamplingPercentage = packet.AppRatingSamplingPercentage;
		netCacheFeatures.DuelsCardDenylist = packet.DuelsCardDenylist;
		netCacheFeatures.ConstructedCardDenylist = packet.ConstructedCardDenylist;
		netCacheFeatures.SideboardCardDenylist = packet.SideboardCardDenylist;
		netCacheFeatures.TwistCardDenylist = packet.TwistCardDenylist;
		netCacheFeatures.TwistDeckTemplateDenylist = packet.TwistDeckTemplateDenylist;
		netCacheFeatures.StandardCardDenylist = packet.StandardCardDenylist;
		netCacheFeatures.WildCardDenylist = packet.WildCardDenylist;
		netCacheFeatures.TavernBrawlCardDenylist = packet.TavernBrawlCardDenylist;
		netCacheFeatures.ArenaCardDenylist = packet.ArenaCardDenylist;
		netCacheFeatures.VFXDenylist = packet.VfxDenylist;
		netCacheFeatures.CallSDKInterval = (packet.HasCallsdkInterval ? packet.CallsdkInterval : (-1));
		netCacheFeatures.EnableAllOptionsIDCheck = packet.HasEnableAlloptionsIdCheck && packet.EnableAlloptionsIdCheck;
		netCacheFeatures.ArenaRemovePremiumEnabled = packet.HasEnableRemovePremiumFromArena && packet.EnableRemovePremiumFromArena;
		netCacheFeatures.WinsNeededToUnlockUndergroundArena = (packet.HasWinsToUnlockUnderground ? packet.WinsToUnlockUnderground : (-1));
		netCacheFeatures.EnableArenaPhaseMessageFTUXTracking = !packet.HasEnableArenaPhaseMessageFtuxTracking || packet.EnableArenaPhaseMessageFtuxTracking;
		netCacheFeatures.EnableArenaPhaseMessages = !packet.HasEnableArenaPhaseMessages || packet.EnableArenaPhaseMessages;
		netCacheFeatures.EnableInGameWebview = !packet.HasEnableInGameWebview || packet.EnableInGameWebview;
		netCacheFeatures.TrialCardChangedRequestCollectionCacheJitter = (packet.HasRequestCollectionCacheUpdateOnTrialCardChangeJitterSecs ? packet.RequestCollectionCacheUpdateOnTrialCardChangeJitterSecs : 60f);
		netCacheFeatures.TwistSeasonOverride = packet.TwistSeasonOverride;
		netCacheFeatures.TwistScenarioOverride = packet.TwistScenarioOverride;
		if (packet.TwistHeroOverrideList != null)
		{
			netCacheFeatures.TwistHeroicDeckHeroHealthOverrides = new Dictionary<CardDefinition, int>();
			foreach (TwistHeroOverride twistHeroOverride in packet.TwistHeroOverrideList)
			{
				CardDefinition key = new CardDefinition
				{
					Name = GameUtils.TranslateDbIdToCardId(twistHeroOverride.HeroCardId),
					Premium = TAG_PREMIUM.NORMAL
				};
				netCacheFeatures.TwistHeroicDeckHeroHealthOverrides.Add(key, twistHeroOverride.Health);
			}
		}
		netCacheFeatures.RankedPlayEnableScenarioOverrides = packet.RankedPlayEnableScenarioOverrides;
		netCacheFeatures.BattlegroundsSkinsEnabled = packet.BattlegroundsSkinsEnabled;
		netCacheFeatures.BattlegroundsGuideSkinsEnabled = packet.BattlegroundsGuideSkinsEnabled;
		netCacheFeatures.BattlegroundsBoardSkinsEnabled = packet.BattlegroundsBoardSkinsEnabled;
		netCacheFeatures.BattlegroundsFinishersEnabled = packet.BattlegroundsFinishersEnabled;
		netCacheFeatures.BattlegroundsEmotesEnabled = packet.BattlegroundsEmotesEnabled;
		netCacheFeatures.BattlegroundsRewardTrackEnabled = packet.BattlegroundsRewardTrackEnabled;
		switch (PlatformSettings.OS)
		{
		case OSCategory.iOS:
			netCacheFeatures.TutorialPreviewVideosEnabled = packet.HasTutorialPreviewVideosEnabledIos && packet.TutorialPreviewVideosEnabledIos;
			break;
		case OSCategory.Android:
			netCacheFeatures.TutorialPreviewVideosEnabled = packet.HasTutorialPreviewVideosEnabledAndroid && packet.TutorialPreviewVideosEnabledAndroid;
			break;
		case OSCategory.PC:
		case OSCategory.Mac:
			netCacheFeatures.TutorialPreviewVideosEnabled = packet.HasTutorialPreviewVideosEnabledDesktop && packet.TutorialPreviewVideosEnabledDesktop;
			break;
		}
		netCacheFeatures.TutorialPreviewVideosTimeout = (packet.HasTutorialPreviewVideosTimeout ? packet.TutorialPreviewVideosTimeout : NetCacheFeatures.Defaults.TutorialPreviewVideosTimeout);
		netCacheFeatures.SkippableTutorialEnabled = packet.HasSkippableTutorialEnabled && packet.SkippableTutorialEnabled;
		netCacheFeatures.MinHPForProgressAfterConcede = (packet.HasMinHpForProgressAfterConcede ? packet.MinHpForProgressAfterConcede : 0);
		netCacheFeatures.MinTurnsForProgressAfterConcede = (packet.HasMinTurnsForProgressAfterConcede ? packet.MinTurnsForProgressAfterConcede : 0);
		netCacheFeatures.BGDuosLeaverRatingPenalty = (packet.HasBgDuosLeaverRatingPenalty ? packet.BgDuosLeaverRatingPenalty : 0f);
		netCacheFeatures.BGMinTurnsForProgressAfterConcede = (packet.HasBgMinTurnsForProgressAfterConcede ? packet.BgMinTurnsForProgressAfterConcede : 0);
		netCacheFeatures.BGCombatSpeedDisabled = packet.HasBgCombatSpeedDisabled && packet.BgCombatSpeedDisabled;
		netCacheFeatures.BGGuideDisableStatePC = (packet.HasBgGuideDisabledStatePc ? packet.BgGuideDisabledStatePc : 0);
		netCacheFeatures.BGGuideDisableStateMobile = (packet.HasBgGuideDisabledStateMobile ? packet.BgGuideDisabledStateMobile : 0);
		netCacheFeatures.EnablePlayingFromMiniHand = packet.HasEnablePlayFromMiniHand && packet.EnablePlayFromMiniHand;
		netCacheFeatures.EnableUpgradeToGolden = packet.HasUpgradeToGoldenEnabled && packet.UpgradeToGoldenEnabled;
		netCacheFeatures.ShouldPrevalidatePastedDeckCodes = packet.HasPrevalidatePastedDeckCodesOnClient && packet.PrevalidatePastedDeckCodesOnClient;
		netCacheFeatures.EnableClickToFixDeck = packet.HasClickToFixDeckEnabled && packet.ClickToFixDeckEnabled;
		netCacheFeatures.LegacyCardValueCacheEnabled = packet.HasLegacyCachedCardValuesEnabled && packet.LegacyCachedCardValuesEnabled;
		if (netCacheFeatures.LegacyCardValueCacheEnabled)
		{
			Get().RefreshNetObject<NetCacheCardValues>();
		}
		netCacheFeatures.OvercappedDecksEnabled = packet.HasOvercappedDecksEnabled && packet.OvercappedDecksEnabled;
		netCacheFeatures.ReportPlayerEnabled = packet.HasReportPlayerEnabled && packet.ReportPlayerEnabled;
		netCacheFeatures.LuckyDrawEnabled = packet.HasLuckyDrawEnabled && packet.LuckyDrawEnabled;
		netCacheFeatures.BattlenetBillingFlowDisableOverride = packet.HasBattlenetBillingFlowDisableOverride && packet.BattlenetBillingFlowDisableOverride;
		netCacheFeatures.BattlegroundsLuckyDrawDisabledCountryCode = (packet.HasBattlegroundsLuckyDrawDisabledCountryCode ? packet.BattlegroundsLuckyDrawDisabledCountryCode : "");
		netCacheFeatures.ContinuousQuickOpenEnabled = packet.HasContinuousQuickOpenEnabled && packet.ContinuousQuickOpenEnabled;
		netCacheFeatures.ShopButtonOnPackOpeningScreenEnabled = packet.HasShopButtonOnPackOpeningScreenEnabled && packet.ShopButtonOnPackOpeningScreenEnabled;
		netCacheFeatures.MassPackOpeningEnabled = packet.HasMassPackOpeningEnabled && packet.MassPackOpeningEnabled;
		netCacheFeatures.MassPackOpeningPackLimit = (packet.HasMassPackOpeningPackLimit ? packet.MassPackOpeningPackLimit : 0);
		netCacheFeatures.MassPackOpeningGoldenPackLimit = (packet.HasMassPackOpeningGoldenPackLimit ? packet.MassPackOpeningGoldenPackLimit : 0);
		netCacheFeatures.MassPackOpeningHooverChunkSize = (packet.HasMassPackOpeningHooverChunkSize ? packet.MassPackOpeningHooverChunkSize : 0);
		netCacheFeatures.MassCatchupPackOpeningEnabled = packet.HasMassCatchupPackOpeningEnabled && packet.MassCatchupPackOpeningEnabled;
		netCacheFeatures.MassCatchupPackOpeningPackLimit = (packet.HasMassCatchupPackOpeningPackLimit ? packet.MassCatchupPackOpeningPackLimit : 0);
		netCacheFeatures.MercenariesEnableVillages = packet.HasMercenariesEnableVillage && packet.MercenariesEnableVillage;
		netCacheFeatures.MercenariesPackOpeningEnabled = packet.HasMercenariesPackOpeningEnabled && packet.MercenariesPackOpeningEnabled;
		netCacheFeatures.Mercenaries.FullyUpgradedStatBoostAttack = (packet.HasMercenariesFullyUpgradedStatBoostAttack ? packet.MercenariesFullyUpgradedStatBoostAttack : 0);
		netCacheFeatures.Mercenaries.FullyUpgradedStatBoostHealth = (packet.HasMercenariesFullyUpgradedStatBoostHealth ? packet.MercenariesFullyUpgradedStatBoostHealth : 0);
		netCacheFeatures.Mercenaries.AttackBoostPerMythicLevel = (packet.HasMercenariesAttackBoostPerMythicLevel ? packet.MercenariesAttackBoostPerMythicLevel : 0f);
		netCacheFeatures.Mercenaries.HealthBoostPerMythicLevel = (packet.HasMercenariesHealthBoostPerMythicLevel ? packet.MercenariesHealthBoostPerMythicLevel : 0f);
		netCacheFeatures.MercenariesTeamMaxSize = (packet.HasMercenariesMaxTeamSize ? packet.MercenariesMaxTeamSize : 6);
		netCacheFeatures.Mercenaries.MythicAbilityRenownScaleAssetId = ((!packet.HasMercenariesAbilityRenownCostScaleAssetId) ? 1 : packet.MercenariesAbilityRenownCostScaleAssetId);
		netCacheFeatures.Mercenaries.MythicEquipmentRenownScaleAssetId = ((!packet.HasMercenariesEquipmentRenownCostScaleAssetId) ? 1 : packet.MercenariesEquipmentRenownCostScaleAssetId);
		netCacheFeatures.Mercenaries.MythicTreasureRenownScaleAssetId = ((!packet.HasMercenariesTreasureRenownCostScaleAssetId) ? 1 : packet.MercenariesTreasureRenownCostScaleAssetId);
		netCacheFeatures.TracerouteEnabled = !packet.HasTracerouteEnabled || packet.TracerouteEnabled;
		netCacheFeatures.Traceroute.MaxHops = (packet.HasTracerouteMaxHops ? packet.TracerouteMaxHops : 30);
		netCacheFeatures.Traceroute.MessageSize = (packet.HasTracerouteMessageSize ? packet.TracerouteMessageSize : 32);
		netCacheFeatures.Traceroute.MaxRetries = (packet.HasTracerouteMaxRetries ? packet.TracerouteMaxRetries : 3);
		netCacheFeatures.Traceroute.TimeoutMs = (packet.HasTracerouteTimeoutMs ? packet.TracerouteTimeoutMs : 3000);
		netCacheFeatures.Traceroute.ResolveHost = packet.HasTracerouteResolveHost && packet.TracerouteResolveHost;
		netCacheFeatures.BattlegroundsMedalFriendListDisplayEnabled = packet.HasBattlegroundsMedalFriendListDisplayEnabled && packet.BattlegroundsMedalFriendListDisplayEnabled;
		netCacheFeatures.RecentFriendListDisplayEnabled = packet.HasRecentFriendListDisplayEnabled && packet.RecentFriendListDisplayEnabled;
		netCacheFeatures.EnableNDERerollSpecialCases = packet.HasNdeRerollSpecialCasesEnabled && packet.HasNdeRerollSpecialCasesEnabled;
		netCacheFeatures.CardValueOverrideServerRegion = (packet.HasCardValueOverrideServerRegion ? packet.CardValueOverrideServerRegion : 0);
		netCacheFeatures.ReturningPlayer.LoginCountNoticeSupressionEnabled = packet.ReturningPlayerLoginCountNotificationSuppressEnabled;
		netCacheFeatures.ReturningPlayer.NoticeSuppressionLoginThreshold = packet.ReturningPlayerLoginCountNotificationSuppressThreshold;
		netCacheFeatures.PetSkinsHSEnabled = true;
		netCacheFeatures.PetSkinsBGEnabled = true;
		netCacheFeatures.MaxPetTreatsAllowedPerGame = packet.MaxPetTreatsAllowedPerGame;
		netCacheFeatures.PetSkinsDenylist = packet.PetSkinsDenylist;
		netCacheFeatures.PetEventDenylist = packet.PetEventDenylist;
		netCacheFeatures.DebugLuckyDrawIgnoredEventTimings = packet.LuckyDrawIgnoredEventTimings;
		OnNetCacheObjReceived(netCacheFeatures);
	}

	public void OnCurrencyState(GameCurrencyStates currencyState)
	{
		if (!currencyState.HasCurrencyVersion || m_currencyVersion > currencyState.CurrencyVersion)
		{
			Log.Net.PrintDebug("Ignoring currency state: {0}, (cached currency version: {1})", currencyState.ToHumanReadableString(), m_currencyVersion);
			return;
		}
		Log.Net.PrintDebug("Caching currency state: {0}", currencyState.ToHumanReadableString());
		m_currencyVersion = currencyState.CurrencyVersion;
		if (currencyState.HasArcaneDustBalance)
		{
			NetCacheArcaneDustBalance netCacheArcaneDustBalance = GetNetObject<NetCacheArcaneDustBalance>();
			if (netCacheArcaneDustBalance == null)
			{
				netCacheArcaneDustBalance = new NetCacheArcaneDustBalance();
			}
			netCacheArcaneDustBalance.Balance = currencyState.ArcaneDustBalance;
			OnNetCacheObjReceived(netCacheArcaneDustBalance);
		}
		if (currencyState.HasCappedGoldBalance && currencyState.HasBonusGoldBalance)
		{
			NetCacheGoldBalance netCacheGoldBalance = GetNetObject<NetCacheGoldBalance>();
			if (netCacheGoldBalance == null)
			{
				netCacheGoldBalance = new NetCacheGoldBalance();
			}
			netCacheGoldBalance.CappedBalance = currencyState.CappedGoldBalance;
			netCacheGoldBalance.BonusBalance = currencyState.BonusGoldBalance;
			OnNetCacheObjReceived(netCacheGoldBalance);
			foreach (DelGoldBalanceListener goldBalanceListener in m_goldBalanceListeners)
			{
				goldBalanceListener(netCacheGoldBalance);
			}
		}
		if (currencyState.HasRenownBalance)
		{
			NetCacheRenownBalance netCacheRenownBalance = GetNetObject<NetCacheRenownBalance>();
			if (netCacheRenownBalance == null)
			{
				netCacheRenownBalance = new NetCacheRenownBalance();
			}
			netCacheRenownBalance.Balance = currencyState.RenownBalance;
			OnNetCacheObjReceived(netCacheRenownBalance);
			foreach (DelRenownBalanceListener renownBalanceListener in m_renownBalanceListeners)
			{
				renownBalanceListener(netCacheRenownBalance);
			}
		}
		if (currencyState.HasBgTokenBalance)
		{
			NetCacheBattlegroundsTokenBalance netCacheBattlegroundsTokenBalance = GetNetObject<NetCacheBattlegroundsTokenBalance>();
			if (netCacheBattlegroundsTokenBalance == null)
			{
				netCacheBattlegroundsTokenBalance = new NetCacheBattlegroundsTokenBalance();
			}
			netCacheBattlegroundsTokenBalance.Balance = currencyState.BgTokenBalance;
			OnNetCacheObjReceived(netCacheBattlegroundsTokenBalance);
			foreach (DelBattlegroundsTokenBalanceListener battlegroundsTokenBalanceListener in m_battlegroundsTokenBalanceListeners)
			{
				battlegroundsTokenBalanceListener(netCacheBattlegroundsTokenBalance);
			}
		}
		if (ServiceManager.TryGet<CurrencyManager>(out var service))
		{
			service.RefreshWallet();
		}
	}

	public void OnBoosterModifications(BoosterModifications packet)
	{
		NetCacheBoosters netObject = Get().GetNetObject<NetCacheBoosters>();
		if (netObject == null)
		{
			return;
		}
		foreach (BoosterInfo modification in packet.Modifications)
		{
			BoosterStack boosterStack = netObject.GetBoosterStack(modification.Type);
			if (boosterStack == null)
			{
				boosterStack = new BoosterStack
				{
					Id = modification.Type
				};
				netObject.BoosterStacks.Add(boosterStack);
			}
			if (modification.Count > 0)
			{
				boosterStack.EverGrantedCount += modification.EverGrantedCount;
			}
			boosterStack.Count += modification.Count;
		}
		OnNetCacheObjReceived(netObject);
	}

	public bool AddExpectedCollectionModification(long version)
	{
		if (!m_handledCardModifications.Contains(version))
		{
			m_expectedCardModifications.Add(version);
			return true;
		}
		return false;
	}

	public void OnCollectionModification(ClientStateNotification packet)
	{
		CollectionModifications collectionModifications = packet.CollectionModifications;
		if (m_handledCardModifications.Contains(collectionModifications.CollectionVersion) || m_initialCollectionVersion >= collectionModifications.CollectionVersion)
		{
			Log.Net.PrintDebug("Ignoring redundant coolection modification (modification was v.{0}; we are v.{1})", collectionModifications.CollectionVersion, Math.Max(m_handledCardModifications.DefaultIfEmpty(0L).Max(), m_initialCollectionVersion));
			return;
		}
		OnCollectionModificationInternal(collectionModifications);
		if (packet.HasAchievementNotifications)
		{
			AchieveManager.Get().OnAchievementNotifications(packet.AchievementNotifications.AchievementNotifications_);
		}
		if (packet.HasNoticeNotifications)
		{
			Network.Get().OnNoticeNotifications(packet.NoticeNotifications);
		}
		if (packet.HasBoosterModifications)
		{
			OnBoosterModifications(packet.BoosterModifications);
		}
		if (collectionModifications.CardModifications.Count <= 0)
		{
			return;
		}
		if (CollectionManager.Get().GetCollectibleDisplay() != null && CollectionManager.Get().GetCollectibleDisplay().GetPageManager() != null)
		{
			CollectionManager.Get().GetCollectibleDisplay().GetPageManager()
				.RefreshCurrentPageContents();
			CollectionManager.Get().GetCollectibleDisplay().UpdateCurrentPageCardLocks();
		}
		if (CraftingManager.IsInitialized)
		{
			CraftingManager craftingManager = CraftingManager.Get();
			if (craftingManager != null && craftingManager.m_craftingUI != null && craftingManager.m_craftingUI.IsEnabled())
			{
				craftingManager.m_craftingUI.UpdateCraftingButtonsAndSoulboundText();
			}
		}
	}

	private void OnCollectionModificationInternal(CollectionModifications packet)
	{
		m_handledCardModifications.Add(packet.CollectionVersion);
		m_expectedCardModifications.Remove(packet.CollectionVersion);
		List<CardModification> cardModifications = packet.CardModifications;
		List<CollectionManager.CardModification> list = new List<CollectionManager.CardModification>();
		foreach (CardModification item in cardModifications)
		{
			Log.Net.PrintDebug("Handling card collection modification (collection version {0}): {1}", packet.CollectionVersion, item.ToHumanReadableString());
			string cardID = GameUtils.TranslateDbIdToCardId(item.AssetCardId);
			if (item.Quantity < 0)
			{
				list.Add(new CollectionManager.CardModification
				{
					modificationType = CollectionManager.CardModification.ModificationType.Remove,
					cardID = cardID,
					premium = (TAG_PREMIUM)item.Premium,
					count = -1 * item.Quantity
				});
			}
			else if (item.Quantity > 0)
			{
				int num = 0;
				int num2 = Math.Min(item.AmountSeen, item.Quantity);
				if (item.AmountSeen > 0)
				{
					list.Add(new CollectionManager.CardModification
					{
						modificationType = CollectionManager.CardModification.ModificationType.Add,
						cardID = cardID,
						premium = (TAG_PREMIUM)item.Premium,
						count = num2,
						seenBefore = true
					});
					num = num2;
				}
				int num3 = item.Quantity - num;
				if (num3 > 0)
				{
					list.Add(new CollectionManager.CardModification
					{
						modificationType = CollectionManager.CardModification.ModificationType.Add,
						cardID = cardID,
						premium = (TAG_PREMIUM)item.Premium,
						count = num3,
						seenBefore = false
					});
				}
			}
		}
		CollectionManager.Get().OnCardsModified(list);
		RewardTrackManager.Get().OnCosmeticCollectionModification();
	}

	public void OnCardBackModifications(CardBackModifications packet)
	{
		NetCacheCardBacks netObject = GetNetObject<NetCacheCardBacks>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.OnCardBackModifications(): trying to access NetCacheCardBacks before it's been loaded");
			return;
		}
		bool flag = false;
		foreach (CardBackModification item in packet.CardBackModifications_)
		{
			if (netObject.CardBacks.Add(item.AssetCardBackId))
			{
				flag = true;
			}
			if (item.HasAutoSetAsFavorite && item.AutoSetAsFavorite)
			{
				ProcessNewFavoriteCardBack(item.AssetCardBackId);
			}
		}
		if (flag && this.CardBacksChanged != null)
		{
			this.CardBacksChanged();
		}
	}

	public void OnBattlegroundsGuideSkinModifications(BattlegroundsGuideSkinModifications packet)
	{
		NetCacheBattlegroundsGuideSkins netObject = GetNetObject<NetCacheBattlegroundsGuideSkins>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.OnBattlegroundsGuideSkinModifications(): trying to access NetCacheBattlegroundsGuideSkins before it has been loaded.");
			return;
		}
		bool flag = false;
		foreach (BattlegroundsGuideSkinModification item in packet.BattlegroundsGuideSkinModifications_)
		{
			if (!item.HasBattlegroundsGuideSkinId)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsGuideSkinModifications(): received BattlegroundsGuideSkinModification message has no BattlegroundsGuideSkinId.");
				continue;
			}
			BattlegroundsGuideSkinId? battlegroundsGuideSkinId = BattlegroundsGuideSkinId.FromUntrustedValue(item.BattlegroundsGuideSkinId);
			if (!battlegroundsGuideSkinId.HasValue)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsGuideSkinModifications(): received BattlegroundsGuideSkinModification message has invalid BattlegroundsGuideSkinId.");
			}
			else if (item.HasAddBattlegroundsGuideSkin && item.AddBattlegroundsGuideSkin)
			{
				netObject.Owned.Add(battlegroundsGuideSkinId.Value);
				if (item.HasAutoSetAsFavorite && item.AutoSetAsFavorite)
				{
					ProcessNewFavoriteBattlegroundsGuide(battlegroundsGuideSkinId.Value);
				}
				netObject.Unseen.Add(battlegroundsGuideSkinId.Value);
				flag = true;
			}
			else if (item.HasRemoveBattlegroundsGuideSkin && item.RemoveBattlegroundsGuideSkin)
			{
				netObject.Owned.Remove(battlegroundsGuideSkinId.Value);
				netObject.Unseen.Remove(battlegroundsGuideSkinId.Value);
				flag = true;
			}
		}
		if (flag && this.OwnedBattlegroundsSkinsChanged != null)
		{
			this.OwnedBattlegroundsSkinsChanged();
		}
	}

	public void OnBattlegroundsHeroSkinModifications(BattlegroundsHeroSkinModifications packet)
	{
		NetCacheBattlegroundsHeroSkins netObject = GetNetObject<NetCacheBattlegroundsHeroSkins>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.OnBattlegroundsHeroSkinModifications(): trying to access NetCacheBattlegroundsHeroSkins before it has been loaded.");
			return;
		}
		bool flag = false;
		foreach (BattlegroundsHeroSkinModification item in packet.BattlegroundsHeroSkinModifications_)
		{
			if (!item.HasBattlegroundsHeroSkinId)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsHeroSkinModifications(): received BattlegroundsHeroSkinModification message has no HasBattlegroundsHeroSkinId.");
				continue;
			}
			if (item.BattlegroundsHeroSkinId == BaconHeroSkinUtils.SKIN_ID_FOR_FAVORITED_BASE_HERO)
			{
				Debug.LogWarning($"NetCache.OnBattlegroundsHeroSkinModifications(): Id for BaseHero (ID: {BaconHeroSkinUtils.SKIN_ID_FOR_FAVORITED_BASE_HERO}) is an invalid value ");
				continue;
			}
			BattlegroundsHeroSkinId? battlegroundsHeroSkinId = BattlegroundsHeroSkinId.FromUntrustedValue(item.BattlegroundsHeroSkinId);
			if (!battlegroundsHeroSkinId.HasValue)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsHeroSkinModifications(): received BattlegroundsHeroSkinModification message has invalid HasBattlegroundsHeroSkinId.");
			}
			else if (item.HasAddBattlegroundsHeroSkin && item.AddBattlegroundsHeroSkin)
			{
				netObject.OwnedBattlegroundsSkins.Add(battlegroundsHeroSkinId.Value);
				if (item.HasAutoSetAsFavorite && item.AutoSetAsFavorite)
				{
					ProcessNewFavoriteBattlegroundsHeroSkin(battlegroundsHeroSkinId.Value);
				}
				netObject.UnseenSkinIds.Add(battlegroundsHeroSkinId.Value);
				flag = true;
			}
			else if (item.HasRemoveBattlegroundsHeroSkin && item.RemoveBattlegroundsHeroSkin)
			{
				netObject.OwnedBattlegroundsSkins.Remove(battlegroundsHeroSkinId.Value);
				netObject.UnseenSkinIds.Remove(battlegroundsHeroSkinId.Value);
				flag = true;
			}
		}
		if (flag && this.OwnedBattlegroundsSkinsChanged != null)
		{
			this.OwnedBattlegroundsSkinsChanged();
		}
	}

	public void OnBattlegroundsBoardSkinModifications(BattlegroundsBoardSkinModifications packet)
	{
		NetCacheBattlegroundsBoardSkins netObject = GetNetObject<NetCacheBattlegroundsBoardSkins>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.OnBattlegroundsBoardSkinModifications(): trying to access NetCacheBattlegroundsBoardSkins before it has been loaded.");
			return;
		}
		bool flag = false;
		foreach (BattlegroundsBoardSkinModification item in packet.BattlegroundsBoardSkinModifications_)
		{
			if (!item.HasBattlegroundsBoardSkinId)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsBoardSkinModifications(): received BattlegroundsBoardSkinModification message has no HasBattlegroundsBoardSkinId.");
				continue;
			}
			BattlegroundsBoardSkinId? battlegroundsBoardSkinId = BattlegroundsBoardSkinId.FromUntrustedValue(item.BattlegroundsBoardSkinId);
			if (!battlegroundsBoardSkinId.HasValue || battlegroundsBoardSkinId.Value.IsDefaultBoard())
			{
				Debug.LogWarning("NetCache.OnBattlegroundsBoardSkinModifications(): received BattlegroundsBoardSkinModification message has invalid HasBattlegroundsBoardSkinId.");
			}
			else if (item.HasAddBattlegroundsBoardSkin && item.AddBattlegroundsBoardSkin)
			{
				netObject.OwnedBattlegroundsBoardSkins.Add(battlegroundsBoardSkinId.Value);
				if (item.HasAutoSetAsFavorite && item.AutoSetAsFavorite)
				{
					ProcessNewFavoriteBattlegroundsBoardSkin(battlegroundsBoardSkinId.Value);
				}
				netObject.UnseenSkinIds.Add(battlegroundsBoardSkinId.Value);
				flag = true;
			}
			else if (item.HasRemoveBattlegroundsBoardSkin && item.RemoveBattlegroundsBoardSkin)
			{
				netObject.OwnedBattlegroundsBoardSkins.Remove(battlegroundsBoardSkinId.Value);
				netObject.UnseenSkinIds.Remove(battlegroundsBoardSkinId.Value);
				flag = true;
			}
		}
		if (flag && this.OwnedBattlegroundsSkinsChanged != null)
		{
			this.OwnedBattlegroundsSkinsChanged();
		}
	}

	private void OnSetBattlegroundsEmoteLoadoutResponse()
	{
		SetBattlegroundsEmoteLoadoutResponse setBattlegroundsEmoteLoadoutResponse = Network.Get().GetSetBattlegroundsEmoteLoadoutResponse();
		if (!setBattlegroundsEmoteLoadoutResponse.Success)
		{
			return;
		}
		NetCacheBattlegroundsEmotes netObject = Get().GetNetObject<NetCacheBattlegroundsEmotes>();
		if (netObject == null)
		{
			return;
		}
		Hearthstone.BattlegroundsEmoteLoadout battlegroundsEmoteLoadout = Hearthstone.BattlegroundsEmoteLoadout.MakeFromNetwork(setBattlegroundsEmoteLoadoutResponse.Loadout);
		if (battlegroundsEmoteLoadout != null && battlegroundsEmoteLoadout != netObject.CurrentLoadout)
		{
			netObject.CurrentLoadout = battlegroundsEmoteLoadout;
			if (BattlegroundsEmoteLoadoutChangedListener != null)
			{
				BattlegroundsEmoteLoadoutChangedListener(battlegroundsEmoteLoadout);
			}
		}
	}

	public void OnBattlegroundsFinisherModifications(BattlegroundsFinisherModifications packet)
	{
		NetCacheBattlegroundsFinishers netObject = GetNetObject<NetCacheBattlegroundsFinishers>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.OnBattlegroundsFinisherModifications(): trying to access NetCacheBattlegroundsFinishers before it has been loaded.");
			return;
		}
		bool flag = false;
		foreach (BattlegroundsFinisherModification item in packet.BattlegroundsFinisherModifications_)
		{
			if (!item.HasBattlegroundsFinisherId)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsFinisherModifications(): received BattlegroundsFinisherModification message has no HasBattlegroundsFinisherId.");
				continue;
			}
			BattlegroundsFinisherId? battlegroundsFinisherId = BattlegroundsFinisherId.FromUntrustedValue(item.BattlegroundsFinisherId);
			if (!battlegroundsFinisherId.HasValue || battlegroundsFinisherId.Value.IsDefaultFinisher())
			{
				Debug.LogWarning("NetCache.OnBattlegroundsFinisherModifications(): received BattlegroundsFinisherModification message has invalid HasBattlegroundsFinisherId.");
			}
			else if (item.HasAddBattlegroundsFinisher && item.AddBattlegroundsFinisher)
			{
				netObject.OwnedBattlegroundsFinishers.Add(battlegroundsFinisherId.Value);
				if (item.HasAutoSetAsFavorite && item.AutoSetAsFavorite)
				{
					ProcessNewFavoriteBattlegroundsFinisher(battlegroundsFinisherId.Value);
				}
				netObject.UnseenSkinIds.Add(battlegroundsFinisherId.Value);
				flag = true;
			}
			else if (item.HasRemoveBattlegroundsFinisher && item.RemoveBattlegroundsFinisher)
			{
				netObject.OwnedBattlegroundsFinishers.Remove(battlegroundsFinisherId.Value);
				netObject.UnseenSkinIds.Remove(battlegroundsFinisherId.Value);
				flag = true;
			}
		}
		if (flag && this.OwnedBattlegroundsSkinsChanged != null)
		{
			this.OwnedBattlegroundsSkinsChanged();
		}
	}

	private void OnBattlegroundsEmotesResponse()
	{
		BattlegroundsEmotesResponse battlegroundsEmotesResponse = Network.Get().GetBattlegroundsEmotesResponse();
		NetCacheBattlegroundsEmotes netCacheBattlegroundsEmotes = new NetCacheBattlegroundsEmotes();
		foreach (BattlegroundsEmoteInfo ownedEmote in battlegroundsEmotesResponse.OwnedEmotes)
		{
			BattlegroundsEmoteId? battlegroundsEmoteId = BattlegroundsEmoteId.FromUntrustedValue(ownedEmote.EmoteId);
			if (!battlegroundsEmoteId.HasValue)
			{
				Log.Net.PrintError("OnBattlegroundsEmotesResponse FAILED (packetOwnedEmote = {0} due to negative ID)", ownedEmote);
				continue;
			}
			if (battlegroundsEmoteId.Value.IsDefaultEmote())
			{
				Log.Net.PrintError("OnBattlegroundsEmotesResponse FAILED (packetOwnedEmote = {0} due to default)", ownedEmote);
				continue;
			}
			if (!CollectionManager.Get().IsValidBattlegroundsEmoteId(battlegroundsEmoteId.Value))
			{
				Log.Net.PrintError("OnBattlegroundsEmotesResponse FAILED (packetOwnedEmote = {0} due to not present in Hearthedit)", ownedEmote);
				continue;
			}
			netCacheBattlegroundsEmotes.OwnedBattlegroundsEmotes.Add(battlegroundsEmoteId.Value);
			if (!ownedEmote.HasSeen)
			{
				netCacheBattlegroundsEmotes.UnseenEmoteIds.Add(battlegroundsEmoteId.Value);
			}
		}
		Hearthstone.BattlegroundsEmoteLoadout battlegroundsEmoteLoadout = Hearthstone.BattlegroundsEmoteLoadout.MakeFromNetwork(battlegroundsEmotesResponse.Loadout);
		if (battlegroundsEmoteLoadout == null)
		{
			Log.Net.PrintError("OnBattlegroundsEmotesResponse FAILED due to invalid loadout.");
		}
		else
		{
			netCacheBattlegroundsEmotes.CurrentLoadout = battlegroundsEmoteLoadout;
		}
		OnNetCacheObjReceived(netCacheBattlegroundsEmotes);
	}

	public void OnBattlegroundsEmoteModifications(BattlegroundsEmoteModifications packet)
	{
		NetCacheBattlegroundsEmotes netObject = GetNetObject<NetCacheBattlegroundsEmotes>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.OnBattlegroundsEmoteModifications(): trying to access NetCacheBattlegroundsEmotes before it has been loaded.");
			return;
		}
		bool flag = false;
		foreach (BattlegroundsEmoteModification item in packet.BattlegroundsEmoteModifications_)
		{
			if (!item.HasBattlegroundsEmoteId)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsEmoteModifications(): received BattlegroundsEmoteModification message has no HasBattlegroundsEmoteId.");
				continue;
			}
			BattlegroundsEmoteId? battlegroundsEmoteId = BattlegroundsEmoteId.FromUntrustedValue(item.BattlegroundsEmoteId);
			if (!battlegroundsEmoteId.HasValue)
			{
				Debug.LogWarning("NetCache.OnBattlegroundsEmoteModifications(): received BattlegroundsEmoteModification message has invalid HasBattlegroundsEmoteId.");
			}
			else if (item.HasRemoveBattlegroundsEmote && item.AddBattlegroundsEmote)
			{
				netObject.OwnedBattlegroundsEmotes.Add(battlegroundsEmoteId.Value);
				netObject.UnseenEmoteIds.Add(battlegroundsEmoteId.Value);
				flag = true;
			}
			else if (item.HasRemoveBattlegroundsEmote && item.RemoveBattlegroundsEmote)
			{
				netObject.OwnedBattlegroundsEmotes.Remove(battlegroundsEmoteId.Value);
				netObject.UnseenEmoteIds.Remove(battlegroundsEmoteId.Value);
				flag = true;
			}
		}
		if (flag && this.OwnedBattlegroundsSkinsChanged != null)
		{
			this.OwnedBattlegroundsSkinsChanged();
		}
	}

	private void OnPetsResponse()
	{
		Network network = Network.Get();
		PetsResponse petsResponse = network.GetPetsResponse();
		if (petsResponse == null)
		{
			return;
		}
		NetCachePets netCachePets = PetUtility.GeneratePetNetCache(petsResponse);
		OnNetCacheObjReceived(netCachePets);
		OfflineDataCache.OfflineData data = OfflineDataCache.ReadOfflineDataFromFile();
		List<SetFavoritePet> list = OfflineDataCache.GenerateSetFavoritePetBackFromDiff(data, netCachePets);
		if (list != null)
		{
			foreach (SetFavoritePet item in list)
			{
				network.HandleFavoritePetRequest(item);
			}
		}
		OfflineDataCache.ClearPetsDirtyFlag(ref data);
		OfflineDataCache.CachePets(ref data, petsResponse);
		OfflineDataCache.WriteOfflineDataToFile(data);
	}

	public void OnPetModifications(PetModifications packet)
	{
		NetCachePets netObject = GetNetObject<NetCachePets>();
		if (netObject == null)
		{
			Log.Net.PrintWarning("NetCache.OnPetModifications(): trying to access NetCachePets before it has been loaded.");
			return;
		}
		bool flag = false;
		foreach (PetModification item in packet.PetModifications_)
		{
			if (!item.HasModifiedPet)
			{
				continue;
			}
			switch (item.Reason)
			{
			case PetModification.ModificationReason.MOD_ADD:
			{
				int petId3 = item.ModifiedPet.PetId;
				if (!GameDbf.Pet.HasRecord(petId3))
				{
					Log.Net.PrintError("NetCache.OnPetModifications(): trying to add pet that does not exist");
					break;
				}
				if (netObject.Pets.ContainsKey(petId3))
				{
					Log.Net.PrintWarning("NetCache.OnPetModifications(): trying to add pet that already exists");
					break;
				}
				netObject.Pets.Add(petId3, PetInfo.FromProto(item.ModifiedPet));
				flag = true;
				break;
			}
			case PetModification.ModificationReason.MOD_REMOVAL:
			{
				int petId2 = item.ModifiedPet.PetId;
				if (!netObject.Pets.ContainsKey(petId2))
				{
					Log.Net.PrintWarning("NetCache.OnPetModifications(): trying to remove pet that does not exist");
					break;
				}
				netObject.Pets.Remove(petId2);
				flag = true;
				break;
			}
			case PetModification.ModificationReason.MOD_XP_LEVEL:
			{
				int petId = item.ModifiedPet.PetId;
				if (!GameDbf.Pet.HasRecord(petId))
				{
					Log.Net.PrintError("NetCache.OnPetModifications(): trying to update pet that does not exist");
					break;
				}
				if (!netObject.Pets.ContainsKey(petId))
				{
					Log.Net.PrintWarning("NetCache.OnPetModifications(): trying to update pet that the player doesn't own");
					break;
				}
				PetInfo petInfo = netObject.Pets[petId];
				int currentXp = petInfo.CurrentXp;
				int currentLevel = petInfo.CurrentLevel;
				PetInfo petInfo2 = PetInfo.FromProto(item.ModifiedPet);
				int currentXp2 = petInfo2.CurrentXp;
				int currentLevel2 = petInfo2.CurrentLevel;
				netObject.Pets[petId] = petInfo2;
				if (currentXp != currentXp2 || currentLevel != currentLevel2)
				{
					this.PetsXpLevelChanged?.Invoke(petId, currentLevel, petInfo2.CurrentLevel, currentXp, petInfo2.CurrentXp);
					flag = true;
				}
				break;
			}
			default:
				Log.Net.PrintError("NetCache.OnPetModifications(): Unknown modification reason " + item.Reason);
				break;
			}
		}
		foreach (PetModification item2 in packet.PetModifications_)
		{
			if (!item2.HasModifiedPetVariant)
			{
				continue;
			}
			switch (item2.Reason)
			{
			case PetModification.ModificationReason.MOD_ADD:
			{
				int petVariantId2 = item2.ModifiedPetVariant.PetVariantId;
				PetVariantDbfRecord record2 = GameDbf.PetVariant.GetRecord(petVariantId2);
				if (record2 == null)
				{
					Log.Net.PrintError("NetCache.OnPetModifications(): trying to add pet variant that does not exist");
					break;
				}
				if (netObject.PetVariants.ContainsKey(petVariantId2))
				{
					Log.Net.PrintWarning("NetCache.OnPetModifications(): trying to add pet variant that already exists");
					break;
				}
				int petId4 = record2.PetId;
				if (!netObject.Pets.TryGetValue(petId4, out var value2))
				{
					Log.Net.PrintError("NetCache.OnPetModifications(): trying to add pet variant that does not have a parent pet");
					break;
				}
				if (!value2.VariantIDs.Contains(petVariantId2))
				{
					Log.Net.PrintWarning("NetCache.OnPetModifications(): trying to add pet variant that parent pet already knows about. Continuing...");
				}
				value2.VariantIDs.Add(petVariantId2);
				PetVariantInfo value3 = PetVariantInfo.FromProto(item2.ModifiedPetVariant);
				netObject.PetVariants.Add(petVariantId2, value3);
				flag = true;
				break;
			}
			case PetModification.ModificationReason.MOD_REMOVAL:
			{
				int petVariantId = item2.ModifiedPetVariant.PetVariantId;
				if (!netObject.PetVariants.ContainsKey(petVariantId))
				{
					Log.Net.PrintWarning("NetCache.OnPetModifications(): trying to remove pet variant that does not exist");
					break;
				}
				netObject.PetVariants.Remove(petVariantId);
				PetVariantDbfRecord record = GameDbf.PetVariant.GetRecord(petVariantId);
				if (record != null && netObject.Pets.TryGetValue(record.PetId, out var value))
				{
					value.VariantIDs.Remove(petVariantId);
				}
				flag = true;
				break;
			}
			case PetModification.ModificationReason.MOD_XP_LEVEL:
				Log.Net.PrintError("NetCache.OnPetModifications(): Pet variants cannot handle XP_Level changes");
				break;
			default:
				Log.Net.PrintError("NetCache.OnPetModifications(): Unknown modification reason " + item2.Reason);
				break;
			}
		}
		if (flag && OwnedPetsChanged != null)
		{
			OfflineDataCache.OfflineData data = OfflineDataCache.ReadOfflineDataFromFile();
			OfflineDataCache.UpdatePets(ref data, packet);
			OwnedPetsChanged?.Invoke();
		}
	}

	private void OnSetFavoritePetResponse()
	{
		SetFavoritePetResponse setFavoritePetResponse = Network.Get().GetSetFavoritePetResponse();
		if (!setFavoritePetResponse.Success)
		{
			Log.Net.PrintError("OnSetFavoritePetResponse FAILED - Response returned not success");
			return;
		}
		bool? isHsFavorite = null;
		bool? isBgFavorite = null;
		if (setFavoritePetResponse.HasIsHsFavorite)
		{
			isHsFavorite = setFavoritePetResponse.IsHsFavorite;
		}
		if (setFavoritePetResponse.HasIsBgFavorite)
		{
			isBgFavorite = setFavoritePetResponse.IsBgFavorite;
		}
		if (setFavoritePetResponse.HasPetId)
		{
			ProcessFavoritePet(setFavoritePetResponse.PetId, isHsFavorite, isBgFavorite);
		}
		else if (setFavoritePetResponse.HasPetVariantId)
		{
			ProcessFavoritePetVariant(setFavoritePetResponse.PetVariantId, isHsFavorite, isBgFavorite);
		}
		else
		{
			Log.Net.PrintError("OnSetFavoritePetResponse FAILED - no pet or variant set");
		}
	}

	public void ProcessFavoritePet(int petId, bool? isHsFavorite, bool? isBgFavorite)
	{
		NetCachePets netObject = GetNetObject<NetCachePets>();
		if (netObject == null)
		{
			Log.Net.PrintError("ProcessFavoritePet FAILED - no pet cache");
			return;
		}
		if (!netObject.Pets.TryGetValue(petId, out var value))
		{
			Log.Net.PrintError($"ProcessFavoritePet FAILED - no pet with id {petId}");
			return;
		}
		bool flag = false;
		if (isHsFavorite.HasValue && isHsFavorite.Value != value.IsHsFavorite)
		{
			value.IsHsFavorite = isHsFavorite.Value;
			flag = true;
		}
		if (isBgFavorite.HasValue && isBgFavorite.Value != value.IsBgFavorite)
		{
			value.IsBgFavorite = isBgFavorite.Value;
			flag = true;
		}
		if (flag)
		{
			this.FavoritePetChanged?.Invoke(value.PetId, value.IsHsFavorite, value.IsBgFavorite);
		}
	}

	public void ProcessFavoritePetVariant(int petVariantId, bool? isHsFavorite, bool? isBgFavorite)
	{
		NetCachePets netObject = GetNetObject<NetCachePets>();
		if (netObject == null)
		{
			Log.Net.PrintError("ProcessFavoritePetVariant FAILED - no pet cache");
			return;
		}
		if (!netObject.PetVariants.TryGetValue(petVariantId, out var value))
		{
			Log.Net.PrintError($"ProcessFavoritePetVariant FAILED - no pet variant with id {petVariantId}");
			return;
		}
		bool flag = false;
		if (isHsFavorite.HasValue && isHsFavorite.Value != value.IsHsFavorite)
		{
			value.IsHsFavorite = isHsFavorite.Value;
			flag = true;
		}
		if (isBgFavorite.HasValue && isBgFavorite.Value != value.IsBgFavorite)
		{
			value.IsBgFavorite = isBgFavorite.Value;
			flag = true;
		}
		if (flag)
		{
			this.FavoritePetVariantChanged?.Invoke(value.PetVariantId, value.IsHsFavorite, value.IsBgFavorite);
		}
	}

	private void OnSetFavoriteCardBackResponse()
	{
		Network.CardBackResponse cardBackResponse = Network.Get().GetCardBackResponse();
		if (!cardBackResponse.Success)
		{
			Log.CardbackMgr.PrintError("SetFavoriteCardBack FAILED (cardBack = {0})", cardBackResponse.CardBack);
		}
		else
		{
			ProcessNewFavoriteCardBack(cardBackResponse.CardBack, cardBackResponse.IsFavorite);
		}
	}

	public void ProcessNewFavoriteCardBack(int cardBackId, bool isFavorite = true)
	{
		NetCacheCardBacks netObject = GetNetObject<NetCacheCardBacks>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.ProcessNewFavoriteCardBack(): trying to access NetCacheCardBacks before it's been loaded");
			return;
		}
		if (isFavorite)
		{
			netObject.FavoriteCardBacks.Add(cardBackId);
		}
		else
		{
			netObject.FavoriteCardBacks.Remove(cardBackId);
		}
		if (this.FavoriteCardBackChanged != null)
		{
			this.FavoriteCardBackChanged(cardBackId, isFavorite);
		}
	}

	public void ProcessNewFavoriteBattlegroundsGuide(BattlegroundsGuideSkinId newGuideID)
	{
		NetCacheBattlegroundsGuideSkins netObject = GetNetObject<NetCacheBattlegroundsGuideSkins>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.ProcessNewFavoriteBattlegroundsGuide(): trying to access ProcessNewFavoriteBattlegroundsGuide before it has been loaded.");
		}
		else if (!netObject.Favorites.Contains(newGuideID))
		{
			netObject.Favorites.Add(newGuideID);
			if (this.FavoriteBattlegroundsGuideSkinChanged != null)
			{
				this.FavoriteBattlegroundsGuideSkinChanged(newGuideID);
			}
		}
	}

	public void ProcessNewFavoriteBattlegroundsHeroSkin(BattlegroundsHeroSkinId newBattlegroundsHeroSkinId)
	{
		if (!newBattlegroundsHeroSkinId.IsValid())
		{
			Debug.LogWarning("NetCache.ProcessNewFavoriteBattlegroundsHeroSkin(): Invalid BattlegroundsHeroSkinId");
			return;
		}
		string skinOrBaseCardId = GameUtils.TranslateDbIdToCardId(newBattlegroundsHeroSkinId.ToValue());
		int num = GameUtils.TranslateCardIdToDbId(CollectionManager.Get().GetBattlegroundsBaseHeroCardId(skinOrBaseCardId));
		NetCacheBattlegroundsHeroSkins netObject = GetNetObject<NetCacheBattlegroundsHeroSkins>();
		netObject.BattlegroundsFavoriteHeroSkins.TryGetValue(num, out var value);
		if (value == null)
		{
			value = new HashSet<int>();
			netObject.BattlegroundsFavoriteHeroSkins.Add(num, value);
		}
		value.Add(newBattlegroundsHeroSkinId.ToValue());
		if (this.FavoriteBattlegroundsHeroSkinChanged != null)
		{
			this.FavoriteBattlegroundsHeroSkinChanged(num, newBattlegroundsHeroSkinId.ToValue());
		}
	}

	public void ProcessNewFavoriteBattlegroundsBoardSkin(BattlegroundsBoardSkinId newFavoriteBattlegroundsBoardSkinID)
	{
		NetCacheBattlegroundsBoardSkins netObject = GetNetObject<NetCacheBattlegroundsBoardSkins>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.ProcessNewFavoriteBattlegroundsBoardSkin(): trying to access NetCacheBattlegroundsBoardSkins before it has been loaded.");
		}
		else if (netObject.BattlegroundsFavoriteBoardSkins.Add(newFavoriteBattlegroundsBoardSkinID) && this.FavoriteBattlegroundsBoardSkinChanged != null)
		{
			this.FavoriteBattlegroundsBoardSkinChanged(newFavoriteBattlegroundsBoardSkinID);
		}
	}

	public void ProcessNewFavoriteBattlegroundsFinisher(BattlegroundsFinisherId newFavoriteBattlegroundsFinisherID)
	{
		NetCacheBattlegroundsFinishers netObject = GetNetObject<NetCacheBattlegroundsFinishers>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.ProcessNewFavoriteBattlegroundsFinisher(): trying to access NetCacheBattlegroundsFinishers before it has been loaded.");
		}
		else if (!netObject.BattlegroundsFavoriteFinishers.Contains(newFavoriteBattlegroundsFinisherID))
		{
			netObject.BattlegroundsFavoriteFinishers.Add(newFavoriteBattlegroundsFinisherID);
			if (this.FavoriteBattlegroundsFinisherChanged != null)
			{
				this.FavoriteBattlegroundsFinisherChanged(newFavoriteBattlegroundsFinisherID);
			}
		}
	}

	private void OnSetFavoriteCosmeticCoinResponse()
	{
		Network.CosmeticCoinResponse coinResponse = Network.Get().GetCoinResponse();
		if (!coinResponse.Success)
		{
			Log.Net.PrintError("SetFavoriteCardBack FAILED (coin = {0})", coinResponse.Coin);
		}
		else
		{
			ProcessNewFavoriteCoin(coinResponse.Coin, coinResponse.IsFavorite);
		}
	}

	public void ProcessNewFavoriteCoin(int coinId, bool isFavorite)
	{
		NetCacheCoins netObject = GetNetObject<NetCacheCoins>();
		if (netObject == null)
		{
			Debug.LogWarning($"NetCache.ProcessNewFavoriteCoin(): trying to accessNetCacheCoins before it's been loaded");
			return;
		}
		if (isFavorite)
		{
			netObject.FavoriteCoins.Add(coinId);
		}
		else
		{
			netObject.FavoriteCoins.Remove(coinId);
		}
		if (this.FavoriteCoinChanged != null)
		{
			this.FavoriteCoinChanged(coinId, isFavorite);
		}
	}

	private void OnGamesInfo()
	{
		NetCacheGamesPlayed gamesInfo = Network.Get().GetGamesInfo();
		if (gamesInfo == null)
		{
			Debug.LogWarning("error getting games info");
		}
		else
		{
			OnNetCacheObjReceived(gamesInfo);
		}
	}

	private void OnProfileProgress()
	{
		OnNetCacheObjReceived(Network.Get().GetProfileProgress());
	}

	private void OnHearthstoneUnavailableGame()
	{
		OnHearthstoneUnavailable(gamePacket: true);
	}

	private void OnHearthstoneUnavailableUtil()
	{
		OnHearthstoneUnavailable(gamePacket: false);
	}

	private void OnHearthstoneUnavailable(bool gamePacket)
	{
		Network.UnavailableReason hearthstoneUnavailable = Network.Get().GetHearthstoneUnavailable(gamePacket);
		Debug.Log("Hearthstone Unavailable!  Reason: " + hearthstoneUnavailable.mainReason);
		string mainReason = hearthstoneUnavailable.mainReason;
		if (!(mainReason == "VERSION"))
		{
			if (mainReason == "OFFLINE")
			{
				Network.Get().ShowConnectionFailureError("GLOBAL_ERROR_NETWORK_UNAVAILABLE_OFFLINE");
				return;
			}
			TelemetryManager.Client().SendNetworkError(NetworkError.ErrorType.SERVICE_UNAVAILABLE, $"{hearthstoneUnavailable.mainReason} - {hearthstoneUnavailable.subReason} - {hearthstoneUnavailable.extraData}", 0);
			Network.Get().ShowConnectionFailureError("GLOBAL_ERROR_NETWORK_UNAVAILABLE_UNKNOWN");
			return;
		}
		ErrorParams errorParams = new ErrorParams();
		if (PlatformSettings.IsMobile() && GameDownloadManagerProvider.Get() != null && !GameDownloadManagerProvider.Get().IsNewMobileVersionReleased)
		{
			errorParams.m_message = GameStrings.Format("GLOBAL_ERROR_NETWORK_UNAVAILABLE_NEW_VERSION");
			errorParams.m_reason = FatalErrorReason.UNAVAILABLE_NEW_VERSION;
		}
		else
		{
			errorParams.m_message = GameStrings.Format("GLOBAL_ERROR_NETWORK_UNAVAILABLE_UPGRADE");
			if ((bool)Error.HAS_APP_STORE)
			{
				errorParams.m_redirectToStore = true;
			}
			errorParams.m_reason = FatalErrorReason.UNAVAILABLE_UPGRADE;
		}
		Error.AddFatal(errorParams);
		ReconnectMgr.Get().FullResetRequired = true;
		ReconnectMgr.Get().UpdateRequired = true;
	}

	private void OnCardBacks()
	{
		Network network = Network.Get();
		OnNetCacheObjReceived(network.GetCardBacks());
		CardBacks cardBacksPacket = network.GetCardBacksPacket();
		if (cardBacksPacket == null)
		{
			return;
		}
		List<int> favoriteCardBacks = cardBacksPacket.FavoriteCardBacks;
		OfflineDataCache.OfflineData data = OfflineDataCache.ReadOfflineDataFromFile();
		List<SetFavoriteCardBack> list = OfflineDataCache.GenerateSetFavoriteCardBackFromDiff(data, favoriteCardBacks);
		if (list != null && list.Count > 0)
		{
			foreach (SetFavoriteCardBack item in list)
			{
				network.SetFavoriteCardBack(item.CardBack, item.IsFavorite);
			}
		}
		OfflineDataCache.ClearCardBackDirtyFlag(ref data);
		OfflineDataCache.CacheCardBacks(ref data, cardBacksPacket);
		OfflineDataCache.WriteOfflineDataToFile(data);
	}

	private void OnCoins()
	{
		Network network = Network.Get();
		OnNetCacheObjReceived(network.GetCoins());
		CosmeticCoins coinsPacket = network.GetCoinsPacket();
		if (coinsPacket == null)
		{
			return;
		}
		List<int> favoriteCoins = coinsPacket.FavoriteCoins;
		OfflineDataCache.OfflineData data = OfflineDataCache.ReadOfflineDataFromFile();
		foreach (SetFavoriteCosmeticCoin item in OfflineDataCache.GenerateSetFavoriteCosmeticCoinFromDiff(data, favoriteCoins))
		{
			network.SetFavoriteCosmeticCoin(ref data, item.Coin, item.IsFavorite);
		}
		OfflineDataCache.ClearCoinDirtyFlag(ref data);
		OfflineDataCache.CacheCoins(ref data, coinsPacket);
		OfflineDataCache.WriteOfflineDataToFile(data);
	}

	private void OnBattlegroundsHeroSkinsResponse()
	{
		CollectionManager collectionManager = CollectionManager.Get();
		BattlegroundsHeroSkinsResponse battlegroundsHeroSkinsResponse = Network.Get().GetBattlegroundsHeroSkinsResponse();
		NetCacheBattlegroundsHeroSkins netCacheBattlegroundsHeroSkins = new NetCacheBattlegroundsHeroSkins();
		foreach (BattlegroundsHeroSkinInfo ownedSkin in battlegroundsHeroSkinsResponse.OwnedSkins)
		{
			if (ownedSkin == null)
			{
				Log.Net.PrintError("OnBattlegroundsHeroSkinsResponse FAILED (packetOwnedSkin = null)");
				continue;
			}
			int item = BaconHeroSkinUtils.SKIN_ID_FOR_FAVORITED_BASE_HERO;
			if (ownedSkin.HeroSkinId != BaconHeroSkinUtils.SKIN_ID_FOR_FAVORITED_BASE_HERO)
			{
				BattlegroundsHeroSkinDbfRecord record = GameDbf.BattlegroundsHeroSkin.GetRecord(ownedSkin.HeroSkinId);
				if (record == null)
				{
					Log.Net.PrintError("OnBattlegroundsHeroSkinsResponse FAILED (packetOwnedSkin = {0} battlegroundsHeroSkinDbfRecord = null)", ownedSkin);
					continue;
				}
				if (!record.Enabled)
				{
					continue;
				}
				BattlegroundsHeroSkinId item2 = BattlegroundsHeroSkinId.FromTrustedValue(record.ID);
				if (ownedSkin.HeroSkinId != BaconHeroSkinUtils.SKIN_ID_FOR_FAVORITED_BASE_HERO)
				{
					netCacheBattlegroundsHeroSkins.OwnedBattlegroundsSkins.Add(item2);
				}
				item = record.ID;
				string cardId = GameUtils.TranslateDbIdToCardId(record.SkinCardId);
				if (!collectionManager.IsBattlegroundsHeroCard(cardId))
				{
					Log.Net.PrintError("OnBattlegroundsHeroSkinsResponse FAILED (packetOwnedSkin = {0})", ownedSkin);
					continue;
				}
				if (!ownedSkin.HasSeen)
				{
					netCacheBattlegroundsHeroSkins.UnseenSkinIds.Add(item2);
				}
			}
			if (ownedSkin.IsFavorite)
			{
				netCacheBattlegroundsHeroSkins.BattlegroundsFavoriteHeroSkins.TryGetValue(ownedSkin.BaseHeroCardId, out var value);
				if (value == null)
				{
					value = new HashSet<int>();
					netCacheBattlegroundsHeroSkins.BattlegroundsFavoriteHeroSkins.Add(ownedSkin.BaseHeroCardId, value);
				}
				value.Add(item);
			}
		}
		OnNetCacheObjReceived(netCacheBattlegroundsHeroSkins);
	}

	private void OnSetBattlegroundsFavoriteHeroSkinResponse()
	{
		SetBattlegroundsFavoriteHeroSkinResponse setBattlegroundsFavoriteHeroSkinResponse = Network.Get().GetSetBattlegroundsFavoriteHeroSkinResponse();
		if (!setBattlegroundsFavoriteHeroSkinResponse.Success)
		{
			return;
		}
		int heroSkinId = setBattlegroundsFavoriteHeroSkinResponse.HeroSkinId;
		int baseHeroCardId = setBattlegroundsFavoriteHeroSkinResponse.BaseHeroCardId;
		NetCacheBattlegroundsHeroSkins netObject = GetNetObject<NetCacheBattlegroundsHeroSkins>();
		if (netObject != null)
		{
			netObject.BattlegroundsFavoriteHeroSkins.TryGetValue(baseHeroCardId, out var value);
			if (value == null)
			{
				value = new HashSet<int>();
				netObject.BattlegroundsFavoriteHeroSkins.Add(baseHeroCardId, value);
			}
			int item = heroSkinId;
			value.Add(item);
			if (this.FavoriteBattlegroundsHeroSkinChanged != null)
			{
				this.FavoriteBattlegroundsHeroSkinChanged(baseHeroCardId, heroSkinId);
			}
		}
	}

	private void OnClearBattlegroundsFavoriteHeroSkinResponse()
	{
		ClearBattlegroundsFavoriteHeroSkinResponse clearBattlegroundsFavoriteHeroSkinResponse = Network.Get().GetClearBattlegroundsFavoriteHeroSkinResponse();
		if (!clearBattlegroundsFavoriteHeroSkinResponse.Success)
		{
			return;
		}
		int heroSkinId = clearBattlegroundsFavoriteHeroSkinResponse.HeroSkinId;
		int baseHeroCardId = clearBattlegroundsFavoriteHeroSkinResponse.BaseHeroCardId;
		NetCacheBattlegroundsHeroSkins netObject = GetNetObject<NetCacheBattlegroundsHeroSkins>();
		if (netObject == null)
		{
			return;
		}
		netObject.BattlegroundsFavoriteHeroSkins.TryGetValue(baseHeroCardId, out var value);
		if (value != null)
		{
			int item = heroSkinId;
			value.Remove(item);
			if (this.FavoriteBattlegroundsHeroSkinChanged != null)
			{
				this.FavoriteBattlegroundsHeroSkinChanged(baseHeroCardId, heroSkinId);
			}
		}
	}

	private void OnBattlegroundsGuideSkinsResponse()
	{
		BattlegroundsGuideSkinsResponse battlegroundsGuideSkinsResponse = Network.Get().GetBattlegroundsGuideSkinsResponse();
		NetCacheBattlegroundsGuideSkins netCacheBattlegroundsGuideSkins = new NetCacheBattlegroundsGuideSkins();
		foreach (BattlegroundsGuideSkinInfo ownedSkin in battlegroundsGuideSkinsResponse.OwnedSkins)
		{
			BattlegroundsGuideSkinId? battlegroundsGuideSkinId = BattlegroundsGuideSkinId.FromUntrustedValue(ownedSkin.GuideSkinId);
			if (!battlegroundsGuideSkinId.HasValue)
			{
				Log.Net.PrintError("OnBattlegroundsGuideSkinsResponse FAILED (ownedSkin = {0})", ownedSkin);
				continue;
			}
			if (!CollectionManager.Get().IsValidBattlegroundsGuideSkinId(battlegroundsGuideSkinId.Value))
			{
				Log.Net.PrintError("OnBattlegroundsGuideSkinsResponse FAILED (ownedSkin = {0})", ownedSkin);
				continue;
			}
			netCacheBattlegroundsGuideSkins.Owned.Add(battlegroundsGuideSkinId.Value);
			if (ownedSkin.IsFavorite)
			{
				netCacheBattlegroundsGuideSkins.Favorites.Add(battlegroundsGuideSkinId.Value);
			}
			if (!ownedSkin.HasSeen)
			{
				netCacheBattlegroundsGuideSkins.Unseen.Add(battlegroundsGuideSkinId.Value);
			}
		}
		OnNetCacheObjReceived(netCacheBattlegroundsGuideSkins);
	}

	private void OnSetBattlegroundsFavoriteGuideSkinResponse()
	{
		SetBattlegroundsFavoriteGuideSkinResponse setBattlegroundsFavoriteGuideSkinResponse = Network.Get().GetSetBattlegroundsFavoriteGuideSkinResponse();
		if (setBattlegroundsFavoriteGuideSkinResponse.Success)
		{
			BattlegroundsGuideSkinId? battlegroundsGuideSkinId = BattlegroundsGuideSkinId.FromUntrustedValue(setBattlegroundsFavoriteGuideSkinResponse.GuideSkinId);
			if (!battlegroundsGuideSkinId.HasValue || !CollectionManager.Get().IsValidBattlegroundsGuideSkinId(battlegroundsGuideSkinId.Value))
			{
				Log.Net.PrintError("OnSetBattlegroundsFavoriteGuideSkinResponse FAILED - invalid skin ID (GuideSkinId = {0})", battlegroundsGuideSkinId);
			}
			NetCacheBattlegroundsGuideSkins netObject = Get().GetNetObject<NetCacheBattlegroundsGuideSkins>();
			if (netObject != null && netObject.Favorites.Add(battlegroundsGuideSkinId.Value) && this.FavoriteBattlegroundsGuideSkinChanged != null)
			{
				this.FavoriteBattlegroundsGuideSkinChanged(battlegroundsGuideSkinId);
			}
		}
	}

	private void OnClearBattlegroundsFavoriteGuideSkinResponse()
	{
		ClearBattlegroundsFavoriteGuideSkinResponse clearBattlegroundsFavoriteGuideSkinResponse = Network.Get().GetClearBattlegroundsFavoriteGuideSkinResponse();
		if (clearBattlegroundsFavoriteGuideSkinResponse.Success)
		{
			BattlegroundsGuideSkinId? battlegroundsGuideSkinId = BattlegroundsGuideSkinId.FromUntrustedValue(clearBattlegroundsFavoriteGuideSkinResponse.GuideSkinId);
			if (!battlegroundsGuideSkinId.HasValue || !CollectionManager.Get().IsValidBattlegroundsGuideSkinId(battlegroundsGuideSkinId.Value))
			{
				Log.Net.PrintError("OnSetBattlegroundsFavoriteGuideSkinResponse FAILED - invalid skin ID (GuideSkinId = {0})", battlegroundsGuideSkinId);
			}
			NetCacheBattlegroundsGuideSkins netObject = Get().GetNetObject<NetCacheBattlegroundsGuideSkins>();
			if (netObject != null && netObject.Favorites.Remove(battlegroundsGuideSkinId.Value) && this.FavoriteBattlegroundsGuideSkinChanged != null)
			{
				this.FavoriteBattlegroundsGuideSkinChanged(battlegroundsGuideSkinId);
			}
		}
	}

	private void OnBattlegroundsBoardSkinsResponse()
	{
		BattlegroundsBoardSkinsResponse battlegroundsBoardSkinsResponse = Network.Get().GetBattlegroundsBoardSkinsResponse();
		NetCacheBattlegroundsBoardSkins netCacheBattlegroundsBoardSkins = new NetCacheBattlegroundsBoardSkins
		{
			BattlegroundsFavoriteBoardSkins = new HashSet<BattlegroundsBoardSkinId>()
		};
		foreach (BattlegroundsBoardSkinInfo ownedSkin in battlegroundsBoardSkinsResponse.OwnedSkins)
		{
			BattlegroundsBoardSkinId? battlegroundsBoardSkinId = BattlegroundsBoardSkinId.FromUntrustedValue(ownedSkin.BoardSkinId);
			if (!battlegroundsBoardSkinId.HasValue)
			{
				Log.Net.PrintWarning("NetCache::OnBattlegroundsBoardSkinsResponse BattlegroundsBoardSkinId missing value");
				continue;
			}
			if (!battlegroundsBoardSkinId.Value.IsValid() || !CollectionManager.Get().IsValidBattlegroundsBoardSkinId(battlegroundsBoardSkinId.Value))
			{
				Log.Net.PrintWarning(string.Format("{0}::{1} BattlegroundsBoardSkinId is invalid: {2}", "NetCache", "OnBattlegroundsBoardSkinsResponse", battlegroundsBoardSkinId));
				continue;
			}
			netCacheBattlegroundsBoardSkins.OwnedBattlegroundsBoardSkins.Add(battlegroundsBoardSkinId.Value);
			if (ownedSkin.IsFavorite)
			{
				netCacheBattlegroundsBoardSkins.BattlegroundsFavoriteBoardSkins.Add(battlegroundsBoardSkinId.Value);
			}
			if (!ownedSkin.HasSeen)
			{
				netCacheBattlegroundsBoardSkins.UnseenSkinIds.Add(battlegroundsBoardSkinId.Value);
			}
		}
		OnNetCacheObjReceived(netCacheBattlegroundsBoardSkins);
	}

	private void OnSetBattlegroundsFavoriteBoardSkinResponse()
	{
		SetBattlegroundsFavoriteBoardSkinResponse setBattlegroundsFavoriteBoardSkinResponse = Network.Get().GetSetBattlegroundsFavoriteBoardSkinResponse();
		if (!setBattlegroundsFavoriteBoardSkinResponse.Success)
		{
			return;
		}
		BattlegroundsBoardSkinId? battlegroundsBoardSkinId = BattlegroundsBoardSkinId.FromUntrustedValue(setBattlegroundsFavoriteBoardSkinResponse.BoardSkinId);
		if (!battlegroundsBoardSkinId.HasValue)
		{
			Log.Net.PrintWarning("NetCache::OnSetBattlegroundsFavoriteBoardSkinResponse BattlegroundsBoardSkinId missing value");
			return;
		}
		if (!battlegroundsBoardSkinId.Value.IsValid() || !CollectionManager.Get().IsValidBattlegroundsBoardSkinId(battlegroundsBoardSkinId.Value))
		{
			Log.Net.PrintWarning(string.Format("{0}::{1} BattlegroundsBoardSkinId is invalid: {2}", "NetCache", "OnSetBattlegroundsFavoriteBoardSkinResponse", battlegroundsBoardSkinId));
			return;
		}
		NetCacheBattlegroundsBoardSkins netObject = Get().GetNetObject<NetCacheBattlegroundsBoardSkins>();
		if (netObject != null && netObject.BattlegroundsFavoriteBoardSkins.Add(battlegroundsBoardSkinId.Value) && this.FavoriteBattlegroundsBoardSkinChanged != null)
		{
			this.FavoriteBattlegroundsBoardSkinChanged(battlegroundsBoardSkinId);
		}
	}

	private void OnClearBattlegroundsFavoriteBoardSkinResponse()
	{
		ClearBattlegroundsFavoriteBoardSkinResponse clearBattlegroundsFavoriteBoardSkinResponse = Network.Get().GetClearBattlegroundsFavoriteBoardSkinResponse();
		if (!clearBattlegroundsFavoriteBoardSkinResponse.Success)
		{
			return;
		}
		BattlegroundsBoardSkinId? battlegroundsBoardSkinId = BattlegroundsBoardSkinId.FromUntrustedValue(clearBattlegroundsFavoriteBoardSkinResponse.BoardSkinId);
		if (!battlegroundsBoardSkinId.HasValue)
		{
			Log.Net.PrintWarning("NetCache::OnClearBattlegroundsFavoriteBoardSkinResponse BattlegroundsBoardSkinId missing value");
			return;
		}
		if (!battlegroundsBoardSkinId.Value.IsValid() || !CollectionManager.Get().IsValidBattlegroundsBoardSkinId(battlegroundsBoardSkinId.Value))
		{
			Log.Net.PrintWarning(string.Format("{0}::{1} BattlegroundsBoardSkinId is invalid: {2}", "NetCache", "OnClearBattlegroundsFavoriteBoardSkinResponse", battlegroundsBoardSkinId));
			return;
		}
		NetCacheBattlegroundsBoardSkins netObject = Get().GetNetObject<NetCacheBattlegroundsBoardSkins>();
		if (netObject != null && netObject.BattlegroundsFavoriteBoardSkins.Remove(battlegroundsBoardSkinId.Value) && this.FavoriteBattlegroundsBoardSkinChanged != null)
		{
			this.FavoriteBattlegroundsBoardSkinChanged(battlegroundsBoardSkinId);
		}
	}

	private void OnBattlegroundsFinishersResponse()
	{
		BattlegroundsFinishersResponse battlegroundsFinishersResponse = Network.Get().GetBattlegroundsFinishersResponse();
		NetCacheBattlegroundsFinishers netCacheBattlegroundsFinishers = new NetCacheBattlegroundsFinishers
		{
			BattlegroundsFavoriteFinishers = new HashSet<BattlegroundsFinisherId>()
		};
		foreach (BattlegroundsFinisherInfo ownedSkin in battlegroundsFinishersResponse.OwnedSkins)
		{
			BattlegroundsFinisherId? battlegroundsFinisherId = BattlegroundsFinisherId.FromUntrustedValue(ownedSkin.FinisherId);
			if (!battlegroundsFinisherId.HasValue)
			{
				Log.Net.PrintError("OnBattlegroundsFinishersResponse FAILED (packetOwnedSkin = {0})", ownedSkin);
				continue;
			}
			if (!CollectionManager.Get().IsValidBattlegroundsFinisherId(battlegroundsFinisherId.Value))
			{
				Log.Net.PrintError("OnBattlegroundsFinishersResponse FAILED (packetOwnedSkin = {0})", ownedSkin);
				continue;
			}
			netCacheBattlegroundsFinishers.OwnedBattlegroundsFinishers.Add(battlegroundsFinisherId.Value);
			if (ownedSkin.IsFavorite)
			{
				netCacheBattlegroundsFinishers.BattlegroundsFavoriteFinishers.Add(battlegroundsFinisherId.Value);
			}
			if (!ownedSkin.HasSeen)
			{
				netCacheBattlegroundsFinishers.UnseenSkinIds.Add(battlegroundsFinisherId.Value);
			}
		}
		OnNetCacheObjReceived(netCacheBattlegroundsFinishers);
	}

	private void OnSetBattlegroundsFavoriteFinisherResponse()
	{
		SetBattlegroundsFavoriteFinisherResponse setBattlegroundsFavoriteFinisherResponse = Network.Get().GetSetBattlegroundsFavoriteFinisherResponse();
		if (!setBattlegroundsFavoriteFinisherResponse.Success)
		{
			return;
		}
		BattlegroundsFinisherId? battlegroundsFinisherId = BattlegroundsFinisherId.FromUntrustedValue(setBattlegroundsFavoriteFinisherResponse.FinisherId);
		if (!battlegroundsFinisherId.HasValue)
		{
			Log.Net.PrintError("OnSetBattlegroundsFavoriteFinisherResponse FAILED - missing value (FinisherId = {0})", battlegroundsFinisherId);
			return;
		}
		if (!CollectionManager.Get().IsValidBattlegroundsFinisherId(battlegroundsFinisherId.Value))
		{
			Log.Net.PrintError("OnSetBattlegroundsFavoriteFinisherResponse FAILED - invalid BattlegroundsFinisherId (FinisherId = {0})", battlegroundsFinisherId);
			return;
		}
		NetCacheBattlegroundsFinishers netObject = GetNetObject<NetCacheBattlegroundsFinishers>();
		if (netObject != null)
		{
			netObject.BattlegroundsFavoriteFinishers.Add(battlegroundsFinisherId.Value);
			if (this.FavoriteBattlegroundsFinisherChanged != null)
			{
				this.FavoriteBattlegroundsFinisherChanged(battlegroundsFinisherId);
			}
		}
	}

	private void OnClearBattlegroundsFavoriteFinisherResponse()
	{
		ClearBattlegroundsFavoriteFinisherResponse clearBattlegroundsFavoriteFinisherResponse = Network.Get().GetClearBattlegroundsFavoriteFinisherResponse();
		if (!clearBattlegroundsFavoriteFinisherResponse.Success)
		{
			return;
		}
		BattlegroundsFinisherId? battlegroundsFinisherId = BattlegroundsFinisherId.FromUntrustedValue(clearBattlegroundsFavoriteFinisherResponse.FinisherId);
		if (!battlegroundsFinisherId.HasValue)
		{
			Log.Net.PrintError("OnClearBattlegroundsFavoriteFinisherResponse FAILED - missing value (FinisherId = {0})", battlegroundsFinisherId);
			return;
		}
		if (!CollectionManager.Get().IsValidBattlegroundsFinisherId(battlegroundsFinisherId.Value))
		{
			Log.Net.PrintError("OnClearBattlegroundsFavoriteFinisherResponse FAILED - invalid BattlegroundsFinisherId (FinisherId = {0})", battlegroundsFinisherId);
			return;
		}
		NetCacheBattlegroundsFinishers netObject = GetNetObject<NetCacheBattlegroundsFinishers>();
		if (netObject != null)
		{
			netObject.BattlegroundsFavoriteFinishers.Remove(battlegroundsFinisherId.Value);
			if (this.FavoriteBattlegroundsFinisherChanged != null)
			{
				this.FavoriteBattlegroundsFinisherChanged(null);
			}
		}
	}

	private void OnPlayerRecords()
	{
		PlayerRecords playerRecordsPacket = Network.Get().GetPlayerRecordsPacket();
		OnPlayerRecordsPacket(playerRecordsPacket);
	}

	public void OnPlayerRecordsPacket(PlayerRecords packet)
	{
		OnNetCacheObjReceived(Network.GetPlayerRecords(packet));
	}

	private void OnRewardProgress()
	{
		OnNetCacheObjReceived(Network.Get().GetRewardProgress());
	}

	private NetCacheHeroLevels GetAllHeroXP(HeroXP packet)
	{
		if (packet == null)
		{
			return new NetCacheHeroLevels();
		}
		NetCacheHeroLevels netCacheHeroLevels = new NetCacheHeroLevels();
		for (int i = 0; i < packet.XpInfos.Count; i++)
		{
			HeroXPInfo heroXPInfo = packet.XpInfos[i];
			HeroLevel heroLevel = new HeroLevel();
			heroLevel.Class = (TAG_CLASS)heroXPInfo.ClassId;
			heroLevel.CurrentLevel.Level = heroXPInfo.Level;
			heroLevel.CurrentLevel.XP = heroXPInfo.CurrXp;
			heroLevel.CurrentLevel.MaxXP = heroXPInfo.MaxXp;
			netCacheHeroLevels.Levels.Add(heroLevel);
		}
		return netCacheHeroLevels;
	}

	public void OnHeroXP(HeroXP packet)
	{
		NetCacheHeroLevels allHeroXP = GetAllHeroXP(packet);
		if (m_prevHeroLevels != null)
		{
			foreach (HeroLevel newHeroLevel in allHeroXP.Levels)
			{
				HeroLevel heroLevel = m_prevHeroLevels.Levels.Find((HeroLevel obj) => obj.Class == newHeroLevel.Class);
				if (heroLevel == null)
				{
					continue;
				}
				if (newHeroLevel != null && newHeroLevel.CurrentLevel != null && newHeroLevel.CurrentLevel.Level != heroLevel.CurrentLevel.Level && (newHeroLevel.CurrentLevel.Level == 20 || newHeroLevel.CurrentLevel.Level == 30 || newHeroLevel.CurrentLevel.Level == 40 || newHeroLevel.CurrentLevel.Level == 50 || newHeroLevel.CurrentLevel.Level == 60))
				{
					if (newHeroLevel.Class == TAG_CLASS.DRUID)
					{
						BnetPresenceMgr.Get().SetGameField(5u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.HUNTER)
					{
						BnetPresenceMgr.Get().SetGameField(6u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.MAGE)
					{
						BnetPresenceMgr.Get().SetGameField(7u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.PALADIN)
					{
						BnetPresenceMgr.Get().SetGameField(8u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.PRIEST)
					{
						BnetPresenceMgr.Get().SetGameField(9u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.ROGUE)
					{
						BnetPresenceMgr.Get().SetGameField(10u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.SHAMAN)
					{
						BnetPresenceMgr.Get().SetGameField(11u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.WARLOCK)
					{
						BnetPresenceMgr.Get().SetGameField(12u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.WARRIOR)
					{
						BnetPresenceMgr.Get().SetGameField(13u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.DEMONHUNTER)
					{
						BnetPresenceMgr.Get().SetGameField(30u, newHeroLevel.CurrentLevel.Level);
					}
					else if (newHeroLevel.Class == TAG_CLASS.DEATHKNIGHT)
					{
						BnetPresenceMgr.Get().SetGameField(31u, newHeroLevel.CurrentLevel.Level);
					}
					else
					{
						Error.AddDevWarningNonRepeating("Missing Hero", "This hero isn't sending out toasts in OnHeroXP().");
					}
				}
				newHeroLevel.PrevLevel = heroLevel.CurrentLevel;
			}
		}
		m_prevHeroLevels = allHeroXP;
		OnNetCacheObjReceived(allHeroXP);
	}

	private void OnAllHeroXP()
	{
		HeroXP heroXP = Network.Get().GetHeroXP();
		OnHeroXP(heroXP);
	}

	private void OnInitialClientState_ProfileNotices(ProfileNotices profileNotices)
	{
		List<ProfileNotice> result = new List<ProfileNotice>();
		Network.Get().HandleProfileNotices(profileNotices.List, ref result);
		m_receivedInitialProfileNotices = true;
		HandleIncomingProfileNotices(result, isInitialNoticeList: true);
		HandleIncomingProfileNotices(m_queuedProfileNotices, isInitialNoticeList: true);
		m_queuedProfileNotices.Clear();
	}

	public void HandleIncomingProfileNotices(List<ProfileNotice> receivedNotices, bool isInitialNoticeList)
	{
		if (!m_receivedInitialProfileNotices)
		{
			m_queuedProfileNotices.AddRange(receivedNotices);
			return;
		}
		if (receivedNotices.Find((ProfileNotice obj) => obj.Type == ProfileNotice.NoticeType.GAINED_MEDAL) != null)
		{
			m_previousMedalInfo = null;
			NetCacheMedalInfo netObject = GetNetObject<NetCacheMedalInfo>();
			if (netObject != null)
			{
				netObject.PreviousMedalInfo = null;
			}
		}
		List<ProfileNotice> list = FindNewNotices(receivedNotices);
		NetCacheProfileNotices netCacheProfileNotices = GetNetObject<NetCacheProfileNotices>();
		if (netCacheProfileNotices == null)
		{
			netCacheProfileNotices = new NetCacheProfileNotices();
		}
		for (int num = 0; num < list.Count; num++)
		{
			if (!m_ackedNotices.Contains(list[num].NoticeID))
			{
				netCacheProfileNotices.Notices.Add(list[num]);
			}
		}
		OnNetCacheObjReceived(netCacheProfileNotices);
		DelNewNoticesListener[] array = m_newNoticesListeners.ToArray();
		foreach (ProfileNotice item in list)
		{
			Log.Achievements.Print("NetCache.OnProfileNotices() sending {0} to {1} listeners", item, array.Length);
		}
		DelNewNoticesListener[] array2 = array;
		foreach (DelNewNoticesListener delNewNoticesListener in array2)
		{
			Log.Achievements.Print("NetCache.OnProfileNotices(): sending notices to {0}::{1}", delNewNoticesListener.Method.ReflectedType.Name, delNewNoticesListener.Method.Name);
			delNewNoticesListener(list, isInitialNoticeList);
		}
	}

	private List<ProfileNotice> FindNewNotices(List<ProfileNotice> receivedNotices)
	{
		List<ProfileNotice> list = new List<ProfileNotice>();
		NetCacheProfileNotices netObject = GetNetObject<NetCacheProfileNotices>();
		if (netObject == null)
		{
			list.AddRange(receivedNotices);
		}
		else
		{
			foreach (ProfileNotice receivedNotice in receivedNotices)
			{
				if (netObject.Notices.Find((ProfileNotice obj) => obj.NoticeID == receivedNotice.NoticeID) == null)
				{
					list.Add(receivedNotice);
				}
			}
		}
		return list;
	}

	public void OnClientOptions(ClientOptions packet)
	{
		NetCacheClientOptions netCacheClientOptions = GetNetObject<NetCacheClientOptions>();
		bool flag = netCacheClientOptions == null;
		if (flag)
		{
			netCacheClientOptions = new NetCacheClientOptions();
		}
		if (packet.HasFailed && packet.Failed)
		{
			Debug.LogError("ReadClientOptions: packet.Failed=true. Unable to retrieve client options from UtilServer.");
			Network.Get().ShowConnectionFailureError("GLOBAL_ERROR_NETWORK_GENERIC");
			return;
		}
		foreach (PegasusUtil.ClientOption option in packet.Options)
		{
			ServerOption index = (ServerOption)option.Index;
			if (option.HasAsInt32)
			{
				netCacheClientOptions.ClientState[index] = new ClientOptionInt(option.AsInt32);
			}
			else if (option.HasAsInt64)
			{
				netCacheClientOptions.ClientState[index] = new ClientOptionLong(option.AsInt64);
			}
			else if (option.HasAsFloat)
			{
				netCacheClientOptions.ClientState[index] = new ClientOptionFloat(option.AsFloat);
			}
			else if (option.HasAsUint64)
			{
				netCacheClientOptions.ClientState[index] = new ClientOptionULong(option.AsUint64);
			}
		}
		netCacheClientOptions.UpdateServerState();
		OnNetCacheObjReceived(netCacheClientOptions);
		if (flag)
		{
			OptionsMigration.UpgradeServerOptions();
		}
		netCacheClientOptions.RemoveInvalidOptions();
	}

	private void SetClientOption(ServerOption type, ClientOptionBase newVal)
	{
		Type typeFromHandle = typeof(NetCacheClientOptions);
		if (!m_netCache.TryGetValue(typeFromHandle, out var value) || !(value is NetCacheClientOptions))
		{
			Debug.LogWarning("NetCache.OnClientOptions: Attempting to set an option before initializing the options cache.");
			return;
		}
		NetCacheClientOptions obj = (NetCacheClientOptions)value;
		obj.ClientState[type] = newVal;
		obj.CheckForDispatchToServer();
		NetCacheChanged<NetCacheClientOptions>();
	}

	public void SetIntOption(ServerOption type, int val)
	{
		SetClientOption(type, new ClientOptionInt(val));
	}

	public void SetLongOption(ServerOption type, long val)
	{
		SetClientOption(type, new ClientOptionLong(val));
	}

	public void SetFloatOption(ServerOption type, float val)
	{
		SetClientOption(type, new ClientOptionFloat(val));
	}

	public void SetULongOption(ServerOption type, ulong val)
	{
		SetClientOption(type, new ClientOptionULong(val));
	}

	public void DeleteClientOption(ServerOption type)
	{
		SetClientOption(type, null);
	}

	public bool ClientOptionExists(ServerOption type)
	{
		NetCacheClientOptions netObject = GetNetObject<NetCacheClientOptions>();
		if (netObject == null)
		{
			return false;
		}
		if (!netObject.ClientState.ContainsKey(type))
		{
			return false;
		}
		return netObject.ClientState[type] != null;
	}

	public void DispatchClientOptionsToServer()
	{
		Get().GetNetObject<NetCacheClientOptions>()?.DispatchClientOptionsToServer();
	}

	private void OnFavoriteHeroesResponse()
	{
		FavoriteHeroesResponse favoriteHeroesResponse = Network.Get().GetFavoriteHeroesResponse();
		NetCacheFavoriteHeroes netCacheFavoriteHeroes = new NetCacheFavoriteHeroes();
		foreach (FavoriteHero favoriteHero in favoriteHeroesResponse.FavoriteHeroes)
		{
			if (!EnumUtils.TryCast<TAG_CLASS>(favoriteHero.ClassId, out var outVal))
			{
				Debug.LogWarning($"NetCache.OnFavoriteHeroesResponse() unrecognized hero class {favoriteHero.ClassId}");
				continue;
			}
			if (!EnumUtils.TryCast<TAG_PREMIUM>(favoriteHero.Hero.Premium, out var outVal2))
			{
				Debug.LogWarning($"NetCache.OnFavoriteHeroesResponse() unrecognized hero premium {favoriteHero.Hero.Premium} for hero class {outVal}");
				continue;
			}
			CardDefinition item = new CardDefinition
			{
				Name = GameUtils.TranslateDbIdToCardId(favoriteHero.Hero.Asset),
				Premium = outVal2
			};
			netCacheFavoriteHeroes.FavoriteHeroes.Add((outVal, item));
		}
		OfflineDataCache.OfflineData data = OfflineDataCache.ReadOfflineDataFromFile();
		List<SetFavoriteHero> list = OfflineDataCache.GenerateSetFavoriteHeroFromDiff(data, netCacheFavoriteHeroes);
		if (list.Any())
		{
			foreach (SetFavoriteHero item2 in list)
			{
				CardDefinition hero = new CardDefinition
				{
					Name = GameUtils.TranslateDbIdToCardId(item2.FavoriteHero.Hero.Asset),
					Premium = (TAG_PREMIUM)item2.FavoriteHero.Hero.Premium
				};
				Network.Get().SetFavoriteHero((TAG_CLASS)item2.FavoriteHero.ClassId, hero, item2.IsFavorite);
			}
			OfflineDataCache.ClearFavoriteHeroesDirtyFlag();
		}
		OnNetCacheObjReceived(netCacheFavoriteHeroes);
		OfflineDataCache.CacheFavoriteHeroes(ref data, favoriteHeroesResponse);
		OfflineDataCache.WriteOfflineDataToFile(data);
	}

	private void OnSetFavoriteHeroResponse()
	{
		Network.SetFavoriteHeroResponse setFavoriteHeroResponse = Network.Get().GetSetFavoriteHeroResponse();
		if (!setFavoriteHeroResponse.Success)
		{
			return;
		}
		if (TAG_CLASS.NEUTRAL == setFavoriteHeroResponse.HeroClass || setFavoriteHeroResponse.Hero == null)
		{
			Debug.LogWarning($"NetCache.OnSetFavoriteHeroResponse: setting hero was a success, but message contains invalid class ({setFavoriteHeroResponse.HeroClass}) and/or hero ({setFavoriteHeroResponse.Hero})");
			return;
		}
		NetCacheFavoriteHeroes netObject = Get().GetNetObject<NetCacheFavoriteHeroes>();
		if (netObject != null)
		{
			if (setFavoriteHeroResponse.IsFavorite)
			{
				netObject.FavoriteHeroes.Add((setFavoriteHeroResponse.HeroClass, setFavoriteHeroResponse.Hero));
			}
			else
			{
				netObject.FavoriteHeroes.Remove((setFavoriteHeroResponse.HeroClass, setFavoriteHeroResponse.Hero));
			}
			Log.CollectionManager.Print("CollectionManager.OnSetFavoriteHeroResponse: favorite hero status for {0} updated to {1}", setFavoriteHeroResponse.Hero, setFavoriteHeroResponse.IsFavorite);
		}
		CollectionManager.Get()?.UpdateFavoriteHero(setFavoriteHeroResponse.HeroClass, setFavoriteHeroResponse.Hero.Name, setFavoriteHeroResponse.Hero.Premium, setFavoriteHeroResponse.IsFavorite);
		PegasusShared.CardDef cardDef = new PegasusShared.CardDef
		{
			Asset = GameUtils.TranslateCardIdToDbId(setFavoriteHeroResponse.Hero.Name),
			Premium = (int)setFavoriteHeroResponse.Hero.Premium
		};
		OfflineDataCache.SetFavoriteHero((int)setFavoriteHeroResponse.HeroClass, cardDef, wasCalledOffline: false, setFavoriteHeroResponse.IsFavorite);
	}

	private void OnAccountLicensesInfoResponse()
	{
		AccountLicensesInfoResponse accountLicensesInfoResponse = Network.Get().GetAccountLicensesInfoResponse();
		NetCacheAccountLicenses netCacheAccountLicenses = new NetCacheAccountLicenses();
		foreach (AccountLicenseInfo item in accountLicensesInfoResponse.List)
		{
			netCacheAccountLicenses.AccountLicenses[item.License] = item;
		}
		OnNetCacheObjReceived(netCacheAccountLicenses);
	}

	private void OnClientStaticAssetsResponse()
	{
		ClientStaticAssetsResponse clientStaticAssetsResponse = Network.Get().GetClientStaticAssetsResponse();
		if (clientStaticAssetsResponse != null)
		{
			OnNetCacheObjReceived(clientStaticAssetsResponse);
		}
	}

	private void OnMercenariesTeamListResponse()
	{
		MercenariesTeamListResponse mercenariesTeamListResponse = Network.Get().MercenariesTeamListResponse();
		if (mercenariesTeamListResponse != null && mercenariesTeamListResponse.HasTeamList)
		{
			OnNetCacheObjReceived(mercenariesTeamListResponse.TeamList);
		}
	}

	private void OnMercenariesMythicTreasureScalarsResponse()
	{
		NetCacheMercenariesMythicTreasureInfo netCacheMercenariesMythicTreasureInfo = GetNetObject<NetCacheMercenariesMythicTreasureInfo>();
		if (netCacheMercenariesMythicTreasureInfo == null)
		{
			netCacheMercenariesMythicTreasureInfo = new NetCacheMercenariesMythicTreasureInfo();
			netCacheMercenariesMythicTreasureInfo.MythicTreasureScalarMap = new Dictionary<int, NetCacheMercenariesMythicTreasureInfo.MythicTreasureScalar>();
		}
		MercenariesMythicTreasureScalarsResponse mercenariesMythicTreasureScalarsResponse = Network.Get().MercenariesMythicTreasureScalarsResponse();
		if (mercenariesMythicTreasureScalarsResponse == null)
		{
			OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
			return;
		}
		if (mercenariesMythicTreasureScalarsResponse.HasErrorCode && mercenariesMythicTreasureScalarsResponse.ErrorCode != PegasusShared.ErrorCode.ERROR_OK)
		{
			OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
			return;
		}
		if (!mercenariesMythicTreasureScalarsResponse.HasTreasureScalarList)
		{
			OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
			return;
		}
		netCacheMercenariesMythicTreasureInfo.MythicTreasureScalarMap.Clear();
		foreach (MercenaryMythicTreasureScalar treasureScalar in mercenariesMythicTreasureScalarsResponse.TreasureScalarList.TreasureScalars)
		{
			if (treasureScalar.HasTreasureId && treasureScalar.HasScalar)
			{
				netCacheMercenariesMythicTreasureInfo.MythicTreasureScalarMap.Add(treasureScalar.TreasureId, new NetCacheMercenariesMythicTreasureInfo.MythicTreasureScalar
				{
					TreasureId = treasureScalar.TreasureId,
					Scalar = (int)treasureScalar.Scalar
				});
			}
		}
		OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
	}

	private void OnMercenariesMythicTreasureScalarPurchaseResponse()
	{
		NetCacheMercenariesMythicTreasureInfo netCacheMercenariesMythicTreasureInfo = GetNetObject<NetCacheMercenariesMythicTreasureInfo>();
		if (netCacheMercenariesMythicTreasureInfo == null)
		{
			netCacheMercenariesMythicTreasureInfo = new NetCacheMercenariesMythicTreasureInfo();
			netCacheMercenariesMythicTreasureInfo.MythicTreasureScalarMap = new Dictionary<int, NetCacheMercenariesMythicTreasureInfo.MythicTreasureScalar>();
		}
		MercenariesMythicTreasureScalarPurchaseResponse mercenariesMythicTreasureScalarPurchaseResponse = Network.Get().MercenariesMythicTreasureScalarPurchaseResponse();
		if (mercenariesMythicTreasureScalarPurchaseResponse == null)
		{
			OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
			return;
		}
		if (!mercenariesMythicTreasureScalarPurchaseResponse.Success)
		{
			OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
			return;
		}
		if (!mercenariesMythicTreasureScalarPurchaseResponse.HasTreasureScalar || !mercenariesMythicTreasureScalarPurchaseResponse.TreasureScalar.HasTreasureId || !mercenariesMythicTreasureScalarPurchaseResponse.TreasureScalar.HasScalar || !mercenariesMythicTreasureScalarPurchaseResponse.HasPurchaseCount)
		{
			OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
			return;
		}
		if (!netCacheMercenariesMythicTreasureInfo.MythicTreasureScalarMap.TryGetValue(mercenariesMythicTreasureScalarPurchaseResponse.TreasureScalar.TreasureId, out var value))
		{
			value = new NetCacheMercenariesMythicTreasureInfo.MythicTreasureScalar
			{
				TreasureId = mercenariesMythicTreasureScalarPurchaseResponse.TreasureScalar.TreasureId,
				Scalar = 0
			};
			netCacheMercenariesMythicTreasureInfo.MythicTreasureScalarMap.Add(value.TreasureId, value);
		}
		if (value.Scalar > mercenariesMythicTreasureScalarPurchaseResponse.TreasureScalar.Scalar)
		{
			OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
			return;
		}
		value.Scalar = (int)mercenariesMythicTreasureScalarPurchaseResponse.TreasureScalar.Scalar;
		OnNetCacheObjReceived(netCacheMercenariesMythicTreasureInfo);
	}

	private void OnMercenariesUpdateMythicBountyLevelResponse()
	{
		MercenariesMythicBountyLevelUpdate mercenariesMythicBountyLevelUpdate = Network.Get().MercenariesMythicBountyLevelUpdate();
		if (mercenariesMythicBountyLevelUpdate != null && mercenariesMythicBountyLevelUpdate.HasMythicBountyLevelInfo)
		{
			NetCacheMercenariesPlayerInfo netObject = GetNetObject<NetCacheMercenariesPlayerInfo>();
			int currentMythicBountyLevel = (int)mercenariesMythicBountyLevelUpdate.MythicBountyLevelInfo.CurrentMythicBountyLevel;
			int minMythicBountyLevel = (int)mercenariesMythicBountyLevelUpdate.MythicBountyLevelInfo.MinMythicBountyLevel;
			int maxMythicBountyLevel = (int)mercenariesMythicBountyLevelUpdate.MythicBountyLevelInfo.MaxMythicBountyLevel;
			if (netObject.CurrentMythicBountyLevel != currentMythicBountyLevel || netObject.MinMythicBountyLevel != minMythicBountyLevel || netObject.MaxMythicBountyLevel != maxMythicBountyLevel)
			{
				LettuceVillageDataUtil.ResetSavedMythicBountyLevel();
			}
			netObject.CurrentMythicBountyLevel = currentMythicBountyLevel;
			netObject.MinMythicBountyLevel = minMythicBountyLevel;
			netObject.MaxMythicBountyLevel = maxMythicBountyLevel;
			OnNetCacheObjReceived(netObject);
		}
	}

	private void RegisterNetCacheHandlers()
	{
		Network network = Network.Get();
		network.RegisterNetHandler(DBAction.PacketID.ID, OnDBAction);
		network.RegisterNetHandler(GenericResponse.PacketID.ID, OnGenericResponse);
		network.RegisterNetHandler(InitialClientState.PacketID.ID, OnInitialClientState);
		network.RegisterNetHandler(MedalInfo.PacketID.ID, OnMedalInfo);
		network.RegisterNetHandler(BattlegroundsRatingInfoResponse.PacketID.ID, OnBaconRatingInfo);
		network.RegisterNetHandler(ProfileProgress.PacketID.ID, OnProfileProgress);
		network.RegisterNetHandler(GamesInfo.PacketID.ID, OnGamesInfo);
		network.RegisterNetHandler(CardValues.PacketID.ID, OnCardValues);
		network.RegisterNetHandler(GuardianVars.PacketID.ID, OnGuardianVars);
		network.RegisterNetHandler(PlayerRecords.PacketID.ID, OnPlayerRecords);
		network.RegisterNetHandler(RewardProgress.PacketID.ID, OnRewardProgress);
		network.RegisterNetHandler(HeroXP.PacketID.ID, OnAllHeroXP);
		network.RegisterNetHandler(CardBacks.PacketID.ID, OnCardBacks);
		network.RegisterNetHandler(SetFavoriteCardBackResponse.PacketID.ID, OnSetFavoriteCardBackResponse);
		network.RegisterNetHandler(FavoriteHeroesResponse.PacketID.ID, OnFavoriteHeroesResponse);
		network.RegisterNetHandler(SetFavoriteHeroResponse.PacketID.ID, OnSetFavoriteHeroResponse);
		network.RegisterNetHandler(AccountLicensesInfoResponse.PacketID.ID, OnAccountLicensesInfoResponse);
		network.RegisterNetHandler(DeckList.PacketID.ID, OnReceivedDeckHeaders);
		network.RegisterNetHandler(PVPDRStatsInfoResponse.PacketID.ID, OnPVPDRStatsInfo);
		network.RegisterNetHandler(CosmeticCoins.PacketID.ID, OnCoins);
		network.RegisterNetHandler(SetFavoriteCosmeticCoinResponse.PacketID.ID, OnSetFavoriteCosmeticCoinResponse);
		network.RegisterNetHandler(BattlegroundsHeroSkinsResponse.PacketID.ID, OnBattlegroundsHeroSkinsResponse);
		network.RegisterNetHandler(SetBattlegroundsFavoriteHeroSkinResponse.PacketID.ID, OnSetBattlegroundsFavoriteHeroSkinResponse);
		network.RegisterNetHandler(ClearBattlegroundsFavoriteHeroSkinResponse.PacketID.ID, OnClearBattlegroundsFavoriteHeroSkinResponse);
		network.RegisterNetHandler(BattlegroundsGuideSkinsResponse.PacketID.ID, OnBattlegroundsGuideSkinsResponse);
		network.RegisterNetHandler(SetBattlegroundsFavoriteGuideSkinResponse.PacketID.ID, OnSetBattlegroundsFavoriteGuideSkinResponse);
		network.RegisterNetHandler(ClearBattlegroundsFavoriteGuideSkinResponse.PacketID.ID, OnClearBattlegroundsFavoriteGuideSkinResponse);
		network.RegisterNetHandler(BattlegroundsBoardSkinsResponse.PacketID.ID, OnBattlegroundsBoardSkinsResponse);
		network.RegisterNetHandler(SetBattlegroundsFavoriteBoardSkinResponse.PacketID.ID, OnSetBattlegroundsFavoriteBoardSkinResponse);
		network.RegisterNetHandler(ClearBattlegroundsFavoriteBoardSkinResponse.PacketID.ID, OnClearBattlegroundsFavoriteBoardSkinResponse);
		network.RegisterNetHandler(BattlegroundsFinishersResponse.PacketID.ID, OnBattlegroundsFinishersResponse);
		network.RegisterNetHandler(SetBattlegroundsFavoriteFinisherResponse.PacketID.ID, OnSetBattlegroundsFavoriteFinisherResponse);
		network.RegisterNetHandler(ClearBattlegroundsFavoriteFinisherResponse.PacketID.ID, OnClearBattlegroundsFavoriteFinisherResponse);
		network.RegisterNetHandler(BattlegroundsEmotesResponse.PacketID.ID, OnBattlegroundsEmotesResponse);
		network.RegisterNetHandler(SetBattlegroundsEmoteLoadoutResponse.PacketID.ID, OnSetBattlegroundsEmoteLoadoutResponse);
		network.RegisterNetHandler(PetsResponse.PacketID.ID, OnPetsResponse);
		network.RegisterNetHandler(SetFavoritePetResponse.PacketID.ID, OnSetFavoritePetResponse);
		network.RegisterNetHandler(Deadend.PacketID.ID, OnHearthstoneUnavailableGame);
		network.RegisterNetHandler(DeadendUtil.PacketID.ID, OnHearthstoneUnavailableUtil);
		network.RegisterNetHandler(ClientStaticAssetsResponse.PacketID.ID, OnClientStaticAssetsResponse);
		network.RegisterNetHandler(MercenariesTeamListResponse.PacketID.ID, OnMercenariesTeamListResponse);
		network.RegisterNetHandler(LettuceMapResponse.PacketID.ID, OnLettuceMapResponse);
		network.RegisterNetHandler(MercenariesPlayerInfoResponse.PacketID.ID, OnMercenariesPlayerInfoResponse);
		network.RegisterNetHandler(MercenariesPvPWinUpdate.PacketID.ID, OnMercenariesPvPWinUpdate);
		network.RegisterNetHandler(MercenariesPlayerBountyInfoUpdate.PacketID.ID, OnMercenariesPlayerBountyInfoUpdate);
		network.RegisterNetHandler(MercenariesBountyAcknowledgeResponse.PacketID.ID, OnMercenariesBountyAcknowledgeResponse);
		network.RegisterNetHandler(MercenariesMythicTreasureScalarsResponse.PacketID.ID, OnMercenariesMythicTreasureScalarsResponse);
		network.RegisterNetHandler(MercenariesMythicTreasureScalarPurchaseResponse.PacketID.ID, OnMercenariesMythicTreasureScalarPurchaseResponse);
		network.RegisterNetHandler(MercenariesMythicBountyLevelUpdate.PacketID.ID, OnMercenariesUpdateMythicBountyLevelResponse);
		network.RegisterNetHandler(MercenariesGetVillageResponse.PacketID.ID, OnVillageDataResponse);
		network.RegisterNetHandler(MercenariesBuildingStateUpdate.PacketID.ID, OnVillageBuildingStateUpdated);
		network.RegisterNetHandler(MercenariesVisitorStateUpdate.PacketID.ID, OnVillageVisitorStateUpdated);
		network.RegisterNetHandler(MercenariesRefreshVillageResponse.PacketID.ID, OnRefreshVillageDataResponse);
	}

	public void RegisterCollectionManager(NetCacheCallback callback)
	{
		RegisterCollectionManager(callback, DefaultErrorHandler);
	}

	public void RegisterCollectionManager(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest request = new NetCacheBatchRequest(callback, errorCallback, RegisterCollectionManager);
		AddCollectionManagerToRequest(ref request);
		NetCacheMakeBatchRequest(request);
	}

	public void RegisterScreenCollectionManager(NetCacheCallback callback)
	{
		RegisterScreenCollectionManager(callback, DefaultErrorHandler);
	}

	public void RegisterScreenCollectionManager(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest request = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenCollectionManager);
		AddCollectionManagerToRequest(ref request);
		request.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheCollection)),
			new Request(typeof(NetCacheFeatures)),
			new Request(typeof(NetCacheHeroLevels))
		});
		NetCacheMakeBatchRequest(request);
	}

	public void RegisterScreenForge(NetCacheCallback callback)
	{
		RegisterScreenForge(callback, DefaultErrorHandler);
	}

	public void RegisterScreenForge(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest request = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenForge);
		AddCollectionManagerToRequest(ref request);
		request.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheFeatures)),
			new Request(typeof(NetCacheHeroLevels))
		});
		NetCacheMakeBatchRequest(request);
	}

	public void RegisterScreenTourneys(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenTourneys);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCachePlayerRecords)),
			new Request(typeof(NetCacheDecks)),
			new Request(typeof(NetCacheFeatures)),
			new Request(typeof(NetCacheHeroLevels))
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterScreenFriendly(NetCacheCallback callback)
	{
		RegisterScreenFriendly(callback, DefaultErrorHandler);
	}

	public void RegisterScreenFriendly(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenFriendly);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheDecks)),
			new Request(typeof(NetCacheHeroLevels))
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterScreenPractice(NetCacheCallback callback)
	{
		RegisterScreenPractice(callback, DefaultErrorHandler);
	}

	public void RegisterScreenPractice(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenPractice);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCachePlayerRecords), rl: true),
			new Request(typeof(NetCacheDecks)),
			new Request(typeof(NetCacheFeatures)),
			new Request(typeof(NetCacheHeroLevels)),
			new Request(typeof(NetCacheRewardProgress))
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterScreenEndOfGame(NetCacheCallback callback)
	{
		RegisterScreenEndOfGame(callback, DefaultErrorHandler);
	}

	public void RegisterScreenEndOfGame(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		if (ServiceManager.TryGet<GameMgr>(out var service) && service.IsSpectator())
		{
			Processor.ScheduleCallback(0f, realTime: false, delegate
			{
				callback();
			});
			return;
		}
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenEndOfGame);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheMedalInfo), rl: true),
			new Request(typeof(NetCacheHeroLevels), rl: true)
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
		PegasusShared.GameType num = service?.GetGameType() ?? PegasusShared.GameType.GT_UNKNOWN;
		bool flag = GameUtils.IsTavernBrawlGameType(num);
		if (num == PegasusShared.GameType.GT_VS_FRIEND && FriendChallengeMgr.Get().IsChallengeTavernBrawl())
		{
			NetCacheFeatures netObject = Get().GetNetObject<NetCacheFeatures>();
			if (netObject != null && netObject.FriendWeekAllowsTavernBrawlRecordUpdate && EventTimingManager.Get().IsEventActive(EventTimingType.FRIEND_WEEK))
			{
				flag = true;
			}
		}
		if (flag)
		{
			TavernBrawlManager.Get().RefreshPlayerRecord();
		}
	}

	public void RegisterScreenPackOpening(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenPackOpening);
		netCacheBatchRequest.AddRequest(new Request(typeof(NetCacheBoosters)));
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterScreenBox(NetCacheCallback callback)
	{
		RegisterScreenBox(callback, DefaultErrorHandler);
	}

	public void RegisterScreenBox(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenBox);
		Debug.Log("RegisterScreenBox tempGuardianVars=" + GetNetObject<NetCacheFeatures>());
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheBoosters)),
			new Request(typeof(NetCacheClientOptions)),
			new Request(typeof(NetCacheProfileProgress)),
			new Request(typeof(NetCacheFeatures)),
			new Request(typeof(NetCacheMedalInfo)),
			new Request(typeof(NetCacheHeroLevels)),
			new Request(typeof(NetCachePlayerRecords), rl: true)
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterScreenStartup(NetCacheCallback callback)
	{
		RegisterScreenStartup(callback, DefaultErrorHandler);
	}

	public void RegisterScreenStartup(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenStartup);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheProfileProgress))
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterScreenLogin(NetCacheCallback callback)
	{
		RegisterScreenLogin(callback, DefaultErrorHandler);
	}

	public void RegisterScreenLogin(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenLogin);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheRewardProgress)),
			new Request(typeof(NetCachePlayerRecords)),
			new Request(typeof(NetCacheGoldBalance)),
			new Request(typeof(NetCacheHeroLevels)),
			new Request(typeof(NetCacheCardBacks), rl: true),
			new Request(typeof(NetCacheFavoriteHeroes), rl: true),
			new Request(typeof(NetCacheAccountLicenses)),
			new Request(typeof(ClientStaticAssetsResponse)),
			new Request(typeof(NetCacheClientOptions)),
			new Request(typeof(NetCacheCoins)),
			new Request(typeof(NetCacheBattlegroundsHeroSkins)),
			new Request(typeof(NetCacheBattlegroundsGuideSkins)),
			new Request(typeof(NetCacheBattlegroundsBoardSkins)),
			new Request(typeof(NetCacheBattlegroundsFinishers)),
			new Request(typeof(NetCacheBattlegroundsEmotes)),
			new Request(typeof(NetCachePets))
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterTutorialEndGameScreen(NetCacheCallback callback)
	{
		RegisterTutorialEndGameScreen(callback, DefaultErrorHandler);
	}

	public void RegisterTutorialEndGameScreen(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		if (ServiceManager.TryGet<GameMgr>(out var service) && service.IsSpectator())
		{
			Processor.ScheduleCallback(0f, realTime: false, delegate
			{
				callback();
			});
			return;
		}
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterTutorialEndGameScreen);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheProfileProgress))
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterFriendChallenge(NetCacheCallback callback)
	{
		RegisterFriendChallenge(callback, DefaultErrorHandler);
	}

	public void RegisterFriendChallenge(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterFriendChallenge);
		netCacheBatchRequest.AddRequest(new Request(typeof(NetCacheProfileProgress)));
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	public void RegisterScreenBattlegrounds(NetCacheCallback callback)
	{
		RegisterScreenBattlegrounds(callback, DefaultErrorHandler);
	}

	public void RegisterScreenBattlegrounds(NetCacheCallback callback, ErrorCallback errorCallback)
	{
		NetCacheBatchRequest netCacheBatchRequest = new NetCacheBatchRequest(callback, errorCallback, RegisterScreenBattlegrounds);
		netCacheBatchRequest.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheFeatures))
		});
		NetCacheMakeBatchRequest(netCacheBatchRequest);
	}

	private void AddCollectionManagerToRequest(ref NetCacheBatchRequest request)
	{
		request.AddRequests(new List<Request>
		{
			new Request(typeof(NetCacheProfileNotices)),
			new Request(typeof(NetCacheDecks)),
			new Request(typeof(NetCacheCollection)),
			new Request(typeof(NetCacheArcaneDustBalance)),
			new Request(typeof(NetCacheClientOptions))
		});
	}

	private void OnPacketThrottled(int packetID, long retryMillis)
	{
		if (packetID != 201)
		{
			return;
		}
		float timeAdded = Time.realtimeSinceStartup + (float)retryMillis / 1000f;
		foreach (NetCacheBatchRequest cacheRequest in m_cacheRequests)
		{
			cacheRequest.m_timeAdded = timeAdded;
		}
	}

	public void Cheat_AddNotice(ProfileNotice notice)
	{
		if (HearthstoneApplication.IsInternal())
		{
			UnloadNetObject<NetCacheProfileNotices>();
			PopupDisplayManager.Get().RewardPopups.ClearSeenNotices();
			notice.NoticeID = 9999L;
			m_ackedNotices.Remove(notice.NoticeID);
			List<ProfileNotice> list = new List<ProfileNotice>();
			list.Add(notice);
			HandleIncomingProfileNotices(list, isInitialNoticeList: false);
		}
	}
}
