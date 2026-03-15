using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HearthstonePayload
{
    /// <summary>
    /// 战旗模式状态快照，与构筑模式的 GameStateData 完全独立。
    /// 序列化为管道协议字符串供 BotService 消费。
    /// </summary>
    public sealed class BattlegroundStateData
    {
        // ── 阶段与回合 ──
        /// <summary>
        /// 当前阶段：RECRUIT（招募阶段）/ COMBAT（战斗阶段）/ HERO_PICK（英雄选择）/ UNKNOWN
        /// </summary>
        public string Phase { get; set; } = "UNKNOWN";
        public int Turn { get; set; }
        public bool IsOurTurn { get; set; }
        public bool IsGameOver { get; set; }
        public string GameResult { get; set; } = "NONE"; // WIN / LOSS / NONE

        // ── 经济 ──
        public int Gold { get; set; }
        public int MaxGold { get; set; }

        // ── 酒馆 ──
        public int TavernTier { get; set; }
        public int UpgradeCost { get; set; }
        public bool CanUpgrade { get; set; }
        public bool CanReroll { get; set; }
        public int RerollCost { get; set; }
        public bool IsFrozen { get; set; }

        // ── 英雄 ──
        public int HeroEntityId { get; set; }
        public string HeroCardId { get; set; } = "";
        public int HeroHealth { get; set; }
        public int HeroArmor { get; set; }

        // ── 英雄技能 ──
        public int HeroPowerEntityId { get; set; }
        public string HeroPowerCardId { get; set; } = "";
        public bool HeroPowerAvailable { get; set; }
        public int HeroPowerCost { get; set; }
        public List<BgHeroPowerData> HeroPowers { get; set; } = new List<BgHeroPowerData>();

        // ── 商店随从 ──
        public List<BgMinionData> ShopCards { get; set; } = new List<BgMinionData>();

        // ── 手牌 ──
        public List<BgMinionData> HandCards { get; set; } = new List<BgMinionData>();

        // ── 场上随从 ──
        public List<BgMinionData> BoardMinions { get; set; } = new List<BgMinionData>();

        // ── 玩家信息 ──
        public int PlayerCount { get; set; }
        public int Placement { get; set; } // 当前名次（0=未知）

        /// <summary>
        /// 序列化为管道传输格式。
        /// 格式: KEY1=VAL1|KEY2=VAL2|...
        /// 列表项用 ; 分隔，子字段用 , 分隔。
        /// </summary>
        public string Serialize()
        {
            var sb = new StringBuilder(512);
            sb.Append("PHASE=").Append(Phase);
            sb.Append("|TURN=").Append(Turn);
            sb.Append("|OUR_TURN=").Append(IsOurTurn ? "1" : "0");
            sb.Append("|GAME_OVER=").Append(IsGameOver ? "1" : "0");
            sb.Append("|RESULT=").Append(GameResult);
            sb.Append("|GOLD=").Append(Gold);
            sb.Append("|MAX_GOLD=").Append(MaxGold);
            sb.Append("|TIER=").Append(TavernTier);
            sb.Append("|UP_COST=").Append(UpgradeCost);
            sb.Append("|CAN_UP=").Append(CanUpgrade ? "1" : "0");
            sb.Append("|CAN_REROLL=").Append(CanReroll ? "1" : "0");
            sb.Append("|REROLL_COST=").Append(RerollCost);
            sb.Append("|FROZEN=").Append(IsFrozen ? "1" : "0");
            sb.Append("|HERO=").Append(HeroEntityId).Append(",").Append(HeroCardId).Append(",").Append(HeroHealth).Append(",").Append(HeroArmor);
            sb.Append("|HP=").Append(HeroPowerEntityId).Append(",").Append(HeroPowerCardId).Append(",").Append(HeroPowerAvailable ? "1" : "0").Append(",").Append(HeroPowerCost);
            sb.Append("|HPS=").Append(SerializeHeroPowerList(HeroPowers));
            sb.Append("|SHOP=").Append(SerializeMinionList(ShopCards));
            sb.Append("|HAND=").Append(SerializeMinionList(HandCards));
            sb.Append("|BOARD=").Append(SerializeMinionList(BoardMinions));
            sb.Append("|PLAYERS=").Append(PlayerCount);
            sb.Append("|PLACE=").Append(Placement);
            return sb.ToString();
        }

        private static string SerializeMinionList(List<BgMinionData> list)
        {
            if (list == null || list.Count == 0) return "";
            return string.Join(";", list.Select(m => m.Serialize()));
        }

        private static string SerializeHeroPowerList(List<BgHeroPowerData> list)
        {
            if (list == null || list.Count == 0) return "";
            return string.Join(";", list.Select(power => power.Serialize()));
        }
    }

    /// <summary>
    /// 战旗随从/卡牌数据（用于商店、手牌、场上）
    /// </summary>
    public sealed class BgMinionData
    {
        public int EntityId { get; set; }
        public string CardId { get; set; } = "";
        public string CardName { get; set; } = "";
        public int Attack { get; set; }
        public int Health { get; set; }
        public int TavernTier { get; set; }
        public int ZonePosition { get; set; }
        public bool IsGolden { get; set; }
        public bool IsTaunt { get; set; }
        public bool IsDivineShield { get; set; }
        public bool IsWindfury { get; set; }
        public bool IsVenomous { get; set; }
        public bool IsReborn { get; set; }
        public int Cost { get; set; }

        /// <summary>
        /// 序列化为紧凑格式: entityId,cardId,atk,hp,tier,pos,flags,cost
        /// flags: G=golden T=taunt D=divine S=windfury V=venomous R=reborn
        /// </summary>
        public string Serialize()
        {
            var flags = new StringBuilder(6);
            if (IsGolden) flags.Append('G');
            if (IsTaunt) flags.Append('T');
            if (IsDivineShield) flags.Append('D');
            if (IsWindfury) flags.Append('S');
            if (IsVenomous) flags.Append('V');
            if (IsReborn) flags.Append('R');
            return $"{EntityId},{CardId},{Attack},{Health},{TavernTier},{ZonePosition},{flags},{Cost}";
        }
    }

    public sealed class BgHeroPowerData
    {
        public int EntityId { get; set; }
        public string CardId { get; set; } = "";
        public bool IsAvailable { get; set; }
        public int Cost { get; set; }
        public int Index { get; set; }

        public string Serialize()
        {
            return $"{EntityId},{CardId},{(IsAvailable ? "1" : "0")},{Cost},{Index}";
        }
    }
}
