using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_GDB_231 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.GDB_231, (b, hero, target, ctx) =>
            {
                if (b == null) return;
                var hand = (hero == b.FriendHero) ? b.Hand : b.EnemyHand;
                foreach (var card in hand)
                {
                    if (CardEffectScriptHelpers.IsDraeneiMinion(card.CardId))
                        card.Atk += 2;
                }
            });
        }
    }
}
