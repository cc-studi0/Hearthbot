using System;
using System.Linq;

namespace HearthstonePayload
{
    internal static class ChoiceController
    {
        public static void Init(CoroutineExecutor coroutine)
        {
            _ = coroutine;
        }

        public static string GetChoiceState()
        {
            return ActionExecutor.GetChoiceState();
        }

        public static string ApplyChoice(string snapshotId, string entityIdsCsv)
        {
            var entityIds = (entityIdsCsv ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token =>
                {
                    int.TryParse(token, out var entityId);
                    return entityId;
                })
                .Where(entityId => entityId > 0)
                .ToArray();

            return ActionExecutor.ApplyChoice(snapshotId, entityIds);
        }
    }
}
