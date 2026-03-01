using System;
using System.Collections.Generic;
using System.Linq;

namespace BotMain
{
    /// <summary>
    /// 简易 AI 引擎：解析 Seed，生成最优操作序列
    /// </summary>
    public class SimpleAI
    {
        /// <summary>
        /// 根据 Seed 字符串生成操作列表
        /// </summary>
        public List<string> DecideActions(string seed)
        {
            var actions = new List<string>();
            if (string.IsNullOrEmpty(seed)) return actions;

            var parts = seed.Split('~');
            if (parts.Length < 21) return actions;

            int mana = ParseInt(parts[1]);
            var hand = ParseEntityList(parts[20]);
            var friendlyMinions = ParseEntityList(parts[18]);
            var enemyMinions = ParseEntityList(parts[19]);
            var heroEnemy = ParseEntity(parts[15]);
            var enemyTargetId = heroEnemy?.EntityId ?? 0;

            // 1. 使用英雄技能（如果有足够法力）
            var heroPower = ParseEntity(parts[16]);
            if (heroPower != null && heroPower.Cost <= mana && !heroPower.Exhausted)
            {
                actions.Add($"HERO_POWER|{heroPower.EntityId}|{enemyTargetId}");
                mana -= heroPower.Cost;
            }

            // 2. 打出手牌（按费用从高到低）
            var playable = hand.Where(c => c.Cost <= mana).OrderByDescending(c => c.Cost).ToList();
            foreach (var card in playable)
            {
                if (card.Cost > mana) continue;
                // 默认以敌方英雄为目标，非指向性卡牌会忽略此参数
                actions.Add($"PLAY|{card.EntityId}|{enemyTargetId}|0");
                mana -= card.Cost;
            }

            // 3. 随从攻击（优先解嘲讽，然后打脸）
            var tauntTargets = enemyMinions.Where(m => m.Taunt).ToList();
            foreach (var minion in friendlyMinions.Where(m => !m.Exhausted && m.Atk > 0))
            {
                if (tauntTargets.Count > 0)
                {
                    var target = tauntTargets[0];
                    actions.Add($"ATTACK|{minion.EntityId}|{target.EntityId}");
                    if (minion.Atk >= target.Health - target.Damage)
                        tauntTargets.RemoveAt(0);
                }
                else if (heroEnemy != null)
                {
                    actions.Add($"ATTACK|{minion.EntityId}|{heroEnemy.EntityId}");
                }
            }

            // 4. 结束回合
            actions.Add("END_TURN");
            return actions;
        }

        private List<SimpleEntity> ParseEntityList(string data)
        {
            if (string.IsNullOrEmpty(data)) return new List<SimpleEntity>();
            return data.Split('|').Select(ParseEntity).Where(e => e != null).ToList();
        }

        private SimpleEntity ParseEntity(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            var p = data.Split('*');
            if (p.Length < 10) return null;
            return new SimpleEntity
            {
                CardId = p[0],
                Atk = ParseInt(p[3]),
                Cost = ParseInt(p[4]),
                Damage = ParseInt(p[5]),
                EntityId = ParseInt(p[7]),
                Health = ParseInt(p[8]),
                Exhausted = p.Length > 17 && p[17] == "True",
                Taunt = p.Length > 15 && p[15] == "True"
            };
        }

        private int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;

        private class SimpleEntity
        {
            public string CardId;
            public int EntityId, Atk, Cost, Damage, Health;
            public bool Exhausted, Taunt;
        }
    }
}
