// ═══════════════════════════════════════════════════════════════
//  标准随从效果 — 3 费随从
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

// ═══ 3费战吼 ═══
    public sealed class Script_BC3_Discover : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "ETC_326","CORE_ULD_209","VAC_523","TOY_054",
                "WORK_027","MIS_006","VAC_332","TOY_307","TTN_718","ETC_113",
                "CATA_697","VAC_955","VAC_957","TTN_712","TOY_520","TOY_809" })
            {
                if (!Enum.TryParse(n,true,out C id)) continue;
                db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1);
                db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            }
        }
    }

// 装甲放血纳迦 3/1 突袭 BC:兆示 | 托维尔雕琢师 3/2 BC:手牌-1费
    public sealed class Script_BC3_Misc : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "CATA_525","CATA_566" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); }
        }
    }

// ── 晦鳞巢母 (CATA_111) 4/3 ──
    // 战吼：如果你的手牌中有龙牌，复原两个法力水晶。
    public sealed class Script_CATA_111 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_111", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (H.HasDragonInHand(b, s))
                    b.Mana = Math.Min(b.MaxMana, b.Mana + 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ── 癫狂的追随者 (CATA_158) 3/1 潜行 亡语：兆示。
    public sealed class Script_CATA_158 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_158", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 残恶梦魇 (CATA_161) 3/3 ──
    // 战吼：使你手牌中或战场上的一个随从获得等同于本随从攻击力的攻击力。
    public sealed class Script_CATA_161 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_161", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null && s != null)
                    t.Atk += s.Atk;
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 无面复制者 (CATA_185) 3/3 扰魔 亡语：变形击杀它的随从。
    public sealed class Script_CATA_185 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_185", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 战场轰炸手 3/4 BC:法术获法伤+1
    public sealed class Script_CATA_209 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_209",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 受伤的侍者 (CATA_304) 3/8 吸血 ──
    // 战吼：对本随从造成4点伤害。
    public sealed class Script_CATA_304 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_304", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null) s.Health -= 4;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 魔眼秘术师 (CATA_490) 3/6 嘲讽 战吼：弃掉一张手牌。
    public sealed class Script_CATA_490 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_490", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.Hand.Count > 0)
                    b.Hand.RemoveAt(H.PickIndex(b.Hand.Count, b, s));
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// 石爪打击者 3/3 嘲讽 手牌变6/6龙
    public sealed class Script_CATA_551 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_551", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 雷鸣流云 (CATA_563) 4/3 战吼：吸收手牌法术。亡语：施放。
    public sealed class Script_CATA_563 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_563", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 吸收手牌中一个法术 → 移除
                var spells = b.Hand.Where(c => H.IsSpellCard(c.CardId)).ToList();
                if (spells.Count > 0)
                    b.Hand.Remove(spells[H.PickIndex(spells.Count, b, s)]);
            });
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 宝石囤积者 (CATA_897) 3/4 战吼：弃牌。亡语：取回弃牌-1费。
    public sealed class Script_CATA_897 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_897", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.Hand.Count > 0)
                    b.Hand.RemoveAt(H.PickIndex(b.Hand.Count, b, s));
            });
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// 咒术专家 3/4 BC:法术拆分两张
    public sealed class Script_CATA_979 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_979",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); } }

// ── 沼泽之子 (CORE_BT_115) 3/4 ──
    // 战吼：如果你在上回合施放过法术，发现一张法术牌。
    public sealed class Script_CORE_BT_115 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_115", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 简化：本回合用过法术就给发现
                if (b.CardsPlayedThisTurn > 0) b.FriendCardDraw += 1;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// 收集者沙库尔 2/4 潜行 攻击→获另一职业牌
    public sealed class Script_CORE_CFM_781 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CORE_CFM_781", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 团队领袖 (CORE_CS2_122) 2/3 其他随从+1攻。
    public sealed class Script_CORE_CS2_122 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_122", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 拉祖尔女士 (CORE_DAL_729) 3/2 战吼：发现对手手牌的复制。
    public sealed class Script_CORE_DAL_729 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DAL_729", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 青铜龙探险者 (CORE_DRG_229) 3/3 吸血 ──
    // 战吼：发现一张龙牌。
    public sealed class Script_CORE_DRG_229 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_DRG_229",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 喷灯破坏者 (CORE_DRG_403) 3/3 ──
    // 战吼：你对手的下一个英雄技能的法力值消耗增加（2）点。
    public sealed class Script_CORE_DRG_403 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DRG_403", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 呓语魔橱 (CORE_EDR_001) 2/4 战吼：获取2张法师法术。
    public sealed class Script_CORE_EDR_001 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EDR_001", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 法瑞克 (CORE_EDR_003) 2/4 战吼：抽一张消耗残骸的牌。
    public sealed class Script_CORE_EDR_003 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EDR_003", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { }); // 残骸翻倍光环
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 苦痛侍僧 (CORE_EX1_007) 1/4 每当受伤抽牌。
    public sealed class Script_CORE_EX1_007 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_007", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 穆克拉 (CORE_EX1_014) 5/6 ──
    // 战吼：使你的对手获得两根香蕉。
    public sealed class Script_CORE_EX1_014 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_014", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 给对手加两张香蕉（1费 buff +1/+1 法术）
                for (int i = 0; i < 2 && b.EnemyHand.Count < 10; i++)
                    b.EnemyHand.Add(new SimEntity
                    {
                        Cost = 1, Type = Card.CType.SPELL, IsFriend = false
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 寒光先知 (CORE_EX1_103) 2/3 ──
    // 战吼：使你的其他鱼人获得+2生命值。
    public sealed class Script_CORE_EX1_103 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_103", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：给所有其他友方随从 +2 生命值
                // （无法精确区分鱼人种族）
                foreach (var m in b.FriendMinions)
                    if (!ReferenceEquals(m, s)) H.Buff(m, 0, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 军情七处特工 (CORE_EX1_134) 3/3 连击：造成3伤害。
    public sealed class Script_CORE_EX1_134 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_134", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.CardsPlayedThisTurn > 0 && t != null)
                    CardEffectDB.Dmg(b, t, 3);
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 鱼人领军 (CORE_EX1_507) 3/3 其他鱼人+2攻。
    public sealed class Script_CORE_EX1_507 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_507", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ═══════ 战士 WARRIOR ═══════

    // ── 暴乱狂战士 (CORE_EX1_604) 2/4 每当随从受伤+1攻。(光环)
    public sealed class Script_CORE_EX1_604 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_604", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 荆棘帮暴徒 (CORE_GIL_534) 3/3 英雄攻击后+1/+1。
    public sealed class Script_CORE_GIL_534 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_GIL_534", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── A3型机械金刚 (CORE_LOE_039) 3/4 ──
    // 战吼：如果你控制着其他机械，发现一张机械牌。
    public sealed class Script_CORE_LOE_039 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_LOE_039", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                bool hasMech = b.FriendMinions.Any(m =>
                    !ReferenceEquals(m, s) && H.IsMechMinion(m.CardId));
                if (hasMech)
                    b.FriendCardDraw += 1;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 南海船长 (CORE_NEW1_027) 3/3 你的其他海盗+1/+1。
    public sealed class Script_CORE_NEW1_027 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_NEW1_027", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 钩拳-3000型 4/3 英雄攻击后+4护甲+抽牌
    public sealed class Script_CORE_NX2_028 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CORE_NX2_028", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 暴虐食尸鬼 (CORE_OG_149) 3/3 ──
    // 战吼：对所有其他随从造成1点伤害。
    public sealed class Script_CORE_OG_149 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_OG_149", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions.ToArray())
                    if (!ReferenceEquals(m, s)) CardEffectDB.Dmg(b, m, 1);
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 拆迁修理工 (CORE_REV_023) 3/3 可交易 ──
    // 战吼：摧毁一个敌方地标。
    public sealed class Script_CORE_REV_023 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_REV_023", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null && t.Type == Card.CType.LOCATION)
                    b.EnemyMinions.Remove(t);
            }, BattlecryTargetType.EnemyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ── 死亡侍僧 (CORE_RLK_121) 2/4 ──
    // 在一个友方亡灵死亡后，抽一张牌。(光环)
    public sealed class Script_CORE_RLK_121 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_RLK_121", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 货物保镖 (CORE_SW_030) 2/4 回合结束获得3护甲。
    public sealed class Script_CORE_SW_030 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_SW_030", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Armor += 3;
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// ── 锈烂蝰蛇 (CORE_SW_072) 3/4 可交易 ──
    // 战吼：摧毁对手的武器。
    public sealed class Script_CORE_SW_072 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_SW_072",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("destroy_weapon")));
    }

// ── 焦油爬行者 (CORE_UNG_928) 1/5 嘲讽 对手回合+2攻。
    public sealed class Script_CORE_UNG_928 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_UNG_928", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ════════════════ 恶魔猎手 DEMONHUNTER ════════════════

    // ── 邪能响尾蛇 (CORE_WC_701) 3/2 突袭 ──
    // 亡语：对所有敌方随从造成1点伤害。
    public sealed class Script_CORE_WC_701 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_WC_701", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 展馆茶杯 (CORE_WON_141) 3/3 ──
    // 战吼：随机使三个不同类型的友方随从获得+1/+1。
    public sealed class Script_CORE_WON_141 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_WON_141", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                int buffed = 0;
                foreach (var m in b.FriendMinions)
                {
                    if (ReferenceEquals(m, s) || buffed >= 3) continue;
                    H.Buff(m, 1, 1);
                    buffed++;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 甘尔葛战刃铸造师 3/3 流放→英雄+3攻
    public sealed class Script_CS3_017 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CS3_017", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 口琴独演者 4/2 BC:无随从→施放奥秘
    public sealed class Script_ETC_028 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_028",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── B-Box拳手 (ETC_072) 4/3 连击：造成4伤害随机分配到敌人身上。
    public sealed class Script_ETC_072 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_072", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.CardsPlayedThisTurn <= 0) return;
                for (int i = 0; i < 4; i++)
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

// 押韵狂人 1/3 突袭 连击→按连击牌数+1/+1
    public sealed class Script_ETC_073 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_073", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 静滞波形 5/6 每回合-1攻或-1血
    public sealed class Script_ETC_089 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "ETC_089"); } }

// 狼人场务 3/4 BC:给对手召0/3
    public sealed class Script_ETC_098 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_098",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { if(b.EnemyMinions.Count<7) b.EnemyMinions.Add(new SimEntity{Atk=0,Health=3,MaxHealth=3,IsTired=true,Type=Card.CType.MINION}); }); } }

// ── 牛铃独演者 (ETC_101) 4/2 ──
    // 战吼：如果你没有控制其他随从，造成2点伤害。
    public sealed class Script_ETC_101 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_101", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 打出自己后场上只有自己 = 没有其他随从
                bool soloPerformer = b.FriendMinions.Count(m => !ReferenceEquals(m, s)) == 0;
                if (soloPerformer && t != null)
                    CardEffectDB.Dmg(b, t, 2);
            }, BattlecryTargetType.EnemyOnly);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// 喧哗歌迷 1/5 BC:选随从+4攻(存活时)
    public sealed class Script_ETC_107 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_107",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { if(t!=null&&t.IsFriend) t.Atk+=4; }, BattlecryTargetType.FriendlyMinion); } }

// 摇滚教父沃恩 4/3 BC:复制手牌不同类型随从各1
    public sealed class Script_ETC_121 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_121",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 2); } }

// ── 硬核信徒 (ETC_209) 2/1 ──
    // 战吼：造成2点伤害。压轴：改为对所有敌人。
    public sealed class Script_ETC_209 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_209", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 检测是否是右手牌（压轴）= 是否是手牌中位置最右的牌
                // 简化：如果手牌为空（刚打出最后一张），视为压轴
                bool isFinale = b.Hand.Count == 0;
                if (isFinale)
                {
                    foreach (var e in b.EnemyMinions.ToArray())
                        CardEffectDB.Dmg(b, e, 2);
                    if (b.EnemyHero != null)
                        CardEffectDB.Dmg(b, b.EnemyHero, 2);
                }
                else if (t != null)
                {
                    CardEffectDB.Dmg(b, t, 2);
                }
            }, BattlecryTargetType.EnemyOnly);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 放克鱼人 (ETC_337) 2/2 圣盾 你的圣盾随从+2攻。
    public sealed class Script_ETC_337 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_337", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 心动歌手 2/5 过量治疗→召随从
    public sealed class Script_ETC_339 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_339", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 滑铲铁腿 (ETC_408) 2/3 突袭 ──
    // 战吼：在本局对战中，你每使用过一个不同类型的随从牌，便获得+1/+1。
    public sealed class Script_ETC_408 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_408", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：在无法追踪使用历史时，按场上不同种族随从数给buff
                // 保守估计给 +2/+2
                if (s != null) H.Buff(s, 2, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 服装裁缝 (ETC_420) 2/2 ──
    // 战吼：使一个友方随从获得等同于本随从的攻击力和生命值。
    public sealed class Script_ETC_420 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_420", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || s == null) return;
                H.Buff(t, s.Atk, s.Health);
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 计拍侏儒 2/4 用某费卡→抽下费卡
    public sealed class Script_ETC_422 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_422", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 死亡金属骑士 3/4 嘲讽 受过治疗→消耗生命值
    public sealed class Script_ETC_523 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "ETC_523", "CORE_ETC_523" })
            { if (!Enum.TryParse(n, true, out C id)) continue; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); }
        }
    }

// 全息技师 3/4 受到刚好1伤→消灭
    public sealed class Script_ETC_534 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_534", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 音乐节保安 2/5 嘲讽 压轴迫使敌人攻击
    public sealed class Script_ETC_542 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "ETC_542"); } }

// 混搭派送机 3/3 同上法师版
    public sealed class Script_JAM_000 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "JAM_000"); } }

// 混搭图腾师 3/2 同上萨满版
    public sealed class Script_JAM_012 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "JAM_012"); } }

// 挑剔的观众 2/1 流放→弹回手牌
    public sealed class Script_JAM_020 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("JAM_020", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 盗版海盗 (JAM_023) 4/3 战吼：获取对手牌库顶牌的复制。
    public sealed class Script_JAM_023 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_023", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 燃琴小鬼 (JAM_032) 3/3 战吼：获取火焰法术×2。
    public sealed class Script_JAM_032 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_032", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// 混搭乐师 3/3 突袭 手牌随机额外效果
    public sealed class Script_JAM_033 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "JAM_033"); } }

// 抱抱泰迪熊 2/4 扩大 扰魔吸血嘲讽
    public sealed class Script_MIS_300 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "MIS_300"); } }

// 暗月魔术师 2/4 扰魔 施法后随机施法
    public sealed class Script_MIS_303 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("MIS_303", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 爆破工程师 (MIS_308) 2/4 回合结束：洗入炸弹到对手牌库。
    public sealed class Script_MIS_308 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_308", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
                b.EnemyDeckCards.Add(0));
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Utility);
        }
    }

// ── 滚灰兔 (MIS_706) 3/2 ──
    // 战吼，亡语：将一件垃圾置入你的手牌（幸运币/石头/香蕉/短刀）。
    public sealed class Script_MIS_706 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_706", true, out C id)) return;
            Action<SimBoard, SimEntity, SimEntity> addJunk = (b, s, t) =>
            {
                b.FriendCardDraw += 1;
            };
            db.Register(id, EffectTrigger.Battlecry, addJunk);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            db.Register(id, EffectTrigger.Deathrattle, addJunk);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 堕寒男爵 (RLK_708) 2/2 ──
    // 战吼，亡语：抽一张牌。
    public sealed class Script_RLK_708 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_708", true, out C id)) return;
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

// ── 黑暗堕落者新兵 (RLK_731) 2/5 ──
    // 战吼：消耗2份残骸，使你手牌中的所有随从牌获得+2攻击力。
    public sealed class Script_RLK_731 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_731", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendExcavateCount >= 2)
                {
                    b.FriendExcavateCount -= 2;
                    H.BuffAllMinionsInHand(b, 2, 0);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 恋旧的新生 2/3 微缩 施放第一个法术+2/+2
    public sealed class Script_TOY_340 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_340", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 绵弹枪神赫米特 3/4 野兽死后获传说野兽
    public sealed class Script_TOY_355 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_355", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 水彩美术家 (TOY_376) 3/3 战吼：抽冰霜法术。
    public sealed class Script_TOY_376 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_376", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                H.DrawRandomCardToHandByPredicate(b, H.IsSpellCard, s));
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 粗心的匠人 (TOY_382) 3/3 亡语：获取两张0费恢复3hp的绷带。
    public sealed class Script_TOY_382 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_382", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 清仓销售员 (TOY_390) 3/2 亡语：使手牌中两张法术-1费。
    public sealed class Script_TOY_390 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_390", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                H.ReduceRandomSpellCostInHand(b, 1, s);
                H.ReduceRandomSpellCostInHand(b, 1, s);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Mana);
        }
    }

// ── 折价区海盗 (TOY_516) 3/2 突袭 连击：召唤复制。
    public sealed class Script_TOY_516 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_516", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.CardsPlayedThisTurn > 0 && s != null && b.FriendMinions.Count < 7)
                {
                    var copy = s.Clone();
                    copy.IsTired = false; copy.HasRush = true;
                    b.FriendMinions.Add(copy);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 泼漆彩鳍鱼人 (TOY_517) 2/3 剧毒 ──
    // 战吼：抽一张突袭随从牌。
    public sealed class Script_TOY_517 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_517", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 凶魔城堡 (TOY_526) 5/6 ──
    // 战吼：攻击你的英雄。
    public sealed class Script_TOY_526 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_526", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || b.FriendHero == null) return;
                CardEffectDB.Dmg(b, b.FriendHero, s.Atk);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// 伴唱机 2/4 英雄技能触发两次
    public sealed class Script_TOY_528 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_528", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 捣蛋林精 (TOY_646) 1/3 吸血嘲讽 亡语：对所有敌人1伤害。
    public sealed class Script_TOY_646 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_646", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 1);
                if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// 绒绒虎 3/2 微缩 突袭吸血圣盾
    public sealed class Script_TOY_811 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TOY_811"); } }

// ── 玩具兵盒 (TOY_814) 0/2 亡语：召五个1/1并具有随机额外效果的士兵。
    public sealed class Script_TOY_814 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_814", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 5 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// 毛绒暴暴狗 4/2 突袭 冰霜法术后获复生
    public sealed class Script_TOY_821 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_821", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 彩虹裁缝 (TOY_823) 3/3 ──
    // 战吼：如果你的套牌中有鲜血/冰霜/邪恶符文牌，对应获得吸血/复生/突袭。
    public sealed class Script_TOY_823 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_823", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                // 近似：假设死骑套牌通常都有各种符文，给全部
                s.IsLifeSteal = true;
                s.HasReborn = true;
                s.HasRush = true;
                s.IsTired = false;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 扮装选手 3/4 对手出随从→变形成3/4复制
    public sealed class Script_TOY_878 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_878", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 装饰美术家 (TOY_882) 2/3 战吼：抽圣盾随从+光环。
    public sealed class Script_TOY_882 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_882", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 软软多头蛇 (TOY_897) 2/4 亡语：洗入翻倍复制到牌库。
    public sealed class Script_TOY_897 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_897", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s != null) b.FriendDeckCards.Add(s.CardId);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 希希集 (TOY_913) 4/4 战吼+亡语：获取一张恶魔猎手牌。
    public sealed class Script_TOY_913 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_913", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => b.FriendCardDraw += 1);
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 速写美术家 (TOY_916) 3/3 战吼：抽暗影法术+获得复制。
    public sealed class Script_TOY_916 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_916", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                H.DrawRandomCardToHandByPredicate(b, H.IsSpellCard, s);
                b.FriendCardDraw += 1; // 复制近似为+1
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// 大作战狂热玩家 2/5 用最左/最右手牌→打1
    public sealed class Script_TOY_943 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_943", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 独眼突击者 3/3 突袭 锻造+3/+2
    public sealed class Script_TTN_042 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_042"); } }

// ── 神秘动物饲养员 (TTN_080) 3/3 突袭 战吼：如果有≥4攻随从，+2/+2。
    public sealed class Script_TTN_080 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_080", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                if (b.FriendMinions.Any(m => !ReferenceEquals(m, s) && m.Atk >= 4))
                    H.Buff(s, 2, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 悼词宣诵者 (TTN_457) 3/3 ──
    // 战吼：消耗3份残骸，造成3点伤害。
    public sealed class Script_TTN_457 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_457", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendExcavateCount >= 3 && t != null)
                {
                    b.FriendExcavateCount -= 3;
                    CardEffectDB.Dmg(b, t, 3);
                }
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 蒸汽守卫 (TTN_468) 3/3 ──
    // 战吼：抽一张法术牌。使你手牌中一张火焰法术牌的法力值消耗减少（1）点。
    public sealed class Script_TTN_468 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_468", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                H.DrawRandomCardToHandByPredicate(b, H.IsSpellCard, s);
                // 近似减费
                foreach (var c in b.Hand)
                {
                    if (H.IsSpellCard(c.CardId) && c.Cost > 0)
                    {
                        c.Cost--;
                        break;
                    }
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw | EffectKind.Mana);
        }
    }

// ── 星辰的学生 (TTN_482) 4/3 亡语：洗入+3/+3复制到牌库。
    public sealed class Script_TTN_482 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_482", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s != null) b.FriendDeckCards.Add(s.CardId);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 岩熔冶锻师 3/3 锻造后获复制
    public sealed class Script_TTN_729 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_729", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 粗心的机械师 4/5 一回合内抽第二张→消灭自己
    public sealed class Script_TTN_731 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_731"); } }

// 铁血座狼 2/4 突袭 击杀后满血
    public sealed class Script_TTN_733 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_733", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 机甲跳蛙 (TTN_734) 2/2 磁力 亡语：使友方机械获得+2/+2及此亡语。
    public sealed class Script_TTN_734 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_734", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var mechs = b.FriendMinions.Where(m => m.Health > 0 && !ReferenceEquals(m, s)).ToList();
                if (mechs.Count > 0) H.Buff(mechs[H.PickIndex(mechs.Count, b, s)], 2, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// ── 饥饿的海怪 (TTN_754) 2/5 战吼：消灭友方随从。亡语：召其复制。
    public sealed class Script_TTN_754 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_754", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || !t.IsFriend) return;
                t.Health = 0;
            }, BattlecryTargetType.FriendlyMinion);
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                // 近似：召唤一个3/3
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 3, Health = 3, MaxHealth = 3, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 流亡穴居人 (TTN_832) 4/4 突袭 ──
    // 战吼：对你的英雄造成4点伤害。
    public sealed class Script_TTN_832 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_832", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Health -= 4;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 风暴之王托里姆 (TTN_835) 3/4 战吼：解锁过载法力并抽等量牌。
    public sealed class Script_TTN_835 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_835", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：如果友方英雄有过载，解锁并抽等量牌
                int overloaded = b.FriendHero?.OverloadedCrystals ?? 0;
                if (overloaded > 0)
                {
                    b.Mana += overloaded;
                    for (int i = 0; i < overloaded; i++)
                        CardEffectDB.DrawCard(b, b.FriendDeckCards);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw | EffectKind.Mana);
        }
    }

// ════════════ 3费 光环/触发 ════════════
    // 阿古尼特魔像 1/5 本回合每抽牌+1攻
    public sealed class Script_TTN_844 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_844", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 命运切分者 (TTN_859) 3/3 亡语：获取对手使用的上一张牌的复制。
    public sealed class Script_TTN_859 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_859", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 阿古斯的信徒 (TTN_861) 2/2 ──
    // 亡语：召唤两个2/2并具有嘲讽的元素。
    public sealed class Script_TTN_861 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_861", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 2, Health = 2, MaxHealth = 2, IsFriend = true,
                        IsTaunt = true, IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// 天才米米尔隆 2/5 使用机械→获小工具
    public sealed class Script_TTN_920 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_920", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 铜尾刺探鼠 4/3 磁力 攻击获幸运币
    public sealed class Script_TTN_921 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_921", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ════════════ 3费纯白板/关键词 ════════════
    // SP-3Y3-D3R蛛型机 3/4 磁力 潜行
    public sealed class Script_TTN_923 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_923"); } }

// 锋鳞 2/4 卡牌费用不能低于2
    public sealed class Script_TTN_924 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_924", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ═══════ 德鲁伊 DRUID ═══════

    // ── 护园森灵 (TTN_927) 3/4 战吼：将友方树人变5/5嘲讽古树。
    public sealed class Script_TTN_927 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_927", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null) return;
                t.Atk = 5; t.Health = 5; t.MaxHealth = 5; t.IsTaunt = true;
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 冰冷整脊师 (VAC_327) 3/3 ──
    // 战吼：使一个随从获得+3/+3并使其冻结。
    public sealed class Script_VAC_327 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_327", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null) return;
                H.Buff(t, 3, 3);
                t.IsFrozen = true;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 消融元素 3/8 嘲讽 永久冻结
    public sealed class Script_VAC_328 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "VAC_328"); } }

// 灶头厨师 2/4 嘲讽 抽到获复制
    public sealed class Script_VAC_337 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "VAC_337"); } }

// ── 霜噬海盗 (VAC_402) 2/2 亡语：冻结3个敌人，已冻结受5伤。
    public sealed class Script_VAC_402 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_402", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var targets = new List<SimEntity>();
                targets.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                for (int i = 0; i < 3 && i < targets.Count; i++)
                {
                    var pick = targets[i];
                    if (pick.IsFrozen) CardEffectDB.Dmg(b, pick, 5);
                    else pick.IsFrozen = true;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// 话痨鹦鹉 3/3 BC:重施放法术
    public sealed class Script_VAC_407 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_407",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 落难的大法师 3/4 每回合第一张法术-2费
    public sealed class Script_VAC_435 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_435", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 海关执法者 2/5 敌方套牌外卡+2费
    public sealed class Script_VAC_440 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_440", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 规划狂人 3/3 BC:选3张牌置牌库顶
    public sealed class Script_VAC_444 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_444",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 歌唱明星卡瑞斯 3/3 手牌变形
    public sealed class Script_VAC_449 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_449", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 悠闲的曲奇 2/2 友方随从死→召对应随从+1费
    public sealed class Script_VAC_450 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_450", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 前台礼宾 3/4 其他职业的牌-1费
    public sealed class Script_VAC_463 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_463", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 始祖龟旅行者 (VAC_518) 1/5 嘲讽 亡语：抽嘲讽随从-2费。
    public sealed class Script_VAC_518 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_518", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                int before = b.Hand.Count;
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                if (b.Hand.Count > before)
                    b.Hand[b.Hand.Count - 1].Cost = Math.Max(0,
                        b.Hand[b.Hand.Count - 1].Cost - 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw | EffectKind.Mana);
        }
    }

// ── 笨拙的搬运工 (VAC_521) 3/3 嘲讽 ──
    // 战吼：如果你的手牌中有法力值消耗≥5的法术牌，召唤一个本随从的复制。
    public sealed class Script_VAC_521 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_521", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                bool hasBigSpell = b.Hand.Any(c => H.IsSpellCard(c.CardId) && c.Cost >= 5);
                if (hasBigSpell && s != null && b.FriendMinions.Count < 7)
                {
                    var copy = s.Clone();
                    copy.IsTired = true;
                    b.FriendMinions.Add(copy);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 刀剑保养师 (VAC_701) 3/3 战吼：武器变3/3。
    public sealed class Script_VAC_701 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_701", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendWeapon != null)
                {
                    b.FriendWeapon.Atk = 3;
                    b.FriendWeapon.Health = 3;
                    b.FriendWeapon.MaxHealth = 3;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 帆船舰长 (VAC_937) 2/4 ──
    // 战吼：使一个友方海盗获得风怒。
    public sealed class Script_VAC_937 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "VAC_937",
                new TriggerDef("Battlecry", "FriendlyMinion",
                    new EffectDef("give_windfury")));
    }

// 粗暴的猢狲 2/4 海盗攻击时+1/+1
    public sealed class Script_VAC_938 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_938", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 祭献小鬼 (VAC_943) 1/6 亡语(你的回合)：召6/6嘲讽小鬼。
    public sealed class Script_VAC_943 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_943", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 6, Health = 6, MaxHealth = 6, IsFriend = true,
                    IsTaunt = true, IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 可怕的主厨 (VAC_946) 2/1 战吼：召0/2蛋。亡语：消灭它。
    public sealed class Script_VAC_946 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_946", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 0, Health = 2, MaxHealth = 2, IsFriend = true,
                    IsTired = true, HasDeathrattle = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 拨号机器人 3/2 BC:抽3张不同费
    public sealed class Script_WORK_006 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("WORK_006",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { for(int i=0;i<3;i++) CardEffectDB.DrawCard(b, b.FriendDeckCards); }); } }

// ── 湍流元素特布勒斯 (WORK_013) 3/3 ──
    // 战吼：使你手牌，牌库和战场上的所有其他战吼随从获得+1/+1。
    public sealed class Script_WORK_013 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_013", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 手牌中有战吼的随从
                foreach (var c in b.Hand)
                    if (c.Type == Card.CType.MINION && H.HasBattlecry(c.CardId))
                        H.Buff(c, 1, 1);
                // 场上有战吼的随从
                foreach (var m in b.FriendMinions)
                    if (!ReferenceEquals(m, s) && H.HasBattlecry(m.CardId))
                        H.Buff(m, 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ═══════ 猎人 HUNTER ═══════

    // ── 劳作老马 (WORK_018) 4/2 亡语：召两匹2/1小马。
    public sealed class Script_WORK_018 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_018", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 2, Health = 1, MaxHealth = 1, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 影随员工 (WORK_032) 4/3 战吼：英雄本回合受过伤→召复制。
    public sealed class Script_WORK_032 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_032", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                if (b.FriendHero != null && b.FriendHero.Health < b.FriendHero.MaxHealth)
                {
                    var copy = s.Clone();
                    copy.IsTired = true;
                    b.FriendMinions.Add(copy);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 笨拙的杂役 2/4 抽牌→变临时牌
    public sealed class Script_WORK_040 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("WORK_040", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 空中施肥者 (YOG_403) 2/2 ──
    // 战吼，亡语：召唤一个2/2的树人。
    public sealed class Script_YOG_403 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_403", true, out C id)) return;
            Action<SimBoard, SimEntity, SimEntity> summonTreent = (b, s, t) =>
            {
                if (b.FriendMinions.Count < 7)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 2, Health = 2, MaxHealth = 2,
                        IsFriend = true, IsTired = true, Type = Card.CType.MINION
                    });
            };
            db.Register(id, EffectTrigger.Battlecry, summonTreent);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
            db.Register(id, EffectTrigger.Deathrattle, summonTreent);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// 秽病行尸 2/4 召唤亡灵→获剧毒
    public sealed class Script_YOG_512 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("YOG_512", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 触须宠溺者 2/5 回合结束获取1/1触须
    public sealed class Script_YOG_517 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("YOG_517", true, out C id)) return; db.Register(id, EffectTrigger.EndOfTurn, (b,s,t)=> b.FriendCardDraw += 1); db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Draw); } }

// ── 多事的仆从 (YOG_518) 3/4 战吼：用过5+法术→抽2。
    public sealed class Script_YOG_518 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_518", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// 燃魂者瓦利亚 1/5 友方亡灵死→敌英雄2伤+获暗影法术
    public sealed class Script_YOG_520 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("YOG_520", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 健身肌器人 (YOG_525) 2/4 ──
    // 战吼：使你手牌中的所有随从牌获得+1/+1。锻造：改为+2/+2。
    public sealed class Script_YOG_525 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_525", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                H.BuffAllMinionsInHand(b, 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 受污染的鞭笞者 (YOG_528) 3/3 战吼：用过5+法术→复原4水晶。
    public sealed class Script_YOG_528 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_528", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                b.Mana = Math.Min(b.MaxMana, b.Mana + 4));
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

}
