# SmartBot 逆向工程文档

## 1. 架构概览

```
┌─────────────┐    DLL注入     ┌──────────────────┐
│  SB.exe     │ ──Loader.dll──>│  Hearthstone.exe │
│  (.NET 8.0) │                │  (Unity/Mono)    │
│  WPF主程序   │<──NamedPipe───│  注入的Payload   │
└─────────────┘   Seed协议      └──────────────────┘
       │
       ├── Profiles/*.cs    (策略脚本，Roslyn动态编译)
       ├── Mulligans/*.cs   (留牌脚本)
       ├── SBAPI.dll        (公共API定义)
       └── Loader.dll       (原生DLL注入器)
```

**运行时**: .NET 8.0 + Microsoft.WindowsDesktop.App 8.0.0 (WPF)

**通信方式**: NamedPipe，游戏内Payload读取棋盘状态序列化为Seed字符串，通过管道传给SB.exe

---

## 2. Loader.dll 接口

原生DLL，导出两个函数：

```csharp
// 注册日志回调
[DllImport("Loader.dll")]
static extern void SetLogCallback(LogCallback cb);

// 注入调用
[DllImport("Loader.dll")]
static extern void Call(byte[] bytes, int bytesize,
    string namespacename, string classname,
    string methodname, string p);

delegate void LogCallback(string message);
```

### 注入流程 (InjectProcess)

```
1. SetLogCallback(cb)          // 注册回调，监听 "exec : 0" 表示注入成功
2. Call(payload.bytes,          // 注入的.NET程序集字节
       payload.bytes.Length,
       payload.namespace,       // 目标命名空间
       payload.classname,       // 目标类名
       payload.methodname+"AA", // 目标方法名(加了AA后缀)
       "Hearthstone.exe")       // 目标进程
3. 等待最多60秒，检查 Injected 标志
```

**原理**: Loader.dll 在目标进程中启动 CLR 宿主，加载传入的 .NET 程序集并调用指定方法。

---

## 3. Seed 协议 (棋盘序列化)

Board 状态通过 `~` 分隔符序列化为字符串，共 66+ 个字段。

### Board.FromSeed() 字段索引

| 索引 | 字段 | 类型 | 说明 |
|------|------|------|------|
| 0 | MaxMana | int | 最大法力水晶 |
| 1 | ManaAvailable | int | 可用法力水晶 |
| 2 | ControllerId | int | 控制者ID |
| 3 | FriendCardCount | int | 友方牌库数量 |
| 4 | EnemyCardCount | int | 敌方手牌数量 |
| 5 | FriendClass | CClass | 友方职业 |
| 6 | EnemyClass | CClass | 敌方职业 |
| 7 | TurnCount | int | 回合数 |
| 8 | OpOverload | int | 对手过载 |
| 9 | Overload | int | 自己过载 |
| 10 | LockedMana | int | 锁定法力 |
| 11 | EnemyMaxMana | int | 敌方最大法力 |
| 12 | FriendDeckCount | int | 友方牌库数 |
| 13 | EnemyDeckCount | int | 敌方牌库数 |
| 14 | HeroFriend | Card | 友方英雄(Card序列化) |
| 15 | HeroEnemy | Card | 敌方英雄 |
| 16 | Ability | Card | 友方英雄技能 |
| 17 | EnemyAbility | Card | 敌方英雄技能 |
| 18 | MinionFriend | Card[] | 友方随从(用`\|`分隔) |
| 19 | MinionEnemy | Card[] | 敌方随从 |
| 20 | Hand | Card[] | 手牌 |
| 21 | Secret | Cards[] | 奥秘(用`\|`分隔的CardId) |
| 22 | FriendGraveyard | Cards[] | 友方墓地 |
| 23 | WeaponFriend | Card | 友方武器(空="yourweapon") |
| 24 | WeaponEnemy | Card | 敌方武器(空="yourweapon") |
| 25 | EnemySecret | int | 敌方奥秘数量 |
| 26 | TrapMgr | TrapManager | 陷阱管理器 |
| 27-37 | 各种标志 | bool/int | 连击、灵感等 |
| 38 | SpellsCostHealth | bool | 法术消耗生命 |
| 39-42 | 位置/地标 | Card[] | LocationFriend/Enemy等 |
| 43-46 | QuestFriendly | 任务相关 | 友方任务/奖励 |
| 47-50 | QuestEnemy | 任务相关 | 敌方任务/奖励 |
| 51-59 | 扩展字段 | 各种 | 锻造、签名等 |
| 60 | TagFriend | TagMap | 友方英雄标签 |
| 61 | TagEnemy | TagMap | 敌方英雄标签 |
| 62-64 | 扩展 | 各种 | 游客、旅行者等 |
| 65 | PlayedCards | Cards[] | 已打出的牌 |
| 66 | PlayedGeneratedCards | Cards[] | 已打出的衍生牌 |

---

## 4. Card 序列化 (CardFromStr)

单张卡牌通过 `*` 分隔符序列化，39+ 个字段。

### Card.CardFromStr() 字段索引

| 索引 | 字段 | 类型 | 说明 |
|------|------|------|------|
| 0 | CardId | Cards | 卡牌ID枚举 |
| 1 | Index | int | 位置索引 |
| 2 | CurrentArmor | int | 当前护甲 |
| 3 | CurrentAtk | int | 当前攻击力 |
| 4 | CurrentCost | int | 当前费用 |
| 5 | Damage | int | 已受伤害 |
| 6 | MaxDurability | int | 最大耐久 |
| 7 | EntityId | int | 实体ID |
| 8 | MaxHealth | int | 最大生命值 |
| 9 | CountAttack | int | 已攻击次数 |
| 10-11 | (未知) | | |
| 12 | SpellPower | int | 法术强度 |
| 13 | IsCharge | bool | 冲锋 |
| 14 | IsDivineShield | bool | 圣盾 |
| 15 | IsTaunt | bool | 嘲讽 |
| 16 | IsWindfury | bool | 风怒 |
| 17 | IsUsed | bool | 已使用 |
| 18 | (未知) | | |
| 19 | IsTired | bool | 疲劳(刚下场) |
| 20 | HasFreeze | bool | 具有冻结效果 |
| 21 | IsFrozen | bool | 被冻结 |
| 22 | IsImmune | bool | 免疫 |
| 23 | HasPoison | bool | 剧毒 |
| 24 | IsSilenced | bool | 被沉默 |
| 25 | IsStealth | bool | 潜行 |
| 26-30 | (未知) | | |
| 31 | IsLifeSteal | bool | 吸血 |
| 32 | HasRush | bool | 突袭 |
| 33 | IsPowered | bool | 已激活(灌注等) |
| 34 | !CanAttackHeroes | bool | 不能攻击英雄(取反) |
| 35 | HasEcho | bool | 回响 |
| 36 | (未知) | | |
| 37 | HasReborn | bool | 复生 |
| 38 | Tags | TagMap | GAME_TAG字典 |
| 39+ | Enchantments | list | 附魔列表 |

**计算属性**:
- `CurrentHealth = MaxHealth - Damage`
- `CanAttackHeroes = !array[34]`
- `CanAttack` = 根据 IsUsed/IsTired/IsFrozen/CountAttack 等计算

### 附魔格式
```
CardId=ControllerId=Count=DeathrattleCardId
```

---

## 5. TagMap 格式 (StringToTagMap)

GAME_TAG 字典序列化为：
```
tagId=value&tagId=value&...
```
其中 `tagId` 是 `GAME_TAG` 枚举的 int 值。

读取方式：
```csharp
int GetTag(Card c, Card.GAME_TAG tag) {
    if (c.tags != null && c.tags.ContainsKey(tag))
        return c.tags[tag];
    return -1;
}
```

---

## 6. SBAPI 核心接口

### 6.1 Board (棋盘状态)

```
Board
├── HeroFriend / HeroEnemy          : Card (英雄)
├── Ability / EnemyAbility          : Card (英雄技能)
├── MinionFriend / MinionEnemy      : List<Card> (场上随从)
├── Hand                            : List<Card> (手牌)
├── WeaponFriend / WeaponEnemy      : Card (武器)
├── Secret                          : List<Card.Cards> (奥秘)
├── FriendGraveyard                 : List<Card.Cards> (墓地)
├── ManaAvailable / MaxMana         : int (法力)
├── EnemyClass / FriendClass        : Card.CClass (职业)
├── EnemyCardCount                  : int (敌方手牌数)
├── FriendDeckCount / EnemyDeckCount: int (牌库数)
├── TurnCount                       : int (回合数)
├── HasCardInHand(Cards) / HasCardOnBoard(Cards) : bool
└── FromSeed(string) / ToSeed()     : 序列化
```

### 6.2 Card (实体)

```
Card
├── Template          : CardTemplate (卡牌模板)
│   ├── Id            : Card.Cards (枚举ID)
│   ├── Name / NameCN : string
│   ├── Cost / Atk / Health : int (原始属性)
│   └── IsSecret      : bool
├── CurrentHealth / CurrentAtk / CurrentCost : int (当前属性)
├── CurrentArmor      : int
├── Type              : Card.CType (MINION/SPELL/WEAPON等)
├── IsTaunt / IsSilenced / CanAttack / IsFrozen ... : bool
├── SpellPower        : int
├── tags              : Dictionary<GAME_TAG, int>
├── IsRace(CRace)     : bool (种族判断)
└── Enchantments      : List<Enchantment>
```

### 6.3 ProfileParameters (策略参数)

```
ProfileParameters(BaseProfile baseProfile)
├── GlobalAggroModifier              : int (全局攻击性)
├── GlobalDefenseModifier            : int (全局防御性)
├── GlobalDrawModifier               : int (抽牌倾向)
├── GlobalWeaponsAttackModifier      : int (武器攻击倾向)
├── GlobalCastSpellsModifier         : int (法术使用倾向)
├── GlobalCastMinionsModifier        : int (随从使用倾向)
├── CastSpellsModifiers              : ModifierDict (单卡法术修饰)
├── CastMinionsModifiers             : ModifierDict (单卡随从修饰)
├── CastHeroPowerModifier            : ModifierDict (英雄技能修饰)
├── CastWeaponsModifiers             : ModifierDict (武器装备修饰)
├── WeaponsAttackModifiers           : ModifierDict (武器攻击修饰)
├── OnBoardFriendlyMinionsValuesModifiers : ModifierDict (友方随从价值)
├── OnBoardBoardEnemyMinionsModifiers     : ModifierDict (敌方随从威胁)
├── PlayOrderModifiers               : ModifierDict (出牌顺序)
├── AttackOrderModifiers             : ModifierDict (攻击顺序)
├── LocationsModifiers               : ModifierDict (地标修饰)
├── ChoicesModifiers                 : ModifierDict (选择修饰)
├── ForcedResimulationCardList       : List<Cards> (强制重新模拟)
└── DiscoverSimulationValueThresholdPercent : int
```

**Modifier**: `new Modifier(value)` 或 `new Modifier(value, target)` — 正值降低优先级，负值提高优先级。

### 6.4 Profile (策略脚本接口)

```csharp
public class MyProfile : Profile
{
    // 核心方法：返回策略参数
    public ProfileParameters GetParameters(Board board) { ... }

    // 芬利选择英雄技能
    public Card.Cards SirFinleyChoice(List<Card.Cards> choices) { ... }

    // 卡扎库斯选择
    public Card.Cards KazakusChoice(List<Card.Cards> choices) { ... }
}
```

### 6.5 Plugin (插件接口)

```
Plugin
├── OnPluginCreated()       // 构造
├── OnTick()                // 每300ms调用
├── OnStarted/OnStopped()   // 机器人启停
├── OnGameBegin/OnGameEnd() // 对局开始/结束
├── OnTurnBegin/OnTurnEnd() // 回合开始/结束
├── OnVictory/OnDefeat()    // 胜利/失败
├── OnLethal()              // 发现斩杀
├── OnConcede()             // 投降
├── OnSimulation()          // 模拟开始
├── OnHandleMulligan()      // 留牌阶段
├── OnWhisperReceived()     // 收到密语
├── OnFriendRequestReceived() // 好友请求
└── OnActionExecute(Action) // 执行动作
```

### 6.6 Bot (控制器)

```
Bot
├── StartBot/StopBot/SuspendBot/ResumeBot()
├── CurrentBoard            : Board (当前棋盘)
├── CurrentMode/ChangeMode(Mode)
├── CurrentProfile/ChangeProfile(string)
├── CurrentDeck/ChangeDeck(string)
├── CurrentMulligan/ChangeMulligan(string)
├── Log(string)
├── Concede()
├── CloseHs/CloseBot()
├── GetQuests/GetPlayerDatas/GetFriends()
├── SendEmote(EmoteType)
└── Set系列(MaxWins/MaxLosses/MaxHours/...)
```

### 6.7 Action 类型

```
Action (基类)
├── AttackAction     // 攻击
├── PushAction       // 打出卡牌
├── TargetAction     // 指定目标
├── ChoiceAction     // 选择
├── EndTurnAction    // 结束回合
├── ConcedeAction    // 投降
├── TradeAction      // 交易卡牌
├── LocationAction   // 地标动作
└── ResimulateAction // 重新模拟
```

---

## 7. 反检测机制

### SmartBot 的 CHECK() 方法
```csharp
void CHECK() {
    // 检测炉石进程中是否加载了 libacsdk 模块(暴雪反作弊SDK)
    if (ACLIBLOADED()) {
        // 发现反作弊模块 → 立即重启炉石
        KillHearthstone();
        RestartHearthstone();
    }
}
```

### SmartBot 安全性优势
1. **运行时注入**: 不在游戏目录留下文件
2. **最小内存占用**: 注入的Payload只做状态读取和序列化
3. **主动反检测**: 检测 libacsdk 并重启
4. **无 Harmony Hook**: 不修改游戏方法，只读取数据
5. **NamedPipe 通信**: 进程间通信不易被检测

---

## 8. 动态编译

策略脚本(.cs)通过 Roslyn (Microsoft.CodeAnalysis) 在运行时编译：

```
Profiles/*.cs → Roslyn编译 → 内存中的Assembly → 反射调用 GetParameters()
```

引用的程序集：
- SBAPI.dll (Board, Card, ProfileParameters 等)
- SmartBotAPI (扩展API)
- System.Collections.Generic, System.Linq

---

## 9. 替代系统开发路线

### 需要自行实现的部分

```
1. 注入Payload DLL
   ├── 通过反射读取炉石游戏状态
   ├── 序列化为Seed格式字符串
   ├── 通过NamedPipe发送给主程序
   └── 接收主程序的Action指令并执行

2. 主程序
   ├── 接收Seed → Board.FromSeed() 反序列化
   ├── Roslyn动态编译策略脚本
   ├── 调用 Profile.GetParameters(board) 获取策略参数
   ├── 本地AI引擎(替代SmartBot云端模拟)
   │   ├── 根据ProfileParameters评估每个可能动作
   │   ├── 树搜索/贪心选择最优动作序列
   │   └── 输出Action列表
   └── 将Action通过NamedPipe发回Payload执行

3. 可复用的部分
   ├── Loader.dll (直接使用)
   ├── SBAPI.dll (API定义，保持兼容)
   └── Profiles/*.cs (现有策略脚本)
```

### Payload DLL 核心任务

```csharp
// 在炉石进程内执行
public static void EntryPoint() {
    // 1. 通过反射获取游戏管理器
    var gameState = GetGameState(); // 反射 GameState 类

    // 2. 遍历所有实体，构建Board
    var seed = SerializeBoardToSeed(gameState);

    // 3. 通过NamedPipe发送
    SendSeed(seed);

    // 4. 接收Action并执行
    var actions = ReceiveActions();
    ExecuteActions(actions); // 调用游戏内部API执行操作
}
```

### 关键反射目标 (炉石内部类)

```
GameState          → 游戏状态管理
Entity             → 实体(卡牌/英雄/技能)
Player             → 玩家信息
Zone               → 区域(手牌/场上/牌库/墓地)
GameEntity.Tags    → GAME_TAG 字典
InputManager       → 操作输入(打牌/攻击/结束回合)
```
