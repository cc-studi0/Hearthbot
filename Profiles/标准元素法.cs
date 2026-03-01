using System;
using System.Collections.Generic;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API;
using SmartBotAPI.Battlegrounds;
using SmartBot.Plugins.API.Actions;


/* Explanation on profiles :
 * 
 * 配置文件中定义的所有值都是百分比修饰符，这意味着它将影响基本配置文件的默认值。
 * 
 * 修饰符值可以在[-10000;范围内设置。 10000]（负修饰符有相反的效果）
 * 您可以为非全局修改器指定目标，这些目标特定修改器将添加到卡的全局修改器+修改器之上（无目标）
 * 
 * 应用的总修改器=全局修改器+无目标修改器+目标特定修改器
 * 
 * GlobalDrawModifier --->修改器应用于卡片绘制值
 * GlobalWeaponsAttackModifier --->修改器适用于武器攻击的价值，它越高，人工智能攻击武器的可能性就越小
 * 
 * GlobalCastSpellsModifier --->修改器适用于所有法术，无论它们是什么。修饰符越高，AI玩法术的可能性就越小
 * GlobalCastMinionsModifier --->修改器适用于所有仆从，无论它们是什么。修饰符越高，AI玩仆从的可能性就越小
 * 
 * GlobalAggroModifier --->修改器适用于敌人的健康值，越高越好，人工智能就越激进
 * GlobalDefenseModifier --->修饰符应用于友方的健康值，越高，hp保守的将是AI
 * 
 * CastSpellsModifiers --->你可以为每个法术设置个别修饰符，修饰符越高，AI玩法术的可能性越小
 * CastMinionsModifiers --->你可以为每个小兵设置单独的修饰符，修饰符越高，AI玩仆从的可能性越小
 * CastHeroPowerModifier --->修饰符应用于heropower，修饰符越高，AI玩它的可能性就越小
 * 
 * WeaponsAttackModifiers --->适用于武器攻击的修饰符，修饰符越高，AI攻击它的可能性越小
 * 
 * OnBoardFriendlyMinionsValuesModifiers --->修改器适用于船上友好的奴才。修饰语越高，AI就越保守。
 * OnBoardBoardEnemyMinionsModifiers --->修改器适用于船上的敌人。修饰符越高，AI就越会将其视为优先目标。
 *
 */

namespace SmartBotProfiles
{
    [Serializable]
    public class standardeggPaladin  : Profile
    {
        #region 英雄技能
        //幸运币
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        //战士
        private const Card.Cards ArmorUp = Card.Cards.HERO_01bp;
        //萨满
        private const Card.Cards TotemicCall = Card.Cards.HERO_02bp;
        //盗贼
        private const Card.Cards DaggerMastery = Card.Cards.HERO_03bp;
        //圣骑士
        private const Card.Cards Reinforce = Card.Cards.HERO_04bp;
        //猎人
        private const Card.Cards SteadyShot = Card.Cards.HERO_05bp;
        //德鲁伊
        private const Card.Cards Shapeshift = Card.Cards.HERO_06bp;
        //术士
        private const Card.Cards LifeTap = Card.Cards.HERO_07bp;
        //法师
        private const Card.Cards Fireblast = Card.Cards.HERO_08bp;
        //牧师
        private const Card.Cards LesserHeal = Card.Cards.HERO_09bp;
        #endregion

#region 英雄能力优先级
        private readonly Dictionary<Card.Cards, int> _heroPowersPriorityTable = new Dictionary<Card.Cards, int>
        {
            {SteadyShot, 9},//猎人
            {LifeTap, 8},//术士
            {DaggerMastery, 7},//盗贼
            {Reinforce, 5},//骑士
            {Fireblast, 4},//法师
            {Shapeshift, 3},//德鲁伊
            {LesserHeal, 2},//牧师
            {ArmorUp, 1},//战士
        };
        private int GetTag(Card c, Card.GAME_TAG tag)
        {
            if (c.tags != null && c.tags.ContainsKey(tag))
                return c.tags[tag];
            return -1;
        }
            private int GetSireDenathriusDamageCount(Card c)
        {
            return GetTag(c, Card.GAME_TAG.TAG_SCRIPT_DATA_NUM_2);
        }
					private int quantityOverflowLava(Card c)
				{
						return GetTag(c, Card.GAME_TAG.TAG_SCRIPT_DATA_NUM_1);
				}
#endregion

#region 直伤卡牌 标准模式
        //直伤法术卡牌（必须是可打脸的伤害） 需要计算法强
        private static readonly Dictionary<Card.Cards, int> _spellDamagesTable = new Dictionary<Card.Cards, int>
        {
            //萨满
            {Card.Cards.CORE_EX1_238, 3},//闪电箭 Lightning Bolt     CORE_EX1_238
            {Card.Cards.DMF_701, 4},//深水炸弹 Dunk Tank     DMF_701
            {Card.Cards.DMF_701t, 4},//深水炸弹 Dunk Tank     DMF_701t
            {Card.Cards.BT_100, 3},//毒蛇神殿传送门 Serpentshrine Portal     BT_100 
            //德鲁伊

            //猎人
            {Card.Cards.BAR_801, 1},//击伤猎物 Wound Prey     BAR_801
            {Card.Cards.CORE_DS1_185, 2},//奥术射击 Arcane Shot     CORE_DS1_185
            {Card.Cards.CORE_BRM_013, 3},//快速射击 Quick Shot     CORE_BRM_013
            {Card.Cards.BT_205, 3},//废铁射击 Scrap Shot     BT_205 
            //法师
            {Card.Cards.BAR_541, 2},//符文宝珠 Runed Orb     BAR_541 
            {Card.Cards.CORE_CS2_029, 6},//火球术 Fireball     CORE_CS2_029
            {Card.Cards.BT_291, 5},//埃匹希斯冲击 Apexis Blast     BT_291 
            //骑士
            {Card.Cards.CORE_CS2_093, 2},//奉献 Consecration     CORE_CS2_093 
            //牧师
            //盗贼
            {Card.Cards.BAR_319, 2},//邪恶挥刺（等级1） Wicked Stab (Rank 1)     BAR_319
            {Card.Cards.BAR_319t, 4},//邪恶挥刺（等级2） Wicked Stab (Rank 2)     BAR_319t
            {Card.Cards.BAR_319t2, 6},//邪恶挥刺（等级3） Wicked Stab (Rank 3)     BAR_319t2 
            {Card.Cards.CORE_CS2_075, 3},//影袭 Sinister Strike     CORE_CS2_075
            {Card.Cards.TSC_086, 4},//剑鱼 TSC_086
            //术士
            {Card.Cards.CORE_CS2_062, 3},//地狱烈焰 Hellfire     CORE_CS2_062
            //战士
            {Card.Cards.DED_006, 6},//重拳先生 DED_006
            //中立
            {Card.Cards.DREAM_02, 5},//伊瑟拉苏醒 Ysera Awakens     DREAM_02
        };
        //直伤随从卡牌（必须可以打脸）
        private static readonly Dictionary<Card.Cards, int> _MinionDamagesTable = new Dictionary<Card.Cards, int>
        {
            //盗贼
            {Card.Cards.BAR_316, 2},//油田伏击者 Oil Rig Ambusher     BAR_316 
            //萨满
            {Card.Cards.CORE_CS2_042, 4},//火元素 Fire Elemental     CORE_CS2_042 
            //德鲁伊
            //术士
            {Card.Cards.CORE_CS2_064, 1},//恐惧地狱火 Dread Infernal     CORE_CS2_064 
            //中立
            {Card.Cards.CORE_CS2_189, 1},//精灵弓箭手 Elven Archer     CORE_CS2_189
            {Card.Cards.CS3_031, 8},//生命的缚誓者阿莱克丝塔萨 Alexstrasza the Life-Binder     CS3_031 
            {Card.Cards.DMF_174t, 4},//马戏团医师 Circus Medic     DMF_174t
            {Card.Cards.DMF_066, 2},//小刀商贩 Knife Vendor     DMF_066 
            {Card.Cards.SCH_199t2, 2},//转校生 Transfer Student     SCH_199t2 
            {Card.Cards.SCH_273, 1},//莱斯·霜语 Ras Frostwhisper     SCH_273
            {Card.Cards.BT_187, 3},//凯恩·日怒 Kayn Sunfury     BT_187
            {Card.Cards.BT_717, 2},//潜地蝎 Burrowing Scorpid     BT_717 
            {Card.Cards.CORE_EX1_249, 2},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.DMF_254, 30},//迦顿男爵 Baron Geddon     CORE_EX1_249 
            {Card.Cards.RLK_222t2, 14},//火焰使者阿斯塔洛 Astalor, the Flamebringer ID：RLK_222t2
            {Card.Cards.RLK_224, 2},//监督者弗里吉达拉 Overseer Frigidara ID：RLK_224
             {Card.Cards.RLK_063, 5},//冰霜巨龙之怒 Frostwyrm's Fury ID：RLK_063 
            {Card.Cards.RLK_015, 3},//凛风冲击 Howling Blast ID：RLK_015 
            {Card.Cards.RLK_516, 2},//碎骨手斧 Bone Breaker ID：RLK_516
        };
        #endregion

#region 攻击模式和自定义 
      private string _log = "";   // 日志字符串
      private const string ProfileVersion = "2026-01-03.1";
      public ProfileParameters GetParameters(Board board)
      {
            _log = "";
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 }; 

            try
            {
                int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                AddLog($"================ 标准元素法 决策日志 v{ProfileVersion} ================");
                AddLog($"敌方血甲: {enemyHealth} | 我方血甲: {friendHealth} | 法力:{board.ManaAvailable} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount}");
                AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
            }
            catch
            {
                // ignore
            }
            //  增加思考时间
            // p.ForceResimulation = true; 
            int a =(board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) - BoardHelper.GetEnemyHealthAndArmor(board);
            //攻击模式切换
        // 德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER 死：DEATHKNIGHT
            if(board.HeroEnemy.CurrentHealth+board.HeroEnemy.CurrentArmor<=10
						// 敌方没有吸血随从
						&&board.MinionEnemy.Count(x=>x.IsLifeSteal==true)==0
						 ){
                p.GlobalAggroModifier = 500;
                AddLog("走脸");
							}else if(board.EnemyClass == Card.CClass.PALADIN
            || board.EnemyClass == Card.CClass.DEMONHUNTER
            || board.EnemyClass == Card.CClass.PRIEST
            || board.EnemyClass == Card.CClass.DEATHKNIGHT
            || board.EnemyClass == Card.CClass.HUNTER
            || board.EnemyClass == Card.CClass.MAGE
            || board.EnemyClass == Card.CClass.ROGUE
            || board.EnemyClass == Card.CClass.WARLOCK
            || board.EnemyClass == Card.CClass.SHAMAN)
            {
               p.GlobalAggroModifier = (int)(a * 0.625 + 120);
                AddLog("攻击值"+(a * 0.625 + 120));
            }else
            {
                         p.GlobalAggroModifier = (int)(a * 0.625 + 120);
                AddLog("攻击值"+(a * 0.625 + 120));
            }	 


       {
 
        
            int myAttack = 0;
            int enemyAttack = 0;

            if (board.MinionFriend != null)
            {
                for (int x = 0; x < board.MinionFriend.Count; x++)
                {
                    myAttack += board.MinionFriend[x].CurrentAtk;
                }
            }

            if (board.MinionEnemy != null)
            {
                for (int x = 0; x < board.MinionEnemy.Count; x++)
                {
                    enemyAttack += board.MinionEnemy[x].CurrentAtk;
                }
            }

            if (board.WeaponEnemy != null)
            {
                enemyAttack += board.WeaponEnemy.CurrentAtk;
            }

            if ((int)board.EnemyClass == 2 || (int)board.EnemyClass == 7 || (int)board.EnemyClass == 8)
            {
                enemyAttack += 1;
            }
            else if ((int)board.EnemyClass == 6)
            {
                enemyAttack += 2;
            }   
         //定义场攻  用法 myAttack <= 5 自己场攻大于小于5  enemyAttack  <= 5 对面场攻大于小于5  已计算武器伤害

            int myMinionHealth = 0;
            int enemyMinionHealth = 0;

            if (board.MinionFriend != null)
            {
                for (int x = 0; x < board.MinionFriend.Count; x++)
                {
                    myMinionHealth += board.MinionFriend[x].CurrentHealth;
                }
            }

            if (board.MinionEnemy != null)
            {
                for (int x = 0; x < board.MinionEnemy.Count; x++)
                {
                    enemyMinionHealth += board.MinionEnemy[x].CurrentHealth;
                }
            }
						// 定义敌方手牌的数量
						int enemyHandCount = board.EnemyCardCount;
            // 友方随从数量
            int friendCount = board.MinionFriend.Count;
            // 手牌数量
            int HandCount = board.Hand.Count;
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
            var TheVirtuesofthePainter = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TOY_810);
             // 定义坟场中一费随从的数量 TUTR_HERO_11bpt
           	int oneCostMinionCount =	board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Type == Card.CType.MINION &&CardTemplate.LoadFromId(card).Id!=Card.Cards.CS2_101t&&CardTemplate.LoadFromId(card).Cost==1)
						+board.Hand.Count(card => card.Template.Id!=Card.Cards.CS2_101t&&card.Type == Card.CType.MINION&&card.Template.Cost==1)
						+board.MinionFriend.Count(card => card.Template.Id!=Card.Cards.CS2_101t&&card.Type == Card.CType.MINION&&card.Template.Cost==1);
						; 
            // 场上实际能存活随从数 -脆弱的食尸鬼的数量 TUTR_HERO_11bpt/HERO_11bpt
            int liveMinionCount = board.MinionFriend.Count-board.MinionFriend.Count(x => x.Template.Id == Card.Cards.HERO_11bpt);
            int canAttackMinion=board.MinionFriend.Count(card => card.CanAttack);
            // 定义敌方三血及以下随从的数量
            int enemyThreeHealthMinionCount = board.MinionEnemy.Count(card => card.CurrentHealth<=3);
   					//   本轮使用过元素
            var isUsedEle = board.MinionFriend.Any(x => x.Template.IsRace(Card.CRace.ELEMENTAL)&& x.GetTag(Card.GAME_TAG.JUST_PLAYED) ==1);
         // 手上是否有可以用的元素
						// 手上幸运币数量
						int coinCount = board.Hand.Count(card => card.Template.Id == Card.Cards.GAME_005);
            var hasEle = board.Hand.Exists(card => card.CurrentCost-coinCount <= board.ManaAvailable &&card.Template.IsRace(Card.CRace.ELEMENTAL));
            var eleCount = board.Hand.Count(card => card.CurrentCost-coinCount <= board.ManaAvailable &&card.Template.IsRace(Card.CRace.ELEMENTAL));
            var useAFeeEntourage = board.MinionFriend.Any(x => x.CurrentCost==1&& x.GetTag(Card.GAME_TAG.JUST_PLAYED) ==1);
						// 上轮使用过得元素数量
						int lastEleCount = board.ElemPlayedLastTurn;
						// 敌方随从的数量
						int enemyCount = board.MinionEnemy.Count;
						// 定义手中燃灯元素的数量
						int lampElement = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_442);
						// 定义敌方2血及两血以下随从数量
						int twoBlood = board.MinionEnemy.Count(card => card.CurrentHealth <= 2);
					// 高价值随从 碎裂巨岩迈沙顿 WW_429  腐化残渣 YOG_519 伊辛迪奥斯 VAC_321  恒星之火萨鲁恩 GDB_304
						int highValue = board.Hand.Count(x=>x.CurrentCost<=3&&x.Template.Id==Card.Cards.WW_429)
						+board.Hand.Count(x=>x.CurrentCost<=3&&x.Template.Id==Card.Cards.YOG_519)
						+board.Hand.Count(x=>x.CurrentCost<=4&&x.Template.Id==Card.Cards.VAC_321)
						+board.Hand.Count(x=>x.CurrentCost<=4&&x.Template.Id==Card.Cards.GDB_304);
						// 未减费的高价值随从
						int highValueNoReduce = board.Hand.Count(x=>x.Template.Id==Card.Cards.WW_429)
						+board.Hand.Count(x=>x.Template.Id==Card.Cards.YOG_519)
						+board.Hand.Count(x=>x.Template.Id==Card.Cards.VAC_321)
						+board.Hand.Count(x=>x.Template.Id==Card.Cards.GDB_304);
						// 场上有法强随从
						var spellCount = board.MinionFriend.FindAll(x => x.IsSilenced == false).Sum(x => x.SpellPower);
						// 当前敌方英雄是不是德：DRUID,法：MAGE,贼：ROGUE 瞎：DEMONHUNTER 死：DEATHKNIGHT
						var isKuaigong = board.EnemyClass == Card.CClass.DRUID || board.EnemyClass == Card.CClass.MAGE || board.EnemyClass == Card.CClass.ROGUE || board.EnemyClass == Card.CClass.DEMONHUNTER || board.EnemyClass == Card.CClass.DEATHKNIGHT;
						// 是否容易被杀死
						bool isEasyKill = board.HeroFriend.CurrentHealth >= enemyAttack+5;
						// 手里是否有小于等于2费的艾泽里特巨人 WW_025
						bool hasEleGiant = board.Hand.Exists(card => card.Template.Id == Card.Cards.WW_025&&card.CurrentCost<=2);
						// 场上可攻击的随从数量 // 可攻击随从
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
           
 #endregion
 #region 不送的怪
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); //奇迹推销员 WW_331
        //   p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(150)); //鱼人木乃伊 CORE_ULD_723 
#endregion
#region 送的怪
        
          if(board.HasCardOnBoard(Card.Cards.TSC_962)){//修饰老巨鳍 Gigafin ID：TSC_962 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TSC_962t, new Modifier(-100)); //修饰老巨鳍之口 Gigafin's Maw ID：TSC_962t 
          }
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_373t, new Modifier(-100)); //具象暗影 Shadow Manifestation ID：REV_373t 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_955, new Modifier(-100)); //执事者斯图尔特 Stewart the Steward ID：REV_955 
#endregion

#region Card.Cards.HERO_02bp 英雄技能
// 如果当前没有使用过元素,且手中有可用的元素牌,降低技能优先值
        if(isUsedEle==false
				&&hasEle==true
				&&eleCount-lampElement>0
				// 随从数小于等于6
				&&friendCount<=6
    		&&!board.MinionEnemy.Exists(x=>x.Template.Id==Card.Cards.CORE_NEW1_021)
				){
            p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_02bp, new Modifier(999)); 
            p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_08bp, new Modifier(999)); 
						AddLog("降低技能优先值");
        }else{
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_02bp, new Modifier(85)); 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_02bp, new Modifier(-550)); 
        // 法师技能 HERO_08bp
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_08bp, new Modifier(85)); 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_08bp, new Modifier(-550)); 
            }
#endregion

// 如果手上没有可用的随从牌 提高手上法术牌的使用优先级
#region 提高手上法术牌的使用优先级
				if(minionNumber==0){
						foreach (var spell in _spellDamagesTable)
						{
								if (board.HasCardInHand(spell.Key)
								)
								{
										p.CastSpellsModifiers.AddOrUpdate(spell.Key, new Modifier(-150));
										AddLog("提高$" + CardTemplate.LoadFromId(spell.Key).NameCN + "使用优先级");
								}
						}
				}
#endregion

#region 直伤打脸
// CORE_CS2_029 火球术 Fireball   敌方血量低于等于直伤时,提高使用优先级
				foreach (var spell in _spellDamagesTable)
				{
						if (board.HasCardInHand(spell.Key)
								&& BoardHelper.GetEnemyHealthAndArmor(board) <= spell.Value + spellCount)
						{
								p.CastSpellsModifiers.AddOrUpdate(spell.Key, new Modifier(-500));
								AddLog("提高$" + CardTemplate.LoadFromId(spell.Key).NameCN + "使用优先级");
						}
				}
#endregion

#region 活体烈焰 FIR_929
        // 未标记不使用
			if(board.HasCardInHand(Card.Cards.FIR_929)
		){
        	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.FIR_929, new Modifier(-99)); 
					AddLog("活体烈焰 -99");
		}
#endregion

#region 奥术飞弹 EX1_277
        // 未标记不使用
			if(board.HasCardInHand(Card.Cards.EX1_277)
		){
        	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_277, new Modifier(-150)); 
					AddLog("奥术飞弹 -150");
		}
#endregion

#region 咒术图书管理员 TLC_226
        // 未标记不使用
				if(
					board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_226)
					&&friendCount<=6
				){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_226, new Modifier(-150));
				AddLog("咒术图书管理员 使用");
				}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_226, new Modifier(350));
				}
#endregion

#region WW_331	奇迹推销员
         if(board.HasCardInHand(Card.Cards.WW_331)
         &&board.MaxMana>=5
		){
        	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); 
					AddLog("奇迹推销员 150");
		}else{
        	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(250)); 
        }
#endregion

#region 戈贡佐姆 VAC_9150
        // 提高优先级
        if(board.HasCardInHand(Card.Cards.VAC_955)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_955, new Modifier(-350));
          AddLog("戈贡佐姆-350");
        }

#endregion

#region 飞行员帕奇斯 VAC_933
        if(board.HasCardInHand(Card.Cards.VAC_933)){
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_933, new Modifier(-350)); 
                AddLog("飞行员帕奇斯 -350");
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_933, new Modifier(999)); 
#endregion


#region 伞降咒符 VAC_925
       if(board.HasCardInHand(Card.Cards.VAC_925)
        &&friendCount<5
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_925, new Modifier(-550));   
        AddLog("伞降咒符 -550");
        }
#endregion

#region 贪婪的伴侣 WW_901
    //  如果没有被active,则不用
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.WW_901)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_901, new Modifier(-99)); 
        AddLog("贪婪的伴侣 -99");
    }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_901, new Modifier(150)); 
    }
#endregion

#region 悠闲的曲奇 VAC_450
    // 当场上可攻击随从大于的等于2时,提高优先级
    if (canAttackMINIONs >= 2 
        && board.HasCardInHand(Card.Cards.VAC_450)
        )
        {
            // 如果场上海盗大于2，提高优先级
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(canAttackMINIONs*-99));
        AddLog("悠闲的曲奇" + (canAttackMINIONs*-99));
        }
        else
        {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(350));
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(999)); 
#endregion

#region VAC_955t 美味奶酪
			foreach(var card in board.Hand)
      {
        if(card.Template.Id == Card.Cards.VAC_955t)
        {
						var tag = quantityOverflowLava(card);
						// 如果当前手牌数小于等于3,且最大回合数大于等于8,提高优先级
						if (board.MinionFriend.Count<=4
						&& board.HasCardInHand(Card.Cards.VAC_955t))
						{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(-50*(10-HandCount)));
								AddLog("美味奶酪"+(-50*(10-HandCount)));
						}else{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(999));
						}
					}
			}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(1999));
#endregion

#region 硬币 GAME_005
			// 如果当前手里有幸运币,可以费用为0,总体费用小于4,降低硬币使用优先级
			if (board.Hand.Exists(card => card.Template.Id == Card.Cards.GAME_005)
			&&board.ManaAvailable==0
			&&board.MaxMana<4
			&&isUsedEle==true
			)
			{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(350));
				AddLog("降低硬币使用优先级");
			}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(5));
			}
#endregion


#region JAM_036 乐坛灾星玛加萨
        if(board.HasCardInHand(Card.Cards.JAM_036)
        &&board.Hand.Count <=5
        &&isUsedEle==true
        &&hasEle==true
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(-350)); 
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(3999)); 
          AddLog("乐坛灾星玛加萨 -350");
        }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(200)); 
        }
#endregion

#region 烈焰亡魂 TTN_479
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_479, new Modifier(1500)); 
        var flamesSouls = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TTN_479);
        var icon = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GAME_005);
				if(flamesSouls!=null
				&&icon!=null
        // 一费后手有硬币,必下烈焰亡魂
        &&board.MaxMana==1
				&&((enemyAttack<=2&&isKuaigong)||(enemyAttack<=3&&!isKuaigong))
        ){
        	p.ComboModifier = new ComboSet(icon.Id,flamesSouls.Id);
          AddLog("一费后手有硬币,必下烈焰亡魂");
        }else if(flamesSouls!=null
				 // 两费之前如果敌方随从攻击力大于2,降低烈焰亡魂优先级
					&&board.MaxMana==2
					&&enemyAttack>1
					&&myAttack<enemyAttack
					&&HasOtherPlayableCard(board,Card.Cards.TTN_479)
					){
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_479, new Modifier(130));
							AddLog("两费之前如果敌方随从攻击力大于2,降低烈焰亡魂优先级");
					}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_479, new Modifier(-350));
				}
				// 法强随从小于5
				if(board.HasCardOnBoard(Card.Cards.TTN_479)
				&&spellCount<5
				){
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TTN_479, new Modifier(350)); 
				}
#endregion

#region 火羽精灵 CORE_UNG_809
        // 一费有烈焰亡魂,降低火羽精灵优先级
        if(board.HasCardInHand(Card.Cards.TTN_479)
        &&board.MaxMana==1
        &&board.HasCardInHand(Card.Cards.CORE_UNG_809)
				&&enemyAttack<3
				// 手上有硬币
				&&board.Hand.Exists(card => card.Template.Id == Card.Cards.GAME_005)
        ){
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_809, new Modifier(999)); 
          AddLog("一费有烈焰亡魂,降低火羽精灵优先级");
        }else if(
        // 手中没有小于下回合数的随从,且当前回合用过元素,降低火羽精灵极其衍生物的优先级
				// 火羽精灵极其衍生物和的数量为1
				board.Hand.Count(x =>x.Template.Id == Card.Cards.UNG_809t1)==1
        &&isUsedEle==true
        &&!board.Hand.Exists(card => card.CurrentCost <= board.MaxMana+1 &&card.Template.IsRace(Card.CRace.ELEMENTAL)&&card.Template.Id!=Card.Cards.UNG_809t1)
        ){
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(999)); 
          AddLog("使用过元素,降低火羽精灵衍生物的优先级");
        }else if (board.Hand.Exists(x => x.CurrentCost == 0 
					&&isUsedEle==true
					&& 
							(x.Template.Id == Card.Cards.CORE_UNG_809 || x.Template.Id == Card.Cards.UNG_809t1)))
					{
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_809, new Modifier(999));
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(999));
							AddLog("不用0费的火羽精灵和其衍生物");
					}else if(board.Hand.Exists(x => (x.Template.Id == Card.Cards.CORE_UNG_809 || x.Template.Id == Card.Cards.UNG_809t1))
					&&isUsedEle==true
					){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_809, new Modifier(130));
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(130));
						AddLog("火羽精灵和其衍生物 130");
					}

				// 手里有摇滚巨石 ETC_742
				if(board.HasCardInHand(Card.Cards.ETC_742)
				){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_UNG_809, new Modifier(999));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(999));
				}else{
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_UNG_809, new Modifier(-99));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(-99));
				}
#endregion

#region 艾泽里特巨人 WW_025
			var AeziritGiant = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.WW_025);
			// 手上艾泽里特巨人数量
			int AeziritGiantCount = board.Hand.Count(x => x.Template.Id == Card.Cards.WW_025);
			// 如果有其他可用随从,且艾泽里特巨人费用大于3,且使用过元素,降低艾泽里特巨人优先级
			if (AeziritGiant != null
			&&eleCount-AeziritGiantCount>0
			&&isUsedEle==false
			// 当前随从数小于等于5
			&&friendCount<=5
			)
			{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_025, new Modifier(999));
				AddLog("艾泽里特巨人不出");
			}else if (AeziritGiant != null
			&&AeziritGiant.CurrentCost<=4
			)
			{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_025, new Modifier(-350));
				AddLog("艾泽里特巨人-350");
			}
			// p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_025, new Modifier(-999)); 
#endregion

#region 暗石守卫 DEEP_027
        if(board.HasCardInHand(Card.Cards.DEEP_027)
        )
        {
          p.ForgeModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(-99)); 
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(350)); 
          AddLog("锻造暗石守卫");
        }
#endregion

#region 暗石守卫 DEEP_027t
        if(board.HasCardInHand(Card.Cards.DEEP_027t)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_027t, new Modifier(-99)); 
          AddLog("暗石守卫-99");
        }
#endregion

#region 湍流元素特布勒斯 WORK_013
        if(board.HasCardInHand(Card.Cards.WORK_013)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_013, new Modifier(-150)); 
          AddLog("湍流元素特布勒斯 -150");
        }
#endregion

#region 降低非元素随从牌使用优先级
    // 如果当回合没有使用过元素,降低非元素随从的使用优先值
    if(isUsedEle==false
    &&hasEle==true
    ){
        foreach(var c in board.Hand)
        {
            if(!c.Template.IsRace(Card.CRace.ELEMENTAL)){
                p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(999));
                p.CastSpellsModifiers.AddOrUpdate(c.Template.Id, new Modifier(999));
                // AddLog("非元素随从牌使用优先级降低"+c.Template.Name);
            }
        }
    }
#endregion

 #region WW_027	可靠陪伴
           // 遍历场上随从,如果类型是图腾,降低可靠陪伴
           if(board.HasCardInHand(Card.Cards.WW_027)
           &&isUsedEle==true
           ){
            foreach (var minion in board.Hand)
            {
                    if (minion.IsRace(Card.CRace.TOTEM))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_027, new Modifier(999,minion.Template.Id));
                        // AddLog("降低可靠陪伴对图腾的使用优先级");
                    }
                    if (minion.IsRace(Card.CRace.ELEMENTAL)
                    &&minion.CanAttack==true
                    )
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_027, new Modifier(-350,minion.Template.Id));
                        // AddLog("提高可靠陪伴对可攻击元素的使用优先级");
                    }
                }
           }
           if(board.HasCardInHand(Card.Cards.WW_027)
           &&isUsedEle!=true
           &&hasEle==false
           // 场上有元素
           &&board.MinionFriend.Exists(x => x.Template.IsRace(Card.CRace.ELEMENTAL))
           ){
            foreach (var minion in board.Hand)
            {
                    if (minion.IsRace(Card.CRace.TOTEM))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_027, new Modifier(999,minion.Template.Id));
                        // AddLog("降低可靠陪伴对图腾的使用优先级");
                    }
                    if (minion.IsRace(Card.CRace.ELEMENTAL)
                    &&minion.CanAttack==true
                    )
                    {
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_027, new Modifier(-350,minion.Template.Id));
                        // AddLog("提高可靠陪伴对可攻击元素的使用优先级");
                    }
                }
           }
#endregion

#region 步移山丘 WW_382
    // 提高优先级
    if(board.HasCardInHand(Card.Cards.WW_382)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_382, new Modifier(-350));
        AddLog("步移山丘 -350");
    }
#endregion

#region 塞拉赞恩 DEEP_036
    // 提高优先级
    if(board.HasCardInHand(Card.Cards.DEEP_036)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_036, new Modifier(-350));
        AddLog("塞拉赞恩 -350");
    }
    if(board.HasCardOnBoard(Card.Cards.DEEP_036)
    ){
        // 主动送掉
        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.DEEP_036, new Modifier(-350));
        AddLog("塞拉赞恩 送");
    }
#endregion

#region 燃灯元素 VAC_442
if(board.HasCardInHand(Card.Cards.VAC_442)){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_442, new Modifier(999));
	// 判斷是否在上回合打出過元素卡
	if (board.ElemPlayedLastTurn > 0)
	{
			// 如果燃灯元素處於標記狀態，優先打出
			if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.VAC_442))
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_442, new Modifier(-90));
					AddLog("上回合打出元素，燃灯元素在標記狀態時提高優先級 -90");
					// 遍历敌方随从,优先对44血3血随从使用
					foreach (var minion in board.MinionEnemy)
					{
							if (minion.CurrentHealth >=1&&minion.CurrentHealth<=4)
							{
									p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_442, new Modifier(-100*minion.CurrentHealth,minion.Template.Id));
									AddLog($"根据敌方随从血量,优先对高血量随从使用{minion.Template.NameCN}{-100*minion.CurrentHealth}");
							}
					}
			}
			else
			{
					// 如果燃灯元素未標記，降低其優先級
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_442, new Modifier(350));
					AddLog("上回合打出元素，但燃灯元素未標記，降低優先級 350");
			}
	}
	else
	{
			// 如果上回合未打出元素，降低燃灯元素的使用優先級
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_442, new Modifier(999));
			AddLog("上回合未打出元素，降低燃灯元素的使用優先級 999");
	}
}
#endregion

#region 三角测量 GDB_451
				// 提高使用优先级
				if(board.HasCardInHand(Card.Cards.GDB_451)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_451, new Modifier(-99));
					AddLog("三角测量 -99");
				}
#endregion

 #region CORE_AT_053 先祖知识
        if(board.HasCardInHand(Card.Cards.CORE_AT_053)
        &&isUsedEle==true
        &&hasEle==true
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_AT_053, new Modifier(-150)); 
        AddLog("先祖知识-150");
        }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_AT_053, new Modifier(150)); 
        }	
 #endregion

 #region 消融元素 VAC_328
        // 如果敌方没有随从,降低消融元素优先级
        if(board.MinionEnemy.Count==0
        // 且我方血量大于15
        &&board.HeroFriend.CurrentHealth>=15
        &&board.HasCardInHand(Card.Cards.VAC_328)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_328, new Modifier(200));
        AddLog("消融元素 200");
        }
         // 提高优先级
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_328, new Modifier(500));
    // 如果场上已经有一个消融元素,降低消融元素优先级
        if(board.HasCardOnBoard(Card.Cards.VAC_328)
        ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_328, new Modifier(350));
            AddLog("消融元素 350");
        }
 #endregion

 #region 三芯诡烛 TOY_370
        if(board.HasCardInHand(Card.Cards.TOY_370)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_370, new Modifier(-150));
        AddLog("三芯诡烛 -150");
        }
 #endregion

 #region 摇滚巨石 ETC_742
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_742, new Modifier(-500));
    //  处于非标记状态,降低使用优先级
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) != 1 && card.Template.Id == Card.Cards.ETC_742)
    &&isUsedEle==true
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_742, new Modifier(9999));
        AddLog("摇滚巨石 9999");
    }else if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.ETC_742)
		// 如果ETC_742标记状态,提高优先级
        ){
            foreach(var c in board.Hand){
            if(c.Template.Id ==Card.Cards.ETC_742 && c.IsPowered){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_742, new Modifier(-150));
            AddLog("摇滚巨石 -150");
            }
            if(c.Template.Id ==Card.Cards.WW_436 && !c.IsPowered){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_742, new Modifier(999));
                }
            }
        }
    // 手中有摇滚巨石,遍历手牌,提高一费卡的出牌顺序
    if(board.HasCardInHand(Card.Cards.ETC_742)
    ){
        foreach(var c in board.Hand)
        {
            if(c.CurrentCost==1){
                p.PlayOrderModifiers.AddOrUpdate(c.Template.Id, new Modifier(999));
                // AddLog("手中有摇滚巨石,遍历手牌,提高一费卡的出牌顺序");
            }
        }
    }
 #endregion

 #region 页岩蛛 DEEP_034
 if(board.HasCardInHand(Card.Cards.DEEP_034)
		){
//  定义手上页岩蛛的数量
		int rockSpider = board.Hand.Count(card => card.Template.Id == Card.Cards.DEEP_034);
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DEEP_034, new Modifier(990));
		if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) != 1 && card.Template.Id == Card.Cards.DEEP_034)
    //  处于非标记状态,降低使用优先级
    &&HasOtherPlayableCard(board,Card.Cards.DEEP_034)
		&&isUsedEle==true
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_034, new Modifier(9999));
        AddLog("页岩蛛 9999");
    }else if(board.MaxMana>=3
				&&(board.HasCardInHand(Card.Cards.GDB_302)
				//  手牌数小于等于7
				&&HandCount<=8)
				){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_034, new Modifier(999));
				AddLog("吸积炽焰出 页岩蛛2 999");
				}else if(board.Hand.Exists(x => x.CurrentCost == 0
				&&highValue>0
				&&isUsedEle==true
				&& x.Template.Id == Card.Cards.DEEP_034)
				&&isEasyKill
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_034, new Modifier(999));
						AddLog("手上有高价值随从,且本回合已经使用过元素,降低页岩蛛优先级");
				}else if(hasEleGiant){
				// 手里是否有小于等于2费的艾泽里特巨人
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_034, new Modifier(999));
						AddLog("手里有小于等于2费的艾泽里特巨人,降低页岩蛛优先级");
				}else{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_034, new Modifier(-150));
				}
		}
 #endregion

 #region 矿车巡逻兵 WW_326
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.WW_326)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_326, new Modifier(-150));
        AddLog("矿车巡逻兵 -150");
    }   
 #endregion

 #region 冰川裂片 CORE_UNG_205
 if(board.HasCardInHand(Card.Cards.CORE_UNG_205)
		){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(15));
			// 降低0费卡的使用优先级
			if(board.Hand.Exists(x=>x.CurrentCost==0 && x.Template.Id==Card.Cards.CORE_UNG_205)
			&&board.MaxMana<=5
			&&isUsedEle==true
			){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(350));
					AddLog("冰川裂片 350");
			}else if(board.Hand.Exists(x=> x.Template.Id==Card.Cards.CORE_UNG_205)
			// 如果一费时，敌方随从为0，手上有火羽精灵，降低冰川裂片优先级
			&&board.MaxMana==1
			&&board.MinionEnemy.Count==0
			){

					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(350));
					AddLog("一费时，敌方随从为0，手上有火羽精灵，降低冰川裂片优先级");
			}else if(isUsedEle==true
			// 如果本回合已经使用过元素，且敌方没有随从，降低冰川裂片优先级
			&&board.MinionEnemy.Count==0
			){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(350));
					AddLog("本回合已经使用过元素，且敌方没有随从，降低冰川裂片优先级");
			}else if(board.MinionEnemy.Count>0
			// 如果敌方有随从,降低冻敌方英雄脸优先级
			){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(130,board.HeroEnemy.Id));
					AddLog("冰川裂片降低冻敌方脸的可能");
			}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(130));
			}
		}
 #endregion

 #region 苏打火山 TOY_500
     if(board.HasCardInHand(Card.Cards.TOY_500)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_500, new Modifier(-5));
        AddLog("苏打火山 -5");
    }
 #endregion

 #region 灾变飓风斯卡尔 WW_026
     if(board.HasCardInHand(Card.Cards.WW_026)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_026, new Modifier(350));
        AddLog("灾变飓风斯卡尔 350");
    }
 #endregion

 #region 梦想策划师杰弗里斯 WORK_027
    // 如果下回合可以斩杀,提高优先级
    if(board.HasCardInHand(Card.Cards.WORK_027)
    &&BoardHelper.HasPotentialLethalNextTurn(board))
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_027, new Modifier(-150));
        AddLog("梦想策划师杰弗里斯 -150");
    }
 #endregion

 #region 跳吧，虫子！ ETC_362
      if(board.HasCardInHand(Card.Cards.ETC_362)
      &&isUsedEle==true
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_362, new Modifier(-99));
        AddLog("跳吧，虫子！ -99");
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_362, new Modifier(350));
    }
 #endregion

 #region 灵魂之火 EX1_308
      if(board.HasCardInHand(Card.Cards.EX1_308)
    // 手牌数
    &&board.Hand.Count-board.Hand.Count(x=>x.Template.Id==Card.Cards.EX1_308)==0
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_308, new Modifier(350));
        AddLog("灵魂之火 350");
    }
 #endregion

 #region 焦油爬行者 CORE_UNG_928
      if(board.HasCardInHand(Card.Cards.CORE_UNG_928)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_928, new Modifier(200));
        AddLog("焦油爬行者 200");
    }
 #endregion

 #region 雷电跳蛙 YOG_524
      if(board.HasCardInHand(Card.Cards.YOG_524)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOG_524, new Modifier(-99));
        AddLog("雷电跳蛙 -99");
    }
 #endregion

 #region 元素链
    //  如果当前回合没有使用过元素,遍历手牌,提高元素使用优先级
    if(isUsedEle==false
    &&hasEle==true
    // 敌方场上有CORE_NEW1_021末日预言者
    &&board.MinionEnemy.Exists(x=>x.Template.Id==Card.Cards.CORE_NEW1_021)
    ){
        foreach(var c in board.Hand)
        {
            if(c.Template.IsRace(Card.CRace.ELEMENTAL)){
                p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(-999));
                AddLog("敌方有末日,元素链提高元素使用优先级"+c.Template.Name);
            }
        }
    }
		// 如果当前随从数等于7
		if(isUsedEle==false
    &&hasEle==true
    &&board.MinionFriend.Count==7
    ){
        foreach(var c in board.Hand)
        {
            if(c.Template.IsRace(Card.CRace.ELEMENTAL)){
                p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(-999));
                AddLog("满随从,元素链提高元素使用优先级"+c.Template.Name);
            }
        }
    }
 #endregion


#region JAM_013 即兴演奏
        if(board.HasCardInHand(Card.Cards.JAM_013)
        ){
            // 遍历场上随从,降低给不可攻击随从的使用优先级
            foreach (var minion in board.MinionFriend)
            {
                if(minion.CanAttack==false){
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_013, new Modifier(999,minion.Template.Id));
                }
            }
        }
#endregion

#region 暗石守卫 DEEP_027
        if(board.HasCardInHand(Card.Cards.DEEP_027)
        )
        {
          p.ForgeModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(-99)); 
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_027, new Modifier(350)); 
          AddLog("锻造暗石守卫");
        }
#endregion

#region 暗石守卫 DEEP_027t
        if(board.HasCardInHand(Card.Cards.DEEP_027t)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DEEP_027t, new Modifier(-99)); 
          AddLog("暗石守卫-99");
        }
#endregion

#region 花岗岩熔铸体 SW_032 
     if(board.HasCardInHand(Card.Cards.SW_032)){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SW_032, new Modifier(-350));
				AddLog("花岗岩熔铸体 -350");
				}
#endregion

#region 吸积炽焰 GDB_302

				if(board.Hand.Exists(x => x.CurrentCost == 3
				&& x.Template.Id == Card.Cards.GDB_302)
				//  手牌数小于等于7
				&&HandCount<=8
				){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_302, new Modifier(-350));
				AddLog("吸积炽焰 -350");
				}else if(board.Hand.Exists(x => x.CurrentCost == 1
				&& x.Template.Id == Card.Cards.GDB_302)
				//  手牌数小于等于7
				&&HandCount<=8
				&&isEasyKill
				&&isUsedEle==true
				){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_302, new Modifier(999));
				AddLog("吸积炽焰 999");
				}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_302, new Modifier(350));
				}
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_302, new Modifier(999));
#endregion

#region 腐化残渣 YOG_519
        var decayedResidue = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.YOG_519);
				// 如果手上有小于3费的腐化残渣,降低使用优先值
				if(board.Hand.Exists(x=>x.CurrentCost<=3&&x.Template.Id==Card.Cards.GDB_305)
				&&decayedResidue!=null
				){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOG_519, new Modifier(-250));
					AddLog("腐化残渣出牌顺序降低");
				}else{
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOG_519, new Modifier(999));
				}
				
			// 当腐化残渣为3费时，提高使用优先值
			if(board.Hand.Exists(x=>x.CurrentCost<=3&&x.Template.Id==Card.Cards.YOG_519)
			&&decayedResidue!=null
			){
				p.ComboModifier = new ComboSet(decayedResidue.Id);
				AddLog("腐化残渣出");
			}else if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 
			&& card.Template.Id == Card.Cards.YOG_519)
			&&(!board.HasCardInHand(Card.Cards.WW_429))
			// 敌方没有合金顾问 WORK_023
			&&board.MinionEnemy.Exists(x=>x.Template.Id!=Card.Cards.WORK_023)
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOG_519, new Modifier(-550));
						AddLog("腐化残渣 -550");
				}else{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOG_519, new Modifier(350));
				}
#endregion

#region 流水档案管理员 TTN_095 
if(board.HasCardInHand(Card.Cards.TTN_095)){
// 手中流水档案管理员 TTN_095 的数量
		int waterArchivist = board.Hand.Count(card => card.Template.Id == Card.Cards.TTN_095);
			// 一费,有高价值随从,有其他可用元素,降低使用优先级//页岩蛛 DEEP_034
			if(board.Hand.Count(card => card.CurrentCost-coinCount <= board.MaxMana &&card.Template.IsRace(Card.CRace.ELEMENTAL)&&card.Template.Id!=Card.Cards.TTN_095)-waterArchivist>0
			&&board.MaxMana==1
			// 手里没有一费的元素
			&&board.Hand.Exists(x=>x.CurrentCost==1&&x.Template.IsRace(Card.CRace.ELEMENTAL))
			){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_095, new Modifier(350));
				AddLog(" 1费,降低流水档案管理员使用优先级");
			}else if(isUsedEle==true
			// 如果使用过元素,降低0费流水档案管理员的使用优先级
			&&board.Hand.Exists(x=>x.CurrentCost==0&&x.Template.Id==Card.Cards.TTN_095)
			){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_095, new Modifier(350));
				AddLog("使用过元素,降低0费流水档案管理员的使用优先级");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_095, new Modifier(130));
				AddLog("流水档案管理员130");
			}
}	
#endregion

#region 熔火符文 TTN_477  锻造过的熔火符文 TTN_477t1
		if(board.HasCardInHand(Card.Cards.TTN_477)
		&&isUsedEle==true
		// 且手上没有可用元素
		&&hasEle==false
		 ){
				p.ForgeModifiers.AddOrUpdate(Card.Cards.TTN_477, new Modifier(-350));
				AddLog("锻造熔火符文");
				}
		if(board.HasCardInHand(Card.Cards.TTN_477)
		 ){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_477, new Modifier(999));
			}
		if(board.HasCardInHand(Card.Cards.TTN_477t1)
		 ){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_477t1, new Modifier(350));
				}
#endregion
#region 溢流熔岩 WW_424
// 定義追蹤連續元素回合數的變量（需要在程序的其他部分初始化並更新）
// 例如，在初始化階段或外部代碼段設置 `int consecutiveElemTurns = 0;`

// 定義手上溢流熔岩的數量
int overflowLavaCount = board.Hand.Count(card => card.Template.Id == Card.Cards.WW_424);

// 基本優先順序調整
p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_424, new Modifier(-50));

// 判斷是否有溢流熔岩在手
if (board.HasCardInHand(Card.Cards.WW_424))
{
    foreach (var card in board.Hand)
    {
        if (card.Template.Id == Card.Cards.WW_424)
        {
            // 使用外部或內部計算的連續使用元素回合數（假設此變量已正確更新）
            int consecutiveElemTurns = board.ElemPlayedLastTurn > 0 ? 1 : 0;

            // 如果連續使用元素的回合數較少（≤1），降低優先級
            if (consecutiveElemTurns <= 1 || board.MinionFriend.Count >= 5)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_424, new Modifier(150));
                AddLog($"連續元素回合數較少({consecutiveElemTurns})或場上隨從數接近上限，降低溢流熔岩優先級 +150");
            }
            else
            {
                // 如果連續使用元素的回合數多（≥2），提高優先級
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_424, new Modifier(-100 * consecutiveElemTurns));
                AddLog($"連續元素回合數較多({consecutiveElemTurns})，提高溢流熔岩優先級 -{100 * consecutiveElemTurns}");
            }

            // 如果敵方有雷霆之神高戈奈斯 (TTN_800)，進一步降低優先級
            if (board.MinionEnemy.Exists(x => x.Template.Id == Card.Cards.TTN_800))
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_424, new Modifier(350));
                AddLog("敵方有雷霆之神高戈奈斯，降低溢流熔岩優先級 +350");
            }
        }
    }
}
#endregion

#region 烈焰喷涌 CORE_UNG_018
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_UNG_018, new Modifier(-500));
		if(board.HasCardInHand(Card.Cards.CORE_UNG_018)
		// 当前手上没有可用元素
		&&hasEle==false
		&&isUsedEle==false
		// 一费时,手上没有一费的元素随从
		&&(board.MaxMana>=2||
		(board.MaxMana==1&&board.Hand.Exists(x=>x.CurrentCost==1 && x.Template.IsRace(Card.CRace.ELEMENTAL))==false&&board.HasCardInHand(Card.Cards.GAME_005)))
		 ){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_018, new Modifier(-99));
				AddLog("烈焰喷涌-99");
				}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_018, new Modifier(-20));
				}
	
#endregion

#region TOY_330t5 奇利亚斯豪华版3000型
   // 可攻击随从
            if (board.HasCardInHand(Card.Cards.TOY_330t5) && canAttackMINIONs>=2)
            {
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(canAttackMINIONs * -20));
             AddLog("奇利亚斯豪华版9900型"+(canAttackMINIONs * -20));
            }
            else
            {
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(350));
            }
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
            // 不送
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(150));
#endregion

#region 破链角斗士 TTN_475
// 定义手上破链角斗士 TTN_475的数量
		int polian = board.Hand.Count(card => card.Template.Id == Card.Cards.TTN_475);
if(board.HasCardInHand(Card.Cards.TTN_475)
&&highValue==0
){
			foreach(var card in board.Hand)
      {
        if(card.Template.Id == Card.Cards.TTN_475)
        {
          var tag = quantityOverflowLava(card);
					// 手牌越少,优先级越高
				if(
					// 当前手牌数+上回合使用的元素<=10,能过4张或者能过一张没有吸积炽焰 GDB_302
					HandCount+tag<=9
					&&(tag>=3||(
						tag>=1
						&&!board.HasCardInHand(Card.Cards.GDB_302))//吸积炽焰 GDB_302
					)
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_475, new Modifier(-150*(tag+1)));
						AddLog("破链角斗士抽"+(tag+1));
						}else if(
					// 当前手牌数+上回合使用的元素<=10,能过4张或者能过一张没有吸积炽焰 GDB_302
					HandCount+tag>=10
					// 手上有其他可用随从
					&&eleCount-polian>0
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_475, new Modifier(999));
						AddLog("破链角斗士不抽");
					}
					}
			}
}
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_475, new Modifier(999));
#endregion

#region 阳炎耀斑 GDB_305
	int sumCount=spellCount+twoBlood;
	// 敌方随从越多,提高耀斑的使用优先值
		if(board.Hand.Exists(x=>x.Template.Id==Card.Cards.GDB_305)
		&&(twoBlood>=3||spellCount>=6)
		// 手里没有一费的元素
		&&!board.Hand.Exists(x=>x.CurrentCost<=1 && x.Template.IsRace(Card.CRace.ELEMENTAL))
		&&isUsedEle==true
		// &&sumCount.CurrentCost<=3
		 ){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_305, new Modifier(-10*sumCount));
				AddLog("阳炎耀斑"+(-10*sumCount));
			}else{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_305, new Modifier(350));
			}
			// 如果阳炎耀斑 GDB_305费用为0,提高使用顺序,其他时候,降低使用顺序
			if(board.Hand.Exists(x=>x.CurrentCost==0 && x.Template.Id==Card.Cards.GDB_305)
			){
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_305, new Modifier(999));
				AddLog("阳炎耀斑优先出");
			}else{
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_305, new Modifier(-500));
			}
#endregion

#region 恒星之火萨鲁恩 GDB_304
 		var starFireSarune = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GDB_304);
		// 定义烈焰亡魂 TTN_479
		var flameSoul = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TTN_479);
	// 提高使用优先级
		if(starFireSarune!=null
		&&board.MaxMana<8
		&&isEasyKill
		 ){
				p.ComboModifier = new ComboSet(starFireSarune.Id);
				// AddLog("恒星之火萨鲁恩出");
			}
		if(starFireSarune!=null
		&&board.MaxMana>=8
		&&flameSoul!=null
		&&isEasyKill
		 ){
				p.ComboModifier = new ComboSet(flameSoul.Id,starFireSarune.Id);
				// AddLog("恒星之火萨鲁恩出");
			}
#endregion

#region 伊辛迪奥斯 VAC_321
    var isingDios = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_321);
			// 场上有伊辛迪奥斯,不送伊辛迪奥斯
			if(board.HasCardOnBoard(Card.Cards.VAC_321)
			){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_321, new Modifier(350));
				AddLog("伊辛迪奥斯 不送");
			}
			// 小于五费之前有4费的伊辛迪奥斯,提高使用优先级
			if(board.Hand.Exists(x=>x.Template.Id==Card.Cards.VAC_321)
			&&isingDios!=null
			&&board.MaxMana<8
			&&isEasyKill
			&&!board.HasCardInHand(Card.Cards.GDB_304)//恒星之火萨鲁恩 GDB_304
			){
				  p.ComboModifier = new ComboSet(isingDios.Id);
				// p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_321, new Modifier(-350));
				AddLog("伊辛迪奥斯出");
			}else if(board.Hand.Exists(x=>x.Template.Id==Card.Cards.VAC_321)
			&&isingDios!=null
			&&board.MaxMana>=8
			&&isEasyKill
			&&flameSoul!=null
			&&!board.HasCardInHand(Card.Cards.GDB_304)//恒星之火萨鲁恩 GDB_304
			){
				  p.ComboModifier = new ComboSet(flameSoul.Id,isingDios.Id);
				// p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_321, new Modifier(-350));
				AddLog("伊辛迪奥斯出");
			}

#endregion

#region 碎裂巨岩迈沙顿 WW_429 
var fragmentedGiantRockMaiden = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.WW_429);
			// 当碎裂巨岩迈沙顿为3费时，提高使用优先值
			if(board.Hand.Exists(x=>x.CurrentCost<=3&&x.Template.Id==Card.Cards.WW_429)
			&&fragmentedGiantRockMaiden!=null
			&&HandCount<=8
			&&board.MaxMana<7
			&&isEasyKill
			){
				p.ComboModifier = new ComboSet(fragmentedGiantRockMaiden.Id);
				// p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_429, new Modifier(-550));
				AddLog("碎裂巨岩迈沙顿出");
			}else if(board.HasCardInHand(Card.Cards.WW_429)
			// 手牌数小于9
			&&HandCount<=8
		 ){
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_429, new Modifier(-350));
				AddLog("碎裂巨岩迈沙顿-350");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_429, new Modifier(350));
			}
#endregion

#region 自燃 GDB_456
if(board.HasCardInHand(Card.Cards.GDB_456)){
	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_456, new Modifier(-50));

	if (board.HasCardInHand(Card.Cards.GDB_456) && GetTag(board.Hand.First(card => card.Template.Id == Card.Cards.GDB_456), Card.GAME_TAG.POWERED_UP) != 1)
	{
			// 如果處於非激活狀態，降低優先級
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_456, new Modifier(350));
			AddLog("自燃處於非激活狀態，降低優先級 350");
	}
	else
	{
			// 默認情況下稍微降低優先級
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_456, new Modifier(130));
			// AddLog("自燃使用優先級默認為 350");
	}
}
#endregion

#region 焦油泥浆怪 TOY_000
 		// 降低0费焦油泥浆怪 TOY_000使用优先级
		if(board.Hand.Exists(x=>x.CurrentCost==0 && x.Template.Id==Card.Cards.TOY_000)
		&&board.MaxMana<=5
		&&isUsedEle==true
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_000, new Modifier(999));
		AddLog("焦油泥浆怪 999");
		}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_000, new Modifier(130));
		}
#endregion

#region 虚灵神谕者 GDB_310
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(999));
 		if(board.HasCardInHand(Card.Cards.GDB_310)
		
    ){
		// 遍历手牌随从,如果是法术,且当前法术牌的费用+3小于等于当前回合数,提高虚灵神谕者的优先级
				foreach (var item in board.Hand)
			{
				if(item.Type==Card.CType.SPELL
				&&item.CurrentCost+3<=board.ManaAvailable
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(-550));
					AddLog("虚灵神谕者 -550");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(999));
					// AddLog("虚灵神谕者 999");
				}
			}
    }
		// 场上有虚灵神谕者,不送
		if(board.MinionFriend.Exists(x=>x.Template.Id==Card.Cards.GDB_310&&x.HasSpellburst)){
			p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(150));
			foreach (var item in board.MinionFriend)
			{
				if(item.Template.Id==Card.Cards.GDB_310
				&&item.HasSpellburst
				){
						// 遍历手上随从,提高法术牌优先级
					foreach (var card in board.Hand)
					{
						if(card.Type==Card.CType.SPELL
						// 手牌数小于等于9
						&&HandCount<=8
						){
							p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-350));
							AddLog("提高法术牌优先级"+card.Template.NameCN);
						}
					}
				}
			}
		}
#endregion


#region 相同牌 一张有法强 一张没法强 优先用法强随从
// 创建一个字典来记录每张随从卡牌的数量
Dictionary<Card.Cards, int> minionCount = new Dictionary<Card.Cards, int>();
Dictionary<Card.Cards, bool> hasSpellPower = new Dictionary<Card.Cards, bool>();

// 遍历手牌，将每张随从卡牌的数量记录到字典中，并记录是否有法强
foreach (var card in board.Hand)
{
    if (card.Type == Card.CType.MINION)
    {
        if (minionCount.ContainsKey(card.Template.Id))
        {
            minionCount[card.Template.Id]++;
        }
        else
        {
            minionCount[card.Template.Id] = 1;
            hasSpellPower[card.Template.Id] = false;
        }

        if (card.SpellPower > 0)
        {
            hasSpellPower[card.Template.Id] = true;
        }
    }
}

// 再次遍历手牌，检查字典中每张随从卡牌的数量是否大于等于2，并且是否有法强和无法强的组合
foreach (var card in board.Hand)
{
    if (card.Type == Card.CType.MINION && minionCount[card.Template.Id] >= 2 && hasSpellPower[card.Template.Id])
    {
        bool hasSpellPowerCard = false;
        bool hasNonSpellPowerCard = false;

        foreach (var c in board.Hand)
        {
            if (c.Template.Id == card.Template.Id)
            {
                if (c.SpellPower > 0)
                {
                    hasSpellPowerCard = true;
                }
                else
                {
                    hasNonSpellPowerCard = true;
                }
            }
        }

        if (hasSpellPowerCard && hasNonSpellPowerCard)
        {
            p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-350));
            AddLog("提高有法强的随从卡牌的使用优先级: " + card.Template.NameCN);
        }
    }
}
#endregion

#region 减过费的高价值随从
var nextTurnMana = board.MaxMana + 1;
var valuableCards = new[] 
{
    (id: Card.Cards.WW_429, cost: 3),
    (id: Card.Cards.YOG_519, cost: 3),
    (id: Card.Cards.VAC_321, cost: 4),
    (id: Card.Cards.GDB_304, cost: 4)
};

var playableNextTurn = board.Hand.Count(card => 
    valuableCards.Any(v => 
        v.id == card.Template.Id && 
        card.CurrentCost <= Math.Min(v.cost, nextTurnMana)
    )
);

// 如果下回合可以使用减过费的高价值随从,当前剩余的法力水晶为0,降低当回合其他随从使用优先级
if (playableNextTurn > 0 && board.ManaAvailable == 0)
{
		foreach (var card in board.Hand)
		{
				if (!valuableCards.Any(v => v.id == card.Template.Id))
				{
						p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(350));
						AddLog("降低当回合其他随从使用优先级: " + card.Template.NameCN);
				}
		}
}
#endregion

#region 重新思考
// 遍历手上的卡牌,如果有这些卡牌,则重新思考
foreach (var card in board.Hand)
{
    // 排除硬币
        if(card.Template.Id != Card.Cards.GAME_005){
        p.ForcedResimulationCardList.Add(card.Template.Id);
        }
}
#endregion


#region 打印场上随从id
foreach (var item in board.MinionFriend)
{
    AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}
// 打印手上的卡牌id
foreach (var item in board.Hand)
{
		AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}

#endregion

#region 投降
  //  回合数大于等于15,敌方血量加护甲大于100,投降
	if(board.MaxMana>=15
	&&BoardHelper.GetEnemyHealthAndArmor(board)>=100
	){
		Bot.Concede();
    AddLog("投降");
	}
#endregion


#region 版本输出
        AddLog("---\n版本27.7 作者by77 Q群:943879501 DC:https://discord.gg/bc2kDM5E\n---");
#endregion

#region 攻击优先 卡牌威胁
var cardModifiers = new Dictionary<Card.Cards, int>
{
     { Card.Cards.WORK_040, 200 },//笨拙的杂役 WORK_040
     { Card.Cards.TOY_606, 200 },//测试假人 TOY_606   
     { Card.Cards.WW_382, 200 },//步移山丘 WW_382   
     { Card.Cards.GDB_841, 200 },//GDB_841	游侠斥候  
     { Card.Cards.GDB_110, 200 },//GDB_110	邪能动力源 
     { Card.Cards.CORE_ICC_210, 200 },//CORE_ICC_210	暗影升腾者 
     { Card.Cards.JAM_024, 200 },//JAM_024	布景光耀之子 
     { Card.Cards.CORE_CS3_014, 200 },//CORE_CS3_014	赤红教士
     
     { Card.Cards.GVG_075, 200 },//GVG_075 	船载火炮
     { Card.Cards.GDB_310, 200 },//GDB_310	虚灵神谕者 
     { Card.Cards.JAM_010, 200 },//JAM_010	点唱机图腾
     { Card.Cards.TOY_351t, -200 },//TOY_351t	神秘的蛋
    { Card.Cards.TOY_351, -200 },//TOY_351	神秘的蛋
    { Card.Cards.WW_391, 200 }, // WW_391	淘金客
    { Card.Cards.TOY_515, 200 }, // 水上舞者索尼娅 TOY_515
    { Card.Cards.CORE_TOY_100, 200 }, // 侏儒飞行员诺莉亚 CORE_TOY_100
    { Card.Cards.WW_381, 200 }, // 受伤的搬运工 WW_381
    { Card.Cards.TTN_900, -200 }, // 石心之王 TTN_900
    { Card.Cards.CORE_DAL_609, 200 }, // 卡雷苟斯 CORE_DAL_609
    { Card.Cards.VAC_406, 900 }, // 困倦的岛民 VAC_406
    { Card.Cards.TOY_646, 200 }, // 捣蛋林精 TOY_646
    { Card.Cards.TOY_357, 200 }, // 抱龙王噗鲁什 TOY_357
    { Card.Cards.VAC_507, 200 }, // 阳光汲取者莱妮莎 VAC_507
    { Card.Cards.WORK_042, 500 }, // 食肉格块 WORK_042
    { Card.Cards.WW_344, 200 }, // 威猛银翼巨龙 
    { Card.Cards.TOY_812, -500 },//TOY_812 皮普希·彩蹄
    { Card.Cards.VAC_532, 200 },//椰子火炮手 VAC_532
    { Card.Cards.RLK_511, 200 },//寒冬先锋	RLK_511
    { Card.Cards.TOY_505, 500 },//TOY_505	玩具船
    { Card.Cards.VAC_926t, 100 },//VAC_926t	坠落的伊利达雷
    { Card.Cards.TOY_381, 200 },//TOY_381	纸艺天使
    { Card.Cards.TSC_908, 55 }, // TSC_908	海中向导芬利爵士
    { Card.Cards.WORK_013, 55 }, // 湍流元素特布勒斯 WORK_013
    { Card.Cards.WW_901, 100 }, // WW_901	贪婪的伴侣
    { Card.Cards.TOY_824, 350 }, // 黑棘针线师
    { Card.Cards.VAC_927, 200 }, // 狂飙邪魔
    { Card.Cards.VAC_938, 200 }, // 粗暴的猢狲
    { Card.Cards.ETC_355, 200 }, // 剃刀沼泽摇滚明星
    { Card.Cards.WW_091, 500 },  // 腐臭淤泥波普加
    { Card.Cards.VAC_450, 9999 }, // 悠闲的曲奇
    { Card.Cards.TOY_028, 200 }, // 团队之灵
    { Card.Cards.VAC_436, 500 }, // 脆骨海盗
    { Card.Cards.VAC_321, 200 }, // 伊辛迪奥斯
    { Card.Cards.TTN_800, 200 }, // 雷霆之神高戈奈斯
    { Card.Cards.TTN_415, 200 }, // 卡兹格罗斯
    { Card.Cards.ETC_541, 200 }, // 盗版之王托尼
    { Card.Cards.CORE_LOOT_231, 200 }, // 奥术工匠
    { Card.Cards.ETC_339, 200 }, // 心动歌手
    { Card.Cards.ETC_833, 200 }, // 箭矢工匠
    { Card.Cards.MIS_026, 500 }, // 傀儡大师多里安
    { Card.Cards.CORE_WON_065, 200 }, // 随船外科医师
    { Card.Cards.WW_357, 500 }, // 老腐和老墓
    { Card.Cards.DEEP_999t2, 200 }, // 深岩之洲晶簇
    { Card.Cards.CFM_039, 200 }, // 杂耍小鬼
    { Card.Cards.WW_364t, 200 }, // 狡诈巨龙威拉罗克
    { Card.Cards.TSC_026t, 200 }, // 可拉克的壳
   
    { Card.Cards.WW_415, 200 }, // 许愿井
    { Card.Cards.CS3_014, 200 }, // 赤红教士
    { Card.Cards.YOG_516, 500 }, // 脱困古神尤格-萨隆
    { Card.Cards.NX2_033, 200 }, // 巨怪塔迪乌斯
    { Card.Cards.JAM_004, 500 }, // 镂骨恶犬
    { Card.Cards.TTN_330, 200 }, // Kologarn
    { Card.Cards.TTN_729, 200 }, // Melted Maker
    { Card.Cards.TTN_812, 150 }, // Victorious Vrykul
    { Card.Cards.TTN_479, 200 }, // Flame Revenant
    { Card.Cards.TTN_732, 250 }, // Invent-o-matic
    { Card.Cards.TTN_466, 250 }, // Minotauren
    { Card.Cards.TTN_801, 350 }, // Champion of Storms
    { Card.Cards.TTN_833, 200 }, // Disciple of Golganneth
    { Card.Cards.TTN_730, 300 }, // Lab Constructor
    { Card.Cards.TTN_920, 300 }, // Mimiron, the Mastermind
    { Card.Cards.TTN_856, 500 }, // Disciple of Amitus
    { Card.Cards.TTN_907, 200 }, // Astral Serpent
    { Card.Cards.TTN_071, 200 }, // Sif
    { Card.Cards.TTN_078, 200 }, // Observer of Myths
    { Card.Cards.TTN_843, 200 }, // Eredar Deceptor
   { Card.Cards.TTN_960, 200 }, // Sargeras, the Destroyer TITAN
    { Card.Cards.TTN_721, 200 }, // V-07-TR-0N Prime TITAN
    { Card.Cards.TTN_429, 500 }, // Aman'Thul TITAN
    { Card.Cards.TTN_858, 200 }, // Amitus, the Peacekeeper TITAN
    { Card.Cards.TTN_075, 200 }, // Norgannon TITAN
    { Card.Cards.TTN_092, 200 }, // Aggramar, the Avenger TITAN
    { Card.Cards.TTN_903, 200 }, // Eonar, the Life Binder TITAN
    { Card.Cards.TTN_862, 200 }, // Argus, the Emerald Star TITAN
    { Card.Cards.TTN_737, 200 }, // The Primus TITAN
    { Card.Cards.NX2_006, 200 }, // 旗标骷髅
    { Card.Cards.ETC_105, 200 }, // 立体声图腾
    { Card.Cards.ETC_522, 200 }, // 尖叫女妖
    { Card.Cards.RLK_121, 200 }, // 死亡侍僧
    { Card.Cards.RLK_539, 200 }, // 达尔坎·德拉希尔
    { Card.Cards.RLK_061, 200 }, // 战场通灵师
    { Card.Cards.RLK_824, 200 }, // 肢体商贩
    { Card.Cards.CORE_EX1_012, 200 }, // 血法师萨尔诺斯
    { Card.Cards.TSC_074, 200 }, // 克托里·光刃
    { Card.Cards.RLK_607, 200 }, // 搅局破法者
    { Card.Cards.RLK_924, 200 }, // 血骑士领袖莉亚德琳
    { Card.Cards.CORE_NEW1_020, 200 }, // 狂野炎术师
    { Card.Cards.RLK_083, 200 }, // 死亡寒冰
    { Card.Cards.RLK_572, 550 }, // 药剂大师普崔塞德
    { Card.Cards.RLK_218, 200 }, // 银月城奥术师
    { Card.Cards.REV_935, 200 }, // 派对图腾
    { Card.Cards.RLK_912, 200 }, // 天灾巨魔
    { Card.Cards.DMF_709, 200 }, // 巨型图腾埃索尔
    { Card.Cards.RLK_970, 200 }, // 陆行鸟牧人
    { Card.Cards.MAW_009t, 200 }, // 影犬
    { Card.Cards.TSC_922, 200 }, // 驻锚图腾
    { Card.Cards.AV_137, 200 }, // 深铁穴居人
    { Card.Cards.REV_515, 200 }, // 豪宅管家俄里翁
    { Card.Cards.TSC_959, 200 }, // 扎库尔
    { Card.Cards.TSC_218, 200 }, // 赛丝诺女士
    { Card.Cards.TSC_962, 200 }, // 老巨鳍
    { Card.Cards.REV_016, 200 }, // 邪恶的厨师
    { Card.Cards.REV_828t, 200 }, // 绑架犯的袋子
    { Card.Cards.KAR_006, 200 }, // 神秘女猎手
    { Card.Cards.REV_332, 200 }, // 心能提取者
    { Card.Cards.REV_011, 200 }, // 嫉妒收割者
    { Card.Cards.LOOT_412, 200 }, // 狗头人幻术师
    { Card.Cards.TSC_950, 200 }, // 海卓拉顿
    { Card.Cards.SW_062, 200 }, // 闪金镇豺狼人
    { Card.Cards.REV_513, 200 }, // 健谈的调酒师
    { Card.Cards.ONY_007, 200 }, // 监护者哈尔琳
    { Card.Cards.CS3_032, 200 }, // 龙巢之母奥妮克希亚
    { Card.Cards.SW_431, 200 }, // 花园猎豹
    { Card.Cards.AV_340, 200 }, // 亮铜之翼
    { Card.Cards.SW_458t, 200 }, // 塔维什的山羊
    { Card.Cards.WC_006, 200 }, // 安娜科德拉
    { Card.Cards.ONY_004, 200 }, // 团本首领奥妮克希亚
    { Card.Cards.TSC_032, 200 }, // 剑圣奥卡尼
    { Card.Cards.SW_319, 200 }, // 农夫
    { Card.Cards.TSC_002, 200 }, // 刺豚拳手
    { Card.Cards.CORE_LOE_077, 200 }, // 布莱恩·铜须
    { Card.Cards.TSC_620, 200 }, // 恶鞭海妖
    { Card.Cards.TSC_073, 200 }, // 拉伊·纳兹亚
    { Card.Cards.DED_006, 200 }, // 重拳先生
    { Card.Cards.CORE_AT_029, 200 }, // 锈水海盗
    { Card.Cards.BAR_074, 500 }, // 前沿哨所
    { Card.Cards.AV_118, 200 }, // 历战先锋
    { Card.Cards.GVG_040, 200 }, // 沙鳞灵魂行者
    { Card.Cards.BT_304, 200 }, // 改进型恐惧魔王
    { Card.Cards.SW_068, 200 }, // 莫尔葛熔魔
    { Card.Cards.DED_519, 200 }, // 迪菲亚炮手
    { Card.Cards.CFM_807, 200 }, // 大富翁比尔杜
    { Card.Cards.TSC_054, 200 }, // 机械鲨鱼
    { Card.Cards.GIL_646, 200 }, // 发条机器人
    { Card.Cards.SW_115, 200 }, // 伯尔纳·锤喙
    { Card.Cards.DMF_237, 200 }, // 狂欢报幕员
    { Card.Cards.DMF_217, 200 }, // 越线的游客
    { Card.Cards.DMF_120, 200 }, // 纳兹曼尼织血者
    { Card.Cards.DMF_707, 200 }, // 鱼人魔术师
    { Card.Cards.DMF_082, 200 }, // 暗月雕像
    { Card.Cards.DMF_708, 200 }, // 伊纳拉·碎雷
    { Card.Cards.DMF_102, 200 }, // 游戏管理员
    { Card.Cards.DMF_222, 200 }, // 获救的流民
    { Card.Cards.ULD_003, 200 }, // 了不起的杰弗里斯
    { Card.Cards.GVG_104, 200 }, // 大胖
    { Card.Cards.UNG_900, 250 }, // 灵魂歌者安布拉
    { Card.Cards.ULD_240, 250 }, // 对空奥术法师
    { Card.Cards.FP1_022, 50 }, // 空灵
    { Card.Cards.FP1_004, 50 }, // 疯狂的科学家
    { Card.Cards.BRM_002, 500 }, // 火妖
    { Card.Cards.CFM_020, 0 }, // 缚链者拉兹
    { Card.Cards.EX1_608, 250 }, // 巫师学徒
    { Card.Cards.BOT_447, -10 }, // 晶化师
    { Card.Cards.SCH_600t3, 250 }, // 加攻击的恶魔伙伴
    { Card.Cards.DRG_320, 0 }, // 新伊瑟拉
    { Card.Cards.CS2_237, 300 }, // 饥饿的秃鹫
    { Card.Cards.YOP_031, 250 }, // 螃蟹骑士
    { Card.Cards.BAR_537, 200 }, // 钢鬃卫兵
    { Card.Cards.BAR_035, 200 }, // 科卡尔驯犬者
    { Card.Cards.BAR_871, 250 }, // 士兵车队
    { Card.Cards.BAR_312, 200 }, // 占卜者车队
    { Card.Cards.BAR_043, 250 }, // 鱼人宝宝车队
    { Card.Cards.BAR_720, 230 }, // 古夫·符文图腾
    { Card.Cards.BAR_038, 200 }, // 塔维什·雷矛
    { Card.Cards.BAR_545, 200 }, // 奥术发光体
    { Card.Cards.BAR_888, 200 }, // 霜舌半人马
    { Card.Cards.BAR_317, 200 }, // 原野联络人
    { Card.Cards.BAR_918, 250 }, // 塔姆辛·罗姆
    { Card.Cards.BAR_076, 200 }, // 莫尔杉哨所
    { Card.Cards.BAR_890, 200 }, // 十字路口大嘴巴
    { Card.Cards.BAR_082, 200 }, // 贫瘠之地诱捕者
    { Card.Cards.BAR_540, 200 }, // 腐烂的普雷莫尔
    { Card.Cards.BAR_878, 200 }, // 战地医师老兵
    { Card.Cards.BAR_048, 200 }, // 布鲁坎
    { Card.Cards.BAR_075, 200 }, // 十字路口哨所
    { Card.Cards.BAR_744, 200 }, // 灵魂医者
    { Card.Cards.FP1_028, 200 }, // 送葬者
    { Card.Cards.CS3_019, 200 }, // 考瓦斯·血棘
    { Card.Cards.CORE_FP1_031, 200 }, // 瑞文戴尔男爵
    { Card.Cards.SCH_317, 200 }, // 团伙核心
    { Card.Cards.BAR_847, 200 }, // 洛卡拉
    { Card.Cards.CS3_025, 200 }, // 伦萨克大王
    { Card.Cards.YOP_021, 200 }, // 被禁锢的凤凰
    { Card.Cards.CS3_033, 200 }, // 沉睡者伊瑟拉
    { Card.Cards.CS3_034, 200 }, // 织法者玛里苟斯
    { Card.Cards.CORE_EX1_110, 0 }, // 凯恩·血蹄
    { Card.Cards.BAR_072, 0 }, // 火刃侍僧
    { Card.Cards.SCH_351, 200 } // 詹迪斯·巴罗夫
};

foreach (var cardModifier in cardModifiers)
{
    if (board.MinionEnemy.Any(minion => minion.Template.Id == cardModifier.Key))
    {
        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(cardModifier.Key, new Modifier(cardModifier.Value));
    }
}
#endregion
						 
#region Bot.Log
Bot.Log(_log);         
#endregion                       

            // ===== 全局硬规则：不解对面的“凯洛斯的蛋”系列（DINO_410*） =====
            // 口径：对面出现蛋阶段时，尽量不要用随从/武器/解牌去处理它（优先处理其他滚雪球点/直伤点）。
            try
            {
                if (board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.Template != null &&
                    (m.Template.Id == Card.Cards.DINO_410
                     || m.Template.Id == Card.Cards.DINO_410t2
                     || m.Template.Id == Card.Cards.DINO_410t3
                     || m.Template.Id == Card.Cards.DINO_410t4
                     || m.Template.Id == Card.Cards.DINO_410t5)))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t2, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t3, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t4, new Modifier(-9999));
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.DINO_410t5, new Modifier(-9999));
                }
            }
            catch { }


//德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER
            return p;
        }}
				 // 向 _log 字符串添加日志的私有方法，包括回车和新行
        private void AddLog(string log)
        {
            _log += "\r\n" + log;
        }
        //芬利·莫格顿爵士技能选择
        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            var filteredTable = _heroPowersPriorityTable.Where(x => choices.Contains(x.Key)).ToList();
            return filteredTable.First(x => x.Value == filteredTable.Max(y => y.Value)).Key;
        }
    
        //卡扎库斯选择
        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }
					// 判断除了指定卡牌之外是否还有其他卡牌
				public bool HasOtherPlayableCard(Board board, Card.Cards card)
				{
						return board.Hand.Any(c => c.CurrentCost <= board.ManaAvailable && c.Template.Id != card);
				}

        //计算类
        public static class BoardHelper
        {
            //得到敌方的血量和护甲之和
            public static int GetEnemyHealthAndArmor(Board board)
            {
                return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
            }

            //得到自己的法强
            public static int GetSpellPower(Board board)
            {
                //计算没有被沉默的随从的法术强度之和
                return board.MinionFriend.FindAll(x => x.IsSilenced == false).Sum(x => x.SpellPower);
            }

            //获得第二轮斩杀血线
            public static int GetSecondTurnLethalRange(Board board)
            {
                //敌方英雄的生命值和护甲之和减去可释放法术的伤害总和
                return GetEnemyHealthAndArmor(board) - GetPlayableSpellSequenceDamages(board);
            }

            //下一轮是否可以斩杀敌方英雄
            public static bool HasPotentialLethalNextTurn(Board board)
            {
                //如果敌方随从没有嘲讽并且造成伤害
                //(敌方生命值和护甲的总和 减去 下回合能生存下来的当前场上随从的总伤害 减去 下回合能攻击的可使用随从伤害总和)
                //后的血量小于总法术伤害
                if (!board.MinionEnemy.Any(x => x.IsTaunt) &&
                    (GetEnemyHealthAndArmor(board) - GetPotentialMinionDamages(board) - GetPlayableMinionSequenceDamages(GetPlayableMinionSequence(board), board))
                        <= GetTotalBlastDamagesInHand(board))
                {
                    return true;
                }
                //法术释放过敌方英雄的血量是否大于等于第二轮斩杀血线
                return GetRemainingBlastDamagesAfterSequence(board) >= GetSecondTurnLethalRange(board);
            }

            //获得下回合能生存下来的当前场上随从的总伤害
            public static int GetPotentialMinionDamages(Board board)
            {
                return GetPotentialMinionAttacker(board).Sum(x => x.CurrentAtk);
            }

            //获得下回合能生存下来的当前场上随从集合
            public static List<Card> GetPotentialMinionAttacker(Board board)
            {
                //下回合能生存下来的当前场上随从集合
                var minionscopy = board.MinionFriend.ToArray().ToList();

                //遍历 以敌方随从攻击力 降序排序 的 场上敌方随从集合
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //以友方随从攻击力 降序排序 的 场上的所有友方随从集合，如果该集合存在生命值大于与敌方随从攻击力
                    if (board.MinionFriend.OrderByDescending(x => x.CurrentAtk).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //以友方随从攻击力 降序排序 的 场上的所有友方随从集合,找出该集合中友方随从的生命值小于等于敌方随从的攻击力的随从
                        var tar = board.MinionFriend.OrderByDescending(x => x.CurrentAtk).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //获得下回合能生存下来的对面随从集合
            public static List<Card> GetSurvivalMinionEnemy(Board board)
            {
                //下回合能生存下来的当前对面场上随从集合
                var minionscopy = board.MinionEnemy.ToArray().ToList();

                //遍历 以友方随从攻击力 降序排序 的 场上友方可攻击随从集合
                foreach (var mi in board.MinionFriend.FindAll(x => x.CanAttack).OrderByDescending(x => x.CurrentAtk))
                {
                    //如果存在友方随从攻击力大于等于敌方随从血量
                    if (board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //以敌方随从血量降序排序的所有敌方随从集合，找出敌方生命值小于等于友方随从攻击力的随从
                        var tar = board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }
                return minionscopy;
            }

            //获取可以使用的随从集合
            public static List<Card.Cards> GetPlayableMinionSequence(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //当前剩余的法力水晶
                var manaAvailable = board.ManaAvailable;

                //遍历以手牌中费用降序排序的集合
                foreach (var card in board.Hand.OrderByDescending(x => x.CurrentCost))
                {
                    //如果当前卡牌不为随从，继续执行
                    if (card.Type != Card.CType.MINION) continue;

                    //当前法力值小于卡牌的费用，继续执行
                    if (manaAvailable < card.CurrentCost) continue;

                    //添加到容器里
                    ret.Add(card.Template.Id);

                    //修改当前使用随从后的法力水晶
                    manaAvailable -= card.CurrentCost;
                }

                return ret;
            }

            //获取可以使用的奥秘集合
            public static List<Card.Cards> GetPlayableSecret(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //遍历手牌中所有奥秘集合
                foreach (var card1 in board.Hand.FindAll(card => card.Template.IsSecret))
                {
                    if (board.Secret.Count > 0)
                    {
                        //遍历头上奥秘集合
                        foreach (var card2 in board.Secret.FindAll(card => CardTemplate.LoadFromId(card).IsSecret))
                        {

                            //如果手里奥秘和头上奥秘不相等
                            if (card1.Template.Id != card2)
                            {
                                //添加到容器里
                                ret.Add(card1.Template.Id);
                            }
                        }
                    }
                    else
                    { ret.Add(card1.Template.Id); }
                }
                return ret;
            }


            //获取下回合能攻击的可使用随从伤害总和
            public static int GetPlayableMinionSequenceDamages(List<Card.Cards> minions, Board board)
            {
                //下回合能攻击的可使用随从集合攻击力相加
                return GetPlayableMinionSequenceAttacker(minions, board).Sum(x => CardTemplate.LoadFromId(x).Atk);
            }

            //获取下回合能攻击的可使用随从集合
            public static List<Card.Cards> GetPlayableMinionSequenceAttacker(List<Card.Cards> minions, Board board)
            {
                //未处理的下回合能攻击的可使用随从集合
                var minionscopy = minions.ToArray().ToList();

                //遍历 以敌方随从攻击力 降序排序 的 场上敌方随从集合
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //以友方随从攻击力 降序排序 的 场上的所有友方随���集合，如果该集合存在生命值大于与敌方随从攻击力
                    if (minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).Any(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk))
                    {
                        //以友方随从攻击力 降序排序 的 场上的所有友方随从集合,找出该集合中友方随从的生命值小于等于敌方随从的攻击力的随从
                        var tar = minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).FirstOrDefault(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk);
                        //将该随从移除掉
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //获取当前回合手牌中的总法术伤害
            public static int GetTotalBlastDamagesInHand(Board board)
            {
                //从手牌中找出法术伤害表存在的法术的伤害总和(包括法强)
                return
                    board.Hand.FindAll(x => _spellDamagesTable.ContainsKey(x.Template.Id))
                        .Sum(x => _spellDamagesTable[x.Template.Id] + GetSpellPower(board));
            }

            //获取可以使用的法术集合
            public static List<Card.Cards> GetPlayableSpellSequence(Board board)
            {
                //卡片集合
                var ret = new List<Card.Cards>();

                //当前剩余的法力水晶
                var manaAvailable = board.ManaAvailable;

                if (board.Secret.Count > 0)
                {
                    //遍历以手牌中费用降序排序的集合
                    foreach (var card in board.Hand.OrderBy(x => x.CurrentCost))
                    {
                        //如果手牌中又不在法术序列的法术牌，继续执行
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //当前法力值小于卡牌的费用，继续执行
                        if (manaAvailable < card.CurrentCost) continue;

                        //添加到容器里
                        ret.Add(card.Template.Id);

                        //修改当前使用随从后的法力水晶
                        manaAvailable -= card.CurrentCost;
                    }
                }
                else if (board.Secret.Count == 0)
                {
                    //遍历以手牌中费用降序排序的集合
                    foreach (var card in board.Hand.FindAll(x => x.Type == Card.CType.SPELL).OrderBy(x => x.CurrentCost))
                    {
                        //如果手牌中又不在法术序列的法术牌，继续执行
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //当前法力值小于卡牌的费用，继续执行
                        if (manaAvailable < card.CurrentCost) continue;

                        //添加到容器里
                        ret.Add(card.Template.Id);

                        //修改当前使用随从后的法力水晶
                        manaAvailable -= card.CurrentCost;
                    }
                }

                return ret;
            }
            
            //获取存在于法术列表中的法术集合的伤害总和(包括法强)
            public static int GetSpellSequenceDamages(List<Card.Cards> sequence, Board board)
            {
                return
                    sequence.FindAll(x => _spellDamagesTable.ContainsKey(x))
                        .Sum(x => _spellDamagesTable[x] + GetSpellPower(board));
            }

            //得到可释放法术的伤害总和
            public static int GetPlayableSpellSequenceDamages(Board board)
            {
                return GetSpellSequenceDamages(GetPlayableSpellSequence(board), board);
            }
            
            //计算在法术释放过敌方英雄的血量
            public static int GetRemainingBlastDamagesAfterSequence(Board board)
            {
                //当前回合总法术伤害减去可释放法术的伤害总和
                return GetTotalBlastDamagesInHand(board) - GetPlayableSpellSequenceDamages(board);
            }

            public static bool IsOutCastCard(Card card, Board board)
            {
                var OutcastLeft = board.Hand.Find(x => x.CurrentCost >= 0);
                var OutcastRight = board.Hand.FindLast(x => x.CurrentCost >= 0);
                if (card.Template.Id == OutcastLeft.Template.Id
                    || card.Template.Id == OutcastRight.Template.Id)
                {
                    return true;
                    
                }
                return false;
            }
            public static bool IsGuldanOutCastCard(Card card, Board board)
            {
                if ((board.FriendGraveyard.Exists(x => CardTemplate.LoadFromId(x).Id == Card.Cards.BT_601)
                    && card.Template.Cost - card.CurrentCost == 3))
                {
                    return true;
                }
                
                return false;
            }
            public static bool  IsOutcast(Card card,Board board)
            {
                if(IsOutCastCard(card,board) || IsGuldanOutCastCard(card,board))
                {
                    return true;
                }
                return false;
            }


            //在没有法术的情况下有潜在致命的下一轮
            public static bool HasPotentialLethalNextTurnWithoutSpells(Board board)
            {
                if (!board.MinionEnemy.Any(x => x.IsTaunt) &&
                    (GetEnemyHealthAndArmor(board) -
                     GetPotentialMinionDamages(board) -
                     GetPlayableMinionSequenceDamages(GetPlayableMinionSequence(board), board) <=
                     0))
                {
                    return true;
                }
                return false;
            }
        }
    }
}