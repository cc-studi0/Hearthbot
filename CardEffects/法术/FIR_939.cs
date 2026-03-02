using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_FIR_939 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 影焰晕染：造成 2 点伤害；发现一张具有黑暗之赐的战士随从牌
            // 发现池近似：当前可识别的“黑暗之赐相关战士随从”候选中随机加入手牌
            db.Register(C.FIR_939, EffectTrigger.Spell, (b, s, t) =>
            {
                if (t != null) CardEffectDB.Dmg(b, t, 2 + CardEffectDB.SP(b));
                CardEffectScriptHelpers.AddDiscoveredCardToHand(b, CardEffectScriptHelpers.WarriorDarkGiftMinionPool, s);
            }, BattlecryTargetType.AnyCharacter);
        }
    }
}
