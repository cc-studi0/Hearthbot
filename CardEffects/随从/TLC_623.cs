using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TLC_623 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 石雕工匠：回合结束时，随机使一个受伤的友方随从获得 +2/+2
            db.Register(C.TLC_623, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                var friends = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                var damaged = friends.Where(m => m.Health > 0 && m.Health < m.MaxHealth).ToList();
                if (damaged.Count == 0) return;

                var pick = damaged[CardEffectScriptHelpers.PickIndex(damaged.Count, b, s)];
                CardEffectScriptHelpers.Buff(pick, 2, 2);
            });
        }
    }
}
