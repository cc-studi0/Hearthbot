using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    internal static class TimelineChoiceUiResolver
    {
        private static readonly string[] RewindTokens = { "rewind" };
        private static readonly string[] KeepTokens = { "keep", "maintain" };
        private static readonly string[] WrapperMemberNames = { "Button", "m_button", "NormalButton", "m_normalButton" };

        public static bool IsTimelineChoiceCardId(string choiceCardId)
        {
            return string.Equals(choiceCardId, "TIME_000ta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(choiceCardId, "TIME_000tb", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryResolveButtonSource(object rewindHudManager, string choiceCardId, out object source)
        {
            source = null;
            if (rewindHudManager == null || !TryGetTokens(choiceCardId, out var tokens))
                return false;

            foreach (var candidate in EnumerateNamedCandidates(rewindHudManager, tokens))
            {
                if (!LooksLikeUiSource(candidate))
                    continue;

                source = UnwrapButtonCandidate(candidate);
                if (source != null)
                    return true;
            }

            return false;
        }

        private static bool TryGetTokens(string choiceCardId, out string[] tokens)
        {
            tokens = null;
            if (string.Equals(choiceCardId, "TIME_000tb", StringComparison.OrdinalIgnoreCase))
            {
                tokens = RewindTokens;
                return true;
            }

            if (string.Equals(choiceCardId, "TIME_000ta", StringComparison.OrdinalIgnoreCase))
            {
                tokens = KeepTokens;
                return true;
            }

            return false;
        }

        private static IEnumerable<object> EnumerateNamedCandidates(object source, IReadOnlyList<string> tokens)
        {
            if (source == null || tokens == null || tokens.Count == 0)
                yield break;

            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = source.GetType();

            foreach (var field in type.GetFields(flags))
            {
                if (!NameMatches(field.Name, tokens))
                    continue;

                object value;
                try { value = field.GetValue(source); }
                catch { continue; }

                if (value != null && seen.Add(value))
                    yield return value;
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead || !NameMatches(property.Name, tokens))
                    continue;

                object value;
                try { value = property.GetValue(source); }
                catch { continue; }

                if (value != null && seen.Add(value))
                    yield return value;
            }

            foreach (var method in type.GetMethods(flags))
            {
                if (method.ReturnType == typeof(void)
                    || method.GetParameters().Length != 0
                    || !NameMatches(method.Name, tokens))
                {
                    continue;
                }

                object value;
                try { value = method.Invoke(source, Array.Empty<object>()); }
                catch { continue; }

                if (value != null && seen.Add(value))
                    yield return value;
            }
        }

        private static bool NameMatches(string name, IReadOnlyList<string> tokens)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return tokens.Any(token => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool LooksLikeUiSource(object candidate)
        {
            if (candidate == null)
                return false;

            var type = candidate.GetType();
            var typeName = type.FullName ?? type.Name ?? string.Empty;
            if (typeName.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("ui", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (TryGetMemberValue(candidate, "gameObject") != null
                || TryGetMemberValue(candidate, "transform") != null)
            {
                return true;
            }

            return WrapperMemberNames.Any(name => TryGetMemberValue(candidate, name) != null);
        }

        private static object UnwrapButtonCandidate(object candidate)
        {
            if (candidate == null)
                return null;

            foreach (var memberName in WrapperMemberNames)
            {
                var nested = TryGetMemberValue(candidate, memberName);
                if (nested != null)
                    return nested;
            }

            return candidate;
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
                if (property != null && property.GetIndexParameters().Length == 0 && property.CanRead)
                    return property.GetValue(source);
            }
            catch
            {
            }

            return null;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
