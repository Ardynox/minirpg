using System;
using Godot;

namespace MiniRPG.Module.Render;

internal sealed class RenderPerfTracer
{
	private IsometricVoxelRenderer.RenderPerfSnapshot _lastPerfSnapshot = IsometricVoxelRenderer.RenderPerfSnapshot.Empty;
	private double _frameTimeEwmaMs;
	private bool _hasFrameTimeEwma;

	public IsometricVoxelRenderer.RenderPerfSnapshot LastPerfSnapshot => _lastPerfSnapshot;

	public void CommitPerfFrame(double frameTimeMs, int spriteCount, int weatherSpriteCount, int tileDrawCommandCount, bool editorViewActive, IsometricVoxelRenderer.RenderTraceSample traceSample)
	{
		if (!_hasFrameTimeEwma)
		{
			_frameTimeEwmaMs = frameTimeMs;
			_hasFrameTimeEwma = true;
		}
		else
		{
			const double alpha = 0.10;
			_frameTimeEwmaMs = (_frameTimeEwmaMs * (1.0 - alpha)) + (frameTimeMs * alpha);
		}
		_lastPerfSnapshot = new IsometricVoxelRenderer.RenderPerfSnapshot(
			weatherSpriteCount + spriteCount,
			tileDrawCommandCount,
			_frameTimeEwmaMs);
	}
}
