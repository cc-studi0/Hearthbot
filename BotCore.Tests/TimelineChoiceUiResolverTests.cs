using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class TimelineChoiceUiResolverTests
    {
        [Fact]
        public void TryResolveButtonSource_ReturnsWrappedRewindButton_ForRewindCard()
        {
            var expected = new FakeButton();
            var manager = new FakeHudManager
            {
                m_rewindButton = new FakeButtonContainer { m_button = expected },
                m_keepButton = new FakeButtonContainer { m_button = new FakeButton() }
            };

            var ok = TimelineChoiceUiResolver.TryResolveButtonSource(manager, "TIME_000tb", out var source);

            Assert.True(ok);
            Assert.Same(expected, source);
        }

        [Fact]
        public void TryResolveButtonSource_PrefersContainer_WhenContainerHasOwnUiRoot()
        {
            var expected = new FakeScreenButtonContainer
            {
                gameObject = new object(),
                m_button = new FakeButton()
            };
            var manager = new FakeHudManager
            {
                m_rewindButton = expected,
                m_keepButton = new FakeButtonContainer { m_button = new FakeButton() }
            };

            var ok = TimelineChoiceUiResolver.TryResolveButtonSource(manager, "TIME_000tb", out var source);

            Assert.True(ok);
            Assert.Same(expected, source);
        }

        [Fact]
        public void TryResolveButtonSource_ReturnsWrappedKeepButton_ForKeepCard()
        {
            var expected = new FakeButton();
            var manager = new FakeHudManager
            {
                m_rewindButton = new FakeButtonContainer { m_button = new FakeButton() },
                m_keepButton = new FakeButtonContainer { m_button = expected }
            };

            var ok = TimelineChoiceUiResolver.TryResolveButtonSource(manager, "TIME_000ta", out var source);

            Assert.True(ok);
            Assert.Same(expected, source);
        }

        [Fact]
        public void TryResolveButtonSource_PrefersExactCurrentGameButtonField_BeforeOtherRewindMembers()
        {
            var expected = new FakeButton();
            var manager = new FakeHudManagerWithSpellFirst
            {
                m_rewindChoiceSpell = new FakeSpell { gameObject = new object() },
                m_rewindButton = expected,
                m_keepButton = new FakeButton()
            };

            var ok = TimelineChoiceUiResolver.TryResolveButtonSource(manager, "TIME_000tb", out var source);

            Assert.True(ok);
            Assert.Same(expected, source);
        }

        [Fact]
        public void TryResolveButtonSource_ReturnsFalse_WhenMatchingButtonMissing()
        {
            var manager = new FakeHudManager();

            var ok = TimelineChoiceUiResolver.TryResolveButtonSource(manager, "TIME_000tb", out var source);

            Assert.False(ok);
            Assert.Null(source);
        }

        private sealed class FakeHudManager
        {
            public FakeButtonContainer m_rewindButton;
            public FakeButtonContainer m_keepButton;
        }

        private sealed class FakeHudManagerWithSpellFirst
        {
            public FakeSpell m_rewindChoiceSpell;
            public FakeButton m_rewindButton;
            public FakeButton m_keepButton;
        }

        private class FakeButtonContainer
        {
            public FakeButton m_button;
        }

        private sealed class FakeScreenButtonContainer : FakeButtonContainer
        {
            public object gameObject;
        }

        private sealed class FakeButton
        {
        }

        private sealed class FakeSpell
        {
            public object gameObject;
        }
    }
}
