using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_WON_095 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "WON_095",
            new TriggerDef(
                "Spell",
                "AnyCharacter",
                new EffectDef("dmg", v: 3, atk: 0, hp: 0, n: 1, dur: 0, useSP: true),
                new EffectDef("draw", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
