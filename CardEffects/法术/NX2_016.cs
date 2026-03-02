using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_NX2_016 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "NX2_016",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 2, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
