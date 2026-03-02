using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_WORK_042 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "WORK_042",
            new TriggerDef(
                "Battlecry",
                "FriendlyMinion",
                new EffectDef("destroy", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            ),
            new TriggerDef(
                "EndOfTurn",
                "None",
                new EffectDef("destroy", v: 0, atk: 0, hp: 0, n: 1, dur: 0, useSP: false)
            )
            );
        }
    }
}
