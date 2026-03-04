using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_WC_025 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.WC_025, (b, hero, target, ctx) =>
            {
                if (b == null || hero != b.FriendHero) return;
                CardEffectScriptHelpers.BuffRandomMinionInHand(b, 1, 0, hero);
            });
        }
    }
}
