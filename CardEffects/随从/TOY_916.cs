using System.Linq;
using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    // 速写美术家：战吼：抽一张暗影法术牌，获取一张它的临时复制。
    internal sealed class Sim_TOY_916 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.Register(C.TOY_916, EffectTrigger.Battlecry, (b, s, t) =>
            {
                // 从牌库抽一张暗影法术
                bool drawn = CardEffectScriptHelpers.DrawRandomCardToHandByPredicate(
                    b,
                    id => CardEffectScriptHelpers.IsShadowSpellCard(id),
                    s);

                // 获取一张临时复制（近似为额外抽一张牌）
                b.FriendCardDraw += 1;
            }, BattlecryTargetType.None);
        }
    }
}
