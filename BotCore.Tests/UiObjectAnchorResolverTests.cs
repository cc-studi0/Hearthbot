using HearthstonePayload;
using Xunit;

namespace BotCore.Tests
{
    public class UiObjectAnchorResolverTests
    {
        [Fact]
        public void ResolveAnchorSource_PrefersRootObject_WhenUiButtonExposesIt()
        {
            var rootObject = new object();
            var source = new FakeUiButton
            {
                m_RootObject = rootObject,
                gameObject = new object()
            };

            var anchor = UiObjectAnchorResolver.ResolveAnchorSource(source);

            Assert.Same(rootObject, anchor);
        }

        [Fact]
        public void ResolveAnchorSource_FallsBackToGameObject_WhenRootObjectMissing()
        {
            var gameObject = new object();
            var source = new FakeUiButton
            {
                gameObject = gameObject
            };

            var anchor = UiObjectAnchorResolver.ResolveAnchorSource(source);

            Assert.Same(gameObject, anchor);
        }

        [Fact]
        public void ResolveAnchorSource_ReturnsSource_WhenNoKnownAnchorMembersExist()
        {
            var source = new object();

            var anchor = UiObjectAnchorResolver.ResolveAnchorSource(source);

            Assert.Same(source, anchor);
        }

        private sealed class FakeUiButton
        {
            public object m_RootObject;
            public object gameObject;
        }
    }
}
