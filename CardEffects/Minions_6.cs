// ═══════════════════════════════════════════════════════════════
//  标准随从效果 — 6 费随从
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

    public sealed class Script_Aura6 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "VAC_441","VAC_340","TTN_415","VAC_447",
                "TTN_800","CATA_139","JAM_004","TTN_466","YOG_530","TTN_075",
                "TOY_531","CATA_529","TTN_071","CATA_154","TTN_721" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Aura, (b,s,t)=>{}); }
        }
    }

// ═══ 6费 ═══
    public sealed class Script_BC6 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var n in new[] { "CATA_497","WORK_063","TTN_487","VAC_945",
                "CATA_213","VAC_506","TOY_373","ETC_386","CATA_552" })
            { if (!Enum.TryParse(n,true,out C id)) continue; db.Register(id, EffectTrigger.Battlecry, (b,s,t)=> b.FriendCardDraw += 1); }
        }
    }

// ── 破鳞盾卫 (CATA_475) 3/6 回合结束对所有敌人造成2伤害。
    public sealed class Script_CATA_475 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_475", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 2);
                if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage);
        }
    }

// ── 乌鳞斥候 (CATA_552) 4/4 战吼：造成等攻伤害。
    public sealed class Script_CATA_552 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CATA_552", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (t != null && s != null) CardEffectDB.Dmg(b, t, s.Atk);
            }, BattlecryTargetType.AnyCharacter);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 光沐元素 (CORE_BAR_310) 6/6 嘲讽 亡语：恢复所有友方8hp。
    public sealed class Script_CORE_BAR_310 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_BAR_310", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.FriendMinions)
                    m.Health = Math.Min(m.MaxHealth, m.Health + 8);
                if (b.FriendHero != null)
                    b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + 8);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Heal);
        }
    }

// ── 火元素 (CORE_CS2_042) 6/5 ──
    // 战吼：造成4点伤害。（经典核心卡）
    public sealed class Script_CORE_CS2_042 : ICardEffectScript
    {
        public void Register(CardEffectDB db) =>
            CardEffectScriptRuntime.RegisterById(db, "CORE_CS2_042",
                new TriggerDef("Battlecry", "AnyCharacter",
                    new EffectDef("dmg", v: 4)));
    }

// 辟法巨龙 5/4 突袭圣盾扰魔
    public sealed class Script_CORE_DRG_079 : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "CORE_DRG_079"); } }

// ── 维拉努斯 (CORE_DRG_095) 7/6 ──
    // 战吼：将所有敌方随从的生命值变为1。
    public sealed class Script_CORE_DRG_095 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_DRG_095", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions)
                {
                    m.Health = 1;
                    m.MaxHealth = 1;
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 凯恩·血蹄 (CORE_EX1_110) 5/5 嘲讽 亡语：召5/5贝恩。
    public sealed class Script_CORE_EX1_110 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_EX1_110", true, out C id)) return;
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

// ── 考内留斯·罗姆 (CORE_SW_080) 4/5 每回合开始/结束各抽牌。(光环)
    public sealed class Script_CORE_SW_080 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_SW_080", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
                CardEffectDB.DrawCard(b, b.FriendDeckCards));
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Draw);
        }
    }

// ── 侏儒飞行员诺莉亚 (CORE_TOY_100) 4/2 突袭 亡语：对所有敌人造成2伤害。
    public sealed class Script_CORE_TOY_100 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_TOY_100", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 2);
                if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, 2);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 卡格瓦 (CORE_TRL_345) 4/6 战吼：将上回合用的所有法术牌移回手牌。
    public sealed class Script_CORE_TRL_345 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("CORE_TRL_345", true, out C id)) return;
            // 无法追踪上回合法术，近似为抽牌
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 领舞 (ETC_328) 4/2 亡语：从牌库召攻击力<本随从的随从。
    public sealed class Script_ETC_328 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_328", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                if (b.FriendMinions.Count >= 7 || b.FriendDeckCards.Count == 0) return;
                var card = b.FriendDeckCards[0];
                b.FriendDeckCards.RemoveAt(0);
                CardEffectDB.Summon(b, b.FriendMinions, card);
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 过气明星 (ETC_349) 5/5 亡语：随机召唤一个5费随从。
    public sealed class Script_ETC_349 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_349", true, out C id)) return;
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

// ── 穆克拉先生 (ETC_836) 10/10 突袭 ──
    // 战吼：用香蕉填满你对手的手牌。
    public sealed class Script_ETC_836 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("ETC_836", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                while (b.EnemyHand.Count < 10)
                    b.EnemyHand.Add(new SimEntity
                    {
                        Cost = 1, Type = Card.CType.SPELL, IsFriend = false
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 末日枭兽 (JAM_029) 3/4 战吼：夺取对手一个空法力水晶。
    public sealed class Script_JAM_029 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("JAM_029", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                b.MaxMana = Math.Min(10, b.MaxMana + 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Mana);
        }
    }

// ── 模玩泰拉图斯 (MIS_712) 7/7 嘲讽扰魔 战吼：10费时+7/+7。
    public sealed class Script_MIS_712 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("MIS_712", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null && b.MaxMana >= 10) H.Buff(s, 7, 7);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 髓骨使御者 (RLK_505 / CORE_RLK_505) 5/5 ──
    // 战吼：消耗最多5份残骸。每消耗一份残骸，随机对一个敌人造成2点伤害。
    public sealed class Script_RLK_505 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            foreach (var name in new[] { "RLK_505", "CORE_RLK_505" })
            {
                if (!Enum.TryParse(name, true, out C id)) continue;
                db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                {
                    int consume = Math.Min(5, b.FriendExcavateCount);
                    b.FriendExcavateCount -= consume;
                    for (int i = 0; i < consume; i++)
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
    }

// ── 侏儒嚼嚼怪 (RLK_720) 5/6 嘲讽吸血 回合结束：攻击最低血敌人。
    public sealed class Script_RLK_720 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("RLK_720", true, out C id)) return;
            db.Register(id, EffectTrigger.EndOfTurn, (b, s, t) =>
            {
                if (s == null) return;
                var targets = new List<SimEntity>();
                targets.AddRange(b.EnemyMinions.Where(m => m.Health > 0));
                if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                if (targets.Count == 0) return;
                var lowest = targets.OrderBy(e => e.Health).First();
                CardEffectDB.Dmg(b, lowest, s.Atk);
                // 吸血恢复
                if (b.FriendHero != null)
                    b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + s.Atk);
            });
            db.RegisterEffectKind(id, EffectTrigger.EndOfTurn, EffectKind.Damage | EffectKind.Heal);
        }
    }

// ── 黏土巢母 (TOY_380) 3/7 嘲讽 亡语：召4/4扰魔雏龙。
    public sealed class Script_TOY_380 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_380", true, out C id)) return;
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

// ── 沙德木刻 (TOY_501) 5/5 战吼：下一个战吼触发3次。
    public sealed class Script_TOY_501 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_501", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => { });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Utility);
        }
    }

// ── 发条演奏家 (TOY_509) 5/5 战吼：对所有敌方随从造成1伤害。
    public sealed class Script_TOY_509 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_509", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var m in b.EnemyMinions.ToArray())
                    CardEffectDB.Dmg(b, m, 1);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Damage);
        }
    }

// ── 测试假人 (TOY_606) 4/8 嘲讽。亡语：造成8伤害随机分敌方随从。
    public sealed class Script_TOY_606 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_606", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 8; i++)
                {
                    var alive = b.EnemyMinions.Where(m => m.Health > 0).ToList();
                    if (alive.Count == 0) break;
                    CardEffectDB.Dmg(b, alive[H.PickIndex(alive.Count, b, s)], 1);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Damage);
        }
    }

// ── 发条执行者 (TOY_880) 3/5 战吼：召唤本随从的1个复制。
    public sealed class Script_TOY_880 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_880", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || b.FriendMinions.Count >= 7) return;
                var copy = s.Clone(); copy.IsTired = true;
                b.FriendMinions.Add(copy);
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// ── 折纸巨龙 (TOY_896) 1/1 圣盾 吸血 ──
    // 战吼：与另一个随从交换属性值。
    public sealed class Script_TOY_896 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TOY_896", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null || t == null) return;
                int tmpAtk = s.Atk, tmpHp = s.Health, tmpMax = s.MaxHealth;
                s.Atk = t.Atk; s.Health = t.Health; s.MaxHealth = t.MaxHealth;
                t.Atk = tmpAtk; t.Health = tmpHp; t.MaxHealth = tmpMax;
            }, BattlecryTargetType.AnyMinion);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 复仇者阿格拉玛 (TTN_092) 3/7 泰坦 战吼：装备3/3武器。
    public sealed class Script_TTN_092 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_092", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
                H.EquipWeaponByStats(b, true, 0, 3, 3));
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 莱登 (TTN_481) 5/5 嘲讽 亡语：召唤用过的套牌外随从。
    public sealed class Script_TTN_481 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_481", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                for (int i = 0; i < 3 && b.FriendMinions.Count < 7; i++)
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = 3, Health = 3, MaxHealth = 3, IsFriend = true,
                        IsTired = true, Type = Card.CType.MINION
                    });
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Summon);
        }
    }

// ── 烈焰猛兽 (TTN_716) 4/5 战吼：获取两张磁力机械-2费。
    public sealed class Script_TTN_716 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_716", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) => b.FriendCardDraw += 2);
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Draw);
        }
    }

// ── 提尔 (TTN_857) 4/5 战吼：复活攻击力为2,3,4的随从各一个。
    public sealed class Script_TTN_857 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("TTN_857", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (int stat in new[] { 2, 3, 4 })
                {
                    if (b.FriendMinions.Count >= 7) break;
                    b.FriendMinions.Add(new SimEntity
                    {
                        Atk = stat, Health = stat, MaxHealth = stat,
                        IsFriend = true, IsTired = true, Type = Card.CType.MINION
                    });
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Summon);
        }
    }

// 饥饿食客哈姆 3/3 嘲讽 回合结束吃敌牌库随从+2/+2
    public sealed class Script_VAC_340_EoT : ICardEffectScript
    { public void Register(CardEffectDB db) { if (!Enum.TryParse("VAC_340",true,out C id)) return; db.Register(id, EffectTrigger.EndOfTurn, (b,s,t)=> { if(s!=null) H.Buff(s,2,2); }); } }

// 恐惧的逃亡者 6/6 不在套牌→冲锋
    public sealed class Script_VAC_447_Extra : ICardEffectScript
    { public void Register(CardEffectDB db) { R.RegisterById(db, "VAC_447"); } }

// ── 武器寄存员 (VAC_924) 6/4 战吼：控制海盗→从牌库装备武器。
    public sealed class Script_VAC_924 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("VAC_924", true, out C id)) return;
            db.Register(id, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (b.FriendMinions.Count(m => H.IsPirateMinion(m.CardId)) > 0)
                {
                    var wid = H.PickWeaponFromFriendlyDeckOrPool(b, s);
                    if (wid != 0) H.EquipWeaponFromCard(b, true, wid);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Battlecry, EffectKind.Buff);
        }
    }

// ── 行程保安 (WORK_010) 2/2 嘲讽 亡语：召8费随从。
    public sealed class Script_WORK_010 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_010", true, out C id)) return;
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

// ── 精魂商贩 (WORK_015) 6/6 突袭 ──
    // 亡语：随机使你手牌中的一张随从牌的法力值消耗减少（6）点。
    public sealed class Script_WORK_015 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            if (!Enum.TryParse("WORK_015", true, out C id)) return;
            db.Register(id, EffectTrigger.Deathrattle, (b, s, t) =>
            {
                var mins = b.Hand.Where(c => H.IsMinionCard(c.CardId)).ToList();
                if (mins.Count > 0)
                {
                    var pick = mins[H.PickIndex(mins.Count, b, s)];
                    pick.Cost = Math.Max(0, pick.Cost - 6);
                }
            });
            db.RegisterEffectKind(id, EffectTrigger.Deathrattle, EffectKind.Mana);
        }
    }

}
