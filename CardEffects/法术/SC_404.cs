using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SC_404 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "SC_404",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 2, hp: 2, n: 1, dur: 0, useSP: false),
                new EffectDef("summon", v: 0, atk: 2, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
