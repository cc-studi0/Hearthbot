using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SCH_259 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponStartOfTurnHook(C.SCH_259, (b, weapon) =>
            {
                // 开局看顶牌并可置底的交互，当前单回合模拟器未驱动该时点。
                // 这里保留接口注册，避免后续扩展时漏挂。
            });
        }
    }
}
