using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MiniRPG.Module.Render;

public enum CombatFxCommandKind
{
	Sprite,
	Projectile,
	Text,
}

public enum CombatFxTextMode
{
	None,
	Damage,
	Block,
}

public enum CombatFxAnchor
{
	Source,
	Target,
}

public sealed record CombatFxCommand
{
	public CombatFxCommandKind Kind { get; init; }
	public CombatFxAnchor Anchor { get; init; } = CombatFxAnchor.Target;
	public string ResourceId { get; init; } = string.Empty;
	public string Text { get; init; } = string.Empty;
	public int WorldX { get; init; }
	public int WorldY { get; init; }
	public int TargetWorldX { get; init; }
	public int TargetWorldY { get; init; }
	public float DurationSeconds { get; init; }
	public float Scale { get; init; } = 1f;
	public Color Tint { get; init; } = Colors.White;
	public int Layer { get; init; }
	public bool RotateToTarget { get; init; }
	public float RisePixels { get; init; } = 48f;
}

public sealed record CombatFxVisualSpec
{
	public CombatFxCommandKind Kind { get; init; }
	public CombatFxAnchor Anchor { get; init; } = CombatFxAnchor.Target;
	public string ResourceId { get; init; } = string.Empty;
	public float DurationSeconds { get; init; } = 0.2f;
	public float Scale { get; init; } = 1f;
	public Color Tint { get; init; } = Colors.White;
	public int Layer { get; init; }
	public bool RotateToTarget { get; init; }
}

public sealed record CombatFxTextSpec
{
	public CombatFxTextMode Mode { get; init; }
	public CombatFxAnchor Anchor { get; init; } = CombatFxAnchor.Target;
	public float DurationSeconds { get; init; } = 0.45f;
	public float RisePixels { get; init; } = 56f;
	public Color Tint { get; init; } = Colors.White;
	public int Layer { get; init; }
}

public sealed record CombatFxSpec
{
	public CombatFxVisualSpec? Primary { get; init; }
	public CombatFxVisualSpec? Hit { get; init; }
	public CombatFxTextSpec? Text { get; init; }
}

public sealed class CombatFxRegistry
{
	private const string DataPath = "combat_fx.json";

	public static readonly CombatFxRegistry Empty = new(
		new Dictionary<string, CombatFxSpec>(StringComparer.OrdinalIgnoreCase),
		"default_attack");

	private readonly Dictionary<string, CombatFxSpec> _effects;
	private readonly string _defaultAttackKey;

	public CombatFxRegistry(Dictionary<string, CombatFxSpec>? effects, string? defaultAttackKey)
	{
		_effects = effects ?? new Dictionary<string, CombatFxSpec>(StringComparer.OrdinalIgnoreCase);
		_defaultAttackKey = string.IsNullOrWhiteSpace(defaultAttackKey) ? "default_attack" : defaultAttackKey;
	}

	public static CombatFxRegistry Load()
	{
		if (!GameDataLocator.TryReadText(DataPath, out var json, out _))
			return Empty;

		return FromJson(json);
	}

	public static CombatFxRegistry FromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return Empty;

		var root = JsonSerializer.Deserialize<CombatFxConfig>(json);
		if (root?.Effects == null || root.Effects.Count == 0)
			return Empty;

		var effects = new Dictionary<string, CombatFxSpec>(StringComparer.OrdinalIgnoreCase);
		foreach (var (key, entry) in root.Effects)
		{
			if (string.IsNullOrWhiteSpace(key))
				continue;

			effects[key] = new CombatFxSpec
			{
				Primary = ToVisualSpec(entry.Primary),
				Hit = ToVisualSpec(entry.Hit),
				Text = ToTextSpec(entry.Text),
			};
		}

		return new CombatFxRegistry(effects, root.DefaultAttack);
	}

	public IReadOnlyList<CombatFxCommand> Resolve(GameEvent gameEvent, bool sourceVisible, bool targetVisible)
	{
		if (gameEvent == null)
			throw new ArgumentNullException(nameof(gameEvent));

		return gameEvent.Type switch
		{
			"combat_attack" => ResolveAttack(gameEvent, sourceVisible, targetVisible),
			"combat_block" => ResolveBlock(gameEvent, sourceVisible),
			_ => Array.Empty<CombatFxCommand>(),
		};
	}

	private IReadOnlyList<CombatFxCommand> ResolveAttack(GameEvent gameEvent, bool sourceVisible, bool targetVisible)
	{
		if (!sourceVisible && !targetVisible)
			return Array.Empty<CombatFxCommand>();

		var effectType = string.IsNullOrWhiteSpace(gameEvent.EffectType)
			? _defaultAttackKey
			: gameEvent.EffectType!;
		var spec = ResolveSpec(effectType);
		var commands = new List<CombatFxCommand>(3);

		if (spec.Primary != null)
			AddVisualCommand(commands, spec.Primary, gameEvent, sourceVisible, targetVisible);
		if (spec.Hit != null && targetVisible)
			AddAnchoredSprite(commands, spec.Hit, gameEvent.TargetX, gameEvent.TargetY, gameEvent);
		if (spec.Text != null)
			AddTextCommand(commands, spec.Text, gameEvent, sourceVisible, targetVisible);

		return commands;
	}

	private IReadOnlyList<CombatFxCommand> ResolveBlock(GameEvent gameEvent, bool sourceVisible)
	{
		if (!sourceVisible)
			return Array.Empty<CombatFxCommand>();

		var spec = ResolveSpec("block");
		var commands = new List<CombatFxCommand>(2);
		if (spec.Primary != null)
			AddAnchoredSprite(commands, spec.Primary, gameEvent.SourceX, gameEvent.SourceY, gameEvent);
		if (spec.Text != null)
			AddTextCommand(commands, spec.Text, gameEvent, sourceVisible: true, targetVisible: false);

		return commands;
	}

	private CombatFxSpec ResolveSpec(string effectType)
	{
		if (_effects.TryGetValue(effectType, out var spec))
			return spec;
		if (_effects.TryGetValue(_defaultAttackKey, out var fallback))
			return fallback;
		return new CombatFxSpec();
	}

	private static void AddVisualCommand(
		ICollection<CombatFxCommand> commands,
		CombatFxVisualSpec spec,
		GameEvent gameEvent,
		bool sourceVisible,
		bool targetVisible)
	{
		switch (spec.Kind)
		{
			case CombatFxCommandKind.Projectile when sourceVisible && targetVisible:
				commands.Add(new CombatFxCommand
				{
					Kind = CombatFxCommandKind.Projectile,
					Anchor = spec.Anchor,
					ResourceId = spec.ResourceId,
					WorldX = gameEvent.SourceX,
					WorldY = gameEvent.SourceY,
					TargetWorldX = gameEvent.TargetX,
					TargetWorldY = gameEvent.TargetY,
					DurationSeconds = spec.DurationSeconds,
					Scale = spec.Scale,
					Tint = spec.Tint,
					Layer = spec.Layer,
					RotateToTarget = true,
				});
				break;
			case CombatFxCommandKind.Sprite:
				var anchorVisible = spec.Anchor == CombatFxAnchor.Source ? sourceVisible : targetVisible;
				if (!anchorVisible)
					return;
				AddAnchoredSprite(
					commands,
					spec,
					spec.Anchor == CombatFxAnchor.Source ? gameEvent.SourceX : gameEvent.TargetX,
					spec.Anchor == CombatFxAnchor.Source ? gameEvent.SourceY : gameEvent.TargetY,
					gameEvent);
				break;
		}
	}

	private static void AddAnchoredSprite(
		ICollection<CombatFxCommand> commands,
		CombatFxVisualSpec spec,
		int worldX,
		int worldY,
		GameEvent gameEvent)
	{
		commands.Add(new CombatFxCommand
		{
			Kind = CombatFxCommandKind.Sprite,
			Anchor = spec.Anchor,
			ResourceId = spec.ResourceId,
			WorldX = worldX,
			WorldY = worldY,
			TargetWorldX = gameEvent.TargetX,
			TargetWorldY = gameEvent.TargetY,
			DurationSeconds = spec.DurationSeconds,
			Scale = spec.Scale,
			Tint = spec.Tint,
			Layer = spec.Layer,
			RotateToTarget = spec.RotateToTarget,
		});
	}

	private static void AddTextCommand(
		ICollection<CombatFxCommand> commands,
		CombatFxTextSpec spec,
		GameEvent gameEvent,
		bool sourceVisible,
		bool targetVisible)
	{
		string text;
		int worldX;
		int worldY;

		switch (spec.Mode)
		{
			case CombatFxTextMode.Damage when targetVisible && gameEvent.Damage > 0:
				text = gameEvent.Damage.ToString(CultureInfo.InvariantCulture);
				worldX = spec.Anchor == CombatFxAnchor.Source ? gameEvent.SourceX : gameEvent.TargetX;
				worldY = spec.Anchor == CombatFxAnchor.Source ? gameEvent.SourceY : gameEvent.TargetY;
				break;
			case CombatFxTextMode.Block when sourceVisible:
				text = string.IsNullOrWhiteSpace(gameEvent.ActionName)
					? LocalizationService.T("data.interaction.block.name")
					: gameEvent.ActionName!;
				worldX = gameEvent.SourceX;
				worldY = gameEvent.SourceY;
				break;
			default:
				return;
		}

		commands.Add(new CombatFxCommand
		{
			Kind = CombatFxCommandKind.Text,
			Anchor = spec.Anchor,
			Text = text,
			WorldX = worldX,
			WorldY = worldY,
			DurationSeconds = spec.DurationSeconds,
			Tint = spec.Tint,
			Layer = spec.Layer,
			RisePixels = spec.RisePixels,
		});
	}

	private static CombatFxVisualSpec? ToVisualSpec(CombatFxVisualConfig? config)
	{
		if (config == null || string.IsNullOrWhiteSpace(config.Kind) || string.IsNullOrWhiteSpace(config.ResourceId))
			return null;

		var kind = config.Kind.Trim().ToLowerInvariant() switch
		{
			"sprite" => CombatFxCommandKind.Sprite,
			"projectile" => CombatFxCommandKind.Projectile,
			_ => (CombatFxCommandKind?)null,
		};
		if (kind == null)
			return null;

		return new CombatFxVisualSpec
		{
			Kind = kind.Value,
			Anchor = ParseAnchor(config.Anchor),
			ResourceId = config.ResourceId,
			DurationSeconds = config.DurationSeconds > 0f ? config.DurationSeconds : 0.2f,
			Scale = config.Scale > 0f ? config.Scale : 1f,
			Tint = ParseColor(config.Tint, Colors.White),
			Layer = config.Layer,
			RotateToTarget = config.RotateToTarget,
		};
	}

	private static CombatFxTextSpec? ToTextSpec(CombatFxTextConfig? config)
	{
		if (config == null || string.IsNullOrWhiteSpace(config.Mode))
			return null;

		var mode = config.Mode.Trim().ToLowerInvariant() switch
		{
			"damage" => CombatFxTextMode.Damage,
			"block" => CombatFxTextMode.Block,
			_ => CombatFxTextMode.None,
		};
		if (mode == CombatFxTextMode.None)
			return null;

		return new CombatFxTextSpec
		{
			Mode = mode,
			Anchor = ParseAnchor(config.Anchor),
			DurationSeconds = config.DurationSeconds > 0f ? config.DurationSeconds : 0.45f,
			RisePixels = config.RisePixels > 0f ? config.RisePixels : 56f,
			Tint = ParseColor(config.Tint, Colors.White),
			Layer = config.Layer,
		};
	}

	private static CombatFxAnchor ParseAnchor(string? anchor) =>
		string.Equals(anchor, "source", StringComparison.OrdinalIgnoreCase)
			? CombatFxAnchor.Source
			: CombatFxAnchor.Target;

	private static Color ParseColor(string? raw, Color fallback)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return fallback;

		return Color.FromString(raw, fallback);
	}

	public sealed class CombatFxConfig
	{
		[JsonPropertyName("defaultAttack")]
		public string DefaultAttack { get; set; } = "default_attack";

		[JsonPropertyName("effects")]
		public Dictionary<string, CombatFxSpecConfig>? Effects { get; set; }
	}

	public sealed class CombatFxSpecConfig
	{
		[JsonPropertyName("primary")]
		public CombatFxVisualConfig? Primary { get; set; }

		[JsonPropertyName("hit")]
		public CombatFxVisualConfig? Hit { get; set; }

		[JsonPropertyName("text")]
		public CombatFxTextConfig? Text { get; set; }
	}

	public sealed class CombatFxVisualConfig
	{
		[JsonPropertyName("kind")]
		public string Kind { get; set; } = string.Empty;

		[JsonPropertyName("anchor")]
		public string Anchor { get; set; } = "target";

		[JsonPropertyName("resourceId")]
		public string ResourceId { get; set; } = string.Empty;

		[JsonPropertyName("duration")]
		public float DurationSeconds { get; set; }

		[JsonPropertyName("scale")]
		public float Scale { get; set; }

		[JsonPropertyName("tint")]
		public string? Tint { get; set; }

		[JsonPropertyName("layer")]
		public int Layer { get; set; }

		[JsonPropertyName("rotateToTarget")]
		public bool RotateToTarget { get; set; }
	}

	public sealed class CombatFxTextConfig
	{
		[JsonPropertyName("mode")]
		public string Mode { get; set; } = string.Empty;

		[JsonPropertyName("anchor")]
		public string Anchor { get; set; } = "target";

		[JsonPropertyName("duration")]
		public float DurationSeconds { get; set; }

		[JsonPropertyName("risePixels")]
		public float RisePixels { get; set; }

		[JsonPropertyName("tint")]
		public string? Tint { get; set; }

		[JsonPropertyName("layer")]
		public int Layer { get; set; }
	}
}
