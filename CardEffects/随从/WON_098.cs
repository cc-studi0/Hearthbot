using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 镀银魔像：如果你弃掉了这张随从牌，则会召唤它。
    internal sealed class Sim_WON_098 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 正常打出无特殊战吼，弃牌召唤无模拟基础设施
            // 注册空战吼确保脚本被加载
            db.Register(C.WON_098, EffectTrigger.Battlecry, (b, s, t) =>
            {
            }, BattlecryTargetType.None);
        }
    }
}
