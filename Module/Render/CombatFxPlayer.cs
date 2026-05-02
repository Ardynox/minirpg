using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Module.Editor;

namespace MiniRPG.Module.Render;

public sealed class CombatFxPlayer
{
	private const int MaxWorldEffects = 24;
	private const int MaxTextEffects = 20;
	private const string EffectsPath = "res://Assets/Art/Placeholders/effects";
	private const string DefaultAnimationName = "default";

	private readonly IsometricVoxelRenderer _mapRender;
	private readonly Node2D _worldRoot;
	private readonly Control _textRoot;
	private readonly Queue<Node> _worldEffects = new();
	private readonly Queue<Control> _textEffects = new();
	private readonly Dictionary<string, ResourceCatalogEntry> _catalogEntries;
	private readonly Dictionary<string, CombatFxVisualAsset> _assetCache = new(StringComparer.OrdinalIgnoreCase);

	public CombatFxPlayer(IsometricVoxelRenderer mapRender, Node2D worldRoot, Control textRoot)
	{
		_mapRender = mapRender;
		_worldRoot = worldRoot;
		_textRoot = textRoot;
		_catalogEntries = BuildCatalogEntries(ResourceCatalogStore.Load().Entries);
	}

	// ResourceCatalogStore 偶尔会因数据表重复 Id 抛多条 entry；旧实现直接 group.Last() 静默吞掉冲突，
	// 导致写错数据后先写的条目消失且毫无提示。这里保留"后写覆盖"行为（与历史一致），但显式 PushWarning
	// 让冲突在启动时可见，便于定位数据表中的重复 Id。
	private static Dictionary<string, ResourceCatalogEntry> BuildCatalogEntries(IReadOnlyList<ResourceCatalogEntry> entries)
	{
		var result = new Dictionary<string, ResourceCatalogEntry>(StringComparer.OrdinalIgnoreCase);
		var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in entries)
		{
			if (string.IsNullOrWhiteSpace(entry.Id))
				continue;
			if (result.ContainsKey(entry.Id) && duplicates.Add(entry.Id))
			{
				GD.PushWarning(
					$"CombatFxPlayer: duplicate resource catalog id '{entry.Id}'; later entry overrides earlier one.");
			}
			result[entry.Id] = entry;
		}

		return result;
	}

	public void Play(IReadOnlyList<CombatFxCommand> commands, int worldZ)
	{
		if (commands.Count == 0)
			return;

		foreach (var command in commands)
		{
			switch (command.Kind)
			{
				case CombatFxCommandKind.Sprite:
					PlaySprite(command, worldZ);
					break;
				case CombatFxCommandKind.Projectile:
					PlayProjectile(command, worldZ);
					break;
				case CombatFxCommandKind.Text:
					PlayText(command, worldZ);
					break;
			}
		}
	}

	private void PlaySprite(CombatFxCommand command, int worldZ)
	{
		if (!_mapRender.TryGetWorldEffectPosition(command.WorldX, command.WorldY, worldZ, out var position))
			return;

		var visual = ResolveVisual(command.ResourceId);
		if (visual == null)
			return;

		TrimWorldEffects();
		var sprite = CreateVisualNode(visual, command);
		sprite.Position = position;
		if (command.RotateToTarget)
		{
			var direction = new Vector2(command.TargetWorldX - command.WorldX, command.TargetWorldY - command.WorldY);
			if (direction.LengthSquared() > 0.001f)
				sprite.Rotation = direction.Angle();
		}

		_worldRoot.AddChild(sprite);
		_worldEffects.Enqueue(sprite);

		var startScale = Vector2.One * Mathf.Max(command.Scale * 0.72f, 0.01f);
		var endScale = Vector2.One * Mathf.Max(command.Scale * 1.06f, 0.01f);
		sprite.Scale = startScale;

		var tween = sprite.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(sprite, "scale", endScale, command.DurationSeconds);
		tween.Parallel().TweenProperty(sprite, "modulate:a", 0.0f, command.DurationSeconds);
		tween.Finished += () => CleanupWorldNode(sprite);
	}

	private void PlayProjectile(CombatFxCommand command, int worldZ)
	{
		if (!_mapRender.TryGetWorldEffectPosition(command.WorldX, command.WorldY, worldZ, out var start)
			|| !_mapRender.TryGetWorldEffectPosition(command.TargetWorldX, command.TargetWorldY, worldZ, out var end))
		{
			return;
		}

		var visual = ResolveVisual(command.ResourceId);
		if (visual == null)
			return;

		TrimWorldEffects();
		var sprite = CreateVisualNode(visual, command);
		sprite.Position = start;
		var delta = end - start;
		if (delta.LengthSquared() > 0.001f)
			sprite.Rotation = delta.Angle();

		_worldRoot.AddChild(sprite);
		_worldEffects.Enqueue(sprite);

		var tween = sprite.CreateTween();
		tween.SetTrans(Tween.TransitionType.Linear);
		tween.SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(sprite, "position", end, command.DurationSeconds);
		tween.Parallel().TweenProperty(sprite, "modulate:a", 0.0f, command.DurationSeconds * 0.45f)
			.SetDelay(command.DurationSeconds * 0.55f);
		tween.Finished += () => CleanupWorldNode(sprite);
	}

	private void PlayText(CombatFxCommand command, int worldZ)
	{
		if (!_mapRender.TryGetWorldOverlayPosition(command.WorldX, command.WorldY, worldZ, out var position))
			return;

		TrimTextEffects();
		var label = new Label
		{
			Name = $"CombatFxText{_textEffects.Count}",
			Text = command.Text,
			ZIndex = command.Layer,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		label.Modulate = command.Tint;
		_textRoot.AddChild(label);
		label.ResetSize();
		var size = label.GetCombinedMinimumSize();
		label.Size = size;
		label.Position = position - new Vector2(size.X / 2f, size.Y);
		_textEffects.Enqueue(label);

		var tween = label.CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(label, "position:y", label.Position.Y - command.RisePixels, command.DurationSeconds);
		tween.Parallel().TweenProperty(label, "modulate:a", 0.0f, command.DurationSeconds);
		tween.Finished += () => CleanupTextNode(label);
	}

	private static Node2D CreateVisualNode(CombatFxVisualAsset visual, CombatFxCommand command)
	{
		if (visual.Frames != null)
		{
			var sprite = new AnimatedSprite2D
			{
				Name = $"CombatFx{command.Kind}",
				SpriteFrames = visual.Frames,
				Animation = DefaultAnimationName,
				Centered = true,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				Scale = Vector2.One * Mathf.Max(command.Scale, 0.01f),
				Modulate = command.Tint,
				ZIndex = command.Layer,
			};
			sprite.Play(DefaultAnimationName);
			return sprite;
		}

		return new Sprite2D
		{
			Name = $"CombatFx{command.Kind}",
			Texture = visual.Texture,
			Centered = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Scale = Vector2.One * Mathf.Max(command.Scale, 0.01f),
			Modulate = command.Tint,
			ZIndex = command.Layer,
		};
	}

	private CombatFxVisualAsset? ResolveVisual(string resourceId)
	{
		if (string.IsNullOrWhiteSpace(resourceId))
			return null;

		if (_assetCache.TryGetValue(resourceId, out var cached))
			return cached;

		var resolved = ResolveFromCatalog(resourceId) ?? ResolveFallbackTexture(resourceId);
		if (resolved != null)
			_assetCache[resourceId] = resolved;

		return resolved;
	}

	private CombatFxVisualAsset? ResolveFromCatalog(string resourceId)
	{
		if (!_catalogEntries.TryGetValue(resourceId, out var entry))
			return null;

		if (string.Equals(entry.Kind, ResourceCatalogKinds.Animation, StringComparison.OrdinalIgnoreCase))
			return ResolveAnimation(entry);

		if (string.IsNullOrWhiteSpace(entry.SourceImagePath))
			return null;

		var texture = LoadTexture(entry.SourceImagePath, entry.Region);
		return texture == null ? null : CombatFxVisualAsset.ForTexture(texture);
	}

	private static CombatFxVisualAsset? ResolveFallbackTexture(string resourceId)
	{
		var texture = ResAccess.Get<Texture2D>($"{EffectsPath}/{resourceId}.png");
		return texture == null ? null : CombatFxVisualAsset.ForTexture(texture);
	}

	private static CombatFxVisualAsset? ResolveAnimation(ResourceCatalogEntry entry)
	{
		if (entry.Frames.Count == 0)
			return null;

		var frames = new SpriteFrames();
		if (!frames.HasAnimation(DefaultAnimationName))
			frames.AddAnimation(DefaultAnimationName);
		frames.SetAnimationLoop(DefaultAnimationName, false);
		frames.SetAnimationSpeed(DefaultAnimationName, Math.Max(entry.Fps ?? 12f, 0.1f));

		foreach (var frame in entry.Frames.OrderBy(frame => frame.Order))
		{
			var texture = LoadTexture(frame.ImagePath, frame.Region);
			if (texture != null)
				frames.AddFrame(DefaultAnimationName, texture);
		}

		return frames.GetFrameCount(DefaultAnimationName) == 0
			? null
			: CombatFxVisualAsset.ForAnimation(frames);
	}

	private static Texture2D? LoadTexture(string path, ResourceCatalogRegion? region)
	{
		if (string.IsNullOrWhiteSpace(path))
			return null;

		var texture = ResAccess.Get<Texture2D>(path);
		if (texture == null || region == null)
			return texture;

		return new AtlasTexture
		{
			Atlas = texture,
			Region = new Rect2(region.X, region.Y, region.Width, region.Height),
		};
	}

	private void TrimWorldEffects()
	{
		while (_worldEffects.Count >= MaxWorldEffects)
		{
			var node = _worldEffects.Dequeue();
			if (IsLiveNode(node))
				node.QueueFree();
		}
	}

	private void TrimTextEffects()
	{
		while (_textEffects.Count >= MaxTextEffects)
		{
			var node = _textEffects.Dequeue();
			if (IsLiveNode(node))
				node.QueueFree();
		}
	}

	private void CleanupWorldNode(Node node)
	{
		if (IsLiveNode(node))
			node.QueueFree();
	}

	private void CleanupTextNode(Control node)
	{
		if (IsLiveNode(node))
			node.QueueFree();
	}

	private static bool IsLiveNode(Node node) =>
		GodotObject.IsInstanceValid(node) && node.GetParent() != null;

	private sealed record CombatFxVisualAsset(Texture2D? Texture, SpriteFrames? Frames)
	{
		public static CombatFxVisualAsset ForTexture(Texture2D texture) => new(texture, null);

		public static CombatFxVisualAsset ForAnimation(SpriteFrames frames) => new(null, frames);
	}
}
