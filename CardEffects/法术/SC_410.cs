using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SC_410 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "SC_410",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 2, hp: 1, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
