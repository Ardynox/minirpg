using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Render;

public sealed class CombatFxPlayer
{
	private const int MaxWorldEffects = 24;
	private const int MaxTextEffects = 20;
	private const string EffectsPath = "res://Assets/Art/Placeholders/effects";

	private readonly TileMapRenderModule _mapRender;
	private readonly Node2D _worldRoot;
	private readonly Control _textRoot;
	private readonly Queue<Node> _worldEffects = new();
	private readonly Queue<Control> _textEffects = new();

	public CombatFxPlayer(TileMapRenderModule mapRender, Node2D worldRoot, Control textRoot)
	{
		_mapRender = mapRender;
		_worldRoot = worldRoot;
		_textRoot = textRoot;
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

		var texture = ResolveTexture(command.ResourceId);
		if (texture == null)
			return;

		TrimWorldEffects();
		var sprite = CreateSprite(texture, command);
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

		var texture = ResolveTexture(command.ResourceId);
		if (texture == null)
			return;

		TrimWorldEffects();
		var sprite = CreateSprite(texture, command);
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

	private static Sprite2D CreateSprite(Texture2D texture, CombatFxCommand command)
	{
		return new Sprite2D
		{
			Name = $"CombatFx{command.Kind}",
			Texture = texture,
			Centered = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Scale = Vector2.One * Mathf.Max(command.Scale, 0.01f),
			Modulate = command.Tint,
			ZIndex = command.Layer,
		};
	}

	private static Texture2D? ResolveTexture(string resourceId)
	{
		if (string.IsNullOrWhiteSpace(resourceId))
			return null;

		return ResAccess.Get<Texture2D>($"{EffectsPath}/{resourceId}.png");
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
}
