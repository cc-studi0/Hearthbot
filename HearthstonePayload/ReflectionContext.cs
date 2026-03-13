using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    /// <summary>
    /// 炉石进程内的共享反射上下文
    /// 统一管理 Assembly-CSharp 的查找和常用类型缓存，
    /// 避免 GameReader / ActionExecutor / SceneNavigator 各自重复初始化
    /// </summary>
    public sealed class ReflectionContext
    {
        private static readonly Lazy<ReflectionContext> _instance =
            new Lazy<ReflectionContext>(() => new ReflectionContext());

        public static ReflectionContext Instance => _instance.Value;

        public Assembly AsmCSharp { get; private set; }
        public Type GameStateType { get; private set; }
        public Type EntityType { get; private set; }
        public Type NetworkType { get; private set; }
        public Type InputMgrType { get; private set; }
        public Type ConnectApiType { get; private set; }
        public Type SceneMgrType { get; private set; }
        public Type GameMgrType { get; private set; }
        public Type CollMgrType { get; private set; }
        public Type GameTagType { get; private set; }

        public bool IsReady { get; private set; }

        // GetTag 方法缓存：key = (实体类型, 参数类型)，避免每次反射遍历
        private readonly ConcurrentDictionary<(Type, Type), MethodInfo> _getTagCache =
            new ConcurrentDictionary<(Type, Type), MethodInfo>();

        // 通用成员查找缓存：key = (类型, 成员名)
        private readonly ConcurrentDictionary<(Type, string), MemberInfo> _memberCache =
            new ConcurrentDictionary<(Type, string), MemberInfo>();

        private ReflectionContext() { }

        public bool Init()
        {
            if (IsReady) return true;

            try
            {
                AsmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (AsmCSharp == null) return false;

                GameStateType = AsmCSharp.GetType("GameState");
                EntityType = AsmCSharp.GetType("Entity");
                NetworkType = AsmCSharp.GetType("Network");
                InputMgrType = AsmCSharp.GetType("InputManager");
                ConnectApiType = AsmCSharp.GetType("ConnectAPI");
                SceneMgrType = AsmCSharp.GetType("SceneMgr");
                GameMgrType = AsmCSharp.GetType("GameMgr");
                CollMgrType = AsmCSharp.GetType("CollectionManager");
                GameTagType = AsmCSharp.GetType("GAME_TAG");

                IsReady = GameStateType != null && EntityType != null;
                return IsReady;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 缓存版 GetTag：对同一实体类型只做一次方法查找
        /// </summary>
        public int GetTagValue(object entity, string tagName)
        {
            if (entity == null || GameTagType == null || string.IsNullOrEmpty(tagName))
                return 0;

            try
            {
                var tagValue = Enum.Parse(GameTagType, tagName, true);
                var tagInt = Convert.ToInt32(tagValue);
                var entityType = entity.GetType();

                // 尝试从缓存获取匹配的 GetTag 方法
                var cacheKey = (entityType, GameTagType);
                if (!_getTagCache.TryGetValue(cacheKey, out var method))
                {
                    method = entityType
                        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetTag"
                            && m.GetParameters().Length == 1
                            && (m.GetParameters()[0].ParameterType.IsEnum
                                || m.GetParameters()[0].ParameterType == typeof(int)));
                    _getTagCache[cacheKey] = method;
                }

                if (method == null) return 0;

                var paramType = method.GetParameters()[0].ParameterType;
                object arg;
                if (paramType.IsEnum)
                    arg = paramType == GameTagType ? tagValue : Enum.ToObject(paramType, tagInt);
                else if (paramType == typeof(int))
                    arg = tagInt;
                else if (paramType == typeof(short))
                    arg = (short)tagInt;
                else if (paramType == typeof(byte))
                    arg = (byte)tagInt;
                else
                    return 0;

                var result = method.Invoke(entity, new[] { arg });
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 缓存版成员查找：方法 > 属性 > 字段，按名称列表依次尝试
        /// </summary>
        public object CallAny(object obj, params string[] members)
        {
            if (obj == null || members == null) return null;

            var type = obj.GetType();
            foreach (var member in members)
            {
                var key = (type, member);
                if (!_memberCache.TryGetValue(key, out var cached))
                {
                    cached = FindMember(type, member);
                    _memberCache[key] = cached; // null 也缓存，避免重复查找
                }

                if (cached == null) continue;

                try
                {
                    if (cached is MethodInfo mi)
                        return mi.Invoke(obj, null);
                    if (cached is PropertyInfo pi)
                        return pi.GetValue(obj, null);
                    if (cached is FieldInfo fi)
                        return fi.GetValue(obj);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// 缓存版静态成员调用
        /// </summary>
        public object CallStaticAny(Type type, params string[] members)
        {
            if (type == null || members == null) return null;

            foreach (var member in members)
            {
                var key = (type, member);
                if (!_memberCache.TryGetValue(key, out var cached))
                {
                    cached = FindStaticMember(type, member);
                    _memberCache[key] = cached;
                }

                if (cached == null) continue;

                try
                {
                    if (cached is MethodInfo mi)
                        return mi.Invoke(null, null);
                    if (cached is PropertyInfo pi)
                        return pi.GetValue(null, null);
                    if (cached is FieldInfo fi)
                        return fi.GetValue(null);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// 缓存版字段/属性读取
        /// </summary>
        public object GetFieldOrPropertyAny(object obj, params string[] names)
        {
            if (obj == null || names == null) return null;

            var type = obj.GetType();
            foreach (var name in names)
            {
                var key = (type, name);
                if (!_memberCache.TryGetValue(key, out var cached))
                {
                    cached = FindMember(type, name);
                    _memberCache[key] = cached;
                }

                if (cached == null) continue;

                try
                {
                    if (cached is PropertyInfo pi)
                        return pi.GetValue(obj, null);
                    if (cached is FieldInfo fi)
                        return fi.GetValue(obj);
                    if (cached is MethodInfo mi)
                        return mi.Invoke(obj, null);
                }
                catch { }
            }

            return null;
        }

        public List<object> CallGetCards(object zone)
        {
            var result = new List<object>();
            try
            {
                var cards = CallAny(zone, "GetCards", "Cards", "GetEntities", "GetEntityList", "m_cards") as IEnumerable;
                if (cards == null && zone is IEnumerable enumerableZone)
                    cards = enumerableZone;
                if (cards == null) return result;
                foreach (var c in cards)
                    result.Add(c);
            }
            catch { }
            return result;
        }

        /// <summary>
        /// 从 GameState.m_entityMap 读取所有 Entity 对象。
        /// 绕过 ZoneMgr UI 单例，直接访问纯数据层，线程安全性更好。
        /// </summary>
        public List<object> GetEntityMapEntries(object gameState)
        {
            if (gameState == null) return new List<object>();

            // 后台线程遍历 Map<int, Entity> 时，主线程可能正在修改它
            // （例如 Discover 结算添加新实体），导致 InvalidOperationException。
            // 先尝试获取 Values 集合的快照以减少竞态窗口，并做最多 3 次重试。
            const int maxRetries = 3;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var mapObj = GetFieldOrPropertyAny(gameState,
                        "m_entityMap",
                        "EntityMap",
                        "m_entities");
                    if (mapObj == null) return new List<object>();

                    // 优先尝试 .Values 属性以获取值集合快照
                    var values = CallAny(mapObj, "get_Values", "Values");
                    IEnumerable enumerable = values as IEnumerable ?? mapObj as IEnumerable;
                    if (enumerable == null) return new List<object>();

                    var result = new List<object>();
                    foreach (var item in enumerable)
                    {
                        // 如果来源是 Values，item 就是 Entity；
                        // 如果来源是原始 Map，item 是 KeyValuePair，需要取 .Value
                        var entity = (values != null)
                            ? item
                            : GetFieldOrPropertyAny(item, "Value", "value");
                        if (entity != null)
                            result.Add(entity);
                    }
                    return result;
                }
                catch
                {
                    // 集合在遍历中被修改，短暂等待后重试
                    if (attempt < maxRetries - 1)
                        System.Threading.Thread.Sleep(15);
                }
            }
            return new List<object>();
        }

        public int ToInt(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (method != null) return method;

            var prop = type.GetProperty(name, flags);
            if (prop != null) return prop;

            var field = type.GetField(name, flags);
            return field;
        }

        private static MemberInfo FindStaticMember(Type type, string name)
        {
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (method != null) return method;

            var prop = type.GetProperty(name, flags);
            if (prop != null) return prop;

            var field = type.GetField(name, flags);
            return field;
        }
    }
}
