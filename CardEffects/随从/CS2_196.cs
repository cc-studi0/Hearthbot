using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CS2_196 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CS2_196",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("summon", v: 0, atk: 1, hp: 1, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
