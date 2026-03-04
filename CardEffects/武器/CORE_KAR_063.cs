using System.Linq;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_KAR_063 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponAura(C.CORE_KAR_063, (b, weapon) =>
            {
                if (b == null || weapon == null) return;
                var baseAtk = CardEffectScriptHelpers.GetBaseAtk(C.CORE_KAR_063, 1);
                var hasSpellPower = weapon == b.FriendWeapon
                    ? b.FriendMinions.Any(m => m.SpellPower > 0)
                    : b.EnemyMinions.Any(m => m.SpellPower > 0);
                weapon.Atk = hasSpellPower ? baseAtk + 2 : baseAtk;
            });
        }
    }
}
