using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_JAM_015 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponInHandRefreshHook(C.JAM_015, (b, weapon) =>
            {
                if (b == null || weapon == null) return;
                // 简化实现：每次刷新时随机给予小幅增益
                var bonus = CardEffectDB.Rnd.Next(3);
                if (bonus == 0) weapon.Atk += 1;
                else if (bonus == 1) weapon.Durability += 1;
            });
        }
    }
}
