using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DAL_720 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DAL_720",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("bounce", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
