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
    public class STDFloodPaladin  : Profile
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
      private const string ProfileVersion = "2026-02-17.43";
	      public ProfileParameters GetParameters(Board board)
	      {
	            _log = "";
	            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 }; 
	            int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
	            int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;

	            try
	            {
	                AddLog($"================ 标准龙战 决策日志 v{ProfileVersion} ================");
	                AddLog($"敌方血甲: {enemyHealth} | 我方血甲: {friendHealth} | 法力:{board.ManaAvailable} | 手牌:{board.Hand.Count} | 牌库:{board.FriendDeckCount}");
	                AddLog("手牌: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
	                    .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
                int friendBoardAttack = board.MinionFriend != null ? board.MinionFriend.Sum(x => x.CurrentAtk) : 0;
                int enemyBoardAttack = board.MinionEnemy != null ? board.MinionEnemy.Sum(x => x.CurrentAtk) : 0;
                AddLog($"我方场攻: {friendBoardAttack} | 敌方场攻: {enemyBoardAttack}");
                if (friendHealth <= 12 && enemyBoardAttack >= Math.Max(1, friendHealth - 2))
                {
                    AddLog("全局：低血高压，先解场保命");
                }
                else if (friendBoardAttack >= enemyHealth && enemyHealth <= 15)
                {
                    AddLog("全局：场攻斩杀窗口，最大化走脸");
                }
                else if (friendBoardAttack >= enemyBoardAttack)
                {
                    AddLog("全局：我方站场领先，偏进攻");
                }
                else
                {
                    AddLog("全局：默认节奏压制");
                }
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
           
// 主逻辑：按职业计算动态激进度（C#6 写法）
switch (board.EnemyClass)
{
    case Card.CClass.PALADIN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 64, board.EnemyClass);
        break;
    case Card.CClass.DEMONHUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 55, board.EnemyClass);
        break;
    case Card.CClass.PRIEST:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 65, board.EnemyClass);
        break;
    case Card.CClass.MAGE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass);
        break;
    case Card.CClass.DEATHKNIGHT:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 50, board.EnemyClass);
        break;
    case Card.CClass.HUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 40, board.EnemyClass);
        break;
    case Card.CClass.ROGUE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 40, board.EnemyClass);
        break;
    case Card.CClass.WARLOCK:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 80, board.EnemyClass);
        break;
    case Card.CClass.SHAMAN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 35, board.EnemyClass);
        break;
    default:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass);
        break;
}
 
        
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
						// 计算场上受伤的随从数量
						int damagedMinionCount = board.MinionFriend.Count(card => card.CurrentHealth < card.MaxHealth);
						AddLog($"场上受伤的随从数量: {damagedMinionCount}");
						// 计算场上龙属性的随从数量
						int dragonMinionCount= board.MinionFriend.Count(card => card.IsRace(Card.CRace.DRAGON));
						AddLog($"场上龙属性的随从数量: {dragonMinionCount}");
                        AddLog($"威胁评估：我方场攻={myAttack} | 敌方场攻={enemyAttack} | 我方场血={myMinionHealth} | 敌方场血={enemyMinionHealth} | 敌方手牌={enemyHandCount}");
                        if ((board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) <= enemyAttack + 2)
                        {
                            AddLog("威胁：对面伤害压力较高，优先处理场面");
                        }
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


#region 硬币 GAME_005
		// 一费时,手上没有两费随从,但是有三费随从,则降低,其他时候随意
		// if(board.MaxMana == 1
		// && board.HasCardInHand(Card.Cards.GAME_005) //硬币
		// &&!board.Hand.Exists(x=>x.CurrentCost==2&&x.Type == Card.CType.MINION)
		// &&board.Hand.Exists(x=>x.CurrentCost==3&&(x.Type == Card.CType.MINION||x.Template.Id == Card.Cards.CORE_GVG_061))
		// ){
		// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));
		// }else{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-55));
		// }

#endregion

// TIME_003 传送门卫士
#region 传送门卫士 TIME_003
			if (board.HasCardInHand(Card.Cards.TIME_003)
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_003, new Modifier(-150));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_003, new Modifier(1999));
					AddLog($"传送门卫士 TIME_003 优先级提升: {-150}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_003, new Modifier(350));
			}
#endregion

// 影焰晕染 FIR_939
// #region 影焰晕染 FIR_939
// 			if (board.HasCardInHand(Card.Cards.FIR_939)
// 			)
// 			{
// 					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.FIR_939, new Modifier(-150));
// 					AddLog($"影焰晕染 FIR_939 优先级提升: {-150}");
// 			}
// 			#endregion

// 纳拉雷克斯，龙群先锋 EDR_844 手里有高费龙 法力值大于等于8
#region 纳拉雷克斯，龙群先锋 EDR_844
			if (board.HasCardInHand(Card.Cards.EDR_844)
			&& board.ManaAvailable >= 8
			&& board.Hand.Exists(card => card.IsRace(Card.CRace.DRAGON) && card.CurrentCost >= 7)
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_844, new Modifier(-250));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_844, new Modifier(1999));
					AddLog($"纳拉雷克斯，龙群先锋 EDR_844 优先级提升: {-250}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_844, new Modifier(350));
			}
#endregion

// 提高法力值消耗为1的高费巨龙的使用优先级 
/*
火光之龙菲莱克 FIR_959 10费
伟岸的德拉克雷斯 DINO_401 9费
无限巨龙姆诺兹多 TIME_024 9费
乘风浮龙 TLC_600 8费
时光领主埃博克 TIME_714 6费
受难的恐翼巨龙 EDR_572 5费

现场播报员 TIME_034 4费
鲜花商贩 EDR_889 2费
永恒雏龙 TIME_045 1费
---
*/ 

#region 高费巨龙被减到1费时优先出（按从上到下顺序依次递减）
            var highCostDragonsOrdered = new List<Card.Cards>
            {
                Card.Cards.FIR_959,  // 火光之龙菲莱克（最高优先）
                Card.Cards.DINO_401, // 伟岸的德拉克雷斯
                Card.Cards.TIME_024, // 无限巨龙姆诺兹多
                Card.Cards.TLC_600,  // 乘风浮龙
                Card.Cards.TIME_714, // 时光领主埃博克
                Card.Cards.EDR_572,  // 受难的恐翼巨龙（相对最低）
            };

            for (int i = 0; i < highCostDragonsOrdered.Count; i++)
            {
                var dragonId = highCostDragonsOrdered[i];
                var card = board.Hand.FirstOrDefault(c => c.Template.Id == dragonId);
                if (card == null)
                    continue;
                if (card.Type != Card.CType.MINION)
                    continue;
                if (card.CurrentCost != 1)
                    continue;

                // 说明：Modifier 越小（更负）越愿意打出；PlayOrder 越大越靠前出
                int castModifier = -420 + i * 25;      // 从上到下逐步降低（变得没那么负）
                int playOrder = 4800 - i * 120;        // 从上到下逐步降低（但仍然很靠前）

                p.CastMinionsModifiers.AddOrUpdate(dragonId, new Modifier(castModifier));
                p.PlayOrderModifiers.AddOrUpdate(dragonId, new Modifier(playOrder));
                AddLog($"高费巨龙被减到1费，按顺序优先: {card.Template.NameCN} {dragonId} cast={castModifier} order={playOrder}");
            }
#endregion


// 龙巢守护者 EDR_457 标记态优先使用
#region 龙巢守护者 EDR_457
			if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.EDR_457)
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_457, new Modifier(-150));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_457, new Modifier(1999));
					AddLog($"龙巢守护者 EDR_457 优先级提升: {-150}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_457, new Modifier(350));
			}
#endregion


// 鲜花商贩 EDR_889 场上龙数量大于等于1,开始使用,数量越多优先级越高,反之则不用
#region 鲜花商贩 EDR_889
			if (board.HasCardInHand(Card.Cards.EDR_889)
			&& dragonMinionCount >= 1
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_889, new Modifier(-150 - dragonMinionCount * 20));
					AddLog($"鲜花商贩 EDR_889 优先级提升: {-150 - dragonMinionCount * 20}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_889, new Modifier(350));
			}
#endregion

#region Card.Cards.HERO_05bp 英雄技能
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_05bp, new Modifier(-500));
				// 
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_05bp, new Modifier(100));
				// 打印出英雄技能board.EnemyAbility.Template.Id
				AddLog($"英雄技能: {board.Ability.Template.Id} ");
#endregion

// TOY_386 礼盒雏龙 如果处在标记态,优先使用,否则降低使用优先级
#region 礼盒雏龙 TOY_386
			if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TOY_386)
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_386, new Modifier(-150));
					AddLog($"礼盒雏龙 TOY_386 优先级提升: {-150}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_386, new Modifier(350));
			}
#endregion
// 香蕉 EX1_014t
#region 香蕉 EX1_014t
			if (board.HasCardInHand(Card.Cards.EX1_014t)
			)
			{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EX1_014t, new Modifier(-150));
					AddLog($"香蕉 EX1_014t 优先级提升: {-150}");
			}
			#endregion

// TIME_750 先行打击 如果处在标记态,优先使用,否则降低使用优先级
#region 先行打击 TIME_750
			if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TIME_750)
			)
			{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_750, new Modifier(-20));
					AddLog($"先行打击 TIME_750 优先级提升: {-20}");
			}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_750, new Modifier(150));
			}
#endregion

// TLC_623 石雕工匠 场上受伤随从大于等于1,开始使用,数量越多优先级越高,反之则不用
#region 石雕工匠 TLC_623
			if (board.HasCardInHand(Card.Cards.TLC_623)
			&& damagedMinionCount >= 1
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_623, new Modifier(-150 - damagedMinionCount * 20));
					AddLog($"石雕工匠 TLC_623 优先级提升: {-150 - damagedMinionCount * 20}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_623, new Modifier(350));
			}
#endregion
// 质量保证 TOY_605 手牌数小于等于8,优先使用
#region 质量保证 TOY_605
			if (board.HasCardInHand(Card.Cards.TOY_605)
			&& board.Hand.Count <= 8
			)
			{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_605, new Modifier(-150));
					AddLog($"质量保证 TOY_605 优先级提升: {-150}");
			}else{
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_605, new Modifier(350));
			}
#endregion

// 先行打击 TIME_750 如果亮黄光,优先使用,否则降低使用优先级
// #region 先行打击 TIME_750
// 			if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TIME_750)
// 			)
// 			{
// 					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_750, new Modifier(-350));
// 					AddLog($"先行打击 TIME_750 优先级提升: {-350}");
// 			}else{
// 				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_750, new Modifier(350));
// 			}
// #endregion

// 黑暗的龙骑士 EDR_456 如果亮黄光,优先使用,否则降低使用优先级
#region 黑暗的龙骑士 EDR_456
			if (board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.EDR_456)
			)
			{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_456, new Modifier(-150));
					AddLog($"黑暗的龙骑士 EDR_456 优先级提升: {-150}");
			}else{
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_456, new Modifier(350));
			}
#endregion

#region 重新思考
var excludedCards = new HashSet<string> { Card.Cards.GAME_005.ToString() };
if (board.Hand != null)
{
    for (int i = 0; i < board.Hand.Count; i++)
    {
        var card = board.Hand[i];
        if (card == null || card.Template == null)
            continue;
        if (excludedCards.Contains(card.Template.Id.ToString()))
            continue;
        p.ForcedResimulationCardList.Add(card.Template.Id);
    }
}
AddLog("重算：已登记手牌重算列表(排除幸运币) 数量=" + p.ForcedResimulationCardList.Count);
#endregion

#region 打印场上随从id
// foreach (var item in board.MinionFriend)
// {
//     AddLog(item.Template.NameCN + ' ' + item.Template.Id);
// }
// 打印手上的卡牌id
foreach (var item in board.Hand)
{
		if (item == null || item.Template == null)
            continue;
        AddLog((string.IsNullOrWhiteSpace(item.Template.NameCN) ? item.Template.Name : item.Template.NameCN) + " " + item.Template.Id);
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


#region 减费出高费的逻辑
// 如果手上存在0费的牌,降低一费随从的使用优先级 啮齿绿鳍鱼人 EDR_999  进化融合怪 VAC_958 鱼人木乃伊 CORE_ULD_723 鱼人招潮者 CORE_EX1_509
if(
	board.Hand.Any(card => card.CurrentCost == 0 && card.IsRace(Card.CRace.MURLOC))
){
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(999));
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(999));
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(999));
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(999));
	AddLog("手上有0费的鱼人牌，降低一费随从的使用优先级");
}else{
	 if(board.HasCardInHand(Card.Cards.EDR_999)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(-150));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(1999));
			AddLog("啮齿绿鳍鱼人 -150");
    }
		if(board.HasCardInHand(Card.Cards.VAC_958)
					){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(-150));
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(999));
					AddLog("进化融合怪-150");
					}
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
        	        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(999));       
									 if(board.HasCardInHand(Card.Cards.CORE_EX1_509)
		// 友方随从小于等于5
		&&board.MinionFriend.Count <= 5
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(4999));
    }
}
#endregion


#region 版本输出
                AddLog("---\n标准龙战 版本:" + ProfileVersion + " 作者by77 Q群:943879501\n---");
#endregion

#region 攻击优先 卡牌威胁
var cardModifiers = new Dictionary<Card.Cards, int>
{   
			{ Card.Cards.TLC_468t1,	200 }, // 细长黏团 TLC_468t1
			{ Card.Cards.DINO_410,	999 }, // 凯洛斯的蛋 DINO_410
		{ Card.Cards.DINO_410t2,	999 }, // 凯洛斯的蛋 DINO_410t2
		{ Card.Cards.DINO_410t4,	999 }, // 凯洛斯的蛋 DINO_410t4
		{ Card.Cards.DINO_410t5,	999 }, // 凯洛斯的蛋 DINO_410t5
		{ Card.Cards.DINO_410t3,	999 }, // 凯洛斯的蛋 DINO_410t3
		{ Card.Cards.SC_671t1,	200 }, // 执政官 SC_671t1
		{ Card.Cards.EDR_849,	200 }, // 梦缚迅猛龙 EDR_849
		{ Card.Cards.EDR_892, -100 }, // 残暴的魔蝠 EDR_892
		{ Card.Cards.EDR_891, -100 }, // 贪婪的地狱猎犬 EDR_891
		{ Card.Cards.GDB_471, 200 }, // GDB_471	沃罗尼招募官
		{ Card.Cards.VAC_501, 200 }, // 极限追逐者阿兰娜 VAC_501 
		{ Card.Cards.GDB_100, 200 }, // 阿肯尼特防护水晶 GDB_100
    { Card.Cards.EDR_815, 200},//EDR_815	尸魔花
    { Card.Cards.TOY_528, 200},//伴唱机 TOY_528
    { Card.Cards.EDR_540, 200},//EDR_540	扭曲的织网蛛
    { Card.Cards.EDR_889, 200},//EDR_889	鲜花商贩 
    { Card.Cards.VAC_503, 200},//VAC_503	召唤师达克玛洛
    { Card.Cards.EDR_816, 200},//EDR_816	怪异魔蚊
    { Card.Cards.EDR_810t, 100},//EDR_810t	饱胀水蛭
    { Card.Cards.SC_765, 200},//SC_765	高阶圣堂武士
    { Card.Cards.SC_756, 200},//SC_756	航母
    { Card.Cards.SC_003, 200},//虫巢女王 SC_003
    { Card.Cards.WW_827, 200},//雏龙牧人 WW_827
    { Card.Cards.TTN_903, 200},//生命的缚誓者艾欧娜尔 TTN_903
    { Card.Cards.TTN_960, 200},//灭世泰坦萨格拉斯 TTN_960
    { Card.Cards.TTN_862, 200},//翠绿之星阿古斯 TTN_862
    { Card.Cards.TTN_429, 200},//阿曼苏尔 TTN_429
     { Card.Cards.TTN_092, 200},//复仇者阿格拉玛 TTN_092
     { Card.Cards.TTN_075, 200},//诺甘农 TTN_075  
    
     { Card.Cards.DEEP_008, 200},//针岩图腾 DEEP_008
     { Card.Cards.CORE_RLK_121, 200},//死亡侍僧 CORE_RLK_121
     { Card.Cards.TTN_737, 200},//兵主 TTN_737
     { Card.Cards.VAC_406, 900},//VAC_406	困倦的岛民
     { Card.Cards.TTN_858, 200},//TTN_858	维和者阿米图斯
     { Card.Cards.GDB_226, 200},//GDB_226	凶恶的入侵者
     { Card.Cards.TOY_330t5, 200},//TOY_330t5 奇利亚斯豪华版3000型
    { Card.Cards.TOY_330t11, 200},//TOY_330t11 奇利亚斯豪华版3000型
     { Card.Cards.CORE_EX1_012, 200},//CORE_EX1_012 血法师萨尔诺斯
     { Card.Cards.CORE_BT_187, 200},//CORE_BT_187	凯恩·日怒
     { Card.Cards.CS2_052, 200},//空气之怒图腾 CS2_052
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
    { Card.Cards.TOY_646, 200 }, // 捣蛋林精 TOY_646
    { Card.Cards.TOY_357, 200 }, // 抱龙王噗鲁什 TOY_357
    { Card.Cards.VAC_507, 200 }, // 阳光汲取者莱妮莎 VAC_507
    { Card.Cards.WORK_042, 500 }, // 食肉格块 WORK_042
    { Card.Cards.WW_344, 200 }, // 威猛银翼巨龙 
    { Card.Cards.TOY_812, -5},//TOY_812 皮普希·彩蹄
    { Card.Cards.VAC_532, 200 },//椰子火炮手 VAC_532
    { Card.Cards.TOY_505, 200 },//TOY_505	玩具船
    { Card.Cards.TOY_381, 200 },//TOY_381	纸艺天使
    { Card.Cards.TOY_824, 350 }, // 黑棘针线师
    { Card.Cards.VAC_927, 200 }, // 狂飙邪魔
    { Card.Cards.VAC_938, 200 }, // 粗暴的猢狲
    { Card.Cards.ETC_355, 200 }, // 剃刀沼泽摇滚明星
    { Card.Cards.WW_091, 200 },  // 腐臭淤泥波普加
    { Card.Cards.VAC_450, 200}, // 悠闲的曲奇
    { Card.Cards.TOY_028, 200 }, // 团队之灵
    { Card.Cards.VAC_436, 200 }, // 脆骨海盗
    { Card.Cards.VAC_321, 200 }, // 伊辛迪奥斯
    { Card.Cards.TTN_800, 200 }, // 雷霆之神高戈奈斯 TTN_800
    { Card.Cards.TTN_415, 200 }, // 卡兹格罗斯
    { Card.Cards.ETC_541, 200 }, // 盗版之王托尼
    { Card.Cards.CORE_LOOT_231, 200 }, // 奥术工匠
    { Card.Cards.ETC_339, 200 }, // 心动歌手
    { Card.Cards.ETC_833, 200 }, // 箭矢工匠
    { Card.Cards.MIS_026, 200 }, // 傀儡大师多里安
    { Card.Cards.CORE_WON_065, 200 }, // 随船外科医师
    { Card.Cards.WW_357, 500 }, // 老腐和老墓
    { Card.Cards.DEEP_999t2, 200 }, // 深岩之洲晶簇
    { Card.Cards.CFM_039, 200 }, // 杂耍小鬼
    { Card.Cards.WW_364t, 200 }, // 狡诈巨龙威拉罗克
    { Card.Cards.TSC_026t, 200 }, // 可拉克的壳
    { Card.Cards.WW_415, 200 }, // 许愿井
    { Card.Cards.CS3_014, 200 }, // 赤红教士
    { Card.Cards.YOG_516, 200 }, // 脱困古神尤格-萨隆
    { Card.Cards.NX2_033, 200 }, // 巨怪塔迪乌斯
    { Card.Cards.JAM_004, 200 }, // 镂骨恶犬
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
    { Card.Cards.TTN_856, 200 }, // Disciple of Amitus
    { Card.Cards.TTN_907, 200 }, // Astral Serpent
    { Card.Cards.TTN_071, 200 }, // Sif
    { Card.Cards.TTN_078, 200 }, // Observer of Myths
    { Card.Cards.TTN_843, 200 }, // Eredar Deceptor
};

foreach (var cardModifier in cardModifiers)
{
    if (board.MinionEnemy.Any(minion => minion.Template.Id == cardModifier.Key))
    {
        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(cardModifier.Key, new Modifier(cardModifier.Value));
    }
}
#endregion

            // 龙战套牌专项：快攻走脸、关键牌时序、重复牌最右优先
            ApplyDragonDeckTuning(board, p, enemyHealth, friendHealth, myAttack, enemyAttack);
						 
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
        }

        private void ApplyDragonDeckTuning(Board board, ProfileParameters p, int enemyHealth, int friendHealth, int myAttack, int enemyAttack)
        {
            if (board == null || p == null || board.Hand == null)
                return;

            int manaNow = Math.Max(0, board.ManaAvailable);
            if (board.HasCardInHand(TheCoin))
                manaNow = Math.Min(10, manaNow + 1);

            var enemyMinions = board.MinionEnemy ?? new List<Card>();
            bool enemyHasTaunt = enemyMinions.Any(m => m != null && m.IsTaunt);
            bool lethalNow = !enemyHasTaunt && myAttack >= enemyHealth;
            bool defendNow = friendHealth <= 12 || enemyAttack >= friendHealth - 1;
            bool raceWindow = !defendNow && (enemyHealth <= 18 || myAttack >= enemyHealth - 10);
            int freeSlots = Math.Max(0, 7 - (board.MinionFriend != null ? board.MinionFriend.Count : 0));
            bool hasPlayableTempo = board.Hand.Any(c =>
                c != null &&
                c.Template != null &&
                c.CurrentCost <= manaNow &&
                c.Template.Id != TheCoin &&
                (c.Type != Card.CType.MINION || freeSlots > 0));

            if (hasPlayableTempo && board.Ability != null && board.Ability.Template != null)
            {
                p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(380));
                AddLog("龙战：有可用节奏动作，后置英雄技能");
            }

            if (lethalNow)
            {
                p.GlobalAggroModifier = 520;
                AddLog("龙战：场攻斩杀窗口，强制走脸");
            }
            else if (defendNow)
            {
                p.GlobalAggroModifier = 55;
                AddLog("龙战：低血压力，优先解场保命");
            }
            else if (raceWindow)
            {
                p.GlobalAggroModifier = 230;
                AddLog("龙战：快攻抢血窗口，优先走脸");
            }
            else
            {
                p.GlobalAggroModifier = 175;
                AddLog("龙战：快攻模式，能走脸优先走脸");
            }

            if (enemyMinions.Count > 0)
            {
                foreach (var enemy in enemyMinions.Where(m => m != null && m.Template != null))
                {
                    if (enemy.IsTaunt)
                    {
                        int tauntPri = defendNow ? 620 : (enemy.CurrentHealth >= 4 ? 420 : 320);
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(tauntPri));
                        continue;
                    }

                    if (defendNow)
                    {
                        int pri = enemy.CurrentAtk >= 4 ? 340 : 190;
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(pri));
                    }
                    else if (raceWindow)
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-220));
                    }
                    else
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-80));
                    }
                }
            }

            // 核心前中期节奏牌
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_386, new Modifier(-320)); // 礼盒雏龙
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_386, new Modifier(9000));
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.REV_990, new Modifier(-220));  // 赤红深渊
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.REV_990, new Modifier(7600));
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_456, new Modifier(-300)); // 黑暗的龙骑士
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_456, new Modifier(9100));
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_457, new Modifier(-260)); // 龙巢守护者
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_457, new Modifier(8600));
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_889, new Modifier(-250)); // 鲜花商贩
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_889, new Modifier(8200));
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_623, new Modifier(-240)); // 石雕工匠
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_623, new Modifier(7600));
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_003, new Modifier(-220)); // 传送门卫士
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_003, new Modifier(8200));
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.END_021, new Modifier(-180)); // 次元武器匠
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_021, new Modifier(7600));
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_045, new Modifier(-190)); // 永恒雏龙
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_045, new Modifier(7900));

            bool enemyHasBoard = enemyMinions.Any(m => m != null);
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.FIR_939, enemyHasBoard ? new Modifier(-150) : new Modifier(150)); // 影焰晕染
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.FIR_939, enemyHasBoard ? new Modifier(7600) : new Modifier(-2000));

            bool hasBigMinionInHand = board.Hand.Any(c => c != null && c.Template != null && c.Type == Card.CType.MINION && c.CurrentCost >= 5);
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_750, hasBigMinionInHand ? new Modifier(-150) : new Modifier(150)); // 先行打击
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_750, hasBigMinionInHand ? new Modifier(7400) : new Modifier(-2400));

            if (manaNow >= 4)
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_034, new Modifier(-99)); // 现场播报员
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_034, new Modifier(5200));
            }
            else
            {
                p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_034, new Modifier(150));
                p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_034, new Modifier(-3200));
            }

            // 高费龙在减费后优先铺开
            foreach (var c in board.Hand.Where(c => c != null && c.Template != null))
            {
                bool keyDragon = c.Template.Id == Card.Cards.END_033 || c.Template.Id == Card.Cards.TLC_600 || c.Template.Id == Card.Cards.DINO_401;
                if (!keyDragon)
                    continue;
                if (c.CurrentCost <= 5)
                {
                    p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(-350));
                    p.PlayOrderModifiers.AddOrUpdate(c.Template.Id, new Modifier(8400));
                    AddLog("龙战：减费高费龙已可用，提升出牌优先级");
                }
                else if (c.Template.Id == Card.Cards.END_033)
                {
                    p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, new Modifier(220));
                    p.PlayOrderModifiers.AddOrUpdate(c.Template.Id, new Modifier(-1600));
                }
            }

            // 格罗玛什：无斩杀机会时后置，避免裸拍亏节奏
            if (board.HasCardInHand(Card.Cards.CORE_EX1_414))
            {
                if (enemyHasTaunt || myAttack < enemyHealth - 4)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_414, new Modifier(350));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_414, new Modifier(-7000));
                    AddLog("格罗玛什：未到终结窗口，先后置");
                }
                else
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_414, new Modifier(-220));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_414, new Modifier(7600));
                }
            }

            // 你要求：后置奇利亚斯的使用顺序（有其他节奏动作时不先拍）
            var zilliax = board.Hand.FirstOrDefault(c => c != null && c.Template != null &&
                (c.Template.Id == Card.Cards.TOY_330t5 || c.Template.Id == Card.Cards.TOY_330t11 || c.Template.Id == Card.Cards.TOY_330));
            if (zilliax != null)
            {
                bool hasOtherPlayableTempo = board.Hand.Any(c => c != null && c.Template != null
                    && c.Id != zilliax.Id
                    && c.CurrentCost <= manaNow
                    && (c.Type == Card.CType.MINION || c.Type == Card.CType.SPELL)
                    && !(c.Template.Id == TheCoin));

                if (!defendNow && zilliax.CurrentCost >= 4 && hasOtherPlayableTempo)
                {
                    p.CastMinionsModifiers.AddOrUpdate(zilliax.Template.Id, new Modifier(150));
                    p.PlayOrderModifiers.AddOrUpdate(zilliax.Template.Id, new Modifier(-9200));
                    AddLog("奇利亚斯：后置到其余节奏动作之后");
                }
                else if (defendNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(zilliax.Template.Id, new Modifier(-150));
                    p.PlayOrderModifiers.AddOrUpdate(zilliax.Template.Id, new Modifier(8600));
                }
                else
                {
                    p.CastMinionsModifiers.AddOrUpdate(zilliax.Template.Id, new Modifier(-120));
                    p.PlayOrderModifiers.AddOrUpdate(zilliax.Template.Id, new Modifier(2200));
                }
            }

            // 红牌：优先锁嘲讽，给随从让路走脸
            if (board.HasCardInHand(Card.Cards.TOY_644) && enemyHasTaunt)
            {
                var tauntTarget = enemyMinions
                    .Where(m => m != null && m.Template != null && m.IsTaunt)
                    .OrderByDescending(m => m.CurrentHealth)
                    .FirstOrDefault();
                if (tauntTarget != null)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(-150));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(-350, tauntTarget.Id));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_644, new Modifier(7600));
                    AddLog("红牌：优先锁定敌方嘲讽后转走脸");
                }
            }

            // 你要求：额外恶魔为0时不打基尔加丹
            if (board.HasCardInHand(Card.Cards.GDB_145))
            {
                int extraDemons = board.Hand.Count(c => c != null && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.IsRace(Card.CRace.DEMON)
                    && c.Template.Id != Card.Cards.GDB_145);

                if (extraDemons == 0)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_145, new Modifier(350));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_145, new Modifier(-9800));
                    AddLog("基尔加丹：无额外恶魔联动，后置不打");
                }
            }

            // 你要求：相同牌优先最右边可用那张
            PreferRightmostPlayableDuplicateByEntity(board, p, manaNow, freeSlots);

            // 你要求：续连熵能可用费用达到2就可打（若是生成进手）
            var entropy = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == Card.Cards.TIME_026);
            if (entropy != null && manaNow >= 2 && entropy.CurrentCost <= manaNow)
            {
                int friendlyCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                if (friendlyCount >= 2)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_026, new Modifier(-350));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_026, new Modifier(7800));
                    AddLog("续连熵能：满足可用费用>=2，且友方随从>=2，优先使用");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_026, new Modifier(-150));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_026, new Modifier(2600));
                    AddLog("续连熵能：满足可用费用>=2，前置启用");
                }
            }

            // 非套牌补资源牌：抢血窗口后置，防守窗口允许
            if (board.HasCardInHand(Card.Cards.GDB_123))
            {
                if (raceWindow && !enemyHasTaunt)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_123, new Modifier(150));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_123, new Modifier(-3600));
                    AddLog("挟持射线：抢血窗口后置，优先走脸");
                }
                else if (defendNow || enemyHasTaunt)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_123, new Modifier(-120));
                    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_123, new Modifier(3600));
                    AddLog("挟持射线：防守/过墙窗口，允许前置");
                }
            }
        }

        private void PreferRightmostPlayableDuplicateByEntity(Board board, ProfileParameters p, int manaNow, int freeSlots)
        {
            if (board == null || p == null || board.Hand == null)
                return;

            var duplicateGroups = board.Hand
                .Where(c => c != null && c.Template != null)
                .GroupBy(c => c.Template.Id)
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var group in duplicateGroups)
            {
                Card rightmostPlayable = null;
                for (int i = board.Hand.Count - 1; i >= 0; i--)
                {
                    var c = board.Hand[i];
                    if (c == null || c.Template == null || c.Template.Id != group.Key)
                        continue;
                    if (c.CurrentCost > manaNow)
                        continue;
                    if (c.Type == Card.CType.MINION && freeSlots <= 0)
                        continue;

                    rightmostPlayable = c;
                    break;
                }

                if (rightmostPlayable == null)
                    continue;

                bool comboSet = SetSingleCardComboByEntityId(
                    board,
                    p,
                    rightmostPlayable.Id,
                    allowCoinBridge: true,
                    forceOverride: true,
                    logWhenSet: "同名牌策略：ComboSet锁定最右可用副本");
                if (!comboSet)
                    continue;

                if (rightmostPlayable.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(group.Key, new Modifier(-150));
                else if (rightmostPlayable.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(group.Key, new Modifier(-150));
                else if (rightmostPlayable.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(group.Key, new Modifier(-150));

                p.PlayOrderModifiers.AddOrUpdate(group.Key, new Modifier(350));
                AddLog("同名牌策略：优先最右可用 " + group.Key);
            }
        }

        private static bool HasExistingCombo(ProfileParameters p)
        {
            if (p == null || p.ComboModifier == null)
                return false;
            try { return !p.ComboModifier.IsEmpty(); }
            catch { return true; }
        }

        private static int GetFreeBoardSlots(Board board)
        {
            int friendCount = 0;
            try { friendCount = board != null && board.MinionFriend != null ? board.MinionFriend.Count : 0; }
            catch { friendCount = 0; }
            return Math.Max(0, 7 - friendCount);
        }

        private static int GetAvailableManaIncludingCoin(Board board)
        {
            int mana = 0;
            try { mana = Math.Max(0, board != null ? board.ManaAvailable : 0); }
            catch { mana = 0; }
            try
            {
                if (board != null && board.HasCardInHand(TheCoin))
                    mana = Math.Min(10, mana + 1);
            }
            catch { }
            return mana;
        }

        private bool SetSingleCardComboByEntityId(Board board, ProfileParameters p, int targetEntityId, bool allowCoinBridge, bool forceOverride, string logWhenSet)
        {
            if (board == null || p == null || board.Hand == null)
                return false;
            if (!forceOverride && HasExistingCombo(p))
                return false;

            var target = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Id == targetEntityId);
            if (target == null)
                return false;

            if (target.Type == Card.CType.MINION && GetFreeBoardSlots(board) <= 0)
                return false;

            int realMana = Math.Max(0, board.ManaAvailable);
            int manaNow = GetAvailableManaIncludingCoin(board);
            if (target.CurrentCost > manaNow)
                return false;

            var combo = new List<int>();
            if (target.CurrentCost > realMana)
            {
                if (!allowCoinBridge)
                    return false;

                var coin = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == TheCoin);
                if (coin == null || target.CurrentCost > realMana + 1)
                    return false;

                combo.Add(coin.Id);
            }

            combo.Add(target.Id);
            p.ComboModifier = new ComboSet(combo.ToArray());

            if (!string.IsNullOrWhiteSpace(logWhenSet))
                AddLog(logWhenSet);

            return true;
        }

        private int CalculateAggroModifier(double baseAggro, int baseValue, Card.CClass enemyClass)
        {
            double winRateModifier = GetWinRateModifier(enemyClass);
            double usageRateModifier = GetUsageRateModifier(enemyClass);
            int finalAggro = (int)(baseAggro * 0.625 + baseValue + winRateModifier + usageRateModifier);
            AddLog(string.Format("职业:{0} | 激进度:{1} | 胜率修正:{2:F1} | 使用率修正:{3:F1}",
                enemyClass, finalAggro, winRateModifier, usageRateModifier));
            return finalAggro;
        }

        private double GetWinRateModifier(Card.CClass enemyClass)
        {
            double winRate = GetWinRateFromData(enemyClass);
            return (winRate - 50) * 1.5;
        }

        private double GetUsageRateModifier(Card.CClass enemyClass)
        {
            double usageRate = GetUsageRateFromData(enemyClass);
            return usageRate * 0.5;
        }

        private double GetWinRateFromData(Card.CClass enemyClass)
        {
            switch (enemyClass)
            {
                case Card.CClass.PALADIN: return 52.3;
                case Card.CClass.DEMONHUNTER: return 54.5;
                case Card.CClass.PRIEST: return 50.8;
                case Card.CClass.MAGE: return 51.2;
                case Card.CClass.DEATHKNIGHT: return 53.1;
                case Card.CClass.HUNTER: return 55.2;
                case Card.CClass.ROGUE: return 56.1;
                case Card.CClass.WARLOCK: return 49.7;
                case Card.CClass.SHAMAN: return 48.5;
                default: return 50.0;
            }
        }

        private double GetUsageRateFromData(Card.CClass enemyClass)
        {
            switch (enemyClass)
            {
                case Card.CClass.PALADIN: return 12.0;
                case Card.CClass.DEMONHUNTER: return 15.0;
                case Card.CClass.PRIEST: return 8.0;
                case Card.CClass.MAGE: return 10.0;
                case Card.CClass.DEATHKNIGHT: return 11.0;
                case Card.CClass.HUNTER: return 14.0;
                case Card.CClass.ROGUE: return 13.0;
                case Card.CClass.WARLOCK: return 7.0;
                case Card.CClass.SHAMAN: return 6.0;
                default: return 10.0;
            }
        }

				 // 向 _log 字符串添加日志的私有方法，包括回车和新行
        private void AddLog(string log)
        {
            _log += "\r\n" + log;
        }
				public class SilverHandProtector
				{
						private readonly Board _board;
						private readonly List<Card.Cards> _silverHandCards;

						public SilverHandProtector(Board board)
					
						{
								_board = board;
								_silverHandCards = new List<Card.Cards>
								{
										Card.Cards.CS2_101t,
										Card.Cards.CS2_101t2,
										Card.Cards.CS2_101t3,
										Card.Cards.CS2_101t4,
										Card.Cards.CS2_101t5,
										Card.Cards.CS2_101t6,
										Card.Cards.CS2_101t7,
										Card.Cards.CS2_101t8
								};
						}

						public void ProtectMinions(ProfileParameters p)
						{
								foreach (var card in _silverHandCards)
								{
										if (_board.HasCardOnBoard(card))
										{
												p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(card, new Modifier(250));
										}
								}
						}

						public bool ShouldProtect()
						{
								// 如果我方随从数量大于敌方随从数量，且手上有十字军光环或者光速抢购
								return (_board.HasCardInHand(Card.Cards.TTN_908) || _board.HasCardInHand(Card.Cards.TOY_716)) &&
											_silverHandCards.Any(card => _board.HasCardOnBoard(card));
						}
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
public int CountSpecificRacesInHand(Board board)
{
    if (board?.MinionFriend == null) return 0; // 检查手牌是否为 null

    // 定义所有可能的种族
    Card.CRace[] races = new Card.CRace[]
    {
        Card.CRace.BLOODELF,
        Card.CRace.DRAENEI,
        Card.CRace.DWARF,
        Card.CRace.GNOME,
        Card.CRace.GOBLIN,
        Card.CRace.HUMAN,
        Card.CRace.NIGHTELF,
        Card.CRace.ORC,
        Card.CRace.TAUREN,
        Card.CRace.TROLL,
        Card.CRace.UNDEAD,
        Card.CRace.WORGEN,
        Card.CRace.GOBLIN2,
        Card.CRace.MURLOC,
        Card.CRace.DEMON,
        Card.CRace.SCOURGE,
        Card.CRace.MECHANICAL,
        Card.CRace.ELEMENTAL,
        Card.CRace.OGRE,
        Card.CRace.PET,
        Card.CRace.TOTEM,
        Card.CRace.NERUBIAN,
        Card.CRace.PIRATE,
        Card.CRace.DRAGON,
        Card.CRace.BLANK,
        Card.CRace.ALL,
        Card.CRace.EGG,
        Card.CRace.QUILBOAR,
        Card.CRace.CENTAUR,
        Card.CRace.FURBOLG,
        Card.CRace.HIGHELF,
        Card.CRace.TREANT,
        Card.CRace.OWLKIN,
        Card.CRace.HALFORC,
        Card.CRace.LOCK,
        Card.CRace.NAGA,
        Card.CRace.OLDGOD,
        Card.CRace.PANDAREN,
        Card.CRace.GRONN,
        Card.CRace.CELESTIAL,
        Card.CRace.GNOLL,
        Card.CRace.GOLEM,
        Card.CRace.HARPY,
        Card.CRace.VULPERA
    };

    HashSet<Card.CRace> uniqueRaces = new HashSet<Card.CRace>();

    foreach (Card card in board.MinionFriend)
    {
        if (card?.Type != Card.CType.MINION) continue; // 忽略空卡或非随从卡

        foreach (Card.CRace race in races)
        {
            if (card.IsRace(race))
            {
                uniqueRaces.Add(race);
                break; // 确保每张卡只添加一个种族
            }
        }
    }

    return uniqueRaces.Count;
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
                    else if (board.Secret.Count == 0)
                    {
                        ret.Add(card1.Template.Id);
                    }
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
                    //以友方随从攻击力 降序排序 的 场上的所有友方随从集合，如果该集合存在生命值大于与敌方随从攻击力
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
