using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_END_021 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 次元武器匠：战吼，使你手牌中所有随从牌和武器牌获得 +2 攻击力
            db.Register(C.END_021, EffectTrigger.Battlecry, (b, s, t) =>
            {
                foreach (var card in b.Hand)
                {
                    if (card.Type == Card.CType.MINION || card.Type == Card.CType.WEAPON)
                        card.Atk += 2;
                }
            }, BattlecryTargetType.None);
        }
    }
}
