// ═══════════════════════════════════════════════════════════════
//  标准随从效果 — 7+ 费随从
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

    public sealed class Script_Aura7 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "VAC_950","TTN_862","CATA_300","TOY_906",
                "CATA_494","TOY_807","VAC_930","CATA_550","TTN_926","TTN_858",
                "JAM_016","TTN_429" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); }
        }
    }

// ═══ 7费 ═══
    public sealed class Script_BC7 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "CATA_553","VAC_415","ETC_541","VAC_702",
                "ETC_409","CORE_GIL_598","VAC_301","VAC_321","CATA_591" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); }
        }
    }

// ── 阿莱克丝塔萨，生命守护者 (CATA_307) 8/8 ──
    // 战吼：将你的英雄剩余生命值变为15。
    public sealed class Script_CATA_307 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_307", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Health = 15;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Heal);
        }
    }

// ── 沃坎诺斯 (CATA_488) 4/8 回合结束对所有其他随从造成2伤害。
    public sealed class Script_CATA_488 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_488", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions.ToArray())
                    if (!ReferenceEquals(m, s)) CardEffectDB.Dmg(b, m, 2);
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 战争大师黑角 (CATA_720) 6/6 战吼：摧毁双方牌库中≤2费牌。
    public sealed class Script_CATA_720 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_720", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 龙脉混血兽 (CATA_723) 8/6 亡语：召两个4费随从。
    public sealed class Script_CATA_723 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_723", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 4, Health = 4, MaxHealth = 4, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 冰喉 (CORE_AT_123) 6/6 嘲讽 亡语：如果手牌有龙→对所有随从3伤害。
    public sealed class Script_CORE_AT_123 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_AT_123", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (!H.HasDragonInHand(b, s)) return;
                foreach (var m in b.FriendMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 3);
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 3);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 山岭野熊 (CORE_AV_337) 5/6 嘲讽 亡语：召两只2/4嘲讽山熊宝宝。
    public sealed class Script_CORE_AV_337 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_AV_337", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 2, Health = 4, MaxHealth = 4, IsFriend = true,
                        IsTaunt = true, IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 愤怒的女祭司 (CORE_BT_493) 6/7 ──
    // 在你的回合结束时，造成6点伤害，随机分配到所有敌人身上。
    public sealed class Script_CORE_BT_493 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_493", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                for (int i = 0; i < 6; i++)
                {
                    var targets = new List<SimEntity>();
                    targets.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                    if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                    if (targets.Count == 0) break;
                    var pick = targets[H.PickIndex(targets.Count, b, s)];
                    CardEffectDB.Dmg(b, pick, 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 暴风城勇士 (CORE_CS2_222) 7/7 其他随从+1/+1。(光环)
    public sealed class Script_CORE_CS2_222 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_CS2_222", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 娜塔莉·塞林 (CORE_EX1_198) 7/1 ──
    // 战吼：消灭一个随从并获得其生命值。
    public sealed class Script_CORE_EX1_198 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_198", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || s == null) return;
                int gainedHp = t.Health;
                t.Health = 0;
                s.Health += gainedHp;
                s.MaxHealth += gainedHp;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ═══════ 法师 MAGE ═══════

    // ── 大法师安东尼达斯 (CORE_EX1_559) 5/7 施放法术→得火球。(光环)
    public sealed class Script_CORE_EX1_559 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_559", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 斯尼德的伐木机 (CORE_GVG_114) 5/7 亡语：召传说随从。
    public sealed class Script_CORE_GVG_114 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_GVG_114", true, out C id)) return;
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

// ── 阿纳克洛斯 (CORE_RLK_919) 8/8 ──
    // 战吼：将所有其他随从送入2回合后的未来。
    public sealed class Script_CORE_RLK_919 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_RLK_919", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 清场效果（随从消失2回合，等于短期清场）
                b.FriendMinions.RemoveAll(m => !ReferenceEquals(m, s));
                b.EnemyMinions.Clear();
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ── 希亚玛特 (CORE_ULD_178) 7/7 ──
    // 战吼：从突袭/嘲讽/圣盾/风怒中获得两种效果。
    public sealed class Script_CORE_ULD_178 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_ULD_178", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                // 近似：给突袭+嘲讽（最常见的实战选择）
                s.HasRush = true;
                s.IsTired = false;
                s.IsTaunt = true;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// 永恒者诺兹多姆 8/8 白板
    public sealed class Script_CS3_035 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CS3_035"); } }

// ── 玩具暴龙 (TOY_356) 7/7 突袭 亡语：随机对一个敌人造成7点伤害。
    public sealed class Script_TOY_356 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_356", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var ts = new List<SimEntity>();
                ts.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) ts.Add(b.EnemyHero);
                if (ts.Count > 0) CardEffectDB.Dmg(b, ts[H.PickIndex(ts.Count, b, s)], 7);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 闪岩哨兵 (TOY_503) 3/7 嘲讽 扰魔 ──
    // 战吼：召唤一个本随从的复制。
    public sealed class Script_TOY_503 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_503", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                var copy = s.Clone();
                copy.IsTired = true;
                b.FriendMinions.Add(copy);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 皮普希·彩蹄 (TOY_812) 4/4 亡语：从牌库召圣盾/突袭/嘲讽随从各1。
    public sealed class Script_TOY_812 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_812", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 3 && b.FriendMinions.Count < 7; i++)
                {
                    if (b.FriendDeckCards.Count > 0)
                    {
                        var card = b.FriendDeckCards[0];
                        b.FriendDeckCards.RemoveAt(0);
                        CardEffectDB.Summon(b, b.FriendMinions, card);
                    }
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 羁押装置 (TTN_700) 6/6 磁力 亡语：召8费随从。
    public sealed class Script_TTN_700 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_700", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 8, Health = 8, MaxHealth = 8, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 神秘恐魔 (TTN_866) 4/10 吸血 回合结束：迫使敌方随从攻击本随从。
    public sealed class Script_TTN_866 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_866", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null) return;
                // 所有敌方随从攻击本随从=本随从受伤+敌方随从受伤
                foreach (var m in b.EnemyMinions.ToArray())
                {
                    if (m.Atk > 0) CardEffectDB.Dmg(b, s, m.Atk);
                    CardEffectDB.Dmg(b, m, s.Atk);
                    // 吸血恢复
                    if (b.FriendHero != null && s.IsLifeSteal)
                        b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth,
                            b.FriendHero.Health + Math.Min(s.Atk, m.Health));
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 打盹的动物管理员 (VAC_421) 5/8 战吼：给对手召8/8野兽，攻击所有随从。
    public sealed class Script_VAC_421 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_421", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 8/8攻击对手所有随从=8伤分到对手所有随从
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 8);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 搁浅巨鲸 (VAC_934) 4/20 嘲讽 ──
    // 战吼：对本随从造成10点伤害。
    public sealed class Script_VAC_934 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_934", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null) s.Health -= 10;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 维扎克斯将军 (YOG_500) 7/6 突袭 战吼：获得4护甲。亡语：失去4护甲再召唤。
    public sealed class Script_YOG_500 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("YOG_500", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Armor += 4;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Armor = Math.Max(0, b.FriendHero.Armor - 4);
                if (b.FriendMinions.Count < 7)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 7, Health = 6, MaxHealth = 6, IsFriend = true,
                        HasRush = true, IsTired = false, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ═══ 8+费 ═══
    public sealed class Script_BC8Plus : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "TOY_960","TTN_441" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 2); }
        }
    }

// 梦境之龙麦琳瑟拉 4/12 BC:填满手牌
    public sealed class Script_CATA_140_BC : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_140",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> { int n= Math.Max(0, 10 - b.Hand.Count); b.FriendCardDraw += n; }); } }

// 艾萨拉 8/8 巨型+2 英雄风怒
    public sealed class Script_CATA_151 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CATA_151"); } }

// 奥拉基尔风暴之主 2/8 巨型+2 突袭风怒 BC:获取2卡
    public sealed class Script_CATA_153_BC : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_153",true,out C id)) return; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 2); } }

// 克洛玛图斯 8/8 巨型+4 嘲讽吸血扰魔圣盾
    public sealed class Script_CATA_432 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CATA_432"); } }

// ── 青铜护卫者 (CATA_476) 3/7 回合结束：召6/6圣盾元素龙。
    public sealed class Script_CATA_476 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_476", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 6, Health = 6, MaxHealth = 6, IsFriend = true,
                    IsDivineShield = true, IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
        }
    }

// ── 改进型恐惧魔王 (CORE_BT_304) 5/7 嘲讽 亡语：召5/5吸血恐惧魔王。
    public sealed class Script_CORE_BT_304 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BT_304", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 5, Health = 5, MaxHealth = 5, IsFriend = true,
                    IsLifeSteal = true, IsTired = true, Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 卡雷苟斯 (CORE_DAL_609) 4/12 第一法术0费 战吼：发现法术。
    public sealed class Script_CORE_DAL_609 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DAL_609", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                b.FriendCardDraw += 1);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// ── 炎魔之王拉格纳罗斯 (CORE_EX1_298) 8/8 回合结束：随机对敌人8伤害。
    public sealed class Script_CORE_EX1_298 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_298", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                var ts = new List<SimEntity>();
                ts.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) ts.Add(b.EnemyHero);
                if (ts.Count > 0) CardEffectDB.Dmg(b, ts[H.PickIndex(ts.Count, b, s)], 8);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 提里奥·弗丁 (CORE_EX1_383) 8/8 圣盾嘲讽 亡语：装备5/3灰烬使者。
    public sealed class Script_CORE_EX1_383 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_383", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
                H.EquipWeaponByStats(b, true, 0, 5, 3));
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// ── 格罗玛什·地狱咆哮 (CORE_EX1_414) 4/9 冲锋 受伤时+6攻。(光环)
    public sealed class Script_CORE_EX1_414 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_414", true, out C id)) return;
            db.Register(id, EffectTrigger.Aura, (b, s, t) => { });
        }
    }

// 风领主奥拉基尔 3/6 冲锋圣盾嘲讽风怒
    public sealed class Script_CORE_NEW1_010 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_NEW1_010"); } }

// ── 莫尔葛熔魔 (CORE_SW_068) 8/8 嘲讽 亡语：获得8护甲。
    public sealed class Script_CORE_SW_068 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_SW_068", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendHero != null) b.FriendHero.Armor += 8;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Buff);
        }
    }

// ── 八爪巨怪 (CORE_ULD_177) 8/8 亡语：抽八张牌。
    public sealed class Script_CORE_ULD_177 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_ULD_177", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 8; i++)
                    CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Draw);
        }
    }

// ── 始生幼龙 (CORE_UNG_848) 4/8 嘲讽 ──
    // 战吼：对所有其他随从造成2点伤害。
    public sealed class Script_CORE_UNG_848 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_UNG_848", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions.ToArray())
                    if (!ReferenceEquals(m, s)) CardEffectDB.Dmg(b, m, 2);
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// 公演增强幼龙 8/8 压轴消灭敌方随从
    public sealed class Script_ETC_099_Extra : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_099",true,out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// 摇滚缝合怪 3/10 突袭 攻击后+1攻再打
    public sealed class Script_ETC_419_Extra : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("ETC_419",true,out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 凯吉·海德 (ETC_526) 5/1 ──
    // 亡语：召唤一只9/9并具有冲锋和嘲讽的凋零野猪。
    public sealed class Script_ETC_526 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_526", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 9, Health = 9, MaxHealth = 9, IsFriend = true,
                    HasCharge = true, IsTaunt = true, IsTired = false,
                    Type = Card.CType.MINION
                });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 发明家砰砰 (TOY_607) 7/7 战吼：复活两个≥5费机械。
    public sealed class Script_TOY_607 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_607", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 5, Health = 5, MaxHealth = 5, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 玛瑟里顿 12/12 休眠 回合结束对所有敌人3伤
    public sealed class Script_TOY_647_EoT : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("TOY_647",true,out C id)) return; db.Register(id, EffectTrigger.EndOfTurn, (b,s,t)=> { foreach(var m in b.EnemyMinions.ToArray()) CardEffectDB.Dmg(b,m,3); if(b.EnemyHero!=null) CardEffectDB.Dmg(b,b.EnemyHero,3); }); } }

// ── 美术家可丽菲罗 (TOY_703) 6/5 战吼：抽随从牌，其他友方随从变其复制。
    public sealed class Script_TOY_703 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_703", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 邪鬼皇后 (TOY_914) 4/4 嘲讽 亡语：召二个4/6嘲讽骑士。
    public sealed class Script_TOY_914 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_914", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 4, Health = 6, MaxHealth = 6, IsFriend = true,
                        IsTaunt = true, IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 霍迪尔之子 (TTN_083) 8/8 战吼：洗入四张8/8巨人（抽到时召唤）。
    public sealed class Script_TTN_083 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_083", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 4; i++)
                    b.FriendDeckCards.Add(0); // 巨人占位
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 科隆加恩 (TTN_330) 6/10 突袭 亡语：将手牌中的随从移给对手。
    public sealed class Script_TTN_330 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_330", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// 风暴巨人 8/8 嘲讽 锻造减费
    public sealed class Script_TTN_724 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "TTN_724"); } }

// ── 巨人之父霍迪尔 (TTN_752) 8/8 战吼：下三张随从属性变8/8。
    public sealed class Script_TTN_752 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_752", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 首管奥丁 (TTN_811) 8/8 战吼：护甲后获等量攻。(标记)
    public sealed class Script_TTN_811 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_811", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// 龙族美餐 6/6 突袭扰魔 每次只受1伤
    public sealed class Script_VAC_527_Extra : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "VAC_527"); } }

    public sealed class Script_Aura8Plus : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "TTN_737","ETC_099","CATA_150","VAC_527",
                "TOY_647","CATA_140","ETC_419","CS3_020","TTN_940",
                "TTN_462","RLK_744","CATA_155","CATA_616","CATA_726","CATA_613",
                "YOG_516","ETC_840","VAC_439","TTN_903","TTN_459","TOY_530","JAM_030",
                "CATA_153","TOY_700" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); }
        }
    }

// ── 暮光主母 (CATA_201) 4/12 ──
    // 战吼：将所有敌方随从移回其拥有者的手牌。
    public sealed class Script_CATA_201 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_201", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    if (b.EnemyHand.Count < 10) b.EnemyHand.Add(m);
                b.EnemyMinions.Clear();
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// 生存专家 6/6 无随从→免疫
    public sealed class Script_CATA_613_Extra : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("CATA_613",true,out C id)) return; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); } }

// ── 恐怖海兽 (CATA_699) 9/6 嘲讽 ──
    // 战吼：选择一个敌方随从，偷取其3点生命值，触发三次。
    public sealed class Script_CATA_699 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_699", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t == null || s == null) return;
                for (int i = 0; i < 3; i++)
                {
                    int steal = Math.Min(3, t.Health);
                    t.Health -= steal;
                    t.MaxHealth -= steal;
                    s.Health += steal;
                    s.MaxHealth += steal;
                }
            }, BattlecryTargetType.EnemyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage | EffectKind.Buff);
        }
    }

// ── 黑曜石雕像 (CORE_ICC_214) 4/8 嘲讽吸血 亡语：消灭一个敌方随从。
    public sealed class Script_CORE_ICC_214 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_ICC_214", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var enemies = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                if (enemies.Count > 0)
                    enemies[H.PickIndex(enemies.Count, b, s)].Health = 0;
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Destroy);
        }
    }

// 贪睡巨龙 6/12 嘲讽
    public sealed class Script_CORE_LOOT_137 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_LOOT_137"); } }

// ── 吵吵演艺团 (ETC_321) 3/6 嘲讽圣盾 亡语：召三个1/2嘲讽圣盾机械。
    public sealed class Script_ETC_321 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_321", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 3 && b.FriendMinions.Count < 7; i++)
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

// ── DJ法力风暴 (ETC_395) 8/8 战吼：手牌法术费用变0。
    public sealed class Script_ETC_395 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_395", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var c in b.Hand)
                    if (H.IsSpellCard(c.CardId)) c.Cost = 0;
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ── 抱龙王噗鲁什 (TOY_357) 6/6 冲锋 战吼：将攻击力<本随从的随从移回牌库。
    public sealed class Script_TOY_357 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_357", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                b.FriendMinions.RemoveAll(m => !ReferenceEquals(m, s) && m.Atk < s.Atk);
                b.EnemyMinions.RemoveAll(m => m.Atk < s.Atk);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Destroy);
        }
    }

// ── 灭世泰坦萨格拉斯 (TTN_960) 6/12 泰坦 战吼：每回合召两个3/2小鬼。
    public sealed class Script_TTN_960 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_960", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 传送门效果 ≈ 每回合召两个3/2
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 3, Health = 2, MaxHealth = 2, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
            // 后续每回合也召
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                for (int i = 0; i < 2 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 3, Health = 2, MaxHealth = 2, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon);
        }
    }

// ── 芝士怪物 (VAC_339) 6/9 嘲讽 回合结束：召属性值等同本随从的元素。
    public sealed class Script_VAC_339 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_339", true, out C id)) return;
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

// ── 莫卓克 (CATA_570) 10/10 ──
    // 战吼：抽一张牌，并使其法力值消耗减少（10）点。
    public sealed class Script_CATA_570 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_570", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                int before = b.Hand.Count;
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
                if (b.Hand.Count > before)
                {
                    var drawn = b.Hand[b.Hand.Count - 1];
                    drawn.Cost = Math.Max(0, drawn.Cost - 10);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw | EffectKind.Mana);
        }
    }

// ── 窜逃的黑翼龙 (CORE_YOP_034) 10/10 回合结束对敌方随从10伤害。
    public sealed class Script_CORE_YOP_034 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_YOP_034", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                var alive = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                if (alive.Count > 0)
                    CardEffectDB.Dmg(b, alive[H.PickIndex(alive.Count, b, s)], 10);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 强音雷象 (ETC_086) 6/12 嘲讽 亡语：对所有敌方随从3伤害。
    public sealed class Script_ETC_086 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_086", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 3);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 安全专家 (MIS_711) 8/8 突袭 亡语：洗入三张炸弹。
    public sealed class Script_MIS_711 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_711", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 3; i++)
                    b.EnemyDeckCards.Add(0); // 炸弹占位
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Utility);
        }
    }

// ── 工厂装配机 (TOY_601) 6/7 回合结束：召6/7机器人攻击敌人。
    public sealed class Script_TOY_601 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_601", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7) return;
                b.FriendMinions.Add(new SimEntity
                {
                    Atk = 6, Health = 7, MaxHealth = 7, IsFriend = true,
                    IsTired = true, Type = Card.CType.MINION
                });
                // 攻击随机敌人近似
                var ts = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                if (ts.Count > 0)
                {
                    var pick = ts[H.PickIndex(ts.Count, b, s)];
                    CardEffectDB.Dmg(b, pick, 6);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Summon | EffectKind.Damage);
        }
    }

// ── 旅行管理员杜加尔 (WORK_043) 3/3 战吼：从牌库召三个随从。
    public sealed class Script_WORK_043 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_043", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                for (int i = 0; i < 3 && b.FriendMinions.Count < 7; i++)
                {
                    if (b.FriendDeckCards.Count > 0)
                    {
                        var cid = b.FriendDeckCards[0];
                        b.FriendDeckCards.RemoveAt(0);
                        CardEffectDB.Summon(b, b.FriendMinions, cid);
                    }
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

}
