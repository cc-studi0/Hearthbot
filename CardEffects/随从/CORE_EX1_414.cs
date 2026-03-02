using System;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_EX1_414 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 格罗玛什·地狱咆哮：冲锋（确保打出后可攻击）
            db.Register(C.CORE_EX1_414, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s == null) return;
                s.HasCharge = true;
                s.IsTired = false;
            }, BattlecryTargetType.None);

            // 受伤时 +6 攻击力（含恢复后移除的处理）
            db.Register(C.CORE_EX1_414, EffectTrigger.Aura, (b, s, t) =>
            {
                if (s == null) return;

                int baseAtk = CardEffectScriptHelpers.GetBaseAtk(C.CORE_EX1_414, 4);
                bool damaged = s.Health < s.MaxHealth;

                if (damaged)
                {
                    if (!s.EnrageBonusActive)
                    {
                        // 从真实局面导入时攻击力可能已包含加成，避免重复叠加
                        if (s.Atk < baseAtk + 6) s.Atk += 6;
                        s.EnrageBonusActive = true;
                    }
                }
                else if (s.EnrageBonusActive)
                {
                    s.Atk = Math.Max(0, s.Atk - 6);
                    s.EnrageBonusActive = false;
                }
            });
        }
    }
}
