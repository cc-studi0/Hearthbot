using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BotMain
{
    internal enum BgZone { Shop, Hand, Board }

    internal readonly struct BgZoneEntry
    {
        public readonly int Position;
        public readonly int EntityId;
        public readonly string CardId;

        public BgZoneEntry(int position, int entityId, string cardId)
        {
            Position = position;
            EntityId = entityId;
            CardId = cardId ?? string.Empty;
        }
    }

    internal sealed class BgZoneSnapshot
    {
        public IReadOnlyDictionary<int, BgZoneEntry> Shop { get; }
        public IReadOnlyDictionary<int, BgZoneEntry> Hand { get; }
        public IReadOnlyDictionary<int, BgZoneEntry> Board { get; }

        public BgZoneSnapshot(
            IReadOnlyDictionary<int, BgZoneEntry> shop,
            IReadOnlyDictionary<int, BgZoneEntry> hand,
            IReadOnlyDictionary<int, BgZoneEntry> board)
        {
            Shop = shop;
            Hand = hand;
            Board = board;
        }

        private IReadOnlyDictionary<int, BgZoneEntry> GetZone(BgZone zone)
        {
            switch (zone)
            {
                case BgZone.Shop: return Shop;
                case BgZone.Hand: return Hand;
                case BgZone.Board: return Board;
                default: return Shop;
            }
        }

        public BgZoneEntry? FindByCardId(BgZone zone, string cardId, int originalPosition)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return null;

            var map = GetZone(zone);
            BgZoneEntry? best = null;
            var bestDistance = int.MaxValue;

            foreach (var entry in map.Values)
            {
                if (!string.Equals(entry.CardId, cardId, StringComparison.Ordinal))
                    continue;

                var distance = Math.Abs(entry.Position - originalPosition);
                if (distance < bestDistance
                    || (distance == bestDistance && (best == null || entry.Position < best.Value.Position)))
                {
                    best = entry;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }

    internal enum BgCommandKind
    {
        Buy,
        Sell,
        Play,
        Other
    }

    internal readonly struct BgCommandSpec
    {
        public readonly BgCommandKind Kind;
        public readonly int EntityId;
        public readonly int Position;
        public readonly int TargetEntityId;
        public readonly string ExpectedCardId;
        public readonly string Raw;

        public BgCommandSpec(BgCommandKind kind, int entityId, int position, int targetEntityId, string expectedCardId, string raw)
        {
            Kind = kind;
            EntityId = entityId;
            Position = position;
            TargetEntityId = targetEntityId;
            ExpectedCardId = expectedCardId ?? string.Empty;
            Raw = raw ?? string.Empty;
        }
    }

    internal enum BgResolutionOutcome
    {
        AsIs,
        Retargeted,
        Aborted
    }

    internal readonly struct BgResolution
    {
        public readonly BgResolutionOutcome Outcome;
        public readonly string RewrittenCommand;
        public readonly string Detail;

        public BgResolution(BgResolutionOutcome outcome, string rewrittenCommand, string detail)
        {
            Outcome = outcome;
            RewrittenCommand = rewrittenCommand ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    internal static class BgExecutionGate
    {
        public static BgCommandSpec ParseCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new BgCommandSpec(BgCommandKind.Other, 0, 0, 0, string.Empty, raw);

            var parts = raw.Split('|');
            var head = parts[0].Trim();

            int TryInt(int idx) => parts.Length > idx && int.TryParse(parts[idx], out var v) ? v : 0;
            string TryStr(int idx) => parts.Length > idx ? parts[idx] : string.Empty;

            switch (head)
            {
                case "BG_BUY":
                    return new BgCommandSpec(BgCommandKind.Buy,
                        entityId: TryInt(1),
                        position: TryInt(2),
                        targetEntityId: 0,
                        expectedCardId: TryStr(3),
                        raw: raw);
                case "BG_SELL":
                    return new BgCommandSpec(BgCommandKind.Sell,
                        entityId: TryInt(1),
                        position: 0,
                        targetEntityId: 0,
                        expectedCardId: TryStr(2),
                        raw: raw);
                case "BG_PLAY":
                    return new BgCommandSpec(BgCommandKind.Play,
                        entityId: TryInt(1),
                        position: TryInt(3),
                        targetEntityId: TryInt(2),
                        expectedCardId: TryStr(4),
                        raw: raw);
                default:
                    return new BgCommandSpec(BgCommandKind.Other, 0, 0, 0, string.Empty, raw);
            }
        }

        public static BgZoneSnapshot ParseZones(string stateData)
        {
            var shop = ExtractZone(stateData, "|SHOP=");
            var hand = ExtractZone(stateData, "|HAND=");
            var board = ExtractZone(stateData, "|BOARD=");
            return new BgZoneSnapshot(shop, hand, board);
        }

        private static IReadOnlyDictionary<int, BgZoneEntry> ExtractZone(string stateData, string prefix)
        {
            var result = new Dictionary<int, BgZoneEntry>();
            if (string.IsNullOrEmpty(stateData))
                return result;

            var start = stateData.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
                return result;

            start += prefix.Length;
            var end = stateData.IndexOf('|', start);
            var segment = end >= 0 ? stateData.Substring(start, end - start) : stateData.Substring(start);

            var items = segment.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var fields = item.Split(',');
                if (fields.Length < 6) continue;
                if (!int.TryParse(fields[0], out var entityId)) continue;
                var cardId = fields[1];
                if (!int.TryParse(fields[5], out var pos) || pos <= 0) continue;
                result[pos] = new BgZoneEntry(pos, entityId, cardId);
            }

            return result;
        }

        public static BgResolution Resolve(BgCommandSpec spec, BgZoneSnapshot snap)
        {
            switch (spec.Kind)
            {
                case BgCommandKind.Buy: return ResolveBuy(spec, snap);
                case BgCommandKind.Sell: return ResolveSell(spec, snap);
                case BgCommandKind.Play: return ResolvePlay(spec, snap);
                default: return new BgResolution(BgResolutionOutcome.AsIs, spec.Raw, "other");
            }
        }

        private static BgResolution ResolveBuy(BgCommandSpec spec, BgZoneSnapshot snap)
        {
            if (string.IsNullOrEmpty(spec.ExpectedCardId))
                return new BgResolution(BgResolutionOutcome.AsIs, spec.Raw, "legacy");

            if (snap.Shop.TryGetValue(spec.Position, out var byPos)
                && byPos.EntityId == spec.EntityId
                && string.Equals(byPos.CardId, spec.ExpectedCardId, StringComparison.Ordinal))
            {
                return new BgResolution(BgResolutionOutcome.AsIs, spec.Raw, "match");
            }

            var hit = snap.FindByCardId(BgZone.Shop, spec.ExpectedCardId, spec.Position);
            if (!hit.HasValue)
                return new BgResolution(BgResolutionOutcome.Aborted, string.Empty, "card_missing");

            var rewritten = $"BG_BUY|{hit.Value.EntityId}|{hit.Value.Position}|{spec.ExpectedCardId}";
            return new BgResolution(BgResolutionOutcome.Retargeted, rewritten, "retarget");
        }

        private static BgResolution ResolveSell(BgCommandSpec spec, BgZoneSnapshot snap)
        {
            if (string.IsNullOrEmpty(spec.ExpectedCardId))
                return new BgResolution(BgResolutionOutcome.AsIs, spec.Raw, "legacy");

            foreach (var entry in snap.Board.Values)
            {
                if (entry.EntityId == spec.EntityId
                    && string.Equals(entry.CardId, spec.ExpectedCardId, StringComparison.Ordinal))
                {
                    return new BgResolution(BgResolutionOutcome.AsIs, spec.Raw, "match");
                }
            }

            var hit = snap.FindByCardId(BgZone.Board, spec.ExpectedCardId, 0);
            if (!hit.HasValue)
                return new BgResolution(BgResolutionOutcome.Aborted, string.Empty, "card_missing");

            var rewritten = $"BG_SELL|{hit.Value.EntityId}|{spec.ExpectedCardId}";
            return new BgResolution(BgResolutionOutcome.Retargeted, rewritten, "retarget");
        }

        private static BgResolution ResolvePlay(BgCommandSpec spec, BgZoneSnapshot snap)
        {
            if (string.IsNullOrEmpty(spec.ExpectedCardId))
                return new BgResolution(BgResolutionOutcome.AsIs, spec.Raw, "legacy");

            foreach (var entry in snap.Hand.Values)
            {
                if (entry.EntityId == spec.EntityId
                    && string.Equals(entry.CardId, spec.ExpectedCardId, StringComparison.Ordinal))
                {
                    return new BgResolution(BgResolutionOutcome.AsIs, spec.Raw, "match");
                }
            }

            var hit = snap.FindByCardId(BgZone.Hand, spec.ExpectedCardId, spec.Position);
            if (!hit.HasValue)
                return new BgResolution(BgResolutionOutcome.Aborted, string.Empty, "card_missing");

            var rewritten = $"BG_PLAY|{hit.Value.EntityId}|{spec.TargetEntityId}|{spec.Position}|{spec.ExpectedCardId}";
            return new BgResolution(BgResolutionOutcome.Retargeted, rewritten, "retarget");
        }
    }

    internal enum BgGateOutcome
    {
        Completed,
        CompletedWithFallback,
        Retargeted,
        Aborted,
        Failed
    }

    internal readonly struct BgGateResult
    {
        public readonly BgGateOutcome Outcome;
        public readonly string ExecutedCommand;
        public readonly string Detail;

        public BgGateResult(BgGateOutcome outcome, string executedCommand, string detail)
        {
            Outcome = outcome;
            ExecutedCommand = executedCommand ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    internal sealed class BgExecutionGateRunner
    {
        private readonly Func<string, string> _send;
        private readonly Func<string> _readState;
        private readonly int _probeTimeoutMs;
        private readonly int _probeIntervalMs;
        private readonly int _fallbackSleepMs;
        private readonly Action<int> _sleep;

        public BgExecutionGateRunner(
            Func<string, string> send,
            Func<string> readState,
            int probeTimeoutMs,
            int probeIntervalMs,
            int fallbackSleepMs,
            Action<int> sleep)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _readState = readState ?? throw new ArgumentNullException(nameof(readState));
            _probeTimeoutMs = probeTimeoutMs;
            _probeIntervalMs = probeIntervalMs;
            _fallbackSleepMs = fallbackSleepMs;
            _sleep = sleep ?? (_ => { });
        }

        public BgGateResult Execute(string rawCommand)
        {
            var spec = BgExecutionGate.ParseCommand(rawCommand);
            var beforeState = _readState() ?? string.Empty;
            var beforeSnap = BgExecutionGate.ParseZones(beforeState);
            var resolution = BgExecutionGate.Resolve(spec, beforeSnap);

            if (resolution.Outcome == BgResolutionOutcome.Aborted)
                return new BgGateResult(BgGateOutcome.Aborted, string.Empty, "resolve:" + resolution.Detail);

            var cmdToSend = resolution.RewrittenCommand;
            var resp = _send(cmdToSend);
            if (IsFailure(resp))
                return new BgGateResult(BgGateOutcome.Failed, cmdToSend, "send:" + (resp ?? "null"));

            var probeHit = Probe(spec, beforeSnap, beforeState);
            var baseOutcome = resolution.Outcome == BgResolutionOutcome.Retargeted
                ? BgGateOutcome.Retargeted
                : (probeHit ? BgGateOutcome.Completed : BgGateOutcome.CompletedWithFallback);

            if (!probeHit)
                _sleep(_fallbackSleepMs);

            return new BgGateResult(baseOutcome, cmdToSend, probeHit ? "probe:hit" : "probe:timeout");
        }

        private bool Probe(BgCommandSpec spec, BgZoneSnapshot beforeSnap, string beforeState)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < _probeTimeoutMs)
            {
                var current = _readState() ?? string.Empty;
                if (!string.Equals(current, beforeState, StringComparison.Ordinal))
                {
                    if (spec.Kind == BgCommandKind.Other)
                        return true;

                    var curSnap = BgExecutionGate.ParseZones(current);
                    if (HasExpectedChange(spec, beforeSnap, curSnap))
                    {
                        if (spec.Kind == BgCommandKind.Buy)
                            WaitForShopStability(current);
                        return true;
                    }
                }
                _sleep(_probeIntervalMs);
            }
            return false;
        }

        private void WaitForShopStability(string stateAfterAction)
        {
            _sleep(80);
            var check = _readState() ?? string.Empty;
            if (string.Equals(check, stateAfterAction, StringComparison.Ordinal))
                return;

            // 商店在买成功后又变了（游戏特效自动刷新），等它稳定
            for (var i = 0; i < 10; i++)
            {
                _sleep(50);
                var next = _readState() ?? string.Empty;
                if (string.Equals(next, check, StringComparison.Ordinal))
                    return;
                check = next;
            }
        }

        private static bool HasExpectedChange(BgCommandSpec spec, BgZoneSnapshot before, BgZoneSnapshot after)
        {
            switch (spec.Kind)
            {
                case BgCommandKind.Buy:
                    return !TryFind(after.Shop, spec.Position, spec.ExpectedCardId)
                        || after.Hand.Count > before.Hand.Count;
                case BgCommandKind.Sell:
                    return after.Board.Count < before.Board.Count
                        || !ContainsEntity(after.Board, spec.EntityId);
                case BgCommandKind.Play:
                    return !TryFind(after.Hand, spec.Position, spec.ExpectedCardId)
                        || after.Board.Count > before.Board.Count;
                default:
                    return true;
            }
        }

        private static bool TryFind(IReadOnlyDictionary<int, BgZoneEntry> map, int pos, string expectedCardId)
        {
            if (!map.TryGetValue(pos, out var entry)) return false;
            if (string.IsNullOrEmpty(expectedCardId)) return true;
            return string.Equals(entry.CardId, expectedCardId, StringComparison.Ordinal);
        }

        private static bool ContainsEntity(IReadOnlyDictionary<int, BgZoneEntry> map, int entityId)
        {
            foreach (var e in map.Values)
                if (e.EntityId == entityId) return true;
            return false;
        }

        private static bool IsFailure(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp)) return true;
            return resp.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                || resp.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resp, "NO_RESPONSE", StringComparison.OrdinalIgnoreCase);
        }
    }
}
