using System.Collections.Generic;

namespace HearthstonePayload
{
    public enum GameResult { None, Win, Loss, Tie }

    /// <summary>
    /// 游戏状态数据模型
    /// </summary>
    public class GameStateData
    {
        public int MaxMana;
        public int EnemyMaxMana;
        public int ManaAvailable;
        public int Overload;
        public int LockedMana;
        public int TurnCount;
        public int Step;
        public int FriendlyPlayerId;
        public int CurrentPlayerId;
        public int FriendlyMulliganState;
        public bool IsMulliganPhase;
        public int FriendDeckCount;
        public int EnemyDeckCount;
        public List<string> FriendDeck = new List<string>();  // 我方牌库中每张牌的 CardId
        public int EnemyHandCount;
        public int EnemySecretCount;
        public bool IsOurTurn;
        public int FriendClass;
        public int EnemyClass;

        // Seed 补全字段
        public int FriendFatigue;
        public int EnemyFatigue;
        public bool IsCombo;
        public bool LockAndLoad;
        public bool SpellsCostHealth;
        public bool EmbraceTheShadow;
        public bool Stampede;
        public bool ElemBuffEnabled;
        public int CardsPlayedThisTurn;
        public int ElemPlayedLastTurn;
        public int BaseMinionDiedThisTurnFriend;
        public int BaseMinionDiedThisTurnEnemy;
        public int CthunAttack;
        public int CthunHealth;
        public bool CthunTaunt;
        public int JadeGolem;
        public int JadeGolemEnemy;
        public int HeroPowerCountThisTurn;

        // 任务数据
        public int QuestFriendlyProgress;
        public string QuestFriendlyId = "";
        public int QuestFriendlyTotal;
        public int QuestFriendlyReward;
        public int QuestEnemyProgress;
        public string QuestEnemyId = "";
        public int QuestEnemyTotal;
        public int QuestEnemyReward;

        // 扩展字段 (parts[55]-[66])
        public int HealAmountThisGame;
        public int HeroPowerDamagesThisGame;
        public bool IsExtraTurn;
        public int ManaTemp;
        public int NumFriendlyMinionsDiedThisGame;
        public int NumEnemyMinionsDiedThisGame;
        public int NumSpellsCastThisGame;
        public int NumWeaponsPlayedThisGame;
        public int NumHeroPowersThisGame;
        public int NumCardsDrawnThisTurn;
        public int NumCardsPlayedThisGame;
        public int NumSecretsPlayedThisGame;

        public bool IsGameOver;
        public string EndGameScreenClass = "";
        public GameResult Result;
        public bool FriendlyConceded;

        public EntityData HeroFriend;
        public EntityData HeroEnemy;
        public EntityData AbilityFriend;
        public EntityData AbilityEnemy;
        public EntityData WeaponFriend;
        public EntityData WeaponEnemy;

        public List<EntityData> MinionFriend = new List<EntityData>();
        public List<EntityData> MinionEnemy = new List<EntityData>();
        public List<EntityData> Hand = new List<EntityData>();

        public List<string> SecretsFriend = new List<string>();
        public List<string> GraveyardFriend = new List<string>();
        public List<int> GraveyardFriendTurn = new List<int>();
        public List<string> GraveyardEnemy = new List<string>();
        public List<int> GraveyardEnemyTurn = new List<int>();
    }

    /// <summary>
    /// 单个实体数据
    /// </summary>
    public class EntityData
    {
        public string CardId;
        public int EntityId;
        public int Atk;
        public int Health;
        public int Damage;
        public int Armor;
        public int Cost;
        public int Durability;
        public int SpellPower;
        public int AttackCount;
        public int ZonePosition;

        public bool Taunt;
        public bool DivineShield;
        public bool Charge;
        public int WindfuryValue;
        public bool Windfury => WindfuryValue > 0;
        public bool CantAttack;
        public bool Dormant;
        public bool Stealth;
        public bool Frozen;
        public bool Freeze;
        public bool Silenced;
        public bool Immune;
        public bool Poisonous;
        public bool Lifesteal;
        public bool Rush;
        public bool Reborn;
        public bool Exhausted;
        public int NumTurnsInPlay;

        // Seed 实体补全字段
        public int TempAtk;
        public bool IsEnraged;
        public bool IsInspire;
        public bool IsTargetable = true;
        public bool IsGenerated;
        public int CountPlayed;
        public bool IsPowered;
        public bool CanAttackHeroes = true;
        public bool HasEcho;
        public bool IsCombo;

        public Dictionary<int, int> Tags = new Dictionary<int, int>();
    }

    public class FriendlyEntityContextEntry
    {
        public int EntityId;
        public string CardId;
        public string Zone;
        public int ZonePosition;
        public bool IsGenerated;
        public int CreatorEntityId;
        public Dictionary<int, int> Tags = new Dictionary<int, int>();
    }
}
