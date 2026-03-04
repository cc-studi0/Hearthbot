using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_BT_922 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.CORE_BT_922, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b == null) return;
                var side = (s != null && !s.IsFriend) ? b.EnemyMinions : b.FriendMinions;
                CardEffectDB.Summon(b, side, C.BT_922t);
                CardEffectDB.Summon(b, side, C.BT_922t);
            });
        }
    }
}
