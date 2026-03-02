using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_ICC_079 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_ICC_079",
            new TriggerDef(
                "Spell",
                "None",
                new EffectDef("armor", v: 3, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
