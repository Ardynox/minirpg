using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Tools;

public partial class PlaceholderCanvasControl : Control
{
	public enum ToolMode
	{
		Pencil,
		Eraser,
		Bucket,
		Eyedropper,
		Pan,
	}

	private sealed class ImageSnapshot
	{
		public int Width { get; init; }
		public int Height { get; init; }
		public Image.Format Format { get; init; }
		public byte[] Data { get; init; } = [];
	}

	private static readonly Color CheckerDark = new(0.16f, 0.16f, 0.18f);
	private static readonly Color CheckerLight = new(0.22f, 0.22f, 0.25f);
	private static readonly Color ImageBorder = new(0.88f, 0.90f, 0.96f, 0.9f);
	private static readonly Color GridLine = new(1.0f, 1.0f, 1.0f, 0.18f);
	private static readonly Color HoverOutline = new(0.96f, 0.78f, 0.22f, 0.95f);
	private const int MaxUndoSteps = 20;
	private const float CheckerSize = 18f;
	private const float MinZoom = 0.125f;
	private const float MaxZoom = 64f;
	private const float ZoomStep = 1.25f;

	private readonly Stack<ImageSnapshot> _undoStack = [];
	private readonly Stack<ImageSnapshot> _redoStack = [];

	private Image? _image;
	private ImageTexture? _texture;
	private Image? _overlayImage;
	private ImageTexture? _overlayTexture;
	private float _overlayOpacity;
	private ToolMode _tool = ToolMode.Pencil;
	private Color _primaryColor = Colors.White;
	private bool _showGrid = true;
	private bool _isPainting;
	private bool _isPanning;
	private Vector2 _panOffset = Vector2.Zero;
	private Vector2 _panDragOrigin = Vector2.Zero;
	private Vector2 _panOffsetOrigin = Vector2.Zero;
	private Vector2I? _lastPaintPixel;
	private Vector2I? _hoverPixel;
	private float _zoom = 1f;
	private bool _hasManualView;

	public event Action? StateChanged;
	public event Action? ImageModified;
	public event Action<Color>? ColorPicked;
	public event Action<Vector2I?>? HoverPixelChanged;

	public ToolMode Tool
	{
		get => _tool;
		set => _tool = value;
	}

	public Color PrimaryColor
	{
		get => _primaryColor;
		set => _primaryColor = value;
	}

	public bool ShowGrid
	{
		get => _showGrid;
		set
		{
			if (_showGrid == value)
				return;

			_showGrid = value;
			QueueRedraw();
			StateChanged?.Invoke();
		}
	}

	public float Zoom => _zoom;
	public bool CanUndo => _undoStack.Count > 0;
	public bool CanRedo => _redoStack.Count > 0;
	public Vector2I CanvasSize => _image == null ? Vector2I.Zero : new Vector2I(_image.GetWidth(), _image.GetHeight());

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		ClipContents = true;
		TextureFilter = TextureFilterEnum.Nearest;
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (_image == null)
			return;

		switch (@event)
		{
			case InputEventMouseButton button:
				HandleMouseButton(button);
				break;
			case InputEventMouseMotion motion:
				HandleMouseMotion(motion);
				break;
		}
	}

	public override void _Draw()
	{
		DrawChecker();
		if (_image == null || _texture == null)
			return;

		var imageRect = GetImageRect();
		if (_overlayTexture != null && _overlayOpacity > 0f)
			DrawTextureRect(_overlayTexture, imageRect, tile: false, modulate: new Color(1f, 1f, 1f, _overlayOpacity));
		DrawTextureRect(_texture, imageRect, tile: false);
		DrawRect(imageRect, ImageBorder, filled: false, width: 1.5f);

		if (_showGrid && _zoom >= 6f)
			DrawPixelGrid(imageRect);

		if (_hoverPixel.HasValue)
			DrawHoverPixel(imageRect, _hoverPixel.Value);
	}

	public void LoadImage(Image image, bool fitToView = true)
	{
		_image = DuplicateImage(image);
		_texture = null;
		_undoStack.Clear();
		_redoStack.Clear();
		_lastPaintPixel = null;
		_hoverPixel = null;
		_isPainting = false;
		_isPanning = false;

		if (fitToView || !_hasManualView)
			FitToView();

		RefreshTexture();
		StateChanged?.Invoke();
	}

	public void SetOverlayImage(Image? image, float opacity)
	{
		_overlayOpacity = Mathf.Clamp(opacity, 0f, 1f);
		if (image == null || _overlayOpacity <= 0f)
		{
			_overlayImage = null;
			_overlayTexture = null;
			QueueRedraw();
			StateChanged?.Invoke();
			return;
		}

		_overlayImage = DuplicateImage(image);
		if (_overlayTexture == null)
			_overlayTexture = ImageTexture.CreateFromImage(_overlayImage);
		else
			_overlayTexture.Update(_overlayImage);

		QueueRedraw();
		StateChanged?.Invoke();
	}

	public Image GetImageCopy() => _image == null
		? Image.CreateEmpty(1, 1, false, Image.Format.Rgba8)
		: DuplicateImage(_image);

	public void ClearImage()
	{
		if (_image == null)
			return;

		BeginMutation();
		_image.Fill(Colors.Transparent);
		RefreshTexture();
		ImageModified?.Invoke();
		StateChanged?.Invoke();
	}

	public void Undo()
	{
		if (_image == null || _undoStack.Count == 0)
			return;

		_redoStack.Push(CaptureSnapshot(_image));
		_image = RestoreSnapshot(_undoStack.Pop());
		RefreshTexture();
		ImageModified?.Invoke();
		StateChanged?.Invoke();
	}

	public void Redo()
	{
		if (_image == null || _redoStack.Count == 0)
			return;

		_undoStack.Push(CaptureSnapshot(_image));
		_image = RestoreSnapshot(_redoStack.Pop());
		RefreshTexture();
		ImageModified?.Invoke();
		StateChanged?.Invoke();
	}

	public void ZoomIn() => ApplyZoom(_zoom * ZoomStep, Size / 2f);

	public void ZoomOut() => ApplyZoom(_zoom / ZoomStep, Size / 2f);

	public void ResetView()
	{
		_hasManualView = false;
		FitToView();
		QueueRedraw();
		StateChanged?.Invoke();
	}

	private void HandleMouseButton(InputEventMouseButton button)
	{
		var localPosition = GetLocalMousePosition();
		switch (button.ButtonIndex)
		{
			case MouseButton.WheelUp when button.Pressed:
				ApplyZoom(_zoom * ZoomStep, localPosition);
				AcceptEvent();
				return;
			case MouseButton.WheelDown when button.Pressed:
				ApplyZoom(_zoom / ZoomStep, localPosition);
				AcceptEvent();
				return;
			case MouseButton.Middle:
				if (button.Pressed)
				{
					BeginPan(localPosition);
					AcceptEvent();
				}
				else if (_isPanning)
				{
					EndPan();
					AcceptEvent();
				}

				return;
			case MouseButton.Left:
				break;
			default:
				return;
		}

		if (_tool == ToolMode.Pan)
		{
			if (button.Pressed)
			{
				BeginPan(localPosition);
				AcceptEvent();
			}
			else if (_isPanning)
			{
				EndPan();
				AcceptEvent();
			}

			return;
		}

		if (!button.Pressed)
		{
			if (_isPainting)
			{
				_isPainting = false;
				_lastPaintPixel = null;
				StateChanged?.Invoke();
				AcceptEvent();
			}

			return;
		}

		var pixel = GetPixelAtLocal(localPosition);
		if (pixel == null)
			return;

		switch (_tool)
		{
			case ToolMode.Pencil:
			case ToolMode.Eraser:
				BeginMutation();
				_isPainting = true;
				_lastPaintPixel = pixel;
				PlotPixel(pixel.Value);
				RefreshTexture();
				ImageModified?.Invoke();
				StateChanged?.Invoke();
				AcceptEvent();
				break;
			case ToolMode.Bucket:
				BeginMutation();
				FloodFill(pixel.Value);
				RefreshTexture();
				ImageModified?.Invoke();
				StateChanged?.Invoke();
				AcceptEvent();
				break;
			case ToolMode.Eyedropper:
				SampleColor(pixel.Value);
				AcceptEvent();
				break;
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion motion)
	{
		var localPosition = GetLocalMousePosition();
		UpdateHover(localPosition);

		if (_isPanning)
		{
			_panOffset = _panOffsetOrigin + (localPosition - _panDragOrigin);
			_hasManualView = true;
			QueueRedraw();
			StateChanged?.Invoke();
			AcceptEvent();
			return;
		}

		if (!_isPainting || _image == null || _lastPaintPixel == null)
			return;

		var pixel = GetPixelAtLocal(localPosition);
		if (pixel == null)
			return;

		DrawStroke(_lastPaintPixel.Value, pixel.Value);
		_lastPaintPixel = pixel;
		RefreshTexture();
		ImageModified?.Invoke();
		StateChanged?.Invoke();
		AcceptEvent();
	}

	private void BeginPan(Vector2 localPosition)
	{
		_isPanning = true;
		_isPainting = false;
		_lastPaintPixel = null;
		_panDragOrigin = localPosition;
		_panOffsetOrigin = _panOffset;
	}

	private void EndPan()
	{
		_isPanning = false;
		StateChanged?.Invoke();
	}

	private void UpdateHover(Vector2 localPosition)
	{
		var next = GetPixelAtLocal(localPosition);
		if (_hoverPixel == next)
			return;

		_hoverPixel = next;
		HoverPixelChanged?.Invoke(_hoverPixel);
		QueueRedraw();
	}

	private void ApplyZoom(float requestedZoom, Vector2 focusPoint)
	{
		if (_image == null)
			return;

		var oldRect = GetImageRect();
		var oldZoom = _zoom;
		var newZoom = Mathf.Clamp(requestedZoom, MinZoom, MaxZoom);
		if (Mathf.IsEqualApprox(oldZoom, newZoom))
			return;

		var focusImage = LocalToImageFloat(focusPoint, oldRect);
		_zoom = newZoom;
		var newRect = GetImageRect();
		var desiredPosition = focusPoint - focusImage * _zoom;
		_panOffset += desiredPosition - newRect.Position;
		_hasManualView = true;
		QueueRedraw();
		StateChanged?.Invoke();
	}

	private void FitToView()
	{
		if (_image == null || Size.X <= 0 || Size.Y <= 0)
		{
			_zoom = 1f;
			_panOffset = Vector2.Zero;
			return;
		}

		var imageSize = new Vector2(_image.GetWidth(), _image.GetHeight());
		var scaleX = Size.X / imageSize.X;
		var scaleY = Size.Y / imageSize.Y;
		_zoom = Mathf.Clamp(Mathf.Min(scaleX, scaleY) * 0.92f, MinZoom, MaxZoom);
		_panOffset = Vector2.Zero;
	}

	private Rect2 GetImageRect()
	{
		if (_image == null)
			return new Rect2(Vector2.Zero, Size);

		var drawSize = new Vector2(_image.GetWidth(), _image.GetHeight()) * _zoom;
		var position = (Size - drawSize) / 2f + _panOffset;
		return new Rect2(position, drawSize);
	}

	private Vector2I? GetPixelAtLocal(Vector2 localPosition)
	{
		if (_image == null)
			return null;

		var rect = GetImageRect();
		if (!rect.HasPoint(localPosition))
			return null;

		var imagePosition = LocalToImageFloat(localPosition, rect);
		var x = Mathf.Clamp((int)MathF.Floor(imagePosition.X), 0, _image.GetWidth() - 1);
		var y = Mathf.Clamp((int)MathF.Floor(imagePosition.Y), 0, _image.GetHeight() - 1);
		return new Vector2I(x, y);
	}

	private Vector2 LocalToImageFloat(Vector2 localPosition, Rect2 imageRect)
	{
		var imageSize = new Vector2(_image!.GetWidth(), _image.GetHeight());
		var scaleX = imageRect.Size.X / imageSize.X;
		var scaleY = imageRect.Size.Y / imageSize.Y;
		var local = localPosition - imageRect.Position;
		return new Vector2(local.X / scaleX, local.Y / scaleY);
	}

	private void PlotPixel(Vector2I pixel)
	{
		if (_image == null)
			return;

		_image.SetPixel(pixel.X, pixel.Y, _tool == ToolMode.Eraser ? Colors.Transparent : _primaryColor);
	}

	private void DrawStroke(Vector2I from, Vector2I to)
	{
		var x0 = from.X;
		var y0 = from.Y;
		var x1 = to.X;
		var y1 = to.Y;
		var dx = Math.Abs(x1 - x0);
		var dy = Math.Abs(y1 - y0);
		var sx = x0 < x1 ? 1 : -1;
		var sy = y0 < y1 ? 1 : -1;
		var err = dx - dy;

		while (true)
		{
			PlotPixel(new Vector2I(x0, y0));
			if (x0 == x1 && y0 == y1)
				break;

			var e2 = err * 2;
			if (e2 > -dy)
			{
				err -= dy;
				x0 += sx;
			}

			if (e2 < dx)
			{
				err += dx;
				y0 += sy;
			}
		}
	}

	private void FloodFill(Vector2I start)
	{
		if (_image == null)
			return;

		var target = _image.GetPixel(start.X, start.Y);
		var replacement = _tool == ToolMode.Eraser ? Colors.Transparent : _primaryColor;
		if (AreColorsEqual(target, replacement))
			return;

		var width = _image.GetWidth();
		var height = _image.GetHeight();
		var queue = new Queue<Vector2I>();
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			if (current.X < 0 || current.X >= width || current.Y < 0 || current.Y >= height)
				continue;
			if (!AreColorsEqual(_image.GetPixel(current.X, current.Y), target))
				continue;

			_image.SetPixel(current.X, current.Y, replacement);
			queue.Enqueue(new Vector2I(current.X - 1, current.Y));
			queue.Enqueue(new Vector2I(current.X + 1, current.Y));
			queue.Enqueue(new Vector2I(current.X, current.Y - 1));
			queue.Enqueue(new Vector2I(current.X, current.Y + 1));
		}
	}

	private void SampleColor(Vector2I pixel)
	{
		if (_image == null)
			return;

		var color = _image.GetPixel(pixel.X, pixel.Y);
		_primaryColor = color;
		ColorPicked?.Invoke(color);
		StateChanged?.Invoke();
	}

	private void BeginMutation()
	{
		if (_image == null)
			return;

		_undoStack.Push(CaptureSnapshot(_image));
		while (_undoStack.Count > MaxUndoSteps)
			TrimUndoBase();
		_redoStack.Clear();
	}

	private void TrimUndoBase()
	{
		if (_undoStack.Count <= MaxUndoSteps)
			return;

		var snapshots = _undoStack.ToArray();
		Array.Resize(ref snapshots, MaxUndoSteps);
		_undoStack.Clear();
		for (var i = snapshots.Length - 1; i >= 0; i--)
			_undoStack.Push(snapshots[i]);
	}

	private void RefreshTexture()
	{
		if (_image == null)
			return;

		if (_texture == null)
			_texture = ImageTexture.CreateFromImage(_image);
		else
			_texture.Update(_image);

		QueueRedraw();
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

	private void DrawPixelGrid(Rect2 imageRect)
	{
		if (_image == null)
			return;

		for (var x = 0; x <= _image.GetWidth(); x++)
		{
			var px = imageRect.Position.X + x * _zoom;
			DrawLine(new Vector2(px, imageRect.Position.Y), new Vector2(px, imageRect.End.Y), GridLine, 1f);
		}

		for (var y = 0; y <= _image.GetHeight(); y++)
		{
			var py = imageRect.Position.Y + y * _zoom;
			DrawLine(new Vector2(imageRect.Position.X, py), new Vector2(imageRect.End.X, py), GridLine, 1f);
		}
	}

	private void DrawHoverPixel(Rect2 imageRect, Vector2I pixel)
	{
		if (_image == null)
			return;

		var pixelRect = new Rect2(
			imageRect.Position.X + pixel.X * _zoom,
			imageRect.Position.Y + pixel.Y * _zoom,
			_zoom,
			_zoom);
		DrawRect(pixelRect, HoverOutline, filled: false, width: 2f);
	}

	private static bool AreColorsEqual(Color a, Color b) =>
		Mathf.IsEqualApprox(a.R, b.R)
		&& Mathf.IsEqualApprox(a.G, b.G)
		&& Mathf.IsEqualApprox(a.B, b.B)
		&& Mathf.IsEqualApprox(a.A, b.A);

	private static Image DuplicateImage(Image source) =>
		Image.CreateFromData(source.GetWidth(), source.GetHeight(), false, source.GetFormat(), source.GetData());

	private static ImageSnapshot CaptureSnapshot(Image image) => new()
	{
		Width = image.GetWidth(),
		Height = image.GetHeight(),
		Format = image.GetFormat(),
		Data = image.GetData(),
	};

	private static Image RestoreSnapshot(ImageSnapshot snapshot) =>
		Image.CreateFromData(snapshot.Width, snapshot.Height, false, snapshot.Format, snapshot.Data);
}
