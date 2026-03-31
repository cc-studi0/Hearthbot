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

        public static Task<BattleNetLaunchResult> LaunchHearthstoneFromDetailed(
            int processId,
            Action<string> log,
            CancellationToken ct,
            int timeoutSeconds = 90)
        {
            return Task.FromResult(BattleNetLaunchResult.Succeeded(
                processId,
                hearthstoneProcessId: 1,
                message: "test shim"));
        }
    }
}
