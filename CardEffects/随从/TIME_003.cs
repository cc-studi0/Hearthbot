using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TIME_003 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 传送门卫士：战吼，随机抽一张随从牌并使其获得 +2/+2
            db.Register(C.TIME_003, EffectTrigger.Battlecry, (b, s, t) =>
            {
                CardEffectScriptHelpers.DrawRandomMinionToHand(b, s, buffAtk: 2, buffHp: 2);
            }, BattlecryTargetType.None);
        }
    }
}
