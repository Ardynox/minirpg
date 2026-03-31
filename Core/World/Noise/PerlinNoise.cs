using System;

namespace MiniRPG.Core.World.Noise;

/// <summary>
/// 纯 C# 实现的改进版 Perlin 噪声（Ken Perlin 2002）。
/// 无 Godot 依赖，确定性输出，适合 chunk 流式生成。
/// </summary>
public class PerlinNoise
{
	private readonly byte[] _perm = new byte[512];

	public PerlinNoise(int seed)
	{
		var rng = new Random(seed);
		var p = new byte[256];
		for (var i = 0; i < 256; i++) p[i] = (byte)i;
		for (var i = 255; i > 0; i--)
		{
			var j = rng.Next(i + 1);
			(p[i], p[j]) = (p[j], p[i]);
		}
		for (var i = 0; i < 512; i++) _perm[i] = p[i & 255];
	}

	/// <summary>2D Perlin 噪声，返回 [-1, 1]。</summary>
	public double Noise2D(double x, double y)
	{
		var xi = (int)Math.Floor(x) & 255;
		var yi = (int)Math.Floor(y) & 255;
		var xf = x - Math.Floor(x);
		var yf = y - Math.Floor(y);

		var u = Fade(xf);
		var v = Fade(yf);

		var aa = _perm[_perm[xi] + yi];
		var ab = _perm[_perm[xi] + yi + 1];
		var ba = _perm[_perm[xi + 1] + yi];
		var bb = _perm[_perm[xi + 1] + yi + 1];

		return Lerp(v,
			Lerp(u, Grad(aa, xf, yf), Grad(ba, xf - 1, yf)),
			Lerp(u, Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1)));
	}

	/// <summary>3D Perlin 噪声，返回 [-1, 1]。</summary>
	public double Noise3D(double x, double y, double z)
	{
		var xi = (int)Math.Floor(x) & 255;
		var yi = (int)Math.Floor(y) & 255;
		var zi = (int)Math.Floor(z) & 255;
		var xf = x - Math.Floor(x);
		var yf = y - Math.Floor(y);
		var zf = z - Math.Floor(z);

		var u = Fade(xf);
		var v = Fade(yf);
		var w = Fade(zf);

		var a = _perm[xi] + yi;
		var aa = _perm[a] + zi;
		var ab = _perm[a + 1] + zi;
		var b = _perm[xi + 1] + yi;
		var ba = _perm[b] + zi;
		var bb = _perm[b + 1] + zi;

		return Lerp(w,
			Lerp(v,
				Lerp(u, Grad3(_perm[aa], xf, yf, zf), Grad3(_perm[ba], xf - 1, yf, zf)),
				Lerp(u, Grad3(_perm[ab], xf, yf - 1, zf), Grad3(_perm[bb], xf - 1, yf - 1, zf))),
			Lerp(v,
				Lerp(u, Grad3(_perm[aa + 1], xf, yf, zf - 1), Grad3(_perm[ba + 1], xf - 1, yf, zf - 1)),
				Lerp(u, Grad3(_perm[ab + 1], xf, yf - 1, zf - 1), Grad3(_perm[bb + 1], xf - 1, yf - 1, zf - 1))));
	}

	/// <summary>分形布朗运动：多层叠加，octaves 层数，persistence 衰减系数。返回约 [-1, 1]。</summary>
	public double FBM2D(double x, double y, int octaves = 4, double persistence = 0.5, double lacunarity = 2.0)
	{
		double total = 0, amplitude = 1, frequency = 1, maxVal = 0;
		for (var i = 0; i < octaves; i++)
		{
			total += Noise2D(x * frequency, y * frequency) * amplitude;
			maxVal += amplitude;
			amplitude *= persistence;
			frequency *= lacunarity;
		}
		return total / maxVal;
	}

	/// <summary>3D 分形布朗运动。</summary>
	public double FBM3D(double x, double y, double z, int octaves = 4, double persistence = 0.5, double lacunarity = 2.0)
	{
		double total = 0, amplitude = 1, frequency = 1, maxVal = 0;
		for (var i = 0; i < octaves; i++)
		{
			total += Noise3D(x * frequency, y * frequency, z * frequency) * amplitude;
			maxVal += amplitude;
			amplitude *= persistence;
			frequency *= lacunarity;
		}
		return total / maxVal;
	}

	private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
	private static double Lerp(double t, double a, double b) => a + t * (b - a);

	private static double Grad(int hash, double x, double y) => (hash & 3) switch
	{
		0 => x + y,
		1 => -x + y,
		2 => x - y,
		_ => -x - y,
	};

	private static double Grad3(int hash, double x, double y, double z)
	{
		var h = hash & 15;
		var u = h < 8 ? x : y;
		var v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
		return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
	}
}
