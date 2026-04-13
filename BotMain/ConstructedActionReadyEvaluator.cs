using System;

namespace BotMain
{
    internal enum ConstructedActionReadyKind
    {
        Unknown = 0,
        Play = 1,
        Attack = 2,
        HeroPower = 3,
        UseLocation = 4,
        Option = 5,
        Trade = 6,
        EndTurn = 7
    }

    internal struct ConstructedObjectReadySnapshot
    {
        public bool Exists { get; set; }
        public bool HasScreenPosition { get; set; }
        public bool PositionStableKnown { get; set; }
        public bool IsPositionStable { get; set; }
        public bool IsActionReadyKnown { get; set; }
        public bool IsActionReady { get; set; }
        public bool IsInteractiveKnown { get; set; }
        public bool IsInteractive { get; set; }

        public bool HasAnyKnownSignal()
        {
            return Exists
                || HasScreenPosition
                || PositionStableKnown
                || IsActionReadyKnown
                || IsInteractiveKnown;
        }
    }

    internal struct ConstructedAttackRuntimeState
    {
        public bool AttackValueKnown { get; set; }
        public int AttackValue { get; set; }
        public bool FrozenKnown { get; set; }
        public bool IsFrozen { get; set; }
        public bool AttackCountKnown { get; set; }
        public int AttackCount { get; set; }
        public bool WindfuryKnown { get; set; }
        public bool HasWindfury { get; set; }
        public bool ExhaustedKnown { get; set; }
        public bool IsExhausted { get; set; }
        public bool ChargeKnown { get; set; }
        public bool HasCharge { get; set; }
        public bool RushKnown { get; set; }
        public bool HasRush { get; set; }
    }

    internal struct ConstructedActionReadyProbe
    {
        public ConstructedActionReadyKind Kind { get; set; }
        public string CommandKind { get; set; }
        public ConstructedObjectReadySnapshot Source { get; set; }
        public ConstructedObjectReadySnapshot Target { get; set; }
        public bool RequiresTarget { get; set; }
        public bool InputDenied { get; set; }
        public bool ResponsePacketBlocked { get; set; }
        public bool BlockingPowerProcessor { get; set; }
        public bool PowerProcessorRunning { get; set; }
        public bool HasActiveServerChange { get; set; }
        public bool ChoiceReady { get; set; }
        public bool PendingTargetConfirmation { get; set; }
        public bool EndTurnButtonReady { get; set; }

        public static ConstructedActionReadyProbe ForPlay(
            ConstructedObjectReadySnapshot source,
            ConstructedObjectReadySnapshot target,
            bool requiresTarget)
        {
            return new ConstructedActionReadyProbe
            {
                Kind = ConstructedActionReadyKind.Play,
                CommandKind = "PLAY",
                Source = source,
                Target = target,
                RequiresTarget = requiresTarget
            };
        }

        public static ConstructedActionReadyProbe ForAttack(
            ConstructedObjectReadySnapshot source,
            ConstructedObjectReadySnapshot target)
        {
            return new ConstructedActionReadyProbe
            {
                Kind = ConstructedActionReadyKind.Attack,
                CommandKind = "ATTACK",
                Source = source,
                Target = target,
                RequiresTarget = true
            };
        }

        public static ConstructedActionReadyProbe ForHeroPower(
            ConstructedObjectReadySnapshot source,
            ConstructedObjectReadySnapshot target,
            bool requiresTarget)
        {
            return new ConstructedActionReadyProbe
            {
                Kind = ConstructedActionReadyKind.HeroPower,
                CommandKind = "HERO_POWER",
                Source = source,
                Target = target,
                RequiresTarget = requiresTarget
            };
        }

        public static ConstructedActionReadyProbe ForUseLocation(
            ConstructedObjectReadySnapshot source,
            ConstructedObjectReadySnapshot target,
            bool requiresTarget)
        {
            return new ConstructedActionReadyProbe
            {
                Kind = ConstructedActionReadyKind.UseLocation,
                CommandKind = "USE_LOCATION",
                Source = source,
                Target = target,
                RequiresTarget = requiresTarget
            };
        }

        public static ConstructedActionReadyProbe ForOption(bool choiceReady, int sourceEntityId)
        {
            return new ConstructedActionReadyProbe
            {
                Kind = ConstructedActionReadyKind.Option,
                CommandKind = "OPTION",
                ChoiceReady = choiceReady
            };
        }

        public static ConstructedActionReadyProbe ForTrade(ConstructedObjectReadySnapshot source)
        {
            return new ConstructedActionReadyProbe
            {
                Kind = ConstructedActionReadyKind.Trade,
                CommandKind = "TRADE",
                Source = source
            };
        }

        public static ConstructedActionReadyProbe ForEndTurn(bool endTurnButtonReady)
        {
            return new ConstructedActionReadyProbe
            {
                Kind = ConstructedActionReadyKind.EndTurn,
                CommandKind = "END_TURN",
                EndTurnButtonReady = endTurnButtonReady
            };
        }
    }

    internal static class ConstructedActionReadyEvaluator
    {
        internal static bool TryEvaluateAttackReadinessFromRuntimeTags(
            ConstructedAttackRuntimeState runtime,
            bool targetIsEnemyHero,
            bool targetIsEnemyMinion,
            out bool isReady,
            out string reason)
        {
            isReady = false;
            reason = "runtime_unknown";

            if (!runtime.AttackValueKnown
                || !runtime.FrozenKnown
                || !runtime.AttackCountKnown
                || !runtime.WindfuryKnown
                || !runtime.ExhaustedKnown)
            {
                reason = "runtime_tags_incomplete";
                return false;
            }

            if (runtime.AttackValue <= 0)
            {
                reason = "atk_le_0_runtime";
                return true;
            }

            if (runtime.IsFrozen)
            {
                reason = "frozen_runtime";
                return true;
            }

            var maxAttackCount = runtime.HasWindfury ? 2 : 1;
            if (runtime.AttackCount >= maxAttackCount)
            {
                reason = "attack_count_limit_runtime";
                return true;
            }

            if (!runtime.IsExhausted)
            {
                isReady = true;
                reason = "ok_runtime";
                return true;
            }

            if (!runtime.ChargeKnown || !runtime.RushKnown)
            {
                reason = "charge_or_rush_unknown";
                return false;
            }

            if (runtime.HasCharge)
            {
                isReady = true;
                reason = "ok_charge_runtime";
                return true;
            }

            if (!runtime.HasRush)
            {
                reason = "exhausted_runtime";
                return true;
            }

            if (targetIsEnemyMinion)
            {
                isReady = true;
                reason = "ok_rush_vs_minion_runtime";
                return true;
            }

            if (targetIsEnemyHero)
            {
                reason = "rush_target_classification_required";
                return false;
            }

            reason = "rush_target_unknown";
            return false;
        }

        internal static ConstructedActionReadyState Evaluate(ConstructedActionReadyProbe probe)
        {
            var commandKind = probe.CommandKind ?? string.Empty;
            var tolerateCosmeticPowerProcessor = probe.Kind == ConstructedActionReadyKind.Attack;

            if (probe.InputDenied)
                return Busy("input_denied", commandKind);

            if (probe.ResponsePacketBlocked)
                return Busy("response_packet_blocked", commandKind);

            if (probe.BlockingPowerProcessor && !tolerateCosmeticPowerProcessor)
                return Busy("blocking_power_processor", commandKind);

            if (probe.PowerProcessorRunning && !tolerateCosmeticPowerProcessor)
                return Busy("power_processor_running", commandKind);

            if (probe.HasActiveServerChange)
                return Busy("zone_active_server_change", commandKind);

            switch (probe.Kind)
            {
                case ConstructedActionReadyKind.Play:
                    if (probe.PendingTargetConfirmation)
                        return Busy("pending_target_confirmation", commandKind);
                    return EvaluateSourceAndTarget(probe, commandKind, requireActionReady: false, requireInteractive: true);
                case ConstructedActionReadyKind.Attack:
                    return EvaluateSourceAndTarget(probe, commandKind, requireActionReady: true, requireInteractive: false);
                case ConstructedActionReadyKind.HeroPower:
                case ConstructedActionReadyKind.UseLocation:
                    return EvaluateSourceAndTarget(probe, commandKind, requireActionReady: false, requireInteractive: false);
                case ConstructedActionReadyKind.Option:
                    return probe.ChoiceReady ? Ready(commandKind) : Busy("choice_not_ready", commandKind);
                case ConstructedActionReadyKind.Trade:
                    return EvaluateSourceAndTarget(probe, commandKind, requireActionReady: false, requireInteractive: true);
                case ConstructedActionReadyKind.EndTurn:
                    return probe.EndTurnButtonReady ? Ready(commandKind) : Busy("end_turn_button_disabled", commandKind);
                default:
                    return Busy(ConstructedActionReadyDiagnostics.UnknownBusyReason, commandKind);
            }
        }

        private static ConstructedActionReadyState EvaluateSourceAndTarget(
            ConstructedActionReadyProbe probe,
            string commandKind,
            bool requireActionReady,
            bool requireInteractive)
        {
            var sourceResult = EvaluateSource(probe.Source, commandKind, requireActionReady, requireInteractive);
            if (!sourceResult.IsReady)
                return sourceResult;

            if (!probe.RequiresTarget)
                return Ready(commandKind);

            return EvaluateTarget(probe.Target, commandKind);
        }

        private static ConstructedActionReadyState EvaluateSource(
            ConstructedObjectReadySnapshot source,
            string commandKind,
            bool requireActionReady,
            bool requireInteractive)
        {
            if (!source.Exists)
                return Busy("source_missing", commandKind);

            if (!source.HasScreenPosition)
                return Busy("source_pos_not_found", commandKind);

            if (source.PositionStableKnown && !source.IsPositionStable)
                return Busy("source_not_stable", commandKind);

            if (requireActionReady && source.IsActionReadyKnown && !source.IsActionReady)
                return Busy("source_action_not_ready", commandKind);

            if (requireInteractive && source.IsInteractiveKnown && !source.IsInteractive)
                return Busy("source_not_interactive", commandKind);

            return Ready(commandKind);
        }

        private static ConstructedActionReadyState EvaluateTarget(ConstructedObjectReadySnapshot target, string commandKind)
        {
            if (!target.Exists)
                return Busy("target_missing", commandKind);

            if (!target.HasScreenPosition)
                return Busy("target_pos_not_found", commandKind);

            if (target.PositionStableKnown && !target.IsPositionStable)
                return Busy("target_not_stable", commandKind);

            return Ready(commandKind);
        }

        private static ConstructedActionReadyState Ready(string commandKind)
        {
            return new ConstructedActionReadyState
            {
                IsReady = true,
                PrimaryReason = ConstructedActionReadyDiagnostics.ReadyReason,
                Flags = Array.Empty<string>(),
                CommandKind = commandKind ?? string.Empty
            };
        }

        private static ConstructedActionReadyState Busy(string reason, string commandKind)
        {
            var normalizedReason = string.IsNullOrWhiteSpace(reason)
                ? ConstructedActionReadyDiagnostics.UnknownBusyReason
                : reason.Trim();

            return new ConstructedActionReadyState
            {
                IsReady = false,
                PrimaryReason = normalizedReason,
                Flags = new[] { normalizedReason },
                CommandKind = commandKind ?? string.Empty
            };
        }
    }
}
