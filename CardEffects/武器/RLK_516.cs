using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_RLK_516 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.RLK_516, (b, hero, target, ctx) =>
            {
                if (b == null || ctx == null || !ctx.TargetWasMinion) return;
                if (hero == b.FriendHero && b.EnemyHero != null)
                    CardEffectDB.Dmg(b, b.EnemyHero, 2);
                else if (hero == b.EnemyHero && b.FriendHero != null)
                    CardEffectDB.Dmg(b, b.FriendHero, 2);
            });
        }
    }
}
