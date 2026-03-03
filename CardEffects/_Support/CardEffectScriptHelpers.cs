using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BotMain.AI;
using SmartBot.Database;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal static class CardEffectScriptHelpers
    {
        public static readonly C[] DarkGiftDragonPool = ParseCardIds("FIR_901", "FIR_956");
        public static readonly C[] WarriorDarkGiftMinionPool = ParseCardIds("EDR_456", "FIR_956");
        public static readonly C[] GenericWeaponPool = ParseCardIds(
            "CORE_DAL_720", "CORE_DMF_238", "CORE_GIL_653", "CORE_ICC_064",
            "DAL_720", "DMF_238", "GIL_653", "GVG_043", "GVG_059", "OG_222", "TRL_543");

        private static readonly Dictionary<C, object> TemplateCache = new();
        private static readonly Dictionary<C, bool> IsDragonCache = new();
        private static readonly Dictionary<C, Card.CType> TypeCache = new();

        public static C[] ParseCardIds(params string[] ids)
        {
            if (ids == null || ids.Length == 0) return Array.Empty<C>();
            var list = new List<C>();
            foreach (var s in ids)
            {
                if (Enum.TryParse(s, true, out C id)) list.Add(id);
            }
            return list.ToArray();
        }

        public static bool IsDragonCard(C id)
        {
            if (IsDragonCache.TryGetValue(id, out var ok)) return ok;
            var tmpl = GetTemplate(id);
            bool isDragon = HasDragonRace(tmpl);
            IsDragonCache[id] = isDragon;
            return isDragon;
        }

        public static bool IsMinionCard(C id) => GetCardType(id, Card.CType.MINION) == Card.CType.MINION;
        public static bool IsWeaponCard(C id) => GetCardType(id, Card.CType.SPELL) == Card.CType.WEAPON;

        public static Card.CType GetCardType(C id, Card.CType fallback)
        {
            if (TypeCache.TryGetValue(id, out var t)) return t;
            var tmpl = GetTemplate(id);
            var raw = ReadProp(tmpl, "Type");
            if (TryParseEnum(raw, out Card.CType parsed))
            {
                TypeCache[id] = parsed;
                return parsed;
            }
            TypeCache[id] = fallback;
            return fallback;
        }

        public static int GetBaseCost(C id, int fallback) => ReadIntProp(GetTemplate(id), fallback, "Cost", "cost");
        public static int GetBaseAtk(C id, int fallback) => ReadIntProp(GetTemplate(id), fallback, "Atk", "Attack", "attack");
        public static int GetBaseHealth(C id, int fallback) => ReadIntProp(GetTemplate(id), fallback, "Health", "Hp", "health");
        public static int GetBaseDurability(C id, int fallback) => ReadIntProp(GetTemplate(id), fallback, "Durability", "Health", "durability");

        public static bool HasDragonInHand(SimBoard b, SimEntity except = null)
            => b.Hand.Any(c => !ReferenceEquals(c, except) && IsDragonCard(c.CardId));

        public static SimEntity PickHandDragon(SimBoard b, SimEntity source = null)
        {
            var choices = b.Hand.Where(c => !ReferenceEquals(c, source) && IsDragonCard(c.CardId)).ToList();
            if (choices.Count == 0) return null;
            return choices[PickIndex(choices.Count, b, source)];
        }

        public static void Buff(SimEntity e, int atk, int hp)
        {
            if (e == null) return;
            e.Atk += atk;
            e.Health += hp;
            e.MaxHealth += hp;
        }

        public static int PickIndex(int count, SimBoard b, SimEntity s = null, SimEntity t = null)
        {
            if (count <= 0) return 0;
            int seed = 17;
            seed = seed * 31 + b.CardsPlayedThisTurn;
            seed = seed * 31 + b.Mana;
            seed = seed * 31 + b.FriendMinions.Count;
            seed = seed * 31 + b.EnemyMinions.Count;
            seed = seed * 31 + b.Hand.Count;
            if (s != null)
            {
                seed = seed * 31 + s.EntityId;
                seed = seed * 31 + s.Atk;
                seed = seed * 31 + s.Health;
            }
            if (t != null)
            {
                seed = seed * 31 + t.EntityId;
                seed = seed * 31 + t.Atk;
                seed = seed * 31 + t.Health;
            }
            if (seed < 0) seed = -seed;
            return seed % count;
        }

        public static bool DrawRandomMinionToHand(SimBoard b, SimEntity source = null, int buffAtk = 0, int buffHp = 0)
        {
            var candidates = b.FriendDeckCards.Where(IsMinionCard).ToList();
            if (candidates.Count == 0)
            {
                b.FriendCardDraw += 1;
                return false;
            }

            var idx = PickIndex(candidates.Count, b, source);
            var cardId = candidates[idx];

            // 只移除一张
            b.FriendDeckCards.Remove(cardId);

            if (b.Hand.Count >= 10) return false;

            var e = CreateCardInHand(cardId, true, b);
            if (e == null) return false;
            Buff(e, buffAtk, buffHp);
            b.Hand.Add(e);
            return true;
        }

        public static bool AddDiscoveredCardToHand(SimBoard b, IEnumerable<C> pool, SimEntity source = null, int buffAtk = 0, int buffHp = 0)
        {
            if (b.Hand.Count >= 10) return false;
            var arr = (pool ?? Enumerable.Empty<C>()).Distinct().ToArray();
            if (arr.Length == 0) return false;

            var buildable = arr.Where(id => CanCreateCardInHand(id, b)).ToArray();
            var pickedPool = buildable.Length > 0 ? buildable : arr;
            var picked = pickedPool[PickIndex(pickedPool.Length, b, source)];
            var e = CreateCardInHand(picked, true, b);
            if (e == null) return false;
            Buff(e, buffAtk, buffHp);
            b.Hand.Add(e);
            return true;
        }

        private static bool CanCreateCardInHand(C id, SimBoard context)
        {
            if (id == 0) return false;
            if (GetTemplate(id) != null) return true;
            return FindKnownEntityByCardId(context, id) != null;
        }

        public static SimEntity CreateCardInHand(C id, bool isFriend, SimBoard context = null)
        {
            var type = GetCardType(id, Card.CType.MINION);
            var template = GetTemplate(id);
            var knownFromTemplate = template != null;

            var inferred = FindKnownEntityByCardId(context, id);
            if (!knownFromTemplate && inferred == null)
                return null;

            var fallbackCost = inferred != null ? Math.Max(0, inferred.Cost) : 0;
            var fallbackAtk = inferred != null ? Math.Max(0, inferred.Atk) : 0;
            var fallbackHp = inferred != null ? Math.Max(1, inferred.MaxHealth > 0 ? inferred.MaxHealth : inferred.Health) : 1;
            var fallbackDurability = inferred != null ? Math.Max(1, inferred.MaxHealth > 0 ? inferred.MaxHealth : inferred.Health) : 2;

            var cost = Math.Max(0, GetBaseCost(id, fallbackCost));
            var atk = Math.Max(0, GetBaseAtk(id, fallbackAtk));

            int hp = type == Card.CType.WEAPON
                ? Math.Max(1, GetBaseDurability(id, fallbackDurability))
                : Math.Max(1, GetBaseHealth(id, fallbackHp));

            return new SimEntity
            {
                CardId = id,
                IsFriend = isFriend,
                Type = type,
                Cost = cost,
                Atk = atk,
                Health = hp,
                MaxHealth = hp
            };
        }

        private static SimEntity FindKnownEntityByCardId(SimBoard b, C id)
        {
            if (b == null) return null;

            var fromHand = b.Hand?.FirstOrDefault(e => e != null && e.CardId == id);
            if (fromHand != null) return fromHand;

            var fromFriend = b.FriendMinions?.FirstOrDefault(e => e != null && e.CardId == id);
            if (fromFriend != null) return fromFriend;

            var fromEnemy = b.EnemyMinions?.FirstOrDefault(e => e != null && e.CardId == id);
            if (fromEnemy != null) return fromEnemy;

            if (b.FriendWeapon != null && b.FriendWeapon.CardId == id) return b.FriendWeapon;
            if (b.EnemyWeapon != null && b.EnemyWeapon.CardId == id) return b.EnemyWeapon;

            return null;
        }

        public static SimEntity EquipWeaponFromCard(SimBoard b, bool isFriend, C id, int fallbackAtk = 2, int fallbackDur = 2)
        {
            int atk = Math.Max(1, GetBaseAtk(id, fallbackAtk));
            int dur = Math.Max(1, GetBaseDurability(id, fallbackDur));
            return EquipWeaponByStats(b, isFriend, id, atk, dur);
        }

        public static SimEntity EquipWeaponByStats(SimBoard b, bool isFriend, C cardId, int atk, int dur)
        {
            var w = new SimEntity
            {
                CardId = cardId,
                Type = Card.CType.WEAPON,
                Atk = atk,
                Health = dur,
                MaxHealth = dur,
                IsFriend = isFriend
            };

            if (isFriend)
            {
                b.FriendWeapon = w;
                if (b.FriendHero != null) b.FriendHero.Atk = atk;
            }
            else
            {
                b.EnemyWeapon = w;
                if (b.EnemyHero != null) b.EnemyHero.Atk = atk;
            }
            return w;
        }

        public static C PickWeaponFromFriendlyDeckOrPool(SimBoard b, SimEntity source = null)
        {
            var pool = b.FriendDeckCards.Where(IsWeaponCard).Distinct().ToList();
            if (pool.Count == 0) pool.AddRange(GenericWeaponPool);
            if (pool.Count == 0) return 0;
            return pool[PickIndex(pool.Count, b, source)];
        }

        private static object GetTemplate(C id)
        {
            if (TemplateCache.TryGetValue(id, out var t)) return t;
            t = CardTemplate.LoadFromId(id);
            TemplateCache[id] = t;
            return t;
        }

        private static bool HasDragonRace(object tmpl)
        {
            if (tmpl == null) return false;

            if (ContainsDragon(ReadProp(tmpl, "Race"))) return true;

            var races = ReadProp(tmpl, "Races");
            if (races is IEnumerable enumerable)
            {
                foreach (var r in enumerable)
                {
                    if (ContainsDragon(r)) return true;
                }
            }

            return false;
        }

        private static bool ContainsDragon(object v)
        {
            if (v == null) return false;
            return v.ToString().IndexOf("DRAGON", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object ReadProp(object obj, params string[] names)
        {
            if (obj == null || names == null) return null;
            var type = obj.GetType();
            foreach (var n in names)
            {
                var p = type.GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p == null) continue;
                try { return p.GetValue(obj); } catch { }
            }
            return null;
        }

        private static int ReadIntProp(object obj, int fallback, params string[] names)
        {
            var v = ReadProp(obj, names);
            if (v == null) return fallback;
            if (v is int i) return i;
            if (int.TryParse(v.ToString(), out var parsed)) return parsed;
            return fallback;
        }

        private static bool TryParseEnum<T>(object value, out T parsed) where T : struct
        {
            parsed = default;
            if (value == null) return false;
            if (value is T t)
            {
                parsed = t;
                return true;
            }
            return Enum.TryParse(value.ToString(), true, out parsed);
        }
    }
}
