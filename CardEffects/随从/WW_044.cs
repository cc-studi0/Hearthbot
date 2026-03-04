using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 列车机务工：亡语：获取一张淤泥桶。
    internal sealed class Sim_WW_044 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.WW_044, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                // 近似：获取一张衍生牌（淤泥桶 WW_044t）
                b.FriendCardDraw += 1;
            });
        }
    }
}
