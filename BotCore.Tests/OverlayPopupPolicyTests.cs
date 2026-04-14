using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class OverlayPopupPolicyTests
    {
        [Fact]
        public void LegacyRootTypes_IncludePopupDisplayManager()
        {
            Assert.Contains(
                "PopupDisplayManager",
                OverlayPopupPolicy.LegacyBlockingDialogRootTypeNames);
        }

        [Theory]
        [InlineData("PopupDisplay", true)]
        [InlineData("LoadingScreen", false)]
        [InlineData("DialogLoading", false)]
        public void GenericPopupDisplay_IsTreatedAsDismissableProbe(string overlayType, bool expected)
        {
            var actual = OverlayPopupPolicy.ShouldTreatAsDismissablePopupDisplay(overlayType);
            Assert.Equal(expected, actual);
        }
    }
}
