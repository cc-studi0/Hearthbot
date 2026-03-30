using System;
using System.Collections.Generic;

namespace BotMain.Learning
{
    public sealed class ActionPatternSignals
    {
        public double FaceDamageRatio { get; set; }
        public int AttackCount { get; set; }
        public int FaceAttackCount { get; set; }
        public int TradeAttackCount { get; set; }
        public int CardsPlayed { get; set; }
        public bool UsedHeroPower { get; set; }
        public double ManaEfficiency { get; set; }
        public double PlayRatio { get; set; }
    }

    public static class ActionPatternClassifier
    {
        public static ActionPatternSignals Classify(
            IReadOnlyList<string> actions,
            int enemyHeroEntityId,
            int manaAvailable,
            int maxMana,
            int handCount)
        {
            var signals = new ActionPatternSignals();
            if (actions == null || actions.Count == 0)
                return signals;

            int faceAttacks = 0;
            int totalAttacks = 0;
            int cardsPlayed = 0;
            bool usedHeroPower = false;

            foreach (var action in actions)
            {
                if (string.IsNullOrWhiteSpace(action)) continue;
                var parts = action.Split('|');
                var type = parts[0].Trim().ToUpperInvariant();

                switch (type)
                {
                    case "ATTACK":
                        totalAttacks++;
                        if (parts.Length >= 3 && int.TryParse(parts[2], out var targetId) && targetId == enemyHeroEntityId)
                            faceAttacks++;
                        break;

                    case "PLAY":
                        cardsPlayed++;
                        break;

                    case "HERO_POWER":
                        usedHeroPower = true;
                        break;
                }
            }

            signals.AttackCount = totalAttacks;
            signals.FaceAttackCount = faceAttacks;
            signals.TradeAttackCount = totalAttacks - faceAttacks;
            signals.FaceDamageRatio = totalAttacks > 0 ? (double)faceAttacks / totalAttacks : 0.0;
            signals.CardsPlayed = cardsPlayed;
            signals.UsedHeroPower = usedHeroPower;
            signals.ManaEfficiency = maxMana > 0 ? Math.Max(0, 1.0 - (double)manaAvailable / maxMana) : 0.0;
            signals.PlayRatio = handCount > 0 ? (double)cardsPlayed / handCount : 0.0;

            return signals;
        }
    }
}
