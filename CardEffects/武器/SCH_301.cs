using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SCH_301 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.SCH_301, (b, hero, target, ctx) =>
            {
                if (b == null || hero == null) return;
                hero.SpellPower += 1;
            });
        }
    }
}
