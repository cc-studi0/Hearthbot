// ═══════════════════════════════════════════════════════════════
//  标准随从效果 — 0-1 费随从
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

// ── 石丘防御者 (Core_UNG_072) 1/5 嘲讽 ──
    // 战吼：发现一张具有嘲讽的随从牌。
    public sealed class Script_Core_UNG_072 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "Core_UNG_072",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// 奇利亚斯豪华版 — 自定义构建 不做模拟
    public sealed class Script_TOY_330 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TOY_330"); } }

// 炫晶小熊 1/2 消耗最后法力水晶+1/+1
    public sealed class Script_CATA_130 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_130", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 古神的眼线 2/1 BC:手牌变幸运币
    public sealed class Script_CATA_200 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_200",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 暮光龙卵 (CATA_210) 0/2 亡语：召2/2雏龙。
    public sealed class Script_CATA_210 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_210", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 2, Health = 2, MaxHealth = 2, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 冬泉雏龙 (CATA_484) 1/2 ──
    // 战吼：发现一张任意职业的法力值消耗为（1）的法术牌。
    public sealed class Script_CATA_484 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CATA_484",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 载蛋雏龙 (CATA_556) 1/2 ──
    // 战吼：随机获取一张法力值消耗小于或等于（3）点的龙牌。
    public sealed class Script_CATA_556 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CATA_556",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// 进击的募援官 2/2 扰魔
    public sealed class Script_CATA_558 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CATA_558"); } }

// 战斗邪犬 1/2 英雄攻击后+1攻
    public sealed class Script_CORE_BT_351 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_351", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 火色魔印奔行者 1/1 流放抽牌
    public sealed class Script_CORE_BT_480 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_480", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 叫嚣的中士 (CORE_CS2_188) 1/1 ──
    // 战吼：在本回合中，使一个随从获得+2攻击力。
    public sealed class Script_CORE_CS2_188 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_CS2_188",
                new TriggerDef("Battlecry", "AnyMinion",
                    new EffectDef("buff", atk: 2, hp: 0)));
    }

// ── 精灵弓箭手 (CORE_CS2_189) 1/1 ──
    // 战吼：造成1点伤害。
    public sealed class Script_CORE_CS2_189 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_CS2_189",
                new TriggerDef("Battlecry", "AnyCharacter",
                    new EffectDef("dmg", v: 1)));
    }

// ── 紫罗兰魔翼鸦 (CORE_DRG_107) 2/1 亡语：将奥术飞弹置入手牌。
    public sealed class Script_CORE_DRG_107 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DRG_107", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// 狼人渗透者 2/1 潜行
    public sealed class Script_CORE_EX1_010 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_EX1_010"); } }

// ── 巫医 (CORE_EX1_011) 2/1 ──
    // 战吼：恢复2点生命值。
    public sealed class Script_CORE_EX1_011 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_EX1_011",
                new TriggerDef("Battlecry", "AnyCharacter",
                    new EffectDef("heal", v: 2)));
    }

// ── 心灵咒术师 (CORE_EX1_193) 1/2 ──
    // 战吼：复制你对手的牌库中的一张牌，并将其置入你的手牌。
    public sealed class Script_CORE_EX1_193 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_EX1_193",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 烈焰小鬼 (CORE_EX1_319) 3/2 ──
    // 战吼：对你的英雄造成3点伤害。
    public sealed class Script_CORE_EX1_319 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_319", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendHero != null)
                    b.FriendHero.Health -= 3;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 鱼人招潮者 (CORE_EX1_509) 1/2 每当你召唤鱼人+1攻。
    public sealed class Script_CORE_EX1_509 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_509", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ════════════ 1费纯白板/关键词 ════════════
    // 这些随从只有关键词（嘲讽/圣盾/突袭/潜行/复生/扰魔/过载），无需脚本
    // 但注册空光环让评估器知道它们存在

    // 正义保护者 1/1 嘲讽+圣盾
    public sealed class Script_CORE_ICC_038 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_ICC_038"); } }

// ── 吹嘘海盗 (CORE_KAR_069) 1/2 ──
    // 战吼：随机将一张另一职业的卡牌置入你的手牌。
    public sealed class Script_CORE_KAR_069 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_KAR_069",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 奥术工匠 (CORE_LOOT_231) 1/3 ──
    // 每当你施放一个法术，便获得等同于其法力值消耗的护甲值。
    // 光环效果，不在这里模拟（需要事件系统）
    // 但标记为有用随从
    public sealed class Script_CORE_LOOT_231 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 标记为有光环的随从，让评估器认为它有持续价值
            if (!Enum.TryParse("CORE_LOOT_231", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 活泼的松鼠 (CORE_SW_439) 2/1 亡语：将四张橡果洗入牌库。
    public sealed class Script_CORE_SW_439 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_SW_439", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 4; i++) b.FriendDeckCards.Add(0);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 凶恶的滑矛纳迦 1/3 施放法术后+1攻
    public sealed class Script_CORE_TSC_827 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_TSC_827", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 欢快的同伴 (CORE_ULD_191) 1/2 ──
    // 战吼：使一个友方随从获得+2生命值。
    public sealed class Script_CORE_ULD_191 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_ULD_191",
                new TriggerDef("Battlecry", "FriendlyMinion",
                    new EffectDef("buff", atk: 0, hp: 2)));
    }

// 鱼人木乃伊 1/1 复生
    public sealed class Script_CORE_ULD_723 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_ULD_723"); } }

// ── 冰川裂片 (CORE_UNG_205) 2/1 ──
    // 战吼：冻结一个敌人。
    public sealed class Script_CORE_UNG_205 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_UNG_205",
                new TriggerDef("Battlecry", "EnemyOnly",
                    new EffectDef("freeze")));
    }

// ── 火羽精灵 (CORE_UNG_809) 1/2 ──
    // 战吼：将一张1/2的元素牌置入你的手牌。
    public sealed class Script_CORE_UNG_809 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_UNG_809", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.Hand.Count < 10)
                    b.Hand.Add(new SimEntity
                    {
                        CardId = 0, Atk = 1, Health = 2, MaxHealth = 2,
                        Cost = 1, IsFriend = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 宝石鹦鹉 (CORE_UNG_912) 1/2 ──
    // 战吼：随机将一张野兽牌置入你的手牌。
    public sealed class Script_CORE_UNG_912 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_UNG_912",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 哀嚎蒸汽 (CORE_WC_042) 1/3 ──
    // 在你使用一张元素牌后，获得+1攻击力。(非战吼，标记光环)
    public sealed class Script_CORE_WC_042 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_WC_042", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ═══════ 牧师 PRIEST ═══════

    // ── 随船外科医师 (CORE_WON_065) 1/2 在你召唤随从后+1生命值。(光环)
    public sealed class Script_CORE_WON_065 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_WON_065", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ═══════ 术士 WARLOCK ═══════

    // ── 邪魔仆从 (CORE_YOD_026) 2/1 亡语：随机使友方随从+本随从攻。
    public sealed class Script_CORE_YOD_026 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_YOD_026", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s == null) return;
                var targets = b.FriendMinions.Where(m => m.Health > 0 && !ReferenceEquals(m, s)).ToList();
                if (targets.Count > 0)
                    targets[H.PickIndex(targets.Count, b, s)].Atk += s.Atk;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// ════════════ 简单光环/触发 ════════════

    // 电击学徒 (CS3_007) 3/2 法伤+1 过载1
    public sealed class Script_CS3_007 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CS3_007"); } }

// ── 虚空协奏者 (ETC_081) 1/3 你的回合中英雄免疫。(光环)
    public sealed class Script_ETC_081 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_081", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 空气吉他手 (ETC_102) 1/1 ──
    // 战吼：使你的武器获得+1耐久度。
    public sealed class Script_ETC_102 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_102", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendWeapon != null)
                {
                    b.FriendWeapon.Health += 1;
                    b.FriendWeapon.MaxHealth += 1;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 人潮冲浪者 (ETC_104) 1/1 亡语：使任意其他随从获得+1/+1和此亡语。
    public sealed class Script_ETC_104 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_104", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var ts = b.FriendMinions.Where(m => m.Health > 0 && !ReferenceEquals(m, s)).ToList();
                if (ts.Count > 0) H.Buff(ts[H.PickIndex(ts.Count, b, s)], 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// ── 频率振荡机 (ETC_106) 2/1 ──
    // 战吼：你的下一张机械牌的法力值消耗减少（1）点。
    public sealed class Script_ETC_106 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_106", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：减少手牌中第一张机械的费用
                foreach (var c in b.Hand)
                {
                    if (H.IsMechMinion(c.CardId))
                    {
                        c.Cost = Math.Max(0, c.Cost - 1);
                        break;
                    }
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ═══ 1费战吼 ═══
    // 吵吵歌迷 1/2 BC:选随从使其无法攻击
    public sealed class Script_ETC_109 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_109",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 剃刀沼泽摇滚明星 1/3 获护甲→再+2
    public sealed class Script_ETC_355 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_355", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 萨克斯独演者 1/2 BC:无随从→手牌加自身
    public sealed class Script_ETC_358_BC : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_358",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); } }

// 驭流骑士 2/1 BC:过载→发现法术
    public sealed class Script_ETC_359 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_359",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); } }

// 沉静的吹笛人 1/1 抉择
    public sealed class Script_ETC_375 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_375",true,out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 自由之魂 (ETC_382) 1/2 战吼+亡语：英雄技能多+1护甲。
    public sealed class Script_ETC_382 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_382", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 刺鬃乐师 1/3 压轴下只野兽+1/+1
    public sealed class Script_ETC_831 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_831", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 盛装歌手 1/1 回合结束抽奥秘
    public sealed class Script_JAM_001 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("JAM_001",true,out C id)) return; db.Register(id, EffectTrigger.EndOfTurn, (b,s,t)=> CardEffectDB.DrawCard(b, b.FriendDeckCards)); db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Draw); } }

// ── 水宝宝鱼人 (MIS_307) 1/1 扩大 ──
    // 战吼：召唤一个属性值等同于本随从并具有突袭的鱼人宝宝。
    public sealed class Script_MIS_307 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_307", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = s.Atk, Health = s.Health, MaxHealth = s.Health,
                    IsFriend = true, HasRush = true, IsTired = false,
                    Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 滑矛布袋手偶 1/2 攻击力随英雄攻击力提高
    public sealed class Script_MIS_710 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_710", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 伊米亚破霜者 (RLK_110) 1/2 ──
    // 战吼：你手牌中每有一张冰霜法术牌，便获得+1攻击力。
    public sealed class Script_RLK_110 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_110", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                // 近似：计手牌中法术数量（无法精确判断冰霜学派）
                int frostCount = b.Hand.Count(c => H.IsSpellCard(c.CardId));
                s.Atk += frostCount;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ════════════ 高价值战吼补充 ════════════

    // ── 扛包收尸人 (RLK_503) 1/3 战吼：获得一份残骸。
    public sealed class Script_RLK_503 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_503", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => b.FriendExcavateCount++);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ════════════════ 死亡骑士 DEATHKNIGHT ════════════════

    // ── 骷髅帮手 (RLK_958) 1/2 ──
    // 战吼：使一个友方亡灵获得+2攻击力。
    public sealed class Script_RLK_958 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "RLK_958",
                new TriggerDef("Battlecry", "FriendlyMinion",
                    new EffectDef("buff", atk: 2, hp: 0)));
    }

// 焦油泥浆怪 0/3 嘲讽 对手回合+2攻
    public sealed class Script_TOY_000 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_000", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 甲虫钥匙链 (TOY_006) 1/1 ──
    // 战吼：发现一张法力值消耗为（2）的卡牌。
    public sealed class Script_TOY_006 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "TOY_006",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

// ── 礼盒雏龙 (TOY_386) 2/1 ──
    // 战吼：如果你的手牌中有龙牌，使该龙牌和本随从获得+1/+1。
    public sealed class Script_TOY_386 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_386", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (H.HasDragonInHand(b, s))
                {
                    // buff self
                    if (s != null) H.Buff(s, 1, 1);
                    // buff a dragon in hand
                    var dragon = H.PickHandDragon(b, s);
                    if (dragon != null) H.Buff(dragon, 1, 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 宝藏经销商 (TOY_518) 1/2 ──
    // 在你召唤一个海盗后，使其获得+1攻击力。(光环)
    public sealed class Script_TOY_518 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_518", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 青玉展品 (TOY_803) 1/1 亡语：+1/+1并洗2张回牌库。
    public sealed class Script_TOY_803 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_803", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                b.FriendDeckCards.Add(0);
                b.FriendDeckCards.Add(0);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 通道沉眠者 3/5 休眠 7随从死亡唤醒
    public sealed class Script_TOY_866 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TOY_866"); } }

// 星界自动机 1/2 每召一个星界自动机+1/+1
    public sealed class Script_TTN_401 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_401", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 得胜的维库人 1/2 攻击后获取1费2/3
    public sealed class Script_TTN_812 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_812", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 无人机拆解器 1/2 BC:获取火花机器人
    public sealed class Script_TTN_860 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_860",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); } }

// ── 修理机器人X-21 (TTN_906) 1/3 亡语：将磁力机械移回手牌。
    public sealed class Script_TTN_906 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_906", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 当日渔获 (VAC_412) 3/3 突袭 ──
    // 战吼：为你的对手召唤一只2/1的鱼虫。
    public sealed class Script_VAC_412 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_412", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.EnemyMinions.Count < 7)
                    b.EnemyMinions.Add(new SimEntity
                    {
                        Atk = 2, Health = 1, MaxHealth = 1,
                        IsFriend = false, IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 心灵按摩师 2/3 受伤→对己英雄等伤
    public sealed class Script_VAC_512 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_512", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 飞行员帕奇斯 (VAC_933) 1/1 战吼：洗6张降落伞入牌库。
    public sealed class Script_VAC_933 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_933", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 6; i++) b.FriendDeckCards.Add(0);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 派对邪犬 (VAC_940) 1/1 ──
    // 战吼：召唤两只1/1的邪能兽。对你的英雄造成3点伤害。
    public sealed class Script_VAC_940 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_940", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 1, Health = 1, MaxHealth = 1, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
                if (b.FriendHero != null)
                    b.FriendHero.Health -= 3;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon | EffectKind.Damage);
        }
    }

// ════════════ 剩余亡语 ════════════

    // ── 进化融合怪 (VAC_958) 1/2 亡语：洗回牌库。
    public sealed class Script_VAC_958 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_958", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s != null) b.FriendDeckCards.Add(s.CardId);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 影触克瓦迪尔 1/3 BC:治疗转伤害
    public sealed class Script_YOG_300 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("YOG_300",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 混乱触须 1/1 BC:随机施放1费法术
    public sealed class Script_YOG_514 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("YOG_514",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 雷电跳蛙 (YOG_524) 1/2 ──
    // 战吼：随机获取一张过载牌。
    public sealed class Script_YOG_524 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "YOG_524",
                new TriggerDef("Battlecry", "None",
                    new EffectDef("draw", n: 1)));
    }

}
