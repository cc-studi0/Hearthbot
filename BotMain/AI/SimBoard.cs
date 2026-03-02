using System.Collections.Generic;
using System.Linq;
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
        public List<SimEntity> Hand = new();
        public int Mana, MaxMana;
        public bool HeroPowerUsed;
        public int CardsPlayedThisTurn;
        public int FriendCardDraw;
        public Card.CClass FriendClass, EnemyClass;
        /// <summary>我方牌库中剩余的 CardId 列表（来自 DeckZone）</summary>
        public List<Card.Cards> FriendDeckCards = new();

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
                Hand = Hand.Select(c => c.Clone()).ToList(),
                Mana = Mana,
                MaxMana = MaxMana,
                HeroPowerUsed = HeroPowerUsed,
                CardsPlayedThisTurn = CardsPlayedThisTurn,
                FriendCardDraw = FriendCardDraw,
                FriendClass = FriendClass,
                EnemyClass = EnemyClass,
                FriendDeckCards = new List<Card.Cards>(FriendDeckCards),
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
            sb.FriendWeapon = board.WeaponFriend != null ? ConvertCard(board.WeaponFriend, true) : null;
            sb.EnemyWeapon = board.WeaponEnemy != null ? ConvertCard(board.WeaponEnemy, false) : null;

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
                IsTired = isTired,
                IsTradeable = c.GetTag(Card.GAME_TAG.TRADEABLE) > 0,
                HasBattlecry = c.Template?.HasBattlecry ?? false,
                HasDeathrattle = c.Template?.HasDeathrattle ?? false,
                CountAttack = c.CountAttack,
                Type = c.Type,
            };
        }
    }
}
