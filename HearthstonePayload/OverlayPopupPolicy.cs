using System;

namespace HearthstonePayload
{
    internal static class OverlayPopupPolicy
    {
        internal static readonly string[] LegacyBlockingDialogRootTypeNames =
        {
            "DialogManager",
            "PopupManager",
            "PopupDisplayManager"
        };

        internal static bool ShouldTreatAsDismissablePopupDisplay(string overlayType)
        {
            return string.Equals(
                overlayType,
                "PopupDisplay",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
