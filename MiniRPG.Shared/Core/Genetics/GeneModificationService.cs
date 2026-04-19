using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;

namespace MiniRPG.Core.Genetics;

/// <summary>
/// 运行时给 actor 修改基因的权威入口。被基因切片设施 / 治疗法术 / debug 命令调，
/// 也是 ServerActionGateway.ExecuteApplyGeneModification 的实现。
///
/// Phase 6 commit 9 已让 Actor.Genome 进存档；本 service 写入后基因表型 tag
/// 自动通过 GeneExpressionService → Actor.ComputeTags 接通；没下游 hook 需要
/// 显式 invalidate cache（运行时实时算）。
///
/// 顺手挂"gene_modified" thought（thoughts.json 已定义 mood -10 / 持续 400 turn），
/// 让玩家观察到"被改基因的 NPC 心情会下降一段时间"——RimWorld 风格"代价感"。
/// </summary>
public static class GeneModificationService
{
	public sealed record Result(bool Success, string? FailureReasonKey, IReadOnlyList<GameEvent> Events);

	public const string EventGeneModified = "gene_modified";
	public const string FailureUnknownGene = "unknown_gene";
	public const string FailureNoTarget = "no_target";

	public static Result TryApplyGene(
		GameState state,
		Actor? target,
		string geneId,
		bool forceDominantAllele,
		Random rng)
	{
		if (target == null)
			return new Result(false, FailureNoTarget, []);

		GeneCatalog.EnsureLoaded();
		var def = GeneCatalog.GetGene(geneId);
		if (def == null)
			return new Result(false, FailureUnknownGene, []);

		target.Genome ??= GeneSpawnService.CreateForRace(rng, target.Race?.Id);
		var pair = ResolveAlleles(rng, def, forceDominantAllele);
		target.Genome!.Loci[geneId] = pair;

		var events = new List<GameEvent>();
		var ev = new GameEvent(EventGeneModified)
		{
			TargetId = target.Id,
			TargetActorName = target.DisplayName,
			TargetX = target.X,
			TargetY = target.Y,
			TargetZ = target.Z,
			ActionName = geneId,
			ConversationPayload = forceDominantAllele ? "homozygous" : "heterozygous",
		};
		events.Add(ev);

		// "gene_modified" thought 已在 Data/thoughts.json 定义（mood -10 / 400 turn）。
		NeedSystem.ApplyThought(target, EventGeneModified, state.Turn, NeedThoughtSources.System, events, state);

		return new Result(true, null, events);
	}

	private static LocusPair ResolveAlleles(Random rng, GeneDef def, bool forceDominantAllele)
	{
		if (forceDominantAllele)
			return new LocusPair { A = def.AlleleA, B = def.AlleleA };

		// 默认走杂合（Aa）：让 spawn 后表型按 dominance 决定，不是直接强 phenotype。
		// 若调用方需要纯隐性可后续追加 `forceRecessive` 参数。
		_ = rng;
		return new LocusPair { A = def.AlleleA, B = def.AlleleB };
	}
}
