using System;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class ClickProfileTest : ClickProfile
{
    private int _pollCount = 0;

    public ClickInstruction GetNextClick(Board board)
    {
        _pollCount++;

        Bot.Log("[ClickTest] ===== Poll #" + _pollCount + " =====");
        Bot.Log("[ClickTest] Turn: " + board.TurnCount + " | Mana: " + board.ManaAvailable + "/" + board.MaxMana);
        Bot.Log("[ClickTest] Hand: " + board.Hand.Count + " cards | FriendMinions: " + board.MinionFriend.Count + " | EnemyMinions: " + board.MinionEnemy.Count);
        Bot.Log("[ClickTest] FriendHero HP: " + board.HeroFriend.CurrentHealth + " armor: " + board.HeroFriend.CurrentArmor);
        Bot.Log("[ClickTest] EnemyHero HP: " + board.HeroEnemy.CurrentHealth + " armor: " + board.HeroEnemy.CurrentArmor);
        Bot.Log("[ClickTest] FriendClass: " + board.FriendClass + " | EnemyClass: " + board.EnemyClass);

        if (board.WeaponFriend != null)
            Bot.Log("[ClickTest] Weapon equipped: atk=" + board.WeaponFriend.CurrentAtk + " dur=" + board.WeaponFriend.CurrentDurability);

        // Log hand contents
        for (int i = 0; i < board.Hand.Count; i++)
        {
            var c = board.Hand[i];
            Bot.Log("[ClickTest]   Hand[" + i + "]: id=" + c.Id + " cost=" + c.CurrentCost + " type=" + c.Type + " atk=" + c.CurrentAtk + " hp=" + c.CurrentHealth + " name=" + c.Template.Id);
        }

        // Log friendly minions
        for (int i = 0; i < board.MinionFriend.Count; i++)
        {
            var m = board.MinionFriend[i];
            Bot.Log("[ClickTest]   FriendMinion[" + i + "]: id=" + m.Id + " atk=" + m.CurrentAtk + " hp=" + m.CurrentHealth + " canAttack=" + m.CanAttack + " taunt=" + m.IsTaunt + " tired=" + m.IsTired + " name=" + m.Template.Id);
        }

        // Log enemy minions
        for (int i = 0; i < board.MinionEnemy.Count; i++)
        {
            var m = board.MinionEnemy[i];
            Bot.Log("[ClickTest]   EnemyMinion[" + i + "]: id=" + m.Id + " atk=" + m.CurrentAtk + " hp=" + m.CurrentHealth + " taunt=" + m.IsTaunt + " name=" + m.Template.Id);
        }

        // Log hero power
        if (board.Ability != null)
            Bot.Log("[ClickTest] HeroPower: cost=" + board.Ability.CurrentCost + " id=" + board.Ability.Id);

        // --- Play cards ---
        foreach (var card in board.Hand)
        {
            if (card.CurrentCost <= board.ManaAvailable)
            {
                Bot.Log("[ClickTest] >> ACTION: PlayCard id=" + card.Id + " cost=" + card.CurrentCost + " name=" + card.Template.Id + " boardIndex=0");
                return ClickInstruction.PlayCard(card.Id, boardIndex: 0);
            }
        }
        Bot.Log("[ClickTest] No playable cards in hand");

        // --- Attack with minions ---
        foreach (var minion in board.MinionFriend)
        {
            if (minion.CanAttack)
            {
                Bot.Log("[ClickTest] >> ACTION: Attack minion id=" + minion.Id + " atk=" + minion.CurrentAtk + " -> EnemyHero id=" + board.HeroEnemy.Id);
                return ClickInstruction.Attack(minion.Id, board.HeroEnemy.Id);
            }
        }
        Bot.Log("[ClickTest] No minions can attack");

        // --- Weapon attack ---
        if (board.WeaponFriend != null && board.HeroFriend.CanAttack)
        {
            Bot.Log("[ClickTest] >> ACTION: Weapon attack hero id=" + board.HeroFriend.Id + " -> EnemyHero id=" + board.HeroEnemy.Id);
            return ClickInstruction.Attack(board.HeroFriend.Id, board.HeroEnemy.Id);
        }

        // --- Hero power ---
        if (board.Ability != null && board.Ability.CurrentCost <= board.ManaAvailable)
        {
            Bot.Log("[ClickTest] >> ACTION: HeroPower (no target)");
            return ClickInstruction.HeroPower();
        }
        Bot.Log("[ClickTest] Hero power not available or not enough mana");

        Bot.Log("[ClickTest] >> ACTION: EndTurn (nothing left to do)");
        return ClickInstruction.End();
    }
}
