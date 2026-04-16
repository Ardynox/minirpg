using System;

namespace MiniRPG.Core.Revival;

/// <summary>
/// 复活代价模型。把"被复活次数"换算成需要施加的持续性惩罚，
/// 供技能 / 设施在调用 <see cref="ReviveService.TryRevive"/> 后追加到被复活者身上。
/// </summary>
/// <remarks>
/// 设计意图（见 <c>Docs/产品愿景.md</c> 死亡与复活段）：
/// "多次复活可累加负面效果（性格偏移、技能折损、灵魂残缺），给'真的回不来的线'留空间。"
///
/// 当前版本只给一个保守的默认公式——让"第一次复活轻微、第三次后很重"成为默认体验。
/// 数字后续会外提到 JSON 配置（<c>Data/revival_costs.json</c>），本类不硬绑方法 id。
/// </remarks>
public static class RevivalCostModel
{
	/// <summary>
	/// 推算被复活者本次应承受的额外 mood 负 thought 强度和持续回合。
	/// 约定：<paramref name="priorRevivalCount"/> 是**本次复活发生之前**的累计复活次数。
	/// 返回值用于上层再调 <see cref="Needs.NeedSystem.ApplyTemporaryThought"/> 挂一条 "revived_scar" thought。
	/// </summary>
	public static (float MoodOffset, int DurationTurns) ComputeRevivalScar(int priorRevivalCount)
	{
		var index = Math.Max(0, priorRevivalCount);
		// 1 次：-4 持续 360 回合；2 次：-8/720；3 次：-12/1080；上限到 -20/1800。
		var moodOffset = -Math.Min(20f, 4f + index * 4f);
		var duration = (int)Math.Min(1800, 360 + index * 360);
		return (moodOffset, duration);
	}

	/// <summary>
	/// 推算被复活者本次应承受的技能/能力百分比折损。
	/// 0.0 表示不折损；0.1 表示把技能等级（或 capacity 上限）扣掉 10%。
	/// 当前模型：每次累加 5%，上限 40%。
	/// </summary>
	public static float ComputeCapacityPenalty(int priorRevivalCount)
	{
		var index = Math.Max(0, priorRevivalCount);
		return Math.Min(0.4f, 0.05f * index);
	}

	/// <summary>
	/// 判断是否触发"彻底回不来"——累计过多次复活后的最终阈值。
	/// 默认：第 6 次复活触发 "no_return"，上层应该拒绝复活并给出 FailureReason。
	/// </summary>
	public static bool IsPermanentlyLost(int priorRevivalCount) =>
		priorRevivalCount >= 6;
}
