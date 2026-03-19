using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SmartBotProfiles
{
    internal static class HSReplayArchetypeEarlyPatch
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            TryPatch();
        }

        private static void TryPatch()
        {
            try
            {
                Type? parserType = Type.GetType("SmartBotAPI.Plugins.API.HSReplayArchetypes.Parser, SBAPI", throwOnError: false);
                if (parserType == null)
                    return;

                SetPrivateStaticField(parserType, "MainPageRegex", "\"archetype_id\"\\s*:\\s*(.*?),");
                SetPrivateStaticField(parserType, "MainPageRegexV1", "\"id\"\\s*:\\s*(.*?),");
                SetPrivateStaticField(parserType, "ArchetypeNameRegex", "\"name\"\\s*:\\s*\"(.*?)\"");
                SetPrivateStaticField(parserType, "ArchetypeIdRegex", "\"id\"\\s*:\\s*(.*?),");
                SetPrivateStaticField(parserType, "ArchetypeClassRegex", "\"player_class_name\"\\s*:\\s*\"(.*?)\"");
            }
            catch
            {
            }
        }

        private static void SetPrivateStaticField(Type type, string fieldName, string value)
        {
            FieldInfo? field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
                return;

            field.SetValue(null, value);
        }
    }
}
