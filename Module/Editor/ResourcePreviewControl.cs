using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Editor;

public partial class ResourcePreviewControl : Control
{
	private static readonly Color CheckerDark = new(0.16f, 0.16f, 0.19f);
	private static readonly Color CheckerLight = new(0.22f, 0.22f, 0.26f);
	private static readonly Color GridColor = new(0.95f, 0.88f, 0.30f, 0.85f);
	private static readonly Color RegionFill = new(0.34f, 0.82f, 1.0f, 0.24f);
	private static readonly Color RegionBorder = new(0.40f, 0.88f, 1.0f, 0.98f);
	private static readonly Color RegionBorderHover = new(1.0f, 0.84f, 0.28f, 1.0f);
	private static readonly Color RegionBorderActive = new(1.0f, 0.60f, 0.20f, 1.0f);
	private static readonly Color HandleFill = new(0.04f, 0.10f, 0.14f, 0.98f);
	private static readonly Color HandleBorder = new(0.88f, 0.97f, 1.0f, 1.0f);
	private static readonly Color HoverFill = new(1.0f, 0.82f, 0.26f, 1.0f);
	private const float CheckerSize = 24f;
	private const float HandleSize = 14f;
	private const float BorderWidth = 3f;
	private const float EdgeHitThickness = 12f;

	private enum HitZone
	{
		None,
		Body,
		Left,
		Right,
		Top,
		Bottom,
		TopLeft,
		TopRight,
		BottomLeft,
		BottomRight,
	}

	private Texture2D? _texture;
	private ResourceCatalogRegion? _region;
	private GridOverlay? _grid;
	private bool _editable;
	private HitZone _hoverZone;
	private HitZone _dragZone;
	private ResourceCatalogRegion? _dragStartRegion;
	private Vector2 _dragStartImagePosition;

	public event Action<ResourceCatalogRegion>? RegionChanged;
	public event Action<bool>? InteractionStateChanged;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		SetProcessInput(true);
	}

	public void SetPreview(Texture2D? texture, ResourceCatalogRegion? region, GridOverlay? grid, bool editable)
	{
		_texture = texture;
		_region = region?.DeepClone();
		_grid = grid;
		_editable = editable && texture != null;
		_hoverZone = HitZone.None;
		if ((!_editable || _region == null) && _dragZone != HitZone.None)
			EndInteraction();
		UpdateCursor();
		QueueRedraw();
	}

	public override void _Input(InputEvent @event)
	{
		if ((_texture == null || !_editable) && _dragZone == HitZone.None)
			return;

		switch (@event)
		{
			case InputEventMouseButton button:
				HandleMouseButton(button, ViewportToLocal(button.Position));
				break;
			case InputEventMouseMotion motion:
				HandleMouseMotion(motion, ViewportToLocal(motion.Position));
				break;
		}
	}

	public override void _Draw()
	{
		DrawChecker();

		if (_texture == null)
			return;

		var textureSize = _texture.GetSize();
		var drawRect = GetTextureRect(textureSize);
		DrawTextureRect(_texture, drawRect, false);

		if (_grid != null)
			DrawGrid(drawRect, textureSize, _grid);

		var drawRegion = GetDrawRegion();
		if (drawRegion == null)
			return;

		DrawRegion(drawRect, textureSize, drawRegion);
		if (_editable)
			DrawHandles(drawRect, textureSize, drawRegion);
	}

	private void HandleMouseButton(InputEventMouseButton button, Vector2 localPosition)
	{
		if (button.ButtonIndex != MouseButton.Left)
			return;

		if (button.Pressed)
		{
			if (!IsPointerNearControl(localPosition))
				return;

			var zone = HitTest(localPosition);
			if (zone == HitZone.None)
				return;

			var drawRegion = GetDrawRegion();
			if (drawRegion == null)
				return;

			_dragZone = zone;
			_dragStartRegion = drawRegion.DeepClone();
			var textureSize = _texture!.GetSize();
			_dragStartImagePosition = ControlToImage(localPosition, textureSize);
			InteractionStateChanged?.Invoke(true);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_dragZone != HitZone.None)
		{
			EndInteraction();
			GetViewport().SetInputAsHandled();
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion motion, Vector2 localPosition)
	{
		if (_dragZone == HitZone.None)
		{
			if (!IsPointerNearControl(localPosition))
			{
				if (_hoverZone != HitZone.None)
				{
					_hoverZone = HitZone.None;
					UpdateCursor();
					QueueRedraw();
				}

				return;
			}

			var nextHover = HitTest(localPosition);
			if (nextHover != _hoverZone)
			{
				_hoverZone = nextHover;
				UpdateCursor();
				QueueRedraw();
			}
			return;
		}

		if (_dragStartRegion == null || _texture == null)
			return;

		var textureSize = _texture.GetSize();
		var current = ControlToImage(localPosition, textureSize);
		var imageBounds = new Rect2(Vector2.Zero, textureSize);
		var updated = ResizeOrMove(_dragStartRegion, _dragZone, _dragStartImagePosition, current);
		var snapped = motion.ShiftPressed ? SnapRegion(updated, _dragZone, textureSize) : updated;
		var clamped = ClampRegion(snapped, imageBounds);

		_region = clamped;
		RegionChanged?.Invoke(clamped.DeepClone());
		_hoverZone = _dragZone;
		UpdateCursor();
		QueueRedraw();
		GetViewport().SetInputAsHandled();
	}

	private void EndInteraction()
	{
		_dragZone = HitZone.None;
		_dragStartRegion = null;
		_hoverZone = HitTest(GetLocalMousePosition());
		UpdateCursor();
		InteractionStateChanged?.Invoke(false);
		QueueRedraw();
	}

	private void UpdateCursor()
	{
		MouseDefaultCursorShape = _dragZone != HitZone.None
			? GetCursorShape(_dragZone)
			: GetCursorShape(_hoverZone);
	}

	private static CursorShape GetCursorShape(HitZone zone)
	{
		return zone switch
		{
			HitZone.Body => CursorShape.Move,
			HitZone.Left or HitZone.Right => CursorShape.Hsize,
			HitZone.Top or HitZone.Bottom => CursorShape.Vsize,
			HitZone.TopLeft or HitZone.BottomRight => CursorShape.Fdiagsize,
			HitZone.TopRight or HitZone.BottomLeft => CursorShape.Bdiagsize,
			_ => CursorShape.Arrow,
		};
	}

	private void DrawChecker()
	{
		for (var y = 0f; y < Size.Y; y += CheckerSize)
		{
			for (var x = 0f; x < Size.X; x += CheckerSize)
			{
				var rect = new Rect2(x, y, CheckerSize, CheckerSize);
				var isLight = (((int)(x / CheckerSize)) + ((int)(y / CheckerSize))) % 2 == 0;
				DrawRect(rect, isLight ? CheckerLight : CheckerDark);
			}
		}
	}

	private void DrawGrid(Rect2 drawRect, Vector2 textureSize, GridOverlay grid)
	{
		if (grid.CellWidth <= 0 || grid.CellHeight <= 0)
			return;

		var scaleX = drawRect.Size.X / textureSize.X;
		var scaleY = drawRect.Size.Y / textureSize.Y;

		foreach (var x in BuildSnapLines(grid.OffsetX, grid.CellWidth, grid.SpacingX, (int)textureSize.X))
		{
			var lineX = drawRect.Position.X + x * scaleX;
			DrawLine(
				new Vector2(lineX, drawRect.Position.Y),
				new Vector2(lineX, drawRect.End.Y),
				GridColor,
				1.5f);
		}

		foreach (var y in BuildSnapLines(grid.OffsetY, grid.CellHeight, grid.SpacingY, (int)textureSize.Y))
		{
			var lineY = drawRect.Position.Y + y * scaleY;
			DrawLine(
				new Vector2(drawRect.Position.X, lineY),
				new Vector2(drawRect.End.X, lineY),
				GridColor,
				1.5f);
		}
	}

	private void DrawRegion(Rect2 drawRect, Vector2 textureSize, ResourceCatalogRegion region)
	{
		var rect = ImageRegionToControlRect(drawRect, textureSize, region);
		var borderColor = _dragZone != HitZone.None
			? RegionBorderActive
			: _hoverZone != HitZone.None
				? RegionBorderHover
				: RegionBorder;
		DrawRect(rect, RegionFill, filled: true);
		DrawRect(rect, borderColor, filled: false, width: BorderWidth);
	}

	private void DrawHandles(Rect2 drawRect, Vector2 textureSize, ResourceCatalogRegion region)
	{
		var rect = ImageRegionToControlRect(drawRect, textureSize, region);
		foreach (var handle in GetHandleRects(rect))
		{
			var fill = handle.Key == _hoverZone || handle.Key == _dragZone ? HoverFill : HandleFill;
			DrawRect(handle.Value, fill, filled: true);
			DrawRect(handle.Value, HandleBorder, filled: false, width: 1.5f);
		}
	}

	private Dictionary<HitZone, Rect2> GetHandleRects(Rect2 regionRect)
	{
		var half = HandleSize / 2f;
		var center = regionRect.Position + regionRect.Size / 2f;
		return new Dictionary<HitZone, Rect2>
		{
			[HitZone.TopLeft] = new(regionRect.Position.X - half, regionRect.Position.Y - half, HandleSize, HandleSize),
			[HitZone.Top] = new(center.X - half, regionRect.Position.Y - half, HandleSize, HandleSize),
			[HitZone.TopRight] = new(regionRect.End.X - half, regionRect.Position.Y - half, HandleSize, HandleSize),
			[HitZone.Right] = new(regionRect.End.X - half, center.Y - half, HandleSize, HandleSize),
			[HitZone.BottomRight] = new(regionRect.End.X - half, regionRect.End.Y - half, HandleSize, HandleSize),
			[HitZone.Bottom] = new(center.X - half, regionRect.End.Y - half, HandleSize, HandleSize),
			[HitZone.BottomLeft] = new(regionRect.Position.X - half, regionRect.End.Y - half, HandleSize, HandleSize),
			[HitZone.Left] = new(regionRect.Position.X - half, center.Y - half, HandleSize, HandleSize),
		};
	}

	private HitZone HitTest(Vector2 controlPosition)
	{
		if (_texture == null)
			return HitZone.None;

		var textureSize = _texture.GetSize();
		var drawRect = GetTextureRect(textureSize);
		if (!drawRect.Grow(HandleSize).HasPoint(controlPosition))
			return HitZone.None;

		var drawRegion = GetDrawRegion();
		if (drawRegion == null)
			return HitZone.None;

		var regionRect = ImageRegionToControlRect(drawRect, textureSize, drawRegion);
		var edgeZone = HitTestEdges(regionRect, controlPosition);
		if (edgeZone != HitZone.None)
			return edgeZone;

		foreach (var handle in GetHandleRects(regionRect))
		{
			if (handle.Value.HasPoint(controlPosition))
				return handle.Key;
		}

		return regionRect.HasPoint(controlPosition) ? HitZone.Body : HitZone.None;
	}

	private static HitZone HitTestEdges(Rect2 regionRect, Vector2 controlPosition)
	{
		var nearLeft = Mathf.Abs(controlPosition.X - regionRect.Position.X) <= EdgeHitThickness;
		var nearRight = Mathf.Abs(controlPosition.X - regionRect.End.X) <= EdgeHitThickness;
		var nearTop = Mathf.Abs(controlPosition.Y - regionRect.Position.Y) <= EdgeHitThickness;
		var nearBottom = Mathf.Abs(controlPosition.Y - regionRect.End.Y) <= EdgeHitThickness;
		var withinX = controlPosition.X >= regionRect.Position.X - EdgeHitThickness
			&& controlPosition.X <= regionRect.End.X + EdgeHitThickness;
		var withinY = controlPosition.Y >= regionRect.Position.Y - EdgeHitThickness
			&& controlPosition.Y <= regionRect.End.Y + EdgeHitThickness;

		if (nearLeft && nearTop)
			return HitZone.TopLeft;
		if (nearRight && nearTop)
			return HitZone.TopRight;
		if (nearLeft && nearBottom)
			return HitZone.BottomLeft;
		if (nearRight && nearBottom)
			return HitZone.BottomRight;
		if (nearLeft && withinY)
			return HitZone.Left;
		if (nearRight && withinY)
			return HitZone.Right;
		if (nearTop && withinX)
			return HitZone.Top;
		if (nearBottom && withinX)
			return HitZone.Bottom;

		return HitZone.None;
	}

	private ResourceCatalogRegion? GetDrawRegion() => _region?.DeepClone();

	private Vector2 ViewportToLocal(Vector2 viewportPosition) =>
		GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;

	private bool IsPointerNearControl(Vector2 localPosition) =>
		new Rect2(-HandleSize, -HandleSize, Size.X + HandleSize * 2f, Size.Y + HandleSize * 2f).HasPoint(localPosition);

	private Rect2 GetTextureRect(Vector2 textureSize)
	{
		if (textureSize.X <= 0 || textureSize.Y <= 0)
			return new Rect2(Vector2.Zero, Size);

		var widthScale = Size.X / textureSize.X;
		var heightScale = Size.Y / textureSize.Y;
		var scale = Mathf.Min(widthScale, heightScale);
		var drawSize = new Vector2(textureSize.X * scale, textureSize.Y * scale);
		var position = (Size - drawSize) / 2f;
		return new Rect2(position, drawSize);
	}

	private Vector2 ControlToImage(Vector2 controlPosition, Vector2 textureSize)
	{
		var drawRect = GetTextureRect(textureSize);
		var local = controlPosition - drawRect.Position;
		var scaleX = drawRect.Size.X / textureSize.X;
		var scaleY = drawRect.Size.Y / textureSize.Y;
		return new Vector2(local.X / scaleX, local.Y / scaleY);
	}

	private static Rect2 ImageRegionToControlRect(Rect2 drawRect, Vector2 textureSize, ResourceCatalogRegion region)
	{
		var scaleX = drawRect.Size.X / textureSize.X;
		var scaleY = drawRect.Size.Y / textureSize.Y;
		return new Rect2(
			drawRect.Position.X + region.X * scaleX,
			drawRect.Position.Y + region.Y * scaleY,
			region.Width * scaleX,
			region.Height * scaleY);
	}

	private static ResourceCatalogRegion ResizeOrMove(
		ResourceCatalogRegion startRegion,
		HitZone zone,
		Vector2 dragStart,
		Vector2 current)
	{
		var delta = current - dragStart;
		var left = (float)startRegion.X;
		var top = (float)startRegion.Y;
		var right = (float)(startRegion.X + startRegion.Width);
		var bottom = (float)(startRegion.Y + startRegion.Height);

		switch (zone)
		{
			case HitZone.Body:
				left += delta.X;
				right += delta.X;
				top += delta.Y;
				bottom += delta.Y;
				break;
			case HitZone.Left:
				left += delta.X;
				break;
			case HitZone.Right:
				right += delta.X;
				break;
			case HitZone.Top:
				top += delta.Y;
				break;
			case HitZone.Bottom:
				bottom += delta.Y;
				break;
			case HitZone.TopLeft:
				left += delta.X;
				top += delta.Y;
				break;
			case HitZone.TopRight:
				right += delta.X;
				top += delta.Y;
				break;
			case HitZone.BottomLeft:
				left += delta.X;
				bottom += delta.Y;
				break;
			case HitZone.BottomRight:
				right += delta.X;
				bottom += delta.Y;
				break;
		}

		if (left > right - 1)
			left = right - 1;
		if (top > bottom - 1)
			top = bottom - 1;

		return new ResourceCatalogRegion
		{
			X = Mathf.RoundToInt(left),
			Y = Mathf.RoundToInt(top),
			Width = Mathf.Max(1, Mathf.RoundToInt(right - left)),
			Height = Mathf.Max(1, Mathf.RoundToInt(bottom - top)),
		};
	}

	private ResourceCatalogRegion SnapRegion(ResourceCatalogRegion region, HitZone zone, Vector2 textureSize)
	{
		var left = region.X;
		var top = region.Y;
		var right = region.X + region.Width;
		var bottom = region.Y + region.Height;

		switch (zone)
		{
			case HitZone.Body:
				left = SnapCoordinate(left, horizontal: true, textureSize);
				top = SnapCoordinate(top, horizontal: false, textureSize);
				right = left + region.Width;
				bottom = top + region.Height;
				break;
			case HitZone.Left:
				left = SnapCoordinate(left, horizontal: true, textureSize);
				break;
			case HitZone.Right:
				right = SnapCoordinate(right, horizontal: true, textureSize);
				break;
			case HitZone.Top:
				top = SnapCoordinate(top, horizontal: false, textureSize);
				break;
			case HitZone.Bottom:
				bottom = SnapCoordinate(bottom, horizontal: false, textureSize);
				break;
			case HitZone.TopLeft:
				left = SnapCoordinate(left, horizontal: true, textureSize);
				top = SnapCoordinate(top, horizontal: false, textureSize);
				break;
			case HitZone.TopRight:
				right = SnapCoordinate(right, horizontal: true, textureSize);
				top = SnapCoordinate(top, horizontal: false, textureSize);
				break;
			case HitZone.BottomLeft:
				left = SnapCoordinate(left, horizontal: true, textureSize);
				bottom = SnapCoordinate(bottom, horizontal: false, textureSize);
				break;
			case HitZone.BottomRight:
				right = SnapCoordinate(right, horizontal: true, textureSize);
				bottom = SnapCoordinate(bottom, horizontal: false, textureSize);
				break;
		}

		if (left > right - 1)
			left = right - 1;
		if (top > bottom - 1)
			top = bottom - 1;

		return new ResourceCatalogRegion
		{
			X = left,
			Y = top,
			Width = Mathf.Max(1, right - left),
			Height = Mathf.Max(1, bottom - top),
		};
	}

	private int SnapCoordinate(int value, bool horizontal, Vector2 textureSize)
	{
		var limit = horizontal ? Mathf.RoundToInt(textureSize.X) : Mathf.RoundToInt(textureSize.Y);
		var clamped = Mathf.Clamp(value, 0, limit);
		if (_grid == null)
			return clamped;

		var offset = horizontal ? _grid.OffsetX : _grid.OffsetY;
		var cell = horizontal ? _grid.CellWidth : _grid.CellHeight;
		var spacing = horizontal ? _grid.SpacingX : _grid.SpacingY;
		if (cell <= 0)
			return clamped;

		var best = clamped;
		var bestDistance = int.MaxValue;
		foreach (var line in BuildSnapLines(offset, cell, spacing, limit))
		{
			var distance = Mathf.Abs(line - clamped);
			if (distance >= bestDistance)
				continue;

			best = line;
			bestDistance = distance;
		}

		return best;
	}

	private static List<int> BuildSnapLines(int offset, int cell, int spacing, int limit)
	{
		var lines = new List<int> { 0, limit };
		if (cell <= 0)
			return lines;

		var step = cell + spacing;
		if (step <= 0)
			step = cell;

		for (var start = offset; start <= limit; start += step)
		{
			if (start >= 0 && start <= limit)
				lines.Add(start);

			var end = start + cell;
			if (end >= 0 && end <= limit)
				lines.Add(end);

			if (step == 0)
				break;
		}

		lines.Sort();
		var deduped = new List<int>(lines.Count);
		foreach (var line in lines)
		{
			if (deduped.Count == 0 || deduped[^1] != line)
				deduped.Add(line);
		}

		return deduped;
	}

	private static ResourceCatalogRegion ClampRegion(ResourceCatalogRegion region, Rect2 imageBounds)
	{
		var left = Mathf.Clamp(region.X, Mathf.FloorToInt(imageBounds.Position.X), Mathf.CeilToInt(imageBounds.End.X) - 1);
		var top = Mathf.Clamp(region.Y, Mathf.FloorToInt(imageBounds.Position.Y), Mathf.CeilToInt(imageBounds.End.Y) - 1);
		var right = Mathf.Clamp(left + region.Width, left + 1, Mathf.CeilToInt(imageBounds.End.X));
		var bottom = Mathf.Clamp(top + region.Height, top + 1, Mathf.CeilToInt(imageBounds.End.Y));

		return new ResourceCatalogRegion
		{
			X = left,
			Y = top,
			Width = Mathf.Max(1, right - left),
			Height = Mathf.Max(1, bottom - top),
		};
	}
}

public sealed class GridOverlay
{
	public int CellWidth { get; init; }
	public int CellHeight { get; init; }
	public int OffsetX { get; init; }
	public int OffsetY { get; init; }
	public int SpacingX { get; init; }
	public int SpacingY { get; init; }
}
