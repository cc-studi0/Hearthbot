using System;

namespace BotMain
{
    internal enum BattleNetRestartFailureKind
    {
        None,
        MissingBinding,
        ProcessExited,
        WindowNotFound,
        BringToFrontFailed,
        WindowRectFailed,
        LaunchTimedOut,
        Cancelled
    }

    internal readonly struct BattleNetRestartBinding
    {
        public BattleNetRestartBinding(int? processId, string windowTitle)
        {
            ProcessId = processId;
            WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? string.Empty : windowTitle;
        }

        public int? ProcessId { get; }
        public string WindowTitle { get; }
    }

    internal readonly struct BattleNetLaunchResult
    {
        public BattleNetLaunchResult(
            bool success,
            BattleNetRestartFailureKind failureKind,
            string message,
            int? battleNetProcessId = null,
            int? hearthstoneProcessId = null)
        {
            Success = success;
            FailureKind = failureKind;
            Message = message ?? string.Empty;
            BattleNetProcessId = battleNetProcessId;
            HearthstoneProcessId = hearthstoneProcessId;
        }

        public bool Success { get; }
        public BattleNetRestartFailureKind FailureKind { get; }
        public string Message { get; }
        public int? BattleNetProcessId { get; }
        public int? HearthstoneProcessId { get; }

        public static BattleNetLaunchResult Succeeded(int battleNetProcessId, int hearthstoneProcessId, string message)
        {
            return new BattleNetLaunchResult(
                success: true,
                failureKind: BattleNetRestartFailureKind.None,
                message: message,
                battleNetProcessId: battleNetProcessId,
                hearthstoneProcessId: hearthstoneProcessId);
        }

        public static BattleNetLaunchResult Failed(
            BattleNetRestartFailureKind kind,
            string message,
            int? battleNetProcessId = null)
        {
            return new BattleNetLaunchResult(
                success: false,
                failureKind: kind,
                message: message,
                battleNetProcessId: battleNetProcessId,
                hearthstoneProcessId: null);
        }
    }

    internal static class BattleNetRestartBindingValidator
    {
        public static BattleNetLaunchResult Validate(BattleNetRestartBinding binding, Func<int, bool> isProcessAlive)
        {
            if (!binding.ProcessId.HasValue || binding.ProcessId.Value <= 0)
            {
                return BattleNetLaunchResult.Failed(
                    BattleNetRestartFailureKind.MissingBinding,
                    "未绑定战网实例");
            }

            if (isProcessAlive == null || !isProcessAlive(binding.ProcessId.Value))
            {
                return BattleNetLaunchResult.Failed(
                    BattleNetRestartFailureKind.ProcessExited,
                    $"战网实例已退出 PID={binding.ProcessId.Value}",
                    binding.ProcessId.Value);
            }

            return new BattleNetLaunchResult(
                success: true,
                failureKind: BattleNetRestartFailureKind.None,
                message: $"战网实例可用 PID={binding.ProcessId.Value}",
                battleNetProcessId: binding.ProcessId.Value);
        }
    }
}
