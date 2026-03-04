using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 超光子弹幕：造成$6点伤害，随机分配到所有敌人身上。将2张时空撕裂洗入你的牌库。
    internal sealed class Sim_TIME_027 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.TIME_027, EffectTrigger.Spell, (b, s, t) =>
            {
                int totalDmg = 6 + CardEffectDB.SP(b);

                // 收集所有敌方目标
                var targets = new List<SimEntity>(b.EnemyMinions);
                if (b.EnemyHero != null) targets.Add(b.EnemyHero);

                if (targets.Count > 0)
                {
                    // 将伤害随机分配到所有敌人身上
                    for (int i = 0; i < totalDmg; i++)
                    {
                        // 过滤掉已死亡的目标
                        var alive = targets.Where(e => e.IsAlive).ToList();
                        if (alive.Count == 0) break;
                        int idx = CardEffectScriptHelpers.PickIndex(alive.Count, b, s);
                        CardEffectDB.Dmg(b, alive[idx], 1);
                    }
                }

                // 洗入 2 张时空撕裂到牌库
                if (Enum.TryParse("TIME_027t", true, out C riftId))
                {
                    b.FriendDeckCards.Add(riftId);
                    b.FriendDeckCards.Add(riftId);
                }
            }, BattlecryTargetType.None);
        }
    }
}
