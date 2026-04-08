using MiniRPG.Core.World.Noise;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PerlinNoiseTests
{
	// ── Determinism ──────────────────────────────────────

	[Fact]
	public void Noise2D_SameSeed_SameOutput()
	{
		var a = new PerlinNoise(42);
		var b = new PerlinNoise(42);
		Assert.Equal(a.Noise2D(1.5, 2.5), b.Noise2D(1.5, 2.5));
	}

	[Fact]
	public void Noise3D_SameSeed_SameOutput()
	{
		var a = new PerlinNoise(42);
		var b = new PerlinNoise(42);
		Assert.Equal(a.Noise3D(1.5, 2.5, 3.5), b.Noise3D(1.5, 2.5, 3.5));
	}

	[Fact]
	public void Noise2D_DifferentSeed_DifferentOutput()
	{
		var a = new PerlinNoise(42);
		var b = new PerlinNoise(99);
		// Extremely unlikely to be equal
		Assert.NotEqual(a.Noise2D(1.5, 2.5), b.Noise2D(1.5, 2.5));
	}

	// ── Value range ──────────────────────────────────────

	[Fact]
	public void Noise2D_ValueRange_WithinBounds()
	{
		var noise = new PerlinNoise(123);
		for (int i = 0; i < 1000; i++)
		{
			double x = i * 0.1;
			double y = i * 0.07;
			var val = noise.Noise2D(x, y);
			Assert.InRange(val, -1.0, 1.0);
		}
	}

	[Fact]
	public void Noise3D_ValueRange_WithinBounds()
	{
		var noise = new PerlinNoise(456);
		for (int i = 0; i < 500; i++)
		{
			double x = i * 0.1;
			double y = i * 0.07;
			double z = i * 0.13;
			var val = noise.Noise3D(x, y, z);
			Assert.InRange(val, -1.5, 1.5); // 3D can slightly exceed [-1,1]
		}
	}

	// ── Smoothness (continuity) ──────────────────────────

	[Fact]
	public void Noise2D_SmallStep_SmallChange()
	{
		var noise = new PerlinNoise(789);
		double prev = noise.Noise2D(5.0, 5.0);
		double step = 0.01;
		for (int i = 1; i <= 10; i++)
		{
			double curr = noise.Noise2D(5.0 + i * step, 5.0);
			double diff = Math.Abs(curr - prev);
			// Small step should produce small change
			Assert.True(diff < 0.1, $"Step {i}: diff={diff} too large");
			prev = curr;
		}
	}

	// ── Integer coordinates ──────────────────────────────

	[Fact]
	public void Noise2D_AtIntegerCoordinates_ReturnsZero()
	{
		// Perlin noise at integer coordinates should be 0 (gradient dot product with zero offset)
		var noise = new PerlinNoise(42);
		var val = noise.Noise2D(0.0, 0.0);
		Assert.Equal(0.0, val, 10);
	}

	[Fact]
	public void Noise2D_AtAnyInteger_ReturnsZero()
	{
		var noise = new PerlinNoise(42);
		for (int x = -5; x <= 5; x++)
			for (int y = -5; y <= 5; y++)
				Assert.Equal(0.0, noise.Noise2D(x, y), 10);
	}

	// ── FBM ──────────────────────────────────────────────

	[Fact]
	public void FBM2D_Deterministic()
	{
		var a = new PerlinNoise(42);
		var b = new PerlinNoise(42);
		Assert.Equal(
			a.FBM2D(1.5, 2.5, octaves: 4),
			b.FBM2D(1.5, 2.5, octaves: 4));
	}

	[Fact]
	public void FBM2D_ValueRange_Bounded()
	{
		var noise = new PerlinNoise(42);
		for (int i = 0; i < 500; i++)
		{
			var val = noise.FBM2D(i * 0.1, i * 0.07);
			Assert.InRange(val, -1.5, 1.5);
		}
	}

	[Fact]
	public void FBM3D_Deterministic()
	{
		var a = new PerlinNoise(42);
		var b = new PerlinNoise(42);
		Assert.Equal(
			a.FBM3D(1.5, 2.5, 3.5, octaves: 3),
			b.FBM3D(1.5, 2.5, 3.5, octaves: 3));
	}

	[Fact]
	public void FBM2D_MoreOctaves_MoreDetail()
	{
		var noise = new PerlinNoise(42);
		// With 1 octave, FBM should equal raw noise (normalized)
		var fbm1 = noise.FBM2D(1.5, 2.5, octaves: 1);
		var raw = noise.Noise2D(1.5, 2.5);
		Assert.Equal(raw, fbm1, 10);
	}

	// ── Negative coordinates ─────────────────────────────

	[Fact]
	public void Noise2D_NegativeCoordinates_Works()
	{
		var noise = new PerlinNoise(42);
		// Should not throw and should be deterministic
		var val = noise.Noise2D(-10.5, -20.3);
		var val2 = noise.Noise2D(-10.5, -20.3);
		Assert.Equal(val, val2);
	}

	// ── Large coordinates ────────────────────────────────

	[Fact]
	public void Noise2D_LargeCoordinates_DoesNotThrow()
	{
		var noise = new PerlinNoise(42);
		var val = noise.Noise2D(10000.5, 20000.7);
		Assert.InRange(val, -1.0, 1.0);
	}
}
