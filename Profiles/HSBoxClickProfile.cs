using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Plugins.API;
using SmartBotProfiles;
using Newtonsoft.Json.Linq;

/// <summary>
/// ClickProfile that reads HSBox recommendations from HSAng memory (via Bot._hsAngRecommendations)
/// and translates them into ClickInstructions for Ranked/Constructed mode.
/// </summary>
[Serializable]
public class HSBoxClickProfile : ClickProfile
{
    private static string _lastActedJson;
    private static int _waitCount;
    private static int _lastTurn = -1;
    private const int MaxWaitBeforeEndTurn = 50; // ~50 × 200ms = 10s of no new rec → auto end turn

    public ClickInstruction GetNextClick(Board board)
    {
        try
        {
            if (board == null)
            {
                Log("[HSBox-DBG] GetNextClick: board is null");
                return ClickInstruction.Wait(500);
            }
            SmartBot.Plugins.API.Bot._hsAngGameMode = "ranked";
            SmartBot.Plugins.API.Bot._hsAngCurrentTurn = board.TurnCount;
            if (!board.IsOwnTurn)
            {
                _waitCount = 0;
                _lastTurn = -1;
                return ClickInstruction.Wait(500);
            }

            var json = SmartBot.Plugins.API.Bot._hsAngRecommendations;
            Log("[HSBox-DBG] GetNextClick: jsonNull=" + (json == null) + " jsonLen=" + (json?.Length ?? 0)
                + " sameAsLast=" + (json == _lastActedJson) + " waitCount=" + _waitCount);

            if (string.IsNullOrEmpty(json))
            {
                return ClickInstruction.Wait(500);
            }

            // Parse
            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (Exception pe)
            {
                Log("[HSBox-WARN] JSON parse failed: " + pe.Message);
                return ClickInstruction.Wait(500);
            }

            var recs = obj["rec"] as JArray;
            if (recs == null || recs.Count == 0)
            {
                Log("[HSBox-DBG] GetNextClick: no recs in JSON");
                return ClickInstruction.Wait(500);
            }

            var firstOption = recs[0]["option"]?.Value<string>() ?? "";

            // Handle choice — don't mark as acted
            if (firstOption == "choice")
            {
                var choiceCardId = recs[0]["cardId"]?.Value<string>() ?? "";
                int choicePos = recs[0]["position"]?.Value<int>() ?? 0;
                Log("[HSBox] Choice recommendation: cardId=" + choiceCardId + " pos=" + choicePos);

                if (choicePos > 0)
                {
                    Log("[HSBox] >> ChooseByIndex(" + (choicePos - 1) + ") cardId=" + choiceCardId);
                    return ClickInstruction.ChooseByIndex(choicePos - 1);
                }
                Log("[HSBox] >> ChooseByIndex(0) fallback");
                return ClickInstruction.ChooseByIndex(0);
            }

            // Handle mulligan/replace — HSBox sends card positions to mulligan away
            // Mulligan is handled by SmartBot's own mulligan system, not by ClickProfile.
            // Just log and wait — the click profile isn't called during mulligan phase anyway.
            if (firstOption == "replace")
            {
                var cardIdsStr = recs[0]["cardIds"]?.Value<string>() ?? "";
                var positionsStr = recs[0]["positions"]?.Value<string>() ?? "";
                Log("[HSBox] Mulligan rec (info only, handled by mulligan system): cardIds=" + cardIdsStr + " positions=" + positionsStr);
                _lastActedJson = json;
                return ClickInstruction.Wait(500);
            }

            // New turn → reset dedup so stale rec is re-evaluated against the new board state.
            // Without this, a stale rec from a previous game/turn causes perpetual DoNothing
            // because sameAsLast stays true until the bridge writes a new rec (which may never happen
            // if _lastBridgeTurn is stuck at a high value from the previous game).
            if (board.TurnCount != _lastTurn)
            {
                Log("[HSBox-DBG] Turn changed: " + _lastTurn + " -> " + board.TurnCount + ", resetting dedup");
                _lastActedJson = null;
                _waitCount = 0;
                _lastTurn = board.TurnCount;
            }

            // Dedup — same recommendation already acted on, poll quickly for next rec
            if (json == _lastActedJson)
            {
                _waitCount++;
                if (_waitCount >= MaxWaitBeforeEndTurn)
                {
                    Log("[HSBox] No new recommendation for " + _waitCount + " polls, auto end turn");
                    _waitCount = 0;
                    return ClickInstruction.End();
                }
                return ClickInstruction.Wait(200);
            }

            // New recommendation — act on it
            _waitCount = 0;
            _lastActedJson = json;

            var rec = recs[0];
            var option = rec["option"]?.Value<string>() ?? "";
            var cardId = rec["cardId"]?.Value<string>() ?? "";
            int position = rec["position"]?.Value<int>() ?? 0;
            var zoneName = rec["zoneName"]?.Value<string>() ?? "";
            var targetCardId = rec["target_cardId"]?.Value<string>() ?? "";
            int targetPos = rec["target_position"]?.Value<int>() ?? 0;
            var targetKind = rec["target_kind"]?.Value<string>() ?? "";
            var subOption = rec["subOption"]?.Value<string>() ?? "";

            Log("[HSBox] Action: " + option
                + " cardId=" + cardId + " pos=" + position + " zone=" + zoneName
                + (!string.IsNullOrEmpty(targetCardId) ? " -> " + targetCardId : "")
                + (!string.IsNullOrEmpty(targetKind) ? " kind=" + targetKind : "")
                + (targetPos > 0 ? " tgtPos=" + targetPos : "")
                + (!string.IsNullOrEmpty(subOption) ? " subOption=" + subOption : ""));

            // Log full board context for debugging
            LogBoardState(board);

            var instruction = BuildInstruction(board, option, cardId, position, zoneName, targetCardId, targetPos, targetKind);

            // Pass Choose One sub-option cardId to the remote module for choose-one resolution
            if (instruction != null && !string.IsNullOrEmpty(subOption))
            {
                instruction.SubOption = subOption;

                // For Choose One cards with a target: play WITHOUT target first,
                // let ChoiceAction resolve the sub-option, then apply target after.
                // Store the target in PositionY for post-choice targeting.
                if (instruction.TargetEntityId != 0)
                {
                    Log("[HSBox-DBG] SubOption+Target: subOption=" + subOption + " deferring targetId=" + instruction.TargetEntityId + " to post-choice");
                    instruction.PositionY = instruction.TargetEntityId;
                    instruction.TargetEntityId = 0;
                }
                else
                {
                    Log("[HSBox-DBG] SubOption: " + subOption);
                }
            }

            return instruction;
        }
        catch (Exception e)
        {
            Log("[HSBox] Error: " + e.Message + "\n" + e.StackTrace);
            return ClickInstruction.Wait(500);
        }
    }

    private void LogBoardState(Board board)
    {
        try
        {
            var sb = new System.Text.StringBuilder("[HSBox-DBG] Board: ");
            sb.Append("mana=").Append(board.ManaAvailable).Append("/").Append(board.MaxMana);
            sb.Append(" hand=[");
            if (board.Hand != null)
                foreach (var c in board.Hand)
                    if (c?.Template != null) sb.Append(c.Template.Id).Append(":").Append(c.Id).Append("(cost=").Append(c.CurrentCost).Append("),");
            sb.Append("] friendly=[");
            if (board.MinionFriend != null)
                foreach (var c in board.MinionFriend)
                    if (c?.Template != null) sb.Append(c.Template.Id).Append(":").Append(c.Id).Append(",");
            sb.Append("] enemy=[");
            if (board.MinionEnemy != null)
                foreach (var c in board.MinionEnemy)
                    if (c?.Template != null) sb.Append(c.Template.Id).Append(":").Append(c.Id).Append(",");
            sb.Append("]");
            if (board.HeroFriend != null) sb.Append(" myHero=").Append(board.HeroFriend.Template?.Id).Append(":").Append(board.HeroFriend.Id);
            if (board.HeroEnemy != null) sb.Append(" enemyHero=").Append(board.HeroEnemy.Template?.Id).Append(":").Append(board.HeroEnemy.Id);
            if (board.Ability != null) sb.Append(" ability=").Append(board.Ability.Template?.Id).Append(":").Append(board.Ability.Id);
            Log(sb.ToString());
        }
        catch { }
    }

    private ClickInstruction BuildInstruction(Board board, string option, string cardId,
        int position, string zoneName, string targetCardId, int targetPos, string targetKind)
    {
        ClickInstruction result = null;

        switch (option)
        {
            case "play":
            case "play_minion":
            case "play_card":
            case "play_spell":
            case "play_weapon":
            case "play_special":
                result = BuildPlayCard(board, cardId, position, zoneName, targetCardId, targetPos, targetKind);
                break;

            case "minion_attack":
            case "attack":
            case "hero_attack":
                result = BuildAttack(board, cardId, position, targetCardId, targetPos, targetKind);
                break;

            case "hero_skill":
            case "heropower":
            case "hero_power":
            case "titan_power":
                result = BuildHeroPower(board, targetCardId, targetPos, targetKind);
                break;

            case "end_turn":
            case "endturn":
                Log("[HSBox] >> EndTurn");
                return ClickInstruction.End();

            case "use_location":
            case "location":
            case "activate_location":
            case "location_power":
                result = BuildLocation(board, cardId, position, targetCardId, targetPos, targetKind);
                break;

            case "trade":
                var tradeCard = FindHandCard(board, cardId, position);
                if (tradeCard != null)
                {
                    Log("[HSBox] >> Trade entityId=" + tradeCard.Id);
                    return ClickInstruction.TradeMinion(tradeCard.Id);
                }
                Log("[HSBox-WARN] Trade: card not found");
                return ClickInstruction.Wait(500);

            case "forge":
                var forgeCard = FindHandCard(board, cardId, position);
                if (forgeCard != null)
                {
                    Log("[HSBox] >> Forge entityId=" + forgeCard.Id);
                    return ClickInstruction.ForgeCard(forgeCard.Id);
                }
                Log("[HSBox-WARN] Forge: card not found");
                return ClickInstruction.Wait(500);

            default:
                Log("[HSBox-WARN] Unknown action: " + option);
                return ClickInstruction.Wait(500);
        }

        if (result == null)
        {
            Log("[HSBox-WARN] BuildInstruction returned null for " + option);
            return ClickInstruction.Wait(500);
        }
        return result;
    }

    #region Action Builders

    private ClickInstruction BuildPlayCard(Board board, string cardId, int position,
        string zoneName, string targetCardId, int targetPos, string targetKind)
    {
        var card = FindHandCard(board, cardId, position);
        if (card == null)
        {
            Log("[HSBox-WARN] PlayCard: card " + cardId + " pos=" + position + " NOT FOUND in hand ("
                + (board.Hand?.Count ?? 0) + " cards)");
            return null;
        }

        Log("[HSBox-DBG] PlayCard: found " + card.Template?.Id + " entityId=" + card.Id
            + " type=" + card.Type + " cost=" + card.CurrentCost + " mana=" + board.ManaAvailable);

        if (card.CurrentCost > board.ManaAvailable)
        {
            Log("[HSBox-WARN] PlayCard: not enough mana (cost=" + card.CurrentCost + " mana=" + board.ManaAvailable + "), skipping");
            return null; // skip — HSAng recommended a card we can't afford (stale rec)
        }

        int boardIdx = board.MinionFriend != null ? board.MinionFriend.Count : 0;

        var target = FindTarget(board, targetCardId, targetPos, targetKind);

        if (target != null)
        {
            Log("[HSBox] >> PlayCard entityId=" + card.Id + " boardIdx=" + boardIdx + " targetId=" + target.Id
                + " (" + target.Template?.Id + ")");
            if (card.Type == Card.CType.MINION)
                return ClickInstruction.PlayCard(card.Id, boardIndex: boardIdx, targetEntityId: target.Id);
            return ClickInstruction.PlayCard(card.Id, targetEntityId: target.Id);
        }

        Log("[HSBox] >> PlayCard entityId=" + card.Id + " boardIdx=" + boardIdx + " (no target)");
        if (card.Type == Card.CType.MINION)
            return ClickInstruction.PlayCard(card.Id, boardIndex: boardIdx);

        return ClickInstruction.PlayCard(card.Id);
    }

    private ClickInstruction BuildAttack(Board board, string cardId, int position,
        string targetCardId, int targetPos, string targetKind)
    {
        Card source = null;

        // Primary: match by position first (handles duplicate cardIds)
        if (position > 0 && board.MinionFriend != null && position <= board.MinionFriend.Count)
        {
            var byPos = board.MinionFriend[position - 1];
            // Accept if position matches AND cardId matches (or cardId not specified)
            if (byPos != null)
            {
                if (string.IsNullOrEmpty(cardId))
                    source = byPos;
                else
                {
                    Card.Cards cid;
                    if (Enum.TryParse(cardId, out cid) && byPos.Template?.Id == cid)
                        source = byPos;
                }
            }
        }

        // Fallback: by cardId (for hero attacks or when position doesn't match)
        if (source == null && !string.IsNullOrEmpty(cardId))
        {
            Card.Cards cid;
            if (Enum.TryParse(cardId, out cid))
            {
                if (board.HeroFriend?.Template?.Id == cid)
                    source = board.HeroFriend;
                else if (board.MinionFriend != null)
                    source = board.MinionFriend.FirstOrDefault(c => c?.Template?.Id == cid);
            }
        }

        // Last resort: by position only
        if (source == null && position > 0 && board.MinionFriend != null && position <= board.MinionFriend.Count)
            source = board.MinionFriend[position - 1];

        if (source == null)
        {
            Log("[HSBox-WARN] Attack: source " + cardId + " pos=" + position + " NOT FOUND"
                + " (friendly minions=" + (board.MinionFriend?.Count ?? 0) + ")");
            return null;
        }

        var target = FindTarget(board, targetCardId, targetPos, targetKind);
        if (target == null)
            target = board.HeroEnemy;

        if (target != null)
        {
            Log("[HSBox] >> Attack sourceId=" + source.Id + " (" + source.Template?.Id + ")"
                + " -> targetId=" + target.Id + " (" + target.Template?.Id + ")");
            return ClickInstruction.Attack(source.Id, target.Id);
        }

        Log("[HSBox-WARN] Attack: no target found");
        return null;
    }

    private ClickInstruction BuildHeroPower(Board board, string targetCardId, int targetPos, string targetKind)
    {
        if (board.Ability == null)
        {
            Log("[HSBox-WARN] HeroPower: ability is null");
            return null;
        }

        var target = FindTarget(board, targetCardId, targetPos, targetKind);
        if (target != null)
        {
            Log("[HSBox] >> HeroPower targetId=" + target.Id + " (" + target.Template?.Id + ")");
            return ClickInstruction.HeroPower(target.Id);
        }

        Log("[HSBox] >> HeroPower (no target)");
        return ClickInstruction.HeroPower();
    }

    private ClickInstruction BuildLocation(Board board, string cardId, int position,
        string targetCardId, int targetPos, string targetKind)
    {
        Card loc = null;
        if (!string.IsNullOrEmpty(cardId) && board.MinionFriend != null)
        {
            Card.Cards cid;
            if (Enum.TryParse(cardId, out cid))
                loc = board.MinionFriend.FirstOrDefault(c => c?.Template?.Id == cid);
        }
        if (loc == null && position > 0 && board.MinionFriend != null && position <= board.MinionFriend.Count)
            loc = board.MinionFriend[position - 1];

        var target = FindTarget(board, targetCardId, targetPos, targetKind);

        if (loc != null && target != null)
        {
            Log("[HSBox] >> UseLocation locId=" + loc.Id + " (" + loc.Template?.Id + ")"
                + " targetId=" + target.Id + " (" + target.Template?.Id + ")");
            return ClickInstruction.UseLocation(loc.Id, target.Id);
        }

        if (loc != null && target == null)
        {
            // Location without target (passive activation)
            Log("[HSBox] >> UseLocation locId=" + loc.Id + " (" + loc.Template?.Id + ") (no target)");
            return ClickInstruction.UseLocation(loc.Id, 0);
        }

        Log("[HSBox-WARN] Location: loc=" + (loc != null ? loc.Template?.Id.ToString() : "null")
            + " target=" + (target != null ? target.Template?.Id.ToString() : "null"));
        return null;
    }

    #endregion

    #region Card Resolution

    private Card FindHandCard(Board board, string cardId, int position)
    {
        if (board.Hand == null || board.Hand.Count == 0) return null;

        Card.Cards cid = default;
        bool hasCid = !string.IsNullOrEmpty(cardId) && Enum.TryParse(cardId, out cid);

        // 1. Match by cardId + position (handles duplicate cardIds)
        if (hasCid && position >= 1 && position <= board.Hand.Count)
        {
            var byPos = board.Hand[position - 1];
            if (byPos?.Template?.Id == cid)
            {
                Log("[HSBox-DBG] FindHandCard: matched by cardId+position=" + position + " -> entityId=" + byPos.Id);
                return byPos;
            }
        }

        // 2. Fallback: cardId only (first match)
        if (hasCid)
        {
            var match = board.Hand.FirstOrDefault(c => c?.Template?.Id == cid);
            if (match != null)
            {
                Log("[HSBox-DBG] FindHandCard: matched by cardId=" + cardId + " -> entityId=" + match.Id);
                return match;
            }
            Log("[HSBox-DBG] FindHandCard: cardId=" + cardId + " parsed but not in hand");
        }
        else if (!string.IsNullOrEmpty(cardId))
        {
            Log("[HSBox-DBG] FindHandCard: cardId=" + cardId + " failed to parse as Card.Cards enum");
        }

        // 3. Fallback: position only
        if (position >= 1 && position <= board.Hand.Count)
        {
            var byPos = board.Hand[position - 1];
            Log("[HSBox-DBG] FindHandCard: matched by position=" + position + " -> " + byPos?.Template?.Id + " entityId=" + byPos?.Id);
            return byPos;
        }

        return null;
    }

    private Card FindTarget(Board board, string targetCardId, int targetPos, string targetKind)
    {
        if (string.IsNullOrEmpty(targetCardId) && targetPos <= 0 && string.IsNullOrEmpty(targetKind))
            return null;

        // Check target kind first
        if (!string.IsNullOrEmpty(targetKind))
        {
            var k = targetKind.ToLowerInvariant();
            if (k.Contains("enemy_hero") || k.Contains("opp_hero"))
            {
                Log("[HSBox-DBG] FindTarget: targetKind=" + targetKind + " -> enemy hero " + board.HeroEnemy?.Template?.Id + " id=" + board.HeroEnemy?.Id);
                return board.HeroEnemy;
            }
            if (k.Contains("friendly_hero") || k.Contains("own_hero"))
            {
                Log("[HSBox-DBG] FindTarget: targetKind=" + targetKind + " -> friendly hero");
                return board.HeroFriend;
            }
        }

        Card.Cards cid = default;
        bool hasCid = !string.IsNullOrEmpty(targetCardId) && Enum.TryParse(targetCardId, out cid);

        if (hasCid && targetCardId.StartsWith("HERO_"))
        {
            if (board.HeroEnemy?.Template?.Id == cid)
            {
                Log("[HSBox-DBG] FindTarget: hero cardId match -> enemy hero id=" + board.HeroEnemy.Id);
                return board.HeroEnemy;
            }
            if (board.HeroFriend?.Template?.Id == cid)
            {
                Log("[HSBox-DBG] FindTarget: hero cardId match -> friendly hero");
                return board.HeroFriend;
            }
        }

        // Enemy minion: try cardId+position first, then cardId only, then position only
        if (board.MinionEnemy != null && hasCid && targetPos >= 1 && targetPos <= board.MinionEnemy.Count)
        {
            var byPos = board.MinionEnemy[targetPos - 1];
            if (byPos?.Template?.Id == cid)
            {
                Log("[HSBox-DBG] FindTarget: enemy minion by cardId+pos=" + targetPos + " -> id=" + byPos.Id);
                return byPos;
            }
        }
        if (board.MinionEnemy != null && hasCid)
        {
            var m = board.MinionEnemy.FirstOrDefault(c => c?.Template?.Id == cid);
            if (m != null)
            {
                Log("[HSBox-DBG] FindTarget: enemy minion " + cid + " -> id=" + m.Id);
                return m;
            }
        }
        if (board.MinionEnemy != null && targetPos >= 1 && targetPos <= board.MinionEnemy.Count)
        {
            var m = board.MinionEnemy[targetPos - 1];
            Log("[HSBox-DBG] FindTarget: enemy minion by pos=" + targetPos + " -> " + m?.Template?.Id + " id=" + m?.Id);
            return m;
        }

        // Friendly minion: same pattern
        if (board.MinionFriend != null && hasCid && targetPos >= 1 && targetPos <= board.MinionFriend.Count)
        {
            var byPos = board.MinionFriend[targetPos - 1];
            if (byPos?.Template?.Id == cid)
            {
                Log("[HSBox-DBG] FindTarget: friendly minion by cardId+pos=" + targetPos + " -> id=" + byPos.Id);
                return byPos;
            }
        }
        if (board.MinionFriend != null && hasCid)
        {
            var m = board.MinionFriend.FirstOrDefault(c => c?.Template?.Id == cid);
            if (m != null)
            {
                Log("[HSBox-DBG] FindTarget: friendly minion " + cid + " -> id=" + m.Id);
                return m;
            }
        }

        // Hand card targeting (for discard battlecries like 魔眼秘术师)
        if (board.Hand != null && hasCid)
        {
            var m = board.Hand.FirstOrDefault(c => c?.Template?.Id == cid);
            if (m != null)
            {
                Log("[HSBox-DBG] FindTarget: hand card " + cid + " -> id=" + m.Id);
                return m;
            }
        }

        Log("[HSBox-DBG] FindTarget: no match for cardId=" + targetCardId + " pos=" + targetPos + " kind=" + targetKind);
        return null;
    }

    #endregion

    private void Log(string message)
    {
        try
        {
            if (message.Contains("[HSBox-DBG]"))
                SmartBot.Plugins.API.Bot.LogDebug(message);
            else
                SmartBot.Plugins.API.Bot.Log(message);
        }
        catch { }
    }
}
