using System;

namespace BotMain
{
    internal readonly struct HandActionCommandParts
    {
        public string ActionType { get; init; }
        public int SourceEntityId { get; init; }
        public int TargetEntityId { get; init; }
        public int Position { get; init; }
        public string SourceCardId { get; init; }
        public int SourceZonePosition { get; init; }
    }

    internal static class HandActionCommandMetadata
    {
        public static bool IsHandSourceAction(string actionType)
        {
            return string.Equals(actionType, "PLAY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionType, "TRADE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionType, "BG_PLAY", StringComparison.OrdinalIgnoreCase);
        }

        public static string AppendPlay(string command, string sourceCardId, int sourceZonePosition)
        {
            return string.IsNullOrWhiteSpace(sourceCardId) || sourceZonePosition <= 0
                ? command
                : command + "|" + sourceCardId + "|" + sourceZonePosition;
        }

        public static string AppendTrade(string command, string sourceCardId, int sourceZonePosition)
        {
            return string.IsNullOrWhiteSpace(sourceCardId) || sourceZonePosition <= 0
                ? command
                : command + "|" + sourceCardId + "|" + sourceZonePosition;
        }

        public static bool TryParse(string command, out HandActionCommandParts parts)
        {
            parts = default;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var segments = command.Split('|');
            if (segments.Length < 2)
                return false;

            var actionType = segments[0];
            if (!int.TryParse(segments[1], out var sourceEntityId))
                return false;

            if (string.Equals(actionType, "TRADE", StringComparison.OrdinalIgnoreCase))
            {
                parts = new HandActionCommandParts
                {
                    ActionType = actionType,
                    SourceEntityId = sourceEntityId,
                    SourceCardId = segments.Length > 2 ? segments[2] ?? string.Empty : string.Empty,
                    SourceZonePosition = segments.Length > 3 && int.TryParse(segments[3], out var tradeZonePos) ? tradeZonePos : 0
                };
                return true;
            }

            var targetEntityId = segments.Length > 2 && int.TryParse(segments[2], out var parsedTarget) ? parsedTarget : 0;
            var position = segments.Length > 3 && int.TryParse(segments[3], out var parsedPosition) ? parsedPosition : 0;
            var sourceCardId = segments.Length > 4 ? segments[4] ?? string.Empty : string.Empty;
            var sourceZonePosition = segments.Length > 5 && int.TryParse(segments[5], out var parsedSourceZonePos) ? parsedSourceZonePos : 0;

            parts = new HandActionCommandParts
            {
                ActionType = actionType,
                SourceEntityId = sourceEntityId,
                TargetEntityId = targetEntityId,
                Position = position,
                SourceCardId = sourceCardId,
                SourceZonePosition = sourceZonePosition
            };
            return true;
        }
    }
}
