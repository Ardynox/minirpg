using Godot;

namespace MiniRPG;

/// <summary>
/// "玩家身上的装备会以什么形状叠加到地图 sprite 上"的分类描述。
///
/// 当前 plan 范围只做 3 大类（武器 / 披风 / 头盔），其它装备（戒指 / 项链 / 鞋）64x64 投影后看不见，
/// 不进 sprite，只在肖像 / inspect 面板里反映。
///
/// 每件 overlay 是个 record struct，存 (是否存在, 颜色)；几何在
/// <see cref="MapSpriteRuntimeFactory"/> 里按 enum 分支画。
/// </summary>
public readonly record struct EquipmentAppearanceData(
	WeaponAppearance Weapon,
	CloakAppearance Cloak,
	HelmetAppearance Helmet)
{
	public static readonly EquipmentAppearanceData Empty = new(
		WeaponAppearance.None,
		CloakAppearance.None,
		HelmetAppearance.None);

	public string ComputeCacheKey() =>
		$"{(int)Weapon.Kind}|{Weapon.TintHex}|{(int)Cloak.Kind}|{Cloak.TintHex}|{(int)Helmet.Kind}|{Helmet.TintHex}";
}

public enum WeaponKind { None, Sword, Axe, Bow, Staff, Mace }
public readonly record struct WeaponAppearance(WeaponKind Kind, Color Tint, string TintHex)
{
	public static readonly WeaponAppearance None = new(WeaponKind.None, Colors.White, "0");
	public static WeaponAppearance Of(WeaponKind kind, Color tint) =>
		new(kind, tint, EquipmentColorUtil.ColorToHex(tint));
}

public enum CloakKind { None, ShortCape, LongCloak, HoodedCloak }
public readonly record struct CloakAppearance(CloakKind Kind, Color Tint, string TintHex)
{
	public static readonly CloakAppearance None = new(CloakKind.None, Colors.White, "0");
	public static CloakAppearance Of(CloakKind kind, Color tint) =>
		new(kind, tint, EquipmentColorUtil.ColorToHex(tint));
}

public enum HelmetKind { None, Cap, FullHelm, Crown }
public readonly record struct HelmetAppearance(HelmetKind Kind, Color Tint, string TintHex)
{
	public static readonly HelmetAppearance None = new(HelmetKind.None, Colors.White, "0");
	public static HelmetAppearance Of(HelmetKind kind, Color tint) =>
		new(kind, tint, EquipmentColorUtil.ColorToHex(tint));
}

internal static class EquipmentColorUtil
{
	public static string ColorToHex(Color c) =>
		$"{(int)(c.R * 255):X2}{(int)(c.G * 255):X2}{(int)(c.B * 255):X2}";
}
