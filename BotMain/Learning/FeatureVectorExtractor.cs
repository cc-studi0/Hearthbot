using System;
using System.Collections.Generic;
using SmartBot.Plugins.API;

namespace BotMain.Learning
{
    public static class FeatureVectorExtractor
    {
        public const int BoardFeatureCount = 12;
        public const int ActionFeatureCount = 10;
        public const int TotalFeatureCount = BoardFeatureCount + ActionFeatureCount;

        public static double[] ExtractBoardFeatures(Board board)
        {
            var f = new double[BoardFeatureCount];
            if (board == null) return f;

            int maxMana = Math.Max(1, board.MaxMana);
            f[0] = (double)board.ManaAvailable / maxMana;
            f[1] = board.MinionFriend?.Count ?? 0;
            f[2] = board.MinionEnemy?.Count ?? 0;
            f[3] = SafeRatio(SumAtk(board.MinionFriend), SumAtk(board.MinionEnemy));
            f[4] = SafeRatio(SumHp(board.MinionFriend), SumHp(board.MinionEnemy));
            f[5] = ((board.HeroFriend?.CurrentHealth ?? 30) - (board.HeroEnemy?.CurrentHealth ?? 30)) / 30.0;
            f[6] = (board.Hand?.Count ?? 0) / 10.0;
            f[7] = HasTaunt(board.MinionEnemy) ? 1.0 : 0.0;
            f[8] = board.TurnCount <= 4 ? 0.0 : board.TurnCount <= 7 ? 0.5 : 1.0;
            f[9] = (board.HeroFriend?.CurrentArmor ?? 0) / 10.0;
            f[10] = board.MinionFriend?.Count > board.MinionEnemy?.Count ? 1.0 : 0.0;
            f[11] = board.MinionEnemy?.Count > 0 && (board.MinionFriend == null || board.MinionFriend.Count == 0) ? 1.0 : 0.0;

            return f;
        }

        public static double[] ExtractActionFeatures(string action, Board board, int enemyHeroEntityId)
        {
            var f = new double[ActionFeatureCount];
            if (string.IsNullOrWhiteSpace(action) || board == null) return f;

            var parts = action.Split('|');
            var type = parts[0].Trim().ToUpperInvariant();

            // one-hot action type
            f[0] = type == "PLAY" ? 1.0 : 0.0;
            f[1] = type == "ATTACK" ? 1.0 : 0.0;
            f[2] = type == "HERO_POWER" ? 1.0 : 0.0;
            f[3] = type == "END_TURN" ? 1.0 : 0.0;

            // target is face
            if (parts.Length >= 3 && int.TryParse(parts[2], out var targetId))
                f[4] = targetId == enemyHeroEntityId ? 1.0 : 0.0;

            // is trading (attack non-hero)
            if (type == "ATTACK" && f[4] == 0.0 && parts.Length >= 3)
                f[5] = 1.0;

            // cost ratio for PLAY
            if (type == "PLAY" && parts.Length >= 2 && int.TryParse(parts[1], out var sourceId))
            {
                var card = FindCardByEntityId(board.Hand, sourceId);
                if (card != null && board.MaxMana > 0)
                    f[6] = (double)card.CurrentCost / Math.Max(1, board.MaxMana);
            }

            // remaining mana normalized
            f[7] = (double)board.ManaAvailable / Math.Max(1, board.MaxMana);

            // hand count normalized
            f[8] = (board.Hand?.Count ?? 0) / 10.0;

            // board count normalized
            f[9] = (board.MinionFriend?.Count ?? 0) / 7.0;

            return f;
        }

        public static double[] Combine(double[] boardFeatures, double[] actionFeatures)
        {
            var combined = new double[TotalFeatureCount];
            Array.Copy(boardFeatures, 0, combined, 0, Math.Min(boardFeatures.Length, BoardFeatureCount));
            Array.Copy(actionFeatures, 0, combined, BoardFeatureCount, Math.Min(actionFeatures.Length, ActionFeatureCount));
            return combined;
        }

        private static double SafeRatio(int a, int b)
        {
            if (a == 0 && b == 0) return 0.5;
            return (double)a / Math.Max(1, a + b);
        }

        private static int SumAtk(IReadOnlyList<Card> minions)
        {
            if (minions == null) return 0;
            int sum = 0;
            foreach (var m in minions) sum += Math.Max(0, m.CurrentAtk);
            return sum;
        }

        private static int SumHp(IReadOnlyList<Card> minions)
        {
            if (minions == null) return 0;
            int sum = 0;
            foreach (var m in minions) sum += Math.Max(0, m.CurrentHealth);
            return sum;
        }

        private static bool HasTaunt(IReadOnlyList<Card> minions)
        {
            if (minions == null) return false;
            foreach (var m in minions)
                if (m.IsTaunt) return true;
            return false;
        }

        private static Card FindCardByEntityId(IReadOnlyList<Card> cards, int entityId)
        {
            if (cards == null) return null;
            foreach (var c in cards)
                if (c.Id == entityId) return c;
            return null;
        }
    }
}
