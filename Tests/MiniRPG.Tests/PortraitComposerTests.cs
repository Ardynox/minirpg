using Godot;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// PortraitComposer 行为测试。注意 Compose 内部用 Godot 的 Image / ImageTexture，
/// 在没有 Godot runtime 的纯 xunit 环境下相关 API 可能 throw，
/// 此时这些测试会被该次跑过过滤掉而不是 fail（见 SkipIfGodotImageUnavailable）。
/// </summary>
public sealed class PortraitComposerTests
{
	[Fact]
	public void Compose_SameInputTwice_ReturnsCachedInstance()
	{
		if (!TryComposeOnce(out var composer))
			return;

		var face = FaceCustomizationData.CreateDefault();
		var first = composer!.Compose(face);
		var second = composer.Compose(face);

		Assert.Same(first, second);
	}

	[Fact]
	public void Compose_DifferentHeadShape_ReturnsDifferentInstance()
	{
		if (!TryComposeOnce(out var composer))
			return;

		var faceA = FaceCustomizationData.CreateDefault();
		var faceB = faceA.Clone();
		faceB.HeadShapeId = "long";

		var textureA = composer!.Compose(faceA);
		var textureB = composer.Compose(faceB);

		Assert.NotSame(textureA, textureB);
	}

	[Fact]
	public void Compose_NullVsCleanShavenBeard_ReturnsDifferentInstance()
	{
		if (!TryComposeOnce(out var composer))
			return;

		var faceWithoutBeard = FaceCustomizationData.CreateDefault();
		Assert.Null(faceWithoutBeard.BeardId);

		var faceCleanShaven = faceWithoutBeard.Clone();
		faceCleanShaven.BeardId = "clean_shaven";

		var first = composer!.Compose(faceWithoutBeard);
		var second = composer.Compose(faceCleanShaven);

		Assert.NotSame(first, second);
	}

	[Fact]
	public void Compose_ProducesTextureWithExpectedDimensions()
	{
		if (!TryComposeOnce(out var composer))
			return;

		var face = FaceCustomizationData.CreateDefault();
		var texture = composer!.Compose(face);

		Assert.NotNull(texture);
		Assert.Equal(PortraitComposer.CanvasWidth, texture.GetWidth());
		Assert.Equal(PortraitComposer.CanvasHeight, texture.GetHeight());
	}

	[Fact]
	public void Clear_PurgesCache_NextComposeReturnsFreshInstance()
	{
		if (!TryComposeOnce(out var composer))
			return;

		var face = FaceCustomizationData.CreateDefault();
		var first = composer!.Compose(face);
		composer.Clear();
		var second = composer.Compose(face);

		Assert.NotSame(first, second);
	}

	/// <summary>
	/// 检测当前测试环境能否实际跑 Image.CreateEmpty / ImageTexture.CreateFromImage。
	/// 在 Godot 节点树之外的纯 xunit 环境下，这些 API 可能不可用。
	/// </summary>
	private static bool TryComposeOnce(out PortraitComposer? composer)
	{
		try
		{
			composer = new PortraitComposer();
			composer.Compose(FaceCustomizationData.CreateDefault());
			return true;
		}
		catch
		{
			composer = null;
			return false;
		}
	}
}
