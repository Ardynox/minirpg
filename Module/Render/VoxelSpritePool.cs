using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Render;

internal sealed class VoxelSpritePool
{
	private readonly List<Sprite2D> _sprites = [];
	private int _activeCount;

	public int ActiveCount => _activeCount;

	public void BeginFrame()
	{
		_activeCount = 0;
	}

	public void EndFrame()
	{
		for (var i = _activeCount; i < _sprites.Count; i++)
			_sprites[i].Visible = false;
	}

	public Sprite2D Acquire(Node parent)
	{
		var idx = _activeCount++;
		if (idx < _sprites.Count) return _sprites[idx];
		var sprite = new Sprite2D
		{
			Name = "VoxelSprite" + _sprites.Count.ToString(),
			Centered = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		parent.AddChild(sprite);
		_sprites.Add(sprite);
		return sprite;
	}
}
