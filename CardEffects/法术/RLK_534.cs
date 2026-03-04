using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 灵魂弹幕：当你使用或弃掉这张牌时，造成$6点伤害，随机分配到所有敌人身上。
    internal sealed class Sim_RLK_534 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 使用时触发：造成 6+SP 点伤害随机分配到敌方
            db.Register(C.RLK_534, EffectTrigger.Spell, (b, s, t) =>
            {
                int totalDmg = 6 + CardEffectDB.SP(b);

                var targets = new List<SimEntity>(b.EnemyMinions);
                if (b.EnemyHero != null) targets.Add(b.EnemyHero);

                if (targets.Count > 0)
                {
                    for (int i = 0; i < totalDmg; i++)
                    {
                        var alive = targets.Where(e => e.IsAlive).ToList();
                        if (alive.Count == 0) break;
                        int idx = CardEffectScriptHelpers.PickIndex(alive.Count, b, s);
                        CardEffectDB.Dmg(b, alive[idx], 1);
                    }
                }
            }, BattlecryTargetType.None);
        }
    }
}
