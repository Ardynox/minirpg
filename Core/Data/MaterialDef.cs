using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Data;

public class MaterialDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("hardness")]
	public float Hardness { get; set; }

	[JsonPropertyName("flammability")]
	public float Flammability { get; set; }

	[JsonPropertyName("corrosionResist")]
	public float CorrosionResist { get; set; }

	[JsonPropertyName("density")]
	public float Density { get; set; } = 1.0f;

	[JsonPropertyName("conductivity")]
	public float Conductivity { get; set; }
}

public static class MaterialRegistry
{
	private static readonly Dictionary<string, MaterialDef> _materials = new();
	private static readonly MaterialDef _fallback = new()
	{
		Id = "unknown",
		Name = "unknown",
		Hardness = 1f,
		Conductivity = 0f,
	};

	public static IReadOnlyDictionary<string, MaterialDef> All => _materials;

	public static void Register(MaterialDef def) => _materials[def.Id] = def;

	public static MaterialDef Get(string id) =>
		_materials.GetValueOrDefault(id, _fallback);
}
