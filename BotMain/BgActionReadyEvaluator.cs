using System;

namespace BotMain
{
    internal enum BgActionReadyKind
    {
        Unknown = 0,
        Buy = 1,
        Sell = 2,
        Play = 3,
        Move = 4,
        Button = 5,
        HeroPower = 6,
        Option = 7
    }

    internal struct BgObjectReadySnapshot
    {
        public bool Exists { get; set; }
        public bool HasScreenPosition { get; set; }
        public bool ActorReadyKnown { get; set; }
        public bool ActorReady { get; set; }
        public bool InteractiveKnown { get; set; }
        public bool IsInteractive { get; set; }
        public bool TweenKnown { get; set; }
        public bool HasActiveTween { get; set; }
        public bool ActiveKnown { get; set; }
        public bool IsActive { get; set; }
        public bool EnabledKnown { get; set; }
        public bool IsEnabled { get; set; }

        public bool HasAnyKnownSignal()
        {
            return Exists
                || HasScreenPosition
                || ActorReadyKnown
                || InteractiveKnown
                || TweenKnown
                || ActiveKnown
                || EnabledKnown;
        }
    }

    internal struct BgActionReadyProbe
    {
        public BgActionReadyKind Kind { get; set; }
        public string CommandKind { get; set; }
        public BgObjectReadySnapshot Source { get; set; }
        public BgObjectReadySnapshot Target { get; set; }
        public bool RequiresTarget { get; set; }
        public bool ResponsePacketBlocked { get; set; }
        public bool InputDenied { get; set; }
        public bool BlockingPowerProcessor { get; set; }
        public bool HasActiveServerChange { get; set; }
        public string HandLayoutReason { get; set; }

        public static BgActionReadyProbe ForBuy(string commandKind, BgObjectReadySnapshot source)
        {
            return new BgActionReadyProbe
            {
                Kind = BgActionReadyKind.Buy,
                CommandKind = commandKind ?? string.Empty,
                Source = source
            };
        }

        public static BgActionReadyProbe ForPlay(string commandKind, BgObjectReadySnapshot source, BgObjectReadySnapshot target, string handLayoutReason)
        {
            return new BgActionReadyProbe
            {
                Kind = BgActionReadyKind.Play,
                CommandKind = commandKind ?? string.Empty,
                Source = source,
                Target = target,
                RequiresTarget = target.HasAnyKnownSignal(),
                HandLayoutReason = handLayoutReason ?? string.Empty
            };
        }

        public static BgActionReadyProbe ForButton(string commandKind, BgObjectReadySnapshot source)
        {
            return new BgActionReadyProbe
            {
                Kind = BgActionReadyKind.Button,
                CommandKind = commandKind ?? string.Empty,
                Source = source
            };
        }
    }

    internal static class BgActionReadyEvaluator
    {
        internal static BgActionReadyState Evaluate(BgActionReadyProbe probe)
        {
            var commandKind = probe.CommandKind ?? string.Empty;

            if (probe.ResponsePacketBlocked)
                return Busy("global_blocked:response_packet_blocked", commandKind);

            if (probe.InputDenied)
                return Busy("global_blocked:input_denied", commandKind);

            if (probe.BlockingPowerProcessor)
                return Busy("global_blocked:blocking_power_processor", commandKind);

            if (probe.HasActiveServerChange)
                return Busy("global_blocked:zone_active_server_change", commandKind);

            if (probe.Kind == BgActionReadyKind.Play && !string.IsNullOrWhiteSpace(probe.HandLayoutReason))
                return Busy(probe.HandLayoutReason, commandKind);

            switch (probe.Kind)
            {
                case BgActionReadyKind.Button:
                    return EvaluateButton(probe, commandKind);
                case BgActionReadyKind.Buy:
                    return EvaluateSourceAction(probe, commandKind, requireInteractive: true, requireActorReady: true);
                case BgActionReadyKind.Play:
                    return EvaluatePlay(probe, commandKind);
                case BgActionReadyKind.Sell:
                case BgActionReadyKind.Move:
                    return EvaluateSourceAction(probe, commandKind, requireInteractive: false, requireActorReady: false);
                case BgActionReadyKind.HeroPower:
                case BgActionReadyKind.Option:
                    return EvaluateSourceAction(probe, commandKind, requireInteractive: false, requireActorReady: false);
                default:
                    return Busy(BgActionReadyDiagnostics.UnknownBusyReason, commandKind);
            }
        }

        private static BgActionReadyState EvaluateButton(BgActionReadyProbe probe, string commandKind)
        {
            var source = probe.Source;
            if (!source.Exists)
                return Busy("source_missing", commandKind);

            if (source.ActiveKnown && !source.IsActive)
                return Busy("button_inactive", commandKind);

            if (source.EnabledKnown && !source.IsEnabled)
                return Busy("button_disabled", commandKind);

            if (source.TweenKnown && source.HasActiveTween)
                return Busy("source_tween_active", commandKind);

            return Ready(commandKind);
        }

        private static BgActionReadyState EvaluatePlay(BgActionReadyProbe probe, string commandKind)
        {
            var sourceResult = EvaluateSourceAction(probe, commandKind, requireInteractive: true, requireActorReady: true);
            if (!sourceResult.IsReady)
                return sourceResult;

            if (!probe.RequiresTarget)
                return Ready(commandKind);

            return EvaluateTarget(probe.Target, commandKind);
        }

        private static BgActionReadyState EvaluateSourceAction(
            BgActionReadyProbe probe,
            string commandKind,
            bool requireInteractive,
            bool requireActorReady)
        {
            var source = probe.Source;
            if (!source.Exists)
                return Busy("source_missing", commandKind);

            if (!source.HasScreenPosition)
                return Busy("source_pos_not_found", commandKind);

            if (requireActorReady && source.ActorReadyKnown && !source.ActorReady)
                return Busy("source_actor_not_ready", commandKind);

            if (source.TweenKnown && source.HasActiveTween)
                return Busy("source_tween_active", commandKind);

            if (requireInteractive && source.InteractiveKnown && !source.IsInteractive)
                return Busy("source_not_interactive", commandKind);

            if (probe.RequiresTarget)
                return EvaluateTarget(probe.Target, commandKind);

            return Ready(commandKind);
        }

        private static BgActionReadyState EvaluateTarget(BgObjectReadySnapshot target, string commandKind)
        {
            if (!target.Exists)
                return Busy("target_missing", commandKind);

            if (!target.HasScreenPosition)
                return Busy("target_pos_not_found", commandKind);

            if (target.TweenKnown && target.HasActiveTween)
                return Busy("target_tween_active", commandKind);

            if (target.ActorReadyKnown && !target.ActorReady)
                return Busy("target_actor_not_ready", commandKind);

            return Ready(commandKind);
        }

        private static BgActionReadyState Ready(string commandKind)
        {
            return new BgActionReadyState
            {
                IsReady = true,
                PrimaryReason = BgActionReadyDiagnostics.ReadyReason,
                Flags = Array.Empty<string>(),
                CommandKind = commandKind ?? string.Empty
            };
        }

        private static BgActionReadyState Busy(string reason, string commandKind)
        {
            var normalizedReason = string.IsNullOrWhiteSpace(reason)
                ? BgActionReadyDiagnostics.UnknownBusyReason
                : reason.Trim();

            return new BgActionReadyState
            {
                IsReady = false,
                PrimaryReason = normalizedReason,
                Flags = new[] { normalizedReason },
                CommandKind = commandKind ?? string.Empty
            };
        }
    }
}
