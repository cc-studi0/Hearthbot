# SBAPI.dll 反编译结果

## 类型总览

### Enums
- SmartBotProfiles.BaseProfile
- SmartBotAPI.Stats.RankLeague
- SmartBotAPI.Stats.BnetRegion
- SmartBotAPI.Stats.ArchetypeStatEntry.SampleSize
- SmartBotAPI.Missplays.Missplay.State
- SmartBot.Plugins.API.Bot.EmoteType
- SmartBot.Plugins.API.Bot.Mode
- SmartBot.Plugins.API.Bot.Scene
- SmartBot.Plugins.API.Bot.ArenaLogic
- SmartBot.Plugins.API.Bot.DiscoverLogic
- SmartBot.Plugins.API.Card.Cards
- SmartBot.Plugins.API.Card.CClass
- SmartBot.Plugins.API.Card.CQuality
- SmartBot.Plugins.API.Card.CRace
- SmartBot.Plugins.API.Card.CSet
- SmartBot.Plugins.API.Card.GAME_TAG
- SmartBot.Plugins.API.Card.TAG_SPELL_SCHOOL
- SmartBot.Plugins.API.Card.CType
- SmartBot.Plugins.API.Deck.DeckType
- SmartBot.Plugins.API.Quest.QuestType

### Interfaces
- SmartBotProfiles.Profile
- SmartBotAPI.Plugins.API.Archetype
- SmartBot.Mulligan.MulliganProfile
- SmartBot.Discover.DiscoverPickHandler
- SmartBot.Arena.ArenaPickHandler

### Delegates
- SmartBot.Plugins.API.Debug.OnReceivedTextResult
- SmartBot.Plugins.API.GUI.OnScreenshotReceivedDel
- SmartBot.Plugins.API.GuiElementButton.ClickCallBack


---

## 1. SmartBotAPI.Stats.BnetRegion

```csharp
namespace SmartBotAPI.Stats;

public enum BnetRegion
{
    REGION_CN = 5,
    REGION_DEV = 60,
    REGION_EU = 2,
    REGION_KR = 3,
    REGION_LIVE_VERIFICATION = 40,
    REGION_MSCHWEITZER_BN11 = 52,
    REGION_MSCHWEITZER_BN12 = 53,
    REGION_PTR = 98,
    REGION_PTR_LOC = 41,
    REGION_TW = 4,
    REGION_UNINITIALIZED = -1,
    REGION_UNKNOWN = 0,
    REGION_US = 1,
    REGION_ALL = -2
}
```

## 2. SmartBotAPI.Stats.RankLeague

```csharp
namespace SmartBotAPI.Stats;

public enum RankLeague
{
    Bronze,
    Silver,
    Gold,
    Platinum,
    Diamond,
    Legend
}
```

## 3. SmartBotAPI.Plugins.API.Archetype (Interface)

```csharp
using System.Collections.Generic;
using SmartBot.Plugins.API;

namespace SmartBotAPI.Plugins.API;

public interface Archetype
{
    string ArchetypeName();
    List<Card.Cards> ArchetypeCardSet();
}
```

## 4. SmartBot.Arena.ArenaPickHandler (Interface)

```csharp
using System.Collections.Generic;
using SmartBot.Plugins.API;

namespace SmartBot.Arena;

public interface ArenaPickHandler
{
    Card.Cards HandlePickDecision(Card.CClass heroClass, List<Card.Cards> deck, Card.Cards choiceOne, Card.Cards choiceTwo, Card.Cards choiceThree);
}
```

## 5. SmartBotAPI.Plugins.API.ArchetypeStats

```csharp
using System.Collections.Generic;

namespace SmartBotAPI.Plugins.API;

public class ArchetypeStats
{
    public static Dictionary<string, Dictionary<string, ArchetypeStatsResult>> Stats = new Dictionary<string, Dictionary<string, ArchetypeStatsResult>>();
}
```

## 6. SmartBotAPI.Plugins.API.ArchetypeStatsResult

```csharp
using System.Runtime.Serialization;

namespace SmartBotAPI.Plugins.API;

[DataContract]
public class ArchetypeStatsResult
{
    [DataMember]
    public int Lost;

    [DataMember]
    public int Wins;
}
```

## 7. SmartBotAPI.Stats.ArchetypeStatEntry

```csharp
using SmartBot.Plugins.API;

namespace SmartBotAPI.Stats;

public class ArchetypeStatEntry
{
    public enum SampleSize
    {
        Hour,
        TwelveHours,
        TwentyHours,
        SeventyHours,
        Month,
        LastMonth
    }

    public BnetRegion Region { get; set; }
    public Bot.Mode Mode { get; set; }
    public SampleSize Sample { get; set; }
    public string Name { get; set; }
    public float Winrate { get; set; }
    public float Proportion { get; set; }

    public ArchetypeStatEntry(BnetRegion region, Bot.Mode mode, SampleSize sample, string name, float winrate, float proportion)
    {
        Region = region;
        Mode = mode;
        Sample = sample;
        Name = name;
        Winrate = winrate;
        Proportion = proportion;
    }

    public ArchetypeStatEntry() { }
}
```

## 8. SmartBotAPI.Stats.StatEntry

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SmartBot.Plugins.API;

namespace SmartBotAPI.Stats;

[Serializable]
[DataContract]
public class StatEntry
{
    [DataMember] public Card.CClass _enemyClass { get; set; }
    [DataMember] public Card.CClass _friendClass { get; set; }
    [DataMember] public Bot.Mode _mode { get; set; }
    [DataMember] public string _profile { get; set; }
    [DataMember] public int _rank { get; set; }
    [DataMember] public BnetRegion _region { get; set; }
    [DataMember] public DateTime _Submitted { get; set; }
    [DataMember] public bool _won { get; set; }
    [DataMember] public List<string> _deck { get; set; }

    public StatEntry() { }

    public StatEntry(Card.CClass friendClass, Card.CClass enemyClass, Bot.Mode mode, string profile, bool won, int rank, DateTime submitted, BnetRegion region, List<string> deck)
    {
        _friendClass = friendClass;
        _enemyClass = enemyClass;
        _mode = mode;
        _profile = profile;
        _won = won;
        _rank = rank;
        _Submitted = submitted;
        _region = region;
        _deck = new List<string>();
    }

    public static StatEntry StringToEntry(string request) { ... }
    public static string EntryToString(StatEntry request) { ... }
}
```

## 9. SmartBotAPI.Stats.ArenaStatEntry

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using SmartBot.Plugins.API;

namespace SmartBotAPI.Stats;

[Serializable]
[DataContract]
public class ArenaStatEntry
{
    [DataMember] public Card.CClass Class { get; set; }
    [DataMember] public int Wins { get; set; }
    [DataMember] public List<string> Deck { get; set; }

    public ArenaStatEntry() { Deck = new List<string>(); }

    public ArenaStatEntry(Card.CClass clas, int wins, List<string> deck)
    {
        Class = clas;
        Wins = wins;
        Deck = deck;
    }
}
```

## 10. SmartBotAPI.Stats.LegendEntry

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using SmartBot.Plugins.API;

namespace SmartBotAPI.Stats;

[Serializable]
[DataContract]
public class LegendEntry
{
    [DataMember] public BnetRegion Region { get; set; }
    [DataMember] public List<string> Cards { get; set; }
    [DataMember] public Dictionary<string, List<string>> Sides { get; set; }
    [DataMember] public DateTime Submitted { get; set; }
    [DataMember] public string Profile { get; set; }
    [DataMember] public string MulliganProfile { get; set; }
    [DataMember] public Bot.Mode Mode { get; set; }
    [DataMember] public Card.CClass Class { get; set; }

    public LegendEntry(BnetRegion region, List<string> cards, Dictionary<string, List<string>> sides,
        DateTime submitted, string profile, string mulliganProfile, Bot.Mode mode, Card.CClass @class)
    {
        Region = region; Cards = cards; Submitted = submitted;
        Profile = profile; MulliganProfile = mulliganProfile;
        Mode = mode; Class = @class; Sides = sides;
    }
}
```

## 11. SmartBotAPI.Missplays.Missplay

```csharp
[DataContract]
public class Missplay
{
    public enum State { Pending, RejectedComplexity, RejectedAlreadyFixed, Fixed, RejectedOther, Discarded }

    private HSReplayArchetype _CachedArchetype;
    private Archetype _CachedLocalArchetype;

    [DataMember] public int Id { get; set; }
    [DataMember] public string Description { get; set; }
    [DataMember] public string Seed { get; set; }
    [DataMember] public string Key { get; set; }
    [DataMember] public DateTime Submitted { get; set; }
    [DataMember] public DateTime Processed { get; set; }
    [DataMember] public State CurrentState { get; set; }
    [DataMember] public string Mode { get; set; }
    [DataMember] public string Classe { get; set; }
    [DataMember] public string Profile { get; set; }
    [DataMember] public string SubmitterName { get; set; }
    [DataMember] public string Actions { get; set; }
    [DataMember] public int IdDb { get; set; }

    public string ArchetypeStr { get; }
    public HSReplayArchetype Archetype => HSReplayArchetypesMatcher.DetectArchetype(...);
    public Archetype LocalArchetype => ArchetypeDetector.DetectArchetype(...);

    public Missplay() { IdDb = -1; }
    public string ToJson() { ... }
    public static Missplay FromJson(string json) { ... }
    public bool IsSecretSeed() { return Board.FromSeed(Seed).SecretEnemy; }
}
```

## 12. SmartBot.Plugins.API.Statistics

```csharp
using System;

namespace SmartBot.Plugins.API;

public class Statistics
{
    public static bool _reset;
    public static int _wins;
    public static int _losses;
    public static int _Arenawins;
    public static int _Arenalosses;
    public static int _conceded;
    public static int _concededTotal;
    public static TimeSpan _elapsedTime;
    public static int _gold;
    public static int _xplevel;
    public static string _xpprogress;

    public static int Wins => _wins;
    public static int Losses => _losses;
    public static int ArenaWins => _Arenawins;
    public static int ArenaLosses => _Arenalosses;
    public static int Conceded => _conceded;
    public static int ConcededTotal => _concededTotal;
    public static TimeSpan ElapsedTime => _elapsedTime;
    public static int Gold => _gold;
    public static int XpLevel => _xplevel;
    public static string XpProgress => _xpprogress;

    public static void Reset() { _reset = true; }
}
```

## 13. SmartBot.Plugins.API.Bot (完整)

```csharp
public class Bot
{
    public enum EmoteType { Threaten, Greetings, Thanks, WellPlayed, Wow, Oops }
    public enum Mode { Standard=0, Arena=2, None=4, ArenaAuto=5, Practice=6, Wild=7, Battleground=9, DuelsNormal=10, Twist=11, Casual=12, Mercenaries=13 }
    public enum Scene { INVALID, STARTUP, LOGIN, HUB, GAMEPLAY, COLLECTIONMANAGER, PACKOPENING, TOURNAMENT, FRIENDLY, FATAL_ERROR, DRAFT, CREDITS, RESET, ADVENTURE, TAVERN_BRAWL }
    public enum ArenaLogic { Heartharena, Custom }
    public enum DiscoverLogic { Default, Simulation, SimulationCustom, Custom }

    // --- 静态字段 ---
    public static readonly object Boardlocker;
    public static Board _currentBoard;
    public static bool _log;
    public static Queue<string> _logValue;
    public static bool _startBot, _stopBot, _closeHs, _squelch, _unsquelch, _closeBot, _finishBot;
    public static bool _startRelogger, _stopRelogger, _suspendBot, _resumeBot;
    public static bool _refreshDecks, _refreshProfiles, _refreshMulligans, _reloadPlugins;
    public static List<Deck> _decks, _decksSelected;
    public static List<string> _profiles, _mulliganProfiles;
    public static PlayerData _playerData;
    public static bool _enableRandomDecks, _enableRandomDecksValue;
    public static bool _changeDeck; public static string _changeDeckValue;
    public static bool _changeMulligan; public static string _changeMulliganValue;
    public static bool _changeProfile; public static string _changeProfileValue;
    public static Mode _currentMode;
    public static Scene _currentScene;
    public static string _currentMulligan, _currentProfile;
    public static Deck _currentDeckName;
    public static bool _changeMode; public static Mode _changeModeValue;
    public static bool _regionset; public static BnetRegion _region;
    public static bool _sendEmote; public static EmoteType _sendEmoteValue;
    // ... hover/arrow/latency fields ...
    public static int _averageLatency, _minLatency, _maxLatency;
    public static bool _setLatencySamplingRate; public static int _setLatencySamplingRateValue;
    public static bool _setAoeSimulationEnabled, _setAoeSimulationEnabledValue;
    public static bool _setMinRank; public static int _setMinRankValue;
    public static bool _setMaxRank; public static int _setMaxRankValue;
    public static bool _setMaxWins; public static int _setMaxWinsValue;
    public static bool _setMaxLosses; public static int _setMaxLossesValue;
    public static bool _setMaxHours; public static int _setMaxHoursValue;
    public static bool _setCloseHs, _setCloseHsValue;
    public static bool _setAutoConcede, _setAutoConcedeValue;
    public static bool _setAutoConcedeAlternative, _setAutoConcedeAlternativeValue;
    public static bool _setAutoConcedeMaxRank; public static int _setAutoConcedeMaxRankValue;
    public static bool _setConcedeWhenLethal, _setConcedeWhenLethalValue;
    public static bool _setAttackRoutineEnabled, _setAttackRoutineEnabledValue;
    public static bool _setThinkingRoutineEnabled, _setThinkingRoutineEnabledValue;
    public static bool _setHoverRoutineEnabled, _setHoverRoutineEnabledValue;
    public static List<Quest> _quests;
    public static bool _canCancelQuest, _cancelQuest; public static Quest _cancelQuestValue;
    public static List<FriendRequest> _requests;
    public static List<Friend> _friends;
    public static bool _goldCapReached;
    public static int _gold;
    public static long _currentOpponent, _lastOpponent;
    public static List<Plugin> _plugins;
    public static string _switchAccountLogin, _switchAccountPassword;
    public static BnetRegion _switchAccountRegion;
    public static bool _switchAccount;
    public static string _currentAccountlogin;
    public static bool _botRunning, _coachEnabled;
    public static bool _refresArenaProfiles, _refresDiscoverProfiles, _refreshArchetypes;
    public static List<string> _arenaProfiles, _discoverProfiles;
    public static SessionRewards _sessionRewards;
    public static List<Archetype> _Archetypes;
    public static DiscoverLogic _discoLogic;
    public static string _discoLogicProfile;
    public static bool _changeAfterArenaMode; public static Mode _changeAfterArenaModeValue;
    public static bool _maxDraftCount; public static int _maxDraftCountValue;
    public static bool _currentDraftCount; public static int _currentDraftCountValue;

    // --- 属性 ---
    public static Board CurrentBoard { get; set; } // 带 Boardlocker 锁

    // --- 静态方法 ---
    public static bool IsCoachEnabled();
    public static bool IsBotRunning();
    public static string GetCurrentAccount();
    public static void SwitchAccount(string login, string password);
    public static void SwitchAccount(string login, string password, BnetRegion region);
    public static void BuildBasicDeck(bool wild, Card.CClass cl);
    public static void BuildCustomDeck(bool wild, Dictionary<Card.Cards, int> customDeck);
    public static List<Plugin> GetPlugins();
    public static SessionRewards GetSessionRewards();
    public static void Log(string log);
    public static void StartBot();
    public static void StopBot();
    public static void Squelch();
    public static void UnsquelchOpponent();
    public static void CloseHs();
    public static void CloseBot();
    public static void StartRelogger();
    public static void StopRelogger();
    public static void SuspendBot();
    public static void Finish();
    public static void ResumeBot();
    public static void RefreshDecks();
    public static void RefreshArenaProfiles();
    public static void RefreshDiscoverProfiles();
    public static void RefreshArchetypes();
    public static void RefreshProfiles();
    public static void ReloadPlugins();
    public static void RefreshMulliganProfiles();
    public static List<Deck> GetDecks();
    public static List<Deck> GetSelectedDecks();
    public static List<string> GetProfiles();
    public static List<string> GetArenaProfiles();
    public static List<string> GetDiscoverProfiles();
    public static List<string> GetMulliganProfiles();
    public static List<Archetype> GetArchetypes();
    public static void ChangeDeck(string deckName);
    public static void ChangeArenaProfile(ArenaLogic logic, string profile = "");
    public static void ChangeDiscoverProfile(DiscoverLogic logic, string profile = "");
    public static void ChangeMulligan(string mulliganName);
    public static void ChangeProfile(string profileName);
    public static Mode CurrentMode();
    public static DiscoverLogic CurrentDiscoverLogic();
    public static string CurrentDiscoverProfile();
    public static Scene CurrentScene();
    public static BnetRegion CurrentRegion();
    public static string CurrentMulligan();
    public static string CurrentProfile();
    public static Deck CurrentDeck();
    public static void ChangeMode(Mode mode);
    public static void ChangeAfterArenaMode(Mode mode);
    public static void SendEmote(EmoteType emoteType);
    public static void SendRandomArrowFromHand(int duration);
    public static void SendRandomArrowFromBoard(int duration);
    public static void SetMaxArenaPayments(int maxdraftscount);
    public static void SetCurrentArenaPayments(int currentdraftscount);
    public static void SendRandomHoverOnHand(int duration);
    public static void SendRandomHoverOnFriendlyMinions(int duration);
    public static void SendRandomHoverOnEnemyMinions(int duration);
    public static void Concede();
    public static void Hover(int duration, Card card);
    public static void Arrow(int duration, Card source, Card target);
    public static int GetAverageLatency();
    public static int GetMinLatency();
    public static int GetMaxLatency();
    public static void SetLatencySamplingRate(int milliseconds = 20000);
    public static void SetMinRank(int minrank);
    public static void SetMaxRank(int maxrank);
    public static void SetMaxWins(int maxwins);
    public static void SetMaxLosses(int maxlosses);
    public static void SetMaxHours(int maxhours);
    public static void SetCloseHs(bool enabled);
    public static void SetAutoConcede(bool enabled);
    public static void SetAutoConcedeAlternativeMode(bool enabled);
    public static void SetAutoConcedeMaxRank(int rank);
    public static void SetConcedeWhenLethal(bool enabled);
    public static void SetThinkingRoutineEnabled(bool enabled);
    public static void SetHoverRoutineEnabled(bool enabled);
    public static List<Quest> GetQuests();
    public static PlayerData GetPlayerDatas();
    public static long GetCurrentOpponentId();
    public static long GetPreviousOpponentId();
    public static bool CanCancelQuest();
    public static void CancelQuest(Quest quest);
    public static List<FriendRequest> GetFriendRequests();
    public static void AcceptFriendRequest(FriendRequest request);
    public static void DeclineFriendRequest(FriendRequest request);
    public static List<Friend> GetFriends();
    public static void RemoveFriend(Friend friend);
    public static void WhisperToFriend(Friend friend, string message);
}
```

## 14. SmartBot.Plugins.Plugin

```csharp
public class Plugin
{
    public string Name;
    public PluginDataContainer DataContainer;
    public bool IsDll;

    public bool IsEnabled() => DataContainer.Enabled;

    // 生命周期回调
    public virtual void OnPluginCreated() { }
    public virtual void OnTick() { }
    public virtual void OnDecklistUpdate() { }
    public virtual void OnDeckAutoCreated() { }
    public virtual void OnInjection() { }
    public virtual void OnStarted() { }
    public virtual void OnStartedFromAPI() { }
    public virtual void OnStopped() { }
    public virtual void OnTurnBegin() { }
    public virtual void OnTurnEnd() { }
    public virtual void OnSimulation() { }
    public virtual void OnGameBegin() { }
    public virtual void OnGameEnd() { }
    public virtual void OnGoldAmountChanged() { }
    public virtual void OnArenaEnd() { }
    public virtual void OnLethal() { }
    public virtual void OnVictory() { }
    public virtual void OnDefeat() { }
    public virtual void OnAllQuestsCompleted() { }
    public virtual void OnQuestCompleted() { }
    public virtual void OnArenaTicketPurchaseFailed() { }
    public virtual void OnConcede() { }
    public virtual void OnGameResolutionUpdate(int width, int height) { }
    public virtual void OnWhisperReceived(Friend friend, string message) { }
    public virtual void OnFriendRequestReceived(FriendRequest request) { }
    public virtual void OnFriendRequestAccepted(Friend friend) { }
    public virtual void OnActionExecute(Action action) { }
    public virtual void OnReceivedEmote(Bot.EmoteType emoteType) { }
    public virtual void OnActionStackReceived(List<Action> actions) { }
    public virtual void OnDataContainerUpdated() { }
    public virtual void OnSharedDataQueryResult(Dictionary<int, Dictionary<string, string>> results) { }
    public virtual void OnHandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass) { }
    public virtual void OnMulliganCardsReplaced(List<Card.Cards> replacedCards) { }

    public bool TryToWriteProperty(string propertyName, object value) { ... }
    public Dictionary<string, object> GetProperties() { ... }
    public virtual void Dispose() { }
    public virtual List<string> Args() => new List<string>();
}
```

## 15. SmartBot.Plugins.API.PlayerData

```csharp
public class PlayerData
{
    private readonly int _goldcap, _legendindex, _legendindexWild, _legendindexClassic;
    private readonly int _rank, _rankWild, _rankClassic;
    private readonly int _stars, _starsWild, _starsClassic;
    private readonly int _streakstars, _streakstarsWild, _streakstarsClassic;
    private readonly int _totalstars, _totalstarsWild, _totalstarsClassic;
    private readonly int _winstreak, _winstreakWild, _winstreakClassic;
    private readonly int DruidLevel, HunterLevel, MageLevel, PaladinLevel;
    private readonly int PriestLevel, RogueLevel, ShamanLevel, WarlockLevel;
    private readonly int WarriorLevel, DemonHunterLevel;
    private readonly GoldenHeroes _golden;

    public static Dictionary<Card.Cards, int> Collection;
    public static string Btag, Dust, RewardTrackLevel, PackCount;

    public GoldenHeroes GetGoldenHeroesDatas();
    public int GetRank(); // 按当前模式
    public int GetRank(Bot.Mode mode);
    public int GetStars();
    public int GetStars(Bot.Mode mode);
    public int GetStreakStars(Bot.Mode mode);
    public int GetStreakStars();
    public int GetWinStreak(Bot.Mode mode);
    public int GetWinStreak();
    public int GetTotalStars(Bot.Mode mode);
    public int GetTotalStars();
    public int GetLegendIndex(Bot.Mode mode);
    public int GetLegendIndex();
    public int GetGoldCapProgress();
    public int GetLevel(Card.CClass heroclass);
}
```

## 16. SmartBot.Plugins.API.Deck

```csharp
[Serializable]
public class Deck
{
    public enum DeckType { Standard, Wild, Casual, Twist }

    public List<string> Cards { get; set; }
    public Dictionary<string, List<string>> Sides { get; set; }
    public string Name { get; set; }
    public long Id { get; set; }
    public Card.CClass Class { get; set; }
    public DeckType Type { get; set; }

    public Deck()
    {
        Cards = new List<string>();
        Sides = new Dictionary<string, List<string>>();
    }

    public bool IsValid() => Cards.Count == 30;
}
```

## 17. SmartBot.Plugins.PluginDataContainer

```csharp
[Serializable]
[CategoryOrder("Plugin", 0)]
public class PluginDataContainer
{
    [Category("Plugin")]
    public bool Enabled { get; set; }

    [Category("Plugin")]
    public string Name { get; set; }
}
```

## 18. SmartBot.Plugins.API.GoldenHeroes

```csharp
public class GoldenHeroes
{
    public bool Warrior, Warlock, Hunter, Priest, Paladin, Rogue, Mage, Shaman, Druid, DemonHunter;
    public int WarriorWins, WarlockWins, HunterWins, PriestWins, PaladinWins;
    public int RogueWins, MageWins, ShamanWins, DruidWins, DemonHunterWins;
}
```

## 19. SmartBot.Plugins.API.Debug

```csharp
public class Debug
{
    public delegate void OnReceivedTextResult(string str);

    public static event OnReceivedTextResult OnAfterBoardReceived;
    public static event OnReceivedTextResult OnBeforeBoardReceived;
    public static event OnReceivedTextResult OnActionsReceived;
    public static event OnReceivedTextResult OnLogReceived;

    public static void SimulateSeed(string seed, string profile, bool autoconcede) { }
}
```

## 20. SmartBot.Plugins.API.TrapManager

```csharp
public class TrapManager
{
    public int SecretCount;
    public bool TriggeredCastMinion;
    public bool TriggeredHeroWithMinion;
    public bool TriggeredMinionWithMinion;

    public TrapManager(bool thwm = false, bool tmwm = false, bool tcm = false);
    public static TrapManager FromString(string str);
    public override string ToString();
    public float GetSecretModifier(Action a, Board b, bool firstAction = false);
}
```

## 21. SmartBot.Plugins.API.GUI

```csharp
public class GUI
{
    public delegate void OnScreenshotReceivedDel(Bitmap bmp);

    public static event OnScreenshotReceivedDel OnScreenshotReceived;

    public static void AddElement(GuiElement element) { }
    public static void RemoveElement(GuiElement element) { }
    public static void ClearUI() { }
    public static void TakeScreenshotToPath(string path, string filename) { }
    public static void RequestScreenshotToBitmap() { }
}
```

## 22. SmartBot.Plugins.API.RemoteProfile

```csharp
public class RemoteProfile
{
    protected List<string> _log = new List<string>();
    protected List<string> _logBestMove = new List<string>();

    public virtual float GetBoardValue(Board board) => 0f;
    public virtual void OnCastMinion(Board board, Card minion, Card target) { }
    public virtual void OnCastSpell(Board board, Card spell, Card target) { }
    public virtual void OnCastWeapon(Board board, Card weapon, Card target) { }
    public virtual void OnAttack(Board board, Card attacker, Card target) { }
    public virtual void OnCastAbility(Board board, Card ability, Card target) { }
    public virtual void OnMinionDeath(Board board, Card minion) { }
    public virtual void OnBoardInit(Board board) { }
    public void DebugBestMove(string message) { }
    public void Debug(string message) { }
    public virtual RemoteProfile DeepClone() => new RemoteProfile();
}
```
