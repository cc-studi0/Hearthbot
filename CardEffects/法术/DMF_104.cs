using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DMF_104 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DMF_104",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("summon", v: 0, atk: 8, hp: 8, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
