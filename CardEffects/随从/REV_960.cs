using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 灰烬元素：战吼：下个回合，每当你的对手抽牌时，对手会受到2点伤害。
    // 模拟中无法追踪对手下回合抽牌事件，
    // 近似：对手下回合至少抽1张（回合开始抽牌），预扣2点伤害。
    internal sealed class Sim_REV_960 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.REV_960, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 对手下回合至少抽1张牌（回合开始），近似造成2点伤害
                if (b.EnemyHero != null)
                    CardEffectDB.Dmg(b, b.EnemyHero, 2);
            }, BattlecryTargetType.None);
        }
    }
}
