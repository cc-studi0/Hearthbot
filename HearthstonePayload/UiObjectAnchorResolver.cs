using System;
using System.Reflection;

namespace HearthstonePayload
{
    internal static class UiObjectAnchorResolver
    {
        private static readonly string[] AnchorMemberNames =
        {
            "m_RootObject",
            "RootObject",
            "rootObject",
            "m_rootObject"
        };

        public static object ResolveAnchorSource(object source)
        {
            if (source == null)
                return null;

            foreach (var memberName in AnchorMemberNames)
            {
                var value = TryGetMemberValue(source, memberName);
                if (value != null)
                    return value;
            }

            var gameObject = TryGetMemberValue(source, "gameObject");
            if (gameObject != null)
                return gameObject;

            return source;
        }

        private static object TryGetMemberValue(object source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name))
                return null;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var field = source.GetType().GetField(name, flags);
                if (field != null)
                    return field.GetValue(source);

                var property = source.GetType().GetProperty(name, flags);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                    return property.GetValue(source);
            }
            catch
            {
            }

            return null;
        }
    }
}
