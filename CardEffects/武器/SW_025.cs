using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SW_025 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.SW_025, (b, hero, target, ctx) =>
            {
                if (b == null) return;
                var hand = (hero == b.FriendHero) ? b.Hand : b.EnemyHand;
                foreach (var card in hand)
                {
                    if (CardEffectScriptHelpers.IsMinionCard(card.CardId) &&
                        CardEffectScriptHelpers.HasBattlecry(card.CardId))
                    {
                        card.Cost = System.Math.Max(0, card.Cost - 1);
                        break;
                    }
                }
            });
        }
    }
}
