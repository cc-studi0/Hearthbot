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
        public bool HasBattlecry, HasDeathrattle;
        public int CountAttack;
        public Card.CType Type;

        public bool CanAttack =>
            Atk > 0 && !IsFrozen &&
            CountAttack < (IsWindfury ? 2 : 1) &&
            !IsTired;

        public bool IsAlive => Health > 0;

        public SimEntity Clone() => (SimEntity)MemberwiseClone();
    }
}
