using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_DED_522 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "DED_522",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("equip", v: 0, atk: 2, hp: 0, n: 1, dur: 3, useSP: false)
            )
            );
        }
    }
}
