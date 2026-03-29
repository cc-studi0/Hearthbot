using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BotMain;
using SmartBot.Plugins.API;
using Xunit;

namespace BotCore.Tests
{
    public class BotServiceProfileRefreshTests
    {
        [Fact]
        public void LoadProfiles_WhenDirectoryMissing_ClearsPreviouslyLoadedProfilesState()
        {
            var service = new BotService();
            var profilesField = GetRequiredField(service, "_profiles");
            var staleProfiles = Assert.IsAssignableFrom<IList>(Activator.CreateInstance(profilesField.FieldType));
            staleProfiles.Add(null);

            profilesField.SetValue(service, staleProfiles);
            SetPrivateField(service, "_selectedProfile", null);
            SetAutoPropertyBackingField(service, "ProfileNames", new List<string> { "OldProfile" });

            InvokePrivateLoad(service, "LoadProfiles", CreateMissingDirectoryPath(), Array.Empty<string>());

            var profiles = Assert.IsAssignableFrom<ICollection>(GetPrivateField(service, "_profiles"));
            var profileNames = Assert.IsType<List<string>>(GetAutoPropertyBackingField(service, "ProfileNames"));
            var selectedProfile = GetPrivateField(service, "_selectedProfile");

            Assert.Empty(profiles.Cast<object>());
            Assert.Empty(profileNames);
            Assert.Null(selectedProfile);
        }

        [Fact]
        public void LoadDiscoverProfiles_WhenDirectoryMissing_ClearsPreviouslyLoadedDiscoverState()
        {
            var service = new BotService();
            SetPrivateField(
                service,
                "_discoverProfileTypes",
                new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OldDiscover"] = typeof(BotServiceProfileRefreshTests)
                });
            SetAutoPropertyBackingField(service, "DiscoverProfileNames", new List<string> { "OldDiscover" });

            InvokePrivateLoad(service, "LoadDiscoverProfiles", CreateMissingDirectoryPath(), Array.Empty<string>());

            var discoverTypes = Assert.IsType<Dictionary<string, Type>>(GetPrivateField(service, "_discoverProfileTypes"));
            var discoverNames = Assert.IsType<List<string>>(GetAutoPropertyBackingField(service, "DiscoverProfileNames"));

            Assert.Empty(discoverTypes);
            Assert.Empty(discoverNames);
        }

        private static void InvokePrivateLoad(BotService service, string methodName, string path, string[] refs)
        {
            var method = typeof(BotService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string[]) }, null);
            Assert.NotNull(method);
            method.Invoke(service, new object[] { path, refs });
        }

        private static string CreateMissingDirectoryPath()
        {
            return Path.Combine(Path.GetTempPath(), "hearthbot-missing-" + Guid.NewGuid().ToString("N"));
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            var field = GetRequiredField(instance, fieldName);
            return field.GetValue(instance);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = GetRequiredField(instance, fieldName);
            field.SetValue(instance, value);
        }

        private static FieldInfo GetRequiredField(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field;
        }

        private static object GetAutoPropertyBackingField(object instance, string propertyName)
        {
            return GetPrivateField(instance, $"<{propertyName}>k__BackingField");
        }

        private static void SetAutoPropertyBackingField(object instance, string propertyName, object value)
        {
            SetPrivateField(instance, $"<{propertyName}>k__BackingField", value);
        }
    }
}
