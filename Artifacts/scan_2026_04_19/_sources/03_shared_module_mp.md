Now I have all the data needed. Here is the full audit report.

---

# MiniRPG.Shared/Module + Multiplayer + Session 审计报告

## 0. 摘要

对 `GameSessionModule`、`ServerActionGateway`、`Session/*`、`Core/Multiplayer/*`、`MiniRPG.Server/*`、`App/RuntimeUi/Multiplayer*.cs`、`ClientCommandRouter` 共约 4500 行进行全量审计。发现 **3 条严重缺陷**（会导致多人功能不可用或状态损坏）、**4 条主要缺陷**（影响正确性但有补偿机制或单机不触发）、**8 条次要缺陷**，以及若干设计/流程层观察。最严重的是 `ProtocolSerializer` 遗漏 `Climb` 命令导致多人攀爬彻底不通、`HostedLobbyService.JoinRoom` 未向 game host 同步 room state 导致二人以上加入房间失败、以及 `ChangeFloor` / 多人快照路径漏清 `FogOfWarTracker` 产生迷雾穿透。整体架构分层清晰，单机路径稳健，主要缺口集中在**多人路径的集成缝隙**。

---

## 1. 严重缺陷（必修：会话切换 leak / 多人不同步 / 状态损坏）

### S-1 `ProtocolSerializer.DeserializeCommand` 遗漏 `ClientCommandKind.Climb`，多人攀爬完全不通

- **文件**：`MiniRPG.Shared/Core/Multiplayer/ProtocolSerializer.cs:39-74`
- **现象**：`Protocol.cs:42` 定义了 `Climb` 枚举值；`ServerActionGateway.cs:106` 有 `ClimbClientCommand` 的执行分支；`TimelineActionClientCommandFactory.cs:103-107` 能构造 `ClimbClientCommand`。但 `ProtocolSerializer.DeserializeCommand` 的 switch 在 `EndCombat` 之后直接 `_ => null`，**没有 `Climb` 分支**。
- **证据**：

```39:74:MiniRPG.Shared/Core/Multiplayer/ProtocolSerializer.cs
	return kind switch
	{
		ClientCommandKind.Move => JsonSerializer.Deserialize<MoveClientCommand>(raw, Options),
		// ... all other cases ...
		ClientCommandKind.EndCombat => JsonSerializer.Deserialize<EndCombatClientCommand>(raw, Options),
		_ => null,   // ← Climb falls through here
	};
```

- **风险**：多人模式下客户端发出 `ClimbClientCommand`，服务器反序列化返回 `null`，`ENetGameServer` 回复 `DeserializationError` 拒绝。该命令单机正常、多人必失败。
- **建议**：在 `_ => null` 前添加 `ClientCommandKind.Climb => JsonSerializer.Deserialize<ClimbClientCommand>(raw, Options),`。

### S-2 `HostedLobbyService.JoinRoom` 未将新玩家同步到 `RoomRuntimeHost.State`，导致 ENet 握手必失败

- **文件**：`MiniRPG.Shared/Core/Multiplayer/HostedLobbyService.cs:36-40`
- **现象**：`JoinRoom` 调用 `_inner.JoinRoom(request)` 在 `InMemoryLobbyService` 内部将新玩家添加到 lobby 的 `entry.Room.Players`，然后返回 `LobbyJoinTicket`（含新 `JoinToken`）。但 `RoomRuntimeHost.State.Room` 是独立拷贝（`RegisterRoom` 做了 `room.Clone()`），不会自动获知此新玩家。当客户端拿 `JoinToken` 经 ENet 连入时，`RoomRuntimeHost.Connect` 在 `State.Room.Players` 中按 token 查找玩家——找不到，返回 `invalid_join_token`。
- **证据**：

```36:40:MiniRPG.Shared/Core/Multiplayer/HostedLobbyService.cs
public LobbyJoinTicket JoinRoom(LobbyJoinRoomRequest request)
{
	var ticket = _inner.JoinRoom(request);
	if (_gameHost.TryGetRoom(ticket.RoomId, out var roomHost))
		roomHost.AppendLifecycleAudit("join", ...);   // ← only audit, no SynchronizeRoom
	return ticket;
}
```

对比 `CreateRoom`（第 18–29 行）调了 `_gameHost.RegisterRoom`，room host 拿到了完整 room state。`JoinRoom` 和 `ReconnectClaim`（第 44–49 行）均缺少这一步。

- **风险**：房主以外的任何玩家都无法成功加入房间。多人模式实际上只支持 1 人。
- **建议**：`JoinRoom` 末尾添加 `roomHost.SynchronizeRoom(_inner.GetRoomState(ticket.RoomId));`。`ReconnectClaim` 同理。

### S-3 `ChangeFloor` 不清理 `FogOfWarTracker`，楼层切换后迷雾穿透

- **文件**：`MiniRPG.Shared/Module/GameSessionModule.cs:587-594`
- **现象**：`Docs/会话生命周期.md` 第 27 行明确列出楼层切换需要 `_fogTracker.Clear()`。`NewGame`（96 行）、`StartWorldCharacter`（221 行）、`NewBlankEditorMap`（254 行）均执行了此操作。但 `ChangeFloor` 只修改 `_state.PlayerZ`、同步 player actor Z、触发 chunk 加载，**完全不碰 fog**。
- **证据**：

```587:594:MiniRPG.Shared/Module/GameSessionModule.cs
public void ChangeFloor(bool goDown)
{
	if (goDown) { _state.PlayerZ++; var player = ActorModule.GetPlayer(_state); if (player != null) player.Z = _state.PlayerZ; }
	else { _state.PlayerZ--; var player = ActorModule.GetPlayer(_state); if (player != null) player.Z = _state.PlayerZ; }
	var center = new WorldCoord(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
	_state.World?.Chunks.UpdateLoadedChunks(center, _state.Turn);
}
```

- **风险**：上/下楼后，旧楼层的可见区域数据残留在 `FogOfWarTracker` 中，新楼层可能直接"全亮"或显示错误迷雾。
- **建议**：在 `ChangeFloor` 开头加 `_fogTracker.Clear();`。

---

## 2. 主要缺陷

### M-1 `ApplyMultiplayerRoomSnapshot` → `FinalizeLoadedGame` 不调 `_fogTracker.Clear()`

- **文件**：`MiniRPG.Shared/Module/GameSessionModule.cs:370-409`、`794-872`
- **现象**：多人会话首次 snapshot 和每次后续 snapshot（`MultiplayerRuntimeCoordinator.HandleMultiplayerSnapshotReceived:266`）都走 `ApplyMultiplayerRoomSnapshot` → `FinalizeLoadedGame`。`FinalizeLoadedGame` 调了 `MapGenModule.InitializeWorld`、`ActorDerivedStateUpdater.SyncAllActorsForSession` 等，但**从不调 `_fogTracker.Clear()`**。只有三条 `New*` 路径清 fog，`CommitPreparedLoad`（读档）也不清——读档路径依赖 `SaveModule.ApplySnapshot` 内部重置，但多人 snapshot 的迷雾数据与本地不对齐。
- **风险**：多人模式下加入房间后，fog 保留单机会话残留；若多人 snapshot 含不同楼层或地图种子，fog 全量错误。
- **建议**：在 `FinalizeLoadedGame` 开头（或 `ApplyMultiplayerRoomSnapshot` 内 `SaveModule.ApplySnapshot` 之后）加 `_fogTracker.Clear()`。

### M-2 `GameEventConsequenceRouter` 仅挂载到 `LocalSessionBackend`，多人服务端无社会模拟

- **文件**：`App/Main.Startup.cs:220-227`、`MiniRPG.Shared/Core/Multiplayer/DedicatedGameServerHost.cs:190-337`
- **现象**：`Main.Startup.cs:226` 创建 `LocalSessionBackend` 后 `AttachConsequenceRouter(_consequenceRouter)`。但 `RoomRuntimeHost.Execute`（DedicatedGameServerHost.cs:190）调用 `ServerActionGateway.Execute` 后只把 events 打包成 snapshot/batch 广播，**从不调任何 consequence router**。
- **风险**：多人模式下 `RelationshipModule`、`ActorMemoryModule`、`RumorBus`、`IncidentStatistics` 四个 handler 全部失活。NPC 关系不会因战斗漂移，记忆不会记录，谣言不会传播。
- **建议**：在 `RoomRuntimeHost` 构造时注入一个服务端 `GameEventConsequenceRouter`，在 `Execute` 中 accepted 分支于 `BuildSnapshot` 前调用 `DispatchConsequences`。可提取为 `ServerSideConsequenceDispatcher` 类。

### M-3 `StartWorldCharacter` 缺少 `PartyModule.Initialize`

- **文件**：`MiniRPG.Shared/Module/GameSessionModule.cs:206-246`
- **现象**：`NewGame`（100 行）调了 `PartyModule.Initialize(_state)`，`FinalizeLoadedGame`（811 行）调了 `PartyModule.EnsureValid`。但 `StartWorldCharacter` 跳过了初始化，直接到 `ActorDerivedStateUpdater.SyncAllActorsForSession`。

```224:226:MiniRPG.Shared/Module/GameSessionModule.cs
	TimelineTurnManager.Reset(_state);
	ActorDerivedStateUpdater.SyncAllActorsForSession(_state);
	SetWorldCharacterContext(...);
```

比对 `NewGame`:

```99:102:MiniRPG.Shared/Module/GameSessionModule.cs
	TimelineTurnManager.Reset(_state);
	PartyModule.Initialize(_state);        // ← 存在
	EnsureStorytellerWorkers();             // ← 存在
	ActorDerivedStateUpdater.SyncAllActorsForSession(_state);
```

- **风险**：通过 World Manager 创建新角色开局时，Party 系统未初始化，`ActiveActorDeathHandler.Handle` 会工作在未初始化的 party 数据上。
- **建议**：在 `StartWorldCharacter` 的 `TimelineTurnManager.Reset` 之后加 `PartyModule.Initialize(_state);` 和 `EnsureStorytellerWorkers();`。

### M-4 多处 `async void` 事件处理器吞异常

- **文件**：`App/RuntimeUi/MultiplayerRuntimeCoordinator.cs:338`、`App/RuntimeUi/MainAppFlowCoordinator.cs:304/345/377/393/823`、`App/Main.Multiplayer.cs:24/70/273/299/304/309/314/349`
- **现象**：共 13 处 `async void` 方法。其中 `HandleMultiplayerDisconnected`（338 行）在 `CloseMultiplayerBackendAsync` 内调 `backend.DisposeAsync()`，若 ENet dispose 抛出，异常无法被调用方捕获，会导致进程崩溃。
- **风险**：在网络不稳定时触发的 disconnect 事件中若 dispose 失败，整个客户端崩溃而非优雅降级。
- **建议**：改为 `async Task` + fire-and-forget wrapper（`_ = HandleMultiplayerDisconnectedAsync(reason)`），或包裹 try/catch 记日志。

---

## 3. 次要缺陷（一行一条）

- **N-1** `ClientCommandRouter.Submit`（:59）和 `Main.Timeline.cs`（:173）同步阻塞 `ValueTask` `.GetAwaiter().GetResult()`，若 backend 实现变为真异步会死锁 UI 线程。
- **N-2** `MultiplayerRuntimeCoordinator` 无一处 `.ConfigureAwait(false)`（`ActivateMultiplayerSessionAsync`、`CloseMultiplayerBackendAsync`、`SubmitMultiplayerCommandAsync`），运行在 Godot 主线程 SynchronizationContext 下安全但限制了后续移植。
- **N-3** `MultiplayerSessionBackend._pendingCommandSentAt`（:46）在正常游玩中只靠 snapshot/rejected 响应删 key，若服务端丢包则无限增长；`ResetPendingConnectState` 仅在 connect 流中清。建议增加 TTL 清扫。
- **N-4** `ExecuteDialogChoose`（ServerActionGateway.cs:339-354）只做预约检查后直接 `Accept()`，对话选项效果不执行。多人模式下所有对话选择成为"客户端自娱自乐"，权威不记录选项。
- **N-5** `ENetGameServer.EnumerateRoomPeers`（:211-218）用 `yield return` 遍历 `_sessions` 字典。虽然当前 Poll 是单线程安全的，但如果未来改为多线程 dispatch 会抛 `InvalidOperationException`。
- **N-6** `RoomRuntimeHost.Execute` 每次命令都 `SaveModule.BuildSnapshot(State)`（:288-298）做全量快照广播。高频命令（如移动）下序列化开销显著。
- **N-7** `ChangeFloor`（:587-594）把上/下楼两个分支压成单行 if/else，可读性差且不便加清理逻辑。
- **N-8** `ServerAuditLogEntry.Metadata`（ServerAuditLog.cs:26）声明为 `Dictionary<string, string>` init 为 `[]`，但该类是 `record` 且 `Metadata` 没有 `init` setter 保护——外部可直接修改审计日志。

---

## 4. 设计/流程层观察

### 4.1 路线图批次 2 / 3 剩余差距的具体证据

**批次 2（会话层收敛）**：
- `GameSessionModule` 仍直接 `Directory.CreateDirectory(SaveDirectory)`（:157）、`Directory.EnumerateFiles`（:160）、`File.GetLastWriteTime`（:164）、`File.WriteAllText`（:483）。应外提到 `WorldCatalogService` / `WorldStore`。具体落点：把 `ListLegacySaveEntries`、`BuildNamedSavePath`、`ExportPresetScenario` 三个方法迁移到 `WorldCatalogService` 或新建 `LegacySaveSlotService`。
- `LocalSessionBackend` 持有 `GameSessionModule` 引用（:13）并直接调 `_session.NewGame` / `_session.StartWorldCharacter` / `_session.NewBlankEditorMap`（:49-62）。批次 2 完成标准要求 backend 不直接反射会话实现细节，应改为通过 `GameSessionStartRequest` 纯数据驱动。
- `_storytellerWorkersRegistered` 一次性标志（:995-1000）是 session 级别副作用但挂在 module 级别生命周期上——如果 `Storyteller` 是全局静态注册，多次 `NewGame` 不会重新注册，但也不会清除过期 worker。应确认 `Storyteller` 的 worker 注册是否需要随 session 重置。

**批次 3（指令层外提）**：
- `ServerActionGateway` 的 `ExecuteChestTake`/`ExecuteChestTakeAll`/`ExecuteChestPut`（:200-288）三段结构几乎一致（resolve actor → resolve container → reservation → 操作 → persist container），可提取 `ContainerInteractionHandler`。
- `ExecuteTradeBuy`/`ExecuteTradeSell`（:290-337）两段也高度对称，可提取 `TradeHandler`。
- `ExecuteStartCombat`/`ExecuteEndCombat`/`ExecuteEndTurn`/`ExecuteUseSkill`（:519-607）四段战斗相关可提取 `CombatCommandHandler`。

### 4.2 多人路径与单机路径的差异列表

| 方面 | 单机 | 多人 | 差异/风险 |
| --- | --- | --- | --- |
| 命令路由 | `ClientCommandRouter.Submit` → `LocalSessionBackend.SubmitCommandAsync` → `ServerActionGateway.Execute` | `ClientCommandRouter.TrySubmit` → `MultiplayerRuntimeCoordinator.TrySubmitClientCommand` → `MultiplayerSessionBackend.SubmitCommandAsync` → ENet → server `Execute` | 一致（同一 `ServerActionGateway`），但 S-1 的 Climb 序列化漏洞仅影响多人 |
| 状态权威 | `_state` 直接修改，即时生效 | 服务端 `RoomRuntimeHost.State` 为权威，客户端收 snapshot 覆盖 | 客户端预测仅限 Move，其余 `_state` 字段在多人下不应本地改 |
| Consequence Router | `LocalSessionBackend` 附带 router，每个 event 走 relationships/memory/rumor | 服务端无 router（M-2） | 多人会话社会模拟静默丢失 |
| FogOfWarTracker | `NewGame` / `NewBlankEditorMap` 清 fog | `ApplyMultiplayerRoomSnapshot` 路径不清 fog（M-1） | 多人加入后 fog 残留 |
| PartyModule.Initialize | `NewGame` 调 | `StartWorldCharacter` 不调（M-3）；`FinalizeLoadedGame` 调 `EnsureValid` | World Character 新开局 party 可能不完整 |
| 楼层切换 | `ChangeFloor` 不清 fog（S-3） | 多人下 `ChangeFloor` 由服务端权威触发，snapshot 覆盖，fog 仍不清 | 两条路径都有 fog 问题 |
| 异常恢复 | 单机无网络异常 | `async void` disconnect handler（M-4）；`_pendingCommandSentAt` 无 TTL（N-3） | 多人断线场景脆弱 |

### 4.3 `ActiveActorDeathHandler` 与 `PlayerDeathPresenter` / `PartyModule` / `ReviveService` 的协作

- `ActiveActorDeathHandler.Handle`（Session/ActiveActorDeathHandler.cs:31-59）是纯数据层：调 `PartyModule.EnsureValid` → 判断死亡的是否 active → `TryPromoteNextLivingMember` → emit events。不碰 UI、不调 backend。
- `GameEventPresentationRouter.DispatchSingle`（GameEventPresentationRouter.cs:95-122）在收到 `actor_killed`/`death_blood_loss`/`death_infection`/`actor_incapacitated` 且 `TargetId == PlayerId` 时调 `_handlePlayerDeath(reason)`。
- **缺口**：`ActiveActorDeathHandler` 发出的 `party_member_lost`/`active_actor_switched`/`party_wiped` 事件在 `GameEventPresentationRouter.DispatchSingle` 中**没有对应分支**。这意味着当 `ActiveActorDeathHandler` 判定需要切换焦点到下一个活着的队员时，客户端表现层不会执行视角切换或全灭 UI——这些 events 被静默忽略。需要在 `GameEventPresentationRouter` 中增加这三类 event 的 dispatch。
- `ReviveService` 的接入位置在 `Docs/重构路线图.md` 批次 1 #5（`PlayerDeathPresenter`），当前仍在 Main 中。

---

## 5. 已确认良好的实践

- **`ClientCommand` 统一路由**：`ClientCommandRouter` 干净地隔离了单机/多人决策，Main 只保留薄委托。
- **`ServerActionGateway.Execute` 是唯一权威执行入口**：单机和多人共享同一份服务端逻辑，行为一致的根本保证。
- **`RoomRuntimeState.Clone()` 深拷贝链完整**：`Players`、`InteractionReservations`、`ActorControlBindings`、`Rules`、`ModeTransitions` 全部逐层 clone，无共享引用。
- **`RoomRuntimeHost` 的 `lock (_gate)` 粒度合理**：每个 room 一把锁，`Connect`/`Execute`/`Disconnect` 互斥，不会出现 state 并发读写。
- **`ClientPredictionState.Configure` 全量清理**：`_predictedByRequestId.Clear()`、`_orderedPredictions.Clear()`、`_rollbackCountByReason.Clear()`、计数器归零，多人重连不会残留旧预测。
- **`MultiplayerRuntimeCoordinator.CloseMultiplayerBackendAsync` 清理对称**：取消所有 6 个事件订阅、dispose backend、重置 `ActivityVersion`，无泄漏路径。
- **`ReservationService` 与 `RoomRuntimeModule.TryReserveInteraction` 配合**：交互预约有 TTL、有 cleanup、有 busy 反馈、有 release，多人共享容器/交易/对话不会永久死锁。
- **`ENetGameClient.Disconnect` 清理完整**：peer disconnect、host destroy、ENet native release、state reset、事件通知，无泄漏。
- **`ProtocolSerializer` 双向 switch 与 `Protocol.cs` enum 对齐**（除 S-1 的 Climb 遗漏外），round-trip 有测试覆盖。

Now I have all the information I need. Let me compile the audit report. I've confirmed that:

1. `ClientCommandKind.Climb` is NOT in `ProtocolSerializer.DeserializeCommand` - confirmed by grep returning no matches
2. No `.ConfigureAwait` calls in `MultiplayerRuntimeCoordinator.cs` - confirmed by grep returning no matches
3. `FinalizeLoadedGame` does NOT call `_fogTracker.Clear()` - confirmed by reading the method
4. `HostedLobbyService.JoinRoom` does NOT call `roomHost.SynchronizeRoom()` - confirmed by reading the method


5. `ChangeFloor` is also problematic - it only handles Z updates and chunk loading but skips both `_state.Reset()` and `_fogTracker.Clear()`, which the documentation indicates should happen on floor transitions.

Actually, looking at the docs more carefully, the full cleanup sequence (state reset + fog clear) is specified for new games, loading saves, floor changes, and multiplayer joins. But `ChangeFloor` implements a lighter operation without creating a new session, so maybe the state reset isn't necessary there. However, `_fogTracker.Clear()` is definitely required since fog data is layer-specific.

The actual critical issues are: `ProtocolSerializer.DeserializeCommand` missing the `Climb` case breaks multiplayer climbing, `HostedLobbyService.JoinRoom` fails to sync the room state to the game host breaking player joins in hosted mode, and `ChangeFloor` doesn't clear fog leaving stale fog data across floor transitions.

Beyond those, there are several severe problems: the multiplayer snapshot path also skips fog clearing, `GameEventConsequenceRouter` isn't registered on the server so social and memory simulation don't work in multiplayer, `StartWorldCharacter` doesn't initialize the party module, and scattered `async void` event handlers can crash the process with unhandled exceptions. There are also major issues like unbounded growth of `_pendingCommandSentAt`, missing `ConfigureAwait(false)` calls in async methods, synchronous blocking on async operations in `ClientCommandRouter.Submit`, and `ExecuteDialogChoose` being a no-op that doesn't actually execute dialog effects server-side.