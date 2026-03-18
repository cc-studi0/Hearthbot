using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SmartBot.Plugins.API;
using ApiCard = SmartBot.Plugins.API.Card;

namespace BotMain.Learning
{
    internal static class LearnedStrategyFeatureExtractor
    {
        internal const string AnyBoardBucket = "ANY";

        internal static string ComputeDeckSignature(IEnumerable<ApiCard.Cards> cards)
        {
            if (cards == null)
                return string.Empty;

            var normalized = cards
                .Where(card => card != 0)
                .Select(card => card.ToString())
                .OrderBy(cardId => cardId, StringComparer.Ordinal)
                .ToArray();
            if (normalized.Length == 0)
                return string.Empty;

            return HashComposite(normalized);
        }

        internal static string ComputeDeckSignatureFromCardIds(IEnumerable<string> cardIds)
        {
            if (cardIds == null)
                return string.Empty;

            var normalized = cardIds
                .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .Select(cardId => cardId.Trim())
                .OrderBy(cardId => cardId, StringComparer.Ordinal)
                .ToArray();
            if (normalized.Length == 0)
                return string.Empty;

            return HashComposite(normalized);
        }

        internal static string NormalizeDeckSignature(string deckSignature, IEnumerable<ApiCard.Cards> fallbackCards)
        {
            if (!string.IsNullOrWhiteSpace(deckSignature))
                return deckSignature.Trim();

            return ComputeDeckSignature(fallbackCards);
        }

        internal static string BuildBoardFingerprint(string seed)
        {
            return string.IsNullOrWhiteSpace(seed)
                ? string.Empty
                : HashComposite(seed.Trim());
        }

        internal static string BuildBoardBucket(Board board, IReadOnlyList<ApiCard.Cards> remainingDeckCards = null)
        {
            if (board == null)
                return string.Empty;

            var friendHeroHealth = Math.Max(0, (board.HeroFriend?.CurrentHealth ?? 0) + (board.HeroFriend?.CurrentArmor ?? 0));
            var enemyHeroHealth = Math.Max(0, (board.HeroEnemy?.CurrentHealth ?? 0) + (board.HeroEnemy?.CurrentArmor ?? 0));
            var enemyPotentialDamage = (board.MinionEnemy ?? new List<Card>())
                .Where(card => card != null && card.CurrentHealth > 0 && card.CurrentAtk > 0 && !card.IsFrozen)
                .Sum(card => Math.Max(0, card.CurrentAtk));
            enemyPotentialDamage += Math.Max(0, board.HeroEnemy?.CurrentAtk ?? 0);
            if (board.WeaponEnemy != null && board.WeaponEnemy.CurrentHealth > 0)
                enemyPotentialDamage += Math.Max(0, board.WeaponEnemy.CurrentAtk);

            var friendBoardCount = board.MinionFriend?.Count ?? 0;
            var enemyBoardCount = board.MinionEnemy?.Count ?? 0;
            var handCount = board.Hand?.Count ?? 0;
            var remainingCount = remainingDeckCards?.Count ?? 0;
            var pressureBucket = enemyPotentialDamage >= friendHeroHealth
                ? "L"
                : enemyPotentialDamage * 2 >= Math.Max(1, friendHeroHealth)
                    ? "H"
                    : enemyPotentialDamage > 0 ? "M" : "N";

            return string.Join(
                "|",
                "T" + Math.Max(0, Math.Min(15, board.TurnCount)),
                "M" + Math.Max(0, Math.Min(10, board.ManaAvailable)),
                "F" + Math.Max(0, Math.Min(7, friendBoardCount)),
                "E" + Math.Max(0, Math.Min(7, enemyBoardCount)),
                "H" + Math.Max(0, Math.Min(10, handCount)),
                "R" + Math.Max(0, Math.Min(30, remainingCount)),
                "FH" + BucketHealth(friendHeroHealth),
                "EH" + BucketHealth(enemyHeroHealth),
                "P" + pressureBucket);
        }

        internal static string BuildMulliganSnapshotSignature(MulliganLearningSample sample)
        {
            if (sample == null)
                return string.Empty;

            var offeredCards = sample.Choices?
                .Select(choice => choice?.CardId ?? string.Empty)
                .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .OrderBy(cardId => cardId, StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();

            return HashComposite(
                sample.DeckSignature ?? string.Empty,
                sample.OwnClass.ToString(),
                sample.EnemyClass.ToString(),
                sample.HasCoin ? "coin" : "no_coin",
                string.Join(",", offeredCards));
        }

        internal static string BuildChoiceSnapshotSignature(ChoiceLearningSample sample)
        {
            if (sample == null)
                return string.Empty;

            var options = sample.Options?
                .Select(option => option?.CardId ?? string.Empty)
                .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .OrderBy(cardId => cardId, StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            var picked = sample.TeacherSelectedEntityIds?
                .OrderBy(entityId => entityId)
                .Select(entityId => entityId.ToString())
                .ToArray() ?? Array.Empty<string>();

            return HashComposite(
                sample.PayloadSignature ?? string.Empty,
                sample.DeckSignature ?? string.Empty,
                sample.Mode ?? string.Empty,
                sample.OriginCardId ?? string.Empty,
                string.Join(",", options),
                string.Join(",", picked));
        }

        internal static bool TryDescribeAction(
            Board board,
            string actionText,
            IReadOnlyList<ApiCard.Cards> remainingDeckCards,
            out LearnedActionObservation observation)
        {
            observation = null;
            if (board == null || string.IsNullOrWhiteSpace(actionText))
                return false;

            var trimmed = actionText.Trim();
            var parts = trimmed.Split('|');
            if (parts.Length == 0)
                return false;

            var actionName = parts[0].Trim().ToUpperInvariant();
            if (actionName == "END_TURN" || actionName == "OPTION")
                return false;

            var sourceEntityId = TryParseEntityId(parts, 1);
            var targetEntityId = TryParseEntityId(parts, 2);
            var source = ResolveSource(board, actionName, sourceEntityId);
            var target = ResolveTarget(board, targetEntityId);
            var sourceCardId = actionName == "ATTACK" && IsFriendlyHero(board, sourceEntityId)
                ? GetCardId(board.WeaponFriend)
                : GetCardId(source);
            var targetCardId = GetCardId(target);

            var scope = LearnedActionScope.Unknown;
            switch (actionName)
            {
                case "PLAY":
                    scope = DescribePlayScope(source);
                    break;
                case "TRADE":
                    scope = LearnedActionScope.Trade;
                    break;
                case "HERO_POWER":
                    scope = LearnedActionScope.HeroPower;
                    break;
                case "USE_LOCATION":
                    scope = LearnedActionScope.UseLocation;
                    break;
                case "ATTACK":
                    scope = IsFriendlyHero(board, sourceEntityId)
                        ? LearnedActionScope.WeaponAttack
                        : LearnedActionScope.AttackOrder;
                    break;
            }

            if (scope == LearnedActionScope.Unknown || string.IsNullOrWhiteSpace(sourceCardId))
                return false;

            observation = new LearnedActionObservation
            {
                Scope = scope,
                BoardBucket = BuildBoardBucket(board, remainingDeckCards),
                SourceCardId = sourceCardId,
                TargetCardId = targetCardId ?? string.Empty,
                ActionText = trimmed
            };
            return true;
        }

        internal static string HashComposite(params string[] parts)
        {
            var payload = string.Join("||", parts ?? Array.Empty<string>());
            if (string.IsNullOrWhiteSpace(payload))
                return string.Empty;

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes);
        }

        private static string BucketHealth(int health)
        {
            if (health <= 5)
                return "0";
            if (health <= 10)
                return "1";
            if (health <= 20)
                return "2";
            return "3";
        }

        private static int TryParseEntityId(string[] parts, int index)
        {
            if (parts == null || parts.Length <= index)
                return 0;

            return int.TryParse(parts[index], out var entityId) ? entityId : 0;
        }

        private static LearnedActionScope DescribePlayScope(Card source)
        {
            if (source == null)
                return LearnedActionScope.Unknown;

            switch (source.Type)
            {
                case Card.CType.MINION:
                    return LearnedActionScope.CastMinion;
                case Card.CType.WEAPON:
                    return LearnedActionScope.CastWeapon;
                case Card.CType.SPELL:
                case Card.CType.HERO:
                case Card.CType.LOCATION:
                    return LearnedActionScope.CastSpell;
                default:
                    return LearnedActionScope.Unknown;
            }
        }

        private static bool IsFriendlyHero(Board board, int entityId)
        {
            return board?.HeroFriend != null && board.HeroFriend.Id == entityId;
        }

        private static Card ResolveSource(Board board, string actionName, int entityId)
        {
            if (board == null || entityId <= 0)
                return null;

            switch (actionName)
            {
                case "PLAY":
                case "TRADE":
                    return board.Hand?.FirstOrDefault(card => card != null && card.Id == entityId);
                case "HERO_POWER":
                    return board.Ability != null && board.Ability.Id == entityId
                        ? board.Ability
                        : board.Ability;
                case "USE_LOCATION":
                    return board.MinionFriend?.FirstOrDefault(card => card != null && card.Id == entityId);
                case "ATTACK":
                    return board.MinionFriend?.FirstOrDefault(card => card != null && card.Id == entityId)
                        ?? (board.HeroFriend != null && board.HeroFriend.Id == entityId ? board.HeroFriend : null);
                default:
                    return null;
            }
        }

        private static Card ResolveTarget(Board board, int entityId)
        {
            if (board == null || entityId <= 0)
                return null;

            if (board.HeroFriend != null && board.HeroFriend.Id == entityId)
                return board.HeroFriend;
            if (board.HeroEnemy != null && board.HeroEnemy.Id == entityId)
                return board.HeroEnemy;

            return board.MinionFriend?.FirstOrDefault(card => card != null && card.Id == entityId)
                ?? board.MinionEnemy?.FirstOrDefault(card => card != null && card.Id == entityId);
        }

        private static string GetCardId(Card card)
        {
            if (card?.Template == null)
                return string.Empty;

            return card.Template.Id.ToString();
        }
    }
}
