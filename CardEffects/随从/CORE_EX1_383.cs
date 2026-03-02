using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_EX1_383 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_EX1_383",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("equip", v: 0, atk: 5, hp: 0, n: 1, dur: 3, useSP: false)
            )
            );
        }
    }
}
