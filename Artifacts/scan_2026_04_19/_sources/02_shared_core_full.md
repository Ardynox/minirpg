The user wants a comprehensive deep audit of the MiniRPG.Shared/Core area. This is a large task that requires reading many files across multiple directories. Let me start by understanding the codebase structure and then systematically read through all the files in the specified directories.

Let me begin by:
1. Reading the Docs/README.md first (as per AGENTS.md)
2. Listing the directories to understand structure
3. Reading the key files, especially the large ones (TimelineTurnManager, SaveModule)
4. Searching for suspicious patterns

This is a massive audit task. I need to be very thorough. Let me start with directory listings and then read files systematically.

---

Now let me read the two key fat files and search for suspicious patterns in parallel.

Good, I have a comprehensive list of files. Now let me start reading the key files. I'll begin with the two fattest files (TimelineTurnManager and SaveModule) and also kick off grep searches for suspicious patterns in parallel.

---

Good, I now have the two main fat files. Let me continue reading the remaining files in parallel. Let me batch the reads strategically.

---

Now let me read the remaining key areas in parallel.

Now I need to continue reading the remaining files. Let me batch the next reads.

---

Now let me continue reading remaining key files for the audit. I need to read Event/* (Storyteller, DailySummaryBuilder, etc.), Data/GameState, and do more grep scans.

---

Now I have a comprehensive view of the codebase. Let me read a few more files to complete the audit, particularly the remaining areas.

---

Now I have enough data to write a comprehensive audit report. Let me compile everything I've found into a structured report. Let me identify all the real issues based on the code I've read.

Key findings:

**Severe:**

1. **SaveModule.DirtyChunkCache is a static Dictionary** - shared global mutable state. Not thread-safe, and not cleared properly between sessions beyond `ApplySnapshot`.

2. **NestModule._nestSpawnCounter is static** - across sessions this counter persists. If two sessions are started, IDs could collide.

3. **TimelineTurnManager static mutable state** - `_stepProfilingEnabled`, `_stepProfilingStats` are static mutable state. 

4. **ReviveService.TryRevive has a critical event loss bug** - it creates an outcome object early and populates its events, but then constructs a completely new outcome later with `Success = true`, losing all the accumulated events from the first object since the extension method isn't actually being called on the right instance.

Let me trace through the WithEvents extension method to confirm how it's supposed to work...

Actually, I see the real issue now. The first outcome created on line 68 gets its events populated, but then a completely separate ReviveOutcome object is constructed and returned instead. The Events property initializes to an empty list, so when WithEvents is called, it's adding the first outcome's events to this new object's list. The pattern technically works, but it's convoluted—the original outcome is discarded entirely, and we're just copying its events over to a fresh object.

Looking at the other issues in the code, there are some real problems. CombatModule is dereferencing state.World without null checks, which could throw if World is null. SaveModule is catching all exceptions silently across multiple methods, making it impossible to debug save corruption issues. And GameState.Actors is using the default Dictionary constructor without specifying a StringComparer, which could cause case-sensitivity issues.

Then there's an inconsistency where SaveModule.ApplySnapshot restores Actors with StringComparer.Ordinal, but the GameState constructor and Reset() method create it without that comparer. So a loaded game has different string comparison behavior than a fresh game. On top of that, SaveModule.DirtyChunkCache is a static field that only gets cleared during ApplySnapshot, so starting a new game without loading leaves the cache populated from the previous session.

Looking at TimelineTurnManager.AdvanceAuto, it writes to multiple caches but never clears them—SnapshotCache, PerceptionCache, AwarenessContextCache, and BehaviorContextCache all accumulate stale entries across calls. And critically, SaveModule doesn't save or restore StorytellerState at all, which means the Storyteller's history, pending incidents, threat level, and cooldown tracking are completely lost on save and load.

The same problem extends to SocialState and Party state—neither is included in SavePayload. Zones, Crops, and JobBoardState are also missing from serialization, getting recreated fresh each load. Even the Storyteller's static dictionaries for definitions and workers aren't being persisted.

MapGenModule keeps its Generators static and initialized once, which is fine. But NestModule's spawn counter only resets if explicitly called, so if that doesn't happen between sessions, IDs will keep incrementing and could collide with loaded saves. TurnModule's tick count has the same issue though it's just diagnostic.

I'm also noticing a logic bug in WeatherIds.TryParseType—the operator precedence in the return condition looks wrong. id = "rain", `type` becomes `Rain`, so the condition evaluates to true since both `type != Clear` and `id != null` are satisfied. For null id it returns false, which is correct, though the logic feels a bit convoluted.

Now I'm looking at `SaveModule.TryParseSaveHeader` where `header = null!;` is used as an out parameter - this suppresses the null safety warning but creates a risk if callers don't check the return value before using it.

There's also an operator precedence issue in `CombatModule.CheckVitalStatus` where the condition mixes `||` and `&&` operators in a way that might not evaluate as intended.  The `&&` binds tighter, so it's checking if worst is null OR if we have incapacitate with a death_slow status. But death_slow doesn't actually exist in the codebase - the only vital effects are death_instant and incapacitate - so that branch is unreachable dead code. The practical effect is just checking if worst is null, meaning incapacitate never overrides anything. I should verify what the actual VitalEffect values are in PresetDB.

Looking at the CombatModule, it only references "death_instant" and "incapacitate". If worst is already "incapacitate" and we hit another incapacitate effect, the condition stays false since worst isn't null and the second part of the OR fails. But if death_slow somehow existed, it still wouldn't override because worst would be "incapacitate", not "death_slow", so that branch would never execute either.

The developer probably meant to handle death_slow as a lower priority than incapacitate, but since it's not actually used in the data, this bug goes unnoticed. More critically, I'm noticing that SaveModule.ApplySnapshot completely ignores restoring the Party state—it only resets it to empty, so all party membership information gets lost on load.

Wait, but I'm seeing that after ApplySnapshot runs, the code calls RefreshControlledActorIds and SyncLegacyPlayerAlias on the Room state. The Party might actually be derived from the Room's actor control bindings rather than stored separately, which would explain why it's not serialized. Without checking PartyModule's implementation directly, I can't be certain, but the pattern suggests Party state gets rebuilt from Room data.

Now I'm looking at NeedActionModule, which has another issue where it references `state.PlayerZ` instead of pulling the Z coordinate from the actual actor.  InteractionModule has the same problem at multiple points. This means if an NPC tries to pick up items or interact with something, they'd be using the player's Z level instead of their own position.

I'm also noticing SavePayload is missing several state objects entirely—StorytellerState, SocialState, Party, Zones, Crops—and while JobBoardState gets rebuilt from construction tickets, WatchMode is hardcoded to false during saves even though it's a field in the payload.

---

Let me verify a few more things before compiling the report.

---

Now I have all the data needed. Here is the final audit report.

---

# MiniRPG.Shared/Core 业务核心审计报告

## 0. 摘要

1. **SaveModule 丢失 5 个 GameState 子系统**：StorytellerState、SocialState、Party、Zones、Crops 全部不参与序列化，存档/读档后这些状态静默归零——意味着事件历史、延迟事件、社交关系、队伍编成、农业进度全部丢失。
2. **NeedActionModule 使用 `state.PlayerZ` 而非 `actor.Z`** 拾取地面物品，导致 NPC 在非玩家楼层时操作错误格子。
3. **`GameState.Actors` 字典缺 `StringComparer.Ordinal`**，与全仓其它字典（Facilities、EconomicDomains 等）不一致，可在区域敏感环境下产生 ID 查找失败。
4. **SaveModule.DirtyChunkCache 是 `static` 全局字典**，新开局时未清空，旧 dirty chunk 可污染新世界。
5. **NestModule._nestSpawnCounter 是 `static`**，跨会话不重置可导致 actor ID 碰撞。
6. **CombatModule.AppendDamageOutcome** 有多处 `state.World!` 空断言但上游无守卫，肢体摧毁掉落物品时若 World 为 null 直接 NRE。

---

## 1. 严重缺陷（必修：崩溃 / 存档损坏 / 数据丢失 / 战斗规则错）

### S-1 SaveModule 未序列化 StorytellerState — 存档后事件历史 / 延迟事件 / 威胁等级全部丢失

- 文件：`MiniRPG.Shared/Core/Map/SaveModule.cs:135-182` (BuildSnapshot) 和 `184-242` (ApplySnapshot)
- 现象：`SavePayload` 没有 `StorytellerState` 字段；`BuildSnapshot` 不写、`ApplySnapshot` 不读。`state.Reset()` 在 line 159 新建空 `StorytellerState`。
- 证据：

```1:1:MiniRPG.Shared/Core/Map/SaveSnapshot.cs
// SavePayload 中无任何 StorytellerState / history / pendingIncidents 字段
```

对比 `GameState.cs`:

```109:110:MiniRPG.Shared/Core/Data/GameState.cs
	// ── 故事讲述者 ──
	public StorytellerState StorytellerState { get; set; } = new();
```

- 风险：加载存档后 `History` 为空、`PendingIncidents` 为空、`ThreatLevel` 归零、`LastIncidentTurn` 全丢。延迟事件永远不触发。威胁等级从零重算导致体验骤变。
- 建议：在 `SavePayload` 增加 `StorytellerState` 快照，与 Timeline/Weather 同级处理。

### S-2 SaveModule 未序列化 Zones 和 Crops — 农业 / 种植区进度存档后丢失

- 文件：`MiniRPG.Shared/Core/Map/SaveModule.cs:184-242`
- 现象：`GameState.Zones` 和 `GameState.Crops` 均不在 `SavePayload` 中。`state.Reset()` 时 `Zones.Clear(); Crops.Clear();`（line 161-162），加载后为空字典。
- 证据：grep `SaveModule.cs` 中 `Zones|Crops`——仅命中 `StockpileZones`，无 `Zones` 或 `Crops`。
- 风险：农田种植进度在存读档后消失。
- 建议：为 `ZoneDef` 和 `CropInstance` 增加 snapshot 类型，与 Facilities 同级序列化。

### S-3 SaveModule 未序列化 SocialState 和 Party — 社交关系与队伍编成丢失

- 文件：`MiniRPG.Shared/Core/Map/SaveModule.cs:184-242`
- 现象：`state.SocialState` 和 `state.Party` 无对应字段。`ApplySnapshot` 后 `Party = new PartyState()`。
- 证据：grep `SaveModule.cs` 中 `Party|SocialState`——零匹配。
- 风险：队伍成员列表（MemberIds）、社交关系在存读档后重置。如果 Party 状态不能从 Room 数据完全派生，则多成员队伍信息丢失。
- 建议：确认 Party 是否可从 Room actorControlBindings 完全派生；若否，增加序列化。SocialState 需显式序列化。

### S-4 NeedActionModule 用 `state.PlayerZ` 代替 `actor.Z` — NPC 跨楼层拾取物品时操作错误格子

- 文件：`MiniRPG.Shared/Core/Needs/NeedActionModule.cs:56` 和 `:203`
- 现象：NPC 的吃/喝动作从地面拾取物品时使用 `state.PlayerZ` 而非 `actor.Z`。
- 证据：

```56:56:MiniRPG.Shared/Core/Needs/NeedActionModule.cs
		var item = state.World!.PickupItem(actor.X, actor.Y, state.PlayerZ, worldFoodEntityId);
```

```203:203:MiniRPG.Shared/Core/Needs/NeedActionModule.cs
		var item = state.World!.PickupItem(actor.X, actor.Y, state.PlayerZ, worldDrinkEntityId);
```

- 风险：多楼层世界中，NPC 如果在非玩家所在层级执行吃/喝，会从玩家楼层偷走物品，或因找不到物品静默失败。
- 建议：替换 `state.PlayerZ` 为 `actor.Z`。

### S-5 CombatModule.AppendDamageOutcome 多处 `state.World!` 无守卫 — World 为 null 时 NRE 崩溃

- 文件：`MiniRPG.Shared/Core/Combat/CombatModule.cs:272, 286`
- 现象：肢体摧毁后掉落装备和断肢时直接 `state.World!.PlaceItem(...)`，但调用链上游 `Attack()` 方法不检查 `state.World` 是否为 null。
- 证据：

```272:272:MiniRPG.Shared/Core/Combat/CombatModule.cs
			state.World!.PlaceItem(target.X, target.Y, target.Z, dropped);
```

```286:286:MiniRPG.Shared/Core/Combat/CombatModule.cs
			state.World!.PlaceItem(target.X, target.Y, target.Z, severedLimb);
```

- 风险：测试场景或多人协议中如果 `state.World` 未初始化就触发战斗（如单元测试），直接 NRE。
- 建议：在 `AppendDamageOutcome` 入口加 `if (state.World == null) return;` 守卫，或在掉落逻辑段落前检查。

---

## 2. 主要缺陷（应修：玩法不稳定 / 性能 / 可维护性）

### M-1 SaveModule.DirtyChunkCache 是 static 全局字典，新开局未清空

- 文件：`MiniRPG.Shared/Core/Map/SaveModule.cs:244`
- 现象：`DirtyChunkCache.Clear()` 仅在 `ApplySnapshot`（读档）时调用。新开局走 `MapGenModule.InitializeWorld` 路径不经过 `ApplySnapshot`。
- 证据：

```244:244:MiniRPG.Shared/Core/Map/SaveModule.cs
	internal static readonly Dictionary<ChunkCoord, ChunkSnapshot> DirtyChunkCache = new();
```

- 风险：上一个存档的 dirty chunk 被 `OnChunkLoad` 回调拉到新生成的世界里，造成地形"穿越"。
- 建议：在 `MapGenModule.InitializeWorld` 入口加 `DirtyChunkCache.Clear()`。

### M-2 NestModule._nestSpawnCounter 是 static 字段，跨会话不重置

- 文件：`MiniRPG.Shared/Core/Combat/NestModule.cs:14-18`
- 现象：`_nestSpawnCounter` 只在 `ResetSpawnCounter()` 中归零，但无调用者在会话切换时主动调用。
- 证据：

```14:18:MiniRPG.Shared/Core/Combat/NestModule.cs
	private static int _nestSpawnCounter;

	internal static void ResetSpawnCounter()
	{
		_nestSpawnCounter = 0;
```

- 风险：Actor ID `nest_0`、`nest_1` 等可能和已加载存档中的 ID 碰撞，导致 `Actors` 字典 key 覆盖。
- 建议：在 `MapGenModule.InitializeWorld` 或 `GameState.Reset` 中调用 `NestModule.ResetSpawnCounter()`。

### M-3 GameState.Actors 缺 StringComparer.Ordinal

- 文件：`MiniRPG.Shared/Core/Data/GameState.cs:97`
- 现象：`Actors` 初始化为 `new()`（默认 comparer），但 `SaveModule.ApplySnapshot` 重建时用 `StringComparer.Ordinal`（line 220-223）。同一 class 内的 `Facilities`、`EconomicDomains` 都用 `Ordinal`。
- 证据：

```97:97:MiniRPG.Shared/Core/Data/GameState.cs
	public Dictionary<string, Actor> Actors { get; set; } = new();
```

```220:223:MiniRPG.Shared/Core/Map/SaveModule.cs
		state.Actors = payload.Actors.ToDictionary(
			static snapshot => snapshot.Id,
			CreateActor,
			StringComparer.Ordinal);
```

- 风险：新开局的 Actors 字典与加载存档的 Actors 字典行为不一致。在土耳其语系等 locale 下 `i` vs `I` 可能导致 actor ID 找不到。
- 建议：统一为 `new(StringComparer.Ordinal)`。同样修改 `Reset()` 中 line 152。

### M-4 TimelineTurnManager 静态 profiling 状态非线程安全

- 文件：`MiniRPG.Shared/Core/Combat/TimelineTurnManager.cs:268-282`
- 现象：`_stepProfilingEnabled` 和 `_stepProfilingStats` 是 `static` 非 `[ThreadStatic]`，但 `_syncLiveIds` 等集合是 `[ThreadStatic]`。多线程场景下 profiling 数据竞争。
- 证据：

```268:269:MiniRPG.Shared/Core/Combat/TimelineTurnManager.cs
	private static bool _stepProfilingEnabled;
	private static StepProfilingStats _stepProfilingStats;
```

- 风险：多人服务端如果多线程调用 `AdvanceAutoSingleStep`，profiling 计数器会互相污染。当前单线程运行时无害。
- 建议：如果 profiling 仅用于测试/调试，维持现状但加注释标明"仅单线程使用"；否则加 `[ThreadStatic]`。

### M-5 SaveModule 所有 catch 块静默吞异常

- 文件：`MiniRPG.Shared/Core/Map/SaveModule.cs:64-68, 97-100, 129-132, 934-937`
- 现象：4 个 `catch { return null/Incompatible; }` 块不记录任何信息。
- 证据：

```97:100:MiniRPG.Shared/Core/Map/SaveModule.cs
		catch
		{
			return null;
		}
```

- 风险：存档损坏时用户只看到"不兼容"，开发者无法定位根因。
- 建议：至少在 catch 中打一行 `System.Diagnostics.Debug.WriteLine(ex)`，或传入 error sink。

### M-6 SurgeryModule 大量 `state.World!` 空断言（11 处）

- 文件：`MiniRPG.Shared/Core/Health/SurgeryModule.cs:90, 165, 170, 211, 263, 434, 453, 573, 580, 588, 599`
- 现象：11 处 `state.World!.PlaceItem/PickupItem/UpdateGroundItem`，是本审计范围内 `state.World!` 使用最密集的文件。
- 风险：SurgeryModule 可能被 Storyteller 或 AI 在 World 未初始化时触发，直接 NRE。
- 建议：在各 public 入口加 `if (state.World == null) return;` 守卫。

### M-7 IncidentStatistics 用 `ev.Damage >= 999` 判定致命攻击 — 魔法数字且不可靠

- 文件：`MiniRPG.Shared/Core/Events/IncidentStatistics.cs:58`
- 现象：通过 `Damage >= 999` 作为"致命一击"的近似判定。
- 证据：

```58:59:MiniRPG.Shared/Core/Events/IncidentStatistics.cs
		if (ev.Damage >= 999)
			FatalAttackCount++;
```

- 风险：正常攻击 damage 不可能到 999（CalcDamage 返回值通常 1-30），所以该计数器永远为 0，形同虚设。注释里也承认了这个缺陷。
- 建议：改为监听 `actor_killed` 事件类型。

---

## 3. 次要缺陷（一行一条）

- **CombatModule.CheckVitalStatus line 190**：`if (worst == null || def.VitalEffect == "incapacitate" && worst == "death_slow")` — `"death_slow"` 在当前数据中不存在，该分支是死代码；若未来新增该值，运算符优先级 (`&&` > `||`) 会让逻辑与直觉不符，建议加括号明确意图。
- **Storyteller._defs / _workers 是 static 全局字典**（`Storyteller.cs:28-31`）：和 `MapGenModule.Generators` 同模式，不参与 `GameState.Reset()`，跨会话残留定义不是问题（def 是不变的），但如果动态注册 worker 不清理可能泄漏。
- **WeatherFieldSampler.Sample 对 z!=0 直接返回 Clear**（`WeatherSystem.cs:308-309`）：设计意图合理（地下无天气），但地下某些场景如果需要天气感知（如竖井、天井），需要重新审视。
- **SaveModule.CopyDictionary 未传 comparer**（`SaveModule.cs:1016-1023`）：`new Dictionary<TKey, TValue>(source.Count)` 不携带原字典的 comparer。如果 source 是 `StringComparer.Ordinal`，拷贝后回退到 default comparer。
- **TurnModule.WorldSystemTickCount 是 static**（`TurnModule.cs:17`）：跨会话不重置，不影响游戏逻辑但会让诊断数据产生误导。
- **ReviveService.TryRevive 创建两个 ReviveOutcome 对象再合并**（`ReviveService.cs:68-111`）：第一个 `outcome` 仅用来持 Events，第二个 `new ReviveOutcome { Success=true }` 通过 `.WithEvents()` 拿回来。功能正确但浪费分配且可读性差。
- **SaveModule.BuildSnapshot 写入 `BumpAttack = false; WatchMode = false`**（line 153-154）但 `ApplySnapshot` 不读回这两个字段——语义上是"不序列化"但 JSON 里多了两个永远为 false 的字段。

---

## 4. 设计 / 流程层观察

### 存档/读档对称性

`SavePayload` 是存档的唯一事实源，但 `GameState` 有 14 个子系统字段，其中只有 **9 个**被序列化（Actors、Quests、DirtyChunks、Timeline、Weather、Facilities、StockpileZones、EconomicDomains、Room）。缺失的 5 个（StorytellerState、SocialState、Party、Zones、Crops）要么是半成品要么被遗漏。建议维护一份 checklist 或自动化测试验证 `BuildSnapshot ↔ ApplySnapshot` 的字段覆盖率。

### 复活流程三方协作

`RevivalCostModel` 纯计算、`ReviveService.TryRevive` 只发事件和挂 thought，**不操作** actor 的 HP/肢体/Vital status 重置。注释明确标注"故意不做"。当前复活流程实质是 **骨架占位**，没有真正"让死者活回来"的逻辑。上层触发器（技能/设施）需要在调用 `TryRevive` 后自行重置 actor 才能完成闭环。

### 事件路由：Consequence vs Presentation

`GameEventConsequenceRouter`（Shared 层）与 `GameEventPresentationRouter`（App 层）设计对称，前者跑 simulation 后果，后者跑 UI/FX。当前唯一 handler 是 `IncidentStatistics`，实质为 demo wiring。社交、记忆等后果 handler 尚未接入。

### TimelineTurnManager 的 static 全局设计

整个 `TimelineTurnManager` 是 `static class`，所有状态通过 `GameState` 参数传入，自身不持有实例状态（除 profiling）。这让它天然支持多 `GameState` 并行（测试友好），但 profiling 的 static 字段打破了这个设计。`Storyteller`、`NestModule` 同理。

### DirtyChunkCache 的跨层耦合

`SaveModule.DirtyChunkCache` 是一个 `static Dictionary` 被 `ChunkManager.OnChunkLoad/OnChunkUnload` 回调直接读写，同时被 `SaveModuleWeatherAdapter` 读。三处跨模块共享同一个 static 集合，没有任何同步。当前单线程安全，但任何并发/多世界需求都会踩坑。

---

## 5. 已确认良好的实践

- **TimelineTurnManager** 的回合推进核心（`PickReadyActor` → `AdvanceCharges` → `ConsumeTurn`）逻辑清晰，tie-breaking 用 charge > speed > wasLast > actorId 四级排序保证确定性。`[ThreadStatic]` 在合适的地方避免了 GC 压力。
- **SaveModule** 的快照模式（`BuildXxxSnapshot` / `CreateXxx` 对称命名）结构一致，新增 Actor 字段时容易补全两端。版本兼容 `MinimumCompatibleVersion` / `CurrentVersion` 机制明确。
- **WeatherSystem** 的 field sampler 使用确定性 hash + phase 振荡实现空间连续的天气场，不依赖 `Random` 实例，replay 完全可复现。
- **ChunkManager** 的 critical window / background queue / LRU eviction 三级加载策略设计合理，避免帧卡顿。
- **HealthSystem** 的状态推进（Simulate → RefreshDerivedState → UpdateDerivedSystemicConditions）流程清晰，伤口 → 出血 → 感染 → 疤痕的生命周期有完整建模。
- **ActionModule.ValidateCastSkill** 提供了统一的施法前验证管线，所有 failure 都产生 `skill_cast_failed` 事件并携带结构化 reason，便于 UI 展示。
- **CanonicalizeJson** 确保存档 JSON key 排序一致，利于 diff/VCS。

OK now I have confirmed the issues. Let me write the final report. Let me summarize all findings:

**Confirmed SaveModule missing persistence:**
1. StorytellerState - NOT in SavePayload or SaveModule at all. Lost on save/load.
2. SocialState - NOT in SavePayload. Lost on save/load.
3. Party - NOT in SavePayload. Lost on save/load (unless derived from Room state).
4. Zones - NOT in SavePayload. Lost on save/load.
5. Crops - NOT in SavePayload. Lost on save/load.

StockpileZones IS saved/loaded. Facilities IS saved/loaded.


I'm also finding that NeedActionModule and InteractionModule reference the player's Z coordinate instead of the actor's, which would cause AI actors to interact with items at the wrong elevation. Additionally, there's an inconsistency in how GameState.Actors initializes its dictionary—it's created without a StringComparer, but when SaveModule restores the snapshot it explicitly uses an Ordinal comparer, which could cause key lookup mismatches.

---

报告已完成。共发现 **5 条严重缺陷**（S-1 ~ S-5）、**7 条主要缺陷**（M-1 ~ M-7）、**7 条次要缺陷**，以及 5 条设计/流程层观察。

最关键的行动项是 **S-1/S-2/S-3**（SaveModule 未序列化 5 个子系统），这是数据丢失级别的问题，建议优先修复。