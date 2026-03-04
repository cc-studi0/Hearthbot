using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DEEP_014 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.DEEP_014, (b, hero, target, ctx) =>
            {
                if (b == null) return;
                if (hero == b.FriendHero)
                    CardEffectDB.DrawCard(b, b.FriendDeck);
                else if (hero == b.EnemyHero)
                    CardEffectDB.DrawCard(b, b.EnemyDeck);
            });
        }
    }
}
