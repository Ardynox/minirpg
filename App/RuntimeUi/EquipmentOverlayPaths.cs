using Godot;

namespace MiniRPG;

/// <summary>
/// B9 装备 per-方向 PNG：<c>Assets/Art/Generated/equipment_overlays/</c>。
/// 行索引 0..7 与 <see cref="MiniRPG.Module.Render.DirectionalSpriteHelper.ResolveDirectionRow"/> 一致，
/// 后缀顺序为 s → sw → w → nw → n → ne → e → se（等距八向）。
/// </summary>
internal static class EquipmentOverlayPaths
{
	public const string BasePath = "res://Assets/Art/Generated/equipment_overlays/";

	/// <summary>与 <see cref="MiniRPG.Module.Render.DirectionalSpriteHelper"/> 行 0..7 对齐。</summary>
	private static readonly string[] DirectionSuffixes = ["s", "sw", "w", "nw", "n", "ne", "e", "se"];

	public static string DirectionSuffix(int directionIndex)
	{
		if (directionIndex < 0 || directionIndex >= DirectionSuffixes.Length)
			return DirectionSuffixes[0];
		return DirectionSuffixes[directionIndex];
	}

	public static string WeaponPath(WeaponKind kind, int directionIndex)
	{
		var slug = kind switch
		{
			WeaponKind.Sword => "sword",
			WeaponKind.Axe => "axe",
			WeaponKind.Bow => "bow",
			WeaponKind.Staff => "staff",
			WeaponKind.Mace => "mace",
			_ => "",
		};
		if (string.IsNullOrEmpty(slug))
			return "";
		return $"{BasePath}equip_weapon_{slug}_{DirectionSuffix(directionIndex)}.png";
	}

	public static string CloakPath(CloakKind kind, int directionIndex)
	{
		var slug = kind switch
		{
			CloakKind.ShortCape => "short",
			CloakKind.LongCloak => "long",
			CloakKind.HoodedCloak => "hooded",
			_ => "",
		};
		if (string.IsNullOrEmpty(slug))
			return "";
		return $"{BasePath}equip_cloak_{slug}_{DirectionSuffix(directionIndex)}.png";
	}

	public static string HelmetPath(HelmetKind kind, int directionIndex)
	{
		var slug = kind switch
		{
			HelmetKind.Cap => "cap",
			HelmetKind.FullHelm => "full",
			HelmetKind.Crown => "crown",
			_ => "",
		};
		if (string.IsNullOrEmpty(slug))
			return "";
		return $"{BasePath}equip_helmet_{slug}_{DirectionSuffix(directionIndex)}.png";
	}

	public static bool HasFullWeaponSet(WeaponKind kind)
	{
		if (kind == WeaponKind.None)
			return false;
		for (var i = 0; i < DirectionSuffixes.Length; i++)
		{
			var p = WeaponPath(kind, i);
			if (!ResourceLoader.Exists(p))
				return false;
		}

		return true;
	}

	public static bool HasFullCloakSet(CloakKind kind)
	{
		if (kind == CloakKind.None)
			return false;
		for (var i = 0; i < DirectionSuffixes.Length; i++)
		{
			if (!ResourceLoader.Exists(CloakPath(kind, i)))
				return false;
		}

		return true;
	}

	public static bool HasFullHelmetSet(HelmetKind kind)
	{
		if (kind == HelmetKind.None)
			return false;
		for (var i = 0; i < DirectionSuffixes.Length; i++)
		{
			if (!ResourceLoader.Exists(HelmetPath(kind, i)))
				return false;
		}

		return true;
	}
}
