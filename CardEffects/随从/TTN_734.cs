using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_TTN_734 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "TTN_734",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("buff", v: 0, atk: 2, hp: 2, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
