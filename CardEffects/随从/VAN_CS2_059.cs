using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_VAN_CS2_059 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "VAN_CS2_059",
            new TriggerDef(
                "EndOfTurn",
                "None",
                new EffectDef("buff", v: 0, atk: 0, hp: 1, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
