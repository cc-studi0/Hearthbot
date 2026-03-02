using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class DefaultMulliganProfile : MulliganProfile
    {
        List<Card.Cards> CardsToKeep = new List<Card.Cards>();
        // 基础留
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            Card.Cards.CORE_WON_065, // CORE_WON_065 随船外科医师
            Card.Cards.VAC_512, // 心灵按摩师 VAC_512
            Card.Cards.TOY_518, // 宝藏经销商 TOY_518
            // Card.Cards.SW_446, // SW_446, // 虚触侍从
        };
        // 暗影投弹手 GVG_009
        private readonly HashSet<Card.Cards> ShadowBombardier = new HashSet<Card.Cards>
        {
            Card.Cards.GVG_009,
        };
        // 虚触侍从 SW_446
        private readonly HashSet<Card.Cards> VoidTouchAttendant = new HashSet<Card.Cards>
        {
            Card.Cards.SW_446, 
        };
        // YOD_032 狂暴邪翼蝠
        private readonly HashSet<Card.Cards> RampagingEvilWingedBat = new HashSet<Card.Cards>
        {
            Card.Cards.YOD_032, 
        };
        // 针灸 VAC_419
        private readonly HashSet<Card.Cards> Acupuncture = new HashSet<Card.Cards>
        {
            Card.Cards.VAC_419, 
        };
        // 空降歹徒 DRG_056
        private readonly HashSet<Card.Cards> ParachuteBrigand = new HashSet<Card.Cards>
        {
            Card.Cards.DRG_056, 
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool HasCoin = choices.Count >= 4;
            int PRIEST = opponentClass == Card.CClass.PRIEST ? 1 : 0;
            int DEMONHUNTER = opponentClass == Card.CClass.DEMONHUNTER ? 1 : 0;
            int MAGE = opponentClass == Card.CClass.MAGE ? 1 : 0;
            int DRUID = opponentClass == Card.CClass.DRUID ? 1 : 0;
            int ROGUE = opponentClass == Card.CClass.ROGUE ? 1 : 0;
            // 新
            int isShadowBombardier = choices.Count(card => ShadowBombardier.Contains(card));
            int isVoidTouchAttendant = choices.Count(card => VoidTouchAttendant.Contains(card));
            int isRampagingEvilWingedBat = choices.Count(card => RampagingEvilWingedBat.Contains(card));
            int isParachuteBrigand = choices.Count(card => ParachuteBrigand.Contains(card));
            int isKeepableCards = choices.Count(card => KeepableCards.Contains(card));
            int isAcupuncture = choices.Count(card => Acupuncture.Contains(card));

// 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER
            Bot.Log("对阵职业" + opponentClass);

            foreach (Card.Cards card in choices)
            {
                // 基础留不重复的牌
                if (KeepableCards.Contains(card) && !CardsToKeep.Contains(card))
                {
                    Keep(card);
                }
            }

            // 新的对战策略
            if (DRUID == 1)
            {
                CardsToKeep.Add(Card.Cards.SW_446); // SW_446, // 虚触侍从
            }
            // 暗影投弹手+虚触+硬币+狂暴邪异伏 留
            if (isShadowBombardier>=1 && isVoidTouchAttendant>=1 && HasCoin && isRampagingEvilWingedBat>=1)
            {
                CardsToKeep.Add(Card.Cards.YOD_032); // YOD_032 狂暴邪翼蝠
                CardsToKeep.Add(Card.Cards.GVG_009); // Card.Cards.GVG_009, // 暗影投弹手
                CardsToKeep.Add(Card.Cards.SW_446); // SW_446, // 虚触侍从
            }
            // 针灸+双狂暴邪翼蝠 留
            if (isRampagingEvilWingedBat >=1&&isAcupuncture>=1)
            {
                CardsToKeep.Add(Card.Cards.VAC_419); // 针灸 VAC_419
                CardsToKeep.Add(Card.Cards.YOD_032); // YOD_032 狂暴邪翼蝠
            }
            // 打牧师 dh 贼 留格卡拉爬行蟹 精神灼烧 纸艺天使
            if (PRIEST == 1 || DEMONHUNTER == 1||ROGUE==1)
            {
                CardsToKeep.Add(Card.Cards.UNG_807); // 葛拉卡爬行蟹
                CardsToKeep.Add(Card.Cards.NX2_019); // 精神灼烧
                CardsToKeep.Add(Card.Cards.TOY_381); // 纸艺天使
            }
            // 打 法师 德 留 异教低阶法师
            if (MAGE == 1 || DRUID == 1)
            {
                CardsToKeep.Add(Card.Cards.SCH_713); // 异教低阶牧师
                CardsToKeep.Add(Card.Cards.REV_960); // 灰烬元素
            }
            // 空降歹徒留一张,有1费海盗留两张
            if(isParachuteBrigand==1)
            {
                CardsToKeep.Add(Card.Cards.DRG_056); // Card.Cards.DRG_056, //  空降歹徒 DRG_056
            }
            if(isParachuteBrigand==2&&isKeepableCards>=1)
            {
                CardsToKeep.Add(Card.Cards.DRG_056); // Card.Cards.DRG_056, //  空降歹徒 DRG_056
            }
            if(isKeepableCards>=1)
            {
                CardsToKeep.Add(Card.Cards.YOD_032); // YOD_032 狂暴邪翼蝠
            }
            // 后手留牌，必留精神灼烧
            if (HasCoin)
            {
                CardsToKeep.Add(Card.Cards.NX2_019); // 精神灼烧
                CardsToKeep.Add(Card.Cards.TOY_381); // 纸艺天使
            }
            // 对于心火牧，冲锋德：113，两种112，222，亡者复生这几张必留 德：DRUID
            // if (opponentClass == Card.CClass.DRUID || opponentClass == Card.CClass.PRIEST)
            // {
            //     CardsToKeep.Add(Card.Cards.SCH_514); // 亡者复生 SCH_514
            // }

            return CardsToKeep;
        }

        public void Keep(Card.Cards card)
        {
            CardsToKeep.Add(card);
        }
    }
}