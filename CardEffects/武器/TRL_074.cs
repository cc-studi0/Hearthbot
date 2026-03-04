using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TRL_074 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.TRL_074, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b == null) return;
                var allies = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                foreach (var m in allies)
                    m.HasRush = true;
            });
        }
    }
}
