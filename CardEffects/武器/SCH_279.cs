using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;
using CType = SmartBot.Plugins.API.Card.CType;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SCH_279 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterAfterHeroAttackWeapon(C.SCH_279, (b, hero, target, ctx) =>
            {
                if (b == null || target == null || target.Type != CType.MINION) return;

                var allies = hero == b.FriendHero ? b.FriendMinions : hero == b.EnemyHero ? b.EnemyMinions : null;
                if (allies == null) return;

                foreach (var m in allies.ToArray())
                {
                    if (!target.IsAlive) break;
                    if (m == null || !m.IsAlive || m.Type == CType.LOCATION || m.Atk <= 0) continue;

                    CardEffectDB.Dmg(b, target, m.Atk);
                    if (target.IsAlive && target.Atk > 0)
                        CardEffectDB.Dmg(b, m, target.Atk);
                }
            });
        }
    }
}
