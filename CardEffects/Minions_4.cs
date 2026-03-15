// ═══════════════════════════════════════════════════════════════
//  标准随从效果 — 4 费随从
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

// ═══ 4费战吼 ═══
    public sealed class Script_BC4_Discover : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "TTN_076","VAC_432","TTN_714","VAC_464","VAC_959",
                "ETC_080","TTN_717","TTN_751","VAC_437","CATA_470","VAC_420" })
            {
                if (!Enum.TryParse(n,true,out C id)) continue;
                db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1);
                db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            }
        }
    }

// 黏弹爆破手 4/4 燃灯元素 沙画元素 音箱践踏者 随行肉虫 自由飞鸟
    // 着魔的技师 灼烧掠夺者 桌游角色扮演玩家 蓄谋诈骗犯
    public sealed class Script_BC4_Misc : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "CATA_186","VAC_442","TOY_513","JAM_034",
                "VAC_935","ETC_336","CATA_780","CATA_160","TOY_915","VAC_333" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); }
        }
    }

// 护巢龙 4/5 手牌条件→召2个3/3嘲讽
    public sealed class Script_CATA_132 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_132", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 净化祭司 (CATA_216) 4/5 战吼：本局治疗+2。(标记)
    public sealed class Script_CATA_216 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_216", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 盛怒主母 (CATA_305) 3/3 回合结束：如果满血，+3生命值。
    public sealed class Script_CATA_305 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_305", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s != null && s.Health >= s.MaxHealth)
                    H.Buff(s, 0, 3);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Buff);
        }
    }

// 大法师卡雷 4/4 BC:法术获法伤+1
    public sealed class Script_CATA_458 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_458",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 多彩龙巢母 2/5 突袭 攻击→复原等攻法力
    public sealed class Script_CATA_469 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_469", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 矛心哨卫 (CATA_474) 3/4 回合结束：获取一张神圣法术-3费。
    public sealed class Script_CATA_474 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_474", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) => b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Draw);
        }
    }

// ── 不稳定的施法者 (CATA_483) 2/5 法伤+1 战吼：如果本回合用法术造过伤害，召复制。
    public sealed class Script_CATA_483 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_483", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                if (b.CardsPlayedThisTurn > 0) // 近似：用过牌就召复制
                {
                    var copy = s.Clone();
                    copy.IsTired = true;
                    b.FriendMinions.Add(copy);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 地狱公爵 2/2 突袭 每弃牌+2/+2
    public sealed class Script_CATA_493 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_493", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 吉恩咒厄国王 5/4 手牌偶/奇变形
    public sealed class Script_CATA_615 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_615", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 火箭跳蛙 (MIS_306) 10/10 突袭 过载4。(纯白板)
    // 无需注册效果

    // ── 缚风者 (CATA_724) 7/7 亡语：解锁过载法力。过载3。
    public sealed class Script_CATA_724 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_724", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                int overloaded = b.FriendHero?.OverloadedCrystals ?? 0;
                if (overloaded > 0) b.Mana += overloaded;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Mana);
        }
    }

// 混沌祈求者 3/5 施法后→施随机同费另一职业法术
    public sealed class Script_CATA_786 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_786", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 鳞甲长矛手 6/6 所有敌方随从拥有嘲讽
    public sealed class Script_CATA_898 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_898", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions) m.IsTaunt = true;
            });
        }
    }

// ── 厚皮科多兽 (CORE_BAR_535) 3/5 嘲讽 亡语：获得5点护甲值。
    public sealed class Script_CORE_BAR_535 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_BAR_535",
                new TriggerDef("Deathrattle", "None", new EffectDef("armor", v: 5)));
    }

// ── 凯恩·日怒 (CORE_BT_187) 3/5 冲锋 ──
    // 所有友方攻击无视嘲讽。（光环标记）
    public sealed class Script_CORE_BT_187 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_187", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ══════════════════ 4 费 ══════════════════

    // ── 暴怒邪吼者 (CORE_BT_416) 4/4 ──
    // 战吼：你的下一张恶魔牌的法力值消耗减少（2）点。
    public sealed class Script_CORE_BT_416 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_416", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var c in b.Hand)
                {
                    if (H.IsDemonCard(c.CardId))
                    {
                        c.Cost = Math.Max(0, c.Cost - 2);
                        break;
                    }
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ── 锦鱼人水语者 (CORE_CFM_061) 4/6 ──
    // 战吼：恢复6点生命值。过载：（1）
    public sealed class Script_CORE_CFM_061 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CFM_061", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null)
                    t.Health = Math.Min(t.MaxHealth, t.Health + 6);
                // 过载无法在模拟中完整追踪，但标记
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Heal);
        }
    }

// 森金持盾卫士 3/5 嘲讽
    public sealed class Script_CORE_CS2_179 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_CS2_179"); } }

// ── 荆棘帮蟊贼 (CORE_DAL_416) 4/4 战吼：发现另一职业法术。
    public sealed class Script_CORE_DAL_416 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DAL_416", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 黑骑士 (CORE_EX1_002) 4/4 可交易 ──
    // 战吼：消灭一个具有嘲讽的敌方随从。
    public sealed class Script_CORE_EX1_002 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_002", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null && t.IsTaunt) t.Health = 0;
            }, BattlecryTargetType.EnemyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ── 王牌猎人 (CORE_EX1_005) 4/2 可交易 ──
    // 战吼：消灭一个攻击力≥7的随从。
    public sealed class Script_CORE_EX1_005 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_005", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null && t.Atk >= 7) t.Health = 0;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ── 暮光幼龙 (CORE_EX1_043) 4/1 ──
    // 战吼：你每有一张手牌，便获得+1生命值。
    public sealed class Script_CORE_EX1_043 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_043", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                int handCards = b.Hand.Count;
                s.Health += handCards;
                s.MaxHealth += handCards;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 吸血蚊 (CORE_GIL_622) 3/3 ──
    // 战吼：对敌方英雄造成3点伤害。为你的英雄恢复3点生命值。
    public sealed class Script_CORE_GIL_622 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_GIL_622", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.EnemyHero != null)
                    CardEffectDB.Dmg(b, b.EnemyHero, 3);
                if (b.FriendHero != null)
                    b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + 3);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage | EffectKind.Heal);
        }
    }

// 闪光的骏马 3/3 对手回合扰魔
    public sealed class Script_CORE_LOOT_193 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_LOOT_193"); } }

// ── 恐怖海盗 (CORE_NEW1_022) 3/3 嘲讽 随武器攻击力减费。
    public sealed class Script_CORE_NEW1_022 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_NEW1_022", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 范达尔·鹿盔 3/6 抉择同时两种效果
    public sealed class Script_CORE_OG_044 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CORE_OG_044", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 血蹄勇士 (CORE_OG_218) 2/6 嘲讽 受伤时+3攻。(光环)
    public sealed class Script_CORE_OG_218 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_OG_218", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 蛛魔护群守卫 (CORE_RLK_062 / RLK_062) 1/3 嘲讽 ──
    // 战吼：召唤本随从的两个复制。
    public sealed class Script_CORE_RLK_062 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var name in new[] { "CORE_RLK_062", "RLK_062" })
            {
                if (!Enum.TryParse(name, true, out C id)) continue;
                db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                {
                    if (s == null) return;
                    for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    {
                        var copy = s.Clone();
                        copy.IsTired = true;
                        b.FriendMinions.Add(copy);
                    }
                });
                db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
            }
        }
    }

// ── 王室图书管理员 (CORE_SW_066) 4/4 可交易 ──
    // 战吼：沉默一个随从。
    public sealed class Script_CORE_SW_066 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_SW_066",
                new TriggerDef("Battlecry", "AnyMinion",
                    new EffectDef("silence")));
    }

// 花园猎豹 4/4 突袭 攻击→英雄+3攻
    public sealed class Script_CORE_SW_431 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CORE_SW_431", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 火羽凤凰 (CORE_UNG_084) 3/4 ──
    // 战吼：造成3点伤害。
    public sealed class Script_CORE_UNG_084 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_UNG_084",
                new TriggerDef("Battlecry", "AnyCharacter",
                    new EffectDef("dmg", v: 3)));
    }

// ── 键盘独演者 (ETC_029) 2/4 战吼：无其他随从时召两个1/2法伤+1。
    public sealed class Script_ETC_029 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_029", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                bool solo = b.FriendMinions.Count(m => !ReferenceEquals(m, s)) == 0;
                if (!solo) return;
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 1, Health = 2, MaxHealth = 2, IsFriend = true,
                        SpellPower = 1, IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 疯狂的指挥 3/4 BC:受疲劳伤→召同数3/3
    public sealed class Script_ETC_070 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_070",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 痴醉歌迷 2/6 BC:选随从获潜行
    public sealed class Script_ETC_108 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_108",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { if(t!=null) t.IsStealth=true; }, BattlecryTargetType.FriendlyMinion); } }

// 商品卖家 3/5 回合结束→法术置对手牌库顶
    public sealed class Script_ETC_111 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_111", true, out C id)) return; db.Register(id, EffectTrigger.EndOfTurn, (b,s,t)=>{}); } }

// 摇摆舞虫 4/3 圣盾 友方失去圣盾→抽牌
    public sealed class Script_ETC_324 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_324", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 碎心歌手赫达尼斯 (ETC_334) 4/8 ──
    // 战吼：对本随从造成4点伤害。
    public sealed class Script_ETC_334 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_334", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null) s.Health -= 4;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ════════════ 4费纯白板/关键词 ════════════
    // 铜管元素 3/3 突袭圣盾嘲讽风怒
    public sealed class Script_ETC_357 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "ETC_357"); } }

// 哈维利亚·墨鸦 4/3 突袭 友突袭随从攻击后全体+1攻
    public sealed class Script_ETC_399 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_399", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 乐器杀手 3/6 武器摧毁→随机装备恶猎武器
    public sealed class Script_ETC_400 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_400", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 音响工程师普兹克 (ETC_425) 5/4 战吼：给对手两张3/3。亡语：为自己召唤。
    public sealed class Script_ETC_425 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_425", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 3, Health = 3, MaxHealth = 3, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// 烟火技师 2/5 施法→获火焰法术
    public sealed class Script_ETC_540 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_540", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 举烛观众 3/3 圣盾 压轴相邻获圣盾
    public sealed class Script_ETC_543 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "ETC_543"); } }

// ── 约德尔狂吼歌手 (JAM_005) 3/4 ──
    // 战吼：触发一个友方随从的亡语，触发两次。
    public sealed class Script_JAM_005 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_005", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || !t.HasDeathrattle) return;
                if (!db.TryGet(t.CardId, EffectTrigger.Deathrattle, out var drFn)) return;
                drFn(b, t, null);
                drFn(b, t, null);
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// 酷炫的食尸鬼 3/1 圣盾复生
    public sealed class Script_JAM_007 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "JAM_007"); } }

// ── 后台保镖 (JAM_014) 4/5 嘲讽 战吼：将一个友方随从变形成本随从的复制。
    public sealed class Script_JAM_014 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_014", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || s == null) return;
                t.Atk = s.Atk; t.Health = s.Health; t.MaxHealth = s.MaxHealth;
                t.IsTaunt = s.IsTaunt;
            }, BattlecryTargetType.FriendlyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 踏韵舞者丽萨 3/5 突袭 英雄攻击后弹回手牌1费
    public sealed class Script_JAM_019 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("JAM_019", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 布景光耀之子 3/4 压轴过量治疗→+2/+2
    public sealed class Script_JAM_024 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("JAM_024", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 恐怖图腾扫兴怪 5/4 BC:弃武器抽3
    public sealed class Script_JAM_035 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("JAM_035",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { for(int i=0;i<3;i++) CardEffectDB.DrawCard(b, b.FriendDeckCards); }); } }

// ── 地狱火！(MIS_703) 6/6 嘲讽 ──
    // 战吼：将你的英雄的剩余生命值变为15。
    public sealed class Script_MIS_703 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_703", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Health = 15;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Heal);
        }
    }

// 残次聒噪怪 3/3 英雄攻击后→召复制
    public sealed class Script_MIS_911 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("MIS_911", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 蛛魔护群守卫 (RLK_062/CORE_RLK_062) 1/3 嘲讽 战吼：召两个复制。
    public sealed class Script_RLK_062 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var name in new[] { "RLK_062", "CORE_RLK_062" })
            {
                if (!Enum.TryParse(name, true, out C id)) continue;
                db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                {
                    if (s == null) return;
                    for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    {
                        var copy = s.Clone();
                        copy.IsTired = true;
                        b.FriendMinions.Add(copy);
                    }
                });
                db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
            }
        }
    }

// ── 萨萨里安 (RLK_223) 3/3 复生 ──
    // 战吼，亡语：随机对一个敌人造成2点伤害。
    public sealed class Script_RLK_223 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_223", true, out C id)) return;
            Action<SimBoard, SimEntity, SimEntity> dmgRandom = (b, s, t) =>
            {
                var targets = new List<SimEntity>();
                targets.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                if (targets.Count == 0) return;
                var pick = targets[H.PickIndex(targets.Count, b, s)];
                CardEffectDB.Dmg(b, pick, 2);
            };
            db.Register(id, EffectTrigger.Battlecry, dmgRandom);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
            db.Register(id, EffectTrigger.Deathrattle, dmgRandom);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 亡语者女士 (RLK_713) 4/3 亡语：复制手牌中冰霜法术。
    public sealed class Script_RLK_713 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_713", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 恶毒恐魔 (RLK_745 / CORE_RLK_745) 2/4 复生 回合结束消耗4残骸召复制。
    public sealed class Script_RLK_745 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var name in new[] { "RLK_745", "CORE_RLK_745" })
            {
                if (!Enum.TryParse(name, true, out C id)) continue;
                db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
                {
                    if (s == null || b.FriendMinions.Count >= 7) return;
                    if (b.FriendExcavateCount >= 4)
                    {
                        b.FriendExcavateCount -= 4;
                        var copy = s.Clone();
                        copy.IsTired = true;
                        b.FriendMinions.Add(copy);
                    }
                });
                db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
            }
        }
    }

// 恋旧的侏儒 4/4 微缩突袭 精确击杀→抽牌
    public sealed class Script_TOY_312 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_312", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 神秘的蛋 (TOY_351) 0/3 亡语：获取牌库野兽牌的复制-3费。
    public sealed class Script_TOY_351 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_351", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                H.DrawRandomCardToHandByPredicate(b, H.IsBeastCard, s));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 三芯诡烛 (TOY_370) 2/3 ──
    // 战吼：随机对一个敌人造成2点伤害，触发三次。
    public sealed class Script_TOY_370 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_370", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 3; i++)
                {
                    var targets = new List<SimEntity>();
                    targets.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                    if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                    if (targets.Count == 0) break;
                    var pick = targets[H.PickIndex(targets.Count, b, s)];
                    CardEffectDB.Dmg(b, pick, 2);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// 粉笔美术家 4/3 BC:抽随从变传说
    public sealed class Script_TOY_388 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_388",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> CardEffectDB.DrawCard(b, b.FriendDeckCards)); } }

// 漫画美术家 3/4 BC:抽5+费随从
    public sealed class Script_TOY_391 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_391",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> CardEffectDB.DrawCard(b, b.FriendDeckCards)); } }

// 神秘女巫哈加莎 4/3 BC:抽2张5+法术
    public sealed class Script_TOY_504 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_504",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { CardEffectDB.DrawCard(b, b.FriendDeckCards); CardEffectDB.DrawCard(b, b.FriendDeckCards); }); } }

// 水上舞者索尼娅 3/3 用1费随从→获0费复制
    public sealed class Script_TOY_515 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_515", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 球霸野猪人 (TOY_642) 3/3 吸血 ──
    // 战吼，亡语：对生命值最低的敌人造成3点伤害。
    public sealed class Script_TOY_642 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_642", true, out C id)) return;
            Action<SimBoard, SimEntity, SimEntity> dmgLowest = (b, s, t) =>
            {
                var targets = new List<SimEntity>();
                targets.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                if (targets.Count == 0) return;
                var lowest = targets.OrderBy(e => e.Health + e.Armor).First();
                CardEffectDB.Dmg(b, lowest, 3);
            };
            db.Register(id, EffectTrigger.Battlecry, dmgLowest);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
            db.Register(id, EffectTrigger.Deathrattle, dmgLowest);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// 实验室奴隶主 3/3 获护甲→召复制每回合一次
    public sealed class Script_TOY_651 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_651", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 欢乐的玩具匠 (TOY_670) 2/1 亡语：召两个1/2嘲讽圣盾机械。
    public sealed class Script_TOY_670 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_670", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 1, Health = 2, MaxHealth = 2, IsFriend = true,
                        IsTaunt = true, IsDivineShield = true, IsTired = true,
                        Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// 酷炫的威兹班 4/5（对战开始效果不影响模拟）
    public sealed class Script_TOY_700 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TOY_700"); } }

// 绿植幼龙 3/5 微缩 抉择法伤+1/抽法术
    public sealed class Script_TOY_801 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_801", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 黑棘针线师 (TOY_824) 2/4 回合结束：造成等攻伤害随机分敌人。
    public sealed class Script_TOY_824 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_824", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null) return;
                for (int i = 0; i < s.Atk; i++)
                {
                    var ts = new List<SimEntity>();
                    ts.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                    if (b.EnemyHero != null) ts.Add(b.EnemyHero);
                    if (ts.Count == 0) break;
                    CardEffectDB.Dmg(b, ts[H.PickIndex(ts.Count, b, s)], 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 套娃傀儡 (TOY_893) 4/3 亡语：再次召唤本随从-1/-1。
    public sealed class Script_TOY_893 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_893", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                int a = Math.Max(0, s.Atk - 1);
                int h = Math.Max(1, s.MaxHealth - 1);
                if (a <= 0 && h <= 1) return; // 太小了不召
                b.FriendMinions.Add(new SimEntity
                {
                    CardId = s.CardId, Atk = a, Health = h, MaxHealth = h,
                    IsFriend = true, IsTired = true, HasDeathrattle = true,
                    Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 折纸仙鹤 (TOY_895) 4/1 嘲讽 ──
    // 战吼：与另一个随从交换生命值。
    public sealed class Script_TOY_895 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_895", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || t == null) return;
                int tmpHp = s.Health;
                int tmpMax = s.MaxHealth;
                s.Health = t.Health;
                s.MaxHealth = t.MaxHealth;
                t.Health = tmpHp;
                t.MaxHealth = tmpMax;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 墓地叛徒 (TTN_455) 4/3 ──
    // 战吼：摧毁你对手牌库中的一张疫病牌，对所有敌方随从造成3点伤害。
    public sealed class Script_TTN_455 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_455", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.EnemyDeckPlagueCount > 0) b.EnemyDeckPlagueCount--;
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 3);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 破链角斗士 (TTN_475) 4/4 战吼：抽一张牌。
    public sealed class Script_TTN_475 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_475", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 求知造物 (TTN_478) 4/3/4 战吼：对所有敌方随从造成1伤害（随法术派系提升）。
    public sealed class Script_TTN_478 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_478", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 萨格拉斯的信徒 (TTN_490) 3/3 战吼：弃法术→召两个3/2小鬼。
    public sealed class Script_TTN_490 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_490", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                var spells = b.Hand.Where(c => H.IsSpellCard(c.CardId)).ToList();
                if (spells.Count == 0) return;
                b.Hand.Remove(spells[H.PickIndex(spells.Count, b, s)]);
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 3, Health = 2, MaxHealth = 2, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 艾欧娜尔的信徒 4/4 BC:下个抉择双效
    public sealed class Script_TTN_503 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_503",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 萨隆邪铁托维尔 3/5 嘲讽 受到攻击→抽牌
    public sealed class Script_TTN_711 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_711", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 生气的冥狱之犬 2/5 突袭 你的回合+4攻
    public sealed class Script_TTN_713 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_713", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 迷时始祖幼龙 4/5 嘲讽 抽到获龙牌
    public sealed class Script_TTN_715 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_715", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 实验室构筑机 (TTN_730) 3/2 回合结束：召唤本随从的复制。
    public sealed class Script_TTN_730 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_730", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                var copy = s.Clone();
                copy.IsTired = true;
                b.FriendMinions.Add(copy);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
        }
    }

// 风暴勇士 3/5 自然法术后→召4/2元素
    public sealed class Script_TTN_801 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_801", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ════════════ 4费 光环/触发 ════════════
    // 艾瑞达欺诈者 3/5 抽牌→召1/1突袭恶魔
    public sealed class Script_TTN_843 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TTN_843", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 海拉 (TTN_850) 4/4 ──
    // 战吼：将全部三种疫病牌洗入你对手的牌库。
    public sealed class Script_TTN_850 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_850", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                b.EnemyDeckPlagueCount += 3;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 石心之王 (TTN_900) 3/2 亡语：召2/2土灵。
    public sealed class Script_TTN_900 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_900", true, out C id)) return;
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

// ── 星界翔龙 (TTN_907) 3/3 回合结束：如果未攻击，抽两张牌。
    public sealed class Script_TTN_907 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_907", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null || s.CountAttack > 0) return;
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Draw);
        }
    }

// 威严的阿努比萨斯 7/7 嘲讽 无法攻击
    public sealed class Script_TTN_931 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_931"); } }

// ── 断生鱿鱼 (VAC_341) 3/4 ──
    // 战吼：消灭一个攻击力≤本随从的敌方随从。
    public sealed class Script_VAC_341 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_341", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null && s != null && t.Atk <= s.Atk)
                    t.Health = 0;
            }, BattlecryTargetType.EnemyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ── 困倦的岛民 (VAC_406) 3/5 嘲讽 亡语：所有其他随从陷入沉睡（疲惫）。
    public sealed class Script_VAC_406 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_406", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                    if (!ReferenceEquals(m, s)) m.IsTired = true;
                foreach (var m in b.EnemyMinions) m.IsTired = true;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 沙滩塑形师蕾拉 2/6 施法→召2费随从+圣盾
    public sealed class Script_VAC_424 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_424", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 伊丽扎·刺刃 (VAC_426) 4/3 亡语：本局随从永久+1攻。
    public sealed class Script_VAC_426 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_426", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                    if (!ReferenceEquals(m, s)) m.Atk += 1;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// 滑雪高手 4/4 有冻结角色→1费
    public sealed class Script_VAC_429 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_429", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 召唤师达克玛洛 4/4 亡语触发两次 亡语随从消灭
    public sealed class Script_VAC_503 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_503", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 海潮之王泰德 4/4 BC:法术费用变5
    public sealed class Script_VAC_524 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_524",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 烧烤大师 (VAC_917) 3/4 战吼抽最低费牌，亡语抽最高费牌。
    public sealed class Script_VAC_917 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_917", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// 救生员 2/7 嘲讽 BC:下法术吸血
    public sealed class Script_VAC_919 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_919",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// 八爪按摩机 1/8 对随从八倍伤害
    public sealed class Script_VAC_936 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_936", true, out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 波池造浪者 (VAC_947) 4/5 ──
    // 战吼：使所有其他随从获得-1/-1。亡语：使所有其他随从获得+1/+1。
    public sealed class Script_VAC_947 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_947", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions.ToArray())
                    if (!ReferenceEquals(m, s)) { m.Atk--; m.Health--; m.MaxHealth--; }
                foreach (var m in b.EnemyMinions.ToArray())
                    { m.Atk--; m.Health--; m.MaxHealth--; }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                    H.Buff(m, 1, 1);
                foreach (var m in b.EnemyMinions)
                    H.Buff(m, 1, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// 顶流主唱 3/3 BC:每派系法术-2费
    public sealed class Script_VAC_954 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_954",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 蒸汽守卫 (TTN_468) 3/3 战吼：抽法术牌+减费火焰法术。
    // (已在 Minions_Cost3 中注册，此处跳过避免重复)

    // ── 断生鱿鱼 (VAC_341) 3/4 战吼：消灭攻击力≤本随从的敌方随从。
    // (已在 Minions_Cost4_5 中注册，此处跳过)

    // ── 合金顾问 (WORK_023) 2/6 嘲讽 每当受伤获得3护甲。(光环)
    public sealed class Script_WORK_023 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_023", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 数据分析狮 2/1 突袭嘲讽 选3次+2攻/+2血
    public sealed class Script_WORK_025 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "WORK_025"); } }

// 摧心魔 4/4 BC:每抽牌造1伤
    public sealed class Script_YOG_402 : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("YOG_402",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=>{}); } }

// ── 髓骨使御者 (RLK_505/CORE_RLK_505) — 已在 Class_DK_DH.cs 注册，跳过

    // ── 越狱者 (YOG_411) 4/4 战吼：用过5+法术→对所有敌人2伤害。
    public sealed class Script_YOG_411 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_411", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 近似：总是触发
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 2);
                if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 扭曲的霜翼龙 (YOG_506) 3/3 突袭 亡语：召属性值等同攻击力的奇美拉。
    public sealed class Script_YOG_506 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_506", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                int a = Math.Max(1, s.Atk);
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = a, Health = a, MaxHealth = a, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 报警安保机器人 (YOG_510) 4/3 亡语：抽随从牌，属性和费用变5。
    public sealed class Script_YOG_510 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_510", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 被寄生的看守者 (YOG_523) 4/4 嘲讽 亡语：获取两张1/1混乱触须。
    public sealed class Script_YOG_523 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_523", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 迷你灭世者 (YOG_527) 4/4 ──
    // 战吼：如果你控制着其他机械，则造成4点伤害。
    public sealed class Script_YOG_527 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_527", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                bool hasMech = b.FriendMinions.Any(m =>
                    !ReferenceEquals(m, s) && H.IsMechMinion(m.CardId));
                if (hasMech && t != null)
                    CardEffectDB.Dmg(b, t, 4);
            }, BattlecryTargetType.EnemyOnly);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

}
