using BotMain.AI;
using SmartBot.Plugins.API;
using C = SmartBot.Plugins.API.Card.Cards;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DINO_401 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            // 伟岸的德拉克雷斯：突袭（确保打出时具备）
            db.Register(C.DINO_401, EffectTrigger.Battlecry, (b, s, t) =>
            {
                if (s != null) s.HasRush = true;
            }, BattlecryTargetType.None);

            // 攻击一个敌方随从后，对所有其他敌方随从造成伤害
            db.Register(C.DINO_401, EffectTrigger.AfterAttackMinion, (b, s, t) =>
            {
                if (s == null || t == null) return;
                int dmg = s.Atk;
                if (dmg <= 0) return;

                var enemies = s.IsFriend ? b.EnemyMinions : b.FriendMinions;
                foreach (var m in enemies.ToArray())
                {
                    if (ReferenceEquals(m, t)) continue;
                    CardEffectDB.Dmg(b, m, dmg);
                }
            });
        }
    }
}
