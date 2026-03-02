using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_EDR_456 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 黑暗的龙骑士：战吼，如果手牌中有龙牌，发现一张具有黑暗之赐的龙牌
            // 发现池近似：当前可识别的“黑暗之赐相关龙牌”候选中随机加入手牌
            db.Register(C.EDR_456, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (!CardEffectScriptHelpers.HasDragonInHand(b, s)) return;
                CardEffectScriptHelpers.AddDiscoveredCardToHand(b, CardEffectScriptHelpers.DarkGiftDragonPool, s);
            }, BattlecryTargetType.None);
        }
    }
}
