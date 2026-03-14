using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    /// <summary>
    /// 通过反射从 CollectionManager 读取牌组列表。
    /// </summary>
    public static class DeckReader
    {
        private static Assembly _asm;
        private static Type _collectionManagerType;

        private static bool Init()
        {
            if (_collectionManagerType != null) return true;

            _asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (_asm == null) return false;

            _collectionManagerType = _asm.GetType("CollectionManager");
            return _collectionManagerType != null;
        }

        public static string LastError { get; private set; }

        /// <summary>
        /// 返回格式："deckName|class|cardId1,cardId2,...;..."
        /// </summary>
        public static string ReadDecks()
        {
            LastError = null;

            if (!Init())
            {
                LastError = _asm == null ? "未找到 Assembly-CSharp" : "未找到 CollectionManager 类型";
                return null;
            }

            try
            {
                var manager = GetSingleton(_collectionManagerType);
                if (manager == null) { LastError = "CollectionManager 单例为 null"; return null; }

                var getDecks = _collectionManagerType.GetMethod("GetDecks",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null, types: Type.EmptyTypes, modifiers: null);
                if (getDecks == null) { LastError = "未找到 GetDecks 方法"; return null; }

                var deckCollection = getDecks.Invoke(manager, null) as IEnumerable;
                if (deckCollection == null) { LastError = "GetDecks 返回 null"; return null; }

                var rows = new List<string>();
                foreach (var entry in deckCollection)
                {
                    var deck = UnwrapDeck(entry);
                    if (deck == null) continue;

                    var deckName = GetProp(deck, "Name")?.ToString() ?? "Unknown";
                    var classObj = GetProp(deck, "Class") ?? Call(deck, "GetClass");
                    var heroClass = classObj?.ToString() ?? "UNKNOWN";

                    var cards = GetDeckCards(deck);
                    rows.Add($"{deckName}|{heroClass}|{string.Join(",", cards)}");
                }

                return string.Join(";", rows);
            }
            catch
            {
                return null;
            }
        }

        private static object UnwrapDeck(object entry)
        {
            if (entry == null) return null;

            var valueProp = entry.GetType().GetProperty("Value",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (valueProp != null)
            {
                var value = valueProp.GetValue(entry, null);
                if (value != null) return value;
            }

            return entry;
        }

        private static List<string> GetDeckCards(object deck)
        {
            var result = new List<string>();
            if (deck == null) return result;

            try
            {
                var getCardsWithId = deck.GetType().GetMethod("GetCardsWithCardID",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getCardsWithId != null)
                {
                    var cards = getCardsWithId.Invoke(deck, null) as IEnumerable;
                    if (cards != null)
                    {
                        foreach (var card in cards)
                        {
                            var id = card?.ToString();
                            if (!string.IsNullOrEmpty(id))
                                result.Add(id);
                        }
                    }

                    if (result.Count > 0)
                        return result;
                }
            }
            catch
            {
                // 回退到下面的槽位解析
            }

            try
            {
                var getSlots = deck.GetType().GetMethod("GetSlots",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getSlots == null) return result;

                var slots = getSlots.Invoke(deck, null) as IEnumerable;
                if (slots == null) return result;

                foreach (var slot in slots)
                {
                    var cardId = GetProp(slot, "CardID")?.ToString();
                    if (string.IsNullOrEmpty(cardId)) continue;

                    var countObj = GetProp(slot, "Count");
                    var count = 1;
                    if (countObj != null)
                        count = Convert.ToInt32(countObj);

                    for (var i = 0; i < count; i++)
                        result.Add(cardId);
                }
            }
            catch
            {
                // 忽略单个牌组解析失败
            }

            return result;
        }

        private static object GetSingleton(Type type)
        {
            var method = type.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, null);
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;

            var type = obj.GetType();
            var property = type.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null) return property.GetValue(obj, null);

            var field = type.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(obj);
        }

        private static object Call(object obj, string methodName)
        {
            if (obj == null) return null;

            var method = obj.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return method?.Invoke(obj, null);
        }
    }
}
