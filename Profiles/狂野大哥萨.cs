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
 * 閰嶇疆鏂囦欢涓畾涔夌殑鎵€鏈夊€奸兘鏄櫨鍒嗘瘮淇グ绗︼紝杩欐剰鍛崇潃瀹冨皢褰卞搷鍩烘湰閰嶇疆鏂囦欢鐨勯粯璁ゅ€笺€?
 * 
 * 淇グ绗﹀€煎彲浠ュ湪[-10000;鑼冨洿鍐呰缃€?10000]锛堣礋淇グ绗︽湁鐩稿弽鐨勬晥鏋滐級
 * 鎮ㄥ彲浠ヤ负闈炲叏灞€淇敼鍣ㄦ寚瀹氱洰鏍囷紝杩欎簺鐩爣鐗瑰畾淇敼鍣ㄥ皢娣诲姞鍒板崱鐨勫叏灞€淇敼鍣?淇敼鍣ㄤ箣涓婏紙鏃犵洰鏍囷級
 * 
 * 搴旂敤鐨勬€讳慨鏀瑰櫒=鍏ㄥ眬淇敼鍣?鏃犵洰鏍囦慨鏀瑰櫒+鐩爣鐗瑰畾淇敼鍣?
 * 
 * GlobalDrawModifier --->淇敼鍣ㄥ簲鐢ㄤ簬鍗＄墖缁樺埗鍊?
 * GlobalWeaponsAttackModifier --->淇敼鍣ㄩ€傜敤浜庢鍣ㄦ敾鍑荤殑浠峰€硷紝瀹冭秺楂橈紝浜哄伐鏅鸿兘鏀诲嚮姝﹀櫒鐨勫彲鑳芥€у氨瓒婂皬
 * 
 * GlobalCastSpellsModifier --->淇敼鍣ㄩ€傜敤浜庢墍鏈夋硶鏈紝鏃犺瀹冧滑鏄粈涔堛€備慨楗扮瓒婇珮锛孉I鐜╂硶鏈殑鍙兘鎬у氨瓒婂皬
 * GlobalCastMinionsModifier --->淇敼鍣ㄩ€傜敤浜庢墍鏈変粏浠庯紝鏃犺瀹冧滑鏄粈涔堛€備慨楗扮瓒婇珮锛孉I鐜╀粏浠庣殑鍙兘鎬у氨瓒婂皬
 * 
 * GlobalAggroModifier --->淇敼鍣ㄩ€傜敤浜庢晫浜虹殑鍋ュ悍鍊硷紝瓒婇珮瓒婂ソ锛屼汉宸ユ櫤鑳藉氨瓒婃縺杩?
 * GlobalDefenseModifier --->淇グ绗﹀簲鐢ㄤ簬鍙嬫柟鐨勫仴搴峰€硷紝瓒婇珮锛宧p淇濆畧鐨勫皢鏄疉I
 * 
 * CastSpellsModifiers --->浣犲彲浠ヤ负姣忎釜娉曟湳璁剧疆涓埆淇グ绗︼紝淇グ绗﹁秺楂橈紝AI鐜╂硶鏈殑鍙兘鎬ц秺灏?
 * CastMinionsModifiers --->浣犲彲浠ヤ负姣忎釜灏忓叺璁剧疆鍗曠嫭鐨勪慨楗扮锛屼慨楗扮瓒婇珮锛孉I鐜╀粏浠庣殑鍙兘鎬ц秺灏?
 * CastHeroPowerModifier --->淇グ绗﹀簲鐢ㄤ簬heropower锛屼慨楗扮瓒婇珮锛孉I鐜╁畠鐨勫彲鑳芥€у氨瓒婂皬
 * 
 * WeaponsAttackModifiers --->閫傜敤浜庢鍣ㄦ敾鍑荤殑淇グ绗︼紝淇グ绗﹁秺楂橈紝AI鏀诲嚮瀹冪殑鍙兘鎬ц秺灏?
 * 
 * OnBoardFriendlyMinionsValuesModifiers --->淇敼鍣ㄩ€傜敤浜庤埞涓婂弸濂界殑濂存墠銆備慨楗拌瓒婇珮锛孉I灏辫秺淇濆畧銆?
 * OnBoardBoardEnemyMinionsModifiers --->淇敼鍣ㄩ€傜敤浜庤埞涓婄殑鏁屼汉銆備慨楗扮瓒婇珮锛孉I灏辫秺浼氬皢鍏惰涓轰紭鍏堢洰鏍囥€?
 *
 */

namespace SmartBotProfiles
{
    [Serializable]
    public class standardeggPaladin  : Profile
    {
        #region 鑻遍泟鎶€鑳?
        //骞歌繍甯?
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        //鎴樺＋
        private const Card.Cards ArmorUp = Card.Cards.HERO_01bp;
        //钀ㄦ弧
        private const Card.Cards TotemicCall = Card.Cards.HERO_02bp;
        //鐩楄醇
        private const Card.Cards DaggerMastery = Card.Cards.HERO_03bp;
        //鍦ｉ獞澹?
        private const Card.Cards Reinforce = Card.Cards.HERO_04bp;
        //鐚庝汉
        private const Card.Cards SteadyShot = Card.Cards.HERO_05bp;
        //寰烽瞾浼?
        private const Card.Cards Shapeshift = Card.Cards.HERO_06bp;
        //鏈＋
        private const Card.Cards LifeTap = Card.Cards.HERO_07bp;
        //娉曞笀
        private const Card.Cards Fireblast = Card.Cards.HERO_08bp;
        //鐗у笀
        private const Card.Cards LesserHeal = Card.Cards.HERO_09bp;
        #endregion

#region 鑻遍泟鑳藉姏浼樺厛绾?
        private readonly Dictionary<Card.Cards, int> _heroPowersPriorityTable = new Dictionary<Card.Cards, int>
        {
            {SteadyShot, 9},//鐚庝汉
            {LifeTap, 8},//鏈＋
            {DaggerMastery, 7},//鐩楄醇
            {Reinforce, 5},//楠戝＋
            {Fireblast, 4},//娉曞笀
            {Shapeshift, 3},//寰烽瞾浼?
            {LesserHeal, 2},//鐗у笀
            {ArmorUp, 1},//鎴樺＋
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

#region 鐩翠激鍗＄墝 鏍囧噯妯″紡
        //鐩翠激娉曟湳鍗＄墝锛堝繀椤绘槸鍙墦鑴哥殑浼ゅ锛?闇€瑕佽绠楁硶寮?
        private static readonly Dictionary<Card.Cards, int> _spellDamagesTable = new Dictionary<Card.Cards, int>
        {
            //钀ㄦ弧
            {Card.Cards.CORE_EX1_238, 3},//闂數绠?Lightning Bolt     CORE_EX1_238
            {Card.Cards.DMF_701, 4},//娣辨按鐐稿脊 Dunk Tank     DMF_701
            {Card.Cards.DMF_701t, 4},//娣辨按鐐稿脊 Dunk Tank     DMF_701t
            {Card.Cards.BT_100, 3},//姣掕泧绁炴浼犻€侀棬 Serpentshrine Portal     BT_100 
            {Card.Cards.TSC_637, 2},//Scalding Geyser TSC_637
            //寰烽瞾浼?

            //鐚庝汉
            {Card.Cards.BAR_801, 1},//鍑讳激鐚庣墿 Wound Prey     BAR_801
            {Card.Cards.CORE_DS1_185, 2},//濂ユ湳灏勫嚮 Arcane Shot     CORE_DS1_185
            {Card.Cards.CORE_BRM_013, 3},//蹇€熷皠鍑?Quick Shot     CORE_BRM_013
            {Card.Cards.BT_205, 3},//搴熼搧灏勫嚮 Scrap Shot     BT_205 
            //娉曞笀
            {Card.Cards.BAR_541, 2},//绗︽枃瀹濈彔 Runed Orb     BAR_541 
            {Card.Cards.CORE_CS2_029, 6},//鐏悆鏈?Fireball     CORE_CS2_029
            {Card.Cards.BT_291, 5},//鍩冨尮甯屾柉鍐插嚮 Apexis Blast     BT_291 
            //楠戝＋
            {Card.Cards.CORE_CS2_093, 2},//濂夌尞 Consecration     CORE_CS2_093 
            //鐗у笀
            //鐩楄醇
            {Card.Cards.BAR_319, 2},//閭伓鎸ュ埡锛堢瓑绾?锛?Wicked Stab (Rank 1)     BAR_319
            {Card.Cards.BAR_319t, 4},//閭伓鎸ュ埡锛堢瓑绾?锛?Wicked Stab (Rank 2)     BAR_319t
            {Card.Cards.BAR_319t2, 6},//閭伓鎸ュ埡锛堢瓑绾?锛?Wicked Stab (Rank 3)     BAR_319t2 
            {Card.Cards.CORE_CS2_075, 3},//褰辫 Sinister Strike     CORE_CS2_075
            {Card.Cards.TSC_086, 4},//鍓戦奔 TSC_086
            //鏈＋
            {Card.Cards.CORE_CS2_062, 3},//鍦扮嫳鐑堢劙 Hellfire     CORE_CS2_062
            //鎴樺＋
            {Card.Cards.DED_006, 6},//閲嶆嫵鍏堢敓 DED_006
            //涓珛
            {Card.Cards.DREAM_02, 5},//浼婄憻鎷夎嫃閱?Ysera Awakens     DREAM_02
            {Card.Cards.TOY_508, 2},//绔嬩綋涔? TOY_508
        };
        //鐩翠激闅忎粠鍗＄墝锛堝繀椤诲彲浠ユ墦鑴革級
        private static readonly Dictionary<Card.Cards, int> _MinionDamagesTable = new Dictionary<Card.Cards, int>
        {
            //鐩楄醇
            {Card.Cards.BAR_316, 2},//娌圭敯浼忓嚮鑰?Oil Rig Ambusher     BAR_316 
            //钀ㄦ弧
            {Card.Cards.CORE_CS2_042, 4},//鐏厓绱?Fire Elemental     CORE_CS2_042 
            //寰烽瞾浼?
            //鏈＋
            {Card.Cards.CORE_CS2_064, 1},//鎭愭儳鍦扮嫳鐏?Dread Infernal     CORE_CS2_064 
            //涓珛
            {Card.Cards.CORE_CS2_189, 1},//绮剧伒寮撶鎵?Elven Archer     CORE_CS2_189
            {Card.Cards.CS3_031, 8},//鐢熷懡鐨勭細瑾撹€呴樋鑾卞厠涓濆钀?Alexstrasza the Life-Binder     CS3_031 
            {Card.Cards.DMF_174t, 4},//椹垙鍥㈠尰甯?Circus Medic     DMF_174t
            {Card.Cards.DMF_066, 2},//灏忓垁鍟嗚穿 Knife Vendor     DMF_066 
            {Card.Cards.SCH_199t2, 2},//杞牎鐢?Transfer Student     SCH_199t2 
            {Card.Cards.SCH_273, 1},//鑾辨柉路闇滆 Ras Frostwhisper     SCH_273
            {Card.Cards.BT_187, 3},//鍑仼路鏃ユ€?Kayn Sunfury     BT_187
            {Card.Cards.BT_717, 2},//娼滃湴铦?Burrowing Scorpid     BT_717 
            {Card.Cards.CORE_EX1_249, 2},//杩﹂】鐢风埖 Baron Geddon     CORE_EX1_249 
            {Card.Cards.DMF_254, 30},//杩﹂】鐢风埖 Baron Geddon     CORE_EX1_249 
            {Card.Cards.RLK_222t2, 14},//鐏劙浣胯€呴樋鏂娲?Astalor, the Flamebringer ID锛歊LK_222t2
            {Card.Cards.RLK_224, 2},//鐩戠潱鑰呭紬閲屽悏杈炬媺 Overseer Frigidara ID锛歊LK_224
             {Card.Cards.RLK_063, 5},//鍐伴湝宸ㄩ緳涔嬫€?Frostwyrm's Fury ID锛歊LK_063 
            {Card.Cards.RLK_015, 3},//鍑涢鍐插嚮 Howling Blast ID锛歊LK_015 
            {Card.Cards.RLK_516, 2},//纰庨鎵嬫枾 Bone Breaker ID锛歊LK_516
        };
        #endregion

#region 鏀诲嚮妯″紡鍜岃嚜瀹氫箟 
      private string _log = "";   // 鏃ュ織瀛楃涓?
      private const string ProfileVersion = "2026-01-03.1";
      public ProfileParameters GetParameters(Board board)
      {
            _log = "";
            var p = new ProfileParameters(BaseProfile.Rush) { DiscoverSimulationValueThresholdPercent = -10 }; 

            if (ProfileCommon.TryRunPureLearningPlayExecutor(board, p))
                return p;

            try
            {
                int enemyHealth = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
                int friendHealth = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
                AddLog($"================ 鐙傞噹澶у摜钀?鍐崇瓥鏃ュ織 v{ProfileVersion} ================");
                AddLog($"鏁屾柟琛€鐢? {enemyHealth} | 鎴戞柟琛€鐢? {friendHealth} | 娉曞姏:{board.ManaAvailable} | 鎵嬬墝:{board.Hand.Count} | 鐗屽簱:{board.FriendDeckCount}");
                AddLog("鎵嬬墝: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => $"{(string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN)}({x.Template.Id}){x.CurrentCost}")));
            }
            catch
            {
                // ignore
            }
            //  澧炲姞鎬濊€冩椂闂?
            // p.ForceResimulation = true;
            int a =(board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) - BoardHelper.GetEnemyHealthAndArmor(board);
            //鏀诲嚮妯″紡鍒囨崲
        	// 寰凤細DRUID 鐚庯細HUNTER 娉曪細MAGE 楠戯細PALADIN 鐗э細PRIEST 璐硷細ROGUE 钀細SHAMAN 鏈細WARLOCK 鎴橈細WARRIOR 鐬庯細DEMONHUNTER 姝伙細DEATHKNIGHT
         
// 涓婚倧杓細鏍规摎鑱锋キ瑷堢畻鍕曟厠鏀绘搳鍊?
switch (board.EnemyClass)
{
    case Card.CClass.PALADIN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 145, board.EnemyClass);
        break;

    case Card.CClass.DEMONHUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 135, board.EnemyClass);
        break;

    case Card.CClass.PRIEST:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 155, board.EnemyClass);
        break;

    case Card.CClass.MAGE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 150, board.EnemyClass);
        break;

    case Card.CClass.DEATHKNIGHT:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 140, board.EnemyClass);
        break;

    case Card.CClass.HUNTER:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 130, board.EnemyClass);
        break;

    case Card.CClass.ROGUE:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 130, board.EnemyClass);
        break;

    case Card.CClass.WARLOCK:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 160, board.EnemyClass);
        break;

    case Card.CClass.SHAMAN:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 125, board.EnemyClass);
        break;

    default:
        p.GlobalAggroModifier = CalculateAggroModifier(a, 150, board.EnemyClass); // 闋愯ō鑱锋キ
        break;
}

// 鏍稿績鏂规硶锛氳▓绠楀嫊鎱嬫敾鎿婂€?
int CalculateAggroModifier(double baseAggro, int baseValue, Card.CClass enemyClass)
{
    double winRateModifier = GetWinRateModifier(enemyClass);
    double usageRateModifier = GetUsageRateModifier(enemyClass);

    // 鏈€绲傝▓绠楁敾鎿婂€?
    int finalAggro = (int)(baseAggro * 0.625 + baseValue + winRateModifier + usageRateModifier);
    AddLog($"鑱锋キ: {enemyClass}, 鏀绘搳鍊? {finalAggro}, 鍕濈巼淇: {winRateModifier}, 浣跨敤鐜囦慨姝? {usageRateModifier}");
    return finalAggro;
}

// 鏂规硶: 瑷堢畻鍕濈巼淇鍊?
double GetWinRateModifier(Card.CClass enemyClass)
{
    double winRate = GetWinRateFromData(enemyClass);
    return (winRate - 50) * 1.5; // 姣忚秴鍑?0%鍕濈巼澧炲姞1.5榛炴敾鎿婂€?
}

// 鏂规硶: 瑷堢畻浣跨敤鐜囦慨姝ｅ€?
double GetUsageRateModifier(Card.CClass enemyClass)
{
    double usageRate = GetUsageRateFromData(enemyClass);
    return usageRate * 0.5; // 姣?%浣跨敤鐜囧鍔?.5榛炴敾鎿婂€?
}

// 妯℃摤鑱锋キ鍕濈巼鏁告摎
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
        default: return 50.0; // 榛樿獚鍕濈巼
    }
}

// 妯℃摤鑱锋キ浣跨敤鐜囨暩鎿?
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
        default: return 10.0; // 榛樿獚浣跨敤鐜?
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
         //瀹氫箟鍦烘敾  鐢ㄦ硶 myAttack <= 5 鑷繁鍦烘敾澶т簬灏忎簬5  enemyAttack  <= 5 瀵归潰鍦烘敾澶т簬灏忎簬5  宸茶绠楁鍣ㄤ激瀹?

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
            // 鍙嬫柟闅忎粠鏁伴噺
            int friendCount = board.MinionFriend.Count;
            // 鎵嬬墝鏁伴噺
            int HandCount = board.Hand.Count;
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
            // 鍙敾鍑婚殢浠?
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);

            // 瀹氫箟澶у摜鍗＄墝ID鐨勯泦鍚?
            var bigBrotherIds = new HashSet<Card.Cards>
            {
                Card.Cards.TSC_639, // 鏆撮宸ㄩ硹鏍兼媺鏍?
                Card.Cards.WW_440,  // 濂旈浄楠忛┈
                Card.Cards.WW_382,  // 姝ョЩ灞变笜
                Card.Cards.TID_712, // 鐚庢疆鑰呰€愭櫘鍥鹃殕
                Card.Cards.TID_711, // Ozumat
                Card.Cards.VAC_934, // Beached Whale
                Card.Cards.BT_155,	// Scrapyard Colossus
                Card.Cards.ETC_086,	// Amplified Elekk
                // Card.Cards.TTN_800, // Golganneth, the Thunderer
            };

            // 璁＄畻鎵嬬墝涓ぇ鍝ョ殑鏁伴噺
            int bigBrother = board.Hand.Count(card => bigBrotherIds.Contains(card.Template.Id));
            AddLog("鎵嬩笂澶у摜鏁伴噺: " + bigBrother);

 #endregion
 #region 涓嶉€佺殑鎬?
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); //濂囪抗鎺ㄩ攢鍛?WW_331
        //   p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(150)); //楸间汉鏈ㄤ箖浼?CORE_ULD_723 
#endregion
#region 閫佺殑鎬?
        
          if(board.HasCardOnBoard(Card.Cards.TSC_962)){//淇グ鑰佸法槌?Gigafin ID锛歍SC_962 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TSC_962t, new Modifier(-100)); //淇グ鑰佸法槌嶄箣鍙?Gigafin's Maw ID锛歍SC_962t 
          }
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_373t, new Modifier(-100)); //鍏疯薄鏆楀奖 Shadow Manifestation ID锛歊EV_373t 
          p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.REV_955, new Modifier(-100)); //鎵т簨鑰呮柉鍥惧皵鐗?Stewart the Steward ID锛歊EV_955 
#endregion

#region Card.Cards.HERO_02bp 鑻遍泟鎶€鑳?
        // 濡傛灉褰撳墠娌℃湁浣跨敤杩囧厓绱?涓旀墜涓湁鍙敤鐨勫厓绱犵墝,闄嶄綆鎶€鑳戒紭鍏堝€?
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_02bp, new Modifier(95)); 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_02bp, new Modifier(-55)); 
#endregion

#region 鎷嶅崠琛屾湪妲?SW_025
    //  濡傛灉鎵嬩笂娌℃湁鎴樺惣闅忎粠 娉ユ布鍙樺舰鎬?DAL_052 ,闄嶄綆姝﹀櫒鏀诲嚮鍊?
    if(!board.HasCardInHand(Card.Cards.DAL_052)
    &&board.WeaponFriend != null 
    && board.WeaponFriend.Template.Id == Card.Cards.SW_025
    ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.SW_025, new Modifier(9999)); 
        AddLog("鎷嶅崠琛屾湪妲屼笉鏀诲嚮");
    }
#endregion

#region 绔ヨ瘽鏋楀湴 TOY_507
    //  濡傛灉鍚庢墜锛屽彲浠ュ湪 2 璐硅烦甯佷笂鍦版爣锛堢璇濇灄鍦帮級
    if(board.HasCardInHand(Card.Cards.TOY_507)
		// 涓旀墜涓婄殑娉ユ布鍙樺舰鎬?DAL_052鏁伴噺涓?
		&&!board.HasCardInHand(Card.Cards.DAL_052)
		&&board.MinionFriend.Count<=6
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_507, new Modifier(-350));  
        AddLog("绔ヨ瘽鏋楀湴"+-350);
    }
    // 鍦轰笂鏈夊湴鏍?鎻愰珮鍦版爣浣跨敤浼樺厛绾?
		if(board.HasCardOnBoard(Card.Cards.TOY_507)
		&&board.HasCardInHand(Card.Cards.DAL_052)&&board.MaxMana>=5
		){
				p.LocationsModifiers.AddOrUpdate(Card.Cards.TOY_507, new Modifier(350)); 
				AddLog("鎵嬩笂鏈夊彉褰㈡€ぇ浜?璐?闄嶄綆鍦版爣浣跨敤");
		}
#endregion

#region 鍏堢鍙敜 GVG_029
    var ancestralCall = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GVG_029);
    // 鎵嬩笂澶у摜鏁伴噺澶т簬0,鎻愰珮鍏堢鍙敜浼樺厛绾?
    if(bigBrother>0
    &&ancestralCall!=null
    // 鍦轰笂闅忎粠灏忎簬绛変簬6
    &&board.MinionFriend.Count<=6
    // 鎵嬮噷娌℃湁娉ユ布鍙樺舰鎬?DAL_052
    &&(!board.HasCardInHand(Card.Cards.DAL_052)
		// 鎴戞柟琛€閲忓皬浜庣瓑浜?0
		||board.HeroFriend.CurrentHealth<=20)
    ){
        // p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GVG_029, new Modifier(-350));
				p.ComboModifier = new ComboSet(ancestralCall.Id);  
        AddLog("鍏堢鍙敜"+-350);
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GVG_029, new Modifier(999));  
    }
		
#endregion

#region 鎴戞壘鍒颁簡 BOT_099
    // 鎵嬩笂澶у摜鏁伴噺澶т簬0,鎻愰珮鍏堢鍙敜浼樺厛绾?
    if(bigBrother>0
    &&board.HasCardInHand(Card.Cards.BOT_099)
    // 鍦轰笂闅忎粠灏忎簬绛変簬6
    &&board.MinionFriend.Count<=6
    // 鎵嬮噷娌℃湁娉ユ布鍙樺舰鎬?DAL_052
    &&!board.HasCardInHand(Card.Cards.DAL_052)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BOT_099, new Modifier(-350));  
        AddLog("鎴戞壘鍒颁簡"+-350);
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.BOT_099, new Modifier(9999));  
    }
#endregion

#region 娉ユ布鍙樺舰鎬?DAL_052
var mudDeformationMonster = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.DAL_052);
    // 鎻愰珮娉ユ布鍙樺舰鎬紭鍏堢骇
    if(mudDeformationMonster!=null
    // 鍦轰笂闅忎粠灏忎簬绛変簬6
    &&board.MinionFriend.Count<=6
    ){
       p.ComboModifier = new ComboSet(mudDeformationMonster.Id);
        AddLog("娉ユ布鍙樺舰鎬嚭");
    }
#endregion

#region 鍗冲叴婕斿 JAM_013
    // 闄嶄綆鍗冲叴婕斿浼樺厛绾?
    if(board.HasCardInHand(Card.Cards.JAM_013)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_013, new Modifier(350));  
        AddLog("鍗冲叴婕斿"+350);
    }
		// 濡傛灉鍦轰笂鍚屾椂鏈夌寧娼€呰€愭櫘鍥鹃殕 TID_712 鍜?(TID_712t 鎴?TID_712t2),闄嶄綆鍗冲叴婕斿瀵圭寧娼€呰€愭櫘鍥鹃殕 TID_712浣跨敤浼樺厛绾?
		if(board.HasCardOnBoard(Card.Cards.TID_712)
		&&(board.HasCardOnBoard(Card.Cards.TID_712t)||board.HasCardOnBoard(Card.Cards.TID_712t2))
		){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_013, new Modifier(999,Card.Cards.TID_712));  
				AddLog("鍗冲叴婕斿涓嶅鐚庢疆鑰呰€愭櫘鍥鹃殕浣跨敤");
		}
#endregion

#region 闆烽渾缁芥斁 SCH_427
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.SCH_427, new Modifier(55));
#endregion

#region 楸肩兢鑱氶泦 TSC_631
  // 鎻愰珮浣跨敤浼樺厛绾?
		if(board.HasCardInHand(Card.Cards.TSC_631)){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TSC_631, new Modifier(-99));  
				AddLog("楸肩兢鑱氶泦"+-99);
		}
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_638, new Modifier(999));  
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_638t, new Modifier(999));  
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_638t2, new Modifier(999));  
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_638t3, new Modifier(999));  
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TSC_638t4, new Modifier(999));  
#endregion

 #region CORE_AT_053 鍏堢鐭ヨ瘑
        if(board.HasCardInHand(Card.Cards.CORE_AT_053)
				// 鎵嬬墝鏁板皬浜庣瓑浜?
				&&HandCount<=7
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_AT_053, new Modifier(-99)); 
        AddLog("鍏堢鐭ヨ瘑-99");
        }
 #endregion

 #region  琛板彉 CFM_696
      //  濡傛灉瀵归潰鏈?SW_075 鑹惧皵鏂囬噹鐚?鎻愰珮琛板彉浼樺厛绾?
			if(board.HasCardOnBoard(Card.Cards.SW_075)
			){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CFM_696, new Modifier(-150));  
					AddLog("琛板彉-150");
			}else{
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CFM_696, new Modifier(130));  
			}
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CFM_696, new Modifier(999));  
 #endregion

 #region 闄嶄綆杩囪浇鐗岄€昏緫
    // 濡傛灉涓嬪洖鍚堝彲浠ョ敤娉ユ布鍙樺舰鎬?DAL_052,闄嶄綆杩囪浇鐗屼紭鍏堢骇
		if(board.Hand.Exists(x=>x.CurrentCost<=board.MaxMana+1 && x.Template.Id==Card.Cards.DAL_052)
		){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_013, new Modifier(350)); //鍗冲叴婕斿 JAM_013
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_AT_053, new Modifier(350)); //CORE_AT_053 鍏堢鐭ヨ瘑
				AddLog("闄嶄綆杩囪浇鐗岄€昏緫1");
		}
    // 濡傛灉涓嬪洖鍚堝彲浠ョ敤鍏堢鍙敜 GVG_029 涓旀湁澶у摜,闄嶄綆杩囪浇鐗屼紭鍏堢骇
		if(board.Hand.Exists(x=>x.CurrentCost<=board.MaxMana+1 && x.Template.Id==Card.Cards.GVG_029)
			 // 鎵嬩笂澶у摜鏁伴噺澶т簬0,鎻愰珮鍏堢鍙敜浼樺厛绾?
		&&bigBrother>0
    // 鍦轰笂闅忎粠灏忎簬绛変簬6
    &&board.MinionFriend.Count<=6
    // 鎵嬮噷娌℃湁娉ユ布鍙樺舰鎬?DAL_052
    &&(!board.HasCardInHand(Card.Cards.DAL_052)
		// 鎴戞柟琛€閲忓皬浜庣瓑浜?0
		||board.HeroFriend.CurrentHealth<=20)
		){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_013, new Modifier(350)); //鍗冲叴婕斿 JAM_013
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_AT_053, new Modifier(350)); //CORE_AT_053 鍏堢鐭ヨ瘑
				AddLog("闄嶄綆杩囪浇鐗岄€昏緫2");
		}
    // 濡傛灉涓嬪洖鍚堝彲浠ョ敤鎴戞壘鍒颁簡 BOT_099 涓旀湁澶у摜,闄嶄綆杩囪浇鐗屼紭鍏堢骇
		if(board.Hand.Exists(x=>x.CurrentCost<=board.MaxMana+1 && x.Template.Id==Card.Cards.BOT_099)
			 // 鎵嬩笂澶у摜鏁伴噺澶т簬0,鎻愰珮鍏堢鍙敜浼樺厛绾?
		&&bigBrother>0
    // 鍦轰笂闅忎粠灏忎簬绛変簬6
    &&board.MinionFriend.Count<=6
    // 鎵嬮噷娌℃湁娉ユ布鍙樺舰鎬?DAL_052
    &&(!board.HasCardInHand(Card.Cards.DAL_052)
		// 鎴戞柟琛€閲忓皬浜庣瓑浜?0
		||board.HeroFriend.CurrentHealth<=20)
		){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.JAM_013, new Modifier(350)); //鍗冲叴婕斿 JAM_013
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_AT_053, new Modifier(350)); //CORE_AT_053 鍏堢鐭ヨ瘑
				AddLog("闄嶄綆杩囪浇鐗岄€昏緫3");
		}
 #endregion

#region 纭竵 GAME_005
    // 濡傛灉涓€璐规墜涓婃湁纭竵,鏈夌璇濇灄鍦?TOY_507,闄嶄綆纭竵浼樺厛绾?
    if(board.MaxMana==1
    &&board.HasCardInHand(Card.Cards.GAME_005)
    &&board.HasCardInHand(Card.Cards.TOY_507)
    ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(350));//纭竵 GAME_005
    }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));//纭竵 GAME_005
    }
#endregion

#region 铏氱伒绁炶皶鑰?GDB_310
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(999));
 		if(board.HasCardInHand(Card.Cards.GDB_310)
		
    ){
		// 閬嶅巻鎵嬬墝闅忎粠,濡傛灉鏄硶鏈?涓斿綋鍓嶆硶鏈墝鐨勮垂鐢?3灏忎簬绛変簬褰撳墠鍥炲悎鏁?鎻愰珮铏氱伒绁炶皶鑰呯殑浼樺厛绾?
				foreach (var item in board.Hand)
			{
				if(item.Type==Card.CType.SPELL
				&&item.CurrentCost+3<=board.ManaAvailable
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(-550));
					AddLog("铏氱伒绁炶皶鑰?-550");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(999));
					// AddLog("铏氱伒绁炶皶鑰?999");
				}
			}
    }
		// 鍦轰笂鏈夎櫄鐏电璋曡€?涓嶉€?
		if(board.MinionFriend.Exists(x=>x.Template.Id==Card.Cards.GDB_310&&x.HasSpellburst)){
			p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_310, new Modifier(150));
			foreach (var item in board.MinionFriend)
			{
				if(item.Template.Id==Card.Cards.GDB_310
				&&item.HasSpellburst
				){
						// 閬嶅巻鎵嬩笂闅忎粠,鎻愰珮娉曟湳鐗屼紭鍏堢骇
					foreach (var card in board.Hand)
					{
						if(card.Type==Card.CType.SPELL
						// 鎵嬬墝鏁板皬浜庣瓑浜?
						&&HandCount<=8
						){
							p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-350));
							AddLog("鎻愰珮娉曟湳鐗屼紭鍏堢骇"+card.Template.NameCN);
						}
					}
				}
			}
		}
#endregion

#region 涓夎娴嬮噺 GDB_451
	if(board.HasCardInHand(Card.Cards.GDB_451)
	){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_451, new Modifier(-150));
		AddLog("涓夎娴嬮噺-150");
	}
#endregion

#region 鎬濊€?
// 閬嶅巻鎵嬩笂鐨勫崱鐗?濡傛灉鏈夎繖浜涘崱鐗?鍒欓噸鏂版€濊€?
foreach (var card in board.Hand)
{
    // 鎺掗櫎纭竵
        if(card.Template.Id != Card.Cards.GAME_005){
        p.ForcedResimulationCardList.Add(card.Template.Id);
        }
}
#endregion


#region VAC_959t05 杩借釜鎶ょ Amulet of Tracking 闅忔満鑾峰彇3寮犱紶璇村崱鐗屻€傦紙鐒跺悗灏嗗叾鍙樺舰鎴愪负鏅€氬崱鐗岋紒锛?
// 濡傛灉鎵嬬墝鏁板皬浜庣瓑浜?,鎻愰珮浣跨敤浼樺厛绾?
        if(board.HasCardInHand(Card.Cards.VAC_959t05)
        &&HandCount<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t05, new Modifier(-99));
        AddLog("杩借釜鎶ょ-99");
        }
#endregion

#region VAC_959t06 鐢熺伒鎶ょ Amulet of Critters 闅忔満鍙敜涓€涓硶鍔涘€兼秷鑰椾负锛?锛夌殑闅忎粠骞朵娇鍏惰幏寰楀槻璁姐€傦紙浣嗗畠鏃犳硶鏀诲嚮锛侊級
        if(board.HasCardInHand(Card.Cards.VAC_959t06)
				// 鍦轰笂闅忎粠灏忎簬绛変簬6
				&&board.MinionFriend.Count<=6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t06, new Modifier(-99));
        AddLog("鐢熺伒鎶ょ-99");
        }
#endregion

#region VAC_959t08 鑳介噺鎶ょ Amulet of Energy 涓轰綘鐨勮嫳闆勬仮澶?2鐐圭敓鍛藉€笺€傦紙鐒跺悗鍙楀埌6鐐逛激瀹筹紒锛?
        if(board.HasCardInHand(Card.Cards.VAC_959t08)
                // 鍙仮澶嶇敓鍛藉€煎ぇ浜?
                &&board.HeroFriend.MaxHealth-board.HeroFriend.CurrentHealth > 6
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t08, new Modifier(-99));
        AddLog("鑳介噺鎶ょ-99");
        }
#endregion

#region VAC_959t10 鎸鸿繘鎶ょ Amulet of Strides 浣夸綘鎵嬬墝涓殑鎵€鏈夊崱鐗岀殑娉曞姏鍊兼秷鑰楀噺灏戯紙1锛夌偣銆傦紙娉曟湳鐗岄櫎澶栵紒锛?
        if(board.HasCardInHand(Card.Cards.VAC_959t10)
        ){
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_959t10, new Modifier(-99));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_959t10, new Modifier(999));
        AddLog("鎸鸿繘鎶ょ-99");
        }
#endregion


#region 鐗堟湰杈撳嚭
                AddLog("---\n鐗堟湰27.7 浣滆€卋y77 Q缇?943879501 DC:https://discord.gg/nn329ZU6Ss\n---");
#endregion

#region 鏀诲嚮浼樺厛 鍗＄墝濞佽儊
var cardModifiers = new Dictionary<Card.Cards, int>
{   
			{ Card.Cards.TLC_468t1,	999 }, // 缁嗛暱榛忓洟 TLC_468t1
			{ Card.Cards.DINO_410,	999 }, // 鍑礇鏂殑铔?DINO_410
		{ Card.Cards.DINO_410t2,	999 }, // 鍑礇鏂殑铔?DINO_410t2
		{ Card.Cards.DINO_410t4,	999 }, // 鍑礇鏂殑铔?DINO_410t4
		{ Card.Cards.DINO_410t5,	999 }, // 鍑礇鏂殑铔?DINO_410t5
		{ Card.Cards.DINO_410t3,	999 }, // 鍑礇鏂殑铔?DINO_410t3
		{ Card.Cards.SC_671t1,	200 }, // 鎵ф斂瀹?SC_671t1
		{ Card.Cards.EDR_849,	200 }, // 姊︾細杩呯寷榫?EDR_849  
		{ Card.Cards.EDR_892, -100 }, // 娈嬫毚鐨勯瓟铦?EDR_892
		{ Card.Cards.EDR_891, -100 }, // 璐┆鐨勫湴鐙辩寧鐘?EDR_891
		{ Card.Cards.GDB_471, 200 }, // GDB_471	娌冪綏灏兼嫑鍕熷畼
		{ Card.Cards.VAC_501, 200 }, // 鏋侀檺杩介€愯€呴樋鍏板 VAC_501 
		{ Card.Cards.GDB_100, 200 }, // 闃胯偗灏肩壒闃叉姢姘存櫠 GDB_100
    { Card.Cards.EDR_815, 200},//EDR_815	灏搁瓟鑺?
    { Card.Cards.TOY_528, 200},//浼村敱鏈?TOY_528
    { Card.Cards.EDR_540, 200},//EDR_540	鎵洸鐨勭粐缃戣洓
    { Card.Cards.EDR_889, 200},//EDR_889	椴滆姳鍟嗚穿 
    { Card.Cards.VAC_503, 200},//VAC_503	鍙敜甯堣揪鍏嬬帥娲?
    { Card.Cards.EDR_816, 200},//EDR_816	鎬紓榄旇殜
    { Card.Cards.EDR_810t, 100},//EDR_810t	楗辫儉姘磋洯
    { Card.Cards.SC_765, 200},//SC_765	楂橀樁鍦ｅ爞姝﹀＋
    { Card.Cards.SC_756, 200},//SC_756	鑸瘝
    { Card.Cards.SC_003, 200},//铏发濂崇帇 SC_003
    { Card.Cards.WW_827, 200},//闆忛緳鐗т汉 WW_827
    { Card.Cards.TTN_903, 200},//鐢熷懡鐨勭細瑾撹€呰壘娆у灏?TTN_903
    { Card.Cards.TTN_960, 200},//鐏笘娉板潶钀ㄦ牸鎷夋柉 TTN_960
    { Card.Cards.TTN_862, 200},//缈犵豢涔嬫槦闃垮彜鏂?TTN_862
    { Card.Cards.TTN_429, 200},//闃挎浖鑻忓皵 TTN_429
     { Card.Cards.TTN_092, 200},//澶嶄粐鑰呴樋鏍兼媺鐜?TTN_092
     { Card.Cards.TTN_075, 200},//璇虹敇鍐?TTN_075鈥冣€?
    
     { Card.Cards.DEEP_008, 200},//閽堝博鍥捐吘 DEEP_008
     { Card.Cards.CORE_RLK_121, 200},//姝讳骸渚嶅儳 CORE_RLK_121
     { Card.Cards.TTN_737, 200},//鍏典富 TTN_737
     { Card.Cards.VAC_406, 900},//VAC_406	鍥板€︾殑宀涙皯
     { Card.Cards.TTN_858, 200},//TTN_858	缁村拰鑰呴樋绫冲浘鏂?
     { Card.Cards.GDB_226, 200},//GDB_226	鍑舵伓鐨勫叆渚佃€?
     { Card.Cards.TOY_330t5, 200},//TOY_330t5 濂囧埄浜氭柉璞崕鐗?000鍨?
    { Card.Cards.TOY_330t11, 200},//TOY_330t11 濂囧埄浜氭柉璞崕鐗?000鍨?
     { Card.Cards.CORE_EX1_012, 200},//CORE_EX1_012 琛€娉曞笀钀ㄥ皵璇烘柉
     { Card.Cards.CORE_BT_187, 200},//CORE_BT_187	鍑仼路鏃ユ€?
     { Card.Cards.CS2_052, 200},//绌烘皵涔嬫€掑浘鑵?CS2_052
     { Card.Cards.WORK_040, 200 },//绗ㄦ嫏鐨勬潅褰?WORK_040
     { Card.Cards.TOY_606, 200 },//娴嬭瘯鍋囦汉 TOY_606   
     { Card.Cards.WW_382, 200 },//姝ョЩ灞变笜 WW_382   
     { Card.Cards.GDB_841, 200 },//GDB_841	娓镐緺鏂ュ€? 
     { Card.Cards.GDB_110, 200 },//GDB_110	閭兘鍔ㄥ姏婧?
     { Card.Cards.CORE_ICC_210, 200 },//CORE_ICC_210	鏆楀奖鍗囪吘鑰?
     { Card.Cards.JAM_024, 200 },//JAM_024	甯冩櫙鍏夎€€涔嬪瓙 
     { Card.Cards.CORE_CS3_014, 200 },//CORE_CS3_014	璧ょ孩鏁欏＋
     
     { Card.Cards.GVG_075, 200 },//GVG_075鈥?鑸硅浇鐏偖
     { Card.Cards.GDB_310, 200 },//GDB_310	铏氱伒绁炶皶鑰?
     { Card.Cards.JAM_010, 200 },//JAM_010	鐐瑰敱鏈哄浘鑵?
     { Card.Cards.TOY_351t, -200 },//TOY_351t	绁炵鐨勮泲
    { Card.Cards.TOY_351, -200 },//TOY_351	绁炵鐨勮泲
    { Card.Cards.WW_391, 200 }, // WW_391	娣橀噾瀹?
    { Card.Cards.TOY_515, 200 }, // 姘翠笂鑸炶€呯储灏煎▍ TOY_515
    { Card.Cards.CORE_TOY_100, 200 }, // 渚忓剴椋炶鍛樿鑾変簹 CORE_TOY_100
    { Card.Cards.WW_381, 200 }, // 鍙椾激鐨勬惉杩愬伐 WW_381
    { Card.Cards.TTN_900, -200 }, // 鐭冲績涔嬬帇 TTN_900
    { Card.Cards.CORE_DAL_609, 200 }, // 鍗￠浄鑻熸柉 CORE_DAL_609
    { Card.Cards.TOY_646, 200 }, // 鎹ｈ泲鏋楃簿 TOY_646
    { Card.Cards.TOY_357, 200 }, // 鎶遍緳鐜嬪櫁椴佷粈 TOY_357
    { Card.Cards.VAC_507, 200 }, // 闃冲厜姹插彇鑰呰幈濡帋 VAC_507
    { Card.Cards.WORK_042, 500 }, // 椋熻倝鏍煎潡 WORK_042
    { Card.Cards.WW_344, 200 }, // 濞佺寷閾剁考宸ㄩ緳 
    { Card.Cards.TOY_812, -5},//TOY_812 鐨櫘甯屄峰僵韫?
    { Card.Cards.VAC_532, 200 },//妞板瓙鐏偖鎵?VAC_532
    { Card.Cards.TOY_505, 200 },//TOY_505	鐜╁叿鑸?
    { Card.Cards.TOY_381, 200 },//TOY_381	绾歌壓澶╀娇
    { Card.Cards.TOY_824, 350 }, // 榛戞閽堢嚎甯?
    { Card.Cards.VAC_927, 200 }, // 鐙傞閭瓟
    { Card.Cards.VAC_938, 200 }, // 绮楁毚鐨勭將鐙?
    { Card.Cards.ETC_355, 200 }, // 鍓冨垁娌兼辰鎽囨粴鏄庢槦
    { Card.Cards.WW_091, 200 },  // 鑵愯嚟娣ゆ偿娉㈡櫘鍔?
    { Card.Cards.VAC_450, 200}, // 鎮犻棽鐨勬洸濂?
    { Card.Cards.TOY_028, 200 }, // 鍥㈤槦涔嬬伒
    { Card.Cards.VAC_436, 200 }, // 鑴嗛娴风洍
    { Card.Cards.VAC_321, 200 }, // 浼婅緵杩ゥ鏂?
    { Card.Cards.TTN_800, 200 }, // 闆烽渾涔嬬楂樻垐濂堟柉 TTN_800
    { Card.Cards.TTN_415, 200 }, // 鍗″吂鏍肩綏鏂?
    { Card.Cards.ETC_541, 200 }, // 鐩楃増涔嬬帇鎵樺凹
    { Card.Cards.CORE_LOOT_231, 200 }, // 濂ユ湳宸ュ尃
    { Card.Cards.ETC_339, 200 }, // 蹇冨姩姝屾墜
    { Card.Cards.ETC_833, 200 }, // 绠煝宸ュ尃
    { Card.Cards.MIS_026, 200 }, // 鍌€鍎″ぇ甯堝閲屽畨
    { Card.Cards.CORE_WON_065, 200 }, // 闅忚埞澶栫鍖诲笀
    { Card.Cards.WW_357, 500 }, // 鑰佽厫鍜岃€佸
    { Card.Cards.DEEP_999t2, 200 }, // 娣卞博涔嬫床鏅剁皣
    { Card.Cards.CFM_039, 200 }, // 鏉傝€嶅皬楝?
    { Card.Cards.WW_364t, 200 }, // 鐙¤瘓宸ㄩ緳濞佹媺缃楀厠
    { Card.Cards.TSC_026t, 200 }, // 鍙媺鍏嬬殑澹?
    { Card.Cards.WW_415, 200 }, // 璁告効浜?
    { Card.Cards.CS3_014, 200 }, // 璧ょ孩鏁欏＋
    { Card.Cards.YOG_516, 200 }, // 鑴卞洶鍙ょ灏ゆ牸-钀ㄩ殕
    { Card.Cards.NX2_033, 200 }, // 宸ㄦ€杩箤鏂?
    { Card.Cards.JAM_004, 200 }, // 闀傞鎭剁姮
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
						 
#region Bot.Log
Bot.Log(_log);         
#endregion                       

            // ===== 鍏ㄥ眬纭鍒欙細涓嶈В瀵归潰鐨勨€滃嚡娲涙柉鐨勮泲鈥濈郴鍒楋紙DINO_410*锛?=====
            // 鍙ｅ緞锛氬闈㈠嚭鐜拌泲闃舵鏃讹紝灏介噺涓嶈鐢ㄩ殢浠?姝﹀櫒/瑙ｇ墝鍘诲鐞嗗畠锛堜紭鍏堝鐞嗗叾浠栨粴闆悆鐐?鐩翠激鐐癸級銆?
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


//寰凤細DRUID 鐚庯細HUNTER 娉曪細MAGE 楠戯細PALADIN 鐗э細PRIEST 璐硷細ROGUE 钀細SHAMAN 鏈細WARLOCK 鎴橈細WARRIOR 鐬庯細DEMONHUNTER
            ApplyLiveMemoryBiasCompat(board, p);
            return p;
        }}
				 // 鍚?_log 瀛楃涓叉坊鍔犳棩蹇楃殑绉佹湁鏂规硶锛屽寘鎷洖杞﹀拰鏂拌
        private void AddLog(string log)
        {
            _log += "\r\n" + log;
        }
        //鑺埄路鑾牸椤跨埖澹妧鑳介€夋嫨
        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            var filteredTable = _heroPowersPriorityTable.Where(x => choices.Contains(x.Key)).ToList();
            return filteredTable.First(x => x.Value == filteredTable.Max(y => y.Value)).Key;
        }
    
        //鍗℃墡搴撴柉閫夋嫨
        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices[0];
        }

        //璁＄畻绫?
        public static class BoardHelper
        {
            //寰楀埌鏁屾柟鐨勮閲忓拰鎶ょ敳涔嬪拰
            public static int GetEnemyHealthAndArmor(Board board)
            {
                return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
            }

            //寰楀埌鑷繁鐨勬硶寮?
            public static int GetSpellPower(Board board)
            {
                //璁＄畻娌℃湁琚矇榛樼殑闅忎粠鐨勬硶鏈己搴︿箣鍜?
                return board.MinionFriend.FindAll(x => x.IsSilenced == false).Sum(x => x.SpellPower);
            }

            //鑾峰緱绗簩杞柀鏉€琛€绾?
            public static int GetSecondTurnLethalRange(Board board)
            {
                //鏁屾柟鑻遍泟鐨勭敓鍛藉€煎拰鎶ょ敳涔嬪拰鍑忓幓鍙噴鏀炬硶鏈殑浼ゅ鎬诲拰
                return GetEnemyHealthAndArmor(board) - GetPlayableSpellSequenceDamages(board);
            }

            //涓嬩竴杞槸鍚﹀彲浠ユ柀鏉€鏁屾柟鑻遍泟
            public static bool HasPotentialLethalNextTurn(Board board)
            {
                //濡傛灉鏁屾柟闅忎粠娌℃湁鍢茶骞朵笖閫犳垚浼ゅ
                //(鏁屾柟鐢熷懡鍊煎拰鎶ょ敳鐨勬€诲拰 鍑忓幓 涓嬪洖鍚堣兘鐢熷瓨涓嬫潵鐨勫綋鍓嶅満涓婇殢浠庣殑鎬讳激瀹?鍑忓幓 涓嬪洖鍚堣兘鏀诲嚮鐨勫彲浣跨敤闅忎粠浼ゅ鎬诲拰)
                //鍚庣殑琛€閲忓皬浜庢€绘硶鏈激瀹?
                if (!board.MinionEnemy.Any(x => x.IsTaunt) &&
                    (GetEnemyHealthAndArmor(board) - GetPotentialMinionDamages(board) - GetPlayableMinionSequenceDamages(GetPlayableMinionSequence(board), board))
                        <= GetTotalBlastDamagesInHand(board))
                {
                    return true;
                }
                //娉曟湳閲婃斁杩囨晫鏂硅嫳闆勭殑琛€閲忔槸鍚﹀ぇ浜庣瓑浜庣浜岃疆鏂╂潃琛€绾?
                return GetRemainingBlastDamagesAfterSequence(board) >= GetSecondTurnLethalRange(board);
            }

            //鑾峰緱涓嬪洖鍚堣兘鐢熷瓨涓嬫潵鐨勫綋鍓嶅満涓婇殢浠庣殑鎬讳激瀹?
            public static int GetPotentialMinionDamages(Board board)
            {
                return GetPotentialMinionAttacker(board).Sum(x => x.CurrentAtk);
            }

            //鑾峰緱涓嬪洖鍚堣兘鐢熷瓨涓嬫潵鐨勫綋鍓嶅満涓婇殢浠庨泦鍚?
            public static List<Card> GetPotentialMinionAttacker(Board board)
            {
                //涓嬪洖鍚堣兘鐢熷瓨涓嬫潵鐨勫綋鍓嶅満涓婇殢浠庨泦鍚?
                var minionscopy = board.MinionFriend.ToArray().ToList();

                //閬嶅巻 浠ユ晫鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鏁屾柟闅忎粠闆嗗悎
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //浠ュ弸鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鐨勬墍鏈夊弸鏂归殢浠庨泦鍚堬紝濡傛灉璇ラ泦鍚堝瓨鍦ㄧ敓鍛藉€煎ぇ浜庝笌鏁屾柟闅忎粠鏀诲嚮鍔?
                    if (board.MinionFriend.OrderByDescending(x => x.CurrentAtk).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //浠ュ弸鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鐨勬墍鏈夊弸鏂归殢浠庨泦鍚?鎵惧嚭璇ラ泦鍚堜腑鍙嬫柟闅忎粠鐨勭敓鍛藉€煎皬浜庣瓑浜庢晫鏂归殢浠庣殑鏀诲嚮鍔涚殑闅忎粠
                        var tar = board.MinionFriend.OrderByDescending(x => x.CurrentAtk).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //灏嗚闅忎粠绉婚櫎鎺?
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //鑾峰緱涓嬪洖鍚堣兘鐢熷瓨涓嬫潵鐨勫闈㈤殢浠庨泦鍚?
            public static List<Card> GetSurvivalMinionEnemy(Board board)
            {
                //涓嬪洖鍚堣兘鐢熷瓨涓嬫潵鐨勫綋鍓嶅闈㈠満涓婇殢浠庨泦鍚?
                var minionscopy = board.MinionEnemy.ToArray().ToList();

                //閬嶅巻 浠ュ弸鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鍙嬫柟鍙敾鍑婚殢浠庨泦鍚?
                foreach (var mi in board.MinionFriend.FindAll(x => x.CanAttack).OrderByDescending(x => x.CurrentAtk))
                {
                    //濡傛灉瀛樺湪鍙嬫柟闅忎粠鏀诲嚮鍔涘ぇ浜庣瓑浜庢晫鏂归殢浠庤閲?
                    if (board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).Any(x => x.CurrentHealth <= mi.CurrentAtk))
                    {
                        //浠ユ晫鏂归殢浠庤閲忛檷搴忔帓搴忕殑鎵€鏈夋晫鏂归殢浠庨泦鍚堬紝鎵惧嚭鏁屾柟鐢熷懡鍊煎皬浜庣瓑浜庡弸鏂归殢浠庢敾鍑诲姏鐨勯殢浠?
                        var tar = board.MinionEnemy.OrderByDescending(x => x.CurrentHealth).FirstOrDefault(x => x.CurrentHealth <= mi.CurrentAtk);
                        //灏嗚闅忎粠绉婚櫎鎺?
                        minionscopy.Remove(tar);
                    }
                }
                return minionscopy;
            }

            //鑾峰彇鍙互浣跨敤鐨勯殢浠庨泦鍚?
            public static List<Card.Cards> GetPlayableMinionSequence(Board board)
            {
                //鍗＄墖闆嗗悎
                var ret = new List<Card.Cards>();

                //褰撳墠鍓╀綑鐨勬硶鍔涙按鏅?
                var manaAvailable = board.ManaAvailable;

                //閬嶅巻浠ユ墜鐗屼腑璐圭敤闄嶅簭鎺掑簭鐨勯泦鍚?
                foreach (var card in board.Hand.OrderByDescending(x => x.CurrentCost))
                {
                    //濡傛灉褰撳墠鍗＄墝涓嶄负闅忎粠锛岀户缁墽琛?
                    if (card.Type != Card.CType.MINION) continue;

                    //褰撳墠娉曞姏鍊煎皬浜庡崱鐗岀殑璐圭敤锛岀户缁墽琛?
                    if (manaAvailable < card.CurrentCost) continue;

                    //娣诲姞鍒板鍣ㄩ噷
                    ret.Add(card.Template.Id);

                    //淇敼褰撳墠浣跨敤闅忎粠鍚庣殑娉曞姏姘存櫠
                    manaAvailable -= card.CurrentCost;
                }

                return ret;
            }

            //鑾峰彇鍙互浣跨敤鐨勫ゥ绉橀泦鍚?
            public static List<Card.Cards> GetPlayableSecret(Board board)
            {
                //鍗＄墖闆嗗悎
                var ret = new List<Card.Cards>();

                //閬嶅巻鎵嬬墝涓墍鏈夊ゥ绉橀泦鍚?
                foreach (var card1 in board.Hand.FindAll(card => card.Template.IsSecret))
                {
                    if (board.Secret.Count > 0)
                    {
                        //閬嶅巻澶翠笂濂ョ闆嗗悎
                        foreach (var card2 in board.Secret.FindAll(card => CardTemplate.LoadFromId(card).IsSecret))
                        {

                            //濡傛灉鎵嬮噷濂ョ鍜屽ご涓婂ゥ绉樹笉鐩哥瓑
                            if (card1.Template.Id != card2)
                            {
                                //娣诲姞鍒板鍣ㄩ噷
                                ret.Add(card1.Template.Id);
                            }
                        }
                    }
                    else
                    { ret.Add(card1.Template.Id); }
                }
                return ret;
            }


            //鑾峰彇涓嬪洖鍚堣兘鏀诲嚮鐨勫彲浣跨敤闅忎粠浼ゅ鎬诲拰
            public static int GetPlayableMinionSequenceDamages(List<Card.Cards> minions, Board board)
            {
                //涓嬪洖鍚堣兘鏀诲嚮鐨勫彲浣跨敤闅忎粠闆嗗悎鏀诲嚮鍔涚浉鍔?
                return GetPlayableMinionSequenceAttacker(minions, board).Sum(x => CardTemplate.LoadFromId(x).Atk);
            }

            //鑾峰彇涓嬪洖鍚堣兘鏀诲嚮鐨勫彲浣跨敤闅忎粠闆嗗悎
            public static List<Card.Cards> GetPlayableMinionSequenceAttacker(List<Card.Cards> minions, Board board)
            {
                //鏈鐞嗙殑涓嬪洖鍚堣兘鏀诲嚮鐨勫彲浣跨敤闅忎粠闆嗗悎
                var minionscopy = minions.ToArray().ToList();

                //閬嶅巻 浠ユ晫鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鏁屾柟闅忎粠闆嗗悎
                foreach (var mi in board.MinionEnemy.OrderByDescending(x => x.CurrentAtk))
                {
                    //浠ュ弸鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鐨勬墍鏈夊弸鏂归殢锟斤拷锟介泦鍚堬紝濡傛灉璇ラ泦鍚堝瓨鍦ㄧ敓鍛藉€煎ぇ浜庝笌鏁屾柟闅忎粠鏀诲嚮鍔?
                    if (minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).Any(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk))
                    {
                        //浠ュ弸鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鐨勬墍鏈夊弸鏂归殢浠庨泦鍚?鎵惧嚭璇ラ泦鍚堜腑鍙嬫柟闅忎粠鐨勭敓鍛藉€煎皬浜庣瓑浜庢晫鏂归殢浠庣殑鏀诲嚮鍔涚殑闅忎粠
                        var tar = minions.OrderByDescending(x => CardTemplate.LoadFromId(x).Atk).FirstOrDefault(x => CardTemplate.LoadFromId(x).Health <= mi.CurrentAtk);
                        //灏嗚闅忎粠绉婚櫎鎺?
                        minionscopy.Remove(tar);
                    }
                }

                return minionscopy;
            }

            //鑾峰彇褰撳墠鍥炲悎鎵嬬墝涓殑鎬绘硶鏈激瀹?
            public static int GetTotalBlastDamagesInHand(Board board)
            {
                //浠庢墜鐗屼腑鎵惧嚭娉曟湳浼ゅ琛ㄥ瓨鍦ㄧ殑娉曟湳鐨勪激瀹虫€诲拰(鍖呮嫭娉曞己)
                return
                    board.Hand.FindAll(x => _spellDamagesTable.ContainsKey(x.Template.Id))
                        .Sum(x => _spellDamagesTable[x.Template.Id] + GetSpellPower(board));
            }

            //鑾峰彇鍙互浣跨敤鐨勬硶鏈泦鍚?
            public static List<Card.Cards> GetPlayableSpellSequence(Board board)
            {
                //鍗＄墖闆嗗悎
                var ret = new List<Card.Cards>();

                //褰撳墠鍓╀綑鐨勬硶鍔涙按鏅?
                var manaAvailable = board.ManaAvailable;

                if (board.Secret.Count > 0)
                {
                    //閬嶅巻浠ユ墜鐗屼腑璐圭敤闄嶅簭鎺掑簭鐨勯泦鍚?
                    foreach (var card in board.Hand.OrderBy(x => x.CurrentCost))
                    {
                        //濡傛灉鎵嬬墝涓張涓嶅湪娉曟湳搴忓垪鐨勬硶鏈墝锛岀户缁墽琛?
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //褰撳墠娉曞姏鍊煎皬浜庡崱鐗岀殑璐圭敤锛岀户缁墽琛?
                        if (manaAvailable < card.CurrentCost) continue;

                        //娣诲姞鍒板鍣ㄩ噷
                        ret.Add(card.Template.Id);

                        //淇敼褰撳墠浣跨敤闅忎粠鍚庣殑娉曞姏姘存櫠
                        manaAvailable -= card.CurrentCost;
                    }
                }
                else if (board.Secret.Count == 0)
                {
                    //閬嶅巻浠ユ墜鐗屼腑璐圭敤闄嶅簭鎺掑簭鐨勯泦鍚?
                    foreach (var card in board.Hand.FindAll(x => x.Type == Card.CType.SPELL).OrderBy(x => x.CurrentCost))
                    {
                        //濡傛灉鎵嬬墝涓張涓嶅湪娉曟湳搴忓垪鐨勬硶鏈墝锛岀户缁墽琛?
                        if (_spellDamagesTable.ContainsKey(card.Template.Id) == false) continue;

                        //褰撳墠娉曞姏鍊煎皬浜庡崱鐗岀殑璐圭敤锛岀户缁墽琛?
                        if (manaAvailable < card.CurrentCost) continue;

                        //娣诲姞鍒板鍣ㄩ噷
                        ret.Add(card.Template.Id);

                        //淇敼褰撳墠浣跨敤闅忎粠鍚庣殑娉曞姏姘存櫠
                        manaAvailable -= card.CurrentCost;
                    }
                }

                return ret;
            }
            
            //鑾峰彇瀛樺湪浜庢硶鏈垪琛ㄤ腑鐨勬硶鏈泦鍚堢殑浼ゅ鎬诲拰(鍖呮嫭娉曞己)
            public static int GetSpellSequenceDamages(List<Card.Cards> sequence, Board board)
            {
                return
                    sequence.FindAll(x => _spellDamagesTable.ContainsKey(x))
                        .Sum(x => _spellDamagesTable[x] + GetSpellPower(board));
            }

            //寰楀埌鍙噴鏀炬硶鏈殑浼ゅ鎬诲拰
            public static int GetPlayableSpellSequenceDamages(Board board)
            {
                return GetSpellSequenceDamages(GetPlayableSpellSequence(board), board);
            }
            
            //璁＄畻鍦ㄦ硶鏈噴鏀捐繃鏁屾柟鑻遍泟鐨勮閲?
            public static int GetRemainingBlastDamagesAfterSequence(Board board)
            {
                //褰撳墠鍥炲悎鎬绘硶鏈激瀹冲噺鍘诲彲閲婃斁娉曟湳鐨勪激瀹虫€诲拰
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


            //鍦ㄦ病鏈夋硶鏈殑鎯呭喌涓嬫湁娼滃湪鑷村懡鐨勪笅涓€杞?
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
        private static bool ApplyLiveMemoryBiasCompat(Board board, ProfileParameters p)
        {
            return false;
        }
    internal static class ProfileCommon
    {
        public static bool TryRunPureLearningPlayExecutor(Board board, ProfileParameters p)
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var executorType = assembly.GetType("SmartBotProfiles.DecisionPlayExecutor", false);
                    if (executorType == null)
                        continue;

                    var method = executorType.GetMethod(
                        "TryRunPureLearningPlayExecutor",
                        new[] { typeof(Board), typeof(ProfileParameters) });
                    if (method == null)
                        continue;

                    object result = method.Invoke(null, new object[] { board, p });
                    return result is bool && (bool)result;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }
    }
    }
}
