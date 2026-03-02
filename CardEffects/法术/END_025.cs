using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_END_025 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "END_025",
            new TriggerDef(
                "Spell",
                "AnyMinion",
                new EffectDef("dmg", v: 3, atk: 0, hp: 0, n: 1, dur: 0, useSP: true)
            )
            );
        }
    }
}
