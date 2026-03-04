using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_GVG_043 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.GVG_043, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                var allies = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                if (allies.Count > 0)
                {
                    var target = allies[CardEffectDB.Rnd.Next(allies.Count)];
                    target.Atk += 1;
                }
            });
        }
    }
}
