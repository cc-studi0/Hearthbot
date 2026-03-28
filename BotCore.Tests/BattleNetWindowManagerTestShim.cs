using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain
{
    internal static class BattleNetWindowManager
    {
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
