using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_EDR_889 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 鲜花商贩：回合结束时，随机使另一条友方龙获得 +1/+1
            db.Register(C.EDR_889, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                var friends = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                var dragons = friends
                    .Where(m => !ReferenceEquals(m, s) && m.Health > 0 && CardEffectScriptHelpers.IsDragonCard(m.CardId))
                    .ToList();
                if (dragons.Count == 0) return;

                var pick = dragons[CardEffectScriptHelpers.PickIndex(dragons.Count, b, s)];
                CardEffectScriptHelpers.Buff(pick, 1, 1);
            });
        }
    }
}
