using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DMF_056 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DMF_056",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("draw", v: 0, atk: 0, hp: 0, n: 2, dur: 0, useSP: false)
            )
            );
        }
    }
}
