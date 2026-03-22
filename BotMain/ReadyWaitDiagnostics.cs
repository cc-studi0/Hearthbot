using System;
using System.Collections.Generic;
using System.Linq;

namespace BotMain
{
    internal sealed class ReadyWaitDiagnosticState
    {
        public bool IsReady { get; set; }
        public string PrimaryReason { get; set; } = string.Empty;
        public IReadOnlyList<string> Flags { get; set; } = Array.Empty<string>();
        public int DrawEntityId { get; set; }
        public int DrawCount { get; set; }
    }

    internal static class ReadyWaitDiagnostics
    {
        internal const string ReadyReason = "ready";
        internal const string UnknownBusyReason = "unknown";
        private static readonly HashSet<string> DrawBlockingReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "friendly_draw",
            "pending_draw_task",
            "turn_start_draw_count"
        };
        private static readonly HashSet<string> ActionPostReadyBypassReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "response_packet_blocked",
            "input_denied",
            "blocking_power_processor",
            "power_processor_running",
            "hand_layout_updating",
            "hand_layout_dirty",
            "game_busy"
        };

        internal static string FormatReadyResponse()
        {
            return FormatResponse(true, ReadyReason, Array.Empty<string>());
        }

        internal static string FormatBusyResponse(
            string primaryReason,
            IEnumerable<string> flags,
            int drawEntityId = 0,
            int drawCount = 0)
        {
            return FormatResponse(false, primaryReason, flags, drawEntityId, drawCount);
        }

        internal static string FormatResponse(
            bool isReady,
            string primaryReason,
            IEnumerable<string> flags,
            int drawEntityId = 0,
            int drawCount = 0)
        {
            if (isReady)
                return "READY:reason=" + ReadyReason;

            var normalizedPrimaryReason = NormalizeBusyReason(primaryReason);
            var normalizedFlags = NormalizeFlags(flags, normalizedPrimaryReason);
            var parts = new List<string>
            {
                "reason=" + normalizedPrimaryReason,
                "flags=" + string.Join(",", normalizedFlags)
            };

            if (drawEntityId > 0)
                parts.Add("drawEntity=" + drawEntityId);

            if (drawCount > 0)
                parts.Add("drawCount=" + drawCount);

            return "BUSY:" + string.Join(";", parts);
        }

        internal static bool TryParseResponse(string response, out ReadyWaitDiagnosticState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(response))
                return false;

            if (string.Equals(response, "READY", StringComparison.OrdinalIgnoreCase))
            {
                state = new ReadyWaitDiagnosticState
                {
                    IsReady = true,
                    PrimaryReason = ReadyReason,
                    Flags = Array.Empty<string>()
                };
                return true;
            }

            if (string.Equals(response, "BUSY", StringComparison.OrdinalIgnoreCase))
            {
                state = new ReadyWaitDiagnosticState
                {
                    IsReady = false,
                    PrimaryReason = UnknownBusyReason,
                    Flags = new[] { UnknownBusyReason }
                };
                return true;
            }

            var separatorIndex = response.IndexOf(':');
            if (separatorIndex <= 0)
                return false;

            var stateToken = response.Substring(0, separatorIndex);
            var payload = response.Substring(separatorIndex + 1);
            var isReady = string.Equals(stateToken, "READY", StringComparison.OrdinalIgnoreCase);
            var isBusy = string.Equals(stateToken, "BUSY", StringComparison.OrdinalIgnoreCase);
            if (!isReady && !isBusy)
                return false;

            var parsedState = new ReadyWaitDiagnosticState
            {
                IsReady = isReady,
                PrimaryReason = isReady ? ReadyReason : UnknownBusyReason,
                Flags = isReady ? Array.Empty<string>() : new[] { UnknownBusyReason }
            };

            if (!string.IsNullOrWhiteSpace(payload))
            {
                foreach (var part in payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmedPart = part?.Trim() ?? string.Empty;
                    if (trimmedPart.Length == 0)
                        continue;

                    var equalsIndex = trimmedPart.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    var key = trimmedPart.Substring(0, equalsIndex).Trim();
                    var value = trimmedPart.Substring(equalsIndex + 1).Trim();
                    if (key.Equals("reason", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedState.PrimaryReason = isReady
                            ? ReadyReason
                            : NormalizeBusyReason(value);
                    }
                    else if (key.Equals("flags", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedState.Flags = ParseFlags(value, isReady, parsedState.PrimaryReason);
                    }
                    else if (key.Equals("drawEntity", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, out var drawEntityId) && drawEntityId > 0)
                            parsedState.DrawEntityId = drawEntityId;
                    }
                    else if (key.Equals("drawCount", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, out var drawCount) && drawCount > 0)
                            parsedState.DrawCount = drawCount;
                    }
                }
            }

            if (isReady)
            {
                parsedState.PrimaryReason = ReadyReason;
                parsedState.Flags = Array.Empty<string>();
            }
            else if (parsedState.Flags == null || parsedState.Flags.Count == 0)
            {
                parsedState.Flags = new[] { NormalizeBusyReason(parsedState.PrimaryReason) };
            }

            state = parsedState;
            return true;
        }

        internal static bool IsDrawBlockingReason(string reason)
        {
            return DrawBlockingReasons.Contains(NormalizeBusyReason(reason));
        }

        internal static bool ShouldBypassActionPostReadyBusyReason(string reason)
        {
            return ActionPostReadyBypassReasons.Contains(NormalizeBusyReason(reason));
        }

        private static IReadOnlyList<string> NormalizeFlags(IEnumerable<string> flags, string fallback)
        {
            var normalized = new List<string>();
            if (flags != null)
            {
                foreach (var flag in flags)
                {
                    var trimmedFlag = (flag ?? string.Empty).Trim();
                    if (trimmedFlag.Length == 0 || normalized.Contains(trimmedFlag, StringComparer.Ordinal))
                        continue;

                    normalized.Add(trimmedFlag);
                }
            }

            if (normalized.Count == 0)
                normalized.Add(NormalizeBusyReason(fallback));

            return normalized;
        }

        private static IReadOnlyList<string> ParseFlags(string raw, bool isReady, string fallback)
        {
            if (isReady)
                return Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(raw))
                return new[] { NormalizeBusyReason(fallback) };

            return NormalizeFlags(
                raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(flag => flag.Trim()),
                fallback);
        }

        private static string NormalizeBusyReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? UnknownBusyReason
                : reason.Trim();
        }
    }
}
