using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI
{
    public static class CardEffectScriptRuntime
    {
        private static readonly Dictionary<string, C> CardIdMap = BuildCardIdMap();

        public static void RegisterById(CardEffectDB db, string cardIdText, params TriggerDef[] triggers)
        {
            if (db == null || string.IsNullOrWhiteSpace(cardIdText) || triggers == null || triggers.Length == 0) return;
            if (!CardIdMap.TryGetValue(cardIdText, out var cardId)) return;

            foreach (var triggerDef in triggers)
            {
                if (triggerDef == null) continue;
                if (!Enum.TryParse(triggerDef.Trigger, true, out EffectTrigger trigger)) continue;

                var targetType = BattlecryTargetType.None;
                if (!string.IsNullOrWhiteSpace(triggerDef.TargetType))
                    Enum.TryParse(triggerDef.TargetType, true, out targetType);

                var fns = new List<Action<SimBoard, SimEntity, SimEntity>>();
                foreach (var e in triggerDef.Effects ?? Array.Empty<EffectDef>())
                {
                    var fn = ParseEffect(e);
                    if (fn != null) fns.Add(fn);
                }

                if (fns.Count == 0) continue;

                Action<SimBoard, SimEntity, SimEntity> combined = (b, s, t) =>
                {
                    foreach (var f in fns) f(b, s, t);
                };
                db.Register(cardId, trigger, combined, targetType);
            }
        }

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

        private static Action<SimBoard, SimEntity, SimEntity> ParseEffect(EffectDef e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Type)) return null;

            var type = e.Type;
            int v = e.V;
            int atk = e.Atk;
            int hp = e.Hp;
            int n = e.N;
            int dur = e.Dur;
            bool sp = e.UseSP;

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
                        foreach (var m in b.EnemyMinions.ToArray()) CardEffectDB.Dmg(b, m, d);
                        if (b.FriendHero != null) CardEffectDB.Dmg(b, b.FriendHero, d);
                        if (b.EnemyHero != null) CardEffectDB.Dmg(b, b.EnemyHero, d);
                    };

                case "dmg_enemy":
                    return (b, s, t) =>
                    {
                        int d = sp ? v + CardEffectDB.SP(b) : v;
                        var enemies = s != null && !s.IsFriend ? b.FriendMinions : b.EnemyMinions;
                        var hero = s != null && !s.IsFriend ? b.FriendHero : b.EnemyHero;
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
                        if (t != null)
                        {
                            t.Atk += atk;
                            t.Health += hp;
                            t.MaxHealth += hp;
                        }
                    };

                case "buff_all":
                    return (b, s, t) =>
                    {
                        foreach (var m in b.FriendMinions)
                        {
                            m.Atk += atk;
                            m.Health += hp;
                            m.MaxHealth += hp;
                        }
                    };

                case "buff_self":
                    return (b, s, t) =>
                    {
                        if (s != null)
                        {
                            s.Atk += atk;
                            s.Health += hp;
                            s.MaxHealth += hp;
                        }
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
                        {
                            b.FriendMinions.Add(new SimEntity
                            {
                                Atk = atk,
                                Health = hp,
                                MaxHealth = hp,
                                IsFriend = true,
                                IsTired = true
                            });
                        }
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

                case "give_taunt": return (b, s, t) => { if (t != null) t.IsTaunt = true; };
                case "give_ds": return (b, s, t) => { if (t != null) t.IsDivineShield = true; };
                case "give_windfury": return (b, s, t) => { if (t != null) t.IsWindfury = true; };
                case "give_rush": return (b, s, t) => { if (t != null) t.HasRush = true; };
                case "give_lifesteal": return (b, s, t) => { if (t != null) t.IsLifeSteal = true; };
                case "give_poison": return (b, s, t) => { if (t != null) t.HasPoison = true; };
                case "give_reborn": return (b, s, t) => { if (t != null) t.HasReborn = true; };
                case "give_immune": return (b, s, t) => { if (b.FriendHero != null) b.FriendHero.IsImmune = true; };

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
                        if (t != null)
                        {
                            t.Health = v;
                            t.MaxHealth = v;
                        }
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
                            Atk = atk,
                            Health = dur > 0 ? dur : hp,
                            MaxHealth = dur > 0 ? dur : hp,
                            IsFriend = true
                        };
                        if (b.FriendHero != null) b.FriendHero.Atk = atk;
                    };

                default:
                    return null;
            }
        }
    }
}
