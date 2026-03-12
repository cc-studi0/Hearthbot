using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    internal static class DiscoverController
    {
        private const int PickTimeoutMs = 8000;
        private const int MaxPickAttempts = 2;
        private const float ClickSettleDelaySeconds = 0.04f;
        private const float ClickHoldSeconds = 0.08f;
        private const float RetryDelaySeconds = 0.08f;
        private const float ResultPollSeconds = 0.08f;
        private const int ResultPollIterations = 32;

        private static readonly string[] PositiveDiscoverTags =
        {
            "DISCOVER",
            "USE_DISCOVER_VISUALS",
            "DISCOVER_STUDIES_VISUAL",
            "SHOW_DISCOVER_FROM_DECK",
            "GOOD_OL_GENERIC_FRIENDLY_DRAGON_DISCOVER_VISUALS"
        };

        private static readonly string[] NegativeDiscoverTags =
        {
            "BACON_IS_MAGIC_ITEM_DISCOVER",
            "IS_SHOP_CHOICE"
        };

        private static CoroutineExecutor _coroutine;
        private static ReflectionContext _ctx;
        private static Assembly _asm;
        private static Type _gameStateType;
        private static Type _choiceCardMgrType;
        private static Type _iTweenType;

        private sealed class DiscoverSnapshot
        {
            public int ChoiceId { get; set; }
            public int SourceEntityId { get; set; }
            public string SourceCardId { get; set; } = string.Empty;
            public bool UiVisible { get; set; }
            public bool Ready { get; set; }
            public string ReadyReason { get; set; } = string.Empty;
            public List<int> ChoiceEntityIds { get; } = new List<int>();
            public List<string> ChoiceCardIds { get; } = new List<string>();
            public Dictionary<int, object> CardsByEntityId { get; } = new Dictionary<int, object>();
        }

        private sealed class DiscoverPacketInfo
        {
            public object GameState { get; set; }
            public object ChoicePacket { get; set; }
            public object ChoiceCardMgr { get; set; }
            public object ChoiceState { get; set; }
            public object SourceEntity { get; set; }
            public int ChoiceId { get; set; }
            public int SourceEntityId { get; set; }
            public List<int> EntityIds { get; } = new List<int>();
        }

        public static void Init(CoroutineExecutor coroutine)
        {
            _coroutine = coroutine;
        }

        public static string GetDiscoverState()
        {
            if (!TryBuildDiscoverSnapshot(out var snapshot))
                return null;

            return string.Join(
                "|",
                snapshot.ChoiceId.ToString(),
                snapshot.SourceEntityId.ToString(),
                Encode(snapshot.SourceCardId),
                snapshot.Ready ? "READY" : "WAITING",
                string.Join(";", snapshot.ChoiceEntityIds.Select((entityId, index) =>
                    entityId + "," + Encode(index < snapshot.ChoiceCardIds.Count ? snapshot.ChoiceCardIds[index] : string.Empty))),
                Encode(snapshot.ReadyReason));
        }

        public static string PickDiscover(int expectedChoiceId, int entityId)
        {
            if (_coroutine == null)
                return "FAIL:no_coroutine";

            return _coroutine.RunAndWait(PickDiscoverCoroutine(expectedChoiceId, entityId), PickTimeoutMs);
        }

        private static IEnumerator<float> PickDiscoverCoroutine(int expectedChoiceId, int entityId)
        {
            InputHook.Simulating = true;

            for (var attempt = 1; attempt <= MaxPickAttempts; attempt++)
            {
                if (!TryBuildDiscoverSnapshot(out var snapshot))
                {
                    _coroutine.SetResult("FAIL:no_discover");
                    yield break;
                }

                if (snapshot.ChoiceId != expectedChoiceId)
                {
                    _coroutine.SetResult("FAIL:choice_mismatch:" + snapshot.ChoiceId);
                    yield break;
                }

                if (!snapshot.Ready)
                {
                    _coroutine.SetResult("FAIL:not_ready:" + Encode(snapshot.ReadyReason));
                    yield break;
                }

                if (!snapshot.CardsByEntityId.TryGetValue(entityId, out var card))
                {
                    _coroutine.SetResult("FAIL:entity_missing:" + entityId);
                    yield break;
                }

                if (!GameObjectFinder.GetObjectScreenPos(card, out var x, out var y))
                {
                    _coroutine.SetResult("FAIL:pos_not_found:" + entityId);
                    yield break;
                }

                foreach (var wait in SmoothMove(x, y, 10, 0.012f))
                    yield return wait;

                yield return ClickSettleDelaySeconds;
                MouseSimulator.LeftDown();
                yield return ClickHoldSeconds;
                MouseSimulator.LeftUp();

                string result = null;
                for (var i = 0; i < ResultPollIterations; i++)
                {
                    yield return ResultPollSeconds;

                    if (!TryBuildDiscoverSnapshot(out var current))
                    {
                        result = "OK:CLOSED";
                        break;
                    }

                    if (current.ChoiceId != expectedChoiceId)
                    {
                        result = "OK:CHAINED:" + current.ChoiceId;
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(result))
                {
                    _coroutine.SetResult(result);
                    yield break;
                }

                if (attempt < MaxPickAttempts)
                    yield return RetryDelaySeconds;
            }

            _coroutine.SetResult("FAIL:TIMEOUT_SAME_CHOICE");
        }

        private static bool TryBuildDiscoverSnapshot(out DiscoverSnapshot snapshot)
        {
            snapshot = null;
            if (!TryReadDiscoverPacket(out var info))
                return false;

            var built = new DiscoverSnapshot
            {
                ChoiceId = info.ChoiceId,
                SourceEntityId = info.SourceEntityId,
                SourceCardId = ResolveCardId(info.SourceEntity)
            };

            built.UiVisible = ReadBool(Call(info.ChoiceCardMgr, "IsFriendlyShown")) || ReadBool(GetFieldOrProp(info.ChoiceCardMgr, "m_friendlyChoicesShown"));

            foreach (var entityId in info.EntityIds)
            {
                built.ChoiceEntityIds.Add(entityId);
                built.ChoiceCardIds.Add(ResolveEntityCardId(info.GameState, entityId));
            }

            built.Ready = IsDiscoverReady(info, built, out var readyReason);
            built.ReadyReason = readyReason;

            snapshot = built;
            return true;
        }

        private static bool TryReadDiscoverPacket(out DiscoverPacketInfo info)
        {
            info = null;
            if (!EnsureTypes())
                return false;

            var gameState = CallStatic(_gameStateType, "Get");
            if (gameState == null)
                return false;

            var choicePacket = Call(gameState, "GetFriendlyEntityChoices");
            if (choicePacket == null)
                return false;

            if (!string.Equals(GetChoiceType(choicePacket), "GENERAL", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!IsSingleChoice(choicePacket))
                return false;

            var choiceCardMgr = CallStatic(_choiceCardMgrType, "Get");
            if (choiceCardMgr == null)
                return false;

            if (ReadBool(Call(choiceCardMgr, "HasSubOption")))
                return false;

            var choiceState = Call(choiceCardMgr, "GetFriendlyChoiceState");
            var sourceEntityId = ToInt(GetFieldOrProp(choicePacket, "Source"));
            if (sourceEntityId <= 0)
                return false;

            var sourceEntity = GetEntity(gameState, sourceEntityId);
            if (sourceEntity == null || !IsDiscoverSource(sourceEntity, choiceState))
                return false;

            var entityIds = ReadIntList(GetFieldOrProp(choicePacket, "Entities"));
            if (entityIds.Count == 0)
                return false;

            info = new DiscoverPacketInfo
            {
                GameState = gameState,
                ChoicePacket = choicePacket,
                ChoiceCardMgr = choiceCardMgr,
                ChoiceState = choiceState,
                SourceEntity = sourceEntity,
                ChoiceId = ToInt(GetFieldOrProp(choicePacket, "ID")),
                SourceEntityId = sourceEntityId
            };

            if (info.ChoiceId <= 0)
                info.ChoiceId = ToInt(GetFieldOrProp(choicePacket, "Id"));

            info.EntityIds.AddRange(entityIds);
            return info.ChoiceId > 0;
        }

        private static bool IsDiscoverSource(object sourceEntity, object choiceState)
        {
            if (sourceEntity == null)
                return false;

            foreach (var negativeTag in NegativeDiscoverTags)
            {
                if (HasEntityTag(sourceEntity, negativeTag))
                    return false;
            }

            if (ReadBool(GetFieldOrProp(choiceState, "m_isSubOptionChoice"))
                || ReadBool(GetFieldOrProp(choiceState, "m_isTitanAbility"))
                || ReadBool(GetFieldOrProp(choiceState, "m_isRewindChoice"))
                || ReadBool(GetFieldOrProp(choiceState, "m_isMagicItemDiscover"))
                || ReadBool(GetFieldOrProp(choiceState, "m_isShopChoice")))
            {
                return false;
            }

            return PositiveDiscoverTags.Any(tag => HasEntityTag(sourceEntity, tag));
        }

        private static bool IsDiscoverReady(DiscoverPacketInfo info, DiscoverSnapshot snapshot, out string reason)
        {
            reason = "ready";

            var choiceState = info.ChoiceState;
            if (choiceState == null)
            {
                reason = "choice_state_null";
                return false;
            }

            if (ReadBool(GetFieldOrProp(choiceState, "m_waitingToStart")))
            {
                reason = "waiting_to_start";
                return false;
            }

            if (!ReadBool(GetFieldOrProp(choiceState, "m_hasBeenRevealed")))
            {
                reason = "not_revealed";
                return false;
            }

            if (ReadBool(GetFieldOrProp(choiceState, "m_hasBeenConcealed")))
            {
                reason = "concealed";
                return false;
            }

            foreach (var entityId in info.EntityIds)
            {
                if (!TryGetFriendlyChoiceCardObject(info, entityId, out var card) || card == null)
                {
                    reason = "card_missing:" + entityId;
                    return false;
                }

                if (!ReadBool(Call(card, "IsActorReady")))
                {
                    reason = "actor_not_ready:" + entityId;
                    return false;
                }

                if (HasActiveTweens(card))
                {
                    reason = "card_tween_active:" + entityId;
                    return false;
                }

                if (!GameObjectFinder.GetObjectScreenPos(card, out _, out _))
                {
                    reason = "pos_not_found:" + entityId;
                    return false;
                }

                snapshot.CardsByEntityId[entityId] = card;
            }

            return true;
        }

        private static bool TryGetFriendlyChoiceCardObject(DiscoverPacketInfo info, int entityId, out object card)
        {
            card = null;
            if (info?.ChoiceCardMgr == null || entityId <= 0)
                return false;

            var friendlyCards = ReadObjectList(Call(info.ChoiceCardMgr, "GetFriendlyCards"));
            if (friendlyCards == null || friendlyCards.Count == 0)
                return false;

            foreach (var candidate in friendlyCards)
            {
                var entity = ResolveChoiceCardEntity(candidate);
                if (ResolveEntityId(entity) != entityId)
                    continue;

                card = candidate;
                return true;
            }

            return false;
        }

        private static bool EnsureTypes()
        {
            if (_gameStateType != null && _choiceCardMgrType != null)
                return true;

            _ctx = ReflectionContext.Instance;
            if (!_ctx.Init())
                return false;

            _asm = _ctx.AsmCSharp;
            _gameStateType = _ctx.GameStateType;
            _choiceCardMgrType = _asm?.GetType("ChoiceCardMgr");
            _iTweenType = _asm?.GetType("iTween")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("iTween"))
                    .FirstOrDefault(type => type != null);

            return _gameStateType != null && _choiceCardMgrType != null;
        }

        private static object Call(object target, string member)
        {
            return target == null ? null : _ctx.CallAny(target, member);
        }

        private static object CallStatic(Type type, string member)
        {
            return type == null ? null : _ctx.CallStaticAny(type, member);
        }

        private static object GetFieldOrProp(object target, string member)
        {
            return target == null ? null : _ctx.GetFieldOrPropertyAny(target, member);
        }

        private static bool IsSingleChoice(object choicePacket)
        {
            var direct = Call(choicePacket, "IsSingleChoice");
            if (direct != null)
                return ReadBool(direct);

            var countMin = ToInt(GetFieldOrProp(choicePacket, "CountMin"));
            var countMax = ToInt(GetFieldOrProp(choicePacket, "CountMax"));
            return countMin == 1 && countMax == 1;
        }

        private static string GetChoiceType(object choicePacket)
        {
            var choiceType = GetFieldOrProp(choicePacket, "ChoiceType");
            return choiceType?.ToString() ?? string.Empty;
        }

        private static object GetEntity(object gameState, int entityId)
        {
            if (gameState == null || entityId <= 0)
                return null;

            try
            {
                var method = gameState.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate =>
                    {
                        if (!string.Equals(candidate.Name, "GetEntity", StringComparison.Ordinal))
                            return false;

                        var parameters = candidate.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
                    });

                return method?.Invoke(gameState, new object[] { entityId });
            }
            catch
            {
                return null;
            }
        }

        private static List<int> ReadIntList(object enumerable)
        {
            var result = new List<int>();
            if (!(enumerable is IEnumerable values))
                return result;

            foreach (var value in values)
            {
                var parsed = ToInt(value);
                if (parsed > 0)
                    result.Add(parsed);
            }

            return result;
        }

        private static List<object> ReadObjectList(object enumerable)
        {
            var result = new List<object>();
            if (!(enumerable is IEnumerable values))
                return result;

            foreach (var value in values)
            {
                if (value != null)
                    result.Add(value);
            }

            return result;
        }

        private static int ResolveEntityId(object entity)
        {
            if (entity == null)
                return 0;

            var entityId = ToInt(GetFieldOrProp(entity, "EntityID"));
            if (entityId > 0)
                return entityId;

            entityId = ToInt(GetFieldOrProp(entity, "EntityId"));
            if (entityId > 0)
                return entityId;

            return ToInt(GetFieldOrProp(entity, "m_entityId"));
        }

        private static object ResolveChoiceCardEntity(object card)
        {
            if (card == null)
                return null;

            return Call(card, "GetEntity")
                ?? GetFieldOrProp(card, "Entity")
                ?? GetFieldOrProp(card, "m_entity")
                ?? card;
        }

        private static string ResolveEntityCardId(object gameState, int entityId)
        {
            var entity = GetEntity(gameState, entityId);
            return ResolveCardId(entity);
        }

        private static string ResolveCardId(object source)
        {
            if (source == null)
                return string.Empty;

            var direct = Call(source, "GetCardId")
                ?? GetFieldOrProp(source, "CardId")
                ?? GetFieldOrProp(source, "m_cardId")
                ?? GetFieldOrProp(source, "ID");
            if (direct != null)
            {
                var cardId = direct.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(cardId))
                    return cardId;
            }

            var card = Call(source, "GetCard");
            if (card != null && !ReferenceEquals(card, source))
                return ResolveCardId(card);

            var entity = Call(source, "GetEntity");
            if (entity != null && !ReferenceEquals(entity, source))
                return ResolveCardId(entity);

            return string.Empty;
        }

        private static bool HasEntityTag(object entity, string tagName)
        {
            return _ctx.GetTagValue(entity, tagName) > 0;
        }

        private static bool HasActiveTweens(object card)
        {
            if (_iTweenType == null || card == null)
                return false;

            var cardGameObject = GetFieldOrProp(card, "gameObject");
            if (cardGameObject == null)
                return false;

            try
            {
                var countMethod = _iTweenType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "Count", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var parameters = method.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(cardGameObject);
                    });
                if (countMethod != null)
                    return ToInt(countMethod.Invoke(null, new[] { cardGameObject })) > 0;

                var hasTweenMethod = _iTweenType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "HasTween", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var parameters = method.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(cardGameObject);
                    });
                return hasTweenMethod != null && ReadBool(hasTweenMethod.Invoke(null, new[] { cardGameObject }));
            }
            catch
            {
                return false;
            }
        }

        private static int ToInt(object value)
        {
            return _ctx?.ToInt(value) ?? 0;
        }

        private static bool ReadBool(object value)
        {
            if (value is bool direct)
                return direct;

            if (value == null)
                return false;

            try
            {
                return Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        private static string Encode(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("|", "/").Replace(";", ",");
        }

        private static IEnumerable<float> SmoothMove(int targetX, int targetY, int steps, float stepDelay)
        {
            var startX = MouseSimulator.CurX;
            var startY = MouseSimulator.CurY;
            for (var i = 1; i <= steps; i++)
            {
                var t = (float)i / steps;
                var ease = t < 0.5f
                    ? 2f * t * t
                    : 1f - (float)Math.Pow(-2f * t + 2f, 2) / 2f;
                MouseSimulator.MoveTo(
                    startX + (int)((targetX - startX) * ease),
                    startY + (int)((targetY - startY) * ease));
                yield return stepDelay;
            }
        }
    }
}
