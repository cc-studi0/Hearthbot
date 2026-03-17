using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    public class GameReader
    {
        private ReflectionContext _ctx;

        private bool Init()
        {
            if (_ctx != null && _ctx.IsReady) return true;
            _ctx = ReflectionContext.Instance;
            return _ctx.Init();
        }

        public GameStateData ReadGameState()
        {
            if (!Init()) return null;

            try
            {
                var gameState = _ctx.CallStaticAny(_ctx.GameStateType, "Get");
                if (gameState == null) return null;

                var friendly = GetFriendlyPlayer(gameState);
                var opposing = GetOpposingPlayer(gameState);
                if (friendly == null || opposing == null) return null;

                var data = new GameStateData();
                ReadPlayerData(friendly, data, true);
                ReadPlayerData(opposing, data, false);
                ReadMana(friendly, opposing, data);
                ReadTurnInfo(gameState, friendly, data);
                ReadExtendedFields(gameState, friendly, opposing, data);
                return data;
            }
            catch
            {
                return null;
            }
        }

        private object GetFriendlyPlayer(object gameState)
        {
            return _ctx.CallAny(gameState,
                "GetFriendlySidePlayer",
                "GetFriendlyPlayer",
                "GetLocalPlayer");
        }

        private object GetOpposingPlayer(object gameState)
        {
            return _ctx.CallAny(gameState,
                "GetOpposingSidePlayer",
                "GetOpposingPlayer",
                "GetOpponentPlayer");
        }

        private object GetCurrentPlayer(object gameState)
        {
            return _ctx.CallAny(gameState, "GetCurrentPlayer", "GetActivePlayer");
        }

        private void ReadPlayerData(object player, GameStateData data, bool isFriendly)
        {
            var hero = _ctx.CallAny(player, "GetHero");
            if (hero != null)
            {
                var heroData = ReadEntity(hero);
                if (isFriendly) data.HeroFriend = heroData;
                else data.HeroEnemy = heroData;

                if (_ctx.GameTagType != null)
                {
                    var heroClass = _ctx.GetTagValue(hero, "CLASS");
                    if (isFriendly) data.FriendClass = heroClass;
                    else data.EnemyClass = heroClass;
                }
            }

            var heroPower = _ctx.CallAny(player, "GetHeroPower");
            if (heroPower != null)
            {
                var hpData = ReadEntity(heroPower);
                if (isFriendly) data.AbilityFriend = hpData;
                else data.AbilityEnemy = hpData;
            }

            var weaponCard = _ctx.CallAny(player, "GetWeaponCard", "GetWeapon");
            if (weaponCard != null)
            {
                var weaponEntity = GetEntity(weaponCard);
                if (weaponEntity != null)
                {
                    var weaponData = ReadEntity(weaponEntity);
                    if (isFriendly) data.WeaponFriend = weaponData;
                    else data.WeaponEnemy = weaponData;
                }
            }

            var battlefield = _ctx.CallAny(player,
                "GetBattlefieldZone",
                "GetBattlefield",
                "BattlefieldZone",
                "PlayZone",
                "m_playZone");
            if (battlefield != null)
            {
                var cards = _ctx.CallGetCards(battlefield);
                var list = cards.Select(c => ReadEntity(GetEntity(c))).Where(e => e != null).ToList();
                if (isFriendly) data.MinionFriend = list;
                else data.MinionEnemy = list;
            }

            if (isFriendly)
            {
                // 在读取手牌前先确保 FriendlyPlayerId 已就绪
                // （ReadTurnInfo 在 ReadPlayerData 之后才执行，此处需提前解析）
                if (data.FriendlyPlayerId <= 0)
                    data.FriendlyPlayerId = ResolvePlayerId(player);

                // 优先通过 EntityMap + Tag 过滤读取手牌（线程安全，不依赖 ZoneMgr UI 单例）
                data.Hand = ReadHandFromEntityMap(data.FriendlyPlayerId);

                // 回退：如果 EntityMap 方式返回空，尝试旧的 ZoneMgr 方式
                if (data.Hand == null || data.Hand.Count == 0)
                {
                    var hand = _ctx.CallAny(player,
                        "GetHandZone",
                        "GetHand",
                        "HandZone",
                        "m_handZone");
                    if (hand != null)
                    {
                        var cards = _ctx.CallGetCards(hand);
                        data.Hand = cards.Select(c => ReadEntity(GetEntity(c))).Where(e => e != null).ToList();
                    }
                }
            }

            var secretZone = _ctx.CallAny(player,
                "GetSecretZone",
                "GetSecretsZone",
                "SecretZone",
                "m_secretZone");
            if (secretZone != null)
            {
                var cards = _ctx.CallGetCards(secretZone);
                var ids = cards.Select(c => GetCardId(GetEntity(c))).Where(id => id != null).ToList();
                if (isFriendly) data.SecretsFriend = ids;
                else data.EnemySecretCount = ids.Count;
            }

            if (isFriendly)
            {
                var graveyard = _ctx.CallAny(player,
                    "GetGraveyardZone",
                    "GetGraveyard",
                    "GraveyardZone",
                    "m_graveyardZone");
                if (graveyard != null)
                {
                    ReadGraveyardData(graveyard, out var ids, out var turns);
                    data.GraveyardFriend = ids;
                    data.GraveyardFriendTurn = turns;
                }
            }
            else
            {
                var graveyard = _ctx.CallAny(player,
                    "GetGraveyardZone",
                    "GetGraveyard",
                    "GraveyardZone",
                    "m_graveyardZone");
                if (graveyard != null)
                {
                    ReadGraveyardData(graveyard, out var ids, out var turns);
                    data.GraveyardEnemy = ids;
                    data.GraveyardEnemyTurn = turns;
                }
            }

            var deck = _ctx.CallAny(player,
                "GetDeckZone",
                "GetDeck",
                "DeckZone",
                "m_deckZone");
            if (deck != null)
            {
                var count = _ctx.ToInt(_ctx.CallAny(deck, "GetCardCount", "Count"));
                if (isFriendly)
                {
                    data.FriendDeckCount = count;
                    // 读取牌库中每张牌的 CardId（友方牌库可见）
                    try
                    {
                        var deckCards = _ctx.CallGetCards(deck);
                        foreach (var card in deckCards)
                        {
                            var entity = GetEntity(card);
                            var cardId = GetCardId(entity);
                            if (!string.IsNullOrWhiteSpace(cardId))
                                data.FriendDeck.Add(cardId);
                        }
                    }
                    catch { /* 读取失败时保持空列表 */ }
                }
                else
                {
                    data.EnemyDeckCount = count;
                }
            }

            if (!isFriendly)
            {
                var hand = _ctx.CallAny(player,
                    "GetHandZone",
                    "GetHand",
                    "HandZone",
                    "m_handZone");
                if (hand != null)
                {
                    data.EnemyHandCount = _ctx.ToInt(_ctx.CallAny(hand, "GetCardCount", "Count"));
                }
            }
        }

        private void ReadMana(object friendlyPlayer, object opposingPlayer, GameStateData data)
        {
            try
            {
                if (_ctx.GameTagType == null) return;

                var friendlyHero = _ctx.CallAny(friendlyPlayer, "GetHero");
                var opposingHero = _ctx.CallAny(opposingPlayer, "GetHero");
                var friendlyEntity = GetPlayerEntity(friendlyPlayer);
                var opposingEntity = GetPlayerEntity(opposingPlayer);
                var friendlySources = new[] { friendlyEntity, friendlyPlayer, friendlyHero };
                var opposingSources = new[] { opposingEntity, opposingPlayer, opposingHero };

                data.MaxMana = ReadPositiveTagValue("RESOURCES", friendlySources);
                if (data.MaxMana <= 0 && TryReadIntFromSources(
                        out var maxByMethod,
                        friendlySources,
                        "GetNumResources",
                        "GetMaxMana",
                        "GetManaCrystalCount",
                        "GetResourceCount"))
                {
                    data.MaxMana = Math.Max(0, maxByMethod);
                }

                var used = ReadPositiveTagValue("RESOURCES_USED", friendlySources);
                if (TryReadIntFromSources(
                        out var usedByMethod,
                        friendlySources,
                        "GetUsedResources",
                        "GetResourcesUsed",
                        "GetSpentMana",
                        "GetUsedMana"))
                {
                    used = Math.Max(0, usedByMethod);
                }

                data.Overload = ReadPositiveTagValue("OVERLOAD_OWED", friendlySources);
                if (TryReadIntFromSources(
                        out var overloadByMethod,
                        friendlySources,
                        "GetOverloadOwed",
                        "GetOverload",
                        "GetOverloadedMana"))
                {
                    data.Overload = Math.Max(0, overloadByMethod);
                }

                data.LockedMana = ReadPositiveTagValue("OVERLOAD_LOCKED", friendlySources);
                if (TryReadIntFromSources(
                        out var lockedByMethod,
                        friendlySources,
                        "GetLockedMana",
                        "GetOverloadLocked",
                        "GetLockedResources"))
                {
                    data.LockedMana = Math.Max(0, lockedByMethod);
                }

                data.EnemyMaxMana = ReadPositiveTagValue("RESOURCES", opposingSources);
                if (data.EnemyMaxMana <= 0 && TryReadIntFromSources(
                        out var enemyMaxByMethod,
                        opposingSources,
                        "GetNumResources",
                        "GetMaxMana",
                        "GetManaCrystalCount",
                        "GetResourceCount"))
                {
                    data.EnemyMaxMana = Math.Max(0, enemyMaxByMethod);
                }

                if (TryReadIntFromSources(
                        out var availableByMethod,
                        friendlySources,
                        "GetNumAvailableResources",
                        "GetAvailableMana",
                        "GetRemainingMana",
                        "GetSpendableMana",
                        "GetManaAvailable",
                        "GetCurrentMana"))
                {
                    data.ManaAvailable = Math.Max(0, availableByMethod);
                }
                else
                {
                    var computed = data.MaxMana - used - data.LockedMana;
                    if (computed < 0)
                        computed = data.MaxMana - used;
                    data.ManaAvailable = Math.Max(0, computed);
                }
            }
            catch
            {
            }
        }

        private object GetPlayerEntity(object player)
        {
            if (player == null) return null;

            var entity = _ctx.CallAny(player, "GetEntity", "GetGameEntity", "GetPlayerEntity");
            if (entity != null) return entity;

            return _ctx.GetFieldOrPropertyAny(player,
                "Entity",
                "GameEntity",
                "PlayerEntity",
                "m_entity",
                "m_gameEntity");
        }

        private int ReadPositiveTagValue(string tagName, params object[] sources)
        {
            if (sources == null || sources.Length == 0) return 0;

            foreach (var source in sources)
            {
                var value = _ctx.GetTagValue(source, tagName);
                if (value > 0) return value;
            }

            return 0;
        }

        private bool TryReadIntFromSources(out int value, object[] sources, params string[] memberNames)
        {
            value = 0;
            if (sources == null || sources.Length == 0 || memberNames == null || memberNames.Length == 0)
                return false;

            foreach (var source in sources)
            {
                if (source == null) continue;

                foreach (var memberName in memberNames)
                {
                    if (string.IsNullOrWhiteSpace(memberName)) continue;

                    var raw = _ctx.CallAny(source, memberName);
                    if (raw == null) continue;

                    value = _ctx.ToInt(raw);
                    return true;
                }
            }

            return false;
        }

        private bool TryReadBoolFromSource(object source, out bool value, params string[] memberNames)
        {
            value = false;
            if (source == null || memberNames == null || memberNames.Length == 0)
                return false;

            foreach (var memberName in memberNames)
            {
                if (string.IsNullOrWhiteSpace(memberName)) continue;

                var raw = _ctx.CallAny(source, memberName);
                if (raw == null) continue;

                if (raw is bool b)
                {
                    value = b;
                    return true;
                }

                if (bool.TryParse(raw.ToString(), out var parsed))
                {
                    value = parsed;
                    return true;
                }

                var i = _ctx.ToInt(raw);
                if (i == 0 || i == 1)
                {
                    value = i == 1;
                    return true;
                }
            }

            return false;
        }

        private bool DetectGameOver(object gameState, object friendly, object opposing, out string endScreenClass)
        {
            endScreenClass = string.Empty;

            try
            {
                if (TryReadBoolFromSource(gameState, out var isGameOverByState, "IsGameOver", "get_IsGameOver")
                    && isGameOverByState)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (TryGetEndGameScreenState(out var shown, out var className))
                {
                    endScreenClass = className ?? string.Empty;
                    if (shown) return true;
                }
            }
            catch
            {
            }

            try
            {
                var friendlyHero = _ctx.CallAny(friendly, "GetHero");
                var opposingHero = _ctx.CallAny(opposing, "GetHero");
                var friendlyPlaystate = _ctx.GetTagValue(friendlyHero, "PLAYSTATE");
                var opposingPlaystate = _ctx.GetTagValue(opposingHero, "PLAYSTATE");
                if (MapFriendlyPlaystateToResult(friendlyPlaystate) != GameResult.None
                    || MapEnemyPlaystateToResult(opposingPlaystate) != GameResult.None)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// 仅读取结算界面状态（不依赖完整对局状态对象）。
        /// 用于在 ReadGameState 暂时不可读时，避免把过渡帧误判成 NO_GAME。
        /// </summary>
        public bool IsEndGameScreenShown(out string className)
        {
            className = string.Empty;
            if (!Init()) return false;

            try
            {
                if (!TryGetEndGameScreenState(out var shown, out var rawClass))
                    return false;
                className = rawClass ?? string.Empty;
                return shown;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetEndGameScreenState(out bool shown, out string className)
        {
            shown = false;
            className = string.Empty;

            var type = _ctx.AsmCSharp?.GetType("EndGameScreen");
            if (type == null) return false;

            var screen = _ctx.CallStaticAny(type, "Get");
            if (screen == null) return false;

            var shownObj = _ctx.GetFieldOrPropertyAny(
                screen,
                "m_shown",
                "shown",
                "IsShown",
                "m_isShown");

            if (shownObj is bool b)
                shown = b;
            else
                shown = _ctx.ToInt(shownObj) == 1;

            className = _ctx.GetFieldOrPropertyAny(
                screen,
                "RealClassName",
                "m_realClassName",
                "ClassName",
                "m_className")?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(className))
            {
                className = _ctx.CallAny(screen, "GetRealClassName", "GetClassName")?.ToString() ?? string.Empty;
            }

            if (!shown && IsEndGameScreenProbablyVisible(screen, className))
                shown = true;

            return true;
        }

        private bool IsEndGameScreenProbablyVisible(object screen, string className)
        {
            if (screen == null)
                return false;

            if (!string.IsNullOrWhiteSpace(className) && IsObjectProbablyVisible(screen))
                return true;

            var candidates = new[]
            {
                _ctx.GetFieldOrPropertyAny(screen, "m_widget", "Widget", "m_root", "m_gameObject", "gameObject"),
                _ctx.GetFieldOrPropertyAny(screen, "m_battlegroundsEndGame", "m_battlegroundsPanel", "m_battlegroundsResults", "m_summaryDisplay"),
                _ctx.GetFieldOrPropertyAny(screen, "m_placeText", "m_placementText", "m_placeLabel", "m_placementLabel"),
                _ctx.GetFieldOrPropertyAny(screen, "m_rankText", "m_rankLabel", "m_resultText")
            };

            return candidates.Any(IsObjectProbablyVisible);
        }

        private bool IsObjectProbablyVisible(object obj)
        {
            if (obj == null)
                return false;

            foreach (var name in new[] { "m_shown", "shown", "IsShown", "m_isShown", "m_visible", "visible", "isVisible" })
            {
                if (TryGetBoolLike(obj, name, out var value) && !value)
                    return false;
            }

            var gameObject = _ctx.GetFieldOrPropertyAny(obj, "gameObject", "GameObject", "m_gameObject", "m_root", "m_RootObject");
            if (gameObject != null)
            {
                foreach (var name in new[] { "activeSelf", "activeInHierarchy" })
                {
                    if (TryGetBoolLike(gameObject, name, out var active) && !active)
                        return false;
                }
            }

            return true;
        }

        private bool TryGetBoolLike(object obj, string name, out bool value)
        {
            value = false;
            if (obj == null || string.IsNullOrWhiteSpace(name))
                return false;

            var raw = _ctx.GetFieldOrPropertyAny(obj, name);
            if (raw is bool fieldBool)
            {
                value = fieldBool;
                return true;
            }

            var called = _ctx.CallAny(obj, name);
            if (called is bool methodBool)
            {
                value = methodBool;
                return true;
            }

            return false;
        }

        private GameResult ResolveGameResult(object friendly, object opposing, string endScreenClass)
        {
            if (!string.IsNullOrWhiteSpace(endScreenClass))
            {
                var lower = endScreenClass.ToLowerInvariant();
                if (lower.Contains("victory")) return GameResult.Win;
                if (lower.Contains("defeat")) return GameResult.Loss;
                if (lower.Contains("tie") || lower.Contains("draw")) return GameResult.Tie;
            }

            var friendlyHero = _ctx.CallAny(friendly, "GetHero");
            var friendlyHeroResult = MapFriendlyPlaystateToResult(_ctx.GetTagValue(friendlyHero, "PLAYSTATE"));
            if (friendlyHeroResult != GameResult.None) return friendlyHeroResult;

            var friendlyEntity = GetPlayerEntity(friendly);
            var friendlyEntityResult = MapFriendlyPlaystateToResult(_ctx.GetTagValue(friendlyEntity, "PLAYSTATE"));
            if (friendlyEntityResult != GameResult.None) return friendlyEntityResult;

            var opposingHero = _ctx.CallAny(opposing, "GetHero");
            var opposingHeroResult = MapEnemyPlaystateToResult(_ctx.GetTagValue(opposingHero, "PLAYSTATE"));
            if (opposingHeroResult != GameResult.None) return opposingHeroResult;

            var opposingEntity = GetPlayerEntity(opposing);
            var opposingEntityResult = MapEnemyPlaystateToResult(_ctx.GetTagValue(opposingEntity, "PLAYSTATE"));
            if (opposingEntityResult != GameResult.None) return opposingEntityResult;

            return GameResult.None;
        }

        private static GameResult MapFriendlyPlaystateToResult(int playstate)
        {
            switch (playstate)
            {
                case 4: return GameResult.Win;   // WON
                case 5: return GameResult.Loss;  // LOST
                case 6: return GameResult.Tie;   // TIED
                case 7: return GameResult.Loss;  // DISCONNECTED
                case 8: return GameResult.Loss;  // CONCEDED
                default: return GameResult.None;
            }
        }

        private static GameResult MapEnemyPlaystateToResult(int playstate)
        {
            switch (playstate)
            {
                case 4: return GameResult.Loss;  // WON (opponent won = we lost)
                case 5: return GameResult.Win;   // LOST (opponent lost = we won)
                case 6: return GameResult.Tie;   // TIED
                case 7: return GameResult.Win;   // DISCONNECTED (opponent DC = we won)
                case 8: return GameResult.Win;   // CONCEDED (opponent conceded = we won)
                default: return GameResult.None;
            }
        }

        private void ReadTurnInfo(object gameState, object friendlyPlayer, GameStateData data)
        {
            data.IsOurTurn = false;
            data.IsMulliganPhase = false;
            data.FriendlyMulliganState = 0;

            try
            {
                var gameEntity = _ctx.CallAny(gameState, "GetGameEntity");
                if (gameEntity != null && _ctx.GameTagType != null)
                {
                    data.TurnCount = _ctx.GetTagValue(gameEntity, "TURN");
                    data.Step = _ctx.GetTagValue(gameEntity, "STEP");
                    data.CurrentPlayerId = _ctx.GetTagValue(gameEntity, "CURRENT_PLAYER");
                    data.FriendlyPlayerId = ResolvePlayerId(friendlyPlayer);
                    data.IsMulliganPhase = IsMulliganStep(data.Step);
                }

                var friendlyEntity = GetPlayerEntity(friendlyPlayer);
                if (friendlyEntity != null && _ctx.GameTagType != null)
                {
                    data.FriendlyMulliganState = _ctx.GetTagValue(friendlyEntity, "MULLIGAN_STATE");
                    if (IsMulliganStateActive(data.FriendlyMulliganState))
                        data.IsMulliganPhase = true;
                }

                var currentPlayer = GetCurrentPlayer(gameState);
                if (data.CurrentPlayerId <= 0)
                    data.CurrentPlayerId = ResolvePlayerId(currentPlayer);
                if (data.FriendlyPlayerId <= 0)
                    data.FriendlyPlayerId = ResolvePlayerId(friendlyPlayer);

                var samePlayer = false;
                if (data.CurrentPlayerId > 0 && data.FriendlyPlayerId > 0)
                    samePlayer = data.CurrentPlayerId == data.FriendlyPlayerId;
                else if (currentPlayer != null && friendlyPlayer != null)
                    samePlayer = IsSameEntity(currentPlayer, friendlyPlayer);

                if (!samePlayer) return;

                var stepDecision = IsActionStep(data.Step);
                if (stepDecision.HasValue)
                {
                    data.IsOurTurn = stepDecision.Value;
                    return;
                }

                data.IsOurTurn = !data.IsMulliganPhase;
            }
            catch
            {
            }
        }

        private void ReadExtendedFields(object gameState, object friendly, object opposing, GameStateData data)
        {
            try
            {
                if (_ctx.GameTagType == null)
                {
                    data.IsGameOver = DetectGameOver(gameState, friendly, opposing, out var onlyEndScreenClass);
                    data.EndGameScreenClass = onlyEndScreenClass ?? string.Empty;
                    data.Result = ResolveGameResult(friendly, opposing, data.EndGameScreenClass);
                    return;
                }

                var friendlyEntity = GetPlayerEntity(friendly);
                var opposingEntity = GetPlayerEntity(opposing);

                // 疲劳
                data.FriendFatigue = _ctx.GetTagValue(friendlyEntity, "FATIGUE");
                data.EnemyFatigue = _ctx.GetTagValue(opposingEntity, "FATIGUE");

                // 连击
                data.CardsPlayedThisTurn = _ctx.GetTagValue(friendlyEntity, "NUM_CARDS_PLAYED_THIS_TURN");
                data.IsCombo = data.CardsPlayedThisTurn > 0;

                // 克苏恩
                data.CthunAttack = _ctx.GetTagValue(friendlyEntity, "CTHUN_ATTACK_BUFF");
                data.CthunHealth = _ctx.GetTagValue(friendlyEntity, "CTHUN_HEALTH_BUFF");
                data.CthunTaunt = _ctx.GetTagValue(friendlyEntity, "CTHUN_TAUNT") == 1;

                // 青玉魔像
                data.JadeGolem = _ctx.GetTagValue(friendlyEntity, "JADE_GOLEM");
                data.JadeGolemEnemy = _ctx.GetTagValue(opposingEntity, "JADE_GOLEM");

                // 随从死亡数
                data.BaseMinionDiedThisTurnFriend = _ctx.GetTagValue(friendlyEntity, "NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_TURN");
                data.BaseMinionDiedThisTurnEnemy = _ctx.GetTagValue(opposingEntity, "NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_TURN");

                // 英雄技能使用次数
                data.HeroPowerCountThisTurn = _ctx.GetTagValue(friendlyEntity, "HEROPOWER_ACTIVATIONS_THIS_TURN");

                // 元素相关
                data.ElemPlayedLastTurn = _ctx.GetTagValue(friendlyEntity, "NUM_ELEMENTALS_PLAYED_LAST_TURN");
                data.ElemBuffEnabled = data.ElemPlayedLastTurn > 0;

                // 特殊效果
                data.SpellsCostHealth = _ctx.GetTagValue(friendlyEntity, "SPELLS_COST_HEALTH") == 1;
                data.EmbraceTheShadow = _ctx.GetTagValue(friendlyEntity, "EMBRACE_THE_SHADOW") == 1;
                data.LockAndLoad = _ctx.GetTagValue(friendlyEntity, "LOCK_AND_LOAD") == 1;
                data.Stampede = _ctx.GetTagValue(friendlyEntity, "STAMPEDE") == 1;

                // 额外回合
                data.IsExtraTurn = _ctx.GetTagValue(friendlyEntity, "EXTRA_TURN") == 1;
                data.ManaTemp = _ctx.GetTagValue(friendlyEntity, "TEMP_RESOURCES");

                // 直接对局结束检测（优先 IsGameOver / EndGameScreen，再回退 PLAYSTATE）
                data.IsGameOver = DetectGameOver(gameState, friendly, opposing, out var endScreenClass);
                data.EndGameScreenClass = endScreenClass ?? string.Empty;
                data.Result = ResolveGameResult(friendly, opposing, data.EndGameScreenClass);

                // 检测我方是否主动投降（PLAYSTATE == CONCEDED(8)）
                try
                {
                    var friendlyHero = _ctx.CallAny(friendly, "GetHero");
                    var friendlyPlaystate = _ctx.GetTagValue(friendlyHero, "PLAYSTATE");
                    data.FriendlyConceded = friendlyPlaystate == 8; // TAG_PLAYSTATE.CONCEDED
                }
                catch { }

                // 统计数据
                ReadGameStats(friendlyEntity, opposingEntity, data);
            }
            catch
            {
            }
        }

        private void ReadGameStats(object friendlyEntity, object opposingEntity, GameStateData data)
        {
            data.HealAmountThisGame = _ctx.GetTagValue(friendlyEntity, "AMOUNT_HEALED_THIS_GAME");
            data.HeroPowerDamagesThisGame = _ctx.GetTagValue(friendlyEntity, "HERO_POWER_DAMAGE_THIS_GAME");
            data.NumFriendlyMinionsDiedThisGame = _ctx.GetTagValue(friendlyEntity, "NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_GAME");
            data.NumEnemyMinionsDiedThisGame = _ctx.GetTagValue(opposingEntity, "NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_GAME");
            data.NumSpellsCastThisGame = _ctx.GetTagValue(friendlyEntity, "NUM_SPELLS_PLAYED_THIS_GAME");
            data.NumWeaponsPlayedThisGame = _ctx.GetTagValue(friendlyEntity, "NUM_WEAPONS_PLAYED_THIS_GAME");
            data.NumHeroPowersThisGame = _ctx.GetTagValue(friendlyEntity, "NUM_HERO_POWER_DAMAGE_THIS_GAME");
            data.NumCardsDrawnThisTurn = _ctx.GetTagValue(friendlyEntity, "NUM_CARDS_DRAWN_THIS_TURN");
            data.NumCardsPlayedThisGame = _ctx.GetTagValue(friendlyEntity, "NUM_CARDS_PLAYED_THIS_GAME");
            data.NumSecretsPlayedThisGame = _ctx.GetTagValue(friendlyEntity, "NUM_SECRETS_PLAYED_THIS_GAME");
        }

        private bool? IsActionStep(int step)
        {
            if (step <= 0) return null;
            if (IsMulliganStep(step)) return false;

            var stepType = _ctx.AsmCSharp.GetType("TAG_STEP")
                ?? _ctx.AsmCSharp.GetType("STEP")
                ?? _ctx.AsmCSharp.GetType("Step");

            var mainAction = GetEnumValue(stepType, "MAIN_ACTION");
            var mainStart = GetEnumValue(stepType, "MAIN_START");
            var mainReady = GetEnumValue(stepType, "MAIN_READY");
            var mainCombat = GetEnumValue(stepType, "MAIN_COMBAT");
            var mainEnd = GetEnumValue(stepType, "MAIN_END");

            if (mainAction.HasValue || mainStart.HasValue || mainReady.HasValue)
            {
                return step == mainAction
                    || step == mainStart
                    || step == mainReady
                    || step == mainCombat
                    || step == mainEnd;
            }

            // fallback: most HS builds use 5~9 for playable turn phases.
            return step >= 5 && step <= 9;
        }

        private bool IsMulliganStep(int step)
        {
            if (step <= 0) return false;

            var stepType = _ctx.AsmCSharp.GetType("TAG_STEP")
                ?? _ctx.AsmCSharp.GetType("STEP")
                ?? _ctx.AsmCSharp.GetType("Step");

            var mulliganStep = GetEnumValue(stepType, "BEGIN_MULLIGAN");
            if (mulliganStep.HasValue)
                return step == mulliganStep.Value;

            // fallback: on most clients BEGIN_MULLIGAN is 4.
            return step == 4;
        }

        private bool IsMulliganStateActive(int state)
        {
            if (state <= 0) return false;

            var mulliganType = _ctx.AsmCSharp.GetType("TAG_MULLIGAN")
                ?? _ctx.AsmCSharp.GetType("MulliganState");

            var done = GetEnumValue(mulliganType, "DONE");
            var input = GetEnumValue(mulliganType, "INPUT");
            var dealing = GetEnumValue(mulliganType, "DEALING");
            var waiting = GetEnumValue(mulliganType, "WAITING");
            var refreshing = GetEnumValue(mulliganType, "REFRESHING");
            var preRefreshing = GetEnumValue(mulliganType, "PREREFRESHING");

            if (done.HasValue && state == done.Value)
                return false;

            if (state == (input ?? 1)
                || state == (dealing ?? 2)
                || state == (waiting ?? 3)
                || state == (refreshing ?? 5)
                || state == (preRefreshing ?? 6))
            {
                return true;
            }

            // fallback: TAG_MULLIGAN.DONE is 4 on current clients.
            return state > 0 && state != 4;
        }

        private int? GetEnumValue(Type enumType, string name)
        {
            if (enumType == null || !enumType.IsEnum || string.IsNullOrEmpty(name))
                return null;

            try
            {
                var value = Enum.Parse(enumType, name, true);
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        private int ResolvePlayerId(object player)
        {
            if (player == null) return 0;

            var id = _ctx.ToInt(_ctx.CallAny(player, "GetPlayerID", "GetPlayerId", "GetID", "GetEntityID", "GetEntityId"));
            if (id > 0) return id;

            id = _ctx.ToInt(_ctx.GetFieldOrPropertyAny(player,
                "PlayerID", "PlayerId", "ID", "EntityID", "EntityId", "m_playerID", "m_playerId"));
            if (id > 0) return id;

            id = _ctx.GetTagValue(player, "PLAYER_ID");
            if (id > 0) return id;

            var entity = GetEntity(player);
            id = _ctx.GetTagValue(entity, "CONTROLLER");
            if (id > 0) return id;

            var hero = _ctx.CallAny(player, "GetHero");
            id = _ctx.GetTagValue(hero, "CONTROLLER");
            return id;
        }

        private bool IsSameEntity(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;

            var idA = ResolvePlayerId(a);
            var idB = ResolvePlayerId(b);
            if (idA > 0 && idB > 0)
                return idA == idB;

            return false;
        }

        private EntityData ReadEntity(object entity)
        {
            if (entity == null) return null;

            try
            {
                var data = new EntityData();
                data.CardId = GetCardId(entity);
                data.EntityId = ResolveEntityId(entity);
                data.Atk = _ctx.GetTagValue(entity, "ATK");
                data.Health = _ctx.GetTagValue(entity, "HEALTH");
                data.Damage = _ctx.GetTagValue(entity, "DAMAGE");
                data.Armor = _ctx.GetTagValue(entity, "ARMOR");
                data.Cost = _ctx.GetTagValue(entity, "COST");
                data.SpellPower = _ctx.GetTagValue(entity, "SPELLPOWER");
                data.Taunt = _ctx.GetTagValue(entity, "TAUNT") == 1;
                data.DivineShield = _ctx.GetTagValue(entity, "DIVINE_SHIELD") == 1;
                data.Charge = _ctx.GetTagValue(entity, "CHARGE") == 1;
                data.Windfury = _ctx.GetTagValue(entity, "WINDFURY") == 1;
                data.Stealth = _ctx.GetTagValue(entity, "STEALTH") == 1;
                data.Frozen = _ctx.GetTagValue(entity, "FROZEN") == 1;
                data.Silenced = _ctx.GetTagValue(entity, "SILENCED") == 1;
                data.Immune = _ctx.GetTagValue(entity, "IMMUNE") == 1;
                data.Poisonous = _ctx.GetTagValue(entity, "POISONOUS") == 1;
                data.Lifesteal = _ctx.GetTagValue(entity, "LIFESTEAL") == 1;
                data.Rush = _ctx.GetTagValue(entity, "RUSH") == 1;
                data.Reborn = _ctx.GetTagValue(entity, "REBORN") == 1;
                data.Exhausted = _ctx.GetTagValue(entity, "EXHAUSTED") == 1;
                data.NumTurnsInPlay = _ctx.GetTagValue(entity, "NUM_TURNS_IN_PLAY");
                data.AttackCount = _ctx.GetTagValue(entity, "NUM_ATTACKS_THIS_TURN");
                data.Durability = _ctx.GetTagValue(entity, "DURABILITY");
                data.ZonePosition = _ctx.GetTagValue(entity, "ZONE_POSITION");
                data.Freeze = _ctx.GetTagValue(entity, "FREEZE") == 1;

                // 补全字段
                data.TempAtk = _ctx.GetTagValue(entity, "TEMP_ATK");
                data.IsEnraged = _ctx.GetTagValue(entity, "ENRAGED") == 1;
                data.IsInspire = _ctx.GetTagValue(entity, "INSPIRE") == 1;
                data.IsTargetable = _ctx.GetTagValue(entity, "CANT_BE_TARGETED_BY_SPELLS") != 1;
                data.IsGenerated = _ctx.GetTagValue(entity, "CREATOR") > 0;
                data.CountPlayed = _ctx.GetTagValue(entity, "NUM_TURNS_IN_PLAY");
                data.IsPowered = _ctx.GetTagValue(entity, "POWERED_UP") == 1;
                data.CanAttackHeroes = _ctx.GetTagValue(entity, "CANNOT_ATTACK_HEROES") != 1;
                data.HasEcho = _ctx.GetTagValue(entity, "ECHO") == 1;
                data.IsCombo = _ctx.GetTagValue(entity, "COMBO") == 1;

                data.Tags = ReadAllTags(entity);
                return data;
            }
            catch
            {
                return null;
            }
        }

        private int ResolveEntityId(object source, int depth = 0)
        {
            if (source == null || depth > 2) return 0;

            try
            {
                var id = _ctx.GetTagValue(source, "ENTITY_ID");
                if (id > 0) return id;
            }
            catch
            {
            }

            try
            {
                var idObj = _ctx.CallAny(source, "GetEntityId", "GetEntityID", "GetID", "GetId");
                var id = _ctx.ToInt(idObj);
                if (id > 0) return id;
            }
            catch
            {
            }

            try
            {
                var idObj = _ctx.GetFieldOrPropertyAny(source,
                    "EntityID", "EntityId", "ID", "Id", "m_entityId", "m_id");
                var id = _ctx.ToInt(idObj);
                if (id > 0) return id;
            }
            catch
            {
            }

            // 某些版本卡牌对象不直接暴露 EntityId，需要先取到内部 Entity 再读取。
            try
            {
                var nested = _ctx.CallAny(source, "GetEntity")
                    ?? _ctx.GetFieldOrPropertyAny(source,
                        "Entity",
                        "m_entity",
                        "GameEntity",
                        "m_gameEntity");
                if (nested != null && !ReferenceEquals(nested, source))
                {
                    var nestedId = ResolveEntityId(nested, depth + 1);
                    if (nestedId > 0) return nestedId;
                }
            }
            catch
            {
            }

            return 0;
        }

        private void ReadGraveyardData(object graveyard, out List<string> ids, out List<int> turns)
        {
            ids = new List<string>();
            turns = new List<int>();
            var cards = _ctx.CallGetCards(graveyard);
            foreach (var c in cards)
            {
                var entity = GetEntity(c);
                var cardId = GetCardId(entity);
                if (cardId == null) continue;
                ids.Add(cardId);
                var turn = _ctx.GetTagValue(entity, "TAG_LAST_KNOWN_COST_IN_HAND");
                if (turn <= 0)
                    turn = _ctx.GetTagValue(entity, "NUM_TURNS_IN_PLAY");
                turns.Add(turn);
            }
        }

        private Dictionary<int, int> ReadAllTags(object entity)
        {
            var result = new Dictionary<int, int>();

            try
            {
                var getTags = entity.GetType().GetMethod("GetTags",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getTags == null) return result;

                var tags = getTags.Invoke(entity, null) as IEnumerable;
                if (tags == null) return result;

                foreach (var kv in tags)
                {
                    var key = _ctx.ToInt(_ctx.GetFieldOrPropertyAny(kv, "Key"));
                    var value = _ctx.ToInt(_ctx.GetFieldOrPropertyAny(kv, "Value"));
                    if (key != 0)
                        result[key] = value;
                }
            }
            catch
            {
            }

            return result;
        }

        private string GetCardId(object entity)
        {
            if (entity == null) return null;

            try
            {
                var value = _ctx.CallAny(entity, "GetCardId", "GetCardID");
                var id = value?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
            catch
            {
            }

            try
            {
                var value = _ctx.GetFieldOrPropertyAny(entity, "CardID", "CardId", "m_cardId", "m_cardID");
                var id = value?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
            catch
            {
            }

            try
            {
                var cardDef = _ctx.CallAny(entity, "GetCardDef", "GetCardData", "GetTemplate")
                    ?? _ctx.GetFieldOrPropertyAny(entity, "CardDef", "CardData", "Template", "m_cardDef", "m_cardData");
                if (cardDef != null)
                {
                    var value = _ctx.GetFieldOrPropertyAny(cardDef, "CardID", "CardId", "m_cardId", "m_id", "Id");
                    var id = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                        return id;
                }
            }
            catch
            {
            }

            try
            {
                var nestedCard = _ctx.CallAny(entity, "GetCard")
                    ?? _ctx.GetFieldOrPropertyAny(entity, "Card", "m_card");
                if (nestedCard != null && !ReferenceEquals(nestedCard, entity))
                    return GetCardId(nestedCard);
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// 通过 GameState.m_entityMap 遍历所有 Entity，
        /// 按 ZONE==HAND、CONTROLLER==friendlyPlayerId 过滤出友方手牌。
        /// 该方式不依赖 ZoneMgr / ZoneHand 等 Unity UI 对象，
        /// 从后台线程调用时更稳定。
        /// </summary>
        private List<EntityData> ReadHandFromEntityMap(int friendlyPlayerId)
        {
            if (friendlyPlayerId <= 0) return new List<EntityData>();

            // 发现（Discover）结算后，实体的 ZONE 标签需要较长时间才能从
            // SETASIDE(5) 完成到 HAND(3) 的切换。旧的 3×30ms（总 ≈90ms）窗口
            // 经常不够用，导致 Discover 后手牌读取持续为空。
            // 增加到 8×80ms（总 ≈640ms）以覆盖动画 + ZoneChangeList 结算。
            const int maxAttempts = 8;
            const int retryDelayMs = 80;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var result = ReadHandFromEntityMapOnce(friendlyPlayerId);
                if (result.Count > 0)
                    return result;

                if (attempt < maxAttempts - 1)
                    System.Threading.Thread.Sleep(retryDelayMs);
            }

            return new List<EntityData>();
        }

        private List<EntityData> ReadHandFromEntityMapOnce(int friendlyPlayerId)
        {
            var result = new List<EntityData>();
            try
            {
                var gameState = _ctx.CallStaticAny(_ctx.GameStateType, "Get");
                if (gameState == null) return result;

                var entities = _ctx.GetEntityMapEntries(gameState);
                foreach (var entity in entities)
                {
                    if (entity == null) continue;

                    // TAG_ZONE.HAND == 3
                    var zone = _ctx.GetTagValue(entity, "ZONE");
                    if (zone != 3) continue;

                    var controller = _ctx.GetTagValue(entity, "CONTROLLER");
                    if (controller != friendlyPlayerId) continue;

                    var data = ReadEntity(entity);
                    if (data != null)
                        result.Add(data);
                }

                // 按 ZonePosition 排序（手牌顺序）
                result.Sort((a, b) => a.ZonePosition.CompareTo(b.ZonePosition));
            }
            catch (System.Exception ex)
            {
                // 记录异常类型，帮助定位是并发修改还是其他问题
                System.Diagnostics.Debug.WriteLine(
                    $"[GameReader] ReadHandFromEntityMapOnce failed: {ex.GetType().Name}: {ex.Message}");
            }

            return result;
        }

        private object GetEntity(object card)
        {
            if (card == null) return null;
            var entity = _ctx.CallAny(card, "GetEntity")
                ?? _ctx.GetFieldOrPropertyAny(card,
                    "Entity",
                    "m_entity",
                    "GameEntity",
                    "m_gameEntity")
                ?? _ctx.CallAny(card, "GetGameEntity");
            return entity ?? card;
        }
    }
}
