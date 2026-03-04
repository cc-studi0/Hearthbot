using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TRL_352 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponAura(C.TRL_352, (b, weapon) =>
            {
                if (b == null || weapon == null) return;
                var hero = (weapon == b.FriendWeapon) ? b.FriendHero : b.EnemyHero;
                if (hero != null && hero.OverloadedCrystals > 0)
                    weapon.Atk = CardEffectScriptHelpers.GetBaseAtk(C.TRL_352, 2) + 2;
            });
        }
    }
}
