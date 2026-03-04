using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DMF_524 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.DMF_524, (b, hero, target, ctx) =>
            {
                if (b == null) return;
                var hand = (hero == b.FriendHero) ? b.Hand : b.EnemyHand;
                foreach (var card in hand)
                {
                    if (CardEffectScriptHelpers.IsMechMinion(card.CardId) ||
                        CardEffectScriptHelpers.IsDragonMinion(card.CardId) ||
                        CardEffectScriptHelpers.IsPirateMinion(card.CardId))
                    {
                        card.Atk += 1;
                        card.Health += 1;
                    }
                }
            });
        }
    }
}
