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

        private readonly List<Card.Cards> WorthySpells = new List<Card.Cards> {};
        // 一费卡
        private readonly HashSet<Card.Cards> OneCostCards = new HashSet<Card.Cards>
        {
            Card.Cards.RLK_039,
        };
        // 保留的卡
        private readonly HashSet<Card.Cards> KeepableCards = new HashSet<Card.Cards>
        {
            // Card.Cards.ULD_413, // 分裂战斧
            Card.Cards.TSC_922, // 驻锚图腾
            Card.Cards.ULD_276, // 怪盗图腾
            // Card.Cards.EX1_244, // 图腾之力
            // Card.Cards.ULD_171, // 图腾潮涌   ULD_171
            Card.Cards.GIL_530, // 阴燃电鳗
            Card.Cards.DMF_704, // 笼斗管理员
            Card.Cards.REV_917, // 石雕凿刀
            Card.Cards.TSC_069, // 深海融合怪
            Card.Cards.AT_052, // 图腾魔像
            Card.Cards.REV_921, // 锻石师
            Card.Cards.TTN_710, // TTN_710	远古图腾
            Card.Cards.DEEP_008, // DEEP_008	针岩图腾	
            Card.Cards.WW_027, // WW_027	可靠陪伴
            // Card.Cards.SCH_713, // SCH_713	异教低阶牧师
            // Card.Cards.WON_091, // WON_091	图腾团聚
						// Card.Cards.CORE_OG_028 // 深渊魔物 CORE_OG_028	深渊魔物
			Card.Cards.TOY_528 // TOY_528	伴唱机
                        
        };

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            bool HasCoin = choices.Count >= 4;
            int DRUID = 0;
            int HUNTER = 0;
            int MAGE = 0;
            int PALADIN = 0;
            int PRIEST = 0;
            int ROGUE = 0;
            int SHAMAN = 0;
            int WARLOCK = 0;
            int WARRIOR = 0;
            int DEMONHUNTER = 0;
            int DEATHKNIGHT = 0;
            int flag1 = choices.Count(card => OneCostCards.Contains(card));
            int kuaigong = (opponentClass == Card.CClass.PALADIN || opponentClass == Card.CClass.HUNTER || opponentClass == Card.CClass.PRIEST || opponentClass == Card.CClass.ROGUE || opponentClass == Card.CClass.WARRIOR || opponentClass == Card.CClass.DEMONHUNTER|| opponentClass == Card.CClass.SHAMAN) ? 1 : 0;
            int mansu = (opponentClass == Card.CClass.DRUID || opponentClass == Card.CClass.MAGE ) ? 1 : 0;

            Bot.Log("对阵职业"+opponentClass);

            if(opponentClass==Card.CClass.PALADIN){
            PALADIN+=1;
            }
            if(opponentClass==Card.CClass.DRUID){
            DRUID+=1;
            }
            if(opponentClass==Card.CClass.HUNTER){
            HUNTER+=1;
            }
            if(opponentClass==Card.CClass.MAGE){
            MAGE+=1;
            }
            if(opponentClass==Card.CClass.PRIEST){
            PRIEST+=1;
            }
            if(opponentClass==Card.CClass.ROGUE){
            ROGUE+=1;
            }
            if(opponentClass==Card.CClass.SHAMAN){
            SHAMAN+=1;
            }
            if(opponentClass==Card.CClass.WARLOCK){
            WARLOCK+=1;
            }
            if(opponentClass==Card.CClass.WARRIOR){
            WARRIOR+=1;
            }
            if(opponentClass==Card.CClass.DEMONHUNTER){
            DEMONHUNTER+=1;
            }
            if(opponentClass == Card.CClass.DEATHKNIGHT){
            DEATHKNIGHT += 1;
            }

            foreach (Card.Cards card in choices.Where(card => KeepableCards.Contains(card) && !CardsToKeep.Contains(card)))
            {
                Keep(card);
            }
							if (HasCoin)
								{
									// CardsToKeep.Add(Card.Cards.CORE_OG_028); // 深渊魔物 CORE_OG_028	深渊魔物
									// CardsToKeep.Add(Card.Cards.ULD_413); // Card.Cards.ULD_413, // 分裂战斧

								}
                                //如果对方为死亡骑士
                                if (DEATHKNIGHT==1)
                                {
                                    CardsToKeep.Add(Card.Cards.ULD_171);//ULD_171	图腾潮涌
                                }
                                // 如果对方为萨满
                                if (SHAMAN == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.EX1_244);// EX1_244	图腾之力
                                }
                                //对方为恶魔猎手
                                if (DEMONHUNTER == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.CORE_EX1_565); // CORE_EX1_565    火舌图腾
                                    CardsToKeep.Add(Card.Cards.JAM_013); // JAM_013	    即兴演奏
                                    CardsToKeep.Add(Card.Cards.EX1_244); // EX1_244	    图腾之力
                                }
                                //对方为德鲁伊
                                if (DRUID == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.ULD_171); // ULD_171    图腾潮涌
                                }
                                //对方为猎人
                                if (HUNTER == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.EX1_244); // EX1_244    图腾之力
                                    CardsToKeep.Add(Card.Cards.CORE_EX1_565); // CORE_EX1_565    火舌图腾
                                }
                                //对方为法师   
                                if (MAGE == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.EX1_244); // EX1_244    图腾之力
                                    CardsToKeep.Add(Card.Cards.ULD_171); // ULD_171    图腾潮涌
                                }
                                //对方为圣骑士 
                                if (PALADIN == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.EX1_244); // EX1_244    图腾之力
                                    CardsToKeep.Add(Card.Cards.ULD_171); // ULD_171    图腾潮涌
                                }
                                //对方为牧师
                                if (PRIEST == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.EX1_244); // EX1_244    图腾之力
                                    CardsToKeep.Add(Card.Cards.ULD_171); // ULD_171    图腾潮涌
                                    CardsToKeep.Add(Card.Cards.CORE_EX1_565); // CORE_EX1_565    火舌图腾
                                    CardsToKeep.Add(Card.Cards.JAM_013); // JAM_013    即兴演奏
                                }
                                //对方为潜行者
                                if (ROGUE == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.ULD_171); // ULD_171    图腾潮涌
                                    CardsToKeep.Add(Card.Cards.JAM_013); // JAM_013    即兴演奏
                                }
                                //对方为术士
                                if (WARLOCK == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.CORE_EX1_565); // CORE_EX1_565    火舌图腾
                                    CardsToKeep.Add(Card.Cards.JAM_013); // JAM_013    即兴演奏
                                }
                                //对方为战士
                                if (WARRIOR == 1)
                                {
                                    CardsToKeep.Add(Card.Cards.ULD_171); // ULD_171    图腾潮涌
                                }

            return CardsToKeep;
        }

        public void Keep(Card.Cards card)
        {
            CardsToKeep.Add(card);
        }
    }
}
