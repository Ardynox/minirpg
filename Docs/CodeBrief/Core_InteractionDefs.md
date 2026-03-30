# `Core/InteractionDef.cs` / `Core/InteractionDefs.cs`

## 职责一句话

交互定义系统：定义玩家可以对其他 Actor 执行的交互动作（如对话、交易、攻击、驯服），支持双向条件检查（发起者 + 目标）。

## `InteractionDef` 结构

```csharp
public class InteractionDef
{
    public string Id { get; set; }      // 唯一标识符，如 "talk", "trade"
    public string Name { get; set; }    // 显示名称，如 "对话"
    
    // 发起者需要满足的 tag 条件
    public Dictionary<string, int> Required { get; set; }
    
    // 目标需要满足的条件（可包含特殊条件）
    public Dictionary<string, int> TargetRequired { get; set; }
    
    public string EffectType { get; set; }  // 效果类型：talk/trade/combat/tame
    public int Power { get; set; }           // 效果强度
}
```

## 条件语法

### 普通 tag 条件

```csharp
Required = new() { ["视觉"] = 1 }
// 要求发起者至少拥有 1 点 "视觉" tag
```

### 特殊条件（以 `@` 开头）

- `@faction:X`：阵营匹配
  ```csharp
  TargetRequired = new() { ["@faction:friendly"] = 1 }
  // 目标必须是友好阵营
  ```

- `@max:tag:N`：tag 上限
  ```csharp
  TargetRequired = new() { ["@max:野性:3"] = 1 }
  // 目标的 "野性" tag 不能超过 3
  ```

## 内置交互定义

### `talk`（对话）

- 发起者需要：`视觉 >= 1`
- 目标需要：阵营 = friendly
- 效果类型：`talk`

### `trade`（交易）

- 发起者需要：`视觉 >= 1`
- 目标需要：`交易 >= 1`
- 效果类型：`trade`

### `attack`（攻击）

- 发起者需要：`近战 >= 1`
- 目标需要：阵营 = hostile
- 效果类型：`combat`

### `tame`（驯服）

- 发起者需要：`驯服 >= 2`
- 目标需要：`野性 <= 3` + 阵营 = hostile
- 效果类型：`tame`

## 使用方式

在 `Main.cs` 中：
```csharp
var interactions = InteractionModule.GetInteractions(player, target, InteractionDefs.All);
```

## 评估关注点

- **扩展性**：添加新交互只需在 `InteractionDefs` 中注册新定义，无需修改 `InteractionModule`
- **双向条件**：设计考虑了发起者和目标的双方需求，支持复杂交互规则
- **特殊条件**：当前支持 faction 和 max 两种特殊条件，可扩展其他条件类型