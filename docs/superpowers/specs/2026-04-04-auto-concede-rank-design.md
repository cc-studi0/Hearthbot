# 段位自动投降设计

## 背景

需要一个功能：当当前段位超过设定目标时，每局开始直接投降，掉到目标段位后恢复正常打。参考 SmartBot 的 AutoConcede + AutoConcedeRank 实现。

HearthBot 已有 `_autoConcede`、`_autoConcedeMaxRank`、`_autoConcedeAlternativeMode` 字段，但段位判断逻辑未实现。

## 目标

- 当前星级 > 设定星级 → 对局开始后直接投降
- 掉到目标星级后恢复正常打
- UI 上可配置目标段位
- 设置持久化到 settings.json

## 设计

### BotService.cs — 投降判断

在主循环进入对局后（获取到 Seed、识别为对局状态后）、留牌之前，加一个检查：

```csharp
if (_autoConcedeMaxRank > 0 && _lastQueriedStarLevel > 0
    && _lastQueriedStarLevel > _autoConcedeMaxRank)
{
    Log($"[AutoConcede] 当前星级 {_lastQueriedStarLevel} > 目标 {_autoConcedeMaxRank}，自动投降");
    var concedeResp = SendActionCommand(pipe, "CONCEDE", 5000);
    Log($"[AutoConcede] CONCEDE -> {concedeResp}");
    // 按投降计入统计，然后进入对局结束流程
}
```

### MainViewModel.cs — UI 绑定

在 `AutoConcede` 属性附近新增 `AutoConcedeMaxRank` 属性：

```csharp
public int AutoConcedeMaxRank
{
    get => _autoConcedeMaxRank;
    set
    {
        if (_autoConcedeMaxRank == value) return;
        _autoConcedeMaxRank = value;
        _bot.SetAutoConcedeMaxRank(value);
        Notify(nameof(AutoConcedeMaxRank));
        SaveSettings();
    }
}
```

### 设置持久化

在 settings.json 的加载/保存逻辑中加入 `AutoConcedeMaxRank` 字段。

### 星级体系说明

HearthBot 用 `_lastQueriedStarLevel` 表示星级（整数），值越大段位越高：
- 铜10 = 1, 铜1 = 10
- 银10 = 11, 银1 = 20
- 金10 = 21, 金1 = 30
- 铂10 = 31, 铂1 = 40
- 钻10 = 41, 钻1 = 50
- 传说 = 51+

所以"超过钻5"就是 `_lastQueriedStarLevel > 45`。
