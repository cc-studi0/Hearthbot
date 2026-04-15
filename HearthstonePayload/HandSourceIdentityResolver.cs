using System;

namespace HearthstonePayload
{
    internal sealed class HandSourceIdentityExpectation
    {
        public int SourceEntityId;
        public string SourceCardId = string.Empty;
        public int SourceZonePosition;
    }

    internal sealed class HandSourceIdentitySnapshot
    {
        public int EntityId;
        public bool InFriendlyHand;
        public string CardId = string.Empty;
        public int ZonePosition;
    }

    internal sealed class HandSourceIdentityResolution
    {
        public bool Success;
        public string Detail = string.Empty;
    }

    internal static class HandSourceIdentityResolver
    {
        public static HandSourceIdentityResolution Validate(
            HandSourceIdentityExpectation expected,
            HandSourceIdentitySnapshot actual)
        {
            if (expected == null)
            {
                return new HandSourceIdentityResolution
                {
                    Success = true,
                    Detail = "no_expectation"
                };
            }

            if (actual == null || !actual.InFriendlyHand)
            {
                return new HandSourceIdentityResolution
                {
                    Success = false,
                    Detail = "entity_left_hand"
                };
            }

            var cardMatches = string.Equals(
                expected.SourceCardId ?? string.Empty,
                actual.CardId ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            var slotMatches = expected.SourceZonePosition > 0
                && expected.SourceZonePosition == actual.ZonePosition;

            if (cardMatches && slotMatches)
            {
                return new HandSourceIdentityResolution
                {
                    Success = true,
                    Detail = "exact_match"
                };
            }

            if (cardMatches)
            {
                return new HandSourceIdentityResolution
                {
                    Success = false,
                    Detail = "slot_changed"
                };
            }

            if (slotMatches)
            {
                return new HandSourceIdentityResolution
                {
                    Success = false,
                    Detail = "card_changed"
                };
            }

            return new HandSourceIdentityResolution
            {
                Success = false,
                Detail = "card_and_slot_changed"
            };
        }
    }
}
