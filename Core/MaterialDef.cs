using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core;

/// <summary>
/// 材质定义。所有物理实体（肢体、物品、地形）都有材质，
/// 材质决定硬度、可燃性、耐腐蚀性、密度等物理属性。
/// </summary>
public class MaterialDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	/// <summary>硬度：影响挖掘/破坏效率和肢体攻击/防御加成。</summary>
	[JsonPropertyName("hardness")]
	public float Hardness { get; set; }

	/// <summary>可燃性：0=不可燃，1=极易燃。</summary>
	[JsonPropertyName("flammability")]
	public float Flammability { get; set; }

	/// <summary>耐腐蚀性：0=极易腐蚀，1=完全耐腐蚀。</summary>
	[JsonPropertyName("corrosionResist")]
	public float CorrosionResist { get; set; }

	/// <summary>密度 (g/cm³)：影响重量计算。</summary>
	[JsonPropertyName("density")]
	public float Density { get; set; } = 1.0f;
}

/// <summary>材质注册表：提供按 ID 查询的全局访问。</summary>
public static class MaterialRegistry
{
	private static readonly Dictionary<string, MaterialDef> _materials = new();
	private static readonly MaterialDef _fallback = new() { Id = "unknown", Name = "未知", Hardness = 1 };

	public static IReadOnlyDictionary<string, MaterialDef> All => _materials;

	public static void Register(MaterialDef def) => _materials[def.Id] = def;

	public static MaterialDef Get(string id) =>
		_materials.GetValueOrDefault(id, _fallback);
}
