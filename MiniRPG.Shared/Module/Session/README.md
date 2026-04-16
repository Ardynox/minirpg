# Session

这个目录只负责会话 backend 边界。
它把 `GameSessionModule` 上层编排和“本地单机 / 远程多人房间”两种 backend 实现隔开。

## 从哪开始读

- 先看 `IGameSessionBackend.cs`：这里定义启动、提交命令、轮询、快照和增量事件的公共契约
- 本地单机会话：`LocalSessionBackend.cs`
- 远程多人房间：`MultiplayerSessionBackend.cs`

## 常见改动去哪里

- 改 backend 统一接口、快照 envelope、delta envelope：`IGameSessionBackend.cs`
- 改本地新游戏、空白编辑器开局、世界角色进入、本地命令执行：`LocalSessionBackend.cs`
- 改远程连接、房间加入、轮询、重连认领、快照/事件接收：`MultiplayerSessionBackend.cs`

## 不要在这里解决什么

- 不把世界目录、继续游戏、存档文件管理写进这里
- 这些属于 `GameSessionModule`、`WorldCatalogService`、`ContinueStateService` 的职责
- 不把 UI、面板、主菜单流程写进 backend 层

## 修改提醒

- 改接口时要同时检查本地和多人两个实现，避免只改一侧
- 这个目录负责“如何接会话 backend”，不负责“整个会话生命周期怎么编排”

## 什么时候更新这份 README

- 只有当 backend 契约、实现分工、或常见改动入口发生变化时才更新
