using BotMain.AI;

namespace BotMain.AI.CardEffectsScripts
{
    internal sealed class Sim_SW_075 : ICardEffectScript
    {
        public void Register(CardEffectDB db)
        {
            CardEffectScriptRuntime.RegisterById(
                db,
                "SW_075",
            new TriggerDef(
                "Deathrattle",
                "None",
                new EffectDef("equip", v: 0, atk: 15, hp: 0, n: 1, dur: 3, useSP: false)
            )
            );
        }
    }
}
