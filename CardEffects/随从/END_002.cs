using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_END_002 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "END_002",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("equip", v: 0, atk: 1, hp: 0, n: 1, dur: 2, useSP: false)
            )
            );
        }
    }
}
