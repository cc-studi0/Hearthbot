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
                AddLog($"================ 标狂圣契骑 决策日志 v{ProfileVersion} ================");
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
               p.GlobalAggroModifier = (int)(a * 0.625 + 86);
                AddLog("攻击值"+(a * 0.625 + 86));
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
						// 敌方随从数量
						int enemyCount = board.MinionEnemy.Count;
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
						// 斩星巨刃 GDB_726
						int starCuttingBlade=board.MinionFriend.Count(x => x.Template.Id == Card.Cards.GDB_726)+board.Hand.Count(x => x.Template.Id == Card.Cards.GDB_726)+board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.GDB_726);
						// 定义坟场圣契的数量 神性圣契 GDB_138 正义圣契 BT_011 希望圣契 BT_024 智慧圣契 BT_025
						int divineSacrament=board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.GDB_138)
						+board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.BT_011)
						+board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.BT_024)
						+board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id == Card.Cards.BT_025);
						AddLog("斩星巨刃数量"+starCuttingBlade);
						AddLog("坟场圣契数量"+divineSacrament);
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

#region Card.Cards.HERO_04bp 英雄技能
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(120)); 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(-50));
#endregion

#region WW_331	奇迹推销员
         if(board.HasCardInHand(Card.Cards.WW_331)
				){
							p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); 
							AddLog("奇迹推销员 150");
				}
#endregion

#region 正义保护者 CORE_ICC_038
        // 降低使用优先级
        if(board.HasCardInHand(Card.Cards.CORE_ICC_038)
        // 如果敌方没有随从
        &&enemyCount==0
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ICC_038, new Modifier(150)); 
          AddLog("正义保护者 150");
        }
#endregion

#region WW_344	威猛银翼巨龙
      if(board.HasCardInHand(Card.Cards.WW_344)
      &&board.HasCardInHand(Card.Cards.DEEP_017)
      &&board.MinionFriend.Count <=5
      &&board.MaxMana>=2
      ){
      p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(250));
      AddLog("威猛银翼巨龙 250");
      }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(-5));
      }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(999));
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(150));
#endregion

 #region  WW_391	淘金客
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(999));
        if(board.HasCardInHand(Card.Cards.WW_391)
        // 我方攻击大于等于对方攻击
        &&myAttack>=enemyAttack
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(-350)); 
        AddLog("淘金客-350");
        }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(150));
        }
        // 不送淘金客
        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(150));
#endregion

#region DEEP_017	采矿事故
       if(board.HasCardInHand(Card.Cards.DEEP_017)
        && board.MinionFriend.Count <=5
       ){
       	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(-150)); 
        AddLog("采矿事故 -150");
      }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(350)); 
      }
       	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(999)); 
#endregion

#region CORE_GVG_061	作战动员
      if(board.HasCardInHand(Card.Cards.CORE_GVG_061)
        // 当前随从数大于等于6
        && board.MinionFriend.Count >= 6
      ){
      p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_GVG_061, new Modifier(350));
      AddLog("作战动员 350");
      }else{
      p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_GVG_061, new Modifier(-200));
	}
#endregion

 #region 布吉舞乐 Boogie Down ETC_318
            var Boogie = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.ETC_318);
            var coins = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GAME_005);
            if(Boogie!=null
            &&board.MinionFriend.Count <= 4
            &&oneCostMinionCount<=4
            &&board.MaxMana == 4
            ){
                p.ComboModifier = new ComboSet(Boogie.Id);
                AddLog("布吉舞乐出 1");
            }
            if(Boogie!=null
            &&board.MinionFriend.Count <= 4
            &&oneCostMinionCount<=4
            ){
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_318, new Modifier(-350));
                AddLog("布吉舞乐出 2");
            }
            // 如果三费有硬币,使用布吉舞乐
            if(Boogie!=null
            &&board.MaxMana == 3
            &&board.MinionFriend.Count <=4
            &&coins!=null
            &&oneCostMinionCount<=2
            ){
                p.ComboModifier = new ComboSet(coins.Id,Boogie.Id);
                AddLog("费用为3且有硬币,使用布吉舞乐");
            }
						// 如果手里有布吉舞乐,遍历手牌,提高手中一费随从出牌优先级,且当前为奇数回合
						if(board.HasCardInHand(Card.Cards.ETC_318)
						&&board.ManaAvailable%2==1
						){
							foreach (var item in board.Hand)
							{
								if(item.CurrentCost==1){
									p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(-150));
									AddLog("手里有布吉舞乐,提高一费随从优先级");
								}
							}
						}
#endregion

#region  CORE_EX1_586    海巨人
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_586, new Modifier(-999));
        var seaGiant = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.CORE_EX1_586);
        if(seaGiant!=null
        &&board.Hand.Exists(x=>x.CurrentCost==0 && x.Template.Id==Card.Cards.CORE_EX1_586
        )){
             p.ComboModifier = new ComboSet(seaGiant.Id);
            AddLog("低费海巨人出");
        }
        if(board.Hand.Exists(x=>(x.CurrentCost<=3) && x.Template.Id==Card.Cards.CORE_EX1_586)
        // 且手里没有1费随从
        &&!board.Hand.Exists(x=>x.CurrentCost==1)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_586, new Modifier(-150));
        AddLog("低于3费海巨人出");
        }
#endregion

#region 音响工程师普兹克 ETC_425
        // 根据敌方手牌的数量提高音响工程师普兹克优先级
        if(board.HasCardInHand(Card.Cards.ETC_425)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_425, new Modifier(-30*enemyHandCount));
          AddLog("音响工程师普兹克"+(-30*enemyHandCount));
        }
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_425, new Modifier(999));
#endregion

#region 光速抢购 TOY_716
        // 我方随从小于3,降低使用优先级
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(15)); 
        if(board.HasCardInHand(Card.Cards.TOY_716)
        &&liveMinionCount >= 2
        )
        {
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(-10*liveMinionCount)); 
          AddLog("光速抢购"+(-10*liveMinionCount));
					// 布吉舞乐出牌优先级提高
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_318, new Modifier(800));

        }else{
          	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(999)); 
				}
#endregion

#region 戈贡佐姆 VAC_955
        // 提高优先级
        if(board.HasCardInHand(Card.Cards.VAC_955)
        // 三费且有作战动员 CORE_GVG_061
        &&board.HasCardInHand(Card.Cards.CORE_GVG_061)
        // 随从小于6
        &&board.MinionFriend.Count <=5
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_955, new Modifier(350));
          AddLog("戈贡佐姆350");
        }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_955, new Modifier(-350));
        }

#endregion

#region VAC_955t 美味奶酪
			foreach(var card in board.Hand)
      {
        if(card.Template.Id == Card.Cards.VAC_955t)
        {
						var tag = quantityOverflowLava(card);
						// 如果当前手牌数小于等于3,且最大回合数大于等于8,提高优先级
						if (board.MinionFriend.Count<=4
						//  场上没随从
						&&(tag>=5)
						&& board.HasCardInHand(Card.Cards.VAC_955t))
						{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(-90*(10-HandCount)));
								AddLog("美味奶酪"+(-90*(10-HandCount)));
						}else{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(999));
						}
					}
			}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(1999));
#endregion

#region 硬币 GAME_005
    //   如果手上有活体天光 WW_366,降低硬币使用优先级
    if(board.HasCardInHand(Card.Cards.WW_366)
    &&board.HasCardInHand(Card.Cards.GAME_005)
    ){
     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(350));//硬币 GAME_005
    AddLog("留硬币出活体天光");
    }else if(board.HasCardInHand(Card.Cards.GDB_726)
		// 如果手上有斩星巨刃 GDB_726,2费之前不用硬币
		&&board.HasCardInHand(Card.Cards.GAME_005)
		&&board.MaxMana<2
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(350));
			AddLog("如果手上有斩星巨刃 GDB_726,2费之前不用硬币");
		}else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));
    }
#endregion

#region 污手街供货商 Grimestreet Outfitter ID：CORE_CFM_753
      if(board.HasCardInHand(Card.Cards.CORE_CFM_753)
      ){
      p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CFM_753, new Modifier(-350));
      p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_CFM_753, new Modifier(999)); 
      AddLog("污手街供货商 -350");
      }
#endregion
#region TTN_860	无人机拆解器
			if(board.HasCardInHand(Card.Cards.TTN_860)){
				 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_860, new Modifier(200)); 
				 AddLog("无人机拆解器 200");
			}
#endregion

#region 画师的美德 TOY_810
		// 手上没武器,手牌随从数大于等于2,提高优先级
         if(TheVirtuesofthePainter!=null
        &&board.WeaponFriend == null 
        &&minionNumber>=2
        &&enemyAttack<=myAttack
        ){
           
            p.ComboModifier = new ComboSet(TheVirtuesofthePainter.Id);
            AddLog("画师的美德");
        }
        // 攻击优先值提高
         if(board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.TOY_810
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.TOY_810, -99);
        AddLog("画师的美德先攻击");
        }
#endregion

#region VAC_923t 圣沙泽尔
		// 提高场上圣沙泽尔的优先级
         if(board.HasCardOnBoard(Card.Cards.VAC_923t)
        ){
        p.LocationsModifiers.AddOrUpdate(Card.Cards.VAC_923t, new Modifier(-99));
            AddLog("圣沙泽尔 -99");
        }
				// 如果手上有星际旅行者,降低圣沙泽尔的优先级
				if(board.HasCardInHand(Card.Cards.VAC_923t)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_923t, new Modifier(99));
					AddLog("手上有星际旅行者,降低圣沙泽尔的优先级");
				}
#endregion

// #region  圣沙泽尔  VAC_923
// 				// 如果手上有星际旅行者 GDB_721,降低圣沙泽尔的优先级
// 				if(board.HasCardInHand(Card.Cards.VAC_923)
// 				&&board.HasCardInHand(Card.Cards.GDB_721)
// 				){
// 					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_923t, new Modifier(150));
// 					AddLog("手上有星际旅行者,降低圣沙泽尔的优先级");
// 				}
// #endregion

#region VAC_923 圣沙泽尔
			  if(board.HasCardOnBoard(Card.Cards.VAC_923)
        ){
					// 遍历场上随从,如果被冻结,则不攻击
					foreach (var item in board.MinionFriend)
					{

						if(item.IsFrozen)
						{
							p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_923, new Modifier(9999));
							AddLog("圣沙泽尔不攻击");
						}
        	}
				}
#endregion

#region VAC_440	海关执法者
		// 提高优先级
            if(board.HasCardInHand(Card.Cards.VAC_440)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_440, new Modifier(-150));
            AddLog("海关执法者 -150");
            }
#endregion

#region WW_331t	蛇油
		// 前期不用蛇油 
		if(board.HasCardInHand(Card.Cards.WW_331t)
		// 当前手上有可以出的随从
		&&board.Hand.Count(card => card.CurrentCost<=board.ManaAvailable)>=1
		&&(board.MaxMana<=5
		||board.HasCardInHand(Card.Cards.ETC_418)//乐器技师 ETC_418
		||board.HasCardInHand(Card.Cards.GDB_726)//斩星巨刃 GDB_726
		||board.HasCardInHand(Card.Cards.WW_366)//活体天光 WW_366
		||board.HasCardInHand(Card.Cards.GDB_310))//虚灵神谕者 GDB_310
		){
		p.TradeModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(999));
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(-999));
		AddLog("不交易蛇油");
		}else{
		p.TradeModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(350));
		}
		// 如果牌库为空,不交易蛇油
		if(board.HasCardInHand(Card.Cards.WW_331t)
		&&board.FriendDeckCount<=2
		){
		p.TradeModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(999));
		AddLog("牌库为空不交易蛇油");
		}
#endregion

#region WW_336	棱彩光束
        	        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(999)); 
                    // 敌方小于3血的随从越多,棱彩光束越优先放
                    if(board.HasCardInHand(Card.Cards.WW_336)
                    &&enemyThreeHealthMinionCount>2
                    ){
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(-20*enemyThreeHealthMinionCount));
                    AddLog("棱彩光束"+(-20*enemyThreeHealthMinionCount));
                    }else{
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(150));
										}
#endregion

#region 鱼人木乃伊 CORE_ULD_723
				    if(board.HasCardInHand(Card.Cards.CORE_ULD_723)
                    // 当有其他一费随从时
          &&board.Hand.Count(card => card.CurrentCost==1&&card.Template.Id!=Card.Cards.CORE_ULD_723)>=1
					// 小于3费时
					&&board.MaxMana<=3
					){
        	        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(130)); 
					AddLog("鱼人木乃伊130");
					}else{
					        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(-20)); 
					}
                  
#endregion

#region TTN_908	十字军光环
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_908, new Modifier(9999)); 
        var crusaderAura = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TTN_908);
			// 场上随从大于2提高优先级
			 if(crusaderAura!=null
            &&(canAttackMinion>2
            &&liveMinionCount>2
            ||(
                // 当前回合大于等于7费,且手中有决斗
                board.MaxMana>=7
                &&board.HasCardInHand(Card.Cards.WW_051)
            )
            )
            ){
             p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_908, new Modifier(-350*canAttackMinion));
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(200)); 
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_GVG_061, new Modifier(200)); 
            AddLog("十字军光环"+(-350*canAttackMinion));
            }else if(BoardHelper.HasPotentialLethalNextTurn(board)
						// 如果下回合叫杀必出,提高优先级
						&&crusaderAura!=null
						){
							p.ComboModifier = new ComboSet(crusaderAura.Id); 
							AddLog("十字军光环出");
						}else{
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_908, new Modifier(999));
			}
#endregion

#region WW_051	决战！
			if(board.HasCardInHand(Card.Cards.WW_051)
            //  场上没有奇利亚斯豪华版3000型
            &&enemyAttack<4
            ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_051, new Modifier(999));
            AddLog("决战！ 999");
            }
#endregion

#region TTN_907	星界翔龙
// 场上有TTN_907	星界翔龙 手牌数小于9,降低TTN_907	星界翔龙攻击优先值
            if(board.HasCardOnBoard(Card.Cards.TTN_907)
            &&HandCount<8
            ){
            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TTN_907, new Modifier(-999));
            AddLog("星界翔龙不攻击过牌");
            }
            // 如果牌库没有牌,场上有星界翔龙,提高送掉的优先值
            if(board.HasCardOnBoard(Card.Cards.TTN_907)
            &&board.FriendDeckCount <= 2
            ){
            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TTN_907, new Modifier(-999));
            AddLog("星界翔龙送掉");
            }
#endregion

#region TOY_330t5 奇利亚斯豪华版3000型
   // 可攻击随从
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
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

#region VAC_958	进化融合怪
            if(board.HasCardInHand(Card.Cards.VAC_958)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(-99));
            AddLog("进化融合怪-99");
            }
#endregion

#region TTN_858	维和者阿米图斯
       	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(3999)); 
       	p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(1999)); 
        // 如果我方随从的攻击值加上2*我方随从的数量大于敌方英雄的生命值,且对方无嘲讽,提高泰坦2技能的优先级
        if(board.HasCardOnBoard(Card.Cards.TTN_858)
        &&myAttack+2*friendCount>BoardHelper.GetEnemyHealthAndArmor(board)
        &&!board.MinionEnemy.Exists(x => x.IsTaunt)
        ){
        p.ChoicesModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-350,2));
        AddLog("维和者阿米图斯选2叫杀");
        }
        if(liveMinionCount >= 3
        &&canAttackMinion>=3
        ){
        p.ChoicesModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-350,2));
        // AddLog("维和者阿米图斯选2");
        }

        if(board.HasCardInHand(Card.Cards.TTN_858)
        &&liveMinionCount >= 3
        &&canAttackMinion>=3
        ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-150));
            AddLog("维和者阿米图斯 -150");
        }
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-150));
#endregion

#region 乐器技师 ETC_418
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(888)); 
        if(board.HasCardInHand(Card.Cards.ETC_418)
        // 坟场 斩星巨刃 GDB_726 小于2
        &&starCuttingBlade< 2
				&&board.MaxMana>=2
				// 手上没有 斩星巨刃 GDB_726
				&&(!board.Hand.Exists(x=>x.Template.Id==Card.Cards.GDB_726))
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-350)); 
          AddLog("乐器技师 -350");
        }else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(200)); 
				}
#endregion

#region 健身肌器人 YOG_525
        if(board.HasCardInHand(Card.Cards.YOG_525)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOG_525, new Modifier(350)); 
          p.ForgeModifiers.AddOrUpdate(Card.Cards.YOG_525, new Modifier(-99));
          AddLog("健身肌器人 200");
        }
#endregion

#region 健身肌器人 YOG_525t
        if(board.HasCardInHand(Card.Cards.YOG_525t)
        &&board.Hand.Count>=3
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOG_525t, new Modifier(-99)); 
          AddLog("健身肌器人 -99");
        }
#endregion

#region JAM_036 乐坛灾星玛加萨
        if(board.HasCardInHand(Card.Cards.JAM_036)
        &&board.Hand.Count <=5
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(-350)); 
          AddLog("乐坛灾星玛加萨 -350");
        }
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(999)); 
#endregion

#region TTN_924 锋鳞
    //    手上没有一费随从,提高使用优先级
        if(board.HasCardInHand(Card.Cards.TTN_924)
        &&board.Hand.Count(card => card.CurrentCost == 1) == 0
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_924, new Modifier(-350));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_924, new Modifier(-5));
        AddLog("锋鳞 -350");
        }
#endregion

#region ETC_102 空气吉他手
// 如果手上有武器,提高使用优先级,否则降低使用优先级
        if(board.HasCardInHand(Card.Cards.ETC_102)
        &&board.WeaponFriend != null
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_102, new Modifier(-99));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_102, new Modifier(999));
        AddLog("空气吉他手-99");
        }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_102, new Modifier(350));
        }
#endregion

#region 金属探测器 VAC_330
// 如果手里没武器,且敌方有3血及以下的随从,则提高使用优先级
        if(board.HasCardInHand(Card.Cards.VAC_330)
        &&board.WeaponFriend == null
        &&board.MinionEnemy.Exists(x => x.CurrentHealth <= 3)
        ){
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.VAC_330, new Modifier(-20));
        AddLog("金属探测器 -20");
        }
    // 如果手上有武器,敌方没有随从,降低武器攻击敌方英雄的优先值
    if(enemyCount == 0
        &&board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.VAC_330
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.VAC_330, 0, board.HeroEnemy.Id);
        AddLog("对面没有随从,降低金属探测器的攻击优先级");
        }
        if(board.MinionEnemy.Exists(x => x.CurrentHealth <= 3)
        &&board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.VAC_330
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.VAC_330, -99);
        AddLog("武器先攻击");
        }
#endregion

#region VAC_959t05 追踪护符 Amulet of Tracking 随机获取3张传说卡牌。（然后将其变形成为普通卡牌！）
// 如果手牌数小于等于3,提高使用优先级
        if(board.HasCardInHand(Card.Cards.VAC_959t05)
        &&HandCount<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t05, new Modifier(-99));
        AddLog("追踪护符-99");
        }
#endregion

#region VAC_959t06 生灵护符 Amulet of Critters 随机召唤一个法力值消耗为（4）的随从并使其获得嘲讽。（但它无法攻击！）
        if(board.HasCardInHand(Card.Cards.VAC_959t06)
				// 场上随从小于等于6
				&&board.MinionFriend.Count<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t06, new Modifier(-99));
        AddLog("生灵护符-99");
        }
#endregion

#region VAC_959t08 能量护符 Amulet of Energy 为你的英雄恢复12点生命值。（然后受到6点伤害！）
        if(board.HasCardInHand(Card.Cards.VAC_959t08)
                // 可恢复生命值大于6
                &&board.HeroFriend.MaxHealth-board.HeroFriend.CurrentHealth > 6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t08, new Modifier(-99));
        AddLog("能量护符-99");
        }
#endregion

#region VAC_959t10 挺进护符 Amulet of Strides 使你手牌中的所有卡牌的法力值消耗减少（1）点。（法术牌除外！）
        if(board.HasCardInHand(Card.Cards.VAC_959t10)
        ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t10, new Modifier(-99));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_959t10, new Modifier(999));
        AddLog("挺进护符-99");
        }
#endregion

#region YOG_509 守护者的力量 
        if(board.HasCardInHand(Card.Cards.YOG_509)
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.YOG_509, new Modifier(350));
        // AddLog("守护者的力量 350");
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOG_509, new Modifier(-999));
#endregion

#region TOY_813 玩具队长塔林姆 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_813, new Modifier(1000));
#endregion

#region WORK_003 假期规划 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WORK_003, new Modifier(1000));
				// 如果手牌数小于等于3,提高使用优先级
				if(board.HasCardInHand(Card.Cards.WORK_003)
				&&HandCount<=7
				// 场上随从小于等于4
				&&board.MinionFriend.Count<=4
				){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WORK_003, new Modifier(-150));
				AddLog("假期规划-150");
				}
#endregion

#region 不送白银新手 CS2_101t
    // 如果我方随从数量大于敌方随从数量,且手上有十字军光环 或者光速抢购 降低送随从的优先级
		if((board.HasCardInHand(Card.Cards.TTN_908)
		||board.HasCardInHand(Card.Cards.TOY_716))
		&&board.HasCardOnBoard(Card.Cards.CS2_101t)
		){
			p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CS2_101t, new Modifier(150));
			AddLog("不送白银新手");
		}
#endregion

#region 海滩导购 VAC_332
    if(board.HasCardInHand(Card.Cards.VAC_332)
		// 手上没有法力值为0的法术牌
		&&!board.Hand.Exists(x=>x.CurrentCost==0)
    ){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_332, new Modifier(-99));
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_332, new Modifier(999));
    AddLog("海滩导购 -99");
    }else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_332, new Modifier(350));
		}
#endregion

#region 星际旅行者 GDB_721
    if(board.HasCardInHand(Card.Cards.GDB_721)
		// 手上没有四费的信仰圣契 GDB_139
		&&!board.Hand.Exists(x=>x.Template.Id==Card.Cards.GDB_139&&x.CurrentCost==4)
    ){
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_721, new Modifier(-150));
    AddLog("星际旅行者 -150");
    }
#endregion

#region 女伯爵莉亚德琳 CORE_BT_334
		// 如果divineSacrament的数量+当前手牌的数量>=10,提高女伯爵莉亚德琳的优先级
		if(board.HasCardInHand(Card.Cards.CORE_BT_334)
		&&board.Hand.Count<=4
		&&board.Hand.Count+divineSacrament>=10
		){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_BT_334, new Modifier(-350));
			AddLog("女伯爵莉亚德琳-350");
		}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_BT_334, new Modifier(-150));
#endregion

#region 阳光汲取者莱妮莎 VAC_507
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(999));
		// 莱妮莎斩杀逻辑
		// 定义场上虚灵的数量 虚灵神谕者 GDB_310
		int ghostCount = board.MinionFriend.Count(x => x.Template.Id == Card.Cards.GDB_310);
		// 定义手上 把经理叫来！ VAC_460 的数量
		int managerCount = board.Hand.Count(x => x.Template.Id == Card.Cards.VAC_460);
		// 定义手上阳光汲取者莱妮莎 VAC_507的数量
		int sunCount = board.Hand.Count(x => x.Template.Id == Card.Cards.VAC_507);
		// 定义手上圣光荧光棒的数量 MIS_709
		int lightCount = board.Hand.Count(x => x.Template.Id == Card.Cards.MIS_709);
		// 定义敌方英雄的生命值+护甲
		int enemyHealth = BoardHelper.GetEnemyHealthAndArmor(board);
			//定义手上0费的神性圣契 GDB_138的数量
		int divineSacrament0 = board.Hand.Count(x => x.Template.Id == Card.Cards.GDB_138&&x.CurrentCost<=1);
		if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 当前场上有阳光汲取者莱妮莎 VAC_507,斩杀线为managerCount*4+lightCount*8,需要4费可以启动
		&&board.MaxMana>=4
		&&managerCount*4+lightCount*8>=enemyHealth
		){
			// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用优先级
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				&&item.Template.Id!=Card.Cards.MIS_709
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("单游客24血斩杀 1");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 当手上有阳光汲取者莱妮莎 VAC_507,斩杀线为managerCount*4+lightCount*8,需要9费可以启动
		&&board.MaxMana>=9
		&&managerCount*4+lightCount*8>=enemyHealth
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用优先级
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				&&item.Template.Id!=Card.Cards.MIS_709
				&&item.Template.Id!=Card.Cards.VAC_507
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-999));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("单游客24血斩杀 2");
		}else if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 当前场上有阳光汲取者莱妮莎 VAC_507,ghostCount为1,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.MaxMana>=4
		&&ghostCount==1
		&&managerCount*6+lightCount*10>=enemyHealth
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用优先级
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				&&item.Template.Id!=Card.Cards.MIS_709
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("游客+单虚灵32血斩杀 1");
		}else if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 当前场上有阳光汲取者莱妮莎 VAC_507,手上有虚灵,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.HasCardInHand(Card.Cards.GDB_310)
		&&managerCount*6+lightCount*10>=enemyHealth
		&&board.MaxMana>=7
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用优先级
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				&&item.Template.Id!=Card.Cards.MIS_709
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("游客+单虚灵32血斩杀 2");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 当前手上有阳光汲取者莱妮莎 VAC_507,ghostCount为1,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.MaxMana>=9
		&&ghostCount==1
		&&managerCount*6+lightCount*10>=enemyHealth
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用优先级
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				&&item.Template.Id!=Card.Cards.MIS_709
				&&item.Template.Id!=Card.Cards.VAC_507
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-999));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("游客+单虚灵32血斩杀 3");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 当前场上有阳光汲取者莱妮莎 VAC_507,手上有虚灵,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.HasCardInHand(Card.Cards.GDB_310)
		&&managerCount*6+lightCount*10>=enemyHealth
		&&board.MaxMana>=10
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用优先级
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				&&item.Template.Id!=Card.Cards.MIS_709
				&&item.Template.Id!=Card.Cards.VAC_507
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-999));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("游客+单虚灵32血斩杀 4");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 如果当前随从攻击力+手上神性圣契的数量*2大于等于敌方英雄的生命值,提高阳光汲取者莱妮莎的使用优先级
		&&myAttack+divineSacrament0*3>=enemyHealth
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-350));
		}else{
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(150));
		}
#endregion

#region 圣光荧光棒 MIS_709
		// 如果手上有圣光荧光棒 MIS_709,提高使用优先级
		if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) != 1 && card.Template.Id == Card.Cards.MIS_709)
		){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(350));
		}else{
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-20));
		}
#endregion

#region 蓄谋诈骗犯 VAC_333
        if(board.HasCardInHand(Card.Cards.VAC_333)
    ){
    foreach(var card in board.Hand)
        {
            if(card.Template.Id == Card.Cards.VAC_333)
            {
            var tag = GetTag(card,SmartBot.Plugins.API.Card.GAME_TAG.DISPLAY_CARD_ON_MOUSEOVER);
            if (tag != -1)
                {
                    var lastCardPlayed = CardTemplate.TemplateList.Values.FirstOrDefault(x => x.DbfId == tag);
                    if (lastCardPlayed != null)
                    {
                        var cardId = lastCardPlayed.Id;
                        var intentionalFraudster = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.VAC_333);
                        if (
                        // 如果上一张用的是 女伯爵莉亚德琳 CORE_BT_334 ,场上随从小于等于5,提高诈骗犯的使用优先级
                        cardId == Card.Cards.CORE_BT_334
                        &&board.MinionFriend.Count<=5
                        &&intentionalFraudster!=null
                        ){
                        p.ComboModifier = new ComboSet(intentionalFraudster.Id);
                        AddLog("蓄谋诈骗犯+女伯爵莉亚德琳");
                        }else if (
                        // 如果上一张用的是 伊瑞尔，希望信标 GDB_141,场上随从小于等于5,提高诈骗犯的使用优先级
                        cardId == Card.Cards.GDB_141
                        &&board.MinionFriend.Count<=5
                        &&intentionalFraudster!=null
                        ){
                         p.ComboModifier = new ComboSet(intentionalFraudster.Id);
                        AddLog("蓄谋诈骗犯+伊瑞尔，希望信标");
                        }else if (
                        // 如果上一张用的是 希望圣契 BT_024,场上随从小于等于5,提高诈骗犯的使用优先级
                        cardId == Card.Cards.BT_024
                        &&board.MinionFriend.Count<=5
                        &&intentionalFraudster!=null
                        ){
                         p.ComboModifier = new ComboSet(intentionalFraudster.Id);
                        AddLog("蓄谋诈骗犯+希望圣契");
                        }else if (
                        // 如果上一张用的是 露米娅 GDB_144,场上随从小于等于5,提高诈骗犯的使用优先级
                        cardId == Card.Cards.GDB_144
                        &&board.MinionFriend.Count<=5
                        &&intentionalFraudster!=null
                        ){
                         p.ComboModifier = new ComboSet(intentionalFraudster.Id);
                        AddLog("蓄谋诈骗犯+露米娅");
                        }else if (
                        // 如果上一张用的是 信仰圣契 GDB_139,场上随从小于等于4,且手里没有维和者阿米图斯 TTN_858和提尔之涕 TTN_855t/TTN_855,提高诈骗犯的使用优先级
                        cardId == Card.Cards.GDB_139
                        &&board.MinionFriend.Count<=4
                        &&intentionalFraudster!=null
                        ){
                         p.ComboModifier = new ComboSet(intentionalFraudster.Id);
                        AddLog("蓄谋诈骗犯+信仰圣契");
                        }else if (
                        // 如果上一张用的是 阳光汲取者莱妮莎 VAC_507,场上随从小于等于5,且手里没有维和者阿米图斯 TTN_858和提尔之涕 TTN_855t/TTN_855,提高诈骗犯的使用优先级
                        cardId == Card.Cards.VAC_507
                        &&board.MinionFriend.Count<=5
                        &&intentionalFraudster!=null
                        ){
                         p.ComboModifier = new ComboSet(intentionalFraudster.Id);
                        AddLog("蓄谋诈骗犯+阳光汲取者莱妮莎");
                        }else if (
                        // 如果上一张用的是 圣沙泽尔 VAC_923,场上随从小于等于5,提高诈骗犯的使用优先级
                        cardId == Card.Cards.VAC_923
                        &&board.MinionFriend.Count<=5
                        &&intentionalFraudster!=null
                        ){
                         p.ComboModifier = new ComboSet(intentionalFraudster.Id);
                        AddLog("蓄谋诈骗犯+圣沙泽尔");
                        }else{
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_333, new Modifier(999));
                        }
                    }
                }else{
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_333, new Modifier(999));
                }
            }
        }
    }
#endregion

#region 神性圣契 GDB_138
// 0费时,提高使用优先级
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_138 && x.CurrentCost == 0)
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_138, new Modifier(-350));
AddLog("神性圣契提高使用优先级");
}else{
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_138, new Modifier(350));
}
#endregion

#region 明澈圣契 GDB_137
p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(999));
// 费用不为0，降低使用优先级
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_137 && x.CurrentCost >=2)
// 手牌数小于等于8
&&HandCount<=8
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(-150));
AddLog("明澈圣契-20");
}
// 0费时,提高使用优先级
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_137 && x.CurrentCost ==1)
// 手牌数小于等于8
&&HandCount<=8
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(-150));
AddLog("明澈圣契-99");
}
// 0费时,提高使用优先级
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_137 && x.CurrentCost == 0)
// 手牌数小于等于8
&&HandCount<=8
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(-350));
AddLog("明澈圣契-350");
}
#endregion

#region 露米娅 GDB_144
// 降低露米娅 GDB_144攻击顺序
if(board.HasCardOnBoard(Card.Cards.GDB_144)
){
p.AttackOrderModifiers.AddOrUpdate(Card.Cards.GDB_144, new Modifier(-50));
AddLog("露米娅最后攻击");
}
if(board.HasCardInHand(Card.Cards.GDB_144)
// 血量大于20或者敌方攻击小于6
&&(board.HeroFriend.CurrentHealth>20
&&enemyAttack<6)
){
p.AttackOrderModifiers.AddOrUpdate(Card.Cards.GDB_144, new Modifier(350));
AddLog("露米娅慢一点出");
}
#endregion

#region 斩星巨刃 GDB_726
        if(board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.GDB_726
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.GDB_726, -99);
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.GDB_726, new Modifier(-50));
        AddLog("武器先攻击");
        }
				if( board.HasCardInHand(Card.Cards.GDB_726)
				// 手上没有四费的信仰圣契 GDB_139
				&&!board.Hand.Exists(x=>x.Template.Id==Card.Cards.GDB_139&&x.CurrentCost==4)
        ){
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.GDB_726, -350);
        AddLog("斩星巨刃出");
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_726, 2999);
#endregion

#region 活体天光 WW_366
        if(board.HasCardInHand(Card.Cards.WW_366)
        // 小于等于5费,提高使用优先级
        // 如果敌方攻击大于我方攻击
        &&(board.MaxMana<=5||enemyAttack>myAttack)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_366, new Modifier(-350));
        AddLog("活体天光-350");
        }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_366, new Modifier(-99));
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_366, new Modifier(3999));
#endregion


#region 进化融合怪 VAC_958
//当我方场上有进化融合怪，提高buff牌对进化融合怪 的使用优先级
if(board.HasCardOnBoard(Card.Cards.VAC_958)
){
	//  阿达尔之手 CORE_BT_292
	if(board.HasCardInHand(Card.Cards.CORE_BT_292)
	){
	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_292, new Modifier(-99,Card.Cards.VAC_958));
	AddLog("阿达尔之手+进化融合怪");
	}
		//  智慧圣契 BT_025
	if(board.HasCardInHand(Card.Cards.BT_025)
	){
	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BT_025, new Modifier(-99,Card.Cards.VAC_958));
	AddLog("智慧圣契+进化融合怪");
	}
	// 神性圣契 GDB_138
	if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_138 && x.CurrentCost <=2)
	){
	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_138, new Modifier(-99,Card.Cards.VAC_958));
	AddLog("神性圣契+进化融合怪");
	}
}
#endregion

#region 王牌发球手 VAC_920
//当我方场上有王牌发球手 VAC_920，提高buff牌对进化融合怪 的使用优先级
if(board.HasCardOnBoard(Card.Cards.VAC_920)
){
	//  阿达尔之手 CORE_BT_292
	if(board.HasCardInHand(Card.Cards.CORE_BT_292)
	){
	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_292, new Modifier(-99,Card.Cards.VAC_920));
	AddLog("阿达尔之手+王牌发球手");
	}
	//  智慧圣契 BT_025
	if(board.HasCardInHand(Card.Cards.BT_025)
	){
	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BT_025, new Modifier(-99,Card.Cards.VAC_920));
	AddLog("智慧圣契+王牌发球手");
	}
	// 神性圣契 GDB_138
	if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_138 && x.CurrentCost <=2)
	){
	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_138, new Modifier(-99,Card.Cards.VAC_920));
	AddLog("神性圣契+王牌发球手");
	}
}
#endregion

#region 冻感舞步 JAM_006
// 下回合叫杀，提高冻感舞步优先级
    if(board.HasCardInHand(Card.Cards.JAM_006)
    &&BoardHelper.HasPotentialLethalNextTurn(board)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_006, new Modifier(-350));
        AddLog("下回合叫杀，提高冻感舞步优先级");
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_006, new Modifier(130));
    }
#endregion

#region 灯火机器人 MIS_918/MIS_918t
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(-500));
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(-500));
		// 当手上有灯火机器人时
		if(board.HasCardInHand(Card.Cards.MIS_918)
		||board.HasCardInHand(Card.Cards.MIS_918t)
		){
			// 遍历手上随从,如果为灯火机器人,且费用小于等于2,提高使用优先级
			foreach (var item in board.Hand)
			{
				if(item.Template.Id==Card.Cards.MIS_918
				&&item.CurrentCost<=2
				// 手上没有0费法术
				&&!board.Hand.Exists(x=>x.CurrentCost<=1&&x.Type==Card.CType.SPELL)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(-20));
					// AddLog("灯火机器人"+(-20));
				}else{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(350));
				}
				if(item.Template.Id==Card.Cards.MIS_918t
				&&item.CurrentCost<=2
				// 手上没有0费法术
				&&!board.Hand.Exists(x=>x.CurrentCost<=1&&x.Type==Card.CType.SPELL)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(-20));
					// AddLog("灯火机器人"+(-20));
				}else{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(350));
				}
			}
		}
#endregion

#region 闪亮舞池 JAM_009
// 敌方没有随从，场上有闪亮舞池 JAM_009，提高使用优先级
		if(board.HasCardOnBoard(Card.Cards.JAM_009)
		&&enemyCount==0
		){
				p.LocationsModifiers.AddOrUpdate(Card.Cards.JAM_009, new Modifier(350));
				AddLog("敌方没有随从，不用闪亮舞池");
		}
#endregion

#region 艾瑞达蛮兵 GDB_320
// 当敌方随从大于等于2时,敌方随从越多,提高使用优先级
		if(board.HasCardInHand(Card.Cards.GDB_320)
		&&enemyCount>=2
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_320, new Modifier(-20*enemyCount));
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_320, new Modifier(1000));
		AddLog("艾瑞达蛮兵"+(-20*enemyCount));
		}
#endregion

#region 无界空宇 GDB_142
var boundlessSpace = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GDB_142);
p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_142, new Modifier(999));
if(boundlessSpace!=null
	&&enemyAttack-myAttack>=5
		){
		p.ComboModifier = new ComboSet(boundlessSpace.Id);
		AddLog("无界空宇出");
		}
#endregion

#region 遇险的航天员 GDB_861
if(board.HasCardInHand(Card.Cards.GDB_861)
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_861, new Modifier(-150));
		AddLog("遇险的航天员 -150");
		}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_861, new Modifier(666));
#endregion

#region  星光漫游者 GDB_720
if(board.HasCardInHand(Card.Cards.GDB_720)
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_720, new Modifier(-99));
		AddLog("星光漫游者 -99");
		}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_720, new Modifier(999));
#endregion


#region 投降
    // 我方场攻小于5,手牌数小于等于1,牌库为空,敌方血量+护甲>20,当前费用大于10,投降
    if (HandCount<=1
        &&board.FriendDeckCount==0
        &&board.MaxMana>=10
        // 敌方血量大于20 
        &&BoardHelper.GetEnemyHealthAndArmor(board)>20
        )
    {
        Bot.Concede();
        AddLog("投降");
    }
#endregion

#region 星际研究员 GDB_728
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(888));
		// 一费不用星际研究员
		if(board.HasCardInHand(Card.Cards.GDB_728)
		&&board.MaxMana==1
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(350));
		AddLog("星际研究员 350");
		}else if(board.HasCardInHand(Card.Cards.GDB_728)
		&&board.MaxMana>=2
    ){
		// 遍历手牌随从,如果是法术,且当前法术牌的费用+3小于等于当前回合数,提高优先级
				foreach (var item in board.Hand)
			{
				if(item.Type==Card.CType.SPELL
				&&item.CurrentCost+2<=board.MaxMana
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(-99));
					// AddLog("星际研究员"+(-150));
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(200));
				}
			}
    }
			if(board.MinionFriend.Exists(x=>x.Template.Id==Card.Cards.GDB_728&&x.HasSpellburst)){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(150));
					// 遍历手上随从,提高法术牌优先级
				foreach (var card in board.Hand)
				{
					if(card.Type==Card.CType.SPELL
					// 手牌数小于等于9
					&&HandCount<=9
					){
						p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-150));
						// AddLog("提高法术牌优先级"+(card.Template.Id));
					}
				}
			}
#endregion

#region 虚灵神谕者 GDB_310
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(999));
 		if(board.HasCardInHand(Card.Cards.GDB_310)
		// 且场上没有拥有法术迸发HasSpellburst的虚灵神谕者 GDB_310
		&&!board.MinionFriend.Exists(x=>x.Template.Id==Card.Cards.GDB_310&&x.HasSpellburst)
    ){
		// 遍历手牌随从,如果是法术,且当前法术牌的费用+3小于等于当前回合数,提高虚灵神谕者的优先级
				foreach (var item in board.Hand)
			{
				if(item.Type==Card.CType.SPELL
				&&item.CurrentCost+3<=board.ManaAvailable
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(-350));
					AddLog("虚灵神谕者 -350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(999));
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
							p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-150));
							// AddLog("提高法术牌优先级");
						}
					}
				}
			}
		}
#endregion

#region 把经理叫来！ VAC_460
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-50));
#endregion

#region 星域戒卫 GDB_461
    // 如果非激活状态,降低其使用优先级
		if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.GDB_461)
		){
			
			foreach(var card in board.Hand)
				{
					if(card.Template.Id == Card.Cards.GDB_461){
								var tag = GetTag(card,SmartBot.Plugins.API.Card.GAME_TAG.DISPLAY_CARD_ON_MOUSEOVER);
							if (tag != -1)
									{
											var lastCardPlayed = CardTemplate.TemplateList.Values.FirstOrDefault(x => x.DbfId == tag);
											var cardId = lastCardPlayed.Id;
											if (
											// 如果上一张使用的是 星际研究员 GDB_728
											cardId == Card.Cards.GDB_728
											){
											p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_461, new Modifier(-99));
											AddLog("星域戒卫回星际研究员");
											}
												if (
											// 如果上一张使用的是 星际旅行者 GDB_721
											cardId == Card.Cards.GDB_721
											){
											p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_461, new Modifier(-99));
											AddLog("星域戒卫回星际旅行者");
											}
									}
						}
					}
		}else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_461, new Modifier(999));
		}
#endregion

#region 信仰圣契 GDB_139
    // 随从数小于等于5,提高使用优先级
		if(board.HasCardInHand(Card.Cards.GDB_139)
		&&board.MinionFriend.Count<=5
		){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_139, new Modifier(-150));
		AddLog("信仰圣契-150");
		}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_139, new Modifier(999));
#endregion

#region 伊瑞尔，希望信标 GDB_141
    // 如果手牌数小于等于7,提高使用优先级
		if(board.HasCardInHand(Card.Cards.GDB_141)
		&&HandCount<=7
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_141, new Modifier(-50*enemyAttack));
		AddLog("伊瑞尔，希望信标"+-50*enemyAttack);
		}
#endregion

#region 神圣牛仔 WW_335
    if(board.HasCardInHand(Card.Cards.WW_335)
		){
		// 遍历手牌随从,如果是法术,且当前法术牌的费用+3小于等于当前回合数,提高神圣牛仔的优先级
				foreach (var item in board.Hand)
			{
				if(item.Type==Card.CType.SPELL
				&&item.CurrentCost==0
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_335, new Modifier(150));
					AddLog("神圣牛仔 150");
				}
			}
		}
#endregion

#region 智慧圣契 BT_025
  //  提高智慧圣契 BT_025出牌顺序
	if(board.HasCardInHand(Card.Cards.BT_025)
	){
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.BT_025, new Modifier(888));
	}
#endregion

#region 纳迦侍从 TID_098
// 如果场上有纳迦侍从 TID_098
if(board.HasCardOnBoard(Card.Cards.TID_098)
// 手牌数小于等于9
&&HandCount<9
// 牌库不为空
&&board.FriendDeckCount>0
){
	// 遍历手牌,如果是法术,提高对纳迦侍从使用优先级
	foreach (var item in board.Hand)
	{
		if(item.Type==Card.CType.SPELL
		&&item.Template.Id!=Card.Cards.MIS_709//圣光荧光棒 MIS_709
		){
			p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(-150, Card.Cards.TID_098));
			AddLog("纳迦侍从-150"+item.Template.Id);
		}
	}
}
#endregion

#region 智慧祝福 EX1_363
	if(board.HasCardInHand(Card.Cards.EX1_363)
	// 手牌数小于等于9
	&&HandCount<=9
	){
		// 遍历我方场上可攻击随从,如果其生命值大于等于4,提高智慧祝福对其使用优先级
		foreach (var item in board.MinionFriend)
		{
			if(item.CanAttack
			){
				// 如果血量越多,提高使用优先级
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_363, new Modifier(-50*item.CurrentHealth, item.Template.Id));
				AddLog("智慧祝福-150"+item.Template.Name);
			}
		}
	}
#endregion

#region 光鳐 TID_077
	if(board.HasCardInHand(Card.Cards.TID_077)
	){
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TID_077, new Modifier(-150));
	}
#endregion

#region 水晶学 BOT_909
	if(board.HasCardInHand(Card.Cards.BOT_909)
	&&oneCostMinionCount<4
	){
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.BOT_909, new Modifier(550));
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BOT_909, new Modifier(-350));
		AddLog("水晶学-350");
	}else{
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.BOT_909, new Modifier(999));
	}
#endregion

#region 圣礼骑士 BAR_873
	if(board.HasCardInHand(Card.Cards.BAR_873)
	){
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.BAR_873, new Modifier(550));
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.BAR_873, new Modifier(-99));
		AddLog("圣礼骑士-99");
	}
#endregion

#region 光鳐 TID_077
	if(board.Hand.Exists(x=>x.CurrentCost>0 && x.Template.Id==Card.Cards.TID_077)
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TID_077, new Modifier(150));
		AddLog("光鳐 150");
	}
#endregion

#region 适者生存 UNG_961
	if(board.HasCardInHand(Card.Cards.UNG_961)
	){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.UNG_961, new Modifier(130));
		AddLog("适者生存 130");
	}
#endregion

#region 精英对决 WON_049
	if(board.HasCardInHand(Card.Cards.WON_049)
	){
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WON_049, new Modifier(999));
	}
#endregion

#region 神圣佳酿 VAC_916t2/VAC_916t3/VAC_916
        // 便利随从,降低神圣佳酿 VAC_916t2/VAC_916t3/VAC_916对不可攻击随从的使用优先级
        if((board.HasCardInHand(Card.Cards.VAC_916t2)||board.HasCardInHand(Card.Cards.VAC_916t3)||board.HasCardInHand(Card.Cards.VAC_916))
        &&board.MinionFriend.Count > 0
        ){
        foreach (var card in board.MinionFriend)
            {
                if (card.CanAttack)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(-20, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(-20, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(-20, card.Template.Id));
                }else{
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(55, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(55, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(55, card.Template.Id));
                }
                if(card.Template.Id == Card.Cards.TOY_330t5)//TOY_330t5 奇利亚斯豪华版3000型
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(350));
                }
            }
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
				// 不对敌方英雄使用
				if((board.HasCardInHand(Card.Cards.VAC_916t2)||board.HasCardInHand(Card.Cards.VAC_916t3)||board.HasCardInHand(Card.Cards.VAC_916))
        ){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(999, board.HeroEnemy.Id));
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(999, board.HeroEnemy.Id));
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(999, board.HeroEnemy.Id));
        }
#endregion

#region 降低对不可攻击卡牌使用法术
//遍历手牌,如果是法术,遍历场上友方随从,降低对不可攻击随从的使用优先级
// 如果场上有可攻击随从
if(board.MinionFriend.Exists(x=>x.CanAttack)
){
	foreach (var item in board.Hand)
	{
		if(item.Type==Card.CType.SPELL
		){
			foreach (var minion in board.MinionFriend)
			{
				if(!minion.CanAttack
				// 且不为纳迦侍从 TID_098
				&&minion.Template.Id!=Card.Cards.TID_098
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(150, minion.Template.Id));
					// AddLog("降低对不可攻击随从的使用优先级"+item.Template.Id);
				}
			}
		}
	}
}
#endregion

#region 重新思考
var reflection = new Dictionary<Card.Cards, int>
{
     { Card.Cards.GDB_721, 500 },// 星际旅行者 GDB_721
     { Card.Cards.GDB_139, 500 },//信仰圣契 GDB_139
     { Card.Cards.GDB_728, 500 },//星际研究员 GDB_728
     { Card.Cards.GDB_310, 500 },//虚灵神谕者 GDB_310
     { Card.Cards.GDB_142, 500 },//无界空宇 GDB_142
     { Card.Cards.WW_335, 500 },// 神圣牛仔 WW_335
     { Card.Cards.TTN_908, 500 },// 十字军光环 TTN_908
     { Card.Cards.MIS_918, 500 },// 灯火机器人 MIS_918 / MIS_918t
     { Card.Cards.MIS_918t, 500 },// 灯火机器人 MIS_918 / MIS_918t
     { Card.Cards.VAC_460, 500 },//把经理叫来！ VAC_460
     { Card.Cards.MIS_709, 500 },//圣光荧光棒 MIS_709
     { Card.Cards.BT_025, 500 },//智慧圣契 BT_025
     { Card.Cards.BT_020, 500 },//奥尔多侍从 BT_020
     { Card.Cards.UNG_961, 500 },//适者生存 UNG_961
};
foreach (var card in reflection)
{
	if (board.Hand.Exists(x => x.Template.Id == card.Key))
    {
        p.ForcedResimulationCardList.Add(card.Key);
    }
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