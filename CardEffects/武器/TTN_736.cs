using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TTN_736 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.TTN_736, (b, hero, target, ctx) =>
            {
                if (b == null || hero == null) return;

                var plaguePool = CardEffectScriptHelpers.KnownPlaguePool;
                if (hero == b.FriendHero)
                {
                    b.EnemyDeckPlagueCount += 1;
                    if (plaguePool.Length > 0)
                        b.EnemyDeckCards.Add(plaguePool[CardEffectScriptHelpers.PickIndex(plaguePool.Length, b, hero, target)]);
                }
                else if (hero == b.EnemyHero)
                {
                    if (plaguePool.Length > 0)
                        b.FriendDeckCards.Add(plaguePool[CardEffectScriptHelpers.PickIndex(plaguePool.Length, b, hero, target)]);
                }
            });
        }
    }
}
