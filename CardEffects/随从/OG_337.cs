using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_OG_337 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "OG_337",
            new TriggerDef(
                "Battlecry",
                "EnemyMinion",
                new EffectDef("buff", v: 0, atk: 0, hp: 1, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
