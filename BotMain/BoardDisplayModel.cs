using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;

namespace BotMain
{
    public class MinionDisplay
    {
        public int Atk, Health, MaxHealth;
        public bool Taunt, DivineShield, Frozen, Stealth, Poisonous;
        public string Name;
    }

    public class BoardDisplayModel
    {
        public int FriendHeroHp, FriendHeroArmor, EnemyHeroHp, EnemyHeroArmor;
        public int Mana, MaxMana;
        public string FriendWeapon, EnemyWeapon;
        public List<MinionDisplay> FriendMinions = new();
        public List<MinionDisplay> EnemyMinions = new();
        public List<string> HandCards = new();

        public static BoardDisplayModel FromBoard(Board board)
        {
            var m = new BoardDisplayModel
            {
                Mana = board.ManaAvailable,
                MaxMana = board.MaxMana,
            };

            if (board.HeroFriend != null)
            {
                m.FriendHeroHp = board.HeroFriend.CurrentHealth;
                m.FriendHeroArmor = board.HeroFriend.CurrentArmor;
            }
            if (board.HeroEnemy != null)
            {
                m.EnemyHeroHp = board.HeroEnemy.CurrentHealth;
                m.EnemyHeroArmor = board.HeroEnemy.CurrentArmor;
            }

            if (board.WeaponFriend != null)
                m.FriendWeapon = $"{board.WeaponFriend.CurrentAtk}/{board.WeaponFriend.CurrentHealth}";
            if (board.WeaponEnemy != null)
                m.EnemyWeapon = $"{board.WeaponEnemy.CurrentAtk}/{board.WeaponEnemy.CurrentHealth}";

            if (board.MinionFriend != null)
                m.FriendMinions = board.MinionFriend.Select(ToDisplay).ToList();
            if (board.MinionEnemy != null)
                m.EnemyMinions = board.MinionEnemy.Select(ToDisplay).ToList();
            if (board.Hand != null)
                m.HandCards = board.Hand.Select(c => $"[{c.CurrentCost}] {c.Template?.Id}").ToList();

            return m;
        }

        private static MinionDisplay ToDisplay(Card c) => new()
        {
            Atk = c.CurrentAtk,
            Health = c.CurrentHealth,
            MaxHealth = c.MaxHealth,
            Taunt = c.IsTaunt,
            DivineShield = c.IsDivineShield,
            Frozen = c.IsFrozen,
            Stealth = c.IsStealth,
            Poisonous = c.HasPoison,
            Name = c.Template?.Id.ToString() ?? "?"
        };
    }
}
