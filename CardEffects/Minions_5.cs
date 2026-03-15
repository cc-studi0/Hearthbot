// ═══════════════════════════════════════════════════════════════
//  标准随从效果 — 5 费随从
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

// 游戏主持奈姆希 3/6 — 已在 Final_Remaining 注册
    // 5费光环/触发
    public sealed class Script_Aura5 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "TOY_511","MIS_918","CORE_CFM_344","MIS_025",
                "VAC_501","ETC_522","JAM_037","MIS_026","CATA_206","VAC_918",
                "VAC_923","VAC_511","TTN_741","JAM_028","WORK_019","VAC_531","VAC_507" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); }
        }
    }

// ═══ 5费 ═══
    public sealed class Script_BC5 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "VAC_443","TOY_652","TOY_341","MIS_914","VAC_519",
                "CATA_722","VAC_423","TOY_521","TOY_385","ETC_428","TTN_842","VAC_529","TOY_383" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); }
        }
    }

// ── 彩翼灵龙 (CATA_133) 4/5 扰魔 回合结束：友方其他随从+1/+1。
    public sealed class Script_CATA_133 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_133", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                    if (!ReferenceEquals(m, s)) H.Buff(m, 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// ── 诺兹多姆 (CATA_473) 4/4 回合结束：圣盾或+3/+3。
    public sealed class Script_CATA_473 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_473", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                {
                    if (m.IsDivineShield) H.Buff(m, 3, 3);
                    else m.IsDivineShield = true;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// ── 青铜救赎者 (CATA_478) 3/3 回合结束：召属性值等同本随从的龙。
    public sealed class Script_CATA_478 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_478", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = s.Atk, Health = s.Health, MaxHealth = s.Health,
                    IsFriend = true, IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
        }
    }

// ── 厄索拉斯 (CATA_481) 5/3 战吼：吞食对手手牌。亡语：吐回。
    public sealed class Script_CATA_481 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_481", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.EnemyHand.Count > 0; i++)
                    b.EnemyHand.RemoveAt(H.PickIndex(b.EnemyHand.Count, b, s));
            });
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                b.EnemyHand.Add(new SimEntity());
                b.EnemyHand.Add(new SimEntity());
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 铜管元素 (ETC_357) 3/3 突袭圣盾嘲讽风怒。(纯白板)
    // 无需注册效果

    // ── 土元素 (CORE_EX1_250) 7/9 嘲讽 过载2。(纯白板)
    // 无需注册效果

    // ── 风领主奥拉基尔 (CORE_NEW1_010) 3/6 冲锋圣盾嘲讽风怒。(纯白板)
    // 无需注册效果

    // ── 飞行助翼 (CATA_564) 5/5 战吼：使友方随从获得超级风怒（无法打脸）。
    public sealed class Script_CATA_564 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_564", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null) return;
                t.IsWindfury = true;
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 毁灭之焰 (CATA_586) 3/3 亡语：随机对敌人造成2伤害。
    public sealed class Script_CATA_586 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_586", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var ts = new List<SimEntity>();
                ts.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) ts.Add(b.EnemyHero);
                if (ts.Count > 0) CardEffectDB.Dmg(b, ts[H.PickIndex(ts.Count, b, s)], 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 土石幼龙 (CATA_999) 4/4 回合结束对敌方英雄造成4伤害。
    public sealed class Script_CATA_999 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_999", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, 4);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 灵魂向导 (CORE_AV_328) 5/5 嘲讽 亡语：抽神圣法术+暗影法术。
    public sealed class Script_CORE_AV_328 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_AV_328", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                H.DrawRandomCardToHandByPredicate(b, H.IsSpellCard, s);
                H.DrawRandomCardToHandByPredicate(b, H.IsSpellCard, s);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 巴拉克·科多班恩 (CORE_BAR_551) 3/5 ──
    // 战吼：抽取法力值消耗为（1），（2）和（3）点的法术牌各一张。
    public sealed class Script_CORE_BAR_551 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BAR_551", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (int cost in new[] { 1, 2, 3 })
                {
                    int targetCost = cost;
                    H.DrawRandomCardToHandByPredicate(b,
                        cid => H.IsSpellCard(cid) && H.GetBaseCost(cid, 99) == targetCost, s);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 扎依，出彩艺人 (CORE_DMF_231) 6/4 ──
    // 战吼：复制你手牌中最左边和最右边的牌。
    public sealed class Script_CORE_DMF_231 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DMF_231", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.Hand.Count == 0) return;
                var copies = new List<SimEntity>();
                if (b.Hand.Count >= 1) copies.Add(b.Hand[0].Clone());
                if (b.Hand.Count >= 2) copies.Add(b.Hand[b.Hand.Count - 1].Clone());
                foreach (var c in copies)
                    if (b.Hand.Count < 10) b.Hand.Add(c);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 厚皮科多兽 CORE_BAR_535 已在Paladin注册，跳过

    // ── 格雷布 (CORE_DMF_734) 4/6 嘲讽 亡语：使友方随从获得"亡语：召格雷布"。
    public sealed class Script_CORE_DMF_734 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DMF_734", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ════════════ 5+费纯白板/关键词 ════════════
    // 荆棘谷猛虎 5/5 潜行
    public sealed class Script_CORE_EX1_028 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_EX1_028"); } }

// 土元素 7/9 嘲讽 过载2
    public sealed class Script_CORE_EX1_250 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_EX1_250"); } }

// ══════════════════ 5 费 ══════════════════

    // ── 末日守卫 (CORE_EX1_310) 5/7 冲锋 ──
    // 战吼：随机弃两张牌。
    public sealed class Script_CORE_EX1_310 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_310", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.Hand.Count > 0; i++)
                {
                    int idx = H.PickIndex(b.Hand.Count, b, s);
                    b.Hand.RemoveAt(idx);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 卑鄙的恐惧魔王 (CORE_ICC_075) 4/6 回合结束对所有敌方随从造成1伤害。
    public sealed class Script_CORE_ICC_075 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_ICC_075", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 馆长 (CORE_KAR_061) 4/6 嘲讽 ──
    // 战吼：抽取野兽牌，龙牌和鱼人牌各一张。
    public sealed class Script_CORE_KAR_061 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_KAR_061", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                H.DrawRandomCardToHandByPredicate(b, H.IsBeastCard, s);
                H.DrawRandomCardToHandByPredicate(b, H.IsDragonCard, s);
                // 鱼人无法检测种族，近似为抽1张
                b.FriendCardDraw += 1;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 蒸汽清洁器 (CORE_REV_946) 5/5 ──
    // 战吼：摧毁双方玩家牌库中所有套牌之外的牌。
    public sealed class Script_CORE_REV_946 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_REV_946", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 展馆茶壶 (CORE_WON_142) 3/3 ──
    // 战吼：随机使三个不同类型的友方随从获得+2/+2。
    public sealed class Script_CORE_WON_142 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_WON_142", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                int buffed = 0;
                foreach (var m in b.FriendMinions)
                {
                    if (ReferenceEquals(m, s) || buffed >= 3) continue;
                    H.Buff(m, 2, 2);
                    buffed++;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 泰兰·弗丁 (CS3_024) 3/3 嘲讽圣盾 亡语：抽费用最高的随从。
    public sealed class Script_CS3_024 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CS3_024", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 伦萨克大王 (CS3_025) 3/6 突袭 攻击时手牌随从+1/+1。(光环)
    public sealed class Script_CS3_025 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CS3_025", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 吉他独演者 (ETC_026) 4/3 ──
    // 战吼：如果你没有控制其他随从，抽一张法术牌/随从牌/武器牌。
    public sealed class Script_ETC_026 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_026", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                bool solo = b.FriendMinions.Count(m => !ReferenceEquals(m, s)) == 0;
                if (solo)
                {
                    H.DrawRandomCardToHandByPredicate(b, H.IsSpellCard, s);
                    H.DrawRandomCardToHandByPredicate(b, H.IsMinionCard, s);
                    H.DrawRandomCardToHandByPredicate(b, H.IsWeaponCard, s);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 歌剧独演者 (ETC_034) 4/6 ──
    // 战吼：如果你没有控制其他随从，对所有敌方随从造成3点伤害。
    public sealed class Script_ETC_034 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_034", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                bool solo = b.FriendMinions.Count(m => !ReferenceEquals(m, s)) == 0;
                if (solo)
                {
                    foreach (var m in b.EnemyMinions.ToArray())
                        CardEffectDB.Dmg(b, m, 3);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 鼓乐独演者 (ETC_035) 5/5 嘲讽 ──
    // 战吼：如果你没有控制其他随从，获得+2/+2和突袭。
    public sealed class Script_ETC_035 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_035", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                bool solo = b.FriendMinions.Count(m => !ReferenceEquals(m, s)) == 0;
                if (solo)
                {
                    H.Buff(s, 2, 2);
                    s.HasRush = true;
                    s.IsTired = false;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 末日管弦家林恩 (ETC_071) 3/6 嘲讽 亡语：双方抽2弃2毁牌库顶2。
    public sealed class Script_ETC_071 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_071", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2; i++)
                    CardEffectDB.DrawCard(b, b.FriendDeckCards);
                for (int i = 0; i < 2 && b.Hand.Count > 0; i++)
                    b.Hand.RemoveAt(H.PickIndex(b.Hand.Count, b, s));
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── MC布林顿 (ETC_078) 5/5 战吼：双方装备1/2麦克风。
    public sealed class Script_ETC_078 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_078", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                H.EquipWeaponByStats(b, true, 0, 1, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 幽灵写手 (ETC_088) 4/4 战吼：发现法术。
    public sealed class Script_ETC_088 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_088", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 舞王坎格尔 (ETC_329) 3/3 吸血 亡语：与手牌随从互换+吸血。
    public sealed class Script_ETC_329 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_329", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 因扎 (ETC_371) 5/5 战吼：本局过载牌-1费。
    public sealed class Script_ETC_371 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_371", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ── 盛夏花童 (ETC_376) 4/5 战吼：抽两张≥6费牌。压轴：减1费。
    public sealed class Script_ETC_376 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_376", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 乐坛灾星玛加萨 (JAM_036) 5/5 战吼：抽5张牌，法术牌给对手。
    public sealed class Script_JAM_036 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_036", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 5; i++)
                    CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// 火箭跳蛙 10/10 突袭 过载4
    public sealed class Script_MIS_306 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "MIS_306"); } }

// ── 积木魔像 (MIS_314) 6/3 突袭 亡语：召三个1费随从。
    public sealed class Script_MIS_314 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_314", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 3 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 死亡使者萨鲁法尔 (RLK_082) 4/6 嘲讽 亡语：移回手牌（消耗生命值）。
    public sealed class Script_RLK_082 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_082", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s == null || b.Hand.Count >= 10) return;
                b.Hand.Add(new SimEntity
                {
                    CardId = s.CardId, Atk = 4, Health = 6, MaxHealth = 6,
                    Cost = 5, IsFriend = true, IsTaunt = true, HasDeathrattle = true,
                    Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 僵尸新娘 (RLK_504) 4/4 ──
    // 战吼：消耗最多10份残骸，召唤一个攻击力和生命值等同于消耗残骸数并具有嘲讽的复活的新郎。
    public sealed class Script_RLK_504 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_504", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                int consume = Math.Min(10, b.FriendExcavateCount);
                if (consume <= 0 || b.FriendMinions.Count >= 7) return;
                b.FriendExcavateCount -= consume;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = consume, Health = consume, MaxHealth = consume,
                    IsFriend = true, IsTaunt = true, IsTired = true,
                    Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 滑冰元素 (TOY_375) 3/4 战吼：冻结敌方随从，获得等攻护甲。
    public sealed class Script_TOY_375 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_375", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null) return;
                t.IsFrozen = true;
                if (b.FriendHero != null) b.FriendHero.Armor += t.Atk;
            }, BattlecryTargetType.EnemyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 游戏主持奈姆希 (TOY_524) 3/6 战吼：抽恶魔牌。亡语：交换位置。
    public sealed class Script_TOY_524 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_524", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                H.DrawRandomCardToHandByPredicate(b, H.IsDemonCard, s));
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 天空慈母艾维娜 (TOY_806) 5/5 战吼：洗入10张1费传说随从。
    public sealed class Script_TOY_806 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_806", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 10; i++)
                    b.FriendDeckCards.Add(0); // 占位
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 玩具队长塔林姆 (TOY_813) 3/7 嘲讽 战吼：将一个随从的属性变为与本随从相同。
    public sealed class Script_TOY_813 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_813", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || s == null) return;
                t.Atk = s.Atk; t.Health = s.Health; t.MaxHealth = s.MaxHealth;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 废弃电子玩偶 (TOY_820) 4/6 回合结束：消灭攻击力低于本随从的随从。
    public sealed class Script_TOY_820 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_820", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null) return;
                var victim = b.EnemyMinions.Where(m => m.Atk < s.Atk && m.Health > 0)
                    .OrderBy(m => m.Health).FirstOrDefault();
                if (victim != null) victim.Health = 0;
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Destroy);
        }
    }

// ── 业余傀儡师 (TOY_828) 2/6 嘲讽 ──
    // 亡语：使你手牌中的亡灵牌获得+2/+2。
    public sealed class Script_TOY_828 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_828", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                // 近似：给手牌中所有随从 +2/+2
                H.BuffAllMinionsInHand(b, 2, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// ── 玩具医生斯缔修 (TOY_830) 5/4 亡语：召5费随从。
    public sealed class Script_TOY_830 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_830", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 5, Health = 5, MaxHealth = 5, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// 工坊保洁员 5/5 BC:控制地标→抽2
    public sealed class Script_TOY_891 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_891",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { CardEffectDB.DrawCard(b, b.FriendDeckCards); CardEffectDB.DrawCard(b, b.FriendDeckCards); }); } }

// ── 折纸青蛙 (TOY_894) 1/4 突袭 ──
    // 战吼：与另一个随从交换攻击力。
    public sealed class Script_TOY_894 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_894", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || t == null) return;
                int tmpAtk = s.Atk;
                s.Atk = t.Atk;
                t.Atk = tmpAtk;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 焰火机师 (TOY_908) 5/5 亡语：召唤两个1/1的砰砰机器人。
    public sealed class Script_TOY_908 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_908", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── XB-488清理机器人 (TTN_458) 3/2 ──
    // 战吼：造成5点伤害，随机分配到所有敌方随从身上。
    public sealed class Script_TTN_458 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_458", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 5; i++)
                {
                    var alive = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                    if (alive.Count == 0) break;
                    var pick = alive[H.PickIndex(alive.Count, b, s)];
                    CardEffectDB.Dmg(b, pick, 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 地铁司机 (TTN_723) 4/4 战吼：抽机械牌+下机械-2费。
    public sealed class Script_TTN_723 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_723", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                foreach (var c in b.Hand)
                    if (H.IsMechMinion(c.CardId)) { c.Cost = Math.Max(0, c.Cost - 2); break; }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw | EffectKind.Mana);
        }
    }

// ── 阿米图斯的信徒 (TTN_856) 4/5 回合结束：召2/2土灵（随土灵数+2/+2）。
    public sealed class Script_TTN_856 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_856", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 2, Health = 2, MaxHealth = 2, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
        }
    }

// ── 园林护卫者基利 (VAC_413) 4/6 亡语：手牌所有随从+2/+3。
    public sealed class Script_VAC_413 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_413", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                H.BuffAllMinionsInHand(b, 2, 3));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// 桑拿常客 5/5 嘲讽 受伤减费
    public sealed class Script_VAC_418 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_418",true,out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 挂机的阿凯 (VAC_446) 0/5 回合结束：未攻击的友方随从+2/+2。
    public sealed class Script_VAC_446 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_446", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                    if (!ReferenceEquals(m, s) && m.CountAttack == 0)
                        H.Buff(m, 2, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// ── 食肉格块 (WORK_042) 4/6 战吼：消灭友方随从，回合结束召其复制。
    public sealed class Script_WORK_042 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_042", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || !t.IsFriend) return;
                t.Health = 0;
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ── 混沌之眼 (YOG_515) 5/4 战吼：获取两张1/1混乱触须。
    public sealed class Script_YOG_515 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_515", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 腐化残渣 (YOG_519) 7/4 战吼：上回合用过元素→7伤害随机分配敌人。
    public sealed class Script_YOG_519 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_519", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：总是触发
                for (int i = 0; i < 7; i++)
                {
                    var ts = new List<SimEntity>();
                    ts.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                    if (b.EnemyHero != null) ts.Add(b.EnemyHero);
                    if (ts.Count == 0) break;
                    CardEffectDB.Dmg(b, ts[H.PickIndex(ts.Count, b, s)], 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

}
