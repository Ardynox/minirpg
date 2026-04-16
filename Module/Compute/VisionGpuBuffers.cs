using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using MiniRPG.Core;
using MiniRPG.Core.AI;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Compute;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuActorData
{
	public int X;
	public int Y;
	public int Z;
	public uint Faction;
	public int FacingX;
	public int FacingY;
	public float SightCapacity;
	public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuObserverData
{
	public uint ActorIndex;
	public int FrontRadius;
	public int RearRadius;
	public int MaxVerticalLayers;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuVisionResult
{
	public uint ObserverIdx;
	public uint VisibleCount;
	public uint Vis0, Vis1, Vis2, Vis3, Vis4, Vis5, Vis6, Vis7, Vis8, Vis9, Vis10, Vis11;

	public readonly uint GetVisibleIndex(int i) => i switch
	{
		0 => Vis0, 1 => Vis1, 2 => Vis2, 3 => Vis3,
		4 => Vis4, 5 => Vis5, 6 => Vis6, 7 => Vis7,
		8 => Vis8, 9 => Vis9, 10 => Vis10, 11 => Vis11,
		_ => 0u,
	};
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuPushConstants
{
	public uint ActorCount;
	public uint ObserverCount;
	public uint ShortlistLimit;
	public int WorldMinX;
	public int WorldMinY;
	public int WorldMinZ;
	public int WorldSizeX;
	public int WorldSizeY;
	public int WorldSizeZ;
}

internal sealed class VisionGpuBuffers : IDisposable
{
	private readonly RenderingDevice _rd;
	private Rid _actorBuffer;
	private Rid _observerBuffer;
	private Rid _resultBuffer;
	private Rid _terrainTexture;
	private Rid _terrainSampler;
	private int _lastActorCapacity;
	private int _lastObserverCapacity;
	private int _lastResultCapacity;

	public Rid ActorBuffer => _actorBuffer;
	public Rid ObserverBuffer => _observerBuffer;
	public Rid ResultBuffer => _resultBuffer;
	public Rid TerrainTexture => _terrainTexture;
	public Rid TerrainSampler => _terrainSampler;

	public VisionGpuBuffers(RenderingDevice rd)
	{
		_rd = rd;
		_terrainSampler = rd.SamplerCreate(new RDSamplerState
		{
			MagFilter = RenderingDevice.SamplerFilter.Nearest,
			MinFilter = RenderingDevice.SamplerFilter.Nearest,
			MipFilter = RenderingDevice.SamplerFilter.Nearest,
		});
	}

	public void EnsureActorBuffer(int count)
	{
		if (count <= _lastActorCapacity && _actorBuffer.IsValid) return;
		FreeBuffer(ref _actorBuffer);
		var size = (uint)(count * Marshal.SizeOf<GpuActorData>());
		_actorBuffer = _rd.StorageBufferCreate(size);
		_lastActorCapacity = count;
	}

	public void EnsureObserverBuffer(int count)
	{
		if (count <= _lastObserverCapacity && _observerBuffer.IsValid) return;
		FreeBuffer(ref _observerBuffer);
		var size = (uint)(count * Marshal.SizeOf<GpuObserverData>());
		_observerBuffer = _rd.StorageBufferCreate(size);
		_lastObserverCapacity = count;
	}

	public void EnsureResultBuffer(int count)
	{
		if (count <= _lastResultCapacity && _resultBuffer.IsValid) return;
		FreeBuffer(ref _resultBuffer);
		var size = (uint)(count * Marshal.SizeOf<GpuVisionResult>());
		_resultBuffer = _rd.StorageBufferCreate(size);
		_lastResultCapacity = count;
	}

	public void UploadActors(ReadOnlySpan<GpuActorData> data)
	{
		var bytes = MemoryMarshal.AsBytes(data);
		_rd.BufferUpdate(_actorBuffer, 0, (uint)bytes.Length, bytes.ToArray());
	}

	public void UploadObservers(ReadOnlySpan<GpuObserverData> data)
	{
		var bytes = MemoryMarshal.AsBytes(data);
		_rd.BufferUpdate(_observerBuffer, 0, (uint)bytes.Length, bytes.ToArray());
	}

	public byte[] DownloadResults(int count)
	{
		var size = (uint)(count * Marshal.SizeOf<GpuVisionResult>());
		return _rd.BufferGetData(_resultBuffer, 0, size);
	}

	public void UploadTerrainOpacity(
		WorldMap world,
		int minX, int minY, int minZ,
		int sizeX, int sizeY, int sizeZ)
	{
		FreeTexture(ref _terrainTexture);

		var data = new byte[sizeX * sizeY * sizeZ];
		for (var z = 0; z < sizeZ; z++)
		for (var y = 0; y < sizeY; y++)
		for (var x = 0; x < sizeX; x++)
		{
			var wx = minX + x;
			var wy = minY + y;
			var wz = minZ + z;
			data[z * sizeY * sizeX + y * sizeX + x] = world.BlocksSight(wx, wy, wz) ? (byte)255 : (byte)0;
		}

		var format = new RDTextureFormat
		{
			Width = (uint)sizeX,
			Height = (uint)sizeY,
			Depth = (uint)sizeZ,
			Format = RenderingDevice.DataFormat.R8Unorm,
			TextureType = RenderingDevice.TextureType.Type3D,
			UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
		};

		_terrainTexture = _rd.TextureCreate(format, new RDTextureView(), new Godot.Collections.Array<byte[]> { data });
	}

	private void FreeBuffer(ref Rid rid)
	{
		if (rid.IsValid)
			_rd.FreeRid(rid);
		rid = default;
	}

	private void FreeTexture(ref Rid rid)
	{
		if (rid.IsValid)
			_rd.FreeRid(rid);
		rid = default;
	}

	public void Dispose()
	{
		FreeBuffer(ref _actorBuffer);
		FreeBuffer(ref _observerBuffer);
		FreeBuffer(ref _resultBuffer);
		FreeTexture(ref _terrainTexture);
		if (_terrainSampler.IsValid)
			_rd.FreeRid(_terrainSampler);
		_terrainSampler = default;
	}
}
