using System;

namespace HearthstonePayload
{
    internal static class ChoiceExecutionPolicy
    {
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
