using System;
using System.Collections.Generic;
using System.Linq;

namespace BotMain
{
    internal sealed class BgActionReadyState
    {
        public bool IsReady { get; set; }
        public string PrimaryReason { get; set; } = string.Empty;
        public IReadOnlyList<string> Flags { get; set; } = Array.Empty<string>();
        public string CommandKind { get; set; } = string.Empty;
        public int SourceEntityId { get; set; }
        public int TargetEntityId { get; set; }
    }

    internal static class BgActionReadyDiagnostics
    {
        internal const string ReadyReason = "ready";
        internal const string UnknownBusyReason = "unknown";

        internal static string FormatReadyResponse(string commandKind, int sourceEntityId = 0, int targetEntityId = 0)
        {
            return FormatResponse(new BgActionReadyState
            {
                IsReady = true,
                PrimaryReason = ReadyReason,
                Flags = Array.Empty<string>(),
                CommandKind = commandKind ?? string.Empty,
                SourceEntityId = sourceEntityId > 0 ? sourceEntityId : 0,
                TargetEntityId = targetEntityId > 0 ? targetEntityId : 0
            });
        }

        internal static string FormatBusyResponse(
            string primaryReason,
            IEnumerable<string> flags,
            string commandKind,
            int sourceEntityId = 0,
            int targetEntityId = 0)
        {
            return FormatResponse(new BgActionReadyState
            {
                IsReady = false,
                PrimaryReason = NormalizeReason(primaryReason),
                Flags = NormalizeFlags(flags, primaryReason),
                CommandKind = commandKind ?? string.Empty,
                SourceEntityId = sourceEntityId > 0 ? sourceEntityId : 0,
                TargetEntityId = targetEntityId > 0 ? targetEntityId : 0
            });
        }

        internal static string FormatResponse(BgActionReadyState state)
        {
            if (state == null)
                return FormatBusyResponse(UnknownBusyReason, null, string.Empty);

            var parts = new List<string>
            {
                "reason=" + (state.IsReady ? ReadyReason : NormalizeReason(state.PrimaryReason))
            };

            if (!state.IsReady)
            {
                var flags = NormalizeFlags(state.Flags, state.PrimaryReason);
                parts.Add("flags=" + string.Join(",", flags));
            }

            if (!string.IsNullOrWhiteSpace(state.CommandKind))
                parts.Add("command=" + state.CommandKind.Trim());

            if (state.SourceEntityId > 0)
                parts.Add("source=" + state.SourceEntityId);

            if (state.TargetEntityId > 0)
                parts.Add("target=" + state.TargetEntityId);

            return (state.IsReady ? "READY:" : "BUSY:") + string.Join(";", parts);
        }

        internal static bool TryParseResponse(string response, out BgActionReadyState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(response))
                return false;

            if (string.Equals(response, "READY", StringComparison.OrdinalIgnoreCase))
            {
                state = new BgActionReadyState
                {
                    IsReady = true,
                    PrimaryReason = ReadyReason,
                    Flags = Array.Empty<string>()
                };
                return true;
            }

            if (string.Equals(response, "BUSY", StringComparison.OrdinalIgnoreCase))
            {
                state = new BgActionReadyState
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

            var parsed = new BgActionReadyState
            {
                IsReady = isReady,
                PrimaryReason = isReady ? ReadyReason : UnknownBusyReason,
                Flags = isReady ? Array.Empty<string>() : new[] { UnknownBusyReason }
            };

            if (!string.IsNullOrWhiteSpace(payload))
            {
                foreach (var part in payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmedPart = (part ?? string.Empty).Trim();
                    if (trimmedPart.Length == 0)
                        continue;

                    var equalsIndex = trimmedPart.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    var key = trimmedPart.Substring(0, equalsIndex).Trim();
                    var value = trimmedPart.Substring(equalsIndex + 1).Trim();

                    if (key.Equals("reason", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.PrimaryReason = isReady ? ReadyReason : NormalizeReason(value);
                    }
                    else if (key.Equals("flags", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.Flags = ParseFlags(value, isReady, parsed.PrimaryReason);
                    }
                    else if (key.Equals("command", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed.CommandKind = value ?? string.Empty;
                    }
                    else if (key.Equals("source", StringComparison.OrdinalIgnoreCase))
                    {
                        int sourceEntityId;
                        if (int.TryParse(value, out sourceEntityId) && sourceEntityId > 0)
                            parsed.SourceEntityId = sourceEntityId;
                    }
                    else if (key.Equals("target", StringComparison.OrdinalIgnoreCase))
                    {
                        int targetEntityId;
                        if (int.TryParse(value, out targetEntityId) && targetEntityId > 0)
                            parsed.TargetEntityId = targetEntityId;
                    }
                }
            }

            if (isReady)
            {
                parsed.PrimaryReason = ReadyReason;
                parsed.Flags = Array.Empty<string>();
            }
            else if (parsed.Flags == null || parsed.Flags.Count == 0)
            {
                parsed.Flags = new[] { NormalizeReason(parsed.PrimaryReason) };
            }

            state = parsed;
            return true;
        }

        private static IReadOnlyList<string> ParseFlags(string raw, bool isReady, string fallback)
        {
            if (isReady)
                return Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(raw))
                return new[] { NormalizeReason(fallback) };

            return NormalizeFlags(
                raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(flag => flag.Trim()),
                fallback);
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
                normalized.Add(NormalizeReason(fallback));

            return normalized;
        }

        private static string NormalizeReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? UnknownBusyReason
                : reason.Trim();
        }
    }
}
