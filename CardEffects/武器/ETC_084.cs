using System;
using BotMain.AI;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ETC_084 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            db.RegisterHeroDamageReplacement(C.ETC_084, (b, hero, incoming) =>
            {
                // “on your turn” 在当前模拟中等价于我方回合，因此只处理我方英雄。
                if (b?.FriendHero == null || hero != b.FriendHero) return incoming;
                if (b.FriendWeapon == null || b.FriendWeapon.CardId != C.ETC_084) return incoming;

                b.FriendHero.Health = Math.Min(b.FriendHero.MaxHealth, b.FriendHero.Health + 2);
                b.FriendWeapon.Health -= 1;
                return 0;
            });
        }
    }
}
