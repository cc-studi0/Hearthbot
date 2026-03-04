using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_AV_402 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.AV_402, (b, hero, target, ctx) =>
            {
                if (b == null || ctx == null || !ctx.HonorableKill) return;
                if (b.Hand.Count >= 10) return;

                var deck = (hero == b.FriendHero) ? b.EnemyDeckCards : b.FriendDeckCards;
                if (deck.Count > 0)
                {
                    var topCard = deck[0];
                    var card = CardEffectScriptHelpers.CreateCardInHand(topCard, hero == b.FriendHero, b);
                    if (card != null)
                        b.Hand.Add(card);
                }
            });
        }
    }
}
