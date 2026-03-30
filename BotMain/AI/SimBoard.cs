using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SmartBot.Plugins.API;

namespace BotMain.AI
{
    public class SimBoard
    {
        public List<SimEntity> FriendMinions = new();
        public List<SimEntity> EnemyMinions = new();
        public SimEntity FriendHero, EnemyHero;
        public SimEntity FriendWeapon, EnemyWeapon;
        public SimEntity HeroPower;
        public SimEntity EnemyHeroPower;
        public List<SimEntity> Hand = new();
        public List<SimEntity> EnemyHand = new();
        public int Mana, MaxMana;
        public bool HeroPowerUsed;
        public int CardsPlayedThisTurn;
        public int FriendCardDraw;
        public int FriendExcavateCount;
        public int EnemyDeckPlagueCount;
        public Card.CClass FriendClass, EnemyClass;
        /// <summary>我方牌库中剩余的 CardId 列表（来自 DeckZone）</summary>
        public List<Card.Cards> FriendDeckCards = new();
        /// <summary>敌方牌库模拟数据（仅用于“洗入对手牌库”类效果的计数/近似）</summary>
        public List<Card.Cards> EnemyDeckCards = new();
        /// <summary>战场上已触发的秘密（仅做脚本兼容与粗略状态）</summary>
        public List<Card.Cards> FriendSecrets = new();
        public List<Card.Cards> EnemySecrets = new();

        // 兼容旧脚本字段命名
        public List<Card.Cards> FriendDeck => FriendDeckCards;
        public List<Card.Cards> EnemyDeck => EnemyDeckCards;

        public SimBoard Clone()
        {
            return new SimBoard
            {
                FriendMinions = FriendMinions.Select(m => m.Clone()).ToList(),
                EnemyMinions = EnemyMinions.Select(m => m.Clone()).ToList(),
                FriendHero = FriendHero?.Clone(),
                EnemyHero = EnemyHero?.Clone(),
                FriendWeapon = FriendWeapon?.Clone(),
                EnemyWeapon = EnemyWeapon?.Clone(),
                HeroPower = HeroPower?.Clone(),
                EnemyHeroPower = EnemyHeroPower?.Clone(),
                Hand = Hand.Select(c => c.Clone()).ToList(),
                EnemyHand = EnemyHand.Select(c => c.Clone()).ToList(),
                Mana = Mana,
                MaxMana = MaxMana,
                HeroPowerUsed = HeroPowerUsed,
                CardsPlayedThisTurn = CardsPlayedThisTurn,
                FriendCardDraw = FriendCardDraw,
                FriendExcavateCount = FriendExcavateCount,
                EnemyDeckPlagueCount = EnemyDeckPlagueCount,
                FriendClass = FriendClass,
                EnemyClass = EnemyClass,
                FriendDeckCards = new List<Card.Cards>(FriendDeckCards),
                EnemyDeckCards = new List<Card.Cards>(EnemyDeckCards),
                FriendSecrets = new List<Card.Cards>(FriendSecrets),
                EnemySecrets = new List<Card.Cards>(EnemySecrets),
            };
        }

        public static SimBoard FromBoard(Board board)
        {
            var sb = new SimBoard
            {
                Mana = board.ManaAvailable,
                MaxMana = board.MaxMana,
                HeroPowerUsed = board.Ability?.GetTag(Card.GAME_TAG.EXHAUSTED) == 1,
                CardsPlayedThisTurn = board.CardsPlayedThisTurn,
                FriendClass = board.FriendClass,
                EnemyClass = board.EnemyClass,
            };

            sb.FriendHero = ConvertCard(board.HeroFriend, true);
            sb.EnemyHero = ConvertCard(board.HeroEnemy, false);
            sb.HeroPower = board.Ability != null ? ConvertCard(board.Ability, true) : null;
            sb.EnemyHeroPower = board.EnemyAbility != null ? ConvertCard(board.EnemyAbility, false) : null;
            sb.FriendWeapon = board.WeaponFriend != null ? ConvertCard(board.WeaponFriend, true) : null;
            sb.EnemyWeapon = board.WeaponEnemy != null ? ConvertCard(board.WeaponEnemy, false) : null;

            // 某些客户端版本里武器的风怒不一定同步到英雄实体，补一层继承以避免漏掉第 2 次攻击。
            if (sb.FriendHero != null && sb.FriendWeapon?.IsWindfury == true)
                sb.FriendHero.IsWindfury = true;
            if (sb.EnemyHero != null && sb.EnemyWeapon?.IsWindfury == true)
                sb.EnemyHero.IsWindfury = true;

            // 兼容部分版本里 Hero.CurrentAtk 未正确反映武器攻击力的情况
            if (sb.FriendHero != null && sb.FriendHero.Atk <= 0 && sb.FriendWeapon != null && sb.FriendWeapon.Health > 0)
                sb.FriendHero.Atk = sb.FriendWeapon.Atk;
            if (sb.EnemyHero != null && sb.EnemyHero.Atk <= 0 && sb.EnemyWeapon != null && sb.EnemyWeapon.Health > 0)
                sb.EnemyHero.Atk = sb.EnemyWeapon.Atk;

            if (board.MinionFriend != null)
                sb.FriendMinions = board.MinionFriend.Select(c => ConvertCard(c, true)).ToList();
            if (board.MinionEnemy != null)
                sb.EnemyMinions = board.MinionEnemy.Select(c => ConvertCard(c, false)).ToList();
            if (board.Hand != null)
                sb.Hand = board.Hand.Select(c => ConvertCard(c, true)).ToList();
            if (board.Secret != null)
                sb.FriendSecrets = board.Secret.ToList();

            return sb;
        }

        private static SimEntity ConvertCard(Card c, bool isFriend)
        {
            if (c == null) return null;

            var atk = c.CurrentAtk;
            if (c.Type == Card.CType.HERO && atk <= 0)
            {
                // 英雄攻击在不同客户端版本上有时落在 tag 而非 CurrentAtk
                var atkTag = c.GetTag(Card.GAME_TAG.ATK);
                atk = Math.Max(atk, atkTag);
            }

            var isTired = c.Type == Card.CType.HERO
                ? c.GetTag(Card.GAME_TAG.EXHAUSTED) == 1
                : c.IsTired && !c.CanAttack;
            var countAttack = ReadCountAttackThisTurn(c);

            // 以游戏实时可攻击状态为准做校正，避免"明明能打却在模拟里被禁攻"
            if (c.CanAttack)
            {
                isTired = false;
                if (!c.IsWindfury)
                    countAttack = 0;
                else if (countAttack > 1)
                    countAttack = 1;
            }

            // 英雄额外校正：即使 Card.CanAttack 为 false（某些客户端版本不可靠），
            // 如果英雄有攻击力且本回合攻击次数为 0、未冰冻，EXHAUSTED 很可能是残留值。
            // 此时清除 isTired，让 SimEntity.CanAttack 能依靠 localReady 判断。
            if (c.Type == Card.CType.HERO && !c.CanAttack && isTired && atk > 0 && countAttack == 0 && !c.IsFrozen)
            {
                isTired = false;
            }

            return new SimEntity
            {
                CardId = c.Template?.Id ?? 0,
                EntityId = c.Id,
                Atk = atk,
                Health = c.CurrentHealth,
                MaxHealth = c.MaxHealth,
                Armor = c.CurrentArmor,
                Cost = c.CurrentCost,
                SpellPower = c.SpellPower,
                IsFriend = isFriend,
                IsTaunt = c.IsTaunt,
                IsDivineShield = c.IsDivineShield,
                IsWindfury = c.IsWindfury,
                HasPoison = c.HasPoison,
                IsLifeSteal = c.IsLifeSteal,
                HasReborn = c.HasReborn,
                IsFrozen = c.IsFrozen,
                IsImmune = c.IsImmune,
                IsSilenced = c.IsSilenced,
                IsStealth = c.IsStealth,
                HasCharge = c.IsCharge,
                HasRush = c.HasRush,
                CanAttackHeroes = ReadCanAttackHeroes(c),
                IsTired = isTired,
                IsTradeable = c.GetTag(Card.GAME_TAG.TRADEABLE) > 0,
                HasBattlecry = c.Template?.HasBattlecry ?? false,
                HasDeathrattle = c.Template?.HasDeathrattle ?? false,
                CountAttack = countAttack,
                OverloadedCrystals = ReadOverloadedCrystals(c),
                UseBoardCanAttack = true,
                BoardCanAttack = c.CanAttack,
                Type = c.Type,
            };
        }

        private static int ReadCountAttackThisTurn(Card c)
        {
            if (c == null) return 0;

            try
            {
                if (Enum.TryParse("NUM_ATTACKS_THIS_TURN", out Card.GAME_TAG tag))
                {
                    var n = c.GetTag(tag);
                    if (n >= 0) return n;
                }
            }
            catch
            {
            }

            return Math.Max(0, c.CountAttack);
        }

        private static bool ReadCanAttackHeroes(Card c)
        {
            if (c == null) return true;

            try
            {
                var prop = typeof(Card).GetProperty("CanAttackHeroes", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
                {
                    var value = prop.GetValue(c);
                    if (value is bool canAttackHeroes)
                        return canAttackHeroes;
                }
            }
            catch
            {
            }

            try
            {
                if (Enum.TryParse("CANNOT_ATTACK_HEROES", out Card.GAME_TAG tag))
                    return c.GetTag(tag) != 1;
            }
            catch
            {
            }

            return true;
        }

        private static int ReadOverloadedCrystals(Card c)
        {
            if (c == null) return 0;

            try
            {
                if (Enum.TryParse("OVERLOAD_OWED", out Card.GAME_TAG owedTag))
                {
                    var owed = c.GetTag(owedTag);
                    if (owed > 0) return owed;
                }

                if (Enum.TryParse("OVERLOAD_LOCKED", out Card.GAME_TAG lockedTag))
                {
                    var locked = c.GetTag(lockedTag);
                    if (locked > 0) return locked;
                }
            }
            catch
            {
            }

            return 0;
        }
    }
}
