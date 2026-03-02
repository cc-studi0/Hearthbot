using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_CORE_DS1_185 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "CORE_DS1_185",
            new TriggerDef(
                "Spell",
                "AnyCharacter",
                new EffectDef("dmg", v: 2, atk: 0, hp: 0, n: 1, dur: 0, useSP: true)
            )
            );
        }
    }
}
