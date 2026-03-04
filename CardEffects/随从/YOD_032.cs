using System;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 狂暴邪翼蝠：在本回合中，你的对手每受到1点伤害，本牌的法力值消耗便减少（1）点。
    internal sealed class Sim_YOD_032 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // Aura：根据敌方英雄已受伤害量近似减费
            db.Register(C.YOD_032, EffectTrigger.Aura, (b, s, t) =>
            {
                if (s == null) return;
                int baseCost = CardEffectScriptHelpers.GetBaseCost(C.YOD_032, 4);

                // 近似：敌方英雄已损失的生命值作为本回合受伤量的下限估计
                int enemyDamageTaken = 0;
                if (b.EnemyHero != null)
                    enemyDamageTaken = Math.Max(0, b.EnemyHero.MaxHealth - b.EnemyHero.Health);

                s.Cost = Math.Max(0, baseCost - enemyDamageTaken);
            });
        }
    }
}
