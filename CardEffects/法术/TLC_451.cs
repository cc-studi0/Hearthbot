using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 咒怨之墓（法术）：从你的牌库中发现另一张牌，将其变为临时卡牌。
    internal sealed class Sim_TLC_451 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.TLC_451, EffectTrigger.Spell, (b, s, t) =>
            {
                // 近似：从牌库抽一张牌（发现→临时卡牌）
                CardEffectDB.DrawCard(b, b.FriendDeckCards);
            }, BattlecryTargetType.None);
        }
    }
}
