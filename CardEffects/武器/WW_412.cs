using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_WW_412 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.WW_412, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b == null) return;
                // Excavate 奖励链未建模，先记录挖掘次数。
                b.FriendExcavateCount += 1;
            });
        }
    }
}
