using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_VAN_EX1_398 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "VAN_EX1_398",
            new TriggerDef(
                "Battlecry",
                "None",
                new EffectDef("equip", v: 0, atk: 2, hp: 0, n: 1, dur: 2, useSP: false)
            )
            );
        }
    }
}
