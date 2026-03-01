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
 * CastMinionsModifiers --->你可以为每个小兵设置单独的修饰符，修饰符越高，AI玩仆从的可能性就越小
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
      private const string ProfileVersion = "2026-01-03.1";
      public ProfileParameters GetParameters(Board board)
      {
            _log = "";
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 }; 

            try
            {
                int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                AddLog($"================ 标准骑士 决策日志 v{ProfileVersion} ================");
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
           
// 主邏輯：根據職業計算動態攻擊值
switch (board.EnemyClass)
{
    case Card.CClass.PALADIN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 4, board.EnemyClass);
        break;

    case Card.CClass.DEMONHUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 5, board.EnemyClass);
        break;

    case Card.CClass.PRIEST:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 5, board.EnemyClass);
        break;

    case Card.CClass.MAGE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass);
        break;

    case Card.CClass.DEATHKNIGHT:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass);
        break;

    case Card.CClass.HUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 0, board.EnemyClass);
        break;

    case Card.CClass.ROGUE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 0, board.EnemyClass);
        break;

    case Card.CClass.WARLOCK:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass);
        break;

    case Card.CClass.SHAMAN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 60, board.EnemyClass);
        break;

    default:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 0, board.EnemyClass); // 預設職業
        break;
}

// 核心方法：計算動態攻擊值
int CalculateAggroModifier(double baseAggro, int baseValue, Card.CClass enemyClass)
{
    double winRateModifier = GetWinRateModifier(enemyClass);
    double usageRateModifier = GetUsageRateModifier(enemyClass);

    // 最終計算攻擊值
    int finalAggro = (int)(baseAggro * 0.625 + baseValue + winRateModifier + usageRateModifier);
    AddLog($"職業: {enemyClass}, 攻擊值: {finalAggro}, 勝率修正: {winRateModifier}, 使用率修正: {usageRateModifier}");
    return finalAggro;
}

// 方法: 計算勝率修正值
double GetWinRateModifier(Card.CClass enemyClass)
{
    double winRate = GetWinRateFromData(enemyClass);
    return (winRate - 50) * 1.5; // 每超出50%勝率增加1.5點攻擊值
}

// 方法: 計算使用率修正值
double GetUsageRateModifier(Card.CClass enemyClass)
{
    double usageRate = GetUsageRateFromData(enemyClass);
    return usageRate * 0.5; // 每1%使用率增加0.5點攻擊值
}

// 模擬職業勝率數據
double GetWinRateFromData(Card.CClass enemyClass)
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
        default: return 50.0; // 默認勝率
    }
}

// 模擬職業使用率數據
double GetUsageRateFromData(Card.CClass enemyClass)
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
        default: return 10.0; // 默認使用率
    }
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
             // 通电机器人 BOT_907
            int aomiCount = board.Secret.Count;
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
            var TheVirtuesofthePainter = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TOY_810);
             // 定义坟场中一费随从的数量 TUTR_HERO_11bpt
            int oneCostMinionCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.WW_331||CardTemplate.LoadFromId(card).Id==Card.Cards.CORE_ICC_038||CardTemplate.LoadFromId(card).Id==Card.Cards.CORE_ULD_723)
            // 加手上的这些牌
            +board.Hand.Count(card => card.Template.Id==Card.Cards.WW_331||card.Template.Id==Card.Cards.CORE_ICC_038||card.Template.Id==Card.Cards.CORE_ULD_723)
            // 加场上的这些牌
            +board.MinionFriend.Count(card => card.Template.Id==Card.Cards.WW_331||card.Template.Id==Card.Cards.CORE_ICC_038||card.Template.Id==Card.Cards.CORE_ULD_723);
            ;
            // AddLog("一费随从数量"+oneCostMinionCount);
            // 场上实际能存活随从数 -脆弱的食尸鬼的数量 TUTR_HERO_11bpt/HERO_11bpt
            int liveMinionCount = board.MinionFriend.Count-board.MinionFriend.Count(x => x.Template.Id == Card.Cards.HERO_11bpt);
            int canAttackMinion=board.MinionFriend.Count(card => card.CanAttack);
            // 定义敌方三血及以下随从的数量
            int enemyThreeHealthMinionCount = board.MinionEnemy.Count(card => card.CurrentHealth<=3);
							// 定义场上所有随从数量
						int allMinionCount = CountSpecificRacesInHand(board);
						// 在 GetParameters 方法中添加
						int holySpellsInHand = board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_251) // 龙鳞军备 EDR_251 神圣 // 神圣佳酿 VAC_916 神圣
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916) // 神圣佳酿 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t2) // 神圣佳酿 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t3) // 神圣佳酿 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_917t); // 防晒霜 VAC_917t; // 神圣佳酿 VAC_916 神圣
						// AddLog($"手上神圣法术数量: {holySpellsInHand}");
						// 低费法术可以对目标使用的
						int lowCostSpells = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t2)  // 圣光护盾
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916)  // 神圣佳酿 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t3) // 神圣佳酿 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_922) // 救生光环 VAC_922 
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_251); // 龙鳞军备 EDR_251; // 神圣佳酿 VAC_916 神圣
						// AddLog($"手上低费法术数量: {lowCostSpells}");
						// 判断手上是否有大于0费的海上船歌 VAC_558和大于0费的灯火机器人 MIS_918 MIS_918t
						int seaShantyCount = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_558 && card.CurrentCost > 0)+
						board.Hand.Count(card => card.Template.Id == Card.Cards.MIS_918t && card.CurrentCost > 0)+
						board.Hand.Count(card => card.Template.Id == Card.Cards.MIS_918 && card.CurrentCost > 0);
						// AddLog($"手上大于0费的海上船歌和灯火机器人数量: {seaShantyCount}");
						// 判断手里是否有灌注牌，苦花骑士 EDR_852  金萼幼龙 EDR_451  振翅守卫 EDR_800 圣光护盾 EDR_264
						int infusedCardsCount = board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_852) // 苦花骑士 EDR_852
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_451) // 金萼幼龙 EDR_451
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_800) // 振翅守卫 EDR_800
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_264); // 圣光护盾 EDR_264
						  // 可攻击随从
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
			// 定义场上鱼人数量 MURLOC
						int murlocCount = board.MinionFriend.Count(card => card.IsRace(Card.CRace.MURLOC))+board.MinionFriend.Count(card => card.IsRace(Card.CRace.ALL));
						// 手牌鱼人数量
						int murlocInHandCount = board.Hand.Count(card => card.IsRace(Card.CRace.MURLOC))+board.Hand.Count(card => card.IsRace(Card.CRace.ALL));
						
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

// 强光护卫 TIME_015 随从 如果己方英雄血量大于等于30 降低强光护卫使用优先级
#region 强光护卫 TIME_015
				if(board.HasCardInHand(Card.Cards.TIME_015)
				&& board.HeroFriend.CurrentHealth>=30
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_015, new Modifier(250)); //强光护卫 TIME_015
					AddLog("强光护卫 +250");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_015, new Modifier(-150)); //强光护卫 TIME_015
				}
#endregion

// 坦克机械师 TIME_017 随从
#region 坦克机械师 TIME_017
				if(board.HasCardInHand(Card.Cards.TIME_017)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_017, new Modifier(-250)); //坦克机械师 TIME_017
					AddLog("坦克机械师 -250");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_017, new Modifier(250)); //坦克机械师 TIME_017
				}
#endregion

// 纸板魔像 TOY_809 随从`
#region 纸板魔像 TOY_809
				if(board.HasCardInHand(Card.Cards.TOY_809)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_809, new Modifier(-350)); //纸板魔像 TOY_809
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_809, new Modifier(3509)); //纸板魔像 TOY_809
					AddLog("纸板魔像 -350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_809, new Modifier(250)); //纸板魔像 TOY_809
				}	
#endregion

/*
法术 时序光环 TIME_700 使用优先级提高 场上友方随从数小于等于6
*/ 
#region 法术 时序光环 TIME_700
				if(board.HasCardInHand(Card.Cards.TIME_700)
				&& board.MinionFriend.Count <=6
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_700, new Modifier(-250)); //时序光环 TIME_700
					AddLog("时序光环 -250");
				}
#endregion

/*
提高地标 安戈洛丛林 TLC_100t1 使用优先级
*/ 
#region 地标 安戈洛丛林 TLC_100t1
				if(board.HasCardInHand(Card.Cards.TLC_100t1)){
					p.LocationsModifiers.AddOrUpdate(Card.Cards.TLC_100t1, new Modifier(-99));
					AddLog("地标 安戈洛丛林 -150");
				}
#endregion

// 梅卡托克的光环 TIME_009t2 侏儒光环 TIME_009t1 当手上有这两张光环 且有明日巨匠格尔宾 TIME_009 时 优先先交易 光环
#region 光环 梅卡托克的光环 TIME_009t2 侏儒光环 TIME_009t1
				if(((board.HasCardInHand(Card.Cards.TIME_009t2)
				|| board.HasCardInHand(Card.Cards.TIME_009t1)))
				&& board.HasCardInHand(Card.Cards.TIME_009)
				){
					p.TradeModifiers.AddOrUpdate(Card.Cards.TIME_009t2, new Modifier(-150)); //明日巨匠格尔宾 TIME_009
					p.TradeModifiers.AddOrUpdate(Card.Cards.TIME_009t1, new Modifier(-150)); //明日巨匠格尔宾 TIME_009
					AddLog("交易光环 -150");
				}
#endregion
/*
提高明日巨匠格尔宾 TIME_009 使用优先级  
*/ 
#region 明日巨匠格尔宾 TIME_009
				if(board.HasCardInHand(Card.Cards.TIME_009)
				&&!((board.HasCardInHand(Card.Cards.TIME_009t2)
				|| board.HasCardInHand(Card.Cards.TIME_009t1)))
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_009, new Modifier(-350)); //明日巨匠格尔宾 TIME_009
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_009, new Modifier(999)); //明日巨匠格尔宾 TIME_009
					AddLog("明日巨匠格尔宾 -350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_009, new Modifier(250)); //明日巨匠格尔宾 TIME_009
				}
#endregion

// 食肉格块 WORK_042
/*
优先对以下随从或者费用大于等于4的随从使用排除食肉模块自身
亡语＞嘲讽＞圣盾
 如果场上没有以下随从,降低对其使用的优先级
坦克机械师 TIME_017
奇利亚斯豪华版 3000 型 TOY_330t5
*/ 
#region 食肉格块 WORK_042
if (board.HasCardInHand(Card.Cards.WORK_042))
{
    bool hasTarget = false;
    foreach (var minion in board.MinionFriend)
    {
        // 排除食肉格块自身
        if (minion.Template.Id == Card.Cards.WORK_042) continue;

        if (minion.Template.Id == Card.Cards.TIME_017 || // 坦克机械师
            minion.Template.Id == Card.Cards.TOY_330t5 || // 奇利亚斯
            minion.CurrentCost >= 4) // 费用大于等于4
        {
            int modifierValue = -200;
            
            // 优先级：亡语 > 嘲讽 > 圣盾
            if (minion.Template.HasDeathrattle)
            {
                modifierValue -= 150; // 亡语优先级最高
            }
            else if (minion.IsTaunt)
            {
                modifierValue -= 100; // 嘲讽次之
            }
            else if (minion.IsDivineShield)
            {
                modifierValue -= 50; // 圣盾再次
            }

            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_042, new Modifier(modifierValue, minion.Template.Id));
            hasTarget = true;
            AddLog($"食肉格块吃 {minion.Template.NameCN} 修正值:{modifierValue}");
        }
    }

    if (!hasTarget)
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_042, new Modifier(350));
        AddLog("食肉格块没有目标，降低优先级");
    }
}
#endregion 

#region 超时空鳍侠 TIME_706
        if(board.HasCardInHand(Card.Cards.TIME_706)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_706, new Modifier(-99)); //超时空鳍侠 TIME_706
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_706, new Modifier(-55)); //超时空鳍侠 TIME_706
					AddLog("超时空鳍侠 -99");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_706, new Modifier(250)); //超时空鳍侠 TIME_706
				}
#endregion

#region 火鳃鱼人 DINO_404
        if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.DINO_404)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DINO_404, new Modifier(-99)); //火鳃鱼人 DINO_404
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DINO_404, new Modifier(-55)); //火鳃鱼人 DINO_404
					AddLog("火鳃鱼人 -99");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DINO_404, new Modifier(250)); //火鳃鱼人 DINO_404
				}
#endregion

#region 恐惧畏缩 TLC_823
        if(board.HasCardInHand(Card.Cards.TLC_823)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_823, new Modifier(-99)); //拼布好朋友 TOY_353
					AddLog("恐惧畏缩 -99");
				}
#endregion

#region 拼布好朋友 TOY_353
        if(board.HasCardInHand(Card.Cards.TOY_353)
				// 手牌数小于等于9
				&& board.Hand.Count <= 9
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_353, new Modifier(-99)); //拼布好朋友 TOY_353
					AddLog("拼布好朋友 -99");
				}
#endregion

#region 劳作老马 WORK_018
        if(board.HasCardInHand(Card.Cards.WORK_018)){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_018, new Modifier(-150)); //劳作老马 WORK_018
					AddLog("劳作老马 -150");
				}
#endregion

#region 泼漆彩鳍鱼人 TOY_517
        if(board.HasCardInHand(Card.Cards.TOY_517)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_517, new Modifier(999)); //泼漆彩鳍鱼人 TOY_517
				}
#endregion

#region 火羽精灵 CORE_UNG_809
        if(board.HasCardInHand(Card.Cards.CORE_UNG_809)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_UNG_809, new Modifier(999)); //火羽精灵 CORE_UNG_809
					// AddLog("火羽精灵 -150");
				}
#endregion

#region 火羽精灵衍生物 UNG_809t1
        if(board.HasCardInHand(Card.Cards.UNG_809t1)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(999)); //火羽精灵衍生物 UNG_809t1
					// AddLog("火羽精灵 -150");
				}
#endregion

#region 鱼人木乃伊 CORE_ULD_723 
        if(board.HasCardInHand(Card.Cards.CORE_ULD_723)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(999)); //鱼人木乃伊 CORE_ULD_723 
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(150)); //鱼人木乃伊 CORE_ULD_723 
					// AddLog("鱼人木乃伊 -150");
				}
#endregion

#region Card.Cards.HERO_04bp 英雄技能
        // p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(100));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(-500));
				// 
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.EDR_445p, new Modifier(55)); //EDR_445p	巨龙的祝福
        // p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_445p, new Modifier(1999));
				p.ForcedResimulationCardList.Add(Card.Cards.EDR_445p);
				// 打印出英雄技能board.EnemyAbility.Template.Id
				AddLog($"英雄技能: {board.Ability.Template.Id} ");
				// 如果手牌里有小于等于2费的鱼人牌,不使用英雄技能
				if (board.Hand.Any(card => card.CurrentCost <= 2 && card.IsRace(Card.CRace.MURLOC)&&card.Template.Id != Card.Cards.GDB_878))//脑鳃鱼人 GDB_878
				{
					p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(999)); // 不使用英雄技能
					AddLog("手牌里有小于等于2费的鱼人牌，不使用英雄技能");
				}

				// 如果手上有食肉格块 WORK_042 且场上随从数大于等于5，不使用英雄技能防止卡格子
				if (board.HasCardInHand(Card.Cards.WORK_042) && board.MinionFriend.Count >= 5)
				{
					p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(999)); // 不使用英雄技能
					AddLog("手上有食肉格块且场上随从数>=5，不使用英雄技能防止卡格子");
				}
#endregion

#region 椰子火炮手 VAC_532
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(-50));
				if(board.HasCardOnBoard(Card.Cards.VAC_532)){
				// 不送椰子火炮手
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(150));
				}
				if(board.HasCardInHand(Card.Cards.VAC_532)
				// 友方随从大于等于2
				&&board.MinionFriend.Count >= 2
				){
				// 不送椰子火炮手
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(-150));
				AddLog("椰子火炮手 -150");
				}
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(999));
#endregion

#region 栉龙 TLC_603
				if(
					board.HasCardInHand(Card.Cards.TLC_603)
				)
				{
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(999));
				}
				// 场上有栉龙 TLC_603
				if(board.HasCardOnBoard(Card.Cards.TLC_603)){
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(150));
					AddLog("栉龙 TLC_603 150");
				}
#endregion

#region 啮齿绿鳍鱼人 EDR_999
				if (
					board.HasCardInHand(Card.Cards.EDR_999)
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(-150));
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(999));
						AddLog("啮齿绿鳍鱼人 -150");
				}
#endregion

#region TLC_110 城市首脑埃舒
				if (
					board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_110)
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_110, new Modifier(-999));
						AddLog("城市首脑埃舒 -999");
				}
#endregion

#region SC_013 玛润
        // 不用马润会卡死
				if (board.HasCardInHand(Card.Cards.SC_013)
				// 随从小于等于3
				&& board.MinionFriend.Count <= 3
				// 敌方随从为0
				&& board.MinionEnemy.Count == 0
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_013, new Modifier(-350));
						AddLog("玛润 SC_013 -350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_013, new Modifier(999));
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.SC_013, new Modifier(-350));
#endregion

#region 可怕的主厨 VAC_946
		// 友方随从书小于等于5
		if (board.MinionFriend.Count <= 5)
		{
				// 如果手上有可怕的主厨 VAC_946,则使用优先级+100
				if (board.HasCardInHand(Card.Cards.VAC_946))
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_946, new Modifier(-350));
						AddLog("可怕的主厨"+(-350));
				}
		}
		// 如果场上有可怕的主厨,主动把他送了
		// if (board.HasCardOnBoard(Card.Cards.VAC_946))
		// {
    //     p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_946, new Modifier(-5)); 
		// 		AddLog("可怕的主厨送");
		// }
#endregion


#region 沉睡的林精 EDR_469
				// 如果手上有沉睡的林精 EDR_469,提高使用优先级
				if (board.HasCardInHand(Card.Cards.EDR_469))
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_469, new Modifier(-150));
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_469, new Modifier(999));
						AddLog("沉睡的林精 EDR_469 -150");
				}
#endregion

#region 鱼人招潮者 CORE_EX1_509
// 降低其攻击顺序`
				if (board.HasCardOnBoard(Card.Cards.CORE_EX1_509))
				{
					p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(-50));
				}
				if (board.HasCardInHand(Card.Cards.EDR_469))
				{
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(3999));
				}
#endregion

 #region ULD_177	八爪巨怪
            if(board.HasCardOnBoard(Card.Cards.ULD_177)
            &&HandCount<=2
            ){
          	p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_177, new Modifier(-100));
            AddLog("八爪巨怪送");
            }
            if(board.HasCardInHand(Card.Cards.ULD_177)
            &&HandCount<=3
            ){
          	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_177, new Modifier(-150));
            AddLog("八爪巨怪出");
            }
 #endregion

#region 幽光鱼竿 CORE_BT_018

    if(board.HasCardInHand(Card.Cards.CORE_BT_018)
		// 手上有武器,且为CORE_BT_018,降低使用优先级
		&&board.WeaponFriend != null 
		&& board.WeaponFriend.Template.Id == Card.Cards.CORE_BT_018
    ){
			p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.CORE_BT_018, new Modifier(350));
			AddLog("已有鱼杆,降低使用优先级");
    }else{
			p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.CORE_BT_018, new Modifier(150));
			// AddLog("没有鱼杆,130");
		}
#endregion

#region 石头 WW_001t

    // if(board.HasCardInHand(Card.Cards.WW_001t)
    // ){
		// 		// 遍历己方场上随从,永远不对其使用石头
		// foreach(var minion in board.MinionFriend)
		// {
		// 		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_001t, new Modifier(9999, minion.Template.Id));
		// 		// AddLog($"石头 {minion.Template.Id} 999");
		// }
		// // 不对己方英雄使用
		// p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_001t, new Modifier(9999, board.HeroFriend.Template.Id));
    // }
	
#endregion

#region 临时牌逻辑
if (board != null && board.Hand != null)
{
    var markedIds = new HashSet<Card.Cards>();
    foreach (var c in board.Hand.ToArray())
    {
        if (c != null && c.Template != null && c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT))
        {
            markedIds.Add(c.Template.Id);
        }
    }

    foreach (var c in board.Hand.ToArray())
    {
        if (c != null && c.Template != null)
        {
            if (markedIds.Contains(c.Template.Id) && c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT))
            {
                p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, -999);
                p.PlayOrderModifiers.AddOrUpdate(c.Template.Id, 999);
                AddLog($"为临时卡 ,提高使用优先级{c.Template.NameCN}-999");
            }
            else if (markedIds.Contains(c.Template.Id) && !c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT) && c.Type == Card.CType.SPELL)
            {
                p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, 150);
                AddLog($"卡片匹配但不是临时卡,降低使用优先级{c.Template.NameCN}150");
            }
        }
    }
}
#endregion

#region  抛石鱼人 TLC_427

    if(board.HasCardInHand(Card.Cards.TLC_427)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_427, new Modifier(-150));
			AddLog("抛石鱼人 -150");
    }
#endregion

#region 飞火流星·芬杰 CORE_CFM_344

    if(board.HasCardInHand(Card.Cards.CORE_CFM_344)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CFM_344, new Modifier(-150));
			AddLog("飞火流星·芬杰 -150");
    }
#endregion

#region 鱼人领军 CORE_EX1_507

    if(board.HasCardInHand(Card.Cards.CORE_EX1_507)
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_507, new Modifier(999));
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_507, new Modifier(200));
    }
#endregion

#region 奖品商贩 CORE_DMF_067

    if(board.HasCardInHand(Card.Cards.CORE_DMF_067)
		// 手牌数小于等于8
		&&board.Hand.Count <= 8
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_DMF_067, new Modifier(999));
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_DMF_067, new Modifier(-150));
			AddLog("奖品商贩 -150");
    }
#endregion

#region 湾鳍健身鱼人 VAC_531

    if(board.HasCardInHand(Card.Cards.VAC_531)
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_531, new Modifier(999));
    }
#endregion

#region CORE_EX1_103 寒光先知

    if(board.HasCardInHand(Card.Cards.CORE_EX1_103)
		// 场上鱼人数量小于等于2
		&& murlocCount <= 2	
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_103, new Modifier(350));
    }
#endregion


#region 鱼人猎潮者 CORE_EX1_506

    if(board.HasCardInHand(Card.Cards.CORE_EX1_506)
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_506, new Modifier(1999));
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_506, new Modifier(-99));
			AddLog("鱼人猎潮者 -99");
    }
#endregion

#region 淹没的地图 TLC_442

    if(board.HasCardInHand(Card.Cards.TLC_442)
		// 大于3费
		&&board.ManaAvailable >= 3
    ){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_442, new Modifier(-99));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_442, new Modifier(3999));
			AddLog("淹没的地图 -99");
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

#region 填鳃暴龙 TLC_240

    if(board.HasCardInHand(Card.Cards.TLC_240)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(-350));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(1999));
			AddLog("填鳃暴龙 -350");
    }
		// 如果场上随从数量小于等于4,提高送掉的优先值
		if(board.MinionFriend.Count <= 4
		&&board.HasCardOnBoard(Card.Cards.TLC_240)
		)
		{
      p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(-100));
			// 提高攻击优先级
			p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(999));

		}
#endregion



#region 紫色珍鳃鱼人 TLC_438

    if(board.HasCardInHand(Card.Cards.TLC_438)
		// 大于等于4费
		&&board.ManaAvailable >= 4
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_438, new Modifier(-150));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_438, new Modifier(5999));
			AddLog("紫色珍鳃鱼人 -150");
    }else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_438, new Modifier(250));
		}
#endregion

#region 温泉踏浪鱼人 TLC_428

    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_428)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_428, new Modifier(-350));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_428, new Modifier(1299));
			AddLog("温泉踏浪鱼人 -350");
    }else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_428, new Modifier(130));
		}
#endregion

#region 蒸鳍偷蛋贼 TLC_429
  var SteamedFinsEggThief = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TLC_429);
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_429)
		// 随从数小于等于4
		&&board.MinionFriend.Count <=4
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_429, new Modifier(-150));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_429, new Modifier(999));
			AddLog("蒸鳍偷蛋贼 -150");
    }else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_429, new Modifier(350));
		}
#endregion

#region 蛮鱼挑战者 TLC_251
  var BarbarianFishChallenger = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TLC_251);
// 如果有蛮鱼挑战者 TLC_251,费用大于等于7,场上随从小于等于2打combo
		// if(board.HasCardInHand(Card.Cards.TLC_251)
		// // 费用大于等于7
		// &&board.MaxMana >= 7
		// // 场上随从小于等于2
		// &&board.MinionFriend.Count <=2
		// &&BarbarianFishChallenger!=null
		// &&SteamedFinsEggThief!=null
		// ){
		// 	if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_429)){
    //     p.ComboModifier = new ComboSet(BarbarianFishChallenger.Id, SteamedFinsEggThief.Id);
		// 		AddLog("蛮鱼挑战者+蒸鳍偷蛋贼combo");
		// 	}
		// }else{
		// 	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_251, new Modifier(-150));
		// }
		if(board.HasCardInHand(Card.Cards.TLC_251)
		){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_251, new Modifier(-150));
			AddLog("蛮鱼挑战者 -150");
		}
#endregion

#region 潜入葛拉卡 TLC_426
    if(board.HasCardInHand(Card.Cards.TLC_426)
    ){
        if(board.MaxMana == 1){
            // 仅第一回合使用
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(-350));
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(9999));
            AddLog("潜入葛拉卡(首回合) -350");
        }else{
            // 非第一回合禁止使用，提高权重避免出牌
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(350));
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(19999));
            AddLog("潜入葛拉卡(非首回合不使用) +350");
        }
    }
#endregion

#region 火光之龙菲莱克 FIR_959
  // 友方随从小于等于5,提高使用优先级
	if(board.HasCardInHand(Card.Cards.FIR_959)
	// 手牌数小于等于7
	&&HandCount<=8
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.FIR_959, new Modifier(-999));
		AddLog("火光之龙菲莱克-999");
	}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.FIR_959, new Modifier(999));
#endregion

#region CORE_REV_308	迷宫向导
  // 友方随从小于等于5,提高使用优先级
	if(board.HasCardInHand(Card.Cards.CORE_REV_308)
	&&board.MinionFriend.Count<=5
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_REV_308, new Modifier(-150));
		AddLog("迷宫向导-150");
	}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_REV_308, new Modifier(999));
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


#region 进化融合怪 VAC_958
//当我方场上有进化融合怪，提高buff牌对进化融合怪 的使用优先级
if(board.HasCardOnBoard(Card.Cards.VAC_958))
{
    // 定义需要调整优先级的buff牌
    var buffCards = new List<Card.Cards>
    {
        Card.Cards.CORE_BT_292, // 阿达尔之手
        Card.Cards.BT_025,      // 智慧圣契
        Card.Cards.GDB_138,     // 神性圣契
        Card.Cards.VAC_917t,    // 防晒霜
        Card.Cards.VAC_916,     // 神圣佳酿1
        Card.Cards.VAC_916t2,   // 神圣佳酿2
        Card.Cards.VAC_916t3    // 神圣佳酿3
    };

    foreach (var buffCard in buffCards)
    {
        // 检查手牌中是否有该buff牌（神性圣契/防晒霜/神圣佳酿系列要求费用≤2）
        bool hasBuff = false;
        if (buffCard == Card.Cards.GDB_138 || buffCard == Card.Cards.VAC_917t || buffCard == Card.Cards.VAC_916 || buffCard == Card.Cards.VAC_916t2 || buffCard == Card.Cards.VAC_916t3)
        {
            hasBuff = board.Hand.Exists(x => x.Template.Id == buffCard && x.CurrentCost <= 2);
        }
        else
        {
            hasBuff = board.HasCardInHand(buffCard);
        }

        if (hasBuff)
        {
            foreach (var minion in board.MinionFriend)
            {
                if (minion.Template.Id == Card.Cards.VAC_958)
                {
                    // 对融合怪提高优先级
                    p.CastSpellsModifiers.AddOrUpdate(buffCard, new Modifier(-150, Card.Cards.VAC_958));
                    AddLog($"{CardTemplate.LoadFromId(buffCard).NameCN}+进化融合怪");
                }
                else
                {
                    // 对其他随从降低优先级
                    p.CastSpellsModifiers.AddOrUpdate(buffCard, new Modifier(150, minion.Template.Id));
                    AddLog($"{CardTemplate.LoadFromId(buffCard).NameCN}+非融合怪 降低优先级");
                }
            }
        }
    }
}
if(board.HasCardInHand(Card.Cards.VAC_958)
){
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(-550));
	AddLog("进化融合怪-550");
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
AddLog("明澈圣契-150");
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

// #region 已腐蚀的梦魇 EDR_846t1
// 	// 如果没有可攻击随从,降低使用优先级
// 	if(canAttackMinion==0
// 	&&board.HasCardInHand(Card.Cards.EDR_846t1)){
// 		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_846t1, new Modifier(350));
// 		AddLog("已腐蚀的梦魇 EDR_846t1 350");
// 	}
// #endregion

#region 把经理叫来！ VAC_460
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-50));
#endregion


#region 海上船歌 VAC_558
	// 如果手上有低费神圣法术,降低使用优先级
	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(-150));
	if(board.HasCardInHand(Card.Cards.VAC_558)
	&&holySpellsInHand>0
	// 友方随从大于0
	&&friendCount>0
	){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(350));
		AddLog("海上船歌 VAC_558 350");
	}else if(board.HasCardInHand(Card.Cards.VAC_558)
	// 友方随从大于0
	&&friendCount<=4
	){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(-350));
		AddLog("海上船歌 VAC_558 -350");
	}
	// 如果场上有莱妮莎,有低于2费的船歌 提高船歌使用优先级
		if (board.HasCardOnBoard(Card.Cards.VAC_507)
		&& board.Hand.Exists(x => x.CurrentCost <=2 && x.Template.Id==Card.Cards.VAC_558)
		){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(3999)); 
			AddLog("如果场上有莱妮莎,有低于2费的船歌 提高船歌使用优先级");
		}
#endregion

#region 阳光汲取者莱妮莎 VAC_507
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(3999));
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
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("单游客24血斩杀 1");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 当手上有阳光汲取者莱妮莎 VAC_507,斩杀线为managerCount*4+lightCount*8,需要9费可以启动
		&&board.MaxMana>=9
		&&managerCount*4+lightCount*8>=enemyHealth
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用优先級
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
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
			AddLog("单游客24血斩杀 2");
		}else if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 当前场上有阳光汲取者莱妮莎 VAC_507,ghostCount为1,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.MaxMana>=4
		&&ghostCount==1
		&&managerCount*6+lightCount*10>=enemyHealth
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用優先級
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("游客+单虚灵32血斩杀 1");
		}else if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 当前场上有阳光汲取者莱妮莎 VAC_507,手上有虚灵,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.HasCardInHand(Card.Cards.GDB_310)
		&&managerCount*6+lightCount*10>=enemyHealth
		&&board.MaxMana>=7
		){
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 提高法术牌对敌方英雄的使用优先级
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("游客+单虚灵32血斩杀 2");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 当前手上有阳光汲取者莱妮莎 VAC_507,ghostCount为1,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.MaxMana>=9
		&&ghostCount==1
		&&managerCount*6+lightCount*10>=enemyHealth
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用優先級
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
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
			AddLog("游客+单虚灵32血斩杀 3");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 当前场上有阳光汲取者莱妮莎 VAC_507,手上有虚灵,斩杀线为managerCount*6+lightCount*10,需要4费可以启动
		&&board.HasCardInHand(Card.Cards.GDB_310)
		&&managerCount*6+lightCount*10>=enemyHealth
		&&board.MaxMana>=10
		){
				// 遍历手牌,降低除了VAC_460 MIS_709之外其他牌的使用優先級
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
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
		}else if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.MIS_709)
		// 敌方随从大于0
		&&board.MinionEnemy.Count>0
		){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-99));
		}
		// 如果手上有圣光荧光棒 MIS_709,提高使用优先级
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(9999, board.HeroEnemy.Id));
		// 不对敌方英雄使用 
#endregion


#region 灯火机器人 MIS_918/MIS_918t
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(-500));
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(-500));
		if(board.Hand.Exists(x => x.Template.Id == Card.Cards.MIS_918&&(x.CurrentCost == 0||lowCostSpells==0)))
		{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(-150));
			AddLog("灯火机器人+-150");
		}else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(150));
		}
		if(board.Hand.Exists(x => x.Template.Id == Card.Cards.MIS_918t&&(x.CurrentCost == 0||lowCostSpells==0)))
		{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(-150));
			AddLog("灯火机器人+-150");
		}else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(150));
		}
#endregion


#region 神圣佳酿 VAC_916t2/VAC_916t3/VAC_916
        if((board.HasCardInHand(Card.Cards.VAC_916t2)||board.HasCardInHand(Card.Cards.VAC_916t3)||board.HasCardInHand(Card.Cards.VAC_916))
        &&board.MinionFriend.Count > 0
        ){
        foreach (var card in board.MinionFriend)
            {
                // 如果随从已有圣盾，则不对其使用神圣佳酿
                if (card.IsDivineShield)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(500, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(500, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(500, card.Template.Id));
                    AddLog($"随从 {card.Template.NameCN} 已有圣盾，不使用神圣佳酿");
                    continue;
                }

                // 优先给大于等于4费的随从贴膜
                if (card.CurrentCost >= 4)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(-200, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(-200, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(-200, card.Template.Id));
                    AddLog($"神圣佳酿优先贴高费随从: {card.Template.NameCN}");
                }
                else if ((card.CanAttack||seaShantyCount>0)
								){
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(-5, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(-5, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(-5, card.Template.Id));
										AddLog("神圣佳酿 VAC_916t2/VAC_916t3/VAC_916 -5");
								}
								else{
										p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(130));
										p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(130));
										p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(130));
									}
                if(card.Template.Id == Card.Cards.TOY_330t5)//TOY_330t5 奇利亚斯豪华版3000型
                {
                    // 这里原代码似乎有误，应该是针对神圣佳酿对奇利亚斯的修饰，或者降低奇利亚斯作为目标的优先级
                     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(350, Card.Cards.TOY_330t5));
                     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(350, Card.Cards.TOY_330t5));
                     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(350, Card.Cards.TOY_330t5));
                }
            }
        }
				// 如果手中有海上船歌 VAC_558,自己场上没随从,提高对自己英雄使用优先级
				// if(seaShantyCount>0
				// &&board.HasCardInHand(Card.Cards.VAC_916t2)||board.HasCardInHand(Card.Cards.VAC_916t3)||board.HasCardInHand(Card.Cards.VAC_916)
        // &&board.MinionFriend.Count == 0)
				// {
				// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(-350, board.HeroFriend.Id));
				// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(-350, board.HeroFriend.Id));
				// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(-350, board.HeroFriend.Id));
				// 	AddLog("神圣佳酿 VAC_916t2/VAC_916t3/VAC_916 -350 对自己英雄");
				// }

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

#region 抗性光环 TTN_851
        // 提高使用优先级
				if(
					board.HasCardInHand(Card.Cards.TTN_851) 
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_851, new Modifier(-350)); 
						AddLog("抗性光环  -350");
				}
#endregion

#region 圣光闪现 CORE_TRL_307
        // 提高使用优先级
				if(
					board.HasCardInHand(Card.Cards.CORE_TRL_307) 
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_TRL_307, new Modifier(-150)); 
						AddLog("圣光闪现  -150");
				}
#endregion

#region 阿达尔之手 CORE_BT_292
        	if(
					board.HasCardInHand(Card.Cards.CORE_BT_292) 
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_292, new Modifier(-150)); 
						AddLog("阿达尔之手  -150");
				}
		
#endregion

#region 救生光环 VAC_922
        // 提高使用优先级
				if(
					board.HasCardInHand(Card.Cards.VAC_922) 
					// 手牌数小于等于9
					&&HandCount<=9
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_922, new Modifier(-550)); 
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_922, new Modifier(999)); 
						AddLog("救生光环  -550");
				}
#endregion

#region 圣光护盾 EDR_264
        // 提高使用优先级
				if(
					board.HasCardInHand(Card.Cards.EDR_264) 
					// 随从数小于等于6
					&&board.MinionFriend.Count <= 6
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_264, new Modifier(-99)); 
						AddLog("圣光护盾  -99");
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_264, new Modifier(3999)); 
#endregion

#region 防晒霜 VAC_917t
        // 提高使用优先级
				if(
					board.HasCardInHand(Card.Cards.VAC_917t)
					// 我方随从大于0
					&&board.MinionFriend.Count > 0
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_917t, new Modifier(-550)); 
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_917t, new Modifier(999)); 
						AddLog("防晒霜  -550");
				}
#endregion

#region 龙鳞军备 EDR_251
        // 提高使用优先级
				if(
					board.Hand.Exists(card => GetTag(card,Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.EDR_251)
					// 手牌数小于等于9,随从数小于等于6
					&&HandCount<=9
					&&board.MinionFriend.Count <= 6
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_251, new Modifier(-150)); 
						AddLog("龙鳞军备  -150");
				}else{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_251, new Modifier(350)); 
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_251, new Modifier(888)); 
#endregion

#region 考内留斯·罗姆 CORE_SW_080
				if(board.HasCardInHand(Card.Cards.CORE_SW_080) 
				&&HandCount<=8
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_SW_080, new Modifier(-150)); 
						AddLog("考内留斯·罗姆 CORE_SW_080 -150");
				}
#endregion

#region 装饰美术家 TOY_882
				if(board.HasCardInHand(Card.Cards.TOY_882) 
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_882, new Modifier(-150)); 
						AddLog("装饰美术家 TOY_882 -150");
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_882, new Modifier(999)); 
#endregion

#region 烈焰元素 UNG_809t1
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(999)); 
#endregion

#region 小精灵 CORE_CS2_231
				if(board.HasCardInHand(Card.Cards.CORE_CS2_231) //小精灵 CORE_CS2_231
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CS2_231, new Modifier(-150)); 
						AddLog("小精灵 CORE_CS2_231 -150");
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_CS2_231, new Modifier(999)); 

#endregion

#region 鸭妈妈 EDR_492
        // 随从数小于等于3可以使用鸭妈妈 EDR_492,否则不使用
				if (board.HasCardInHand(Card.Cards.EDR_492) //鸭妈妈 EDR_492
				&& board.MinionFriend.Count <= 3
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_492, new Modifier(-99)); 
						AddLog("鸭妈妈 EDR_492 -99");
				}
				else
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_492, new Modifier(350)); 
				}
#endregion

#region 乌索尔 EDR_259
// 判断是否有莎拉达希尔
int sarahCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_846)
+board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_255);
        var susuoer = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.EDR_259);
		// 不使用乌索尔 EDR_259
  //  当手上有莎拉达希尔 EDR_846时,乌索尔 EDR_259才使用,否则不使用
		if (susuoer!= null
		&& board.HasCardInHand(Card.Cards.EDR_846) //莎拉达希尔 EDR_846
		// 且手上没有大于7费的海上船歌
		&& !board.Hand.Exists(x => x.CurrentCost >= 7 && x.Template.Id==Card.Cards.VAC_558)
		// 手牌数小于等于5
		&& board.Hand.Count <= 7
		&& (board.HasCardInHand(Card.Cards.EDR_846)||board.HasCardInHand(Card.Cards.EDR_255)) //莎拉达希尔 EDR_846/复苏烈焰 EDR_255
		){
			p.ComboModifier = new ComboSet(susuoer.Id);
			AddLog("乌索尔1出");
		}else if (susuoer!= null
		&& board.HasCardInHand(Card.Cards.EDR_255) //复苏烈焰 EDR_255
		){
			p.ComboModifier = new ComboSet(susuoer.Id);
			AddLog("乌索尔2出");
		}else if(sarahCount==0){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_259, new Modifier(9999)); 
		}
		// AddLog("莎拉达希尔 EDR_846 数量"+sarahCount);
#endregion

#region 莎拉达希尔 EDR_846
// 判断是否用过乌索尔
int ursoCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_259)+
+board.MinionFriend.Count(card => card.Template.Id==Card.Cards.EDR_259);
    // 不使用莎拉达希尔 EDR_846
		if (board.HasCardInHand(Card.Cards.EDR_846) //莎拉达希尔 EDR_846
		&&ursoCount==0
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_846, new Modifier(9999)); 
		}else{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_846, new Modifier(-150)); 
		}
#endregion

#region 复苏烈焰 EDR_255
// 坟场复苏烈焰数量
int reviveCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_255);
    // 不使用莎拉达希尔 EDR_846
		if (board.HasCardInHand(Card.Cards.EDR_255) //莎拉达希尔 EDR_255
		&&ursoCount==0
		&&reviveCount>=1
		&&board.HasCardInHand(Card.Cards.EDR_259)
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_255, new Modifier(9999)); 
		}
#endregion

#region 巨熊之槌 EDR_253
      //  当手上没有武器时,提高巨熊之槌 EDR_253使用优先级
			if(board.HasCardInHand(Card.Cards.EDR_253) //巨熊之槌 EDR_253
			){
				p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(-150)); 
				AddLog("巨熊之槌 -150");
			}
			// 攻击优先级提高
			// 如果手牌数小于等于9
			if ((board.Hand.Count >=9||board.FriendDeckCount<=2
			)
			&&board.WeaponFriend != null
      && board.WeaponFriend.Template.Id == Card.Cards.EDR_253
			){
				p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(999));
				AddLog("巨熊之槌 不攻击");
			}
  		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(3999));
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(3999));
#endregion

#region 脑鳃鱼人 GDB_878
// 场上鱼人大于等于2,提高使用优先级
		if(board.HasCardInHand(Card.Cards.GDB_878)
		&&(murlocCount>=3
		||(murlocInHandCount>=2 && board.Hand.Count <= 3) //手上有2个鱼人,且手牌数小于等于9
		)
		// 场上没有脑鳃鱼人 GDB_878
		&& !board.HasCardOnBoard(Card.Cards.GDB_878)
		// 场上鱼人数量+手牌数量-1<=10
		&& (murlocCount + board.Hand.Count-1 <= 9)
		){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_878, new Modifier(-50*(murlocCount)));
			AddLog("脑鳃鱼人"+(-50*(murlocCount)));
		}else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_878, new Modifier(350));
		}
		
		int deathrattleGDB878Count = 0;
		foreach (var minion in board.MinionFriend)
		{
				// 只要有一个亡语是脑鳃鱼人就计一次
				if (minion.Enchantments.Any(enchant => 
						enchant.DeathrattleCard != null && 
						enchant.DeathrattleCard.Template.Id == Card.Cards.GDB_878))
				{
						deathrattleGDB878Count++;
				}
		}
		// 现在 deathrattleGDB878Count 就是场上拥有脑鳃鱼人亡语的随从数量
		AddLog("场上拥有脑鳃鱼人亡语的随从数量: " + deathrattleGDB878Count);
		// 打印实际过牌数量
		AddLog("实际过牌数量: " +( murlocCount+deathrattleGDB878Count));
#endregion

#region 疯狂生物 EDR_105
      //  手牌数小于等于9,提高疯狂生物 EDR_105使用优先级
				if (board.Hand.Count <= 9
				&& board.HasCardInHand(Card.Cards.EDR_105) //疯狂生物 EDR_105
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_105, new Modifier(-99)); 
						AddLog("疯狂生物 EDR_105 -99");
				}
				else
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_105, new Modifier(350)); 
				}
#endregion

#region 忙碌机器人 WORK_002
// 定义场上友方一攻击力随从的数量	
				int oneAttackMinionCount = board.MinionFriend.Count(x => x.CurrentAtk == 1);
				// AddLog("一攻击力随从数量"+oneAttackMinionCount);
        // 如果一攻击力随从大于等于2,提高忙碌机器人 WORK_002使用优先级
                if (oneAttackMinionCount >= 2
				&& board.HasCardInHand(Card.Cards.WORK_002) //忙碌机器人 WORK_002
				&&board.MinionEnemy.Count==0
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_002, new Modifier(-150* oneAttackMinionCount));
						AddLog("忙碌机器人 WORK_002 -"+(150* oneAttackMinionCount)); 
				}else
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_002, new Modifier(999)); 
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WORK_002, new Modifier(16)); 
#endregion

#region 梦缚迅猛龙 EDR_849
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(4999)); 
			//  大于等于两费,提高使用优先级
			 if ((board.MaxMana >= 2||board.HasCardInHand(Card.Cards.GAME_005))
			//  手里有硬币

				&& board.HasCardInHand(Card.Cards.EDR_849) //梦缚迅猛龙 EDR_849
				&&!board.HasCardOnBoard(Card.Cards.EDR_849)
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(-150)); 
						AddLog("梦缚迅猛龙 EDR_849 -150");
				}
			if(board.HasCardOnBoard(Card.Cards.EDR_849)){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(350)); 
				AddLog("梦缚迅猛龙不送");
				}
				// 手里有迅猛龙,不用凶恶的滑矛纳迦 CORE_TSC_827
				if (board.HasCardInHand(Card.Cards.EDR_849) //梦缚迅猛龙 EDR_849
				&& board.HasCardInHand(Card.Cards.CORE_TSC_827) //凶恶的滑矛纳迦 CORE_TSC_827
				&&(board.MaxMana >= 2||(board.HasCardInHand(Card.Cards.GAME_005)&&board.MaxMana == 1))
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(350)); 
						AddLog("凶恶的滑矛纳迦不使用");
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

#region 2費邏輯
        if(board.MaxMana == 2
				&&board.HasCardInHand(Card.Cards.YOG_525) //有健身肌器人 YOG_525
				&&board.HasCardInHand(Card.Cards.CORE_CFM_753)//有污手街供货商 CORE_CFM_753
						)
						{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CFM_753, new Modifier(999)); 
					p.ForgeModifiers.AddOrUpdate(Card.Cards.YOG_525, new Modifier(-350));
					AddLog("2費優先鑄造健身肌器人");
        }
#endregion
#region 正义保护者 CORE_ICC_038
        // 降低使用优先级
        if(board.HasCardInHand(Card.Cards.CORE_ICC_038)
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
      p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(150));
      AddLog("威猛银翼巨龙 150");
      }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(-99));
      }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(3999));
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(150));
#endregion

#region  WW_391 淘金客
        if(board.HasCardInHand(Card.Cards.WW_391)){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(-99)); 
        AddLog("淘金客-99");
        }
		if(board.HasCardInHand(Card.Cards.WW_391)&&board.Hand.Count <4){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(-200)); 
        AddLog("手牌小於4張淘金客-200");
        }
				// 不送淘金客
				if(board.HasCardOnBoard(Card.Cards.WW_391)){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(200)); 
				AddLog("淘金客不送");
				}
#endregion
#region 音乐治疗师 ETC_325 
    //  如果active,且5費後才用
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.ETC_325)
		&&board.MaxMana>=5
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_325, new Modifier(-150)); 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_325, new Modifier(999)); 
        AddLog("音乐治疗师 -150");
    }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_325, new Modifier(999)); 
    }
#endregion
#region 服装裁缝 ETC_420 
    //  如果buff小於7,则不用
    if(board.Hand.Exists(x=>x.CurrentAtk<7 && x.Template.Id==Card.Cards.ETC_420)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_420, new Modifier(500)); 
        AddLog("服装裁缝 500");
    }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_420, new Modifier(-150)); 
    }
#endregion
#region DEEP_017	采矿事故
       if(board.HasCardInHand(Card.Cards.DEEP_017)
        && board.MinionFriend.Count <=5
       ){
       	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(-550)); 
        AddLog("采矿事故 -550");
      }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(999)); 
      }
       	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(999)); 
#endregion

#region CORE_GVG_061	作战动员
      if(board.HasCardInHand(Card.Cards.CORE_GVG_061)
        // 当前随从数大于等于6
        && board.MinionFriend.Count<=5
      ){
      p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_GVG_061, new Modifier(-350));
      AddLog("作战动员 -350");
      }else{
      p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_GVG_061, new Modifier(350));
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
					// 己方随从小于等于6,主动送音响工程师普兹克
				if(board.HasCardOnBoard(Card.Cards.ETC_425)
				&&board.MinionFriend.Count <= 6
				){
					  p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TTN_907, new Modifier(-20));
					AddLog("音响工程师普兹克送");
				}			
#endregion

#region 光速抢购 TOY_716
        // 我方随从小于3,降低使用优先级
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(15)); 
        if(board.HasCardInHand(Card.Cards.TOY_716)
        &&liveMinionCount >= 3
				&&board.HasCardInHand(Card.Cards.TTN_908)//TTN_908	十字军光环
        ){
					// 如果场上有随从,则降低使用优先级
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(150)); 
					AddLog("光速抢购 150");
				}else if(board.HasCardInHand(Card.Cards.TOY_716)
        &&liveMinionCount >= 3
				&&!board.HasCardInHand(Card.Cards.TTN_908)//TTN_908	十字军光环
        )
				// 如果场上有随从,则降低使用优先级
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
		// 一费时,手上没有两费随从,但是有三费随从,则降低,其他时候随意
		if(board.MaxMana == 1
		&& board.HasCardInHand(Card.Cards.GAME_005) //硬币
		&&!board.Hand.Exists(x=>x.CurrentCost==2&&x.Type == Card.CType.MINION)
		&&board.Hand.Exists(x=>x.CurrentCost==3&&(x.Type == Card.CType.MINION||x.Template.Id == Card.Cards.CORE_GVG_061))
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));
		}else{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-55));
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


#region 展馆茶壶 CORE_WON_142
 // 记录当前随从种类数量
    // AddLog($"当前随从种类数量: {allMinionCount}");
if(board.HasCardInHand(Card.Cards.CORE_WON_142))
{
   
    
    // 根据随从种类调整优先级
    if (allMinionCount >= 2)
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(-350));
        AddLog("展馆茶壶 -350: 随从种类大于3，提高优先级");
    }
    else
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(350));
        AddLog("展馆茶壶 350: 随从种类少于等于3，降低优先级");
    }
}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(16));
#endregion

#region TTN_860	无人机拆解器
			if(board.HasCardInHand(Card.Cards.TTN_860)){
				 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_860, new Modifier(200)); 
				 AddLog("无人机拆解器 200");
			}
#endregion

#region 画师的美德 TOY_810
if (TheVirtuesofthePainter != null && board.WeaponFriend == null && minionNumber >= 2)
{
    // 提高画师的美德的優先級
    p.ComboModifier = new ComboSet(TheVirtuesofthePainter.Id);
    AddLog("画师的美德 - 提高優先級");
}

if (board.WeaponFriend != null && board.WeaponFriend.Template.Id == Card.Cards.TOY_810)
{
    // 如果已裝備画师的美德且手上有乐坛灾星玛加萨
    if (!board.MinionFriend.Any(x => x.Template.Id == Card.Cards.JAM_036) 
        && board.HasCardInHand(Card.Cards.JAM_036))
    {
        // 等待玛加萨出场前不攻击
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.TOY_810, new Modifier(999));
        AddLog("手上有乐坛灾星玛加萨，等待其出场后再攻击");
    }
    else
    {
        // 准备攻击
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.TOY_810, new Modifier(-99));
        AddLog("画师的美德 准备攻击");
    }
}
#endregion

#region VAC_923t 圣沙泽尔
		// 提高场上圣沙泽尔的优先级
         if(board.HasCardOnBoard(Card.Cards.VAC_923t)
        ){
        p.LocationsModifiers.AddOrUpdate(Card.Cards.VAC_923t, new Modifier(-99));
            AddLog("圣沙泽尔 -99");
        }
#endregion

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
		// 如果牌库为空,不交易蛇油
		if(board.HasCardInHand(Card.Cards.WW_331t)
		&&board.FriendDeckCount<=2
		){
		p.TradeModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(999));
		AddLog("牌库为空不交易蛇油");
		}
#endregion
#region WW_336	棱彩光束
        	        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(1999)); 
                    // 敌方小于3血的随从越多,棱彩光束越优先放
                    if(board.HasCardInHand(Card.Cards.WW_336)
                    &&enemyThreeHealthMinionCount>2
                    ){
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(-20*enemyThreeHealthMinionCount));
                    AddLog("棱彩光束"+(-20*enemyThreeHealthMinionCount));
                    }else{
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(350));
										}
#endregion

#region TTN_908	十字军光环
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_908, new Modifier(9999)); 
        var crusaderAura = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TTN_908);
			// 场上随从大于2提高优先级
			 if(crusaderAura!=null
            &&(canAttackMinion>=2
            &&liveMinionCount>=2
            )
            ){
            // p.ComboModifier = new ComboSet(crusaderAura.Id); 
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_908, new Modifier(-350));
							AddLog("十字军光环-350");
            }
#endregion

#region 星舰
       	foreach(var c in board.MinionFriend)
			{
				//check for starship
				if(GetTag(c,Card.GAME_TAG.STARSHIP) == 1 && GetTag(c,Card.GAME_TAG.LAUNCHPAD) == 1)
				{
					int StarshipAtk = c.CurrentAtk;
					int StarshipHp = c.CurrentHealth;
					AddLog("星舰攻击力 : " + StarshipAtk.ToString() + " / 星舰血量 : " + StarshipHp.ToString());
					// 如果星舰的攻击力加防御力小于10,则不发射
					if(StarshipAtk + StarshipHp < 10)
					{
						p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(-100));
						AddLog("星舰不发射"+c.Template.NameCN);
					}
					else
					{
						p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(-50));
						AddLog("星舰发射"+c.Template.NameCN);
					}
				}
				
			}
#endregion

#region WW_051	决战！
			// if(board.HasCardInHand(Card.Cards.WW_051)
      //       //  场上没有奇利亚斯豪华版3000型
      //       &&enemyAttack<4
      //       ){
      //       p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_051, new Modifier(999));
      //       AddLog("决战！ 999");
      //       }
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
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(3999));
            // 不送
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(350));
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
if (board.HasCardInHand(Card.Cards.ETC_418)) // 手上有乐器技师
{
    // 强制乐坛灾星玛加萨为最高优先级
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-9999)); 
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-9999)); 
    AddLog("乐器技师优先级提升至最高 (-9999)");

    // 如果当前手牌少于等于5张
    if (board.Hand.Count <= 5)
    {
        AddLog("条件满足：手牌少于等于5张，乐器技师即将打出");
    }

    // 如果装备了画师的美德，进一步优先考虑
    if (board.HasCardInHand(Card.Cards.TOY_810) || 
        (board.WeaponFriend != null && board.WeaponFriend.Template.Id == Card.Cards.TOY_810))
    {
        AddLog("装备画师的美德，乐器技师优先打出");
    }
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

#region JAM_036 乐坛灾星玛加萨 优化
if (board.HasCardInHand(Card.Cards.JAM_036))
{
    // 强制乐坛灾星玛加萨为最高优先级
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(-9999)); 
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(-9999)); 
    AddLog("乐坛灾星玛加萨优先级提升至最高 (-9999)");

    // 如果当前手牌少于等于5张
    if (board.Hand.Count <= 5)
    {
        AddLog("条件满足：手牌少于等于5张，乐坛灾星玛加萨即将打出");
    }

    // 如果装备了画师的美德，进一步优先考虑
    if (board.HasCardInHand(Card.Cards.TOY_810) || 
        (board.WeaponFriend != null && board.WeaponFriend.Template.Id == Card.Cards.TOY_810))
    {
        AddLog("装备画师的美德，乐坛灾星玛加萨优先打出");
    }
}
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

#region 乐器技师 ETC_418
// 手上有乐器技师 ETC_418,场上有可攻击的随从,提高乐器技师对可攻击随从的优先级
        if(board.HasCardInHand(Card.Cards.ETC_418)
        &&board.MinionFriend.Count(card => card.CanAttack) > 0
        ){
            foreach (var item in board.MinionFriend)
            {
                if (item.CanAttack)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-99, item.Template.Id));
                    AddLog("乐器技师对可攻击随从的优先级"+item.Template.Id);
                }
            }
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
    if(board.MinionEnemy.Count == 0
        &&board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.VAC_330
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.VAC_330, 999, board.HeroEnemy.Id);
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
				// 如果手上有玩具队长塔林姆,遍历敌方场上随从,降低对其使用的优先级
				if(board.HasCardInHand(Card.Cards.TOY_813)
				&&board.MinionEnemy.Count>0
				){
					foreach (var item in board.MinionEnemy)
					{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_813, new Modifier(150, item.Template.Id));
						AddLog("玩具队长塔林姆 150");
					}
				}
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
// 使用对象判断逻辑
var silverHandProtector = new SilverHandProtector(board);
if (silverHandProtector.ShouldProtect())
{
    silverHandProtector.ProtectMinions(p);
		// 遍历场上随从,降低起送死优先级
		foreach (var item in board.MinionFriend)
		{
				p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(250));
				AddLog($"不送 {item.Template.NameCN} 250");
		}
}
#endregion

#region 投降
    // 我方场攻小于5,手牌数小于等于1,牌库为空,敌方血量+护甲>20,当前费用大于10,投降
    if (HandCount<=1
        &&board.FriendDeckCount==0
        &&board.MaxMana>=10
        // 敌方血量大于20 
        &&BoardHelper.GetEnemyHealthAndArmor(board)>=20
        )
    {
        Bot.Concede();
        AddLog("投降");
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

#region 重新思考
// 定义需要排除的卡牌集合

var excludedCards = new HashSet<string>
{
    Card.Cards.GAME_005.ToString(),
		// 蛮鱼挑战者 TLC_251
		Card.Cards.TLC_251.ToString()
};
// 遍历并处理卡牌的方法
void EvaluateCards(IEnumerable<Card> cards)
{
    foreach (var card in cards)
    {
         // 如果卡牌不在排除列表中，则加入重新思考的队列
        if (!excludedCards.Contains(card.Template.Id.ToString()))
        {
            p.ForcedResimulationCardList.Add(card.Template.Id);
        }
    }
}
// 遍历手牌
EvaluateCards(board.Hand);


// 逻辑后续 - 重新思考
// if (p.ForcedResimulationCardList.Any())
// {
    // 如果支持日志系统，使用框架的日志记录方法
    // AddLog("需要重新评估策略的卡牌数: " + p.ForcedResimulationCardList.Count);

    // 如果没有日志系统，可以直接处理策略
    // 替代具体的重新思考逻辑
// }
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

#region 版本输出
                AddLog("---\n版本27.7 作者by77 Q群:943879501 DC:https://discord.gg/nn329ZU6Ss\n---");
#endregion

#region 攻击优先 卡牌威胁
var cardModifiers = new Dictionary<Card.Cards, int>
{   
			{ Card.Cards.TLC_468t1,	999 }, // 细长黏团 TLC_468t1
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


//德：DRUID 猎：HUNTER 法：MAGE 骑：PALADIN 牧：PRIEST 贼：ROGUE 萨：SHAMAN 术：WARLOCK 战：WARRIOR 瞎：DEMONHUNTER 死：DEATHKNIGHT
            return p;
        }}
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