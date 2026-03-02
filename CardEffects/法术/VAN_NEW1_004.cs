using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_VAN_NEW1_004 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "VAN_NEW1_004",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("bounce", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
