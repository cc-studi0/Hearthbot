using System;
using System.Collections.Generic;

namespace HearthstonePayload
{
    internal static class ChoiceExecutionPolicy
    {
        public static int PickImplicitOptionTarget(IReadOnlyList<int> candidateEntityIds)
        {
            if (candidateEntityIds == null || candidateEntityIds.Count == 0)
                return 0;

            var selected = 0;
            for (var i = 0; i < candidateEntityIds.Count; i++)
            {
                var entityId = candidateEntityIds[i];
                if (entityId <= 0)
                    continue;

                if (selected > 0 && selected != entityId)
                    return 0;

                selected = entityId;
            }

            return selected;
        }

        public static bool IsMouseOnlyChoice(string mode, bool isEntityChoice)
        {
            if (!isEntityChoice)
                return false;

            switch ((mode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "DISCOVER":
                case "TIMELINE":
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldUseMouseForChoice(
            string mode,
            int countMax,
            int selectedEntityCount,
            bool isMagicItemDiscover,
            bool isShopChoice,
            bool isLaunchpadAbility)
        {
            if (selectedEntityCount != 1)
                return false;

            if (countMax > 1
                || isMagicItemDiscover
                || isShopChoice
                || isLaunchpadAbility)
            {
                return false;
            }

            switch ((mode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "DISCOVER":
                case "DREDGE":
                case "ADAPT":
                case "TIMELINE":
                case "GENERAL":
                case "CHOOSE_ONE":
                case "TARGET":
                case "OPTION_TARGET":
                    return true;
                default:
                    return false;
            }
        }
    }
}
