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
 * CastMinionsModifiers --->浣犲彲浠ヤ负姣忎釜灏忓叺璁剧疆鍗曠嫭鐨勪慨楗扮锛屼慨楗扮瓒婇珮锛孉I鐜╀粏浠庣殑鍙兘鎬у氨瓒婂皬
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
    public class STDFloodPaladin  : Profile
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
                AddLog("================ 鏍囧噯楠戝＋ 鍐崇瓥鏃ュ織 v" + ProfileVersion + " ================");
                AddLog("鏁屾柟琛€鐢? " + enemyHealth + " | 鎴戞柟琛€鐢? " + friendHealth + " | 娉曞姏:" + board.ManaAvailable + " | 鎵嬬墝:" + board.Hand.Count + " | 鐗屽簱:" + board.FriendDeckCount);
                AddLog("鎵嬬墝: " + string.Join(", ", board.Hand.Where(x => x != null && x.Template != null)
                    .Select(x => (string.IsNullOrWhiteSpace(x.Template.NameCN) ? x.Template.Name : x.Template.NameCN) + "(" + x.Template.Id + ")" + x.CurrentCost)));
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
        p.GlobalAggroModifier = CalculateAggroModifier(a, 0, board.EnemyClass); // 闋愯ō鑱锋キ
        break;
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
						// 瀹氫箟鏁屾柟鎵嬬墝鐨勬暟閲?
						int enemyHandCount = board.EnemyCardCount;
            // 鍙嬫柟闅忎粠鏁伴噺
            int friendCount = board.MinionFriend.Count;
            // 鎵嬬墝鏁伴噺
            int HandCount = board.Hand.Count;
             // 閫氱數鏈哄櫒浜?BOT_907
            int aomiCount = board.Secret.Count;
            int minionNumber=board.Hand.Count(card => card.Type == Card.CType.MINION);
            var TheVirtuesofthePainter = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TOY_810);
             // 瀹氫箟鍧熷満涓竴璐归殢浠庣殑鏁伴噺 TUTR_HERO_11bpt
            int oneCostMinionCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.WW_331||CardTemplate.LoadFromId(card).Id==Card.Cards.CORE_ICC_038||CardTemplate.LoadFromId(card).Id==Card.Cards.CORE_ULD_723)
            // 鍔犳墜涓婄殑杩欎簺鐗?
            +board.Hand.Count(card => card.Template.Id==Card.Cards.WW_331||card.Template.Id==Card.Cards.CORE_ICC_038||card.Template.Id==Card.Cards.CORE_ULD_723)
            // 鍔犲満涓婄殑杩欎簺鐗?
            +board.MinionFriend.Count(card => card.Template.Id==Card.Cards.WW_331||card.Template.Id==Card.Cards.CORE_ICC_038||card.Template.Id==Card.Cards.CORE_ULD_723);
            ;
            // AddLog("涓€璐归殢浠庢暟閲?+oneCostMinionCount);
            // 鍦轰笂瀹為檯鑳藉瓨娲婚殢浠庢暟 -鑴嗗急鐨勯灏搁鐨勬暟閲?TUTR_HERO_11bpt/HERO_11bpt
            int liveMinionCount = board.MinionFriend.Count-board.MinionFriend.Count(x => x.Template.Id == Card.Cards.HERO_11bpt);
            int canAttackMinion=board.MinionFriend.Count(card => card.CanAttack);
            // 瀹氫箟鏁屾柟涓夎鍙婁互涓嬮殢浠庣殑鏁伴噺
            int enemyThreeHealthMinionCount = board.MinionEnemy.Count(card => card.CurrentHealth<=3);
							// 瀹氫箟鍦轰笂鎵€鏈夐殢浠庢暟閲?
						int allMinionCount = CountSpecificRacesInHand(board);
						// 鍦?GetParameters 鏂规硶涓坊鍔?
						int holySpellsInHand = board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_251) // 榫欓碁鍐涘 EDR_251 绁炲湥 // 绁炲湥浣抽吙 VAC_916 绁炲湥
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916) // 绁炲湥浣抽吙 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t2) // 绁炲湥浣抽吙 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t3) // 绁炲湥浣抽吙 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_917t); // 闃叉檼闇?VAC_917t; // 绁炲湥浣抽吙 VAC_916 绁炲湥
						// AddLog($"鎵嬩笂绁炲湥娉曟湳鏁伴噺: {holySpellsInHand}");
						// 浣庤垂娉曟湳鍙互瀵圭洰鏍囦娇鐢ㄧ殑
						int lowCostSpells = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t2)  // 鍦ｅ厜鎶ょ浘
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916)  // 绁炲湥浣抽吙 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_916t3) // 绁炲湥浣抽吙 VAC_916
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_922) // 鏁戠敓鍏夌幆 VAC_922 
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_251); // 榫欓碁鍐涘 EDR_251; // 绁炲湥浣抽吙 VAC_916 绁炲湥
						// AddLog($"鎵嬩笂浣庤垂娉曟湳鏁伴噺: {lowCostSpells}");
						// 鍒ゆ柇鎵嬩笂鏄惁鏈夊ぇ浜?璐圭殑娴蜂笂鑸规瓕 VAC_558鍜屽ぇ浜?璐圭殑鐏伀鏈哄櫒浜?MIS_918 MIS_918t
						int seaShantyCount = board.Hand.Count(card => card.Template.Id == Card.Cards.VAC_558 && card.CurrentCost > 0)+
						board.Hand.Count(card => card.Template.Id == Card.Cards.MIS_918t && card.CurrentCost > 0)+
						board.Hand.Count(card => card.Template.Id == Card.Cards.MIS_918 && card.CurrentCost > 0);
						// AddLog($"鎵嬩笂澶т簬0璐圭殑娴蜂笂鑸规瓕鍜岀伅鐏満鍣ㄤ汉鏁伴噺: {seaShantyCount}");
						// 鍒ゆ柇鎵嬮噷鏄惁鏈夌亴娉ㄧ墝锛岃嫤鑺遍獞澹?EDR_852  閲戣惣骞奸緳 EDR_451  鎸繀瀹堝崼 EDR_800 鍦ｅ厜鎶ょ浘 EDR_264
						int infusedCardsCount = board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_852) // 鑻﹁姳楠戝＋ EDR_852
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_451) // 閲戣惣骞奸緳 EDR_451
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_800) // 鎸繀瀹堝崼 EDR_800
						+ board.Hand.Count(card => card.Template.Id == Card.Cards.EDR_264); // 鍦ｅ厜鎶ょ浘 EDR_264
						  // 鍙敾鍑婚殢浠?
            int canAttackMINIONs=board.MinionFriend.Count(card => card.CanAttack);
			// 瀹氫箟鍦轰笂楸间汉鏁伴噺 MURLOC
						int murlocCount = board.MinionFriend.Count(card => card.IsRace(Card.CRace.MURLOC))+board.MinionFriend.Count(card => card.IsRace(Card.CRace.ALL));
						// 鎵嬬墝楸间汉鏁伴噺
						int murlocInHandCount = board.Hand.Count(card => card.IsRace(Card.CRace.MURLOC))+board.Hand.Count(card => card.IsRace(Card.CRace.ALL));
						
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

// 寮哄厜鎶ゅ崼 TIME_015 闅忎粠 濡傛灉宸辨柟鑻遍泟琛€閲忓ぇ浜庣瓑浜?0 闄嶄綆寮哄厜鎶ゅ崼浣跨敤浼樺厛绾?
#region 寮哄厜鎶ゅ崼 TIME_015
				if(board.HasCardInHand(Card.Cards.TIME_015)
				&& board.HeroFriend.CurrentHealth>=30
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_015, new Modifier(250)); //寮哄厜鎶ゅ崼 TIME_015
					AddLog("寮哄厜鎶ゅ崼 +250");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_015, new Modifier(-150)); //寮哄厜鎶ゅ崼 TIME_015
				}
#endregion

// 鍧﹀厠鏈烘甯?TIME_017 闅忎粠
#region 鍧﹀厠鏈烘甯?TIME_017
				if(board.HasCardInHand(Card.Cards.TIME_017)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_017, new Modifier(-250)); //鍧﹀厠鏈烘甯?TIME_017
					AddLog("鍧﹀厠鏈烘甯?-250");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_017, new Modifier(250)); //鍧﹀厠鏈烘甯?TIME_017
				}
#endregion

// 绾告澘榄斿儚 TOY_809 闅忎粠`
#region 绾告澘榄斿儚 TOY_809
				if(board.HasCardInHand(Card.Cards.TOY_809)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_809, new Modifier(-350)); //绾告澘榄斿儚 TOY_809
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_809, new Modifier(3509)); //绾告澘榄斿儚 TOY_809
					AddLog("绾告澘榄斿儚 -350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_809, new Modifier(250)); //绾告澘榄斿儚 TOY_809
				}	
#endregion

/*
娉曟湳 鏃跺簭鍏夌幆 TIME_700 浣跨敤浼樺厛绾ф彁楂?鍦轰笂鍙嬫柟闅忎粠鏁板皬浜庣瓑浜?
*/ 
#region 娉曟湳 鏃跺簭鍏夌幆 TIME_700
				if(board.HasCardInHand(Card.Cards.TIME_700)
				&& board.MinionFriend.Count <=6
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_700, new Modifier(-250)); //鏃跺簭鍏夌幆 TIME_700
					AddLog("鏃跺簭鍏夌幆 -250");
				}
#endregion

/*
鎻愰珮鍦版爣 瀹夋垐娲涗笡鏋?TLC_100t1 浣跨敤浼樺厛绾?
*/ 
#region 鍦版爣 瀹夋垐娲涗笡鏋?TLC_100t1
				if(board.HasCardInHand(Card.Cards.TLC_100t1)){
					p.LocationsModifiers.AddOrUpdate(Card.Cards.TLC_100t1, new Modifier(-99));
					AddLog("鍦版爣 瀹夋垐娲涗笡鏋?-150");
				}
#endregion

// 姊呭崱鎵樺厠鐨勫厜鐜?TIME_009t2 渚忓剴鍏夌幆 TIME_009t1 褰撴墜涓婃湁杩欎袱寮犲厜鐜?涓旀湁鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009 鏃?浼樺厛鍏堜氦鏄?鍏夌幆
#region 鍏夌幆 姊呭崱鎵樺厠鐨勫厜鐜?TIME_009t2 渚忓剴鍏夌幆 TIME_009t1
				if(((board.HasCardInHand(Card.Cards.TIME_009t2)
				|| board.HasCardInHand(Card.Cards.TIME_009t1)))
				&& board.HasCardInHand(Card.Cards.TIME_009)
				){
					p.TradeModifiers.AddOrUpdate(Card.Cards.TIME_009t2, new Modifier(-150)); //鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009
					p.TradeModifiers.AddOrUpdate(Card.Cards.TIME_009t1, new Modifier(-150)); //鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009
					AddLog("浜ゆ槗鍏夌幆 -150");
				}
#endregion
/*
鎻愰珮鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009 浣跨敤浼樺厛绾? 
*/ 
#region 鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009
				if(board.HasCardInHand(Card.Cards.TIME_009)
				&&!((board.HasCardInHand(Card.Cards.TIME_009t2)
				|| board.HasCardInHand(Card.Cards.TIME_009t1)))
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_009, new Modifier(-350)); //鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_009, new Modifier(999)); //鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009
					AddLog("鏄庢棩宸ㄥ尃鏍煎皵瀹?-350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_009, new Modifier(250)); //鏄庢棩宸ㄥ尃鏍煎皵瀹?TIME_009
				}
#endregion

// 椋熻倝鏍煎潡 WORK_042
/*
浼樺厛瀵逛互涓嬮殢浠庢垨鑰呰垂鐢ㄥぇ浜庣瓑浜?鐨勯殢浠庝娇鐢ㄦ帓闄ら鑲夋ā鍧楄嚜韬?
浜¤锛炲槻璁斤紴鍦ｇ浘
 濡傛灉鍦轰笂娌℃湁浠ヤ笅闅忎粠,闄嶄綆瀵瑰叾浣跨敤鐨勪紭鍏堢骇
鍧﹀厠鏈烘甯?TIME_017
濂囧埄浜氭柉璞崕鐗?3000 鍨?TOY_330t5
*/ 
#region 椋熻倝鏍煎潡 WORK_042
if (board.HasCardInHand(Card.Cards.WORK_042))
{
    bool hasTarget = false;
    foreach (var minion in board.MinionFriend)
    {
        // 鎺掗櫎椋熻倝鏍煎潡鑷韩
        if (minion.Template.Id == Card.Cards.WORK_042) continue;

        if (minion.Template.Id == Card.Cards.TIME_017 || // 鍧﹀厠鏈烘甯?
            minion.Template.Id == Card.Cards.TOY_330t5 || // 濂囧埄浜氭柉
            minion.CurrentCost >= 4) // 璐圭敤澶т簬绛変簬4
        {
            int modifierValue = -200;
            
            // 浼樺厛绾э細浜¤ > 鍢茶 > 鍦ｇ浘
            if (minion.Template.HasDeathrattle)
            {
                modifierValue -= 150; // 浜¤浼樺厛绾ф渶楂?
            }
            else if (minion.IsTaunt)
            {
                modifierValue -= 100; // 鍢茶娆′箣
            }
            else if (minion.IsDivineShield)
            {
                modifierValue -= 50; // 鍦ｇ浘鍐嶆
            }

            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_042, new Modifier(modifierValue, minion.Template.Id));
            hasTarget = true;
            AddLog("椋熻倝鏍煎潡鍚? " + minion.Template.NameCN + " 淇鍊? " + modifierValue);
        }
    }

    if (!hasTarget)
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_042, new Modifier(350));
        AddLog("椋熻倝鏍煎潡娌℃湁鐩爣锛岄檷浣庝紭鍏堢骇");
    }
}
#endregion 

#region 瓒呮椂绌洪硩渚?TIME_706
        if(board.HasCardInHand(Card.Cards.TIME_706)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_706, new Modifier(-99)); //瓒呮椂绌洪硩渚?TIME_706
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_706, new Modifier(-55)); //瓒呮椂绌洪硩渚?TIME_706
					AddLog("瓒呮椂绌洪硩渚?-99");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TIME_706, new Modifier(250)); //瓒呮椂绌洪硩渚?TIME_706
				}
#endregion

#region 鐏硟楸间汉 DINO_404
        if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.DINO_404)
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DINO_404, new Modifier(-99)); //鐏硟楸间汉 DINO_404
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DINO_404, new Modifier(-55)); //鐏硟楸间汉 DINO_404
					AddLog("鐏硟楸间汉 -99");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.DINO_404, new Modifier(250)); //鐏硟楸间汉 DINO_404
				}
#endregion

#region 鎭愭儳鐣忕缉 TLC_823
        if(board.HasCardInHand(Card.Cards.TLC_823)
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_823, new Modifier(-99)); //鎷煎竷濂芥湅鍙?TOY_353
					AddLog("鎭愭儳鐣忕缉 -99");
				}
#endregion

#region 鎷煎竷濂芥湅鍙?TOY_353
        if(board.HasCardInHand(Card.Cards.TOY_353)
				// 鎵嬬墝鏁板皬浜庣瓑浜?
				&& board.Hand.Count <= 9
				){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_353, new Modifier(-99)); //鎷煎竷濂芥湅鍙?TOY_353
					AddLog("鎷煎竷濂芥湅鍙?-99");
				}
#endregion

#region 鍔充綔鑰侀┈ WORK_018
        if(board.HasCardInHand(Card.Cards.WORK_018)){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_018, new Modifier(-150)); //鍔充綔鑰侀┈ WORK_018
					AddLog("鍔充綔鑰侀┈ -150");
				}
#endregion

#region 娉兼紗褰╅硩楸间汉 TOY_517
        if(board.HasCardInHand(Card.Cards.TOY_517)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_517, new Modifier(999)); //娉兼紗褰╅硩楸间汉 TOY_517
				}
#endregion

#region 鐏窘绮剧伒 CORE_UNG_809
        if(board.HasCardInHand(Card.Cards.CORE_UNG_809)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_UNG_809, new Modifier(999)); //鐏窘绮剧伒 CORE_UNG_809
					// AddLog("鐏窘绮剧伒 -150");
				}
#endregion

#region 鐏窘绮剧伒琛嶇敓鐗?UNG_809t1
        if(board.HasCardInHand(Card.Cards.UNG_809t1)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(999)); //鐏窘绮剧伒琛嶇敓鐗?UNG_809t1
					// AddLog("鐏窘绮剧伒 -150");
				}
#endregion

#region 楸间汉鏈ㄤ箖浼?CORE_ULD_723 
        if(board.HasCardInHand(Card.Cards.CORE_ULD_723)){
					p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(999)); //楸间汉鏈ㄤ箖浼?CORE_ULD_723 
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(150)); //楸间汉鏈ㄤ箖浼?CORE_ULD_723 
					// AddLog("楸间汉鏈ㄤ箖浼?-150");
				}
#endregion

#region Card.Cards.HERO_04bp 鑻遍泟鎶€鑳?
        // p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(100));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(-500));
				// 
        p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.EDR_445p, new Modifier(55)); //EDR_445p	宸ㄩ緳鐨勭绂?
        // p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_445p, new Modifier(1999));
				p.ForcedResimulationCardList.Add(Card.Cards.EDR_445p);
				// 鎵撳嵃鍑鸿嫳闆勬妧鑳絙oard.EnemyAbility.Template.Id
				AddLog("鑻遍泟鎶€鑳? " + board.Ability.Template.Id + " ");
				// 濡傛灉鎵嬬墝閲屾湁灏忎簬绛変簬2璐圭殑楸间汉鐗?涓嶄娇鐢ㄨ嫳闆勬妧鑳?
				if (board.Hand.Any(card => card.CurrentCost <= 2 && card.IsRace(Card.CRace.MURLOC)&&card.Template.Id != Card.Cards.GDB_878))//鑴戦硟楸间汉 GDB_878
				{
					p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(999)); // 涓嶄娇鐢ㄨ嫳闆勬妧鑳?
					// legacy log removed for compiler compatibility
				}

				// 濡傛灉鎵嬩笂鏈夐鑲夋牸鍧?WORK_042 涓斿満涓婇殢浠庢暟澶т簬绛変簬5锛屼笉浣跨敤鑻遍泟鎶€鑳介槻姝㈠崱鏍煎瓙
				if (board.HasCardInHand(Card.Cards.WORK_042) && board.MinionFriend.Count >= 5)
				{
					p.CastHeroPowerModifier.AddOrUpdate(Card.Cards.HERO_04bp, new Modifier(999)); // 涓嶄娇鐢ㄨ嫳闆勬妧鑳?
					AddLog("鎵嬩笂鏈夐鑲夋牸鍧椾笖鍦轰笂闅忎粠鏁?=5锛屼笉浣跨敤鑻遍泟鎶€鑳介槻姝㈠崱鏍煎瓙");
				}
#endregion

#region 妞板瓙鐏偖鎵?VAC_532
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(-50));
				if(board.HasCardOnBoard(Card.Cards.VAC_532)){
				// 涓嶉€佹ぐ瀛愮伀鐐墜
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(150));
				}
				if(board.HasCardInHand(Card.Cards.VAC_532)
				// 鍙嬫柟闅忎粠澶т簬绛変簬2
				&&board.MinionFriend.Count >= 2
				){
				// 涓嶉€佹ぐ瀛愮伀鐐墜
				p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(-150));
				AddLog("妞板瓙鐏偖鎵?-150");
				}
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_532, new Modifier(999));
#endregion

#region 鏍夐緳 TLC_603
				if(
					board.HasCardInHand(Card.Cards.TLC_603)
				)
				{
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(999));
				}
				// 鍦轰笂鏈夋爥榫?TLC_603
				if(board.HasCardOnBoard(Card.Cards.TLC_603)){
					p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(150));
					AddLog("鏍夐緳 TLC_603 150");
				}
#endregion

#region 鍟娇缁块硩楸间汉 EDR_999
				if (
					board.HasCardInHand(Card.Cards.EDR_999)
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(-150));
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_999, new Modifier(999));
						AddLog("鍟娇缁块硩楸间汉 -150");
				}
#endregion

#region TLC_110 鍩庡競棣栬剳鍩冭垝
				if (
					board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_110)
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_110, new Modifier(-999));
						AddLog("鍩庡競棣栬剳鍩冭垝 -999");
				}
#endregion

#region SC_013 鐜涙鼎
        // 涓嶇敤椹鼎浼氬崱姝?
				if (board.HasCardInHand(Card.Cards.SC_013)
				// 闅忎粠灏忎簬绛変簬3
				&& board.MinionFriend.Count <= 3
				// 鏁屾柟闅忎粠涓?
				&& board.MinionEnemy.Count == 0
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_013, new Modifier(-350));
						AddLog("鐜涙鼎 SC_013 -350");
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.SC_013, new Modifier(999));
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.SC_013, new Modifier(-350));
#endregion

#region 鍙€曠殑涓诲帹 VAC_946
		// 鍙嬫柟闅忎粠涔﹀皬浜庣瓑浜?
		if (board.MinionFriend.Count <= 5)
		{
				// 濡傛灉鎵嬩笂鏈夊彲鎬曠殑涓诲帹 VAC_946,鍒欎娇鐢ㄤ紭鍏堢骇+100
				if (board.HasCardInHand(Card.Cards.VAC_946))
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_946, new Modifier(-350));
						AddLog("鍙€曠殑涓诲帹"+(-350));
				}
		}
		// 濡傛灉鍦轰笂鏈夊彲鎬曠殑涓诲帹,涓诲姩鎶婁粬閫佷簡
		// if (board.HasCardOnBoard(Card.Cards.VAC_946))
		// {
    //     p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_946, new Modifier(-5)); 
		// 		AddLog("鍙€曠殑涓诲帹閫?);
		// }
#endregion


#region 娌夌潯鐨勬灄绮?EDR_469
				// 濡傛灉鎵嬩笂鏈夋矇鐫＄殑鏋楃簿 EDR_469,鎻愰珮浣跨敤浼樺厛绾?
				if (board.HasCardInHand(Card.Cards.EDR_469))
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_469, new Modifier(-150));
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_469, new Modifier(999));
						AddLog("娌夌潯鐨勬灄绮?EDR_469 -150");
				}
#endregion

#region 楸间汉鎷涙疆鑰?CORE_EX1_509
// 闄嶄綆鍏舵敾鍑婚『搴廯
				if (board.HasCardOnBoard(Card.Cards.CORE_EX1_509))
				{
					p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(-50));
				}
				if (board.HasCardInHand(Card.Cards.EDR_469))
				{
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_509, new Modifier(3999));
				}
#endregion

 #region ULD_177	鍏埅宸ㄦ€?
            if(board.HasCardOnBoard(Card.Cards.ULD_177)
            &&HandCount<=2
            ){
          	p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.ULD_177, new Modifier(-100));
            // legacy log removed for compiler compatibility
            }
            if(board.HasCardInHand(Card.Cards.ULD_177)
            &&HandCount<=3
            ){
          	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ULD_177, new Modifier(-150));
            AddLog("鍏埅宸ㄦ€嚭");
            }
 #endregion

#region 骞藉厜楸肩 CORE_BT_018

    if(board.HasCardInHand(Card.Cards.CORE_BT_018)
		// 鎵嬩笂鏈夋鍣?涓斾负CORE_BT_018,闄嶄綆浣跨敤浼樺厛绾?
		&&board.WeaponFriend != null 
		&& board.WeaponFriend.Template.Id == Card.Cards.CORE_BT_018
    ){
			p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.CORE_BT_018, new Modifier(350));
			// legacy log removed for compiler compatibility
    }else{
			p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.CORE_BT_018, new Modifier(150));
			// AddLog("娌℃湁楸兼潌,130");
		}
#endregion

#region 鐭冲ご WW_001t

    // if(board.HasCardInHand(Card.Cards.WW_001t)
    // ){
		// 		// 閬嶅巻宸辨柟鍦轰笂闅忎粠,姘歌繙涓嶅鍏朵娇鐢ㄧ煶澶?
		// foreach(var minion in board.MinionFriend)
		// {
		// 		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_001t, new Modifier(9999, minion.Template.Id));
		// 		// AddLog($"鐭冲ご {minion.Template.Id} 999");
		// }
		// // 涓嶅宸辨柟鑻遍泟浣跨敤
		// p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_001t, new Modifier(9999, board.HeroFriend.Template.Id));
    // }
	
#endregion

#region 涓存椂鐗岄€昏緫
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
                AddLog("涓轰复鏃跺崱 ,鎻愰珮浣跨敤浼樺厛绾? " + c.Template.NameCN + " -999");
            }
            else if (markedIds.Contains(c.Template.Id) && !c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT) && c.Type == Card.CType.SPELL)
            {
                p.CastMinionsModifiers.AddOrUpdate(c.Template.Id, 150);
                AddLog("鍗＄墖鍖归厤浣嗕笉鏄复鏃跺崱,闄嶄綆浣跨敤浼樺厛绾? " + c.Template.NameCN + " 150");
            }
        }
    }
}
#endregion

#region  鎶涚煶楸间汉 TLC_427

    if(board.HasCardInHand(Card.Cards.TLC_427)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_427, new Modifier(-150));
			AddLog("鎶涚煶楸间汉 -150");
    }
#endregion

#region 椋炵伀娴佹槦路鑺澃 CORE_CFM_344

    if(board.HasCardInHand(Card.Cards.CORE_CFM_344)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CFM_344, new Modifier(-150));
			AddLog("椋炵伀娴佹槦路鑺澃 -150");
    }
#endregion

#region 楸间汉棰嗗啗 CORE_EX1_507

    if(board.HasCardInHand(Card.Cards.CORE_EX1_507)
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_507, new Modifier(999));
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_507, new Modifier(200));
    }
#endregion

#region 濂栧搧鍟嗚穿 CORE_DMF_067

    if(board.HasCardInHand(Card.Cards.CORE_DMF_067)
		// 鎵嬬墝鏁板皬浜庣瓑浜?
		&&board.Hand.Count <= 8
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_DMF_067, new Modifier(999));
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_DMF_067, new Modifier(-150));
			AddLog("濂栧搧鍟嗚穿 -150");
    }
#endregion

#region 婀鹃硩鍋ヨ韩楸间汉 VAC_531

    if(board.HasCardInHand(Card.Cards.VAC_531)
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_531, new Modifier(999));
    }
#endregion

#region CORE_EX1_103 瀵掑厜鍏堢煡

    if(board.HasCardInHand(Card.Cards.CORE_EX1_103)
		// 鍦轰笂楸间汉鏁伴噺灏忎簬绛変簬2
		&& murlocCount <= 2	
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_103, new Modifier(350));
    }
#endregion


#region 楸间汉鐚庢疆鑰?CORE_EX1_506

    if(board.HasCardInHand(Card.Cards.CORE_EX1_506)
    ){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_506, new Modifier(1999));
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_506, new Modifier(-99));
			AddLog("楸间汉鐚庢疆鑰?-99");
    }
#endregion

#region 娣规病鐨勫湴鍥?TLC_442

    if(board.HasCardInHand(Card.Cards.TLC_442)
		// 澶т簬3璐?
		&&board.ManaAvailable >= 3
    ){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_442, new Modifier(-99));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_442, new Modifier(3999));
			AddLog("娣规病鐨勫湴鍥?-99");
    }
#endregion


#region 鎮犻棽鐨勬洸濂?VAC_450
    // 褰撳満涓婂彲鏀诲嚮闅忎粠澶т簬鐨勭瓑浜?鏃?鎻愰珮浼樺厛绾?
    if (canAttackMINIONs >= 2 
        && board.HasCardInHand(Card.Cards.VAC_450)
        )
        {
            // 濡傛灉鍦轰笂娴风洍澶т簬2锛屾彁楂樹紭鍏堢骇
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(canAttackMINIONs*-99));
        // legacy log removed for compiler compatibility
        }
        else
        {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(350));
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_450, new Modifier(999)); 
#endregion

#region 濉硟鏆撮緳 TLC_240

    if(board.HasCardInHand(Card.Cards.TLC_240)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(-350));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(1999));
			AddLog("濉硟鏆撮緳 -350");
    }
		// 濡傛灉鍦轰笂闅忎粠鏁伴噺灏忎簬绛変簬4,鎻愰珮閫佹帀鐨勪紭鍏堝€?
		if(board.MinionFriend.Count <= 4
		&&board.HasCardOnBoard(Card.Cards.TLC_240)
		)
		{
      p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(-100));
			// 鎻愰珮鏀诲嚮浼樺厛绾?
			p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TLC_240, new Modifier(999));

		}
#endregion



#region 绱壊鐝嶉硟楸间汉 TLC_438

    if(board.HasCardInHand(Card.Cards.TLC_438)
		// 澶т簬绛変簬4璐?
		&&board.ManaAvailable >= 4
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_438, new Modifier(-150));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_438, new Modifier(5999));
			AddLog("绱壊鐝嶉硟楸间汉 -150");
    }else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_438, new Modifier(250));
		}
#endregion

#region 娓╂硥韪忔氮楸间汉 TLC_428

    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_428)
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_428, new Modifier(-350));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_428, new Modifier(1299));
			AddLog("娓╂硥韪忔氮楸间汉 -350");
    }else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_428, new Modifier(130));
		}
#endregion

#region 钂搁硩鍋疯泲璐?TLC_429
  var SteamedFinsEggThief = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TLC_429);
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_429)
		// 闅忎粠鏁板皬浜庣瓑浜?
		&&board.MinionFriend.Count <=4
    ){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_429, new Modifier(-150));
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_429, new Modifier(999));
			AddLog("钂搁硩鍋疯泲璐?-150");
    }else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_429, new Modifier(350));
		}
#endregion

#region 铔奔鎸戞垬鑰?TLC_251
  var BarbarianFishChallenger = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TLC_251);
// 濡傛灉鏈夎洰楸兼寫鎴樿€?TLC_251,璐圭敤澶т簬绛変簬7,鍦轰笂闅忎粠灏忎簬绛変簬2鎵揷ombo
		// if(board.HasCardInHand(Card.Cards.TLC_251)
		// // 璐圭敤澶т簬绛変簬7
		// &&board.MaxMana >= 7
		// // 鍦轰笂闅忎粠灏忎簬绛変簬2
		// &&board.MinionFriend.Count <=2
		// &&BarbarianFishChallenger!=null
		// &&SteamedFinsEggThief!=null
		// ){
		// 	if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.TLC_429)){
    //     p.ComboModifier = new ComboSet(BarbarianFishChallenger.Id, SteamedFinsEggThief.Id);
		// 		AddLog("铔奔鎸戞垬鑰?钂搁硩鍋疯泲璐糲ombo");
		// 	}
		// }else{
		// 	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_251, new Modifier(-150));
		// }
		if(board.HasCardInHand(Card.Cards.TLC_251)
		){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_251, new Modifier(-150));
			AddLog("铔奔鎸戞垬鑰?-150");
		}
#endregion

#region 娼滃叆钁涙媺鍗?TLC_426
    if(board.HasCardInHand(Card.Cards.TLC_426)
    ){
        if(board.MaxMana == 1){
            // 浠呯涓€鍥炲悎浣跨敤
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(-350));
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(9999));
            AddLog("娼滃叆钁涙媺鍗?棣栧洖鍚? -350");
        }else{
            // 闈炵涓€鍥炲悎绂佹浣跨敤锛屾彁楂樻潈閲嶉伩鍏嶅嚭鐗?
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(350));
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_426, new Modifier(19999));
            AddLog("娼滃叆钁涙媺鍗?闈為鍥炲悎涓嶄娇鐢? +350");
        }
    }
#endregion

#region 鐏厜涔嬮緳鑿茶幈鍏?FIR_959
  // 鍙嬫柟闅忎粠灏忎簬绛変簬5,鎻愰珮浣跨敤浼樺厛绾?
	if(board.HasCardInHand(Card.Cards.FIR_959)
	// 鎵嬬墝鏁板皬浜庣瓑浜?
	&&HandCount<=8
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.FIR_959, new Modifier(-999));
		AddLog("鐏厜涔嬮緳鑿茶幈鍏?999");
	}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.FIR_959, new Modifier(999));
#endregion

#region CORE_REV_308	杩峰鍚戝
  // 鍙嬫柟闅忎粠灏忎簬绛変簬5,鎻愰珮浣跨敤浼樺厛绾?
	if(board.HasCardInHand(Card.Cards.CORE_REV_308)
	&&board.MinionFriend.Count<=5
	){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_REV_308, new Modifier(-150));
		AddLog("杩峰鍚戝-150");
	}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_REV_308, new Modifier(999));
#endregion

#region 淇′话鍦ｅ GDB_139
    // 闅忎粠鏁板皬浜庣瓑浜?,鎻愰珮浣跨敤浼樺厛绾?
		if(board.HasCardInHand(Card.Cards.GDB_139)
		&&board.MinionFriend.Count<=5
		){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_139, new Modifier(-150));
		AddLog("淇′话鍦ｅ-150");
		}
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_139, new Modifier(999));
#endregion

#region 浼婄憺灏旓紝甯屾湜淇℃爣 GDB_141
    // 濡傛灉鎵嬬墝鏁板皬浜庣瓑浜?,鎻愰珮浣跨敤浼樺厛绾?
		if(board.HasCardInHand(Card.Cards.GDB_141)
		&&HandCount<=7
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_141, new Modifier(-50*enemyAttack));
		AddLog("浼婄憺灏旓紝甯屾湜淇℃爣"+-50*enemyAttack);
		}
#endregion


#region 鏄熼檯鐮旂┒鍛?GDB_728
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(888));
		// 涓€璐逛笉鐢ㄦ槦闄呯爺绌跺憳
		if(board.HasCardInHand(Card.Cards.GDB_728)
		&&board.MaxMana==1
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(350));
		AddLog("鏄熼檯鐮旂┒鍛?350");
		}else if(board.HasCardInHand(Card.Cards.GDB_728)
		&&board.MaxMana>=2
    ){
		// 閬嶅巻鎵嬬墝闅忎粠,濡傛灉鏄硶鏈?涓斿綋鍓嶆硶鏈墝鐨勮垂鐢?3灏忎簬绛変簬褰撳墠鍥炲悎鏁?鎻愰珮浼樺厛绾?
				foreach (var item in board.Hand)
			{
				if(item.Type==Card.CType.SPELL
				&&item.CurrentCost+2<=board.MaxMana
				){
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(-99));
					// AddLog("鏄熼檯鐮旂┒鍛?+(-150));
				}else{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(200));
				}
			}
    }
			if(board.MinionFriend.Exists(x=>x.Template.Id==Card.Cards.GDB_728&&x.HasSpellburst)){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_728, new Modifier(150));
					// 閬嶅巻鎵嬩笂闅忎粠,鎻愰珮娉曟湳鐗屼紭鍏堢骇
				foreach (var card in board.Hand)
				{
					if(card.Type==Card.CType.SPELL
					// 鎵嬬墝鏁板皬浜庣瓑浜?
					&&HandCount<=9
					){
						p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-150));
						// AddLog("鎻愰珮娉曟湳鐗屼紭鍏堢骇"+(card.Template.Id));
					}
				}
			}
#endregion


#region 杩涘寲铻嶅悎鎬?VAC_958
//褰撴垜鏂瑰満涓婃湁杩涘寲铻嶅悎鎬紝鎻愰珮buff鐗屽杩涘寲铻嶅悎鎬?鐨勪娇鐢ㄤ紭鍏堢骇
if(board.HasCardOnBoard(Card.Cards.VAC_958))
{
    // 瀹氫箟闇€瑕佽皟鏁翠紭鍏堢骇鐨刡uff鐗?
    var buffCards = new List<Card.Cards>
    {
        Card.Cards.CORE_BT_292, // 闃胯揪灏斾箣鎵?
        Card.Cards.BT_025,      // 鏅烘収鍦ｅ
        Card.Cards.GDB_138,     // 绁炴€у湥濂?
        Card.Cards.VAC_917t,    // 闃叉檼闇?
        Card.Cards.VAC_916,     // 绁炲湥浣抽吙1
        Card.Cards.VAC_916t2,   // 绁炲湥浣抽吙2
        Card.Cards.VAC_916t3    // 绁炲湥浣抽吙3
    };

    foreach (var buffCard in buffCards)
    {
        // 妫€鏌ユ墜鐗屼腑鏄惁鏈夎buff鐗岋紙绁炴€у湥濂?闃叉檼闇?绁炲湥浣抽吙绯诲垪瑕佹眰璐圭敤鈮?锛?
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
                    // 瀵硅瀺鍚堟€彁楂樹紭鍏堢骇
                    p.CastSpellsModifiers.AddOrUpdate(buffCard, new Modifier(-150, Card.Cards.VAC_958));
                    // legacy log removed for compiler compatibility
                }
                else
                {
                    // 瀵瑰叾浠栭殢浠庨檷浣庝紭鍏堢骇
                    p.CastSpellsModifiers.AddOrUpdate(buffCard, new Modifier(150, minion.Template.Id));
                    // legacy log removed for compiler compatibility
                }
            }
        }
    }
}
if(board.HasCardInHand(Card.Cards.VAC_958)
){
	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_958, new Modifier(-550));
	AddLog("杩涘寲铻嶅悎鎬?550");
}
#endregion

#region 鏂╂槦宸ㄥ垉 GDB_726
        if(board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.GDB_726
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.GDB_726, -99);
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.GDB_726, new Modifier(-50));
        // legacy log removed for compiler compatibility
        }
				if( board.HasCardInHand(Card.Cards.GDB_726)
				// 鎵嬩笂娌℃湁鍥涜垂鐨勪俊浠板湥濂?GDB_139
				&&!board.Hand.Exists(x=>x.Template.Id==Card.Cards.GDB_139&&x.CurrentCost==4)
        ){
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.GDB_726, -350);
        // legacy log removed for compiler compatibility
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_726, 2999);
#endregion

#region 绁炴€у湥濂?GDB_138
// 0璐规椂,鎻愰珮浣跨敤浼樺厛绾?
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_138 && x.CurrentCost == 0)
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_138, new Modifier(-350));
AddLog("绁炴€у湥濂戞彁楂樹娇鐢ㄤ紭鍏堢骇");
}else{
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_138, new Modifier(350));
}
#endregion

#region 鏄庢緢鍦ｅ GDB_137
p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(999));
// 璐圭敤涓嶄负0锛岄檷浣庝娇鐢ㄤ紭鍏堢骇
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_137 && x.CurrentCost >=2)
// 鎵嬬墝鏁板皬浜庣瓑浜?
&&HandCount<=8
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(-150));
AddLog("鏄庢緢鍦ｅ-150");
}
// 0璐规椂,鎻愰珮浣跨敤浼樺厛绾?
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_137 && x.CurrentCost ==1)
// 鎵嬬墝鏁板皬浜庣瓑浜?
&&HandCount<=8
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(-150));
AddLog("鏄庢緢鍦ｅ-99");
}
// 0璐规椂,鎻愰珮浣跨敤浼樺厛绾?
if(board.Hand.Exists(x => x.Template.Id == Card.Cards.GDB_137 && x.CurrentCost == 0)
// 鎵嬬墝鏁板皬浜庣瓑浜?
&&HandCount<=8
){
p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_137, new Modifier(-350));
AddLog("鏄庢緢鍦ｅ-350");
}
#endregion

// #region 宸茶厫铓€鐨勬ⅵ榄?EDR_846t1
// 	// 濡傛灉娌℃湁鍙敾鍑婚殢浠?闄嶄綆浣跨敤浼樺厛绾?
// 	if(canAttackMinion==0
// 	&&board.HasCardInHand(Card.Cards.EDR_846t1)){
// 		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_846t1, new Modifier(350));
// 		AddLog("宸茶厫铓€鐨勬ⅵ榄?EDR_846t1 350");
// 	}
// #endregion

#region 鎶婄粡鐞嗗彨鏉ワ紒 VAC_460
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-50));
#endregion


#region 娴蜂笂鑸规瓕 VAC_558
	// 濡傛灉鎵嬩笂鏈変綆璐圭鍦ｆ硶鏈?闄嶄綆浣跨敤浼樺厛绾?
	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(-150));
	if(board.HasCardInHand(Card.Cards.VAC_558)
	&&holySpellsInHand>0
	// 鍙嬫柟闅忎粠澶т簬0
	&&friendCount>0
	){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(350));
		AddLog("娴蜂笂鑸规瓕 VAC_558 350");
	}else if(board.HasCardInHand(Card.Cards.VAC_558)
	// 鍙嬫柟闅忎粠澶т簬0
	&&friendCount<=4
	){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(-350));
		AddLog("娴蜂笂鑸规瓕 VAC_558 -350");
	}
	// 濡傛灉鍦轰笂鏈夎幈濡帋,鏈変綆浜?璐圭殑鑸规瓕 鎻愰珮鑸规瓕浣跨敤浼樺厛绾?
		if (board.HasCardOnBoard(Card.Cards.VAC_507)
		&& board.Hand.Exists(x => x.CurrentCost <=2 && x.Template.Id==Card.Cards.VAC_558)
		){
			p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_558, new Modifier(3999)); 
			// legacy log removed for compiler compatibility
		}
#endregion

#region 闃冲厜姹插彇鑰呰幈濡帋 VAC_507
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(3999));
		// 鑾卞Ξ鑾庢柀鏉€閫昏緫
		// 瀹氫箟鍦轰笂铏氱伒鐨勬暟閲?铏氱伒绁炶皶鑰?GDB_310
		int ghostCount = board.MinionFriend.Count(x => x.Template.Id == Card.Cards.GDB_310);
		// 瀹氫箟鎵嬩笂 鎶婄粡鐞嗗彨鏉ワ紒 VAC_460 鐨勬暟閲?
		int managerCount = board.Hand.Count(x => x.Template.Id == Card.Cards.VAC_460);
		// 瀹氫箟鎵嬩笂闃冲厜姹插彇鑰呰幈濡帋 VAC_507鐨勬暟閲?
		int sunCount = board.Hand.Count(x => x.Template.Id == Card.Cards.VAC_507);
		// 瀹氫箟鎵嬩笂鍦ｅ厜鑽у厜妫掔殑鏁伴噺 MIS_709
		int lightCount = board.Hand.Count(x => x.Template.Id == Card.Cards.MIS_709);
		// 瀹氫箟鏁屾柟鑻遍泟鐨勭敓鍛藉€?鎶ょ敳
		int enemyHealth = BoardHelper.GetEnemyHealthAndArmor(board);
			//瀹氫箟鎵嬩笂0璐圭殑绁炴€у湥濂?GDB_138鐨勬暟閲?
		int divineSacrament0 = board.Hand.Count(x => x.Template.Id == Card.Cards.GDB_138&&x.CurrentCost<=1);
		if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 褰撳墠鍦轰笂鏈夐槼鍏夋辈鍙栬€呰幈濡帋 VAC_507,鏂╂潃绾夸负managerCount*4+lightCount*8,闇€瑕?璐瑰彲浠ュ惎鍔?
		&&board.MaxMana>=4
		&&managerCount*4+lightCount*8>=enemyHealth
		){
			// 閬嶅巻鎵嬬墝,闄嶄綆闄や簡VAC_460 MIS_709涔嬪鍏朵粬鐗岀殑浣跨敤浼樺厛绾?
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 鎻愰珮娉曟湳鐗屽鏁屾柟鑻遍泟鐨勪娇鐢ㄤ紭鍏堢骇
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("鍗曟父瀹?4琛€鏂╂潃 1");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 褰撴墜涓婃湁闃冲厜姹插彇鑰呰幈濡帋 VAC_507,鏂╂潃绾夸负managerCount*4+lightCount*8,闇€瑕?璐瑰彲浠ュ惎鍔?
		&&board.MaxMana>=9
		&&managerCount*4+lightCount*8>=enemyHealth
		){
				// 閬嶅巻鎵嬬墝,闄嶄綆闄や簡VAC_460 MIS_709涔嬪鍏朵粬鐗岀殑浣跨敤浼樺厛绱?
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
			// 鎻愰珮娉曟湳鐗屽鏁屾柟鑻遍泟鐨勪娇鐢ㄤ紭鍏堢骇
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-999));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("鍗曟父瀹?4琛€鏂╂潃 2");
		}else if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 褰撳墠鍦轰笂鏈夐槼鍏夋辈鍙栬€呰幈濡帋 VAC_507,ghostCount涓?,鏂╂潃绾夸负managerCount*6+lightCount*10,闇€瑕?璐瑰彲浠ュ惎鍔?
		&&board.MaxMana>=4
		&&ghostCount==1
		&&managerCount*6+lightCount*10>=enemyHealth
		){
				// 閬嶅巻鎵嬬墝,闄嶄綆闄や簡VAC_460 MIS_709涔嬪鍏朵粬鐗岀殑浣跨敤鍎厛绱?
			foreach (var item in board.Hand)
			{
				if(item.Template.Id!=Card.Cards.VAC_460
				){
					p.CastSpellsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
					p.CastWeaponsModifiers.AddOrUpdate(item.Template.Id, new Modifier(999));
				}
			}
			// 鎻愰珮娉曟湳鐗屽鏁屾柟鑻遍泟鐨勪娇鐢ㄤ紭鍏堢骇
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("娓稿+鍗曡櫄鐏?2琛€鏂╂潃 1");
		}else if(board.HasCardOnBoard(Card.Cards.VAC_507)
		// 褰撳墠鍦轰笂鏈夐槼鍏夋辈鍙栬€呰幈濡帋 VAC_507,鎵嬩笂鏈夎櫄鐏?鏂╂潃绾夸负managerCount*6+lightCount*10,闇€瑕?璐瑰彲浠ュ惎鍔?
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
			// 鎻愰珮娉曟湳鐗屽鏁屾柟鑻遍泟鐨勪娇鐢ㄤ紭鍏堢骇
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("娓稿+鍗曡櫄鐏?2琛€鏂╂潃 2");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 褰撳墠鎵嬩笂鏈夐槼鍏夋辈鍙栬€呰幈濡帋 VAC_507,ghostCount涓?,鏂╂潃绾夸负managerCount*6+lightCount*10,闇€瑕?璐瑰彲浠ュ惎鍔?
		&&board.MaxMana>=9
		&&ghostCount==1
		&&managerCount*6+lightCount*10>=enemyHealth
		){
				// 閬嶅巻鎵嬬墝,闄嶄綆闄や簡VAC_460 MIS_709涔嬪鍏朵粬鐗岀殑浣跨敤鍎厛绱?
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
			// 鎻愰珮娉曟湳鐗屽鏁屾柟鑻遍泟鐨勪娇鐢ㄤ紭鍏堢骇
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-999));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("娓稿+鍗曡櫄鐏?2琛€鏂╂潃 3");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 褰撳墠鍦轰笂鏈夐槼鍏夋辈鍙栬€呰幈濡帋 VAC_507,鎵嬩笂鏈夎櫄鐏?鏂╂潃绾夸负managerCount*6+lightCount*10,闇€瑕?璐瑰彲浠ュ惎鍔?
		&&board.HasCardInHand(Card.Cards.GDB_310)
		&&managerCount*6+lightCount*10>=enemyHealth
		&&board.MaxMana>=10
		){
				// 閬嶅巻鎵嬬墝,闄嶄綆闄や簡VAC_460 MIS_709涔嬪鍏朵粬鐗岀殑浣跨敤鍎厛绱?
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
			// 鎻愰珮娉曟湳鐗屽鏁屾柟鑻遍泟鐨勪娇鐢ㄤ紭鍏堢骇
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-999));
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_460, new Modifier(-999,board.HeroEnemy.Id));
			AddLog("娓稿+鍗曡櫄鐏?2琛€鏂╂潃 4");
		}else if(board.HasCardInHand(Card.Cards.VAC_507)
		// 濡傛灉褰撳墠闅忎粠鏀诲嚮鍔?鎵嬩笂绁炴€у湥濂戠殑鏁伴噺*2澶т簬绛変簬鏁屾柟鑻遍泟鐨勭敓鍛藉€?鎻愰珮闃冲厜姹插彇鑰呰幈濡帋鐨勪娇鐢ㄤ紭鍏堢骇
		&&myAttack+divineSacrament0*3>=enemyHealth
		){
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(-350));
		}else{
		p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_507, new Modifier(150));
		}
#endregion

#region 鍦ｅ厜鑽у厜妫?MIS_709
	// 濡傛灉鎵嬩笂鏈夊湥鍏夎崸鍏夋 MIS_709,鎻愰珮浣跨敤浼樺厛绾?
		if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) != 1 && card.Template.Id == Card.Cards.MIS_709)
		){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(350));
		}else if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.MIS_709)
		// 鏁屾柟闅忎粠澶т簬0
		&&board.MinionEnemy.Count>0
		){
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(-99));
		}
		// 濡傛灉鎵嬩笂鏈夊湥鍏夎崸鍏夋 MIS_709,鎻愰珮浣跨敤浼樺厛绾?
		p.CastSpellsModifiers.AddOrUpdate(Card.Cards.MIS_709, new Modifier(9999, board.HeroEnemy.Id));
		// 涓嶅鏁屾柟鑻遍泟浣跨敤 
#endregion


#region 鐏伀鏈哄櫒浜?MIS_918/MIS_918t
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(-500));
		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(-500));
		if(board.Hand.Exists(x => x.Template.Id == Card.Cards.MIS_918&&(x.CurrentCost == 0||lowCostSpells==0)))
		{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(-150));
			AddLog("鐏伀鏈哄櫒浜?-150");
		}else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918, new Modifier(150));
		}
		if(board.Hand.Exists(x => x.Template.Id == Card.Cards.MIS_918t&&(x.CurrentCost == 0||lowCostSpells==0)))
		{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(-150));
			AddLog("鐏伀鏈哄櫒浜?-150");
		}else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.MIS_918t, new Modifier(150));
		}
#endregion


#region 绁炲湥浣抽吙 VAC_916t2/VAC_916t3/VAC_916
        if((board.HasCardInHand(Card.Cards.VAC_916t2)||board.HasCardInHand(Card.Cards.VAC_916t3)||board.HasCardInHand(Card.Cards.VAC_916))
        &&board.MinionFriend.Count > 0
        ){
        foreach (var card in board.MinionFriend)
            {
                // 濡傛灉闅忎粠宸叉湁鍦ｇ浘锛屽垯涓嶅鍏朵娇鐢ㄧ鍦ｄ匠閰?
                if (card.IsDivineShield)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(500, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(500, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(500, card.Template.Id));
                    AddLog("闅忎粠 " + card.Template.NameCN + " 宸叉湁鍦ｇ浘锛屼笉浣跨敤绁炲湥浣抽吙");
                    continue;
                }

                // 浼樺厛缁欏ぇ浜庣瓑浜?璐圭殑闅忎粠璐磋啘
                if (card.CurrentCost >= 4)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(-200, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(-200, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(-200, card.Template.Id));
                    AddLog("绁炲湥浣抽吙浼樺厛璐撮珮璐归殢浠? " + card.Template.NameCN);
                }
                else if ((card.CanAttack||seaShantyCount>0)
								){
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(-5, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(-5, card.Template.Id));
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(-5, card.Template.Id));
										AddLog("绁炲湥浣抽吙 VAC_916t2/VAC_916t3/VAC_916 -5");
								}
								else{
										p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(130));
										p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(130));
										p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(130));
									}
                if(card.Template.Id == Card.Cards.TOY_330t5)//TOY_330t5 濂囧埄浜氭柉璞崕鐗?000鍨?
                {
                    // 杩欓噷鍘熶唬鐮佷技涔庢湁璇紝搴旇鏄拡瀵圭鍦ｄ匠閰垮濂囧埄浜氭柉鐨勪慨楗帮紝鎴栬€呴檷浣庡鍒╀簹鏂綔涓虹洰鏍囩殑浼樺厛绾?
                     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(350, Card.Cards.TOY_330t5));
                     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(350, Card.Cards.TOY_330t5));
                     p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(350, Card.Cards.TOY_330t5));
                }
            }
        }
				// 濡傛灉鎵嬩腑鏈夋捣涓婅埞姝?VAC_558,鑷繁鍦轰笂娌￠殢浠?鎻愰珮瀵硅嚜宸辫嫳闆勪娇鐢ㄤ紭鍏堢骇
				// if(seaShantyCount>0
				// &&board.HasCardInHand(Card.Cards.VAC_916t2)||board.HasCardInHand(Card.Cards.VAC_916t3)||board.HasCardInHand(Card.Cards.VAC_916)
        // &&board.MinionFriend.Count == 0)
				// {
				// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(-350, board.HeroFriend.Id));
				// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(-350, board.HeroFriend.Id));
				// 	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(-350, board.HeroFriend.Id));
				// 	AddLog("绁炲湥浣抽吙 VAC_916t2/VAC_916t3/VAC_916 -350 瀵硅嚜宸辫嫳闆?);
				// }

        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(999));
				// 涓嶅鏁屾柟鑻遍泟浣跨敤
				if((board.HasCardInHand(Card.Cards.VAC_916t2)||board.HasCardInHand(Card.Cards.VAC_916t3)||board.HasCardInHand(Card.Cards.VAC_916))
        ){
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t2, new Modifier(999, board.HeroEnemy.Id));
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916t3, new Modifier(999, board.HeroEnemy.Id));
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_916, new Modifier(999, board.HeroEnemy.Id));
        }
#endregion

#region 鎶楁€у厜鐜?TTN_851
        // 鎻愰珮浣跨敤浼樺厛绾?
				if(
					board.HasCardInHand(Card.Cards.TTN_851) 
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_851, new Modifier(-350)); 
						AddLog("鎶楁€у厜鐜? -350");
				}
#endregion

#region 鍦ｅ厜闂幇 CORE_TRL_307
        // 鎻愰珮浣跨敤浼樺厛绾?
				if(
					board.HasCardInHand(Card.Cards.CORE_TRL_307) 
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_TRL_307, new Modifier(-150)); 
						AddLog("鍦ｅ厜闂幇  -150");
				}
#endregion

#region 闃胯揪灏斾箣鎵?CORE_BT_292
        	if(
					board.HasCardInHand(Card.Cards.CORE_BT_292) 
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_BT_292, new Modifier(-150)); 
						AddLog("闃胯揪灏斾箣鎵? -150");
				}
		
#endregion

#region 鏁戠敓鍏夌幆 VAC_922
        // 鎻愰珮浣跨敤浼樺厛绾?
				if(
					board.HasCardInHand(Card.Cards.VAC_922) 
					// 鎵嬬墝鏁板皬浜庣瓑浜?
					&&HandCount<=9
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_922, new Modifier(-550)); 
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_922, new Modifier(999)); 
						AddLog("鏁戠敓鍏夌幆  -550");
				}
#endregion

#region 鍦ｅ厜鎶ょ浘 EDR_264
        // 鎻愰珮浣跨敤浼樺厛绾?
				if(
					board.HasCardInHand(Card.Cards.EDR_264) 
					// 闅忎粠鏁板皬浜庣瓑浜?
					&&board.MinionFriend.Count <= 6
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_264, new Modifier(-99)); 
						AddLog("鍦ｅ厜鎶ょ浘  -99");
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_264, new Modifier(3999)); 
#endregion

#region 闃叉檼闇?VAC_917t
        // 鎻愰珮浣跨敤浼樺厛绾?
				if(
					board.HasCardInHand(Card.Cards.VAC_917t)
					// 鎴戞柟闅忎粠澶т簬0
					&&board.MinionFriend.Count > 0
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_917t, new Modifier(-550)); 
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_917t, new Modifier(999)); 
						AddLog("闃叉檼闇? -550");
				}
#endregion

#region 榫欓碁鍐涘 EDR_251
        // 鎻愰珮浣跨敤浼樺厛绾?
				if(
					board.Hand.Exists(card => GetTag(card,Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.EDR_251)
					// 鎵嬬墝鏁板皬浜庣瓑浜?,闅忎粠鏁板皬浜庣瓑浜?
					&&HandCount<=9
					&&board.MinionFriend.Count <= 6
				){
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_251, new Modifier(-150)); 
						AddLog("榫欓碁鍐涘  -150");
				}else{
						p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_251, new Modifier(350)); 
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_251, new Modifier(888)); 
#endregion

#region 鑰冨唴鐣欐柉路缃楀 CORE_SW_080
				if(board.HasCardInHand(Card.Cards.CORE_SW_080) 
				&&HandCount<=8
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_SW_080, new Modifier(-150)); 
						AddLog("鑰冨唴鐣欐柉路缃楀 CORE_SW_080 -150");
				}
#endregion

#region 瑁呴グ缇庢湳瀹?TOY_882
				if(board.HasCardInHand(Card.Cards.TOY_882) 
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_882, new Modifier(-150)); 
						AddLog("瑁呴グ缇庢湳瀹?TOY_882 -150");
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_882, new Modifier(999)); 
#endregion

#region 鐑堢劙鍏冪礌 UNG_809t1
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.UNG_809t1, new Modifier(999)); 
#endregion

#region 灏忕簿鐏?CORE_CS2_231
				if(board.HasCardInHand(Card.Cards.CORE_CS2_231) //灏忕簿鐏?CORE_CS2_231
				){
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CS2_231, new Modifier(-150)); 
						AddLog("灏忕簿鐏?CORE_CS2_231 -150");
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_CS2_231, new Modifier(999)); 

#endregion

#region 楦濡?EDR_492
        // 闅忎粠鏁板皬浜庣瓑浜?鍙互浣跨敤楦濡?EDR_492,鍚﹀垯涓嶄娇鐢?
				if (board.HasCardInHand(Card.Cards.EDR_492) //楦濡?EDR_492
				&& board.MinionFriend.Count <= 3
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_492, new Modifier(-99)); 
						AddLog("楦濡?EDR_492 -99");
				}
				else
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_492, new Modifier(350)); 
				}
#endregion

#region 涔岀储灏?EDR_259
// 鍒ゆ柇鏄惁鏈夎帋鎷夎揪甯屽皵
int sarahCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_846)
+board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_255);
        var susuoer = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.EDR_259);
		// 涓嶄娇鐢ㄤ箤绱㈠皵 EDR_259
  //  褰撴墜涓婃湁鑾庢媺杈惧笇灏?EDR_846鏃?涔岀储灏?EDR_259鎵嶄娇鐢?鍚﹀垯涓嶄娇鐢?
		if (susuoer!= null
		&& board.HasCardInHand(Card.Cards.EDR_846) //鑾庢媺杈惧笇灏?EDR_846
		// 涓旀墜涓婃病鏈夊ぇ浜?璐圭殑娴蜂笂鑸规瓕
		&& !board.Hand.Exists(x => x.CurrentCost >= 7 && x.Template.Id==Card.Cards.VAC_558)
		// 鎵嬬墝鏁板皬浜庣瓑浜?
		&& board.Hand.Count <= 7
		&& (board.HasCardInHand(Card.Cards.EDR_846)||board.HasCardInHand(Card.Cards.EDR_255)) //鑾庢媺杈惧笇灏?EDR_846/澶嶈嫃鐑堢劙 EDR_255
		){
			p.ComboModifier = new ComboSet(susuoer.Id);
			// legacy log removed for compiler compatibility
		}else if (susuoer!= null
		&& board.HasCardInHand(Card.Cards.EDR_255) //澶嶈嫃鐑堢劙 EDR_255
		){
			p.ComboModifier = new ComboSet(susuoer.Id);
			// legacy log removed for compiler compatibility
		}else if(sarahCount==0){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_259, new Modifier(9999)); 
		}
		// AddLog("鑾庢媺杈惧笇灏?EDR_846 鏁伴噺"+sarahCount);
#endregion

#region 鑾庢媺杈惧笇灏?EDR_846
// 鍒ゆ柇鏄惁鐢ㄨ繃涔岀储灏?
int ursoCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_259)+
+board.MinionFriend.Count(card => card.Template.Id==Card.Cards.EDR_259);
    // 涓嶄娇鐢ㄨ帋鎷夎揪甯屽皵 EDR_846
		if (board.HasCardInHand(Card.Cards.EDR_846) //鑾庢媺杈惧笇灏?EDR_846
		&&ursoCount==0
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_846, new Modifier(9999)); 
		}else{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_846, new Modifier(-150)); 
		}
#endregion

#region 澶嶈嫃鐑堢劙 EDR_255
// 鍧熷満澶嶈嫃鐑堢劙鏁伴噺
int reviveCount = board.FriendGraveyard.Count(card => CardTemplate.LoadFromId(card).Id==Card.Cards.EDR_255);
    // 涓嶄娇鐢ㄨ帋鎷夎揪甯屽皵 EDR_846
		if (board.HasCardInHand(Card.Cards.EDR_255) //鑾庢媺杈惧笇灏?EDR_255
		&&ursoCount==0
		&&reviveCount>=1
		&&board.HasCardInHand(Card.Cards.EDR_259)
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.EDR_255, new Modifier(9999)); 
		}
#endregion

#region 宸ㄧ唺涔嬫 EDR_253
      //  褰撴墜涓婃病鏈夋鍣ㄦ椂,鎻愰珮宸ㄧ唺涔嬫 EDR_253浣跨敤浼樺厛绾?
			if(board.HasCardInHand(Card.Cards.EDR_253) //宸ㄧ唺涔嬫 EDR_253
			){
				p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(-150)); 
				AddLog("宸ㄧ唺涔嬫 -150");
			}
			// 鏀诲嚮浼樺厛绾ф彁楂?
			// 濡傛灉鎵嬬墝鏁板皬浜庣瓑浜?
			if ((board.Hand.Count >=9||board.FriendDeckCount<=2
			)
			&&board.WeaponFriend != null
      && board.WeaponFriend.Template.Id == Card.Cards.EDR_253
			){
				p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(999));
				// legacy log removed for compiler compatibility
			}
  		p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(3999));
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.EDR_253, new Modifier(3999));
#endregion

#region 鑴戦硟楸间汉 GDB_878
// 鍦轰笂楸间汉澶т簬绛変簬2,鎻愰珮浣跨敤浼樺厛绾?
		if(board.HasCardInHand(Card.Cards.GDB_878)
		&&(murlocCount>=3
		||(murlocInHandCount>=2 && board.Hand.Count <= 3) //鎵嬩笂鏈?涓奔浜?涓旀墜鐗屾暟灏忎簬绛変簬9
		)
		// 鍦轰笂娌℃湁鑴戦硟楸间汉 GDB_878
		&& !board.HasCardOnBoard(Card.Cards.GDB_878)
		// 鍦轰笂楸间汉鏁伴噺+鎵嬬墝鏁伴噺-1<=10
		&& (murlocCount + board.Hand.Count-1 <= 9)
		){
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_878, new Modifier(-50*(murlocCount)));
			AddLog("鑴戦硟楸间汉"+(-50*(murlocCount)));
		}else{
			p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_878, new Modifier(350));
		}
		
		int deathrattleGDB878Count = 0;
		foreach (var minion in board.MinionFriend)
		{
				// 鍙鏈変竴涓骸璇槸鑴戦硟楸间汉灏辫涓€娆?
				if (minion.Enchantments.Any(enchant => 
						enchant.DeathrattleCard != null && 
						enchant.DeathrattleCard.Template.Id == Card.Cards.GDB_878))
				{
						deathrattleGDB878Count++;
				}
		}
		// 鐜板湪 deathrattleGDB878Count 灏辨槸鍦轰笂鎷ユ湁鑴戦硟楸间汉浜¤鐨勯殢浠庢暟閲?
		AddLog("鍦轰笂鎷ユ湁鑴戦硟楸间汉浜¤鐨勯殢浠庢暟閲? " + deathrattleGDB878Count);
		// 鎵撳嵃瀹為檯杩囩墝鏁伴噺
		AddLog("瀹為檯杩囩墝鏁伴噺: " +( murlocCount+deathrattleGDB878Count));
#endregion

#region 鐤媯鐢熺墿 EDR_105
      //  鎵嬬墝鏁板皬浜庣瓑浜?,鎻愰珮鐤媯鐢熺墿 EDR_105浣跨敤浼樺厛绾?
				if (board.Hand.Count <= 9
				&& board.HasCardInHand(Card.Cards.EDR_105) //鐤媯鐢熺墿 EDR_105
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_105, new Modifier(-99)); 
						AddLog("鐤媯鐢熺墿 EDR_105 -99");
				}
				else
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_105, new Modifier(350)); 
				}
#endregion

#region 蹇欑鏈哄櫒浜?WORK_002
// 瀹氫箟鍦轰笂鍙嬫柟涓€鏀诲嚮鍔涢殢浠庣殑鏁伴噺	
				int oneAttackMinionCount = board.MinionFriend.Count(x => x.CurrentAtk == 1);
				// AddLog("涓€鏀诲嚮鍔涢殢浠庢暟閲?+oneAttackMinionCount);
        // 濡傛灉涓€鏀诲嚮鍔涢殢浠庡ぇ浜庣瓑浜?,鎻愰珮蹇欑鏈哄櫒浜?WORK_002浣跨敤浼樺厛绾?
                if (oneAttackMinionCount >= 2
				&& board.HasCardInHand(Card.Cards.WORK_002) //蹇欑鏈哄櫒浜?WORK_002
				&&board.MinionEnemy.Count==0
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_002, new Modifier(-150* oneAttackMinionCount));
						AddLog("蹇欑鏈哄櫒浜?WORK_002 -"+(150* oneAttackMinionCount)); 
				}else
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WORK_002, new Modifier(999)); 
				}
						p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WORK_002, new Modifier(16)); 
#endregion

#region 姊︾細杩呯寷榫?EDR_849
				p.PlayOrderModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(4999)); 
			//  澶т簬绛変簬涓よ垂,鎻愰珮浣跨敤浼樺厛绾?
			 if ((board.MaxMana >= 2||board.HasCardInHand(Card.Cards.GAME_005))
			//  鎵嬮噷鏈夌‖甯?

				&& board.HasCardInHand(Card.Cards.EDR_849) //姊︾細杩呯寷榫?EDR_849
				&&!board.HasCardOnBoard(Card.Cards.EDR_849)
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(-150)); 
						AddLog("姊︾細杩呯寷榫?EDR_849 -150");
				}
			if(board.HasCardOnBoard(Card.Cards.EDR_849)){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.EDR_849, new Modifier(350)); 
				// legacy log removed for compiler compatibility
				}
				// 鎵嬮噷鏈夎繀鐚涢緳,涓嶇敤鍑舵伓鐨勬粦鐭涚撼杩?CORE_TSC_827
				if (board.HasCardInHand(Card.Cards.EDR_849) //姊︾細杩呯寷榫?EDR_849
				&& board.HasCardInHand(Card.Cards.CORE_TSC_827) //鍑舵伓鐨勬粦鐭涚撼杩?CORE_TSC_827
				&&(board.MaxMana >= 2||(board.HasCardInHand(Card.Cards.GAME_005)&&board.MaxMana == 1))
				)
				{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(350)); 
						AddLog("鍑舵伓鐨勬粦鐭涚撼杩︿笉浣跨敤");
				}
#endregion

#region WW_331	濂囪抗鎺ㄩ攢鍛?
         if(board.HasCardInHand(Card.Cards.WW_331)
         &&board.MaxMana>=5
		){
        	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(150)); 
					AddLog("濂囪抗鎺ㄩ攢鍛?150");
		}else{
        	p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_331, new Modifier(250)); 
        }
#endregion

#region 2璨婚倧杓?
        if(board.MaxMana == 2
				&&board.HasCardInHand(Card.Cards.YOG_525) //鏈夊仴韬倢鍣ㄤ汉 YOG_525
				&&board.HasCardInHand(Card.Cards.CORE_CFM_753)//鏈夋薄鎵嬭渚涜揣鍟?CORE_CFM_753
						)
						{
					p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_CFM_753, new Modifier(999)); 
					p.ForgeModifiers.AddOrUpdate(Card.Cards.YOG_525, new Modifier(-350));
					AddLog("2璨诲劒鍏堥憚閫犲仴韬倢鍣ㄤ汉");
        }
#endregion
#region 姝ｄ箟淇濇姢鑰?CORE_ICC_038
        // 闄嶄綆浣跨敤浼樺厛绾?
        if(board.HasCardInHand(Card.Cards.CORE_ICC_038)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ICC_038, new Modifier(150)); 
          AddLog("姝ｄ箟淇濇姢鑰?150");
        }
#endregion

#region WW_344	濞佺寷閾剁考宸ㄩ緳
      if(board.HasCardInHand(Card.Cards.WW_344)
      &&board.HasCardInHand(Card.Cards.DEEP_017)
      &&board.MinionFriend.Count <=5
      &&board.MaxMana>=2
      ){
      p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(150));
      AddLog("濞佺寷閾剁考宸ㄩ緳 150");
      }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(-99));
      }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(3999));
				p.AttackOrderModifiers.AddOrUpdate(Card.Cards.WW_344, new Modifier(150));
#endregion

#region  WW_391 娣橀噾瀹?
        if(board.HasCardInHand(Card.Cards.WW_391)){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(-99)); 
        AddLog("娣橀噾瀹?99");
        }
		if(board.HasCardInHand(Card.Cards.WW_391)&&board.Hand.Count <4){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(-200)); 
        AddLog("鎵嬬墝灏忔柤4寮垫窐閲戝-200");
        }
				// 涓嶉€佹窐閲戝
				if(board.HasCardOnBoard(Card.Cards.WW_391)){
				p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WW_391, new Modifier(200)); 
				// legacy log removed for compiler compatibility
				}
#endregion
#region 闊充箰娌荤枟甯?ETC_325鈥?
    //  濡傛灉active,涓?璨诲緦鎵嶇敤
    if(board.Hand.Exists(card => GetTag(card, Card.GAME_TAG.POWERED_UP) == 1 && card.Template.Id == Card.Cards.ETC_325)
		&&board.MaxMana>=5
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_325, new Modifier(-150)); 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_325, new Modifier(999)); 
        AddLog("闊充箰娌荤枟甯?-150");
    }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_325, new Modifier(999)); 
    }
#endregion
#region 鏈嶈瑁佺紳 ETC_420鈥?
    //  濡傛灉buff灏忔柤7,鍒欎笉鐢?
    if(board.Hand.Exists(x=>x.CurrentAtk<7 && x.Template.Id==Card.Cards.ETC_420)
    ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_420, new Modifier(500)); 
        AddLog("鏈嶈瑁佺紳 500");
    }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_420, new Modifier(-150)); 
    }
#endregion
#region DEEP_017	閲囩熆浜嬫晠
       if(board.HasCardInHand(Card.Cards.DEEP_017)
        && board.MinionFriend.Count <=5
       ){
       	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(-550)); 
        AddLog("閲囩熆浜嬫晠 -550");
      }else{
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(999)); 
      }
       	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.DEEP_017, new Modifier(999)); 
#endregion

#region CORE_GVG_061	浣滄垬鍔ㄥ憳
      if(board.HasCardInHand(Card.Cards.CORE_GVG_061)
        // 褰撳墠闅忎粠鏁板ぇ浜庣瓑浜?
        && board.MinionFriend.Count<=5
      ){
      p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_GVG_061, new Modifier(-350));
      AddLog("浣滄垬鍔ㄥ憳 -350");
      }else{
      p.CastSpellsModifiers.AddOrUpdate(Card.Cards.CORE_GVG_061, new Modifier(350));
			}
#endregion

 #region 甯冨悏鑸炰箰 Boogie Down ETC_318
            var Boogie = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.ETC_318);
            var coins = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.GAME_005);
            if(Boogie!=null
            &&board.MinionFriend.Count <= 4
            &&oneCostMinionCount<=4
            &&board.MaxMana == 4
            ){
                p.ComboModifier = new ComboSet(Boogie.Id);
                AddLog("甯冨悏鑸炰箰鍑?1");
            }
            if(Boogie!=null
            &&board.MinionFriend.Count <= 4
            &&oneCostMinionCount<=4
            ){
                p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_318, new Modifier(-350));
                AddLog("甯冨悏鑸炰箰鍑?2");
            }
            // 濡傛灉涓夎垂鏈夌‖甯?浣跨敤甯冨悏鑸炰箰
            if(Boogie!=null
            &&board.MaxMana == 3
            &&board.MinionFriend.Count <=4
            &&coins!=null
            &&oneCostMinionCount<=2
            ){
                p.ComboModifier = new ComboSet(coins.Id,Boogie.Id);
                AddLog("璐圭敤涓?涓旀湁纭竵,浣跨敤甯冨悏鑸炰箰");
            }
#endregion

#region  CORE_EX1_586    娴峰法浜?
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_586, new Modifier(-999));
        var seaGiant = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.CORE_EX1_586);
        if(seaGiant!=null
        &&board.Hand.Exists(x=>x.CurrentCost==0 && x.Template.Id==Card.Cards.CORE_EX1_586
        )){
             p.ComboModifier = new ComboSet(seaGiant.Id);
            AddLog("浣庤垂娴峰法浜哄嚭");
        }
        if(board.Hand.Exists(x=>(x.CurrentCost<=3) && x.Template.Id==Card.Cards.CORE_EX1_586)
        // 涓旀墜閲屾病鏈?璐归殢浠?
        &&!board.Hand.Exists(x=>x.CurrentCost==1)
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_586, new Modifier(-150));
        // legacy log removed for compiler compatibility
        }
#endregion

#region 闊冲搷宸ョ▼甯堟櫘鍏瑰厠 ETC_425
        // 鏍规嵁鏁屾柟鎵嬬墝鐨勬暟閲忔彁楂橀煶鍝嶅伐绋嬪笀鏅吂鍏嬩紭鍏堢骇
        if(board.HasCardInHand(Card.Cards.ETC_425)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_425, new Modifier(-30*enemyHandCount));
          AddLog("闊冲搷宸ョ▼甯堟櫘鍏瑰厠"+(-30*enemyHandCount));
        }
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_425, new Modifier(999));
					// 宸辨柟闅忎粠灏忎簬绛変簬6,涓诲姩閫侀煶鍝嶅伐绋嬪笀鏅吂鍏?
				if(board.HasCardOnBoard(Card.Cards.ETC_425)
				&&board.MinionFriend.Count <= 6
				){
					  p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TTN_907, new Modifier(-20));
					// legacy log removed for compiler compatibility
				}			
#endregion

#region 鍏夐€熸姠璐?TOY_716
        // 鎴戞柟闅忎粠灏忎簬3,闄嶄綆浣跨敤浼樺厛绾?
          p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(15)); 
        if(board.HasCardInHand(Card.Cards.TOY_716)
        &&liveMinionCount >= 3
				&&board.HasCardInHand(Card.Cards.TTN_908)//TTN_908	鍗佸瓧鍐涘厜鐜?
        ){
					// 濡傛灉鍦轰笂鏈夐殢浠?鍒欓檷浣庝娇鐢ㄤ紭鍏堢骇
					p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(150)); 
					AddLog("鍏夐€熸姠璐?150");
				}else if(board.HasCardInHand(Card.Cards.TOY_716)
        &&liveMinionCount >= 3
				&&!board.HasCardInHand(Card.Cards.TTN_908)//TTN_908	鍗佸瓧鍐涘厜鐜?
        )
				// 濡傛灉鍦轰笂鏈夐殢浠?鍒欓檷浣庝娇鐢ㄤ紭鍏堢骇
        {
          p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(-10*liveMinionCount)); 
          // legacy log removed for compiler compatibility
					// 甯冨悏鑸炰箰鍑虹墝浼樺厛绾ф彁楂?
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_318, new Modifier(800));

        }else{
          	p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_716, new Modifier(999)); 
				}
#endregion

#region 鎴堣础浣愬 VAC_955
        // 鎻愰珮浼樺厛绾?
        if(board.HasCardInHand(Card.Cards.VAC_955)
        // 涓夎垂涓旀湁浣滄垬鍔ㄥ憳 CORE_GVG_061
        &&board.HasCardInHand(Card.Cards.CORE_GVG_061)
        // 闅忎粠灏忎簬6
        &&board.MinionFriend.Count <=5
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_955, new Modifier(350));
          AddLog("鎴堣础浣愬350");
        }else{
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_955, new Modifier(-350));
        }

#endregion

#region VAC_955t 缇庡懗濂堕叒
			foreach(var card in board.Hand)
      {
        if(card.Template.Id == Card.Cards.VAC_955t)
        {
						var tag = quantityOverflowLava(card);
						// 濡傛灉褰撳墠鎵嬬墝鏁板皬浜庣瓑浜?,涓旀渶澶у洖鍚堟暟澶т簬绛変簬8,鎻愰珮浼樺厛绾?
						if (board.MinionFriend.Count<=4
						&& board.HasCardInHand(Card.Cards.VAC_955t))
						{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(-50*(10-HandCount)));
								AddLog("缇庡懗濂堕叒"+(-50*(10-HandCount)));
						}else{
								p.CastSpellsModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(999));
						}
					}
			}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_955t, new Modifier(1999));
#endregion

#region 纭竵 GAME_005
		// 涓€璐规椂,鎵嬩笂娌℃湁涓よ垂闅忎粠,浣嗘槸鏈変笁璐归殢浠?鍒欓檷浣?鍏朵粬鏃跺€欓殢鎰?
		if(board.MaxMana == 1
		&& board.HasCardInHand(Card.Cards.GAME_005) //纭竵
		&&!board.Hand.Exists(x=>x.CurrentCost==2&&x.Type == Card.CType.MINION)
		&&board.Hand.Exists(x=>x.CurrentCost==3&&(x.Type == Card.CType.MINION||x.Template.Id == Card.Cards.CORE_GVG_061))
		){
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(55));
		}else{
			p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-55));
		}

#endregion

#region 鎵撳嵃鍦轰笂闅忎粠id
foreach (var item in board.MinionFriend)
{
    AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}
// 鎵撳嵃鎵嬩笂鐨勫崱鐗宨d
foreach (var item in board.Hand)
{
		AddLog(item.Template.NameCN + ' ' + item.Template.Id);
}

#endregion


#region 灞曢鑼跺６ CORE_WON_142
 // 璁板綍褰撳墠闅忎粠绉嶇被鏁伴噺
    // AddLog($"褰撳墠闅忎粠绉嶇被鏁伴噺: {allMinionCount}");
if(board.HasCardInHand(Card.Cards.CORE_WON_142))
{
   
    
    // 鏍规嵁闅忎粠绉嶇被璋冩暣浼樺厛绾?
    if (allMinionCount >= 2)
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(-350));
        AddLog("灞曢鑼跺６ -350: 闅忎粠绉嶇被澶т簬3锛屾彁楂樹紭鍏堢骇");
    }
    else
    {
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(350));
        AddLog("灞曢鑼跺６ 350: 闅忎粠绉嶇被灏戜簬绛変簬3锛岄檷浣庝紭鍏堢骇");
    }
}
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_WON_142, new Modifier(16));
#endregion

#region TTN_860	鏃犱汉鏈烘媶瑙ｅ櫒
			if(board.HasCardInHand(Card.Cards.TTN_860)){
				 p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_860, new Modifier(200)); 
				 AddLog("鏃犱汉鏈烘媶瑙ｅ櫒 200");
			}
#endregion

#region 鐢诲笀鐨勭編寰?TOY_810
if (TheVirtuesofthePainter != null && board.WeaponFriend == null && minionNumber >= 2)
{
    // 鎻愰珮鐢诲笀鐨勭編寰风殑鍎厛绱?
    p.ComboModifier = new ComboSet(TheVirtuesofthePainter.Id);
    // legacy log removed for compiler compatibility
}

if (board.WeaponFriend != null && board.WeaponFriend.Template.Id == Card.Cards.TOY_810)
{
    // 濡傛灉宸茶鍌欑敾甯堢殑缇庡痉涓旀墜涓婃湁涔愬潧鐏炬槦鐜涘姞钀?
    if (!board.MinionFriend.Any(x => x.Template.Id == Card.Cards.JAM_036) 
        && board.HasCardInHand(Card.Cards.JAM_036))
    {
        // 绛夊緟鐜涘姞钀ㄥ嚭鍦哄墠涓嶆敾鍑?
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.TOY_810, new Modifier(999));
        AddLog("鎵嬩笂鏈変箰鍧涚伨鏄熺帥鍔犺惃锛岀瓑寰呭叾鍑哄満鍚庡啀鏀诲嚮");
    }
    else
    {
        // 鍑嗗鏀诲嚮
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.TOY_810, new Modifier(-99));
        AddLog("鐢诲笀鐨勭編寰?鍑嗗鏀诲嚮");
    }
}
#endregion

#region VAC_923t 鍦ｆ矙娉藉皵
		// 鎻愰珮鍦轰笂鍦ｆ矙娉藉皵鐨勪紭鍏堢骇
         if(board.HasCardOnBoard(Card.Cards.VAC_923t)
        ){
        p.LocationsModifiers.AddOrUpdate(Card.Cards.VAC_923t, new Modifier(-99));
            AddLog("鍦ｆ矙娉藉皵 -99");
        }
#endregion

#region VAC_923 鍦ｆ矙娉藉皵
			  if(board.HasCardOnBoard(Card.Cards.VAC_923)
        ){
					// 閬嶅巻鍦轰笂闅忎粠,濡傛灉琚喕缁?鍒欎笉鏀诲嚮
					foreach (var item in board.MinionFriend)
					{

						if(item.IsFrozen)
						{
							p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_923, new Modifier(9999));
							// legacy log removed for compiler compatibility
						}
        	}
				}
#endregion

#region VAC_440	娴峰叧鎵ф硶鑰?
		// 鎻愰珮浼樺厛绾?
            if(board.HasCardInHand(Card.Cards.VAC_440)
            ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_440, new Modifier(-150));
            AddLog("娴峰叧鎵ф硶鑰?-150");
            }
#endregion
#region WW_331t	铔囨补
		// 鍓嶆湡涓嶇敤铔囨补 
		// 濡傛灉鐗屽簱涓虹┖,涓嶄氦鏄撹泧娌?
		if(board.HasCardInHand(Card.Cards.WW_331t)
		&&board.FriendDeckCount<=2
		){
		p.TradeModifiers.AddOrUpdate(Card.Cards.WW_331t, new Modifier(999));
		// legacy log removed for compiler compatibility
		}
#endregion
#region WW_336	妫卞僵鍏夋潫
        	        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(1999)); 
                    // 鏁屾柟灏忎簬3琛€鐨勯殢浠庤秺澶?妫卞僵鍏夋潫瓒婁紭鍏堟斁
                    if(board.HasCardInHand(Card.Cards.WW_336)
                    &&enemyThreeHealthMinionCount>2
                    ){
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(-20*enemyThreeHealthMinionCount));
                    AddLog("妫卞僵鍏夋潫"+(-20*enemyThreeHealthMinionCount));
                    }else{
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_336, new Modifier(350));
										}
#endregion

#region TTN_908	鍗佸瓧鍐涘厜鐜?
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_908, new Modifier(9999)); 
        var crusaderAura = board.Hand.FirstOrDefault(x => x.Template.Id == Card.Cards.TTN_908);
			// 鍦轰笂闅忎粠澶т簬2鎻愰珮浼樺厛绾?
			 if(crusaderAura!=null
            &&(canAttackMinion>=2
            &&liveMinionCount>=2
            )
            ){
            // p.ComboModifier = new ComboSet(crusaderAura.Id); 
            p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TTN_908, new Modifier(-350));
							AddLog("鍗佸瓧鍐涘厜鐜?350");
            }
#endregion

#region 鏄熻埌
       	foreach(var c in board.MinionFriend)
			{
				//check for starship
				if(GetTag(c,Card.GAME_TAG.STARSHIP) == 1 && GetTag(c,Card.GAME_TAG.LAUNCHPAD) == 1)
				{
					int StarshipAtk = c.CurrentAtk;
					int StarshipHp = c.CurrentHealth;
					AddLog("鏄熻埌鏀诲嚮鍔?: " + StarshipAtk.ToString() + " / 鏄熻埌琛€閲?: " + StarshipHp.ToString());
					// 濡傛灉鏄熻埌鐨勬敾鍑诲姏鍔犻槻寰″姏灏忎簬10,鍒欎笉鍙戝皠
					if(StarshipAtk + StarshipHp < 10)
					{
						p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(-100));
						// legacy log removed for compiler compatibility
					}
					else
					{
						p.LocationsModifiers.AddOrUpdate(c.Id, new Modifier(-50));
						AddLog("鏄熻埌鍙戝皠"+c.Template.NameCN);
					}
				}
				
			}
#endregion

#region WW_051	鍐虫垬锛?
			// if(board.HasCardInHand(Card.Cards.WW_051)
      //       //  鍦轰笂娌℃湁濂囧埄浜氭柉璞崕鐗?000鍨?
      //       &&enemyAttack<4
      //       ){
      //       p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WW_051, new Modifier(999));
      //       AddLog("鍐虫垬锛?999");
      //       }
#endregion

#region TTN_907	鏄熺晫缈旈緳
// 鍦轰笂鏈塗TN_907	鏄熺晫缈旈緳 鎵嬬墝鏁板皬浜?,闄嶄綆TTN_907	鏄熺晫缈旈緳鏀诲嚮浼樺厛鍊?
            if(board.HasCardOnBoard(Card.Cards.TTN_907)
            &&HandCount<8
            ){
            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TTN_907, new Modifier(-999));
            // legacy log removed for compiler compatibility
            }
            // 濡傛灉鐗屽簱娌℃湁鐗?鍦轰笂鏈夋槦鐣岀繑榫?鎻愰珮閫佹帀鐨勪紭鍏堝€?
            if(board.HasCardOnBoard(Card.Cards.TTN_907)
            &&board.FriendDeckCount <= 2
            ){
            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TTN_907, new Modifier(-999));
            AddLog("鏄熺晫缈旈緳閫佹帀");
            }
#endregion

#region TOY_330t5 濂囧埄浜氭柉璞崕鐗?000鍨?
            p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(3999));
            // 涓嶉€?
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_330t5, new Modifier(350));
#endregion

#region TTN_858	缁村拰鑰呴樋绫冲浘鏂?
       	p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(3999)); 
       	p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(1999)); 
        // 濡傛灉鎴戞柟闅忎粠鐨勬敾鍑诲€煎姞涓?*鎴戞柟闅忎粠鐨勬暟閲忓ぇ浜庢晫鏂硅嫳闆勭殑鐢熷懡鍊?涓斿鏂规棤鍢茶,鎻愰珮娉板潶2鎶€鑳界殑浼樺厛绾?
        if(board.HasCardOnBoard(Card.Cards.TTN_858)
        &&myAttack+2*friendCount>BoardHelper.GetEnemyHealthAndArmor(board)
        &&!board.MinionEnemy.Exists(x => x.IsTaunt)
        ){
        p.ChoicesModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-350,2));
        AddLog("缁村拰鑰呴樋绫冲浘鏂€?鍙潃");
        }
        if(liveMinionCount >= 3
        &&canAttackMinion>=3
        ){
        p.ChoicesModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-350,2));
        // AddLog("缁村拰鑰呴樋绫冲浘鏂€?");
        }

        if(board.HasCardInHand(Card.Cards.TTN_858)
        &&liveMinionCount >= 3
        &&canAttackMinion>=3
        ){
            p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-150));
            AddLog("缁村拰鑰呴樋绫冲浘鏂?-150");
        }
        p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TTN_858, new Modifier(-150));
#endregion

#region 涔愬櫒鎶€甯?ETC_418
if (board.HasCardInHand(Card.Cards.ETC_418)) // 鎵嬩笂鏈変箰鍣ㄦ妧甯?
{
    // 寮哄埗涔愬潧鐏炬槦鐜涘姞钀ㄤ负鏈€楂樹紭鍏堢骇
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-9999)); 
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-9999)); 
    AddLog("涔愬櫒鎶€甯堜紭鍏堢骇鎻愬崌鑷虫渶楂?(-9999)");

    // 濡傛灉褰撳墠鎵嬬墝灏戜簬绛変簬5寮?
    if (board.Hand.Count <= 5)
    {
        // legacy log removed for compiler compatibility
    }

    // 濡傛灉瑁呭浜嗙敾甯堢殑缇庡痉锛岃繘涓€姝ヤ紭鍏堣€冭檻
    if (board.HasCardInHand(Card.Cards.TOY_810) || 
        (board.WeaponFriend != null && board.WeaponFriend.Template.Id == Card.Cards.TOY_810))
    {
        // legacy log removed for compiler compatibility
    }
}
#endregion

#region 鍋ヨ韩鑲屽櫒浜?YOG_525
        if(board.HasCardInHand(Card.Cards.YOG_525)
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOG_525, new Modifier(350)); 
          p.ForgeModifiers.AddOrUpdate(Card.Cards.YOG_525, new Modifier(-99));
          AddLog("鍋ヨ韩鑲屽櫒浜?200");
        }
#endregion

#region 鍋ヨ韩鑲屽櫒浜?YOG_525t
        if(board.HasCardInHand(Card.Cards.YOG_525t)
        &&board.Hand.Count>=3
        )
        {
          p.CastMinionsModifiers.AddOrUpdate(Card.Cards.YOG_525t, new Modifier(-99)); 
          AddLog("鍋ヨ韩鑲屽櫒浜?-99");
        }
#endregion

#region JAM_036 涔愬潧鐏炬槦鐜涘姞钀?浼樺寲
if (board.HasCardInHand(Card.Cards.JAM_036))
{
    // 寮哄埗涔愬潧鐏炬槦鐜涘姞钀ㄤ负鏈€楂樹紭鍏堢骇
    p.CastMinionsModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(-9999)); 
    p.PlayOrderModifiers.AddOrUpdate(Card.Cards.JAM_036, new Modifier(-9999)); 
    AddLog("涔愬潧鐏炬槦鐜涘姞钀ㄤ紭鍏堢骇鎻愬崌鑷虫渶楂?(-9999)");

    // 濡傛灉褰撳墠鎵嬬墝灏戜簬绛変簬5寮?
    if (board.Hand.Count <= 5)
    {
        // legacy log removed for compiler compatibility
    }

    // 濡傛灉瑁呭浜嗙敾甯堢殑缇庡痉锛岃繘涓€姝ヤ紭鍏堣€冭檻
    if (board.HasCardInHand(Card.Cards.TOY_810) || 
        (board.WeaponFriend != null && board.WeaponFriend.Template.Id == Card.Cards.TOY_810))
    {
        // legacy log removed for compiler compatibility
    }
}
#endregion

#region TTN_924 閿嬮碁
    //    鎵嬩笂娌℃湁涓€璐归殢浠?鎻愰珮浣跨敤浼樺厛绾?
        if(board.HasCardInHand(Card.Cards.TTN_924)
        &&board.Hand.Count(card => card.CurrentCost == 1) == 0
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TTN_924, new Modifier(-350));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TTN_924, new Modifier(-5));
        AddLog("閿嬮碁 -350");
        }
#endregion

#region 涔愬櫒鎶€甯?ETC_418
// 鎵嬩笂鏈変箰鍣ㄦ妧甯?ETC_418,鍦轰笂鏈夊彲鏀诲嚮鐨勯殢浠?鎻愰珮涔愬櫒鎶€甯堝鍙敾鍑婚殢浠庣殑浼樺厛绾?
        if(board.HasCardInHand(Card.Cards.ETC_418)
        &&board.MinionFriend.Count(card => card.CanAttack) > 0
        ){
            foreach (var item in board.MinionFriend)
            {
                if (item.CanAttack)
                {
                    p.CastSpellsModifiers.AddOrUpdate(Card.Cards.ETC_418, new Modifier(-99, item.Template.Id));
                    // legacy log removed for compiler compatibility
                }
            }
        }
#endregion

#region ETC_102 绌烘皵鍚変粬鎵?
// 濡傛灉鎵嬩笂鏈夋鍣?鎻愰珮浣跨敤浼樺厛绾?鍚﹀垯闄嶄綆浣跨敤浼樺厛绾?
        if(board.HasCardInHand(Card.Cards.ETC_102)
        &&board.WeaponFriend != null
        ){
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_102, new Modifier(-99));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.ETC_102, new Modifier(999));
        AddLog("绌烘皵鍚変粬鎵?99");
        }else{
        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.ETC_102, new Modifier(350));
        }
#endregion

#region 閲戝睘鎺㈡祴鍣?VAC_330
// 濡傛灉鎵嬮噷娌℃鍣?涓旀晫鏂规湁3琛€鍙婁互涓嬬殑闅忎粠,鍒欐彁楂樹娇鐢ㄤ紭鍏堢骇
        if(board.HasCardInHand(Card.Cards.VAC_330)
        &&board.WeaponFriend == null
        &&board.MinionEnemy.Exists(x => x.CurrentHealth <= 3)
        ){
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.VAC_330, new Modifier(-20));
        AddLog("閲戝睘鎺㈡祴鍣?-20");
        }
    // 濡傛灉鎵嬩笂鏈夋鍣?鏁屾柟娌℃湁闅忎粠,闄嶄綆姝﹀櫒鏀诲嚮鏁屾柟鑻遍泟鐨勪紭鍏堝€?
    if(board.MinionEnemy.Count == 0
        &&board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.VAC_330
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.VAC_330, 999, board.HeroEnemy.Id);
        // legacy log removed for compiler compatibility
    }
        if(board.MinionEnemy.Exists(x => x.CurrentHealth <= 3)
        &&board.WeaponFriend != null 
        && board.WeaponFriend.Template.Id == Card.Cards.VAC_330
        ){
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.VAC_330, -99);
        // legacy log removed for compiler compatibility
        }
#endregion

#region YOG_509 瀹堟姢鑰呯殑鍔涢噺 
        if(board.HasCardInHand(Card.Cards.YOG_509)
        ){
        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.YOG_509, new Modifier(350));
        // AddLog("瀹堟姢鑰呯殑鍔涢噺 350");
        }
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.YOG_509, new Modifier(-999));
#endregion

#region TOY_813 鐜╁叿闃熼暱濉旀灄濮?
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_813, new Modifier(1000));
				// 濡傛灉鎵嬩笂鏈夌帺鍏烽槦闀垮鏋楀,閬嶅巻鏁屾柟鍦轰笂闅忎粠,闄嶄綆瀵瑰叾浣跨敤鐨勪紭鍏堢骇
				if(board.HasCardInHand(Card.Cards.TOY_813)
				&&board.MinionEnemy.Count>0
				){
					foreach (var item in board.MinionEnemy)
					{
						p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_813, new Modifier(150, item.Template.Id));
						AddLog("鐜╁叿闃熼暱濉旀灄濮?150");
					}
				}
#endregion

#region WORK_003 鍋囨湡瑙勫垝 
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.WORK_003, new Modifier(1000));
				// 濡傛灉鎵嬬墝鏁板皬浜庣瓑浜?,鎻愰珮浣跨敤浼樺厛绾?
				if(board.HasCardInHand(Card.Cards.WORK_003)
				&&HandCount<=7
				// 鍦轰笂闅忎粠灏忎簬绛変簬4
				&&board.MinionFriend.Count<=4
				){
				p.CastSpellsModifiers.AddOrUpdate(Card.Cards.WORK_003, new Modifier(-150));
				AddLog("鍋囨湡瑙勫垝-150");
				}
#endregion

#region 涓嶉€佺櫧閾舵柊鎵?CS2_101t
// 浣跨敤瀵硅薄鍒ゆ柇閫昏緫
var silverHandProtector = new SilverHandProtector(board);
if (silverHandProtector.ShouldProtect())
{
    silverHandProtector.ProtectMinions(p);
		// 閬嶅巻鍦轰笂闅忎粠,闄嶄綆璧烽€佹浼樺厛绾?
		foreach (var item in board.MinionFriend)
		{
				p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(item.Template.Id, new Modifier(250));
				AddLog("涓嶉€? " + item.Template.NameCN + " 250");
		}
}
#endregion

#region 鎶曢檷
    // 鎴戞柟鍦烘敾灏忎簬5,鎵嬬墝鏁板皬浜庣瓑浜?,鐗屽簱涓虹┖,鏁屾柟琛€閲?鎶ょ敳>20,褰撳墠璐圭敤澶т簬10,鎶曢檷
    if (HandCount<=1
        &&board.FriendDeckCount==0
        &&board.MaxMana>=10
        // 鏁屾柟琛€閲忓ぇ浜?0 
        &&BoardHelper.GetEnemyHealthAndArmor(board)>=20
        )
    {
        Bot.Concede();
        AddLog("鎶曢檷");
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

#region 閲嶆柊鎬濊€?
// 瀹氫箟闇€瑕佹帓闄ょ殑鍗＄墝闆嗗悎

var excludedCards = new HashSet<string>
{
    Card.Cards.GAME_005.ToString(),
		// 铔奔鎸戞垬鑰?TLC_251
		Card.Cards.TLC_251.ToString()
};
foreach (var card in board.Hand)
{
    if (!excludedCards.Contains(card.Template.Id.ToString()))
    {
        p.ForcedResimulationCardList.Add(card.Template.Id);
    }
}


// 閫昏緫鍚庣画 - 閲嶆柊鎬濊€?
// if (p.ForcedResimulationCardList.Any())
// {
    // 濡傛灉鏀寔鏃ュ織绯荤粺锛屼娇鐢ㄦ鏋剁殑鏃ュ織璁板綍鏂规硶
    // AddLog("闇€瑕侀噸鏂拌瘎浼扮瓥鐣ョ殑鍗＄墝鏁? " + p.ForcedResimulationCardList.Count);

    // 濡傛灉娌℃湁鏃ュ織绯荤粺锛屽彲浠ョ洿鎺ュ鐞嗙瓥鐣?
    // 鏇夸唬鍏蜂綋鐨勯噸鏂版€濊€冮€昏緫
// }
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


//寰凤細DRUID 鐚庯細HUNTER 娉曪細MAGE 楠戯細PALADIN 鐗э細PRIEST 璐硷細ROGUE 钀細SHAMAN 鏈細WARLOCK 鎴橈細WARRIOR 鐬庯細DEMONHUNTER 姝伙細DEATHKNIGHT
            ApplyLiveMemoryBiasCompat(board, p);
            return p;
        }}
				 // 鍚?_log 瀛楃涓叉坊鍔犳棩蹇楃殑绉佹湁鏂规硶锛屽寘鎷洖杞﹀拰鏂拌
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
								// 濡傛灉鎴戞柟闅忎粠鏁伴噺澶т簬鏁屾柟闅忎粠鏁伴噺锛屼笖鎵嬩笂鏈夊崄瀛楀啗鍏夌幆鎴栬€呭厜閫熸姠璐?
								return (_board.HasCardInHand(Card.Cards.TTN_908) || _board.HasCardInHand(Card.Cards.TOY_716)) &&
											_silverHandCards.Any(card => _board.HasCardOnBoard(card));
						}
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
public int CountSpecificRacesInHand(Board board)
{
    if (board?.MinionFriend == null) return 0; // 妫€鏌ユ墜鐗屾槸鍚︿负 null

    // 瀹氫箟鎵€鏈夊彲鑳界殑绉嶆棌
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
        if (card?.Type != Card.CType.MINION) continue; // 蹇界暐绌哄崱鎴栭潪闅忎粠鍗?

        foreach (Card.CRace race in races)
        {
           
            if (card.IsRace(race))
            {
                uniqueRaces.Add(race);
                break; // 纭繚姣忓紶鍗″彧娣诲姞涓€涓鏃?
            }
        }
    }

    return uniqueRaces.Count;
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
                    else if (board.Secret.Count == 0)
                    {
                        ret.Add(card1.Template.Id);
                    }
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
                    //浠ュ弸鏂归殢浠庢敾鍑诲姏 闄嶅簭鎺掑簭 鐨?鍦轰笂鐨勬墍鏈夊弸鏂归殢浠庨泦鍚堬紝濡傛灉璇ラ泦鍚堝瓨鍦ㄧ敓鍛藉€煎ぇ浜庝笌鏁屾柟闅忎粠鏀诲嚮鍔?
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
        private int CalculateAggroModifier(double baseAggro, int baseValue, Card.CClass enemyClass)
        {
            double winRateModifier = GetWinRateModifier(enemyClass);
            double usageRateModifier = GetUsageRateModifier(enemyClass);
            int finalAggro = (int)(baseAggro * 0.625 + baseValue + winRateModifier + usageRateModifier);
            AddLog("鑱锋キ: " + enemyClass + ", 鏀绘搳鍊? " + finalAggro + ", 鍕濈巼淇: " + winRateModifier + ", 浣跨敤鐜囦慨姝? " + usageRateModifier);
            return finalAggro;
        }

        private double GetWinRateModifier(Card.CClass enemyClass)
        {
            return (GetWinRateFromData(enemyClass) - 50) * 1.5;
        }

        private double GetUsageRateModifier(Card.CClass enemyClass)
        {
            return GetUsageRateFromData(enemyClass) * 0.5;
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
