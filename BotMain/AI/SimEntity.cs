using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public class SimEntity
    {
        public Card.Cards CardId;
        public int EntityId, Atk, Health, MaxHealth, Armor, Cost, SpellPower;
        public bool IsFriend, IsTaunt, IsDivineShield, IsWindfury, HasPoison;
        public bool IsLifeSteal, HasReborn, IsFrozen, IsImmune, IsSilenced, IsStealth;
        public bool HasCharge, HasRush, IsTired;
        public bool IsTradeable;
        public bool HasBattlecry, HasDeathrattle;
        public bool EnrageBonusActive;
        public int CountAttack;
        public int EffectCounter1;
        public int EffectCounter2;
        public int OverloadedCrystals;
        public Card.CType Type;
        public bool UseBoardCanAttack;
        public bool BoardCanAttack;

        public bool CanAttack
        {
            get
            {
                var maxAttackCount = IsWindfury ? 2 : 1;
                var localReady =
                    Atk > 0
                    && !IsFrozen
                    && !IsTired
                    && CountAttack < maxAttackCount;

                if (!UseBoardCanAttack)
                    return localReady;

                // 英雄攻击：Board.CanAttack 在装备武器等场景下不总是及时更新为 true，
                // 而 EXHAUSTED tag 也可能残留为 1 导致 localReady 为 false。
                // 对英雄使用 OR 逻辑：任一来源认为能攻击即可，避免漏掉合法的英雄攻击。
                if (Type == Card.CType.HERO)
                    return BoardCanAttack || localReady;

                // 随从仍使用严格的 AND 校验，避免产生无效攻击动作。
                return BoardCanAttack && localReady;
            }
        }

        public bool IsAlive => Health > 0;

        // 兼容旧脚本里将武器耐久当成独立字段的写法
        public int Durability
        {
            get => Health;
            set
            {
                Health = value;
                if (value > MaxHealth) MaxHealth = value;
            }
        }

        public SimEntity Clone() => (SimEntity)MemberwiseClone();
    }
}
