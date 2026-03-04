using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 异教低阶牧师：战吼：下个回合你的对手的法术的法力值消耗增加（1）点。
    // 模拟中无法直接影响对手下回合费用，登记为空战吼占位。
    // AI 价值由 Profile 评分体现。
    internal sealed class Sim_CORE_SCH_713 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.CORE_SCH_713, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 战吼效果：对手下回合法术+1费
                // 在模拟中难以精确体现（影响对手未来回合），
                // 这里留空，让 Profile 对该卡给出合理评分即可。
            }, BattlecryTargetType.None);
        }
    }
}
