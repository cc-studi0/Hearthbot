using System;
using System.Collections.Generic;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    [System.Flags]
    public enum EffectKind
    {
        None = 0,
        Damage = 1 << 0,
        Heal = 1 << 1,
        Buff = 1 << 2,
        Destroy = 1 << 3,
        Draw = 1 << 4,
        Summon = 1 << 5,
        Armor = 1 << 6,
        Mana = 1 << 7,
        Utility = 1 << 8,
    }

    public enum EffectTrigger
    {
        Battlecry,
        Deathrattle,
        Spell,
        EndOfTurn,
        Aura,
        LocationActivation,
        AfterAttackMinion,
        AfterDamaged
    }

    public class CardEffectDB
    {
        // 兼容旧脚本调用 CardEffectDB.Rnd.Next(...)
        internal static Random Rnd => Random.Shared;

        public sealed class HeroAttackContext
        {
            public int AttackDamage { get; set; }
            public bool TargetWasMinion { get; set; }
            public bool HonorableKill { get; set; }
        }

        private readonly Dictionary<(Card.Cards, EffectTrigger), Action<SimBoard, SimEntity, SimEntity>> _db = new();
        private readonly Dictionary<(Card.Cards, EffectTrigger), BattlecryTargetType> _targetTypes = new();
        private readonly Dictionary<(Card.Cards, EffectTrigger), EffectKind> _effectKinds = new();
        private readonly Dictionary<Card.Cards, Func<SimBoard, SimEntity, int, int>> _heroDamageReplacementByWeapon = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity, SimEntity, HeroAttackContext>> _afterHeroAttackByWeapon = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity>> _afterFriendlySpellCastByWeapon = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard>> _afterFriendlyHeroPowerUsedByWeapon = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity>> _afterFriendlyDivineShieldLostByWeapon = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity>> _afterFriendlyMinionDiedByWeapon = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity>> _afterTradeByCard = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity>> _weaponAuras = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity>> _weaponStartOfTurnHooks = new();
        private readonly Dictionary<Card.Cards, Action<SimBoard, SimEntity>> _weaponInHandRefreshHooks = new();
        private readonly HashSet<Card.Cards> _heroImmuneWhileAttackingWeapons = new();

        public void Register(Card.Cards id, EffectTrigger t, Action<SimBoard, SimEntity, SimEntity> fn)
            => _db[(id, t)] = fn;

        public void RegisterTargetType(Card.Cards id, EffectTrigger t, BattlecryTargetType targetType)
            => _targetTypes[(id, t)] = targetType;

        public void Register(Card.Cards id, EffectTrigger t, Action<SimBoard, SimEntity, SimEntity> fn, BattlecryTargetType targetType)
        {
            _db[(id, t)] = fn;
            if (targetType != BattlecryTargetType.None)
                _targetTypes[(id, t)] = targetType;
        }

        public bool TryGet(Card.Cards id, EffectTrigger t, out Action<SimBoard, SimEntity, SimEntity> fn)
            => _db.TryGetValue((id, t), out fn);

        public bool TryGetTargetType(Card.Cards id, EffectTrigger t, out BattlecryTargetType targetType)
            => _targetTypes.TryGetValue((id, t), out targetType);

        public void RegisterEffectKind(Card.Cards id, EffectTrigger t, EffectKind kinds)
        {
            if (kinds == EffectKind.None) return;
            if (_effectKinds.TryGetValue((id, t), out var existing))
                _effectKinds[(id, t)] = existing | kinds;
            else
                _effectKinds[(id, t)] = kinds;
        }

        public EffectKind GetEffectKinds(Card.Cards id, EffectTrigger t)
            => _effectKinds.TryGetValue((id, t), out var kinds) ? kinds : EffectKind.None;

        public bool HasEffectKind(Card.Cards id, EffectTrigger t, EffectKind kind)
        {
            if (kind == EffectKind.None) return false;
            var kinds = GetEffectKinds(id, t);
            return (kinds & kind) != 0;
        }

        public bool Has(Card.Cards id, EffectTrigger t) => _db.ContainsKey((id, t));

        public void RegisterHeroDamageReplacement(Card.Cards weaponId, Func<SimBoard, SimEntity, int, int> fn)
        {
            if (fn == null) return;
            _heroDamageReplacementByWeapon[weaponId] = fn;
        }

        public int ApplyHeroDamageReplacement(Card.Cards weaponId, SimBoard board, SimEntity hero, int incomingDamage)
        {
            if (incomingDamage <= 0) return 0;
            if (_heroDamageReplacementByWeapon.TryGetValue(weaponId, out var fn))
            {
                var replaced = fn(board, hero, incomingDamage);
                return replaced < 0 ? 0 : replaced;
            }
            return incomingDamage;
        }

        public void RegisterAfterHeroAttackWeapon(Card.Cards weaponId, Action<SimBoard, SimEntity, SimEntity, HeroAttackContext> fn)
        {
            if (fn == null) return;
            _afterHeroAttackByWeapon[weaponId] = fn;
        }

        public void TriggerAfterHeroAttackWeapon(SimBoard board, SimEntity hero, SimEntity target, HeroAttackContext context)
        {
            if (board == null || hero == null) return;
            var weapon = hero == board.FriendHero ? board.FriendWeapon : hero == board.EnemyHero ? board.EnemyWeapon : null;
            if (weapon == null) return;
            if (_afterHeroAttackByWeapon.TryGetValue(weapon.CardId, out var fn))
                fn(board, hero, target, context ?? new HeroAttackContext());
        }

        public void RegisterAfterFriendlySpellCastWeapon(Card.Cards weaponId, Action<SimBoard, SimEntity> fn)
        {
            if (fn == null) return;
            _afterFriendlySpellCastByWeapon[weaponId] = fn;
        }

        public void TriggerAfterFriendlySpellCastWeapon(SimBoard board, SimEntity spellCard)
        {
            if (board?.FriendWeapon == null) return;
            if (_afterFriendlySpellCastByWeapon.TryGetValue(board.FriendWeapon.CardId, out var fn))
                fn(board, spellCard);
        }

        public void RegisterAfterFriendlyHeroPowerUsedWeapon(Card.Cards weaponId, Action<SimBoard> fn)
        {
            if (fn == null) return;
            _afterFriendlyHeroPowerUsedByWeapon[weaponId] = fn;
        }

        public void TriggerAfterFriendlyHeroPowerUsedWeapon(SimBoard board)
        {
            if (board?.FriendWeapon == null) return;
            if (_afterFriendlyHeroPowerUsedByWeapon.TryGetValue(board.FriendWeapon.CardId, out var fn))
                fn(board);
        }

        public void RegisterAfterFriendlyDivineShieldLostWeapon(Card.Cards weaponId, Action<SimBoard, SimEntity> fn)
        {
            if (fn == null) return;
            _afterFriendlyDivineShieldLostByWeapon[weaponId] = fn;
        }

        public void TriggerAfterFriendlyDivineShieldLostWeapon(SimBoard board, SimEntity minion)
        {
            if (board?.FriendWeapon == null) return;
            if (_afterFriendlyDivineShieldLostByWeapon.TryGetValue(board.FriendWeapon.CardId, out var fn))
                fn(board, minion);
        }

        public void RegisterAfterFriendlyMinionDiedWeapon(Card.Cards weaponId, Action<SimBoard, SimEntity> fn)
        {
            if (fn == null) return;
            _afterFriendlyMinionDiedByWeapon[weaponId] = fn;
        }

        public void TriggerAfterFriendlyMinionDiedWeapon(SimBoard board, SimEntity deadMinion)
        {
            if (board?.FriendWeapon == null) return;
            if (_afterFriendlyMinionDiedByWeapon.TryGetValue(board.FriendWeapon.CardId, out var fn))
                fn(board, deadMinion);
        }

        public void RegisterAfterTradeCard(Card.Cards cardId, Action<SimBoard, SimEntity> fn)
        {
            if (fn == null) return;
            _afterTradeByCard[cardId] = fn;
        }

        public void TriggerAfterTradeCard(SimBoard board, SimEntity tradedCard)
        {
            if (board == null || tradedCard == null) return;
            if (_afterTradeByCard.TryGetValue(tradedCard.CardId, out var fn))
                fn(board, tradedCard);
        }

        public void RegisterWeaponAura(Card.Cards weaponId, Action<SimBoard, SimEntity> fn)
        {
            if (fn == null) return;
            _weaponAuras[weaponId] = fn;
        }

        public void ApplyWeaponAuras(SimBoard board)
        {
            if (board == null) return;
            if (board.FriendWeapon != null && _weaponAuras.TryGetValue(board.FriendWeapon.CardId, out var friendFn))
                friendFn(board, board.FriendWeapon);
            if (board.EnemyWeapon != null && _weaponAuras.TryGetValue(board.EnemyWeapon.CardId, out var enemyFn))
                enemyFn(board, board.EnemyWeapon);
        }

        public void RegisterWeaponStartOfTurnHook(Card.Cards weaponId, Action<SimBoard, SimEntity> fn)
        {
            if (fn == null) return;
            _weaponStartOfTurnHooks[weaponId] = fn;
        }

        public void RegisterWeaponInHandRefreshHook(Card.Cards weaponId, Action<SimBoard, SimEntity> fn)
        {
            if (fn == null) return;
            _weaponInHandRefreshHooks[weaponId] = fn;
        }

        public void RegisterHeroImmuneWhileAttackingWeapon(Card.Cards weaponId)
            => _heroImmuneWhileAttackingWeapons.Add(weaponId);

        public bool HasHeroImmuneWhileAttackingWeapon(Card.Cards weaponId)
            => _heroImmuneWhileAttackingWeapons.Contains(weaponId);

        public int Count => _db.Count;

        public static CardEffectDB BuildDefault()
        {
            var db = new CardEffectDB();
            CardEffectScriptLoader.LoadAll(db);
            return db;
        }

        // 供卡牌效果脚本运行时调用的辅助方法
        internal static int SP(SimBoard b) { int sp=0; foreach(var m in b.FriendMinions) sp+=m.SpellPower; return sp; }
        internal static void DrawCard(SimBoard b, List<Card.Cards> deck)
        {
            if (b == null || deck == null) return;

            bool isFriendDeck = ReferenceEquals(deck, b.FriendDeckCards)
                || !ReferenceEquals(deck, b.EnemyDeckCards);

            if (deck.Count <= 0)
            {
                if (isFriendDeck) b.FriendCardDraw += 1;
                return;
            }

            var cardId = deck[0];
            deck.RemoveAt(0);

            if (isFriendDeck)
            {
                if (b.Hand.Count >= 10)
                {
                    b.FriendCardDraw += 1;
                    return;
                }

                var drawn = CardEffectsScripts.CardEffectScriptHelpers.CreateCardInHand(cardId, true, b);
                if (drawn != null)
                    b.Hand.Add(drawn);
                else
                    b.FriendCardDraw += 1;
                return;
            }

            if (b.EnemyHand.Count >= 10) return;
            var enemyDrawn = CardEffectsScripts.CardEffectScriptHelpers.CreateCardInHand(cardId, false, b);
            b.EnemyHand.Add(enemyDrawn ?? new SimEntity
            {
                CardId = cardId,
                IsFriend = false,
                Type = Card.CType.MINION
            });
        }

        internal static SimEntity Summon(SimBoard b, List<SimEntity> side, Card.Cards cardId)
        {
            if (b == null || side == null || side.Count >= 7) return null;

            bool isFriendSide = ReferenceEquals(side, b.FriendMinions)
                || (side.Count > 0 && side[0].IsFriend)
                || !ReferenceEquals(side, b.EnemyMinions);

            var summoned = CardEffectsScripts.CardEffectScriptHelpers.CreateCardInHand(cardId, isFriendSide, b)
                ?? new SimEntity
                {
                    CardId = cardId,
                    IsFriend = isFriendSide,
                    Type = Card.CType.MINION,
                    Atk = Math.Max(0, CardEffectsScripts.CardEffectScriptHelpers.GetBaseAtk(cardId, 1)),
                    Health = Math.Max(1, CardEffectsScripts.CardEffectScriptHelpers.GetBaseHealth(cardId, 1)),
                    MaxHealth = Math.Max(1, CardEffectsScripts.CardEffectScriptHelpers.GetBaseHealth(cardId, 1))
                };

            summoned.IsFriend = isFriendSide;
            summoned.Type = Card.CType.MINION;
            summoned.IsTired = !summoned.HasCharge;
            summoned.EntityId = NextEntityId(b);
            side.Add(summoned);
            return summoned;
        }

        internal static void Dmg(SimBoard b, SimEntity t, int d) { if(t==null||t.IsImmune||d<=0) return; if(t.IsDivineShield){t.IsDivineShield=false;return;} t.Health-=d; }
        internal static void DoSilence(SimEntity t) { if(t==null) return; t.IsTaunt=false;t.IsDivineShield=false;t.HasPoison=false;t.IsLifeSteal=false;t.IsWindfury=false;t.HasReborn=false;t.IsSilenced=true;t.SpellPower=0; }
        private static void Silence(SimEntity t) => DoSilence(t);

        private static int NextEntityId(SimBoard b)
        {
            if (b == null) return 1;

            int maxId = 0;
            void Touch(SimEntity e)
            {
                if (e != null && e.EntityId > maxId)
                    maxId = e.EntityId;
            }

            Touch(b.FriendHero);
            Touch(b.EnemyHero);
            Touch(b.FriendWeapon);
            Touch(b.EnemyWeapon);
            Touch(b.HeroPower);
            foreach (var e in b.FriendMinions) Touch(e);
            foreach (var e in b.EnemyMinions) Touch(e);
            foreach (var e in b.Hand) Touch(e);
            foreach (var e in b.EnemyHand) Touch(e);

            return maxId + 1;
        }
    }
}
