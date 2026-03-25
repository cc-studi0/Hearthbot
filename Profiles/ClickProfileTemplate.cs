using System;
using System.Collections.Generic;
using SmartBot.Plugins.API;
using SmartBotProfiles;

// ClickProfile Template
// This profile demonstrates all available ClickInstruction actions.
// Modify the logic to suit your strategy.

[Serializable]
public class ClickProfileTemplate : ClickProfile
{
    // State tracking between polls (persists across calls within a game)
    private bool _usedHeroPower = false;

    public ClickInstruction GetNextClick(Board board)
    {
        // --- 1. Handle discover/choice if active ---
        // If a discover or choice prompt is on screen, pick the first option
        // (You can inspect board state to make smarter choices)
        // Uncomment if needed:
        // return ClickInstruction.ChooseByIndex(0);

        // --- 2. Play cards from hand ---
        foreach (var card in board.Hand)
        {
            if (card.CurrentCost > board.ManaAvailable)
                continue;

            // Skip cards you don't want to play
            // if (card.Template.Id == Card.Cards.XXX) continue;

            if (card.Type == Card.CType.MINION)
            {
                // Play minion to the rightmost position
                int position = board.MinionFriend.Count;

                // Check if it has a targeted battlecry by looking at Targets
                if (card.Targets != null && card.Targets.Count > 0)
                {
                    // Pick the first valid target
                    var target = card.Targets[0];
                    return ClickInstruction.PlayCard(card.Id, boardIndex: position, targetEntityId: target.Id);
                }

                return ClickInstruction.PlayCard(card.Id, boardIndex: position);
            }

            if (card.Type == Card.CType.SPELL)
            {
                // Targeted spell: pick best target
                if (card.Targets != null && card.Targets.Count > 0)
                {
                    // Example: target the highest health enemy minion
                    Card bestTarget = null;
                    foreach (var t in card.Targets)
                    {
                        if (!t.IsFriend)
                            if (bestTarget == null || t.CurrentHealth > bestTarget.CurrentHealth)
                                bestTarget = t;
                    }

                    if (bestTarget != null)
                        return ClickInstruction.PlayCard(card.Id, targetEntityId: bestTarget.Id);
                }

                // Non-targeted spell
                return ClickInstruction.PlayCard(card.Id);
            }

            if (card.Type == Card.CType.WEAPON)
            {
                return ClickInstruction.PlayCard(card.Id);
            }

            if (card.Type == Card.CType.LOCATION)
            {
                // Location cards need a target to activate
                if (board.MinionEnemy.Count > 0)
                    return ClickInstruction.UseLocation(card.Id, board.MinionEnemy[0].Id);
            }
        }

        // --- 3. Attack with minions ---
        foreach (var minion in board.MinionFriend)
        {
            if (!minion.CanAttack)
                continue;

            // Find the best target
            // Priority: taunt minions > enemy hero
            Card target = null;

            // Must attack taunt minions first
            foreach (var enemy in board.MinionEnemy)
                if (enemy.IsTaunt)
                {
                    target = enemy;
                    break;
                }

            // If no taunt, go face
            if (target == null)
                target = board.HeroEnemy;

            return ClickInstruction.Attack(minion.Id, target.Id);
        }

        // --- 4. Attack with weapon ---
        if (board.WeaponFriend != null && board.HeroFriend.CanAttack)
        {
            Card target = null;

            foreach (var enemy in board.MinionEnemy)
                if (enemy.IsTaunt)
                {
                    target = enemy;
                    break;
                }

            if (target == null)
                target = board.HeroEnemy;

            return ClickInstruction.Attack(board.HeroFriend.Id, target.Id);
        }

        // --- 5. Use hero power ---
        if (!_usedHeroPower && board.Ability != null && board.Ability.CurrentCost <= board.ManaAvailable)
        {
            _usedHeroPower = true;

            // Targeted hero powers (Mage, Priest, Druid, etc.)
            if (board.FriendClass == Card.CClass.MAGE)
                return ClickInstruction.HeroPower(board.HeroEnemy.Id);

            if (board.FriendClass == Card.CClass.PRIEST)
                return ClickInstruction.HeroPower(board.HeroFriend.Id);

            // Non-targeted hero powers
            return ClickInstruction.HeroPower();
        }

        // --- 6. End turn ---
        _usedHeroPower = false;
        return ClickInstruction.End();
    }
}
