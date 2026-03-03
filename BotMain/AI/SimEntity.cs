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

                // Board.CanAttack 在个别版本会出现“可攻击=true 但攻击值/状态不满足”的短暂错位，
                // 这里改为“提示 + 本地状态”双重校验，避免产生无效攻击动作。
                return UseBoardCanAttack
                    ? BoardCanAttack && localReady
                    : localReady;
            }
        }

        public bool IsAlive => Health > 0;

        public SimEntity Clone() => (SimEntity)MemberwiseClone();
    }
}
