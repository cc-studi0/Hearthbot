# 狂野颜射术重构方案

## 📋 重构目标

将原文件中分散的卡牌逻辑集中管理，每张卡牌的所有判断条件集中到一个区域，使用 `if-else if-else` 顺序判断结构，避免逻辑冲突。

---

## ⚠️ 原代码存在的问题

### 问题1：逻辑分散
同一张卡牌的modifier在代码中被设置了多次，分散在不同区域：

```csharp
// 位置1：第1449行
p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(999));

// 位置2：第1973行
p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(350));

// 位置3：第2257行
p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-200));

// 位置4：第2449行
p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(450));

// ... 还有更多地方
```

### 问题2：互相覆盖
后面的设置会覆盖前面的设置，导致无法预测最终生效的是哪个值。

### 问题3：缺乏优先级
没有明确的条件优先级顺序，可能导致低优先级条件覆盖高优先级条件。

---

## ✅ 重构解决方案

### 核心思想
**一卡一区，顺序判断**

每张卡牌创建一个专门的处理方法，在该方法内部按优先级从高到低使用 `if-else if-else` 结构判断。

### 示例：时空之爪（END_016）重构

#### 重构前（分散在多处）:
```csharp
// 某处：禁用替换
if (hasClawEquipped && hasClawInHand)
{
    p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(999));
}

// 另一处：提升优先级
if (hasHandInMaxCost)
{
    p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-200));
}

// 又一处：抑制攻击
if (hasBarrageInHand)
{
    p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(999));
}
```

#### 重构后（集中处理）:
```csharp
private void ProcessCard_END_016_TimeWarpClaw(ProfileParameters p, Board board, 
    bool hasDiscardComponentAtMax, bool canTapNow, bool hasNoDrawLeft, 
    bool lethalThisTurn, int enemyHp, int maxCostInHand)
{
    // 【优先级1 - 最高】斩杀窗口：先法术后武器攻击
    if (lethalThisTurn && heroCanAttackWithClaw && hasPlayableDamageSpell)
    {
        p.WeaponsAttackModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(650));
        AddLog($"[时空之爪-优先级1] 斩杀窗口 => 后置爪子攻击");
        return; // ← 关键：走完就结束，不再往下走
    }

    // 【优先级2】已装备且手中有重复：禁止替换
    else if (hasClawEquipped && hasClawInHand && !shouldAllowReplacement)
    {
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(999));
        AddLog($"[时空之爪-优先级2] 已装备 => 严禁替换");
        return; // ← 走完就结束
    }

    // 【优先级3】配合古尔丹之手
    else if (hasHandInMaxCost && handCount <= 7)
    {
        p.CastWeaponsModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(-200));
        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.END_016, new Modifier(7500));
        AddLog($"[时空之爪-优先级3] 配合古尔丹之手 => 提升优先级");
        return;
    }

    // ... 继续按优先级排列其他条件

    // 【优先级N - 默认】无特殊条件
    else
    {
        AddLog($"[时空之爪-默认] 使用默认值");
        // 不设置任何modifier，使用引擎默认值
    }
}
```

---

## 📦 需要重构的主要卡牌

根据代码分析，以下卡牌需要集中处理：

### 高优先级（逻辑最复杂）
1. **END_016** - 时空之爪 ⭐⭐⭐⭐⭐
2. **EX1_308** - 灵魂之火 ⭐⭐⭐⭐⭐
3. **TIME_027** - 超光子弹幕 ⭐⭐⭐⭐
4. **YOD_032** - 狂暴邪翼蝠 ⭐⭐⭐⭐
5. **RLK_534** - 灵魂弹幕 ⭐⭐⭐⭐

### 中优先级
6. **WON_103** - 维希度斯的窟穴 ⭐⭐⭐
7. **TLC_603** - 栉龙 ⭐⭐⭐
8. **LOOT_014** - 狗头人图书管理员 ⭐⭐⭐
9. **TLC_451** - 咒怨之墓 ⭐⭐⭐
10. **ULD_163** - 过期货物专卖商 ⭐⭐⭐

### 一般优先级
11. **TOY_916** - 速写美术家 ⭐⭐
12. **CORE_NEW1_022** - 恐怖海盗 ⭐⭐
13. **WON_098/KAR_205** - 镀银魔像 ⭐⭐
14. **RLK_532** - 行尸 ⭐⭐
15. **TOY_518** - 宝藏经销商 ⭐
16. **GDB_333** - 太空海盗 ⭐

---

## 🔄 重构步骤建议

### 阶段1：创建框架（已完成 ✓）
- ✅ 创建新文件结构
- ✅ 设计处理方法模板
- ✅ 实现核心卡牌示例

### 阶段2：迁移核心逻辑
1. 为每张核心卡牌创建专门的 `ProcessCard_XXX` 方法
2. 从原文件中提取该卡牌的所有判断逻辑
3. 按优先级从高到低排序
4. 使用 `if-else if-else` 结构重写
5. 每个分支添加清晰的注释和日志

### 阶段3：集成测试
1. 保留原文件作为备份
2. 在新文件中测试每个卡牌的逻辑
3. 对比新旧逻辑的执行结果
4. 修复发现的差异

### 阶段4：完善和优化
1. 补充所有辅助方法的实现
2. 统一日志格式
3. 添加错误处理
4. 性能优化

---

## 📝 编码规范

### 方法命名
```csharp
private void ProcessCard_<CardID>_<CardName>(ProfileParameters p, Board board, ...)
```

### 优先级注释
```csharp
// 【优先级1 - 最高】描述这个条件的作用
// 【优先级2】描述这个条件的作用
// 【优先级3 - 默认】正常情况
```

### 日志格式
```csharp
AddLog($"[卡牌名称-优先级X] 触发条件 => 执行动作(modifier值)");
```

### 返回控制
每个条件分支处理完后必须 `return`，确保不会继续往下执行。

---

## 🎯 重构优势

1. **可维护性提升** ✓
   - 每张卡牌的所有逻辑集中在一个方法中
   - 修改某张卡牌的逻辑只需要改一个地方

2. **逻辑清晰** ✓
   - 明确的优先级顺序
   - if-else if-else 保证只走一个分支

3. **避免冲突** ✓
   - 同一张卡牌不会在多处被设置
   - 不会出现互相覆盖的问题

4. **易于调试** ✓
   - 清晰的日志标识每个分支
   - 容易追踪决策过程

5. **易于扩展** ✓
   - 添加新条件只需在对应方法中插入新的 else if
   - 不影响其他卡牌的逻辑

---

## ⚡ 下一步行动

1. **审阅示例代码** ([狂野颜射术重构_NEW.cs](狂野颜射术重构_NEW.cs))
   - 查看时空之爪、灵魂之火等核心卡牌的重构示例
   - 确认重构方向是否符合预期

2. **确认重构范围**
   - 是否需要重构所有卡牌，还是只重构核心卡牌？
   - 是否需要保留原文件作为参考？

3. **开始全面重构**
   - 按优先级逐个重构剩余卡牌
   - 补充辅助方法的实现
   - 完整测试所有逻辑

---

## � 重构完成度（2026-02-05更新）

✅ **已完成**：
1. ✅ 创建重构框架和示例代码
2. ✅ 实现5个核心卡牌的处理方法（时空之爪、灵魂之火、超光子弹幕、狂暴邪翼蝠、维希度斯的窟穴）
3. ✅ 添加4个额外卡牌处理方法（灵魂弹幕、栉龙&狗头人、过期货物专卖商、恐怖海盗）
4. ✅ 完善所有辅助方法的实现
5. ✅ 补充GetModifiers方法的全局计算逻辑
6. ✅ 代码可以编译（等待测试）

**重构进度**：核心功能 100% | 剩余卡牌 0% | 测试 0%

**已重构卡牌**（9张）：
- END_016 - 时空之爪（7个优先级）⭐⭐⭐⭐⭐
- EX1_308 - 灵魂之火（8个优先级）⭐⭐⭐⭐⭐
- TIME_027 - 超光子弹幕（4个优先级）⭐⭐⭐⭐
- YOD_032 - 狂暴邪翼蝠（4个优先级）⭐⭐⭐⭐
- WON_103 - 维希度斯的窟穴（4个优先级）⭐⭐⭐
- RLK_534 - 灵魂弹幕（3个优先级）⭐⭐⭐⭐
- TLC_603 & LOOT_014 - 栉龙&狗头人（过牌优先级）⭐⭐⭐
- ULD_163 - 过期货物专卖商（送死逻辑）⭐⭐⭐
- CORE_NEW1_022 - 恐怖海盗（0费/4费）⭐⭐

---

## �📞 需要确认

- ✅ 重构方向是否正确？
- ✅ 是否需要调整优先级判断的顺序？
- ✅ 是否有其他特殊逻辑需要考虑？
- ✅ 是否需要继续完成全部卡牌的重构？

请确认后我将继续完成剩余工作！
