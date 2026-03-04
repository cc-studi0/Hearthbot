using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_YOP_013 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponAura(C.YOP_013, (b, weapon) =>
            {
                if (b == null || weapon == null) return;
                var baseAtk = CardEffectScriptHelpers.GetBaseAtk(C.YOP_013, 1);
                var ownerHero = weapon == b.FriendWeapon ? b.FriendHero : b.EnemyHero;
                var hasArmor = ownerHero != null && ownerHero.Armor > 0;
                weapon.Atk = hasArmor ? baseAtk + 3 : baseAtk;
            });
        }
    }
}
