using System;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 狡诈的郊狼：潜行。在本回合中，敌方英雄每受到一次伤害，本牌的法力值消耗便减少（1）点。
    internal sealed class Sim_TIME_047 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 战吼：确保潜行
            db.Register(C.TIME_047, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null)
                    s.IsStealth = true;
            }, BattlecryTargetType.None);

            // Aura：根据敌方英雄已受伤害量近似减费
            db.Register(C.TIME_047, EffectTrigger.Aura, (b, s, t) =>
            {
                if (s == null) return;
                int baseCost = CardEffectScriptHelpers.GetBaseCost(C.TIME_047, 5);

                // 近似：敌方英雄已损失的生命值作为本回合受伤次数的下限估计
                int enemyDamageTaken = 0;
                if (b.EnemyHero != null)
                    enemyDamageTaken = Math.Max(0, b.EnemyHero.MaxHealth - b.EnemyHero.Health);

                s.Cost = Math.Max(0, baseCost - enemyDamageTaken);
            });
        }
    }
}
