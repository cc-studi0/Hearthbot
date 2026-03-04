using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CFM_717 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.CFM_717, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                var side = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                if (side.Count < 7)
                {
                    var golem = new SimEntity
                    {
                        CardId = C.CFM_712_t01,
                        IsFriend = (s == null || s.IsFriend),
                        Type = SmartBot.Plugins.API.Card.CType.MINION,
                        Atk = 1,
                        Health = 1,
                        MaxHealth = 1
                    };
                    side.Add(golem);
                }
            });
        }
    }
}
