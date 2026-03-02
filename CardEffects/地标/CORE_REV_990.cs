using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_REV_990 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 赤红深渊（核心）：对一个随从造成 1 点伤害，并使其获得 +2 攻击力
            db.Register(C.CORE_REV_990, EffectTrigger.LocationActivation, (b, s, t) =>
            {
                if (t == null) return;
                CardEffectDB.Dmg(b, t, 1);
                if (t.Health > 0) t.Atk += 2;
            }, BattlecryTargetType.AnyMinion);
        }
    }
}
