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
        public static readonly C[] KnownPlaguePool = ParseCardIds("TTN_450", "TTN_454", "TTN_455");

        private static readonly Dictionary<C, object> TemplateCache = new();
        private static readonly Dictionary<C, bool> IsDragonCache = new();
        private static readonly Dictionary<C, bool> IsBeastCache = new();
        private static readonly Dictionary<C, bool> IsDemonCache = new();
        private static readonly Dictionary<C, bool> IsMechCache = new();
        private static readonly Dictionary<C, bool> IsPirateCache = new();
        private static readonly Dictionary<C, bool> IsDraeneiCache = new();
        private static readonly Dictionary<C, bool> IsOutcastCache = new();
        private static readonly Dictionary<C, bool> IsShadowSpellCache = new();
        private static readonly Dictionary<C, bool> HasDarkGiftCache = new();
        private static readonly Dictionary<C, bool> HasBattlecryCache = new();
        private static readonly Dictionary<C, bool> IsDeathrattleMinionCache = new();
        private static readonly Dictionary<C, Card.CType> TypeCache = new();
        private static C[] _allOutcastCards;
        private static C[] _allDemonCards;

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
        public static bool IsSpellCard(C id) => GetCardType(id, Card.CType.MINION) == Card.CType.SPELL;
        public static bool IsWeaponCard(C id) => GetCardType(id, Card.CType.SPELL) == Card.CType.WEAPON;

        public static bool IsBeastCard(C id)
        {
            if (IsBeastCache.TryGetValue(id, out var ok)) return ok;
            ok = HasRace(GetTemplate(id), "BEAST");
            IsBeastCache[id] = ok;
            return ok;
        }

        // 兼容旧脚本命名
        public static bool IsBeastMinion(C id) => IsRaceMinion(id, "BEAST", IsBeastCache);
        public static bool IsDragonMinion(C id) => IsRaceMinion(id, "DRAGON", IsDragonCache);
        public static bool IsMechMinion(C id) => IsRaceMinion(id, "MECH", IsMechCache);
        public static bool IsPirateMinion(C id) => IsRaceMinion(id, "PIRATE", IsPirateCache);
        public static bool IsDraeneiMinion(C id) => IsRaceMinion(id, "DRAENEI", IsDraeneiCache);

        public static bool HasBattlecry(C id)
        {
            if (HasBattlecryCache.TryGetValue(id, out var ok)) return ok;
            var tmpl = GetTemplate(id);

            ok = (ReadProp(tmpl, "HasBattlecry") is bool hasBc && hasBc)
                || HasMechanic(tmpl, "BATTLECRY")
                || ContainsIgnoreCase(ReadProp(tmpl, "Text", "text"), "Battlecry");

            HasBattlecryCache[id] = ok;
            return ok;
        }

        public static bool IsDemonCard(C id)
        {
            if (IsDemonCache.TryGetValue(id, out var ok)) return ok;
            ok = HasRace(GetTemplate(id), "DEMON");
            IsDemonCache[id] = ok;
            return ok;
        }

        public static bool IsOutcastCard(C id)
        {
            if (IsOutcastCache.TryGetValue(id, out var ok)) return ok;
            var tmpl = GetTemplate(id);
            ok = HasMechanic(tmpl, "OUTCAST")
                || ContainsIgnoreCase(ReadProp(tmpl, "Text", "text"), "Outcast");
            IsOutcastCache[id] = ok;
            return ok;
        }

        public static bool IsShadowSpellCard(C id)
        {
            if (IsShadowSpellCache.TryGetValue(id, out var ok)) return ok;
            var tmpl = GetTemplate(id);
            ok = IsSpellCard(id) && ContainsIgnoreCase(ReadProp(tmpl, "SpellSchool", "spellSchool"), "SHADOW");
            IsShadowSpellCache[id] = ok;
            return ok;
        }

        public static bool HasDarkGiftKeyword(C id)
        {
            if (HasDarkGiftCache.TryGetValue(id, out var ok)) return ok;
            var tmpl = GetTemplate(id);
            ok = ContainsIgnoreCase(ReadProp(tmpl, "Text", "text"), "Dark Gift")
                || ContainsIgnoreCase(ReadProp(tmpl, "CollectionText", "collectionText"), "Dark Gift");
            HasDarkGiftCache[id] = ok;
            return ok;
        }

        public static bool IsDeathrattleMinionCard(C id)
        {
            if (IsDeathrattleMinionCache.TryGetValue(id, out var ok)) return ok;
            if (!IsMinionCard(id))
            {
                IsDeathrattleMinionCache[id] = false;
                return false;
            }

            var tmpl = GetTemplate(id);
            if (ReadProp(tmpl, "HasDeathrattle") is bool hasDr && hasDr)
            {
                IsDeathrattleMinionCache[id] = true;
                return true;
            }

            ok = HasMechanic(tmpl, "DEATHRATTLE")
                || ContainsIgnoreCase(ReadProp(tmpl, "Text", "text"), "Deathrattle");
            IsDeathrattleMinionCache[id] = ok;
            return ok;
        }

        public static C[] GetAllOutcastCards()
        {
            if (_allOutcastCards != null) return _allOutcastCards;
            _allOutcastCards = Enum.GetValues(typeof(C)).Cast<C>()
                .Where(IsOutcastCard)
                .ToArray();
            return _allOutcastCards;
        }

        public static C[] GetAllDemonCards()
        {
            if (_allDemonCards != null) return _allDemonCards;
            _allDemonCards = Enum.GetValues(typeof(C)).Cast<C>()
                .Where(IsDemonCard)
                .ToArray();
            return _allDemonCards;
        }

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

        public static bool DrawRandomCardToHandByPredicate(
            SimBoard b,
            Func<C, bool> predicate,
            SimEntity source = null,
            int buffAtk = 0,
            int buffHp = 0)
        {
            if (b == null || predicate == null) return false;

            var candidates = b.FriendDeckCards.Where(id => predicate(id)).ToList();
            if (candidates.Count == 0)
            {
                b.FriendCardDraw += 1;
                return false;
            }

            var idx = PickIndex(candidates.Count, b, source);
            var cardId = candidates[idx];
            b.FriendDeckCards.Remove(cardId);

            if (b.Hand.Count >= 10) return false;

            var e = CreateCardInHand(cardId, true, b);
            if (e == null) return false;
            Buff(e, buffAtk, buffHp);
            b.Hand.Add(e);
            return true;
        }

        public static bool ReduceRandomSpellCostInHand(SimBoard b, int amount, SimEntity source = null)
        {
            if (b == null || amount <= 0) return false;
            var spells = b.Hand.Where(c => IsSpellCard(c.CardId)).ToList();
            if (spells.Count == 0) return false;
            var pick = spells[PickIndex(spells.Count, b, source)];
            pick.Cost = Math.Max(0, pick.Cost - amount);
            return true;
        }

        public static bool BuffRandomMinionInHand(SimBoard b, int atk, int hp, SimEntity source = null)
        {
            if (b == null) return false;
            var mins = b.Hand.Where(c => IsMinionCard(c.CardId)).ToList();
            if (mins.Count == 0) return false;
            var pick = mins[PickIndex(mins.Count, b, source)];
            Buff(pick, atk, hp);
            return true;
        }

        public static void BuffAllMinionsInHand(SimBoard b, int atk, int hp)
        {
            if (b == null) return;
            foreach (var c in b.Hand.Where(c => IsMinionCard(c.CardId)))
                Buff(c, atk, hp);
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

            if (ContainsRaceText(ReadProp(tmpl, "Race"), "DRAGON")) return true;

            var races = ReadProp(tmpl, "Races");
            if (races is IEnumerable enumerable)
            {
                foreach (var r in enumerable)
                {
                    if (ContainsRaceText(r, "DRAGON")) return true;
                }
            }

            return false;
        }

        private static bool HasRace(object tmpl, string raceName)
        {
            if (tmpl == null || string.IsNullOrWhiteSpace(raceName)) return false;
            if (ContainsRaceText(ReadProp(tmpl, "Race"), raceName)) return true;
            var races = ReadProp(tmpl, "Races");
            if (races is IEnumerable enumerable)
            {
                foreach (var r in enumerable)
                {
                    if (ContainsRaceText(r, raceName)) return true;
                }
            }
            return false;
        }

        private static bool ContainsRaceText(object v, string raceName)
        {
            if (v == null) return false;
            return v.ToString().IndexOf(raceName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRaceMinion(C id, string raceName, Dictionary<C, bool> cache)
        {
            if (cache != null && cache.TryGetValue(id, out var ok)) return ok;
            ok = IsMinionCard(id) && HasRace(GetTemplate(id), raceName);
            if (cache != null) cache[id] = ok;
            return ok;
        }

        private static bool HasMechanic(object tmpl, string mechanicName)
        {
            if (tmpl == null || string.IsNullOrWhiteSpace(mechanicName)) return false;
            var mechanics = ReadProp(tmpl, "Mechanics", "mechanics");
            if (mechanics is IEnumerable enumerable)
            {
                foreach (var m in enumerable)
                {
                    if (ContainsIgnoreCase(m, mechanicName)) return true;
                }
            }
            return false;
        }

        private static bool ContainsIgnoreCase(object value, string text)
        {
            if (value == null || string.IsNullOrWhiteSpace(text)) return false;
            return value.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
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
