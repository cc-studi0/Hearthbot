using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DAL_088 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DAL_088",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("summon", v: 0, atk: 0, hp: 5, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
