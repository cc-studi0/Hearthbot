using System;
using System.Collections.Generic;
using System.Linq;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain
{
    internal static class SeedCompatibility
    {
        private const int LegacyDeckCardListPartIndex = 31;
        private const int CardTypeTag = 202;
        private const int CardTypeHero = 3;
        private const int CardTypeMinion = 4;
        private const int CardTypeSpell = 5;
        private const int CardTypeEnchantment = 6;
        private const int CardTypeWeapon = 7;
        private const int CardTypeItem = 8;
        private const int CardTypeToken = 9;
        private const int CardTypeHeroPower = 10;
        private const int CardTypeLocation = 39;
        private const int CardTypeBattlegroundSpell = 42;

        private const string HeroPlaceholder = "HERO_01";
        private const string HeroPowerPlaceholder = "HERO_01bp";
        private const string MinionPlaceholder = "CORE_CS2_231";
        private const string SpellPlaceholder = "CORE_CS2_029";
        private const string WeaponPlaceholder = "VAN_CS2_106";
        private const string LocationPlaceholder = "VAC_929";

        private static readonly int[] SingleEntityParts = { 12, 13, 14, 15, 16, 17 };
        private static readonly int[] EntityListParts = { 18, 19, 20 };
        private static readonly int[] MinionCardIdListParts = { 22, 23, 24, 25 };
        private static readonly int[] SpellCardIdListParts = { 21 };
        private static readonly int[] SingleSpellCardIdParts = { 44, 48 };

        internal static string GetCompatibleSeed(string seed, out string detail)
        {
            detail = string.Empty;
            var rawSeed = seed ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawSeed))
                return rawSeed;

            var parts = rawSeed.Split('~');
            if (parts.Length < 21)
                return rawSeed;

            var changes = new List<string>();
            if (parts.Length > LegacyDeckCardListPartIndex)
                TryNormalizeLegacyDeckCardList(parts, changes);

            foreach (var index in SingleEntityParts)
            {
                if (index < parts.Length)
                    parts[index] = SanitizeEntity(parts[index], index, changes);
            }

            foreach (var index in EntityListParts)
            {
                if (index < parts.Length)
                    parts[index] = SanitizeEntityList(parts[index], index, changes);
            }

            foreach (var index in MinionCardIdListParts)
            {
                if (index < parts.Length)
                    parts[index] = SanitizeCardIdList(parts[index], index, MinionPlaceholder, changes);
            }

            foreach (var index in SpellCardIdListParts)
            {
                if (index < parts.Length)
                    parts[index] = SanitizeCardIdList(parts[index], index, SpellPlaceholder, changes);
            }

            foreach (var index in SingleSpellCardIdParts)
            {
                if (index < parts.Length)
                    parts[index] = SanitizeSingleCardId(parts[index], index, SpellPlaceholder, changes);
            }

            if (changes.Count == 0)
                return rawSeed;

            detail = BuildDetail(changes);
            return string.Join("~", parts);
        }

        private static bool TryNormalizeLegacyDeckCardList(string[] parts, List<string> changes)
        {
            if (parts == null
                || parts.Length <= LegacyDeckCardListPartIndex
                || changes == null)
            {
                return false;
            }

            var raw = parts[LegacyDeckCardListPartIndex];
            if (!LooksLikeLegacyDeckCardList(raw, out var itemCount, out var preview))
                return false;

            parts[LegacyDeckCardListPartIndex] = string.Empty;
            changes.Add(
                string.IsNullOrWhiteSpace(preview)
                    ? $"legacy_deck_list_removed@p{LegacyDeckCardListPartIndex}(count={itemCount})"
                    : $"legacy_deck_list_removed@p{LegacyDeckCardListPartIndex}(count={itemCount},preview={preview})");
            return true;
        }

        private static bool LooksLikeLegacyDeckCardList(string raw, out int itemCount, out string preview)
        {
            itemCount = 0;
            preview = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var items = raw
                .Split('|')
                .Select(item => item?.Trim() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            if (items.Length == 0 || items.Any(item => !LooksLikeLegacyDeckCardToken(item)))
                return false;

            itemCount = items.Length;
            preview = string.Join(",", items.Take(4));
            return true;
        }

        private static bool LooksLikeLegacyDeckCardToken(string raw)
        {
            var token = raw?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token)
                || token == "0"
                || string.Equals(token, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "False", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (token.IndexOfAny(new[] { '*', '~', '&', '=' }) >= 0)
                return false;

            return token.Contains("_");
        }

        private static string SanitizeEntityList(string raw, int partIndex, List<string> replacements)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "0")
                return raw;

            var changed = false;
            var items = raw.Split('|');
            for (var i = 0; i < items.Length; i++)
            {
                var sanitized = SanitizeEntity(items[i], partIndex, replacements, i + 1);
                if (!string.Equals(sanitized, items[i], StringComparison.Ordinal))
                {
                    items[i] = sanitized;
                    changed = true;
                }
            }

            return changed ? string.Join("|", items) : raw;
        }

        private static string SanitizeEntity(string raw, int partIndex, List<string> replacements, int entityIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "0")
                return raw;

            var fields = raw.Split('*');
            if (fields.Length == 0)
                return raw;

            var cardId = fields[0]?.Trim();
            if (string.IsNullOrWhiteSpace(cardId) || IsKnownCardId(cardId))
                return raw;

            var cardType = TryReadCardType(fields);
            var placeholder = ResolvePlaceholder(partIndex, cardType);
            if (string.IsNullOrWhiteSpace(placeholder)
                || !IsKnownCardId(placeholder)
                || string.Equals(cardId, placeholder, StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }

            fields[0] = placeholder;
            replacements.Add(BuildReplacement(cardId, placeholder, partIndex, entityIndex, cardType));
            return string.Join("*", fields);
        }

        private static string SanitizeCardIdList(string raw, int partIndex, string placeholder, List<string> replacements)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "0")
                return raw;

            var changed = false;
            var items = raw.Split('|');
            for (var i = 0; i < items.Length; i++)
            {
                var sanitized = SanitizeSingleCardId(items[i], partIndex, placeholder, replacements, i + 1);
                if (!string.Equals(sanitized, items[i], StringComparison.Ordinal))
                {
                    items[i] = sanitized;
                    changed = true;
                }
            }

            return changed ? string.Join("|", items) : raw;
        }

        private static string SanitizeSingleCardId(string raw, int partIndex, string placeholder, List<string> replacements, int entityIndex = 0)
        {
            var cardId = raw?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cardId) || cardId == "0" || IsKnownCardId(cardId))
                return raw;

            if (string.IsNullOrWhiteSpace(placeholder) || !IsKnownCardId(placeholder))
                return raw;

            replacements.Add(BuildReplacement(cardId, placeholder, partIndex, entityIndex, null));
            return placeholder;
        }

        private static int? TryReadCardType(string[] fields)
        {
            if (fields == null || fields.Length <= 38)
                return null;

            var tags = fields[38];
            if (string.IsNullOrWhiteSpace(tags))
                return null;

            foreach (var entry in tags.Split('&'))
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                var separatorIndex = entry.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                if (!int.TryParse(entry.Substring(0, separatorIndex), out var tagId)
                    || tagId != CardTypeTag)
                {
                    continue;
                }

                if (int.TryParse(entry.Substring(separatorIndex + 1), out var tagValue))
                    return tagValue;
            }

            return null;
        }

        private static string ResolvePlaceholder(int partIndex, int? cardType)
        {
            switch (partIndex)
            {
                case 12:
                case 13:
                    return WeaponPlaceholder;
                case 14:
                case 15:
                    return HeroPlaceholder;
                case 16:
                case 17:
                    return HeroPowerPlaceholder;
            }

            switch (cardType ?? 0)
            {
                case CardTypeHero:
                    return HeroPlaceholder;
                case CardTypeMinion:
                case CardTypeToken:
                    return MinionPlaceholder;
                case CardTypeSpell:
                case CardTypeEnchantment:
                case CardTypeItem:
                case CardTypeBattlegroundSpell:
                    return SpellPlaceholder;
                case CardTypeWeapon:
                    return WeaponPlaceholder;
                case CardTypeHeroPower:
                    return HeroPowerPlaceholder;
                case CardTypeLocation:
                    return LocationPlaceholder;
            }

            if (partIndex == 18 || partIndex == 19)
                return MinionPlaceholder;

            if (partIndex == 20)
                return MinionPlaceholder;

            return MinionPlaceholder;
        }

        private static bool IsKnownCardId(string cardId)
        {
            return !string.IsNullOrWhiteSpace(cardId)
                && Enum.TryParse(cardId.Trim(), true, out ApiCard.Cards parsed)
                && parsed != 0;
        }

        private static string BuildReplacement(string source, string target, int partIndex, int entityIndex, int? cardType)
        {
            var location = $"p{partIndex}";
            if (entityIndex > 0)
                location += $"#{entityIndex}";

            return cardType.HasValue
                ? $"{source}->{target}@{location}/type={cardType.Value}"
                : $"{source}->{target}@{location}";
        }

        private static string BuildDetail(List<string> replacements)
        {
            var preview = replacements
                .Where(replacement => !string.IsNullOrWhiteSpace(replacement))
                .Take(5)
                .ToArray();
            var suffix = replacements.Count > preview.Length ? ",..." : string.Empty;
            return $"seed_compat changes={replacements.Count} [{string.Join("; ", preview)}{suffix}]";
        }
    }
}
