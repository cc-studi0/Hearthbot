using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ONY_027 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "ONY_027",
            new TriggerDef(
                "Spell",
                "EnemyMinion",
                new EffectDef("buff", v: 0, atk: 1, hp: 1, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
