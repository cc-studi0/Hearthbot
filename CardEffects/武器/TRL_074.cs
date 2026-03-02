using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TRL_074 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TRL_074",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("give_rush", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
