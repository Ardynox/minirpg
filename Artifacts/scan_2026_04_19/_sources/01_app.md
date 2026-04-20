I've now read all files in scope. Let me compile the audit report.

# App + RuntimeUi 审计报告

## 1. 严重缺陷（必修，可能崩溃/数据损坏/影响主流程）

### S-1 多个 `async void` 事件处理器缺少 try/catch，未捕获异常将崩溃进程
- 文件：`App/RuntimeUi/MultiplayerRuntimeCoordinator.cs:338-347`
- 现象：`HandleMultiplayerDisconnected` 是 `async void`，await 后无异常保护
- 证据：

```338:347:App/RuntimeUi/MultiplayerRuntimeCoordinator.cs
private async void HandleMultiplayerDisconnected(string reason)
{
    if (_suppressDisconnectHandling)
        return;

    await CloseMultiplayerBackendAsync(suppressDisconnectHandling: true);
    _closeRoomPanel();
    _flow.MarkDisconnectedRecoverable(reason);
    _openHubWithStatus(reason, true);
}
```

- 风险：网络断连时如果 `CloseMultiplayerBackendAsync` 或任何回调抛出异常，`async void` 的异常直接传播到 SynchronizationContext，导致整个进程崩溃。同样问题存在于 `Main.Multiplayer.cs:349` 的 `HandleMultiplayerReturnToMenu`。
- 建议：用 try/catch 包裹 await 及后续调用，与 `HandleMultiplayerHubRefreshRequested` 等已有保护的 handler 保持一致。

### S-2 `_ExitTree` 吞掉所有异常无任何日志
- 文件：`App/Main.cs:278-295`
- 现象：两个 try/catch 块各捕获 `Exception` 后完全不做任何处理
- 证据：

```278:295:App/Main.cs
public override void _ExitTree()
{
    try
    {
        CloseMultiplayerBackendAsync(suppressDisconnectHandling: true).GetAwaiter().GetResult();
    }
    catch (Exception)
    {
    }

    try
    {
        _localServerLauncher?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    catch (Exception)
    {
    }
}
```

- 风险：如果多人 backend 关闭失败（例如本地服务器进程泄漏），完全没有诊断信息。`GetAwaiter().GetResult()` 在 Godot 主线程上阻塞调用 async 方法，存在死锁风险（虽然是退出时，影响有限）。
- 建议：至少 `GD.PushWarning` 或 `GD.PrintErr` 记录异常。

### S-3 GameEventPresentationRouter 直接修改 GameState（状态所有权违反）
- 文件：`App/RuntimeUi/GameEventPresentationRouter.cs:109`
- 现象：UI 表现层路由器直接对 `_state.KillCount` 做自增
- 证据：

```107:110:App/RuntimeUi/GameEventPresentationRouter.cs
case "actor_killed":
    _state.KillCount++;
    _combatUi.HandleActorKilled(e);
    break;
```

- 风险：`KillCount` 在表现层被修改，绕过了领域逻辑层。多人模式下如果服务端也维护 KillCount，会导致不一致。存档恢复时如果 Dispatch 被 replay，KillCount 可能重复增加。
- 建议：将 KillCount 的自增移到 `GameEventConsequenceRouter` 或 backend 的事件处理中。

## 2. 主要缺陷（应修，影响正确性/扩展性）

### M-1 `SubmitPlayerAction` → `SubmitPlayerActionWithResult` 双重调用导致逻辑冗余执行
- 文件：`App/Main.Timeline.cs:133-155`
- 现象：`SubmitPlayerAction` 先检查 autoNav 中断 + 多人路由，然后调用 `SubmitPlayerActionWithResult`，后者重复执行同样的两个检查
- 证据：

```133:155:App/Main.Timeline.cs
private void SubmitPlayerAction(TimelinePlayerAction action)
{
    if (!_autoNav.IsExecutingStep)
        InterruptAutoNavigationForManualInput();

    if (TrySubmitMultiplayerTimelineAction(action))
        return;

    _ = SubmitPlayerActionWithResult(action);
}

private TimelineStepResult SubmitPlayerActionWithResult(TimelinePlayerAction action)
{
    if (!_autoNav.IsExecutingStep)
        InterruptAutoNavigationForManualInput();

    if (TrySubmitMultiplayerTimelineAction(action))
        return new TimelineStepResult();
    // ...
}
```

- 风险：autoNav 中断逻辑执行两次（副作用风险低但语义不清晰）。`TrySubmitMultiplayerTimelineAction` 在非多人模式下返回 false 无害，但如果日后该方法增加副作用，可能导致双重执行。
- 建议：`SubmitPlayerAction` 直接调用核心逻辑，不经过 `SubmitPlayerActionWithResult`，或让后者不重复前置检查。

### M-2 `newmap` 文本命令绕过正常会话切换流程
- 文件：`App/Main.Command.cs:154-160`
- 现象：直接调用 `_session.NewGame()` 而不走 `PrepareSessionTransition` + `FinalizeSessionPanels`
- 证据：

```154:160:App/Main.Command.cs
case "newmap":
    _session.NewGame(PlayerCreationOptions.CreateDefault());
    ClearPlayerTargeting();
    RefreshPlayerCharacterVisual();
    SyncTimelineAutoAdvanceState();
    _log.Add(LocalizationService.T("log.game.new_map_generated"));
    FlushMap();
    break;
```

- 风险：不清理 armed skill、inspect mode、watch mode、rest mode、dialog/trade UI、incidentStatistics/relationships/rumorBus 等状态。对比 `MainAppFlowCoordinator.PrepareSessionTransition` 的完整清理列表，此路径遗漏了大量重置步骤，会导致旧会话的 UI/状态残留到新地图。
- 建议：走 `MainAppFlowCoordinator.DoStartNewGame()` 或至少补齐 `PrepareSessionTransition` + `FinalizeSessionPanels` 调用。

### M-3 `PlayerRestModeController` 已提取但 Main 仍保留完整内联实现（半迁移）
- 文件：`App/Main.Timeline.cs:66-128` vs `App/RuntimeUi/PlayerRestModeController.cs:1-107`
- 现象：`Main.Timeline.cs` 中的 `ProcessPlayerRestMode()` 和 `TogglePlayerRestMode()` 与 `PlayerRestModeController.Tick()` 和 `Toggle()` 逻辑完全重复。Main 实际执行的是内联版本（通过 `_playerRestModeActive` 字段），而 `PlayerRestModeController` 有自己的 `_active` 字段。
- 风险：两套实现并存，任何修复只改一处会导致行为分歧。重构路线图已记录休息模式提取，但当前状态是 controller 已写好却未接入。
- 建议：完成迁移——Main 中只保留 `_playerRestMode.Tick()` / `_playerRestMode.Toggle()` 委托。

### M-4 `OnSessionEndedAudio()` 定义但从未被调用
- 文件：`App/Main.Audio.cs:56-59`
- 现象：方法体存在，调用 `_musicCoordinator?.Unbind()`，但全仓搜索无调用点
- 证据：

```56:59:App/Main.Audio.cs
private void OnSessionEndedAudio()
{
    _musicCoordinator?.Unbind();
}
```

- 风险：回到主菜单或切换会话时音乐协调器未解绑，可能导致旧状态绑定到新会话的 GameState。
- 建议：在 `HandleBackToMenu` / `PrepareSessionTransition` 路径中调用 `OnSessionEndedAudio()`。

### M-5 `ClientCommandRouter` 与 `Main.Timeline.cs` 存在重复的命令提交路径
- 文件：`App/RuntimeUi/ClientCommandRouter.cs:54-62` vs `App/Main.Timeline.cs:157-176`
- 现象：`ClientCommandRouter.Submit()` 和 `Main.TrySubmitClientCommand()` + `Main.SubmitClientCommand()` 各自独立实现了相同的路由逻辑（多人优先 → 单机 fallback）
- 证据：

```54:62:App/RuntimeUi/ClientCommandRouter.cs
public void Submit(ClientCommand command)
{
    if (TrySubmit(command))
        return;
    var result = _sessionBackend.SubmitCommandAsync(command).GetAwaiter().GetResult();
    if (!result.Accepted && !string.IsNullOrEmpty(result.FailureReason))
        _log.Add(result.FailureReason);
}
```

```168:176:App/Main.Timeline.cs
private void SubmitClientCommand(ClientCommand command)
{
    if (TrySubmitClientCommand(command))
        return;
    var result = _sessionBackend.SubmitCommandAsync(command).GetAwaiter().GetResult();
    if (!result.Accepted && !string.IsNullOrEmpty(result.FailureReason))
        _log.Add(result.FailureReason);
}
```

- 风险：改一处忘改另一处。两处都包含 `.GetAwaiter().GetResult()` 阻塞调用。
- 建议：Main 中的版本应委托到 `ClientCommandRouter.Submit()`。

### M-6 `MainAppFlowCoordinator` 构造函数超过 50 个 `Action` 参数
- 文件：`App/RuntimeUi/MainAppFlowCoordinator.cs:97-214`
- 现象：构造函数接收约 50 个 `Action`/`Func` 委托参数
- 风险：易传错参数位置（C# 无命名 Action<> 的类型区分）。任何新需求都要扩展此签名。已在重构路线图中知晓，不展开。

## 3. 次要缺陷（可清理）

- `App/Main.Command.cs:37-41`：`:dig_up` 分支被前面 `:dig_*` 通配已覆盖，是不可达的死代码（`dig_up` 无冒号版仍可达）。
- `App/Main.cs:362`：`_Input` 方法的 XML doc 注释写的是"标记所有常驻面板脏标记"，但实际是输入处理，与 `_Ready()` 和 `MarkUIDirty()` 上方注释重复且不匹配。
- `App/Main.cs:255`：`_Ready()` 上方的 XML doc 注释同样是"标记所有常驻面板脏标记"，与方法职责不符。
- `App/Main.Timeline.cs:446`：`HandlePlayerDeath` 的 XML doc 是空 `<summary>`。
- `App/Main.Settings.cs:47-48`：`BuildSettingsUiState` 中 `resolvedContext` 的 fallback 逻辑在 `_menu.InMenu` 为 false 时返回 `InGamePause`，但调用 `SyncSettingsUiState(null)` 时 `context` 总是 null，依赖 fallback 路径可能不如显式传参清晰。
- `App/RuntimeUi/GameEventPresentationRouter.cs:75,82-85,92-93,96,99,113`：多处使用 `==` 比较字符串 ID（如 `e.InitiatorId == _state.PlayerId`），虽然 C# 中 `==` 对 string 做值比较是正确的，但与仓库中大量使用 `string.Equals(..., StringComparison.Ordinal)` 的风格不一致。
- `App/Main.WorldTool.cs:404-407`：`LocalizeFacilityRotation` 方法存在但仓库内无调用点，疑似残留。
- `App/Main.Startup.cs:85-89`：`StoreLoadedHeavyResource` 方法体只有 `_ = path; return true;`，是 noop 占位。当前 `StartupHeavyLoadSteps` 为空数组，所以不会被调用，但如果将来加 step 会无声忽略。
- `App/ResourceCatalogEditor.cs`：1970+ 行的单文件工具编辑器，独立运行不影响主游戏流程，体积大但职责单一。

## 4. 设计/流程层观察（不必立即改但需要记录）

- **`_ExitTree` 同步阻塞 async 方法**：`Main._ExitTree` 使用 `.GetAwaiter().GetResult()` 阻塞等待 `CloseMultiplayerBackendAsync` 和 `_localServerLauncher?.DisposeAsync()`。在 Godot 退出流程中这是可接受的妥协，但如果 backend dispose 涉及网络 I/O 会延长退出时间。
- **`SubmitClientCommand` / `ClientCommandRouter.Submit` 均使用阻塞 `.GetAwaiter().GetResult()`**：单机模式下同步阻塞 async 命令提交。当前 `LocalSessionBackend.SubmitCommandAsync` 是同步完成的 Task，无阻塞风险。但如果未来引入真正异步的单机 backend（如磁盘持久化队列），此处会变成阻塞热点。
- **Main 与 Coordinator 的薄/厚委托不一致**：部分方法（如 chest、autoNav、targeting）已是纯单行委托到 coordinator，但 rest mode、timeline auto-advance、watch mode 等仍有大量业务逻辑内联在 Main partial files 中。重构路线图批次 1 已计划这些迁移。
- **`WireEventHandlers` 只做 `+=` 订阅，无对称的 `-=` 退订**：`Main.Wiring.cs` 中大量事件订阅在 `_ExitTree` 中未解除。由于 Main 是场景根节点，生命周期与应用一致，实际不会泄漏——但如果将来支持 hot-reload 或测试场景替换，会成为问题。
- **RuntimeComposition 中的懒加载面板引用在构建时为 null**：`RuntimeUiRefs` 在 `BuildRuntimeComposition` 时 `ChestPanel`/`DialogPanel`/`TradePanel`/`QuestPanel`/`ActorInspectPanel`/`LimbTargetPanel` 全为 null，这些面板后续通过 `Ensure*Panel()` 创建后才赋值到 `_chestPanel` 等字段，但不会回写到 `RuntimeUiRefs`。如果 `RuntimeViewCoordinator` 依赖这些引用，会拿到 null。
- **GameEventPresentationRouter 使用字符串字面量做事件类型分发**（如 `"combat_attack"`、`"actor_killed"`），与事件边界协议耦合紧密。重构路线图中已有相关记录。

## 5. 已确认良好的实践（避免误改）

- **`CaptureRuntimeUiMode` (Main.UiMode.cs)** 做了完整的 null guard，在任何依赖服务未初始化时返回安全的 "blocked everything" 快照，避免 startup 过程中的 NPE。
- **`MainStartupCoordinator` 状态机**设计清晰，`LoadingHeavyAssets → CompletingHeavyAssetsSynchronously → Finalizing → Ready/Failed` 路径有明确的状态转换，且 timeout fallback 和同步降级路径均有日志。
- **`MultiplayerRuntimeCoordinator.CloseMultiplayerBackendAsync`** 正确地先 null 置换 `_backend`，再解除事件订阅，最后 `DisposeAsync`，避免了 dispose 过程中收到回调的竞态。
- **`ModalStateController.Prepare`** 在打开新模态前统一清理旧状态的模式，避免了模态叠加。
- **`MainInputCoordinator`** 的分层输入处理（modal → layout edit → map editor → inspect → panel → gameplay）优先级清晰，避免了输入穿透。
- **`WorldManagerDeletionRouter`** 通过提取三条相似的删除回调路由，消除了 `MainAppFlowCoordinator` 中的重复代码，结构良好。

Now I have enough context to write the audit report. Let me carefully analyze all the code I've read and identify real defects.

Let me go through the categories:

**1. Code Defects (Bugs):**

S-1: `_ExitTree` swallows exceptions silently (Main.cs:284-285). Two separate try/catch blocks catch `Exception` and do nothing - no logging at all. If CloseMultiplayerBackendAsync or _localServerLauncher.DisposeAsync fails, it's completely silently eaten. This is a genuine bug - at minimum there should be logging.

S-2: `SubmitClientCommand` does synchronous `.GetAwaiter().GetResult()` on async method (Main.Timeline.cs:173). 

This blocking pattern also appears in ClientCommandRouter.cs:59, and it's problematic because it synchronously waits on the main thread—if the backend performs any I/O in single-player mode, the UI will freeze. Beyond that, this approach isn't safe across all contexts and can potentially deadlock depending on the synchronization context.

S-3: `_ExitTree` similarly blocks on async disposal with `.GetAwaiter().GetResult()` (Main.cs:282, 290), which carries the same threading concerns though it happens at shutdown.

M-1: `OnCommand` has dead code with unreachable branches—the `:dig_up` and `dig_up` cases in Main.Command.cs:37-41 will never execute because the earlier condition on line 32-35 already catches any command starting with `:dig_` (except `:dig` itself), so `:dig_up` gets handled there first. Let me trace through the logic more carefully to confirm which branches are actually reachable. I'm noticing there's duplicate rest mode logic scattered across the codebase—Main.Timeline.cs has its own ProcessPlayerRestMode and TogglePlayerRestMode methods, while a separate PlayerRestModeController exists in RuntimeUi with identical functionality. The controller appears to be constructed but never actually wired into Main's initialization, so the actual rest mode processing is happening directly in Main.Timeline.cs instead. PlayerRestModeController exists but the code in Main.Timeline.cs still has the inline logic, so both implementations are present—this is a dead code and incomplete migration issue since Main is what actually executes. I'm also noticing `OnSessionEndedAudio` is defined but never called, and there's inconsistent string comparison throughout GameEventPresentationRouter.cs where `==` is used instead of `string.Equals` with `StringComparison.Ordinal`.

The string comparison with `==` actually works fine in C# since it does value comparison, but it's inconsistent with the rest of the codebase. For the `HandleMultiplayerDisconnected` event handler in MultiplayerRuntimeCoordinator.cs, it's marked as `async void` which is the standard pattern for event handlers, but there's no try/catch around the await call, so if `CloseMultiplayerBackendAsync` throws an exception it could crash the process.

I'm seeing this same issue across multiple event handlers—HandleMenuMultiplayer and several Hub-related handlers also use `async void` without proper exception handling, though most of the Hub ones do have try/catch blocks. HandleMultiplayerDisconnected is notably missing that protection, and there's another similar case in Main.Multiplayer.cs with `HandleMultiplayerReturnToMenu` that also lacks error handling.

The `MainAppFlowCoordinator` constructor is a major design issue with 50+ parameters, all Action delegates—any interface change cascades through the codebase, though this is already flagged in the roadmap.

I'm also noticing that `SubmitPlayerAction` and `SubmitPlayerActionWithResult` are doing redundant multiplayer checks...

Examining the code flow more carefully...

in single-player mode, both `TrySubmitMultiplayerTimelineAction` calls return false, so there's no actual double submission—just redundant computation. But there are other issues: the synchronous blocking call to `CloseMultiplayerBackendAsync` in `_ExitTree` creates a deadlock risk on the UI thread, and the presentation layer directly mutating `_state.KillCount++` violates state ownership principles.

I'm also noticing several `async void` methods without proper error handling that could crash the app if exceptions occur, though `HandleMenuMultiplayer` appears safer since it wraps its async call in try/catch. I should also flag the `newmap` command which calls `_session.NewGame()` directly from a text input without any busy operation wrapper or resource readiness check.

Looking at the actual implementation, the `newmap` command just generates a new map and refreshes visuals without calling `PrepareSessionTransition` to clean up armed skills, inspect mode, and threat HUD state like a proper game start would do. This is a significant gap in session state management.