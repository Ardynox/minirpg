using System;
using MiniRPG.Core.Conversation;

namespace MiniRPG.Module.Handlers;

/// <summary>
/// 对话类 <see cref="ClientCommand"/> 的权威执行器：把 5 个 <c>Conversation*</c> 命令从
/// <see cref="ServerActionGateway"/> 的超大 switch 里外提出来，<c>Gateway.Execute</c> 只负责分发。
/// </summary>
/// <remarks>
/// <para>路线图批次 3 (<c>Docs/重构路线图.md</c>) "Execute 只做分发，不包含领域规则"的第一步落地。
/// 本文件把权威校验 + 调用 <see cref="ConversationModule"/> 生成 events 的组合沉到 handler 层；
/// <see cref="ServerActionGateway.Execute"/> 里的 5 个 switch 分支因此变成单行 forward。</para>
///
/// <para>**为什么先拆 Conversation**：
/// <list type="bullet">
/// <item>这 5 个 execute 每个都只做"校验 NpcActorId + 调 <see cref="ConversationModule"/> 生成 events"，是目前所有 Execute* 里最干净的 5 条，最适合作为"外提 handler"的示范。</item>
/// <item>Chest / Trade 等 handler 内嵌物品 / 容器直写，拆起来会碰 Inventory 权威路径；先不动它们。</item>
/// </list></para>
/// </remarks>
public static class ConversationHandler
{
	/// <summary>选择对话分支。</summary>
	public static ServerActionResult Choose(GameState state, ConversationChooseClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.NpcActorId))
			return RejectInvalidTarget();
		var rng = new Random(state.WorldSeed ^ state.Turn ^ command.NpcActorId.GetHashCode(StringComparison.Ordinal));
		return ServerActionResult.Accept(events: ConversationModule.Choose(
			state,
			command.NpcActorId,
			command.PlayerSessionId,
			command.BranchIndex,
			rng));
	}

	/// <summary>推进当前对话节点的台词（Lines 阶段）。</summary>
	public static ServerActionResult AdvanceLine(GameState state, ConversationAdvanceLineClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.NpcActorId))
			return RejectInvalidTarget();
		return ServerActionResult.Accept(events: ConversationModule.AdvanceLine(
			state,
			command.NpcActorId,
			command.PlayerSessionId));
	}

	/// <summary>主动离开对话。</summary>
	public static ServerActionResult Leave(GameState state, ConversationLeaveClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.NpcActorId))
			return RejectInvalidTarget();
		return ServerActionResult.Accept(events: ConversationModule.Leave(
			state,
			command.NpcActorId,
			command.PlayerSessionId));
	}

	/// <summary>旁观者请求接管当前对话。</summary>
	public static ServerActionResult RequestTakeover(GameState state, ConversationRequestTakeoverClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.NpcActorId))
			return RejectInvalidTarget();
		return ServerActionResult.Accept(events: ConversationModule.RequestTakeover(
			state,
			command.NpcActorId,
			command.PlayerSessionId));
	}

	/// <summary>当前会话持有者批准/拒绝旁观者的接管请求。</summary>
	public static ServerActionResult ApproveTakeover(GameState state, ConversationApproveTakeoverClientCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.NpcActorId))
			return RejectInvalidTarget();
		return ServerActionResult.Accept(events: ConversationModule.ApproveTakeover(
			state,
			command.NpcActorId,
			command.PlayerSessionId,
			command.Approve));
	}

	private static ServerActionResult RejectInvalidTarget() =>
		ServerActionResult.Reject(
			LocalizationService.TOrFallback("log.server_action.invalid_dialog", "Conversation target is invalid."),
			ErrorCode.InvalidDialog.ToWireCode());
}
