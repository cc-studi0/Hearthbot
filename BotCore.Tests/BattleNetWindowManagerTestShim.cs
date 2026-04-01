using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain
{
    public class BattleNetInstance
    {
        public int ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public string WindowTitle { get; set; } = string.Empty;
    }

    internal static class BattleNetWindowManager
    {
        public static List<BattleNetInstance> EnumerateInstances()
        {
            return new List<BattleNetInstance>();
        }

        public static bool IsProcessAlive(int processId)
        {
            return true;
        }

        public static Task<BattleNetLaunchResult> LaunchHearthstoneViaProtocol(
            Action<string> log,
            CancellationToken ct,
            int timeoutSeconds = 120)
        {
            return Task.FromResult(BattleNetLaunchResult.Succeeded(
                battleNetProcessId: 0,
                hearthstoneProcessId: 1,
                message: "test shim"));
        }

        public static Task<BattleNetLaunchResult> LaunchHearthstoneFromDetailed(
            int processId,
            Action<string> log,
            CancellationToken ct,
            int timeoutSeconds = 120)
        {
            return LaunchHearthstoneViaProtocol(log, ct, timeoutSeconds);
        }

        public static void KillHearthstone(Action<string> log) { }
    }
}
