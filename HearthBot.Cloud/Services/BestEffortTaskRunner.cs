using Microsoft.Extensions.Logging;

namespace HearthBot.Cloud.Services;

public static class BestEffortTaskRunner
{
    public static async Task<bool> TryRunAsync(
        Func<Task> action,
        ILogger logger,
        string operationName)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{OperationName} failed, but the primary flow will continue", operationName);
            return false;
        }
    }
}
