using System;
using System.Linq;
using System.Reflection;

namespace BotMain.AI
{
    public static class CardEffectScriptLoader
    {
        public static int LoadAll(CardEffectDB db)
        {
            if (db == null) return 0;

            var asm = Assembly.GetExecutingAssembly();
            var types = asm
                .GetTypes()
                .Where(t => typeof(ICardEffectScript).IsAssignableFrom(t))
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .Where(t => string.Equals(t.Namespace, "BotMain.AI.CardEffectsScripts", StringComparison.Ordinal))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToArray();

            int loaded = 0;
            foreach (var t in types)
            {
                try
                {
                    if (Activator.CreateInstance(t) is ICardEffectScript script)
                    {
                        script.Register(db);
                        loaded++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CardEffectScriptLoader] 脚本 {t.FullName} 加载失败: {ex.Message}");
                }
            }
            return loaded;
        }
    }
}
