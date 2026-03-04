using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_UNG_929 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterWeaponInHandRefreshHook(C.UNG_929, (b, cardInHand) =>
            {
                // “每回合在手牌变成新武器”需要跨回合/回合开始驱动。
                // 当前单回合模拟器未驱动该时点，先保留接口注册。
            });
        }
    }
}
