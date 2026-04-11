using System;

namespace BotMain
{
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
    }
}
