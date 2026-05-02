using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 占位 UI 图标路径与加载（<c>Assets/Art/Placeholders/ui_icons/</c>），以及少量非 ui_icons 的占位（作物 / 地面材料堆 / 物品小图）。
/// </summary>
public static class PlaceholderUiIconCatalog
{
	public const string BasePath = "res://Assets/Art/Placeholders/ui_icons/";

	public const string ItemIconsBasePath = "res://Assets/Art/Placeholders/item_icons/";

	public const string CropsBasePath = "res://Assets/Art/Placeholders/crops/";

	public const string MaterialWorldBasePath = "res://Assets/Art/Placeholders/material_world/";

	private static readonly Dictionary<string, Texture2D?> TextureCache = new();

	public static string PathForNeed(string needId) => $"{BasePath}need_{needId}.png";

	public static string PathForCondition(string conditionId) => $"{BasePath}condition_{conditionId}.png";

	public static string PathForThought(string thoughtId) => $"{BasePath}thought_{thoughtId}.png";

	public static string PathForProfession(string professionId) => $"{BasePath}profession_{professionId}.png";

	public static string PathForRace(string raceId) => $"{BasePath}race_{raceId}.png";

	public static string PathForInteraction(string interactionId) => $"{BasePath}interaction_{interactionId}.png";

	/// <summary>叙事事件（<c>Data/storyteller_incidents.json</c> 的 <c>id</c>），与资源名 <c>incident_&lt;id&gt;.png</c> 一致。</summary>
	public static string PathForIncident(string incidentId) => $"{BasePath}incident_{incidentId}.png";

	public static string PathForSurgery(string operationId) => $"{BasePath}surgery_{operationId}.png";

	public static string PathForRoomRole(string roomRoleId) => $"{BasePath}room_{roomRoleId}.png";

	public static string PathForCapacity(string capacityId) => $"{BasePath}capacity_{capacityId}.png";

	public static string PathForItemIcon(string itemId) => $"{ItemIconsBasePath}item_{itemId}.png";

	/// <summary>B8 作物阶段图：<c>crop_&lt;cropDefId&gt;_stage0..2</c>。</summary>
	public static string PathForCropStage(string cropDefId, int stage) =>
		$"{CropsBasePath}crop_{cropDefId}_stage{stage}.png";

	/// <summary>B8 地面原料小堆：<c>mat_&lt;materialId&gt;_stack</c>。</summary>
	public static string PathForMaterialStack(string materialId) => $"{MaterialWorldBasePath}mat_{materialId}_stack.png";

	/// <summary>B7 大部位线稿：<c>limb_head</c>、<c>limb_left_arm</c> 等（后缀与文件名一致）。</summary>
	public static string PathForLimb(string limbPlaceholderSuffix) => $"{BasePath}limb_{limbPlaceholderSuffix}.png";

	/// <summary>将运行时 <see cref="Limb"/> 映射到 B7 十类占位图之一。</summary>
	public static string ResolveLimbPlaceholderSuffix(Limb limb)
	{
		var lower = limb.Id.ToLowerInvariant();
		if (lower.Contains("neck"))
			return "neck";
		if (lower.Contains("brain") || string.Equals(limb.BodyPart, BodyParts.Head, StringComparison.Ordinal))
			return "head";
		if (lower.Contains("heart") || lower.Contains("lung"))
			return "heart_lungs";
		if (lower.Contains("eye"))
			return "eyes";
		if (string.Equals(limb.BodyPart, BodyParts.Hand, StringComparison.Ordinal)
			|| lower.Contains("hand")
			|| lower.Contains("finger"))
			return "hands";
		if (lower.Contains("left_arm") || (string.Equals(limb.BodyPart, BodyParts.Arm, StringComparison.Ordinal) && lower.Contains("left")))
			return "left_arm";
		if (lower.Contains("right_arm") || (string.Equals(limb.BodyPart, BodyParts.Arm, StringComparison.Ordinal) && lower.Contains("right")))
			return "right_arm";
		if (lower.Contains("left_leg") || lower.Contains("left_foot")
			|| (string.Equals(limb.BodyPart, BodyParts.Leg, StringComparison.Ordinal) && lower.Contains("left")))
			return "left_leg";
		if (lower.Contains("right_leg") || lower.Contains("right_foot")
			|| (string.Equals(limb.BodyPart, BodyParts.Leg, StringComparison.Ordinal) && lower.Contains("right")))
			return "right_leg";
		if (string.Equals(limb.BodyPart, BodyParts.Foot, StringComparison.Ordinal))
			return lower.Contains("left") ? "left_leg" : "right_leg";
		if (string.Equals(limb.BodyPart, BodyParts.Torso, StringComparison.Ordinal))
			return "torso";
		if (string.Equals(limb.BodyPart, BodyParts.Head, StringComparison.Ordinal))
			return "head";
		if (string.Equals(limb.BodyPart, BodyParts.Arm, StringComparison.Ordinal))
			return "left_arm";
		if (string.Equals(limb.BodyPart, BodyParts.Leg, StringComparison.Ordinal))
			return "left_leg";
		return "torso";
	}

	public static Texture2D? ResolveLimbRowIcon(Limb limb) =>
		TryLoadTexture(PathForLimb(ResolveLimbPlaceholderSuffix(limb)));

	/// <summary>
	/// 存在则加载并缓存；缺失或加载失败返回 <c>null</c>（不抛异常）。
	/// </summary>
	public static Texture2D? TryLoadTexture(string resPath)
	{
		if (TextureCache.TryGetValue(resPath, out var cached))
			return cached;

		if (!ResourceLoader.Exists(resPath))
		{
			TextureCache[resPath] = null;
			return null;
		}

		var tex = ResourceLoader.Load<Texture2D>(resPath);
		TextureCache[resPath] = tex;
		return tex;
	}

	/// <summary>与 Needs HUD 一致：有阶段 thought 图则优先，否则 need 图。</summary>
	public static Texture2D? ResolveNeedRowIcon(string needId, float value)
	{
		Texture2D? tex = null;
		var stageId = NeedCatalog.ResolveStageId(needId, value);
		if (!string.IsNullOrEmpty(stageId))
			tex = TryLoadTexture(PathForThought(stageId));

		return tex ?? TryLoadTexture(PathForNeed(needId));
	}

	public static Texture2D? ResolveMoodRowIcon(bool moodAllowed)
	{
		if (!moodAllowed)
			return null;
		return TryLoadTexture(PathForNeed(NeedIds.Mood));
	}

	/// <summary>地面列表：优先 <c>item_&lt;Id&gt;.png</c>；否则材料类用 <c>mat_&lt;MaterialId&gt;_stack</c>。</summary>
	public static Texture2D? ResolveGroundItemIcon(Item item)
	{
		if (item == null)
			return null;

		var itemTex = TryLoadTexture(PathForItemIcon(item.Id));
		if (itemTex != null)
			return itemTex;

		if (string.Equals(item.Category, ItemCategories.Material, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(item.MaterialId))
			return TryLoadTexture(PathForMaterialStack(item.MaterialId));

		return null;
	}
}
