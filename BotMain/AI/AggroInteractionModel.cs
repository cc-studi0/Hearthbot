using System;
using SmartBotProfiles;

namespace BotMain.AI
{
    public sealed class AggroInteractionContext
    {
        public float AggroCoef { get; private set; }
        public float FaceBias { get; private set; }
        public float TradeBias { get; private set; }
        public float ThreatBias { get; private set; }
        public float SurvivalBias { get; private set; }
        public float OverkillPenaltyScale { get; private set; }
        public float OverkillTolerance { get; private set; }
        public float HighThreatAttackThreshold { get; private set; }
        public float SafeFaceEnemyEhpThreshold { get; private set; }
        public float DangerPressureThreshold { get; private set; }
        public float LethalWindowMultiplier { get; private set; }
        public float PersistentMinionConservation { get; private set; }

        public static AggroInteractionContext FromAggroCoef(float aggroCoef)
        {
            var coef = Clamp(aggroCoef, 0.2f, 2.2f);
            var aggressive = Clamp01((coef - 1f) / 1.2f);
            var defensive = Clamp01((1f - coef) / 0.8f);

            return new AggroInteractionContext
            {
                AggroCoef = coef,
                FaceBias = Clamp(1f + aggressive * 0.9f - defensive * 0.35f, 0.55f, 1.95f),
                TradeBias = Clamp(1f + defensive * 0.95f - aggressive * 0.35f, 0.55f, 1.95f),
                ThreatBias = Clamp(1f + defensive * 0.75f - aggressive * 0.25f, 0.6f, 1.8f),
                SurvivalBias = Clamp(1f + defensive * 1.1f - aggressive * 0.4f, 0.55f, 2.2f),
                OverkillPenaltyScale = Clamp(1f + defensive * 0.8f - aggressive * 0.55f, 0.3f, 2.2f),
                OverkillTolerance = Clamp(3f + aggressive * 2f - defensive * 1.2f, 1f, 6f),
                HighThreatAttackThreshold = Clamp(5f - defensive * 1.8f + aggressive * 1.2f, 3f, 8f),
                SafeFaceEnemyEhpThreshold = Clamp(12f + defensive * 6f - aggressive * 4f, 6f, 20f),
                DangerPressureThreshold = Clamp(0.62f - defensive * 0.2f + aggressive * 0.1f, 0.35f, 0.8f),
                LethalWindowMultiplier = Clamp(1.25f + aggressive * 0.55f - defensive * 0.15f, 1f, 2.1f),
                PersistentMinionConservation = Clamp(1f + defensive * 0.9f - aggressive * 0.3f, 0.6f, 2f),
            };
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public interface IAggroInteractionModel
    {
        AggroInteractionContext Build(SimBoard board, ProfileParameters param);
    }

    public sealed class DefaultAggroInteractionModel : IAggroInteractionModel
    {
        public AggroInteractionContext Build(SimBoard board, ProfileParameters param)
        {
            var aggroCoef = 1f;
            if (param?.GlobalAggroModifier != null)
                aggroCoef = param.GlobalAggroModifier.GetValueCoef();
            return AggroInteractionContext.FromAggroCoef(aggroCoef);
        }
    }
}
