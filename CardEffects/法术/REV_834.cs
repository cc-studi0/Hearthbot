using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_REV_834 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "REV_834",
            new TriggerDef(
                "Spell",
                "EnemyMinion",
                new EffectDef("dmg", v: 1, atk: 0, hp: 0, n: 1, dur: 0, useSP: true)
            )
            );
        }
    }
}
