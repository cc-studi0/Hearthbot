using System;
using System.Collections.Generic;

namespace BotMain
{
    internal sealed class MulliganProtocolChoice
    {
        public string CardId { get; set; }
        public int EntityId { get; set; }
    }

    internal sealed class MulliganProtocolSnapshot
    {
        public int OwnClass { get; set; }
        public int EnemyClass { get; set; }
        public bool HasCoin { get; set; }
        public List<MulliganProtocolChoice> Choices { get; } = new List<MulliganProtocolChoice>();
    }

    internal static class MulliganProtocol
    {
        internal static bool IsTransientFailure(string result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return true;

            var normalized = result.ToLowerInvariant();
            return normalized.Contains("waiting_for_cards")
                || normalized.Contains("waiting_for_ready")
                || normalized.Contains("waiting_for_user_input")
                || normalized.Contains("friendly_choices_not_ready")
                || normalized.Contains("response_packet_blocked")
                || normalized.Contains("response_mode_not_choice")
                || normalized.Contains("input_not_ready")
                || normalized.Contains("mulligan_not_active")
                || normalized.Contains("starting_cards_not_ready")
                || normalized.Contains("starting_cards_empty")
                || normalized.Contains("starting_cards_entity_not_ready")
                || normalized.Contains("starting_cards_cardid_not_ready")
                || normalized.Contains("marked_state_not_ready")
                || normalized.Contains("game_state_not_available")
                || normalized.Contains("choice_entities_not_ready")
                || normalized.Contains("choice_id_not_ready")
                || normalized.Contains("entity_not_found")
                || normalized.Contains("entity_set_changed")
                || normalized.Contains("wait:mulligan_manager")
                || normalized.Contains("wait:mulligan_ready")
                || normalized.Contains("wait:choice_packet")
                || normalized.Contains("wait:mouse_fallback");
        }

        internal static bool TryParseState(string payload, out MulliganProtocolSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;

            if (string.IsNullOrWhiteSpace(payload))
            {
                error = "mulligan payload empty";
                return false;
            }

            var parts = payload.Split('|');
            if (parts.Length < 2)
            {
                error = "mulligan payload format invalid";
                return false;
            }

            int ownClass;
            int enemyClass;
            if (!int.TryParse(parts[0], out ownClass) || !int.TryParse(parts[1], out enemyClass))
            {
                error = "mulligan class parse failed";
                return false;
            }

            snapshot = new MulliganProtocolSnapshot
            {
                OwnClass = ownClass,
                EnemyClass = enemyClass
            };

            if (parts.Length >= 4)
            {
                snapshot.HasCoin = string.Equals(parts[3], "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parts[3], "true", StringComparison.OrdinalIgnoreCase);
            }

            if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[2]))
                return true;

            var cardEntries = parts[2].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var cardEntry in cardEntries)
            {
                var pair = cardEntry.Split(',');
                if (pair.Length != 2) continue;

                int entityId;
                if (!int.TryParse(pair[1], out entityId) || entityId <= 0) continue;

                snapshot.Choices.Add(new MulliganProtocolChoice
                {
                    CardId = pair[0],
                    EntityId = entityId
                });
            }

            return true;
        }
    }
}
