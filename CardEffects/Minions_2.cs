// ═══════════════════════════════════════════════════════════════
//  标准随从效果 — 2 费随从
//  按费用段分类整理
// ═══════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;
using H = BotMain.AI.CardEffectsScripts.CardEffectScriptHelpers;
using R = BotMain.AI.CardEffectScriptRuntime;

namespace BotMain.AI.CardEffectsScripts
{

// 死灵殡葬师 | 鲜血魔术师(2版) BC:消耗残骸发现
    public sealed class Script_BC2_DK : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "CORE_RLK_116","RLK_066","CORE_RLK_066" })
            {
                if (!Enum.TryParse(n,true,out C id)) continue;
                db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { if (b.FriendExcavateCount > 0) { b.FriendExcavateCount--; b.FriendCardDraw += 1; } });
            }
        }
    }

// ═══ 2费战吼 ═══
    // 蔽影密探 发现法术 | 避难的幸存者 换牌 | 潮池学徒 发现 | 风潮浪客 发现
    // 高端玩家 抽牌 | 扩音机 法力上限11 | 旅行社职员 发现地标 | 面具变装大师 发现
    // 伪信徒 发现 | 血帆征兵员 发现海盗 | 卡亚矿石造物 发现
    // 全部近似为 draw+1
    public sealed class Script_BC2_Discover : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "CATA_614","CATA_721","VAC_304","ETC_103","MIS_916",
                "ETC_087","VAC_438","VAC_336","TTN_484","VAC_430","TTN_925" })
            {
                if (!Enum.TryParse(n,true,out C id)) continue;
                db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1);
                db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            }
        }
    }

// 历战无面者 | 封面艺人 BC:变形复制
    public sealed class Script_BC_Copy : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "YOG_501","ETC_110" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); }
        }
    }

// 费伍德树人 2/2 BC:获临时法力水晶
    public sealed class Script_CATA_131 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_131",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.Mana += 1); } }

// 速逝鱼人 1/1 BC:下张鱼人消耗生命
    public sealed class Script_CATA_180 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_180",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 无私的保卫者 2/6 嘲讽 受伤+1
    public sealed class Script_CATA_208 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CATA_208"); } }

// ── 黑翼实验品 (CATA_464) 3/1 亡语：获取一张2费伤害法术。
    public sealed class Script_CATA_464 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_464", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// 祈雨元素 1/4 法术造伤+2攻
    public sealed class Script_CATA_487 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_487", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 天空之墙哨兵 0/3 嘲讽 BC:兆示
    public sealed class Script_CATA_565 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_565",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 霜冻小鬼 (CATA_612) 5/3 ──
    // 战吼：冻结本随从。
    public sealed class Script_CATA_612 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_612", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null) s.IsFrozen = true;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 暗誓信徒 (CATA_725) 2/1 ──
    // 战吼：兆示。亡语：为你的英雄恢复3点生命值。
    public sealed class Script_CATA_725 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_725", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendHero != null)
                    b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + 3);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Heal);
        }
    }

// ── 凶恶的雨云 (CORE_BOT_533) 2/3 ──
    // 战吼：随机将一张元素牌置入你的手牌。
    public sealed class Script_CORE_BOT_533 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_BOT_533",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 污手街供货商 (CORE_CFM_753) 2/2 ──
    // 战吼：使你手牌中的所有随从牌获得+1/+1。
    public sealed class Script_CORE_CFM_753 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CFM_753", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                H.BuffAllMinionsInHand(b, 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 卑劣的脏鼠 (CORE_CFM_790) 2/6 嘲讽 ──
    // 战吼：使你的对手随机从手牌中召唤一个随从。
    public sealed class Script_CORE_CFM_790 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CFM_790", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：从对手手牌召唤一个随从
                var minions = b.EnemyHand.Where(c => c.Type == Card.CType.MINION).ToList();
                if (minions.Count > 0 && b.EnemyMinions.Count < 7)
                {
                    var pick = minions[H.PickIndex(minions.Count, b, s)];
                    b.EnemyHand.Remove(pick);
                    pick.IsTired = true;
                    b.EnemyMinions.Add(pick);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 狗头人地卜师 2/2 法伤+1
    public sealed class Script_CORE_CS2_142 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_CS2_142"); } }

// ── 奖品商贩 (CORE_DMF_067) 2/3 ──
    // 战吼，亡语：每个玩家抽一张牌。
    public sealed class Script_CORE_DMF_067 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DMF_067", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ═══════ 圣骑士 PALADIN ═══════

    // ── 赤鳞驯龙者 (CORE_DMF_194) 2/3 亡语：抽一张龙牌。
    public sealed class Script_CORE_DMF_194 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DMF_194", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                H.DrawRandomCardToHandByPredicate(b, H.IsDragonCard, s));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 狐人老千 (CORE_DMF_511) 3/2 ──
    // 战吼：你的下一张连击牌法力值消耗减少（2）点。
    public sealed class Script_CORE_DMF_511 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DMF_511", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ═══════════ 常见亡语 ═══════════

    // ── 血法师萨尔诺斯 (CORE_EX1_012) 1/1 法伤+1 亡语：抽牌。
    public sealed class Script_CORE_EX1_012 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_012", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 年轻的酒仙 (CORE_EX1_049) 3/2 ──
    // 战吼：使一个友方随从从战场上移回你的手牌。
    public sealed class Script_CORE_EX1_049 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_EX1_049",
                new TriggerDef("Battlecry", "FriendlyMinion",
                    new EffectDef("bounce")));
    }

// ── 日怒保卫者 (CORE_EX1_058) 2/3 ──
    // 战吼：使相邻的随从获得嘲讽。
    public sealed class Script_CORE_EX1_058 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_058", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 简化：给所有友方随从嘲讽（因为模拟器不追踪位置）
                // 但只给非自己的
                foreach (var m in b.FriendMinions)
                    if (!ReferenceEquals(m, s)) m.IsTaunt = true;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 疯狂的炼金师 (CORE_EX1_059) 2/2 ──
    // 战吼：使一个随从的攻击力和生命值互换。
    public sealed class Script_CORE_EX1_059 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_059", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || t.Type != Card.CType.MINION) return;
                int tmpAtk = t.Atk;
                t.Atk = t.Health;
                t.Health = tmpAtk;
                t.MaxHealth = tmpAtk;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 疯狂投弹者 (CORE_EX1_082) 3/2 ──
    // 战吼：造成3点伤害，随机分配到所有其他角色身上。
    public sealed class Script_CORE_EX1_082 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_082", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：3点伤害分配到敌方场面
                var targets = new List<SimEntity>();
                targets.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                targets.AddRange(b.FriendMinions.Where(m => m.Health > 0 && !ReferenceEquals(m, s)));
                if (b.FriendHero != null) targets.Add(b.FriendHero);
                for (int i = 0; i < 3 && targets.Count > 0; i++)
                {
                    var pick = targets[H.PickIndex(targets.Count, b, s)];
                    CardEffectDB.Dmg(b, pick, 1);
                    targets = targets.Where(x => x.Health > 0).ToList();
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 战利品贮藏者 (CORE_EX1_096) 2/1 亡语：抽牌。
    public sealed class Script_CORE_EX1_096 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_096", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 游学者周卓 (CORE_EX1_100) 0/4 施放法术→复制给对手。
    public sealed class Script_CORE_EX1_100 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_100", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ═══════ 盗贼 ROGUE ═══════

    // ── 迪菲亚头目 (CORE_EX1_131) 3/2 连击：召2/1迪菲亚强盗。
    public sealed class Script_CORE_EX1_131 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_131", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 连击近似：本回合打过牌就触发
                if (b.CardsPlayedThisTurn > 0 && b.FriendMinions.Count < 7)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 2, Health = 1, MaxHealth = 1, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 恐狼前锋 (CORE_EX1_162) 2/2 相邻随从+1攻。
    public sealed class Script_CORE_EX1_162 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_162", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 银色保卫者 (CORE_EX1_362) 3/2 ──
    // 战吼：使一个其他友方随从获得圣盾。
    public sealed class Script_CORE_EX1_362 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_EX1_362",
                new TriggerDef("Battlecry", "FriendlyMinion",
                    new EffectDef("give_ds")));
    }

// ── 鱼人猎潮者 (CORE_EX1_506) 2/1 ──  (属于2费但放这里因为是经典核心卡)
    // 战吼：召唤一个1/1的鱼人斥候。
    public sealed class Script_CORE_EX1_506 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_EX1_506",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("summon", atk: 1, hp: 1)));
    }

// ═══════ 萨满 SHAMAN ═══════

    // ── 电击学徒 (CS3_007) 3/2 法伤+1 过载1。(无需注册效果,白板带法伤)

    // ── 火舌图腾 (CORE_EX1_565) 0/3 光环：相邻随从+2攻。
    public sealed class Script_CORE_EX1_565 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_565", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 蛛魔之卵 (CORE_FP1_007) 0/2 亡语：召4/4蛛魔。
    public sealed class Script_CORE_FP1_007 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_FP1_007", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 4, Health = 4, MaxHealth = 4, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ════════════ 2费纯白板/关键词 ════════════
    // 吵吵机器人 1/2 嘲讽+圣盾
    public sealed class Script_CORE_GVG_085 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_GVG_085"); } }

// ── 暗影升腾者 (CORE_ICC_210) 2/3 回合结束：使另一个友方随从+1/+1。
    public sealed class Script_CORE_ICC_210 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_ICC_210", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                var ts = b.FriendMinions.Where(m => m.Health > 0 && !ReferenceEquals(m, s)).ToList();
                if (ts.Count > 0) H.Buff(ts[H.PickIndex(ts.Count, b, s)], 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// ── 虚空幽龙史学家 (CORE_KAR_062) 2/3 ──
    // 战吼：如果你的手牌中有龙牌，便发现一张龙牌。
    public sealed class Script_CORE_KAR_062 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_KAR_062", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (H.HasDragonInHand(b, s))
                    b.FriendCardDraw += 1;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 粗俗的矮劣魔 (CORE_LOOT_013) 2/4 嘲讽 ──
    // 战吼：对你的英雄造成2点伤害。
    public sealed class Script_CORE_LOOT_013 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_LOOT_013", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Health -= 2;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 硬壳甲虫 (CORE_LOOT_413) 2/3 亡语：获得3点护甲。
    public sealed class Script_CORE_LOOT_413 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_LOOT_413", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Armor += 3;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// ── 血帆袭击者 (CORE_NEW1_018) 2/3 ──
    // 战吼：获得等同于你的武器攻击力的攻击力。
    public sealed class Script_CORE_NEW1_018 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_NEW1_018", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                int weaponAtk = b.FriendWeapon?.Atk ?? 0;
                s.Atk += weaponAtk;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 狂野炎术师 (CORE_NEW1_020) 3/2 施放法术后对所有随从1伤害。
    public sealed class Script_CORE_NEW1_020 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_NEW1_020", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ═══════════ 常见光环（标记让评估器知晓价值） ═══════════

    // ── 末日预言者 (CORE_NEW1_021) 0/7 下回合开始消灭所有随从。
    public sealed class Script_CORE_NEW1_021 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_NEW1_021", true, out C id)) return;
            // 特殊：放下末日意味着下回合开始全场清
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 精灵龙 3/2 扰魔
    public sealed class Script_CORE_NEW1_023 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_NEW1_023"); } }

// ── 幽暗城商贩 (CORE_OG_330) 2/3 亡语：获取对手职业卡牌。
    public sealed class Script_CORE_OG_330 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_OG_330", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 迷宫向导 (CORE_REV_308) 1/1 ──
    // 战吼：随机召唤一个法力值消耗为（2）的随从。
    public sealed class Script_CORE_REV_308 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_REV_308", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                // 近似：召唤一个 2/2
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 2, Health = 2, MaxHealth = 2,
                    IsFriend = true, IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 异教低阶牧师 (CORE_SCH_713) 3/2 ──
    // 战吼：下个回合你的对手法术的法力值消耗增加（1）点。
    // 模拟中无法追踪跨回合效果，标记为 noop
    public sealed class Script_CORE_SCH_713 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_SCH_713", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// 游荡贤者 2/2 流放→左右手牌-1费
    public sealed class Script_CORE_TSC_217 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_TSC_217", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 受伤的托维尔人 (CORE_ULD_271) 2/6 嘲讽 ──
    // 战吼：对本随从造成3点伤害。
    public sealed class Script_CORE_ULD_271 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_ULD_271", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null) s.Health -= 3;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 恐鳞追猎者 (CORE_UNG_800) 2/3 ──
    // 战吼：触发一个友方随从的亡语。
    public sealed class Script_CORE_UNG_800 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_UNG_800", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || !t.HasDeathrattle) return;
                if (!db.TryGet(t.CardId, EffectTrigger.Deathrattle, out var drFn)) return;
                drFn(b, t, null);
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 黑市摊贩 (CORE_WON_096) 2/3 ──
    // 战吼：发现一张法力值消耗为（1）的卡牌。
    public sealed class Script_CORE_WON_096 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_WON_096",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 雾帆劫掠者 (CS3_022) 2/2 ──
    // 战吼：如果你装备着武器，造成2点伤害。
    public sealed class Script_CS3_022 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CS3_022", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendWeapon != null && b.FriendWeapon.Health > 0 && t != null)
                    CardEffectDB.Dmg(b, t, 2);
            }, BattlecryTargetType.EnemyOnly);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// 红鳃锋颚战士 3/1 突袭
    public sealed class Script_CS3_038 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CS3_038"); } }

// 次中音号小鬼 2/2 BC:受疲劳伤→+属性
    public sealed class Script_ETC_068 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_068",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 八爪碟机 4/1 连击获连击牌
    public sealed class Script_ETC_077 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_077", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 立体声图腾 (ETC_105) 0/3 回合结束：使手牌随从+2/+2。
    public sealed class Script_ETC_105 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_105", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
                H.BuffRandomMinionInHand(b, 2, 2, s));
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// 音乐治疗师 2/3 突袭 压轴吸血
    public sealed class Script_ETC_325 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "ETC_325"); } }

// ── 梦中男神 (ETC_332) 1/2 ──
    // 战吼：为所有其他友方随从恢复3点生命值。每有一个被过量治疗，便获得+1/+1。
    public sealed class Script_ETC_332 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_332", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                foreach (var m in b.FriendMinions)
                {
                    if (ReferenceEquals(m, s)) continue;
                    int before = m.Health;
                    m.Health = Math.Min(m.MaxHealth, m.Health + 3);
                    int overHeal = 3 - (m.Health - before);
                    if (overHeal > 0 && before >= m.MaxHealth)
                        H.Buff(s, 1, 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Heal | EffectKind.Buff);
        }
    }

// ── 派对动物 (ETC_350) 2/3 ──
    // 战吼：使你手牌中每个不同类型的各一张随从牌获得+1/+1。
    public sealed class Script_ETC_350 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_350", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：给手牌中几张随从 +1/+1
                var seen = new HashSet<string>();
                foreach (var c in b.Hand)
                {
                    if (c.Type != Card.CType.MINION) continue;
                    // 按种族区分"类型"太复杂，近似为给最多3张随从+1/+1
                    string key = c.CardId.ToString();
                    if (seen.Add(key) && seen.Count <= 3)
                        H.Buff(c, 1, 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 潮流猎豹 (ETC_385) 2/1 战吼+亡语：英雄技能多+1攻。
    public sealed class Script_ETC_385 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_385", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 暗影之眼 (ETC_398) 2/3 ──
    // 你的英雄拥有吸血。(光环)
    public sealed class Script_ETC_398 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_398", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 蛇啮鼓手 1/1 突袭 BC:每死亡随从+1/+1
    public sealed class Script_ETC_410 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_410",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 乐器技师 (ETC_418) 1/2 ──
    // 战吼：抽一张武器牌。
    public sealed class Script_ETC_418 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_418", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                H.DrawRandomCardToHandByPredicate(b, H.IsWeaponCard, s);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 音频切分机 (ETC_536) 3/2 亡语：复制手牌中费用最高的法术。
    public sealed class Script_ETC_536 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_536", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 摇滚巨石 (ETC_742) 2/2 突袭 ──
    // 战吼：如果你使用的上一张牌法力值消耗为（1）点，便获得+1/+1。
    public sealed class Script_ETC_742 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_742", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：如果已使用过牌，就给 buff（保守估计）
                if (s != null && b.CardsPlayedThisTurn > 0)
                    H.Buff(s, 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 箭矢工匠 2/3 施法后对最低血敌人1伤
    public sealed class Script_ETC_833 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_833", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ═══════════ 回合结束效果 ═══════════

    // ── 点唱机图腾 (JAM_010) 0/4 回合结束：召1/1白银之手新兵。
    public sealed class Script_JAM_010 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_010", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
        }
    }

// 单曲流星 2/1 突袭 连击获剧毒
    public sealed class Script_JAM_021 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "JAM_021"); } }

// 人气树精 2/3 抉择
    public sealed class Script_JAM_026 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("JAM_026",true,out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 饭圈迷弟 2/2 抉择
    public sealed class Script_JAM_027 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("JAM_027",true,out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 战场通灵师 (RLK_061) 2/2 回合结束：将一份残骸复活为1/3嘲讽。
    public sealed class Script_RLK_061 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_061", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7 || b.FriendExcavateCount <= 0) return;
                b.FriendExcavateCount--;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 1, Health = 3, MaxHealth = 3, IsFriend = true,
                    IsTaunt = true, IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
        }
    }

// 死亡寒冰 (RLK_083/CORE_RLK_083) 2/3 施法后对两个随机敌人1伤
    public sealed class Script_RLK_083 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "RLK_083", "CORE_RLK_083" })
            {
                if (!Enum.TryParse(n, true, out C id)) continue;
                db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
            }
        }
    }

// ── 寒冬先锋 (RLK_511) 3/2 ──
    // 亡语：抽一张冰霜法术牌。
    public sealed class Script_RLK_511 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_511", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                H.DrawRandomCardToHandByPredicate(b, H.IsSpellCard, s);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 凶恶的血虫 (RLK_711) 3/2 ──
    // 战吼：使你手牌中的一张随从牌获得等同于本随从攻击力的攻击力。
    public sealed class Script_RLK_711 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_711", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                H.BuffRandomMinionInHand(b, s.Atk, 0, s);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 团队之灵 0/3 潜行 英雄回合+2攻
    public sealed class Script_TOY_028 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_028", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 漆彩帆布龙 3/2 BC:友方野兽获随机效果
    public sealed class Script_TOY_350 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_350",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 纸艺天使 2/3 英雄技能0费
    public sealed class Script_TOY_381 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_381", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 玩具船 2/3 召唤海盗后抽牌
    public sealed class Script_TOY_505 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_505", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 发条树苗 (TOY_802) 2/1 战吼：复原1个法力水晶。
    public sealed class Script_TOY_802 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_802", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => b.Mana += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ── 蹒跚的僵尸坦克 (TOY_827) 3/2 嘲讽 ──
    // 战吼：消耗5份残骸以召唤一个本随从的复制。
    public sealed class Script_TOY_827 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_827", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                if (b.FriendExcavateCount >= 5)
                {
                    b.FriendExcavateCount -= 5;
                    var copy = s.Clone();
                    copy.IsTired = true;
                    b.FriendMinions.Add(copy);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 太阳看护者 2/3 BC:获神圣法术
    public sealed class Script_TTN_039 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_039",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); } }

// 酷冰机器人 2/3 磁力 冻结受伤角色
    public sealed class Script_TTN_077 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_077"); } }

// 神话观测者 1/4 召唤高攻随从后全体友方+1攻
    public sealed class Script_TTN_078 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_078", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 吸附寄生体 3/1 磁力突袭 可附机械和野兽
    public sealed class Script_TTN_087 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_087"); } }

// 流水档案管理员 2/2 BC:下张元素-2费
    public sealed class Script_TTN_095 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_095",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 困苦的克瓦迪尔 (TTN_450) 3/2 ──
    // 亡语：随机将两张疫病牌洗入你对手的牌库。
    public sealed class Script_TTN_450 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_450", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                b.EnemyDeckPlagueCount += 2;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 蔽刺触手 (TTN_456) 2/2 吸血 ──
    // 战吼：随机对一个敌方随从造成2点伤害。
    public sealed class Script_TTN_456 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_456", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                var enemies = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                if (enemies.Count > 0)
                {
                    var pick = enemies[H.PickIndex(enemies.Count, b, s)];
                    CardEffectDB.Dmg(b, pick, 2);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// 岩肤护甲商 2/2 BC:护甲变化→抽2
    public sealed class Script_TTN_469 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_469",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 愤怒焚炉 1/3 磁力 溅射
    public sealed class Script_TTN_471 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_471"); } }

// ── 烈焰亡魂 (TTN_479) 2/3 召唤元素后+1/+1。
    public sealed class Script_TTN_479 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_479", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 发明机器人 2/3 磁力吸附时+1/+1
    public sealed class Script_TTN_732 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_732", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 星空投影师 3/2 BC:友方随从复制到手
    public sealed class Script_TTN_742 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_742",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); } }

// ── 高戈奈斯的信徒 (TTN_833) 2/3 回合结束：使手牌中过载牌-1费。
    public sealed class Script_TTN_833 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_833", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.Hand.Count > 0)
                {
                    var pick = b.Hand[H.PickIndex(b.Hand.Count, b, s)];
                    pick.Cost = Math.Max(0, pick.Cost - 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Mana);
        }
    }

// 晶石雕像 5/4 突袭 休眠 抽4牌后唤醒
    public sealed class Script_TTN_840 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_840"); } }

// 崇高的迷你机 2/3 磁力 攻击后手牌随从+1/+1
    public sealed class Script_TTN_852 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_852", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 脆骨海盗 1/4 亡语随从获复生
    public sealed class Script_VAC_436 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_436", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 饮品侍者 (VAC_461) 2/2 亡语：获取一张饮品法术。
    public sealed class Script_VAC_461 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_461", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 恐惧猎犬训练师 (VAC_514) 2/2 突袭 ──
    // 亡语：召唤一只1/1并具有复生的恐惧猎犬。
    public sealed class Script_VAC_514 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_514", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true,
                    HasReborn = true, IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// 椰子火炮手 1/4 相邻攻击→打1
    public sealed class Script_VAC_532 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_532", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 王牌发球手 2/3 获得攻击力后→手牌最高费-1
    public sealed class Script_VAC_920 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_920", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 狂飙邪魔 2/2 海盗攻击后英雄+1攻
    public sealed class Script_VAC_927 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_927", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 无畏的火焰杂耍者 1/1 BC:获受伤量属性
    public sealed class Script_VAC_942 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_942",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// XB-931型家政机 2/3 地标效果后+3护甲
    public sealed class Script_VAC_956 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_956", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 宠物鹦鹉 1/1 BC:重复上张1费牌
    public sealed class Script_VAC_961 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_961",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 忙碌机器人 (WORK_002) 3/2 ──
    // 战吼：使你攻击力为1的随从获得+1/+1。
    public sealed class Script_WORK_002 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_002", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                    if (m.Atk == 1 && !ReferenceEquals(m, s))
                        H.Buff(m, 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 月度魔范员工 (WORK_009) 3/2 ──
    // 战吼：使一个友方随从获得吸血。
    public sealed class Script_WORK_009 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "WORK_009",
                new TriggerDef("Battlecry", "FriendlyMinion",
                    new EffectDef("give_lifesteal")));
    }

// ── 饮水图腾 (WORK_011) 0/3 回合结束：相邻随从+1/+1。
    public sealed class Script_WORK_011 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_011", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null) return;
                int idx = b.FriendMinions.IndexOf(s);
                if (idx < 0) return;
                if (idx > 0) H.Buff(b.FriendMinions[idx - 1], 1, 1);
                if (idx < b.FriendMinions.Count - 1) H.Buff(b.FriendMinions[idx + 1], 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// ── 暴富特使 (WORK_031) 4/4 ──
    // 战吼：将你手牌中法力值消耗最高的牌置于你的牌库顶。
    public sealed class Script_WORK_031 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_031", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                var highest = b.Hand.Where(c => !ReferenceEquals(c, s))
                    .OrderByDescending(c => c.Cost).FirstOrDefault();
                if (highest != null)
                {
                    b.Hand.Remove(highest);
                    b.FriendDeckCards.Insert(0, highest.CardId);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 忙碌的苦工 (WORK_041) 2/3 亡语：你的下一张地标牌-2费。
    public sealed class Script_WORK_041 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_041", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Mana);
        }
    }

// ── 萨隆铁矿蹒跚者 (YOG_521) 2/3 ──
    // 战吼：如果在本局对战中施放过5个法术，英雄本回合获得+4攻击力。
    public sealed class Script_YOG_521 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_521", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：已施放过法术就给英雄攻击力
                if (b.CardsPlayedThisTurn > 0 && b.FriendHero != null)
                    b.FriendHero.Atk += 4;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

}
