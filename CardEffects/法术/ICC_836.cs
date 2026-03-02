using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_ICC_836 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "ICC_836",
            new TriggerDef(
                "Spell",
                "EnemyMinion",
                new EffectDef("dmg", v: 2, atk: 0, hp: 0, n: 1, dur: 0, useSP: true)
            )
            );
        }
    }
}
