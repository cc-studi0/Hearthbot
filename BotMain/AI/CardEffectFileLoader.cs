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
    /// 从 CardEffects/ 目录加载单卡效果文件（每张卡一个 JSON）。
    ///
    /// 文件名即卡牌 ID，如 CORE_REV_290.json。
    ///
    /// 格式：
    /// {
    ///   "Battlecry": {
    ///     "targetType": "FriendlyMinion",
    ///     "effects": [
    ///       { "type": "buff", "atk": 2, "hp": 1 },
    ///       { "type": "draw", "n": 1 }
    ///     ]
    ///   },
    ///   "Spell": { ... },
    ///   "Deathrattle": { ... },
    ///   "EndOfTurn": { ... },
    ///   "LocationActivation": { ... }
    /// }
    ///
    /// 支持的 effect type：
    ///   dmg          - 对目标造成伤害   { "type":"dmg", "v":3, "useSP":true }
    ///   dmg_all      - AOE 伤害全体     { "type":"dmg_all", "v":2 }
    ///   dmg_enemy    - AOE 伤害敌方     { "type":"dmg_enemy", "v":2 }
    ///   heal         - 治疗目标         { "type":"heal", "v":4 }
    ///   heal_hero    - 治疗己方英雄     { "type":"heal_hero", "v":4 }
    ///   buff         - +攻/+血          { "type":"buff", "atk":2, "hp":1 }
    ///   buff_all     - 全场友方 +攻/+血 { "type":"buff_all", "atk":1, "hp":1 }
    ///   draw         - 抽牌             { "type":"draw", "n":1 }
    ///   armor        - 获得护甲         { "type":"armor", "v":4 }
    ///   summon       - 召唤随从         { "type":"summon", "atk":2, "hp":2 }
    ///   destroy      - 消灭目标         { "type":"destroy" }
    ///   silence      - 沉默目标         { "type":"silence" }
    ///   freeze       - 冻结目标         { "type":"freeze" }
    ///   bounce       - 回手目标         { "type":"bounce" }
    ///   give_taunt   - 给予嘲讽         { "type":"give_taunt" }
    ///   give_ds      - 给予圣盾         { "type":"give_ds" }
    ///   give_windfury- 给予风怒         { "type":"give_windfury" }
    ///   give_rush    - 给予突袭         { "type":"give_rush" }
    ///   give_lifesteal - 给予溅射       { "type":"give_lifesteal" }
    ///   give_poison  - 给予剧毒         { "type":"give_poison" }
    ///   hero_atk     - 英雄获得攻击力   { "type":"hero_atk", "v":2 }
    ///   equip        - 装备武器         { "type":"equip", "atk":2, "dur":2 }
    /// </summary>
    public static class CardEffectFileLoader
    {
        private static readonly Dictionary<string, C> _cardIdMap = BuildCardIdMap();

        private static Dictionary<string, C> BuildCardIdMap()
        {
            var map = new Dictionary<string, C>(StringComparer.OrdinalIgnoreCase);
            foreach (C val in Enum.GetValues(typeof(C)))
            {
                var name = val.ToString();
                if (!map.ContainsKey(name)) map[name] = val;
            }
            return map;
        }

        /// <summary>
        /// 递归扫描 cardEffectsDir 目录及子目录下所有 *.json 文件，加载到 db。
        /// 支持按卡牌类型分子目录存放（如 随从/、法术/、地标/ 等）。
        /// </summary>
        public static int LoadFromDirectory(CardEffectDB db, string cardEffectsDir)
        {
            if (!Directory.Exists(cardEffectsDir)) return 0;

            int loaded = 0;
            foreach (var file in Directory.GetFiles(cardEffectsDir, "*.json", SearchOption.AllDirectories))
            {
                var cardName = Path.GetFileNameWithoutExtension(file);
                if (!_cardIdMap.TryGetValue(cardName, out var cardId)) continue;

                try
                {
                    var root = JObject.Parse(File.ReadAllText(file));
                    if (LoadCardFile(db, cardId, root))
                        loaded++;
                }
                catch (Exception ex)
                {
                    // 解析失败只跳过这张卡，不中断其他加载
                    System.Diagnostics.Debug.WriteLine($"[CardEffectFileLoader] 解析 {file} 失败: {ex.Message}");
                }
            }
            return loaded;
        }

        private static bool LoadCardFile(CardEffectDB db, C cardId, JObject root)
        {
            bool any = false;
            foreach (var prop in root.Properties())
            {
                if (!TryParseTrigger(prop.Name, out var trigger)) continue;
                var block = prop.Value as JObject;
                if (block == null) continue;

                // 解析目标类型
                var targetType = BattlecryTargetType.None;
                if (block["targetType"] is JToken ttToken)
                {
                    if (!TryParseTargetType(ttToken.ToString(), out targetType))
                        System.Diagnostics.Debug.WriteLine($"[CardEffectFileLoader] 警告: {cardId} targetType \"{ttToken}\" 无效，有效值: None/AnyCharacter/AnyMinion/EnemyOnly/EnemyMinion/FriendlyOnly/FriendlyMinion");
                }

                // 构建复合效果 lambda
                var effectList = block["effects"] as JArray;
                if (effectList == null || effectList.Count == 0) continue;

                var fns = new List<Action<SimBoard, SimEntity, SimEntity>>();
                foreach (var e in effectList)
                {
                    var efObj = e as JObject;
                    if (efObj == null) continue;
                    var fn = ParseEffect(efObj);
                    if (fn != null) fns.Add(fn);
                }

                if (fns.Count == 0) continue;

                // 合并为单个 lambda
                Action<SimBoard, SimEntity, SimEntity> combined = (b, s, t) =>
                {
                    foreach (var f in fns) f(b, s, t);
                };

                db.Register(cardId, trigger, combined, targetType);
                any = true;
            }
            return any;
        }

        // ── 解析单个效果对象 ──────────────────────────────────────
        private static Action<SimBoard, SimEntity, SimEntity> ParseEffect(JObject e)
        {
            var type = e["type"]?.ToString() ?? "";
            int v    = e["v"]?.ToObject<int>()  ?? 0;
            int atk  = e["atk"]?.ToObject<int>()  ?? 0;
            int hp   = e["hp"]?.ToObject<int>()   ?? 0;
            int n    = e["n"]?.ToObject<int>()    ?? 1;
            int dur  = e["dur"]?.ToObject<int>()  ?? 0;
            bool sp  = e["useSP"]?.ToObject<bool>() ?? false;

            switch (type)
            {
                case "dmg":
                    return (b, s, t) =>
                    {
                        if (t != null)
                        {
                            int d = sp ? v + CardEffectDB.SP(b) : v;
                            CardEffectDB.Dmg(b, t, d);
                        }
                    };

                case "dmg_all":
                    return (b, s, t) =>
                    {
                        int d = sp ? v + CardEffectDB.SP(b) : v;
                        foreach (var m in b.FriendMinions.ToArray()) CardEffectDB.Dmg(b, m, d);
                        foreach (var m in b.EnemyMinions.ToArray())  CardEffectDB.Dmg(b, m, d);
                        if (b.FriendHero != null) CardEffectDB.Dmg(b, b.FriendHero, d);
                        if (b.EnemyHero  != null) CardEffectDB.Dmg(b, b.EnemyHero,  d);
                    };

                case "dmg_enemy":
                    return (b, s, t) =>
                    {
                        int d = sp ? v + CardEffectDB.SP(b) : v;
                        var enemies = s != null && !s.IsFriend ? b.FriendMinions : b.EnemyMinions;
                        var hero    = s != null && !s.IsFriend ? b.FriendHero    : b.EnemyHero;
                        foreach (var m in enemies.ToArray()) CardEffectDB.Dmg(b, m, d);
                        if (hero != null) CardEffectDB.Dmg(b, hero, d);
                    };

                case "heal":
                    return (b, s, t) =>
                    {
                        if (t != null) t.Health = Math.Min(t.MaxHealth, t.Health + v);
                    };

                case "heal_hero":
                    return (b, s, t) =>
                    {
                        if (b.FriendHero != null)
                            b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + v);
                    };

                case "buff":
                    return (b, s, t) =>
                    {
                        if (t != null) { t.Atk += atk; t.Health += hp; t.MaxHealth += hp; }
                    };

                case "buff_all":
                    return (b, s, t) =>
                    {
                        foreach (var m in b.FriendMinions)
                        { m.Atk += atk; m.Health += hp; m.MaxHealth += hp; }
                    };

                case "buff_self":
                    return (b, s, t) =>
                    {
                        if (s != null) { s.Atk += atk; s.Health += hp; s.MaxHealth += hp; }
                    };

                case "draw":
                    return (b, s, t) => { b.FriendCardDraw += n; };

                case "armor":
                    return (b, s, t) =>
                    {
                        if (b.FriendHero != null) b.FriendHero.Armor += v;
                    };

                case "summon":
                    return (b, s, t) =>
                    {
                        if (b.FriendMinions.Count < 7)
                            b.FriendMinions.Add(new SimEntity
                            {
                                Atk = atk, Health = hp, MaxHealth = hp,
                                IsFriend = true, IsTired = true
                            });
                    };

                case "destroy":
                    return (b, s, t) =>
                    {
                        if (t != null) t.Health = 0;
                    };

                case "silence":
                    return (b, s, t) => { CardEffectDB.DoSilence(t); };

                case "freeze":
                    return (b, s, t) =>
                    {
                        if (t != null) t.IsFrozen = true;
                    };

                case "bounce":
                    return (b, s, t) =>
                    {
                        if (t != null)
                        {
                            b.FriendMinions.Remove(t);
                            b.EnemyMinions.Remove(t);
                            if (t.IsFriend && b.Hand.Count < 10) b.Hand.Add(t);
                        }
                    };

                case "give_taunt":    return (b, s, t) => { if (t != null) t.IsTaunt        = true; };
                case "give_ds":       return (b, s, t) => { if (t != null) t.IsDivineShield  = true; };
                case "give_windfury": return (b, s, t) => { if (t != null) t.IsWindfury      = true; };
                case "give_rush":     return (b, s, t) => { if (t != null) t.HasRush         = true; };
                case "give_lifesteal":return (b, s, t) => { if (t != null) t.IsLifeSteal     = true; };
                case "give_poison":   return (b, s, t) => { if (t != null) t.HasPoison       = true; };
                case "give_reborn":   return (b, s, t) => { if (t != null) t.HasReborn       = true; };
                case "give_immune":   return (b, s, t) => { if (b.FriendHero != null) b.FriendHero.IsImmune = true; };

                case "dmg_face":
                    return (b, s, t) =>
                    {
                        int d = sp ? v + CardEffectDB.SP(b) : v;
                        if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, d);
                    };

                case "dmg_random":
                    return (b, s, t) =>
                    {
                        int d = sp ? v + CardEffectDB.SP(b) : v;
                        var ts = new List<SimEntity>(b.EnemyMinions);
                        if (b.EnemyHero != null) ts.Add(b.EnemyHero);
                        if (ts.Count > 0) CardEffectDB.Dmg(b, ts[0], d);
                    };

                case "destroy_all":
                    return (b, s, t) =>
                    {
                        foreach (var m in b.FriendMinions.ToArray()) m.Health = 0;
                        foreach (var m in b.EnemyMinions.ToArray()) m.Health = 0;
                    };

                case "destroy_weapon":
                    return (b, s, t) => { b.EnemyWeapon = null; };

                case "freeze_all":
                    return (b, s, t) =>
                    {
                        foreach (var m in b.EnemyMinions) m.IsFrozen = true;
                        if (b.EnemyHero != null) b.EnemyHero.IsFrozen = true;
                    };

                case "heal_all":
                    return (b, s, t) =>
                    {
                        foreach (var m in b.FriendMinions)
                            m.Health = Math.Min(m.MaxHealth, m.Health + v);
                        if (b.FriendHero != null)
                            b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + v);
                    };

                case "set_hp":
                    return (b, s, t) =>
                    {
                        if (t != null) { t.Health = v; t.MaxHealth = v; }
                    };

                case "clear_board":
                    return (b, s, t) =>
                    {
                        b.FriendMinions.Clear();
                        b.EnemyMinions.Clear();
                    };

                case "noop":
                    return (b, s, t) => { };

                case "add_mana":
                    return (b, s, t) => { b.Mana += v; };

                case "hero_atk":
                    return (b, s, t) =>
                    {
                        if (b.FriendHero != null) b.FriendHero.Atk += v;
                    };

                case "equip":
                    return (b, s, t) =>
                    {
                        b.FriendWeapon = new SimEntity
                        {
                            Atk = atk, Health = dur > 0 ? dur : hp,
                            MaxHealth = dur > 0 ? dur : hp, IsFriend = true
                        };
                        if (b.FriendHero != null) b.FriendHero.Atk = atk;
                    };

                default:
                    return null;
            }
        }

        // ── 辅助解析 ─────────────────────────────────────────────
        private static bool TryParseTrigger(string s, out EffectTrigger trigger)
        {
            return Enum.TryParse(s, true, out trigger);
        }

        private static bool TryParseTargetType(string s, out BattlecryTargetType tt)
        {
            return Enum.TryParse(s, true, out tt);
        }
    }
}
