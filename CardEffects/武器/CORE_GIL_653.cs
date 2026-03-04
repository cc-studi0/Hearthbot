using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_GIL_653 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.CORE_GIL_653, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b == null) return;
                var allies = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                if (allies.Count > 0)
                {
                    var target = allies[CardEffectDB.Rnd.Next(allies.Count)];
                    target.Atk += 2;
                    target.Health += 1;
                }
            });
        }
    }
}
