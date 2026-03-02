using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_SW_066 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 王室图书管理员：可交易；战吼，沉默一个随从
            db.Register(C.CORE_SW_066, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectDB.DoSilence(t);
            }, BattlecryTargetType.AnyMinion);
        }
    }
}
