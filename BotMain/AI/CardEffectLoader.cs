using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI
{
    /// <summary>
    /// 从 effects_data.json 加载卡牌效果到 CardEffectDB
    /// 每个 JSON 类别对应一个固定的效果模板，新卡只需编辑 JSON 无需改 C#
    /// </summary>
    public static class CardEffectLoader
    {
        // 枚举名缓存，避免每次 Enum.TryParse
        private static readonly Dictionary<string, C> _cardIdMap = BuildCardIdMap();

        public static void LoadFromJson(CardEffectDB db, string jsonPath)
        {
            if (!File.Exists(jsonPath)) return;

            var json = JObject.Parse(File.ReadAllText(jsonPath));

            foreach (var prop in json.Properties())
            {
                var category = prop.Name;
                var entries = prop.Value as JArray;
                if (entries == null || entries.Count == 0) continue;

                if (!TryGetHandler(category, out var trigger, out var handler, out var effectTargetType)) continue;

                foreach (var entry in entries)
                {
                    string id;
                    int v1 = 0, v2 = 0;
                    bool hasSP = false;

                    if (entry is JArray arr)
                    {
                        id = arr[0]?.ToString();
                        if (arr.Count > 1) v1 = (int)arr[1];
                        if (arr.Count > 2)
                        {
                            // 第三个参数：bool 表示 spellPower，int 表示第二个数值
                            var third = arr[2];
                            if (third.Type == JTokenType.Boolean)
                                hasSP = (bool)third;
                            else
                                v2 = (int)third;
                        }
                    }
                    else
                    {
                        id = entry.ToString();
                    }

                    if (string.IsNullOrEmpty(id)) continue;
                    if (!_cardIdMap.TryGetValue(id, out var cardId)) continue;

                    var fn = handler(v1, v2, hasSP);
                    if (fn != null)
                    {
                        db.Register(cardId, trigger, fn);
                        if (effectTargetType != BattlecryTargetType.None)
                            db.RegisterTargetType(cardId, trigger, effectTargetType);
                    }
                }
            }
        }

        private static Dictionary<string, C> BuildCardIdMap()
        {
            var map = new Dictionary<string, C>(StringComparer.Ordinal);
            foreach (C val in Enum.GetValues(typeof(C)))
            {
                var name = val.ToString();
                if (!map.ContainsKey(name))
                    map[name] = val;
            }
            return map;
        }

        // 效果模板工厂：返回 (trigger, handler)
        // handler 接收 (v1, v2, hasSP) 返回效果 lambda
        private delegate Action<SimBoard, SimEntity, SimEntity> EffectFactory(int v1, int v2, bool hasSP);

        private static bool TryGetHandler(string category, out EffectTrigger trigger, out EffectFactory handler, out BattlecryTargetType effectTargetType)
        {
            trigger = EffectTrigger.Spell;
            handler = null;
            effectTargetType = BattlecryTargetType.None;

            // 解析前缀确定触发器
            if (category.StartsWith("spell_", StringComparison.Ordinal))
                trigger = EffectTrigger.Spell;
            else if (category.StartsWith("bc_", StringComparison.Ordinal) || category.StartsWith("combo_", StringComparison.Ordinal))
                trigger = EffectTrigger.Battlecry;
            else if (category.StartsWith("dr_", StringComparison.Ordinal))
                trigger = EffectTrigger.Deathrattle;
            else if (category.StartsWith("eot_", StringComparison.Ordinal))
                trigger = EffectTrigger.EndOfTurn;
            else
                return false;

            // 解析后缀确定效果类型
            var suffix = GetSuffix(category);
            handler = GetFactory(suffix, trigger);

            // 对战吼和法术类别，确定目标类型
            if (trigger == EffectTrigger.Battlecry || trigger == EffectTrigger.Spell)
                effectTargetType = GetEffectTargetType(suffix);

            return handler != null;
        }

        /// <summary>
        /// 根据效果后缀判断需要什么类型的目标
        /// </summary>
        private static BattlecryTargetType GetEffectTargetType(string suffix)
        {
            switch (suffix)
            {
                // ── 需要目标的伤害类 → 敌方（随从+英雄） ──
                case "dmg":          return BattlecryTargetType.AnyCharacter;

                // ── 仅打敌方英雄（无需选择目标） ──
                case "dmg_enemy_hero": return BattlecryTargetType.None;
                case "face":         return BattlecryTargetType.None;

                // ── AOE 无需选择目标 ──
                case "aoe_all":      return BattlecryTargetType.None;
                case "aoe_enemies_heroes": return BattlecryTargetType.None;
                case "aoe_enemy":    return BattlecryTargetType.None;
                case "random_enemy": return BattlecryTargetType.None;
                case "destroy_all":  return BattlecryTargetType.None;
                case "freeze_all":   return BattlecryTargetType.None;

                // ── 定向消灭 → 敌方随从 ──
                case "destroy":      return BattlecryTargetType.AnyMinion;
                case "destroy_weapon": return BattlecryTargetType.None;

                // ── 沉默 → 任意随从 ──
                case "silence":      return BattlecryTargetType.AnyMinion;

                // ── 冰冻 → 敌方（随从+英雄） ──
                case "freeze":       return BattlecryTargetType.EnemyOnly;

                // ── 治疗 → 任意角色 ──
                case "heal":         return BattlecryTargetType.AnyCharacter;
                case "heal_hero":    return BattlecryTargetType.None;
                case "heal_all":     return BattlecryTargetType.None;
                case "restore_all":  return BattlecryTargetType.None;

                // ── Buff 目标随从 → 友方随从 ──
                case "buff":         return BattlecryTargetType.FriendlyMinion;
                case "atk_buff":     return BattlecryTargetType.FriendlyMinion;
                case "atk":          return BattlecryTargetType.FriendlyMinion;
                case "give_hp":      return BattlecryTargetType.FriendlyMinion;
                case "set_hp":       return BattlecryTargetType.AnyMinion;

                // ── Buff 自身/全场 → 无需目标 ──
                case "gain_buff":    return BattlecryTargetType.None;
                case "buff_all":     return BattlecryTargetType.None;
                case "buff_hand":    return BattlecryTargetType.None;

                // ── 给予关键字 → 友方随从 ──
                case "give_taunt":   return BattlecryTargetType.FriendlyMinion;
                case "give_ds":      return BattlecryTargetType.FriendlyMinion;
                case "give_windfury": return BattlecryTargetType.FriendlyMinion;
                case "give_lifesteal": return BattlecryTargetType.FriendlyMinion;
                case "give_rush":    return BattlecryTargetType.FriendlyMinion;

                // ── 回手 → 任意随从 ──
                case "bounce":       return BattlecryTargetType.AnyMinion;

                // ── 无需目标的效果 ──
                case "draw":         return BattlecryTargetType.None;
                case "armor":        return BattlecryTargetType.None;
                case "summon":       return BattlecryTargetType.None;
                case "equip":        return BattlecryTargetType.None;
                case "add_hand":     return BattlecryTargetType.None;
                case "set_hero_hp":  return BattlecryTargetType.None;

                default:             return BattlecryTargetType.None;
            }
        }

        private static string GetSuffix(string category)
        {
            // spell_dmg -> dmg, bc_aoe_all -> aoe_all, dr_dmg_enemy_hero -> dmg_enemy_hero
            var prefixes = new[] { "spell_", "bc_", "dr_", "eot_", "combo_" };
            foreach (var p in prefixes)
                if (category.StartsWith(p, StringComparison.Ordinal))
                    return category.Substring(p.Length);
            return category;
        }

        private static EffectFactory GetFactory(string suffix, EffectTrigger trigger)
        {
            var isDR = trigger == EffectTrigger.Deathrattle;
            var isEOT = trigger == EffectTrigger.EndOfTurn;

            switch (suffix)
            {
                // === 伤害类 ===
                case "dmg":
                    if (isDR) // dr_dmg 不存在，但 dr_dmg_random 和 dr_dmg_enemy_hero 有
                        return null;
                    return (v1, v2, sp) => MkDmgTarget(v1, sp);

                case "dmg_enemy_hero":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var hero = s.IsFriend ? b.EnemyHero : b.FriendHero;
                            if (hero != null) CardEffectDB.Dmg(b, hero, v1);
                        };
                    return null;

                case "aoe_all":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        int d = sp ? v1 + CardEffectDB.SP(b) : v1;
                        foreach (var m in b.FriendMinions.ToArray()) CardEffectDB.Dmg(b, m, d);
                        foreach (var m in b.EnemyMinions.ToArray()) CardEffectDB.Dmg(b, m, d);
                    };

                case "aoe_enemies_heroes":
                case "aoe_enemy":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        int d = sp ? v1 + CardEffectDB.SP(b) : v1;
                        if (isEOT || isDR)
                        {
                            var enemies = s.IsFriend ? b.EnemyMinions : b.FriendMinions;
                            var hero = s.IsFriend ? b.EnemyHero : b.FriendHero;
                            foreach (var m in enemies.ToArray()) CardEffectDB.Dmg(b, m, d);
                            if (hero != null) CardEffectDB.Dmg(b, hero, d);
                        }
                        else
                        {
                            foreach (var m in b.EnemyMinions.ToArray()) CardEffectDB.Dmg(b, m, d);
                            if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, d);
                        }
                    };

                case "face":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        int d = sp ? v1 + CardEffectDB.SP(b) : v1;
                        if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, d);
                    };

                case "random_enemy":
                case "dmg_random":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        int d = sp ? v1 + CardEffectDB.SP(b) : v1;
                        SimEntity target;
                        if (isEOT || isDR)
                        {
                            var enemies = s.IsFriend ? b.EnemyMinions : b.FriendMinions;
                            var hero = s.IsFriend ? b.EnemyHero : b.FriendHero;
                            var ts = new List<SimEntity>(enemies);
                            if (hero != null) ts.Add(hero);
                            target = ts.Count > 0 ? ts[0] : null;
                        }
                        else
                        {
                            var ts = new List<SimEntity>(b.EnemyMinions);
                            if (b.EnemyHero != null) ts.Add(b.EnemyHero);
                            target = ts.Count > 0 ? ts[0] : null;
                        }
                        if (target != null) CardEffectDB.Dmg(b, target, d);
                    };

                // === 冻结类 ===
                case "freeze":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var enemies = s.IsFriend ? b.EnemyMinions : b.FriendMinions;
                            if (enemies.Count > 0) enemies[0].IsFrozen = true;
                        };
                    return (v1, v2, sp) => (b, s, t) => { if (t != null) t.IsFrozen = true; };

                case "freeze_all":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        foreach (var m in b.EnemyMinions) m.IsFrozen = true;
                        foreach (var m in b.FriendMinions) m.IsFrozen = true;
                    };

                // === 消灭类 ===
                case "destroy":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var enemies = s.IsFriend ? b.EnemyMinions : b.FriendMinions;
                            if (enemies.Count > 0) enemies[0].Health = 0;
                        };
                    return (v1, v2, sp) => (b, s, t) => { if (t != null) t.Health = 0; };

                case "destroy_all":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        foreach (var m in b.FriendMinions.ToArray()) m.Health = 0;
                        foreach (var m in b.EnemyMinions.ToArray()) m.Health = 0;
                    };

                case "destroy_weapon":
                    return (v1, v2, sp) => (b, s, t) => { b.EnemyWeapon = null; };

                // === 沉默 ===
                case "silence":
                    return (v1, v2, sp) => (b, s, t) => { CardEffectDB.DoSilence(t); };

                // === 治疗类 ===
                case "heal":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) t.Health = Math.Min(t.MaxHealth, t.Health + v1);
                    };

                case "heal_hero":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (b.FriendHero != null)
                            b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + v1);
                    };

                case "restore":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var hero = s.IsFriend ? b.FriendHero : b.EnemyHero;
                            if (hero != null) hero.Health = Math.Min(hero.MaxHealth, hero.Health + v1);
                        };
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (b.FriendHero != null)
                            b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + v1);
                    };

                case "restore_all":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        foreach (var m in b.FriendMinions)
                            m.Health = Math.Min(m.MaxHealth, m.Health + v1);
                        if (b.FriendHero != null)
                            b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + v1);
                    };

                // === 抽牌类 ===
                case "draw":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            if (s.IsFriend) b.FriendCardDraw += v1;
                        };
                    if (isEOT)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            if (s.IsFriend) b.FriendCardDraw += v1;
                        };
                    return (v1, v2, sp) => (b, s, t) => { b.FriendCardDraw += v1; };

                case "add_hand":
                    // 加牌到手模拟为抽牌
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            if (s.IsFriend) b.FriendCardDraw += v1;
                        };
                    return (v1, v2, sp) => (b, s, t) => { b.FriendCardDraw += v1; };

                // === 护甲类 ===
                case "armor":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var hero = s.IsFriend ? b.FriendHero : b.EnemyHero;
                            if (hero != null) hero.Armor += v1;
                        };
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (b.FriendHero != null) b.FriendHero.Armor += v1;
                    };

                // === 召唤类 ===
                case "summon":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var list = s.IsFriend ? b.FriendMinions : b.EnemyMinions;
                            if (list.Count < 7)
                                list.Add(new SimEntity { Atk = v1, Health = v2, MaxHealth = v2, IsFriend = s.IsFriend, IsTired = true });
                        };
                    if (isEOT)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var list = s.IsFriend ? b.FriendMinions : b.EnemyMinions;
                            if (list.Count < 7)
                                list.Add(new SimEntity { Atk = v1, Health = v2, MaxHealth = v2, IsFriend = s.IsFriend, IsTired = true });
                        };
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (b.FriendMinions.Count < 7)
                            b.FriendMinions.Add(new SimEntity { Atk = v1, Health = v2, MaxHealth = v2, IsFriend = true, IsTired = true });
                    };

                // === Buff 类 ===
                case "buff":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            var list = s.IsFriend ? b.FriendMinions : b.EnemyMinions;
                            if (list.Count > 0) { list[0].Atk += v1; list[0].Health += v2; list[0].MaxHealth += v2; }
                        };
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) { t.Atk += v1; t.Health += v2; t.MaxHealth += v2; }
                    };

                case "buff_all":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        foreach (var m in b.FriendMinions) { m.Atk += v1; m.Health += v2; m.MaxHealth += v2; }
                    };

                case "buff_friendly":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        var list = (isEOT || isDR) && !s.IsFriend ? b.EnemyMinions : b.FriendMinions;
                        foreach (var m in list) { m.Atk += v1; m.Health += v2; m.MaxHealth += v2; }
                    };

                case "buff_hand":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (isDR && s.IsFriend || !isDR)
                            foreach (var c in b.Hand) { c.Atk += v1; c.Health += v2; c.MaxHealth += v2; }
                    };

                case "gain_buff":
                    // 战吼：自身获得 buff
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        s.Atk += v1; s.Health += v2; s.MaxHealth += v2;
                    };

                case "self_buff":
                    // 回合结束自身 buff
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        s.Atk += v1; s.Health += v2; s.MaxHealth += v2;
                    };

                case "atk_buff":
                case "atk":
                case "buff_atk":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) t.Atk += v1;
                        else s.Atk += v1;
                    };

                case "give_hp":
                case "buff_hp":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) { t.Health += v1; t.MaxHealth += v1; }
                    };

                case "set_hp":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) { t.Health = v1; t.MaxHealth = v1; }
                    };

                case "set_hero_hp":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (b.FriendHero != null) { b.FriendHero.Health = v1; b.FriendHero.MaxHealth = v1; }
                    };

                // === 关键词类 ===
                case "give_taunt":
                    return (v1, v2, sp) => (b, s, t) => { if (t != null) t.IsTaunt = true; };
                case "give_ds":
                    return (v1, v2, sp) => (b, s, t) => { if (t != null) t.IsDivineShield = true; };
                case "give_windfury":
                    return (v1, v2, sp) => (b, s, t) => { if (t != null) t.IsWindfury = true; };
                case "give_lifesteal":
                    return (v1, v2, sp) => (b, s, t) => { if (t != null) t.IsLifeSteal = true; };
                case "give_rush":
                    return (v1, v2, sp) => (b, s, t) => { if (t != null) t.HasRush = true; };

                // === 武器类 ===
                case "equip":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            if (s.IsFriend)
                            {
                                b.FriendWeapon = new SimEntity { Atk = v1, Health = v2, MaxHealth = v2, IsFriend = true };
                                if (b.FriendHero != null) b.FriendHero.Atk = v1;
                            }
                        };
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        b.FriendWeapon = new SimEntity { Atk = v1, Health = v2, MaxHealth = v2, IsFriend = true };
                        if (b.FriendHero != null) b.FriendHero.Atk = v1;
                    };

                // === 回手类 ===
                case "bounce":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null)
                        {
                            b.FriendMinions.Remove(t);
                            b.EnemyMinions.Remove(t);
                            if (t.IsFriend && b.Hand.Count < 10) b.Hand.Add(t);
                        }
                    };

                // === 变形类：替换目标为指定身材随从 ===
                case "polymorph":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null)
                        {
                            int a = v1 > 0 ? v1 : 1, h = v2 > 0 ? v2 : 1;
                            bool wasFriend = t.IsFriend;
                            var list = wasFriend ? b.FriendMinions : b.EnemyMinions;
                            int idx = list.IndexOf(t);
                            if (idx >= 0)
                                list[idx] = new SimEntity { Atk = a, Health = h, MaxHealth = h, IsFriend = wasFriend, IsTired = true };
                        }
                    };

                // === 复制到手牌 ===
                case "copy_hand":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            if (s.IsFriend) b.FriendCardDraw += Math.Max(1, v1);
                        };
                    return (v1, v2, sp) => (b, s, t) => { b.FriendCardDraw += Math.Max(1, v1); };

                // === 获得法力水晶 ===
                case "mana_crystal":
                    return (v1, v2, sp) => (b, s, t) => { b.FriendCardDraw += Math.Max(1, v1); };

                // === 减费：近似为抽 1 张 ===
                case "reduce_cost":
                    return (v1, v2, sp) => (b, s, t) => { b.FriendCardDraw += 1; };

                // === 奥秘：按费用近似伤害 ===
                case "secret":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        int d = Math.Max(1, v1);
                        var targets = new List<SimEntity>(b.EnemyMinions);
                        if (b.EnemyHero != null) targets.Add(b.EnemyHero);
                        if (targets.Count > 0) CardEffectDB.Dmg(b, targets[0], d);
                    };

                // === 随机消灭一个敌方随从 ===
                case "random_destroy":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (isEOT || isDR)
                        {
                            var enemies = s.IsFriend ? b.EnemyMinions : b.FriendMinions;
                            if (enemies.Count > 0) enemies[0].Health = 0;
                        }
                        else
                        {
                            if (b.EnemyMinions.Count > 0) b.EnemyMinions[0].Health = 0;
                        }
                    };

                // === 自身变形：设置自身攻/血 ===
                case "transform_self":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (v1 > 0) s.Atk = v1;
                        if (v2 > 0) { s.Health = v2; s.MaxHealth = v2; }
                    };

                // === 偷取控制权 ===
                case "steal":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null && !t.IsFriend)
                        {
                            b.EnemyMinions.Remove(t);
                            if (b.FriendMinions.Count < 7)
                            {
                                t.IsFriend = true;
                                t.IsTired = true;
                                b.FriendMinions.Add(t);
                            }
                        }
                    };

                // === 洗入牌库 ===
                case "shuffle":
                    if (isDR)
                        return (v1, v2, sp) => (b, s, t) =>
                        {
                            if (s.IsFriend) b.FriendCardDraw += Math.Max(1, v1);
                        };
                    return (v1, v2, sp) => (b, s, t) => { b.FriendCardDraw += Math.Max(1, v1); };

                // === 弃牌 ===
                case "discard":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        int n = Math.Max(1, v1);
                        for (int i = 0; i < n && b.Hand.Count > 0; i++)
                            b.Hand.RemoveAt(b.Hand.Count - 1);
                    };

                // === 对所有友方随从伤害 ===
                case "aoe_friendly":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        int d = sp ? v1 + CardEffectDB.SP(b) : v1;
                        if (isEOT || isDR)
                        {
                            var friends = s.IsFriend ? b.FriendMinions : b.EnemyMinions;
                            foreach (var m in friends.ToArray()) CardEffectDB.Dmg(b, m, d);
                        }
                        else
                        {
                            foreach (var m in b.FriendMinions.ToArray()) CardEffectDB.Dmg(b, m, d);
                        }
                    };

                // === 对自身英雄伤害 ===
                case "dmg_self":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (isEOT || isDR)
                        {
                            var hero = s.IsFriend ? b.FriendHero : b.EnemyHero;
                            if (hero != null) CardEffectDB.Dmg(b, hero, v1);
                        }
                        else
                        {
                            if (b.FriendHero != null) CardEffectDB.Dmg(b, b.FriendHero, v1);
                        }
                    };

                // === 给予免疫 ===
                case "give_immune":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) t.IsImmune = true;
                        else s.IsImmune = true;
                    };

                // === 给予潜行 ===
                case "give_stealth":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) t.IsStealth = true;
                        else s.IsStealth = true;
                    };

                // === 给予剧毒 ===
                case "give_poison":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) t.HasPoison = true;
                        else s.HasPoison = true;
                    };

                // === 给予复生 ===
                case "give_reborn":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (t != null) t.HasReborn = true;
                        else s.HasReborn = true;
                    };

                // === 英雄获得攻击力 ===
                case "hero_atk":
                    return (v1, v2, sp) => (b, s, t) =>
                    {
                        if (isEOT || isDR)
                        {
                            var hero = s.IsFriend ? b.FriendHero : b.EnemyHero;
                            if (hero != null) hero.Atk += v1;
                        }
                        else
                        {
                            if (b.FriendHero != null) b.FriendHero.Atk += v1;
                        }
                    };

                // === 无法模拟的效果：空操作占位 ===
                case "noop":
                    return (v1, v2, sp) => (b, s, t) => { };

                default:
                    return null;
            }
        }

        private static Action<SimBoard, SimEntity, SimEntity> MkDmgTarget(int dmg, bool useSP)
        {
            if (useSP)
                return (b, s, t) => { if (t != null) CardEffectDB.Dmg(b, t, dmg + CardEffectDB.SP(b)); };
            return (b, s, t) => { if (t != null) CardEffectDB.Dmg(b, t, dmg); };
        }
    }
}
