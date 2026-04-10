using System.Collections.Generic;

namespace MiniRPG.Core.AI;

/// <summary>
/// 从 GameState 构造不同精度的 Perception。
/// 当前由批处理入口统一构建：cheap broad-phase + 少量 LOS 校验。
/// </summary>
public static class PerceptionBuilder
{
	public static Perception Build(GameState state, Actor self, SimDetail detail)
	{
		var batch = BuildBatch(state, [new AIVisionRequest(self, detail)]);
		if (batch.TryGetValue(self.Id, out var perception))
			return perception;

		return new Perception
		{
			Self = self,
			Turn = state.Turn,
			Floor = self.Z,
			State = state,
		};
	}

	public static Dictionary<string, Perception> BuildBatch(GameState state, IReadOnlyList<AIVisionRequest> requests) =>
		AIVisionBatch.Build(state, requests);
}
