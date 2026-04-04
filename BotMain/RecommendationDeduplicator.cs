using System;
using System.Collections.Generic;

namespace BotMain
{
    /// <summary>
    /// SmartBot 风格的推荐去重器。用 key-based HashSet 记住整局已成功执行的推荐。
    /// 与 SmartBot 的区别：操作确认成功后才标记已消费，失败可重试。
    /// </summary>
    internal sealed class RecommendationDeduplicator
    {
        private readonly HashSet<string> _knownKeys = new(StringComparer.Ordinal);
        private readonly object _lock = new();

        /// <summary>
        /// 检查推荐是否已被成功执行过。
        /// </summary>
        public bool IsKnown(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            lock (_lock) { return _knownKeys.Contains(key); }
        }

        /// <summary>
        /// 操作确认成功后调用，标记该推荐为已消费。
        /// </summary>
        public void MarkConsumed(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_lock) { _knownKeys.Add(key); }
        }

        /// <summary>
        /// 对局开始/结束时清空所有已知 key。
        /// </summary>
        public void Clear()
        {
            lock (_lock) { _knownKeys.Clear(); }
        }

        /// <summary>当前已知 key 数量（用于日志）。</summary>
        public int Count
        {
            get { lock (_lock) { return _knownKeys.Count; } }
        }

        /// <summary>
        /// 从推荐的 PayloadSignature 和首个动作命令构建去重 key。
        /// </summary>
        public static string BuildKey(string payloadSignature, string firstActionCommand)
        {
            return $"{payloadSignature ?? ""}|{firstActionCommand ?? ""}";
        }

        /// <summary>
        /// 从推荐的 PayloadSignature 和动作列表构建去重 key。
        /// </summary>
        public static string BuildKey(string payloadSignature, IReadOnlyList<string> actions)
        {
            var firstAction = actions != null && actions.Count > 0 ? actions[0] : "";
            return BuildKey(payloadSignature, firstAction);
        }
    }
}
