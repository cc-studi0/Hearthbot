using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_LOOT_044 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponAura(C.LOOT_044, (b, weapon) =>
            {
                if (b == null || weapon == null) return;
                var hero = (weapon == b.FriendWeapon) ? b.FriendHero : b.EnemyHero;
                if (hero != null)
                    weapon.Atk = hero.Armor;
            });
        }
    }
}
