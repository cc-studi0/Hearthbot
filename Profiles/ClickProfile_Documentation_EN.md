# ClickProfile System — Author's Guide

## Overview

The **ClickProfile** system lets you write profiles that control the bot **one click at a time**, instead of relying on the built-in AI engine. When a ClickProfile is selected, the bot bypasses the AI backend entirely. Instead, every time the board state updates, your profile's `GetNextClick()` method is called, and you return a single instruction — a click, a game action, or a "wait" signal. The bot executes it, then calls you again with the updated board.

This creates a simple polling loop:

```
Board state parsed → GetNextClick(board) called → Instruction executed → Board re-parsed → repeat
```

---

## Getting Started

Create a `.cs` file in the `Profiles/` directory. Your class must implement the `ClickProfile` interface:

```csharp
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class MyProfile : ClickProfile
{
    public ClickInstruction GetNextClick(Board board)
    {
        // Your logic here
        return ClickInstruction.End();
    }
}
```

The file is compiled at runtime — no rebuild needed. It will appear in the profile dropdown alongside regular profiles.

---

## The ClickInstruction API

`GetNextClick()` must return a `ClickInstruction`. Use the static factory methods below to create one.

### Three Levels of Control

| Level | Methods | Description |
|-------|---------|-------------|
| **Raw** | `RawClick`, `RawClickAt` | Bare mouse clicks. You control every single click manually. |
| **High-level** | `PlayCard`, `Attack`, `HeroPower`, `Choose`, `UseLocation`, `TradeMinion`, `ForgeCard` | Complete game actions. One call = one full action. |
| **Control** | `End`, `ConcedeGame`, `Wait` | Flow control — end turn, concede, or wait and re-poll. |

You can mix all levels freely in the same profile.

---

### Raw Click Methods

#### `ClickInstruction.RawClick(int entityId)`

Clicks on a game entity by its ID. This is the most basic operation — it simply clicks the entity, no interpretation.

Use this for manual multi-step sequences. For example, to attack with a minion:
- **Poll 1**: `RawClick(attackerId)` — picks up the minion
- **Poll 2**: `RawClick(targetId)` — drops it on the target, completing the attack

```csharp
// Click on a specific entity
return ClickInstruction.RawClick(board.MinionFriend[0].Id);
```

#### `ClickInstruction.RawClickAt(int x, int y)`

Clicks at a specific screen position (pixel coordinates).

```csharp
// Click at screen coordinates
return ClickInstruction.RawClickAt(500, 400);
```

---

### High-Level Game Action Methods

These execute a complete game action in a single instruction. The bot handles all the mouse movements internally.

#### `ClickInstruction.PlayCard(int entityId, int boardIndex = 0, int targetEntityId = 0)`

Plays a card from hand.
- `entityId` — The card's `Id` from `board.Hand`
- `boardIndex` — Board position for minion placement (0-based, left to right)
- `targetEntityId` — Target entity ID for targeted spells/battlecries (0 = no target)

```csharp
// Play a minion to board position 2
return ClickInstruction.PlayCard(card.Id, boardIndex: 2);

// Play a targeted spell on an enemy minion
return ClickInstruction.PlayCard(spell.Id, targetEntityId: board.MinionEnemy[0].Id);

// Play a minion with a targeted battlecry
return ClickInstruction.PlayCard(card.Id, boardIndex: 0, targetEntityId: board.HeroFriend.Id);
```

#### `ClickInstruction.Attack(int attackerEntityId, int targetEntityId)`

Attacks with a minion or hero weapon.

```csharp
// Attack enemy hero with a minion
return ClickInstruction.Attack(board.MinionFriend[0].Id, board.HeroEnemy.Id);

// Attack an enemy minion
return ClickInstruction.Attack(board.MinionFriend[0].Id, board.MinionEnemy[0].Id);
```

#### `ClickInstruction.HeroPower(int targetEntityId = 0)`

Uses the hero power. Pass a target entity ID for targeted hero powers (Mage, Priest, etc.), or 0 for non-targeted ones.

```csharp
// Non-targeted hero power (Paladin, Warlock, etc.)
return ClickInstruction.HeroPower();

// Targeted hero power (Mage ping, Priest heal, etc.)
return ClickInstruction.HeroPower(board.MinionEnemy[0].Id);
```

#### `ClickInstruction.Choose(int entityId)`

Picks a discover or choice option by entity ID.

```csharp
return ClickInstruction.Choose(choiceEntityId);
```

#### `ClickInstruction.ChooseByIndex(int index)`

Picks a discover or choice option by its position index (0-based, left to right).

```csharp
// Pick the first (leftmost) option
return ClickInstruction.ChooseByIndex(0);
```

#### `ClickInstruction.UseLocation(int locationEntityId, int targetEntityId)`

Activates a Location card on a target.

```csharp
return ClickInstruction.UseLocation(locationCard.Id, board.MinionEnemy[0].Id);
```

#### `ClickInstruction.TradeMinion(int entityId)`

Trades a minion (Tradeable keyword).

```csharp
return ClickInstruction.TradeMinion(card.Id);
```

#### `ClickInstruction.ForgeCard(int entityId)`

Forges a card (Forge keyword).

```csharp
return ClickInstruction.ForgeCard(card.Id);
```

---

### Control Methods

#### `ClickInstruction.End()`

Ends your turn.

```csharp
return ClickInstruction.End();
```

#### `ClickInstruction.ConcedeGame()`

Concedes the game.

```csharp
return ClickInstruction.ConcedeGame();
```

#### `ClickInstruction.Wait(int ms = 500)`

Does nothing — waits the specified milliseconds then re-polls `GetNextClick()` with the current board state. Use this when you're waiting for something to happen (animations, game state to update, etc.).

```csharp
// Wait 1 second then get called again
return ClickInstruction.Wait(1000);
```

---

## The Board Object

`GetNextClick()` receives a `Board` object representing the current game state. Here are the most important properties:

### Cards & Entities

| Property | Type | Description |
|----------|------|-------------|
| `Hand` | `List<Card>` | Cards in your hand |
| `MinionFriend` | `List<Card>` | Your minions on the board |
| `MinionEnemy` | `List<Card>` | Enemy minions on the board |
| `HeroFriend` | `Card` | Your hero |
| `HeroEnemy` | `Card` | Enemy hero |
| `WeaponFriend` | `Card` | Your equipped weapon (null if none) |
| `WeaponEnemy` | `Card` | Enemy's equipped weapon (null if none) |
| `Ability` | `Card` | Your hero power |
| `EnemyAbility` | `Card` | Enemy hero power |
| `Secret` | `List<Card.Cards>` | Your active secrets |
| `Deck` | `List<Card.Cards>` | Known cards remaining in your deck |

### Mana & Resources

| Property | Type | Description |
|----------|------|-------------|
| `ManaAvailable` | `int` | Current available mana |
| `MaxMana` | `int` | Maximum mana this turn |
| `LockedMana` | `int` | Locked (overloaded) mana |
| `OverloadedMana` | `int` | Mana that will be locked next turn |

### Game State

| Property | Type | Description |
|----------|------|-------------|
| `TurnCount` | `int` | Current turn number |
| `IsOwnTurn` | `bool` | Whether it's your turn |
| `IsCombo` | `bool` | Whether combo is active (a card was played this turn) |
| `EnemyCardCount` | `int` | Number of cards in enemy hand |
| `FriendDeckCount` | `int` | Cards remaining in your deck |
| `EnemyDeckCount` | `int` | Cards remaining in enemy deck |
| `SecretEnemy` | `bool` | Whether enemy has secrets |
| `SecretEnemyCount` | `int` | Number of enemy secrets |
| `FriendClass` | `Card.CClass` | Your hero class |
| `EnemyClass` | `Card.CClass` | Enemy hero class |

### Helper Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `HasCardInHand(Card.Cards id)` | `bool` | Check if a specific card is in your hand |
| `HasCardOnBoard(Card.Cards id, bool side)` | `bool` | Check if a card is on the board (side=true for friendly) |
| `HasWeapon(bool side)` | `bool` | Check if a weapon is equipped |
| `GetManaAvailableNextTurn()` | `int` | Predicted mana next turn |

---

## The Card Object

Each card (in hand, on board, heroes, weapons) is a `Card` with these key properties:

### Identity

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `int` | Unique entity ID for this game (use this in ClickInstruction methods) |
| `Template` | `CardTemplate` | Card template with static card data |
| `Type` | `Card.CType` | MINION, SPELL, WEAPON, HERO, LOCATION |
| `Race` | `Card.CRace` | Minion race (MURLOC, DRAGON, BEAST, etc.) |

### Stats

| Property | Type | Description |
|----------|------|-------------|
| `CurrentCost` | `int` | Current mana cost |
| `CurrentAtk` | `int` | Current attack value |
| `CurrentHealth` | `int` | Current health |
| `MaxHealth` | `int` | Maximum health |
| `CurrentArmor` | `int` | Current armor (heroes) |

### State & Mechanics

| Property | Type | Description |
|----------|------|-------------|
| `CanAttack` | `bool` | Whether this minion/hero can attack right now |
| `IsTaunt` | `bool` | Has Taunt |
| `IsDivineShield` | `bool` | Has Divine Shield |
| `IsCharge` | `bool` | Has Charge |
| `HasRush` | `bool` | Has Rush |
| `IsWindfury` | `bool` | Has Windfury |
| `IsFrozen` | `bool` | Is Frozen |
| `IsStealth` | `bool` | Is Stealthed |
| `IsImmune` | `bool` | Is Immune |
| `HasPoison` | `bool` | Has Poisonous |
| `IsLifeSteal` | `bool` | Has Lifesteal |
| `HasDeathRattle` | `bool` | Has Deathrattle |
| `HasReborn` | `bool` | Has Reborn |
| `IsSilenced` | `bool` | Has been Silenced |
| `IsTired` | `bool` | Just played (summoning sickness) |
| `SpellPower` | `int` | Spell Damage bonus |
| `NumTurnsInPlay` | `int` | Turns this minion has been on the board |
| `PlayedThisTurn` | `bool` | Was played this turn |
| `IsFriend` | `bool` | Is a friendly entity |

---

## Complete Examples

### Example 1: Simple Face Profile (High-Level)

Plays all cards, attacks face with everything, uses hero power.

```csharp
using System;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class SimpleFace : ClickProfile
{
    public ClickInstruction GetNextClick(Board board)
    {
        // Play cards from hand (cheapest first)
        foreach (var card in board.Hand)
        {
            if (card.CurrentCost <= board.ManaAvailable)
                return ClickInstruction.PlayCard(card.Id, boardIndex: board.MinionFriend.Count);
        }

        // Attack with all minions that can attack
        foreach (var minion in board.MinionFriend)
        {
            if (minion.CanAttack)
                return ClickInstruction.Attack(minion.Id, board.HeroEnemy.Id);
        }

        // Attack with weapon if equipped
        if (board.WeaponFriend != null && board.HeroFriend.CanAttack)
            return ClickInstruction.Attack(board.HeroFriend.Id, board.HeroEnemy.Id);

        // Use hero power
        if (board.Ability != null && board.Ability.CurrentCost <= board.ManaAvailable)
            return ClickInstruction.HeroPower();

        return ClickInstruction.End();
    }
}
```

### Example 2: Raw Click — Manual Two-Step Attack

Demonstrates the raw click mode where each mouse click is a separate instruction.

```csharp
using System;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class RawClickAttacker : ClickProfile
{
    private bool _holding = false;
    private int _holdingId = 0;

    public ClickInstruction GetNextClick(Board board)
    {
        if (!_holding)
        {
            // Step 1: Pick up a minion
            foreach (var minion in board.MinionFriend)
            {
                if (minion.CanAttack)
                {
                    _holding = true;
                    _holdingId = minion.Id;
                    return ClickInstruction.RawClick(minion.Id);
                }
            }
            return ClickInstruction.End();
        }
        else
        {
            // Step 2: Drop on target
            _holding = false;
            return ClickInstruction.RawClick(board.HeroEnemy.Id);
        }
    }
}
```

### Example 3: Trading Profile

Trades all Tradeable cards, then ends turn.

```csharp
using System;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class TraderProfile : ClickProfile
{
    public ClickInstruction GetNextClick(Board board)
    {
        // Trade any tradeable cards
        foreach (var card in board.Hand)
        {
            if (card.HasTag(Card.GAME_TAG.TRADEABLE) && card.GetTag(Card.GAME_TAG.TRADEABLE) == 1)
                return ClickInstruction.TradeMinion(card.Id);
        }

        // Then play remaining hand
        foreach (var card in board.Hand)
        {
            if (card.CurrentCost <= board.ManaAvailable)
                return ClickInstruction.PlayCard(card.Id, boardIndex: 0);
        }

        return ClickInstruction.End();
    }
}
```

### Example 4: Waiting and Polling

Demonstrates waiting for game state to change.

```csharp
using System;
using SmartBot.Plugins.API;
using SmartBotProfiles;

[Serializable]
public class WaitingProfile : ClickProfile
{
    private int _pollCount = 0;

    public ClickInstruction GetNextClick(Board board)
    {
        _pollCount++;

        // Wait 2 seconds before acting (4 polls at 500ms each)
        if (_pollCount <= 4)
            return ClickInstruction.Wait(500);

        // Now act
        _pollCount = 0;
        return ClickInstruction.End();
    }
}
```

---

## Important Notes

1. **Entity IDs**: Always use `card.Id` (the runtime entity ID), not card template IDs. Entity IDs are unique per game and per board state.

2. **State between calls**: Your profile instance persists across calls within a game, so you can use instance fields to track state between polls (like in the RawClick example).

3. **Null checks**: Always check for null before accessing cards. `board.WeaponFriend`, `board.WeaponEnemy`, etc. can be null.

4. **RawClick timing**: After a `RawClick`, the bot waits ~0.5 seconds before re-polling. After a high-level action (Attack, PlayCard, etc.), the bot waits for the game to fully process the action before re-polling.

5. **Returning null**: If `GetNextClick()` returns null, it is treated as `Wait(500)`.

6. **No rebuild needed**: Profile `.cs` files are compiled at runtime. Just save the file and refresh profiles in the bot UI.

7. **Mixing modes**: You can freely mix raw clicks, high-level actions, and control instructions in the same profile across different calls.
