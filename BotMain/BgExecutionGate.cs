using System;
using System.Collections.Generic;

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
    }
}
