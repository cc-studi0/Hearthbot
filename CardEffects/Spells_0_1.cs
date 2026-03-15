// ═══════════════════════════════════════════════════════════════
//  标准法术效果 — 0-1 费法术
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
    // ═══ 0费法术 ═══

    // 暗影步 — 弹回随从-2费
    public sealed class Spell_CORE_EX1_144 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_144", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t == null || !t.IsFriend) return;
                b.FriendMinions.Remove(t);
                b.Hand.Add(t);
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Utility);
        }
    }

    // 背刺 — 对未受伤随从2伤
    public sealed class Spell_CORE_CS2_072 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_072", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null && t.Health == t.MaxHealth) CardEffectDB.Dmg(b, t, 2);
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 伺机待发 — 下法术-2费（只做标记）
    public sealed class Spell_CORE_EX1_145 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CORE_EX1_145",true,out C id)) return; db.Register(id, EffectTrigger.Spell, (b,s,t)=>{}); } }

    // 寒冬号角 RLK_042/CORE_RLK_042 — 复原2水晶
    public sealed class Spell_RLK_042 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "RLK_042", "CORE_RLK_042" })
            {
                if (!Enum.TryParse(n, true, out C id)) continue;
                db.Register(id, EffectTrigger.Spell, (b, s, t) => b.Mana = Math.Min(b.MaxMana, b.Mana + 2));
                db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Mana);
            }
        }
    }

    // 激活 — 获得一个临时法力水晶
    public sealed class Spell_CORE_EX1_169 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_169", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) => b.Mana += 1);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Mana);
        }
    }

    // 禁忌之果 — 消耗所有法力
    public sealed class Spell_YOG_529 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_529", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Armor += b.Mana * 2;
                b.Mana = 0;
            });
        }
    }

    // 突破邪火 — 随从获得突袭
    public sealed class Spell_JAM_017 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_017", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) { t.HasRush = true; t.IsTired = false; }
            }, BattlecryTargetType.FriendlyMinion);
        }
    }

    // 音乐狂欢 — 使用随从获萨满法术
    public sealed class Spell_ETC_367 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_367",true,out C id)) return; db.Register(id, EffectTrigger.Spell, (b,s,t)=>{}); } }

    // ═══ 1费法术 ═══

    // 奥术射击 — 造成2伤
    public sealed class Spell_CORE_DS1_185 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DS1_185", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 2);
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 冰冷触摸 RLK_038 — 2伤+冻结
    public sealed class Spell_RLK_038 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_038", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) { CardEffectDB.Dmg(b, t, 2); t.IsFrozen = true; }
            }, BattlecryTargetType.EnemyOnly);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 颤地觉醒 — 获取3张4/1冰虫
    public sealed class Spell_TTN_081 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_081",true,out C id)) return; db.Register(id, EffectTrigger.Spell, (b,s,t)=> b.FriendCardDraw += 3); db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw); } }

    // 盾牌猛击 — 等护甲伤害
    public sealed class Spell_CORE_EX1_410 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_410", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null && b.FriendHero != null) CardEffectDB.Dmg(b, t, b.FriendHero.Armor);
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 活体根须 — 抉择：2伤/召两个1/1
    public sealed class Spell_CORE_AT_037 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_AT_037", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                // 近似：召唤两个1/1
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity { Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true, IsTired = true, Type = Card.CType.MINION });
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Summon);
        }
    }

    // 击伤猎物 — 1伤+召1/1突袭
    public sealed class Spell_CORE_BAR_801 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BAR_801", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 1);
                if (b.FriendMinions.Count < 7)
                    b.FriendMinions.Add(new SimEntity { Atk = 1, Health = 1, MaxHealth = 1, HasRush = true, IsFriend = true, Type = Card.CType.MINION });
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage | EffectKind.Summon);
        }
    }

    // 激寒急流 — 2伤+对随机敌随从1伤
    public sealed class Spell_CATA_485 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_485", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 2);
                var enemies = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                if (enemies.Count > 0) CardEffectDB.Dmg(b, enemies[H.PickIndex(enemies.Count, b, s)], 1);
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 快速治疗 — 恢复5
    public sealed class Spell_CORE_AT_055 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_AT_055", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) { int heal = Math.Min(5, t.MaxHealth - t.Health); t.Health += heal; }
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Heal);
        }
    }

    // 烈焰喷涌 — 2伤+获取1/2
    public sealed class Spell_CORE_UNG_018 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_UNG_018", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 2);
                b.FriendCardDraw += 1;
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage | EffectKind.Draw);
        }
    }

    // 灵魂炸弹 — 对随从和己英雄各4伤
    public sealed class Spell_CORE_BOT_222 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BOT_222", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 4);
                if (b.FriendHero != null) CardEffectDB.Dmg(b, b.FriendHero, 4);
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 猛击 — 2伤，如目标存活抽牌
    public sealed class Spell_CORE_EX1_391 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_391", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) { CardEffectDB.Dmg(b, t, 2); if (t.Health > 0) CardEffectDB.DrawCard(b, b.FriendDeckCards); }
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage | EffectKind.Draw);
        }
    }

    // 莫瑞甘的灵界 — 抽3张临时牌
    public sealed class Spell_CORE_BOT_568 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BOT_568", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            { for (int i = 0; i < 3; i++) CardEffectDB.DrawCard(b, b.FriendDeckCards); });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }

    // 闪电箭 — 3伤 过载1
    public sealed class Spell_CORE_EX1_238 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_238", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 3);
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 神圣惩击 — 对随从3伤
    public sealed class Spell_CORE_CS1_130 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS1_130", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 3);
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 死亡缠绕 — 1伤，死亡抽牌
    public sealed class Spell_CORE_EX1_302 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_302", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) { CardEffectDB.Dmg(b, t, 1); if (t.Health <= 0) CardEffectDB.DrawCard(b, b.FriendDeckCards); }
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage | EffectKind.Draw);
        }
    }

    // 流电爆裂 — 召两个1/1突袭 过载1
    public sealed class Spell_CORE_BOT_451 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BOT_451", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity { Atk = 1, Health = 1, MaxHealth = 1, HasRush = true, IsFriend = true, Type = Card.CType.MINION });
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Summon);
        }
    }

    // 真言术：盾 — +2血 抽牌
    public sealed class Spell_CORE_CS2_004 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_004", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) { t.Health += 2; t.MaxHealth += 2; }
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff | EffectKind.Draw);
        }
    }

    // 致命药膏 — 武器+2攻
    public sealed class Spell_CORE_CS2_074 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_074", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (b.FriendWeapon != null) { b.FriendWeapon.Atk += 2; if (b.FriendHero != null) b.FriendHero.Atk += 2; }
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }

    // 斩杀 — 消灭受伤敌随从
    public sealed class Spell_CORE_CS2_108 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_108", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null && !t.IsFriend && t.Health < t.MaxHealth) t.Health = 0;
            }, BattlecryTargetType.EnemyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Destroy);
        }
    }

    // 林木幼苗 — 召两个1/1
    public sealed class Spell_TTN_950 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_950", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity { Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true, IsTired = true, Type = Card.CType.MINION });
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Summon);
        }
    }

    // 混乱品味 ETC_394 — 2伤 压轴发现
    public sealed class Spell_ETC_394 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_394", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 2);
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 女巫森林苹果 — 两张2/2进手牌
    public sealed class Spell_CORE_GIL_663 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CORE_GIL_663",true,out C id)) return; db.Register(id, EffectTrigger.Spell, (b,s,t)=> b.FriendCardDraw += 2); db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw); } }

    // 针灸 — 双方英雄4伤
    public sealed class Spell_VAC_419 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_419", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (b.FriendHero != null) CardEffectDB.Dmg(b, b.FriendHero, 4);
                if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, 4);
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 愈合 — 恢复满血 抽牌
    public sealed class Spell_CATA_302 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_302", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) t.Health = t.MaxHealth;
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Heal | EffectKind.Draw);
        }
    }

    // 致聋术 — 沉默随从
    public sealed class Spell_JAM_022 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_022", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) { t.HasDeathrattle = false; t.IsDivineShield = false; t.IsStealth = false; t.IsFrozen = false; t.IsTaunt = false; }
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Utility);
        }
    }

    // 1费发现/获取类 — 全部近似 draw+1
    public sealed class Spell_1Cost_Discover : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] {
                "CORE_GIL_836","ETC_074","CORE_SCH_158","CATA_621","YOG_511",
                "TTN_476","TTN_317","CORE_DS1_184","CORE_YOP_001","ETC_535",
                "Core_LOE_115","VAC_335","TOY_510","MIS_708","MIS_104","MIS_700"
            })
            {
                if (!Enum.TryParse(n, true, out C id)) continue;
                db.Register(id, EffectTrigger.Spell, (b, s, t) => b.FriendCardDraw += 1);
                db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
            }
        }
    }

    // 抛接嬉戏 — 抽随从牌（可能再抽法术）
    public sealed class Spell_TOY_352 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_352", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) => CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }

    // 批量生产 — 抽2+自伤3
    public sealed class Spell_MIS_707 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_707", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                if (b.FriendHero != null) CardEffectDB.Dmg(b, b.FriendHero, 3);
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }

    // 标准的卡牌包 — 获取5张临时嘲讽
    public sealed class Spell_MIS_705 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("MIS_705",true,out C id)) return; db.Register(id, EffectTrigger.Spell, (b,s,t)=> b.FriendCardDraw += 5); db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw); } }

    // 1费 buff/utility 小法术（效果简单或无法精确模拟）
    public sealed class Spell_1Cost_Misc : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] {
                "TOY_881","CATA_554","TOY_644","TTN_922","ETC_424",
                "VAC_916","VAC_922","VAC_338","TTN_486","TOY_825",
                "CATA_585","VAC_428","CATA_528","YOG_503","ETC_363",
                "VAC_520","VAC_404","ETC_201","CATA_136","ETC_076",
                "TTN_726","VAC_941"
            })
            { if (!Enum.TryParse(n, true, out C id)) continue; db.Register(id, EffectTrigger.Spell, (b,s,t)=>{}); }
        }
    }

    // 光鲜包装 — 有圣盾随从+2/+3
    public sealed class Spell_TOY_881_Detail : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_881", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null && t.IsDivineShield) { t.Atk += 2; t.Health += 3; t.MaxHealth += 3; }
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }

    // 焦油飞溅 — 本回合伤害翻倍+1伤
    public sealed class Spell_TTN_726_Detail : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_726", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 1);
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 换挡漂移 — 洗2张+抽3张
    public sealed class Spell_TTN_922_Detail : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_922", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.Hand.Count > 0; i++)
                {
                    var c = b.Hand[0]; b.Hand.RemoveAt(0); b.FriendDeckCards.Add(c.CardId);
                }
                for (int i = 0; i < 3; i++) CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Draw);
        }
    }

    // 立体书 — 2伤 + 召两个0/1嘲讽
    public sealed class Spell_TOY_508 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_508", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 2);
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity { Atk = 0, Health = 1, MaxHealth = 1, IsTaunt = true, IsFriend = true, IsTired = true, Type = Card.CType.MINION });
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage | EffectKind.Summon);
        }
    }

    // 混乱吞噬 — 消灭友方随从→消灭敌方随从
    public sealed class Spell_TTN_932 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_932", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                // 需要两个目标，近似只处理敌方目标
                if (t != null && !t.IsFriend) t.Health = 0;
            }, BattlecryTargetType.EnemyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Destroy);
        }
    }

    // 主歌乐句 — 英雄+2攻 +2护甲
    public sealed class Spell_ETC_363_Detail : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_363", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (b.FriendHero != null) { b.FriendHero.Atk += 2; b.FriendHero.Armor += 2; }
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Buff);
        }
    }

    // 银樽海韵 — 2伤随机分配到敌随从
    public sealed class Spell_VAC_520_Detail : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_520", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                for (int i = 0; i < 2; i++)
                {
                    var targets = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                    if (targets.Count == 0) break;
                    CardEffectDB.Dmg(b, targets[H.PickIndex(targets.Count, b, s)], 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }

    // 夜影花茶 — 对随从2伤+对己英雄2伤
    public sealed class Spell_VAC_404_Detail : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_404", true, out C id)) return;
            db.Register(id, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 2);
                if (b.FriendHero != null) CardEffectDB.Dmg(b, b.FriendHero, 2);
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Spell, EffectKind.Damage);
        }
    }
}
