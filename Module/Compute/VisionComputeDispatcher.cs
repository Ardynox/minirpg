using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using MiniRPG.Core;
using MiniRPG.Core.AI;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Compute;

public sealed class VisionComputeDispatcher : IDisposable
{
	private readonly RenderingDevice _rd;
	private readonly VisionGpuBuffers _buffers;
	private Rid _shader;
	private Rid _pipeline;
	private bool _initialized;

	private static readonly uint ActorDataSize = (uint)Marshal.SizeOf<GpuActorData>();
	private static readonly uint ObserverDataSize = (uint)Marshal.SizeOf<GpuObserverData>();
	private static readonly uint ResultDataSize = (uint)Marshal.SizeOf<GpuVisionResult>();

	public bool IsAvailable => _initialized;

	public VisionComputeDispatcher()
	{
		_rd = RenderingServer.GetRenderingDevice();
		_buffers = new VisionGpuBuffers(_rd);
	}

	public bool TryInitialize()
	{
		if (_initialized) return true;
		if (_rd == null) return false;

		try
		{
			var shaderFile = GD.Load<RDShaderFile>("res://Assets/Shaders/ai_vision_compute.glsl");
			if (shaderFile == null) return false;

			var spirv = shaderFile.GetSpirV();
			if (spirv == null) return false;

			_shader = _rd.ShaderCreateFromSpirV(spirv);
			if (!_shader.IsValid) return false;

			_pipeline = _rd.ComputePipelineCreate(_shader);
			if (!_pipeline.IsValid)
			{
				_rd.FreeRid(_shader);
				_shader = default;
				return false;
			}

			_initialized = true;
			return true;
		}
		catch
		{
			return false;
		}
	}

	internal Dictionary<string, List<Actor>> Dispatch(
		GameState state,
		IReadOnlyList<AIVisionRequest> requests,
		IReadOnlyList<VisionActorSnapshot> actorSnapshots,
		Dictionary<string, VisionActorSnapshot> snapshotsById,
		AIVisionConfig config)
	{
		var result = new Dictionary<string, List<Actor>>(requests.Count);
		if (!_initialized || state.World == null) return result;

		var actorCount = actorSnapshots.Count;
		var observerCount = requests.Count;
		var maxVerticalLayers = Math.Max(0, config.MaxVerticalVisionLayers);

		var actorIndexMap = new Dictionary<string, uint>(actorCount, StringComparer.Ordinal);
		var gpuActors = new GpuActorData[actorCount];
		for (var i = 0; i < actorCount; i++)
		{
			var snap = actorSnapshots[i];
			actorIndexMap[snap.Actor.Id] = (uint)i;
			gpuActors[i] = new GpuActorData
			{
				X = snap.X,
				Y = snap.Y,
				Z = snap.Z,
				Faction = EncodeFaction(snap.Faction),
				FacingX = snap.Actor.FacingX,
				FacingY = snap.Actor.FacingY,
				SightCapacity = snap.SightCapacity,
				Flags = snap.IsDead ? 1u : 0u,
			};
		}

		var gpuObservers = new GpuObserverData[observerCount];
		for (var i = 0; i < observerCount; i++)
		{
			var req = requests[i];
			if (!actorIndexMap.TryGetValue(req.Observer.Id, out var actorIdx))
				continue;

			var snap = snapshotsById[req.Observer.Id];
			var range = req.Detail == SimDetail.Full
				? Math.Max(0, config.FullRange)
				: Math.Max(0, config.SimplifiedRange);

			if (state.World != null
				&& snap.Z == 0
				&& state.World.IsWeatherExposed(snap.X, snap.Y, snap.Z))
			{
				var weather = WeatherRules.GetLocalWeather(state, snap.X, snap.Y, snap.Z);
				range = Math.Max(1, (int)MathF.Round(range * WeatherRules.GetAiVisionMultiplier(weather)));
			}

			var vision = VisionRangeScaler.ScaleDirectional(range, snap.SightCapacity, config.RearVisionRatio);

			gpuObservers[i] = new GpuObserverData
			{
				ActorIndex = actorIdx,
				FrontRadius = vision.FrontRadius,
				RearRadius = vision.RearRadius,
				MaxVerticalLayers = maxVerticalLayers,
			};
		}

		ComputeWorldBounds(actorSnapshots, maxVerticalLayers,
			out var minX, out var minY, out var minZ,
			out var sizeX, out var sizeY, out var sizeZ);

		_buffers.EnsureActorBuffer(actorCount);
		_buffers.EnsureObserverBuffer(observerCount);
		_buffers.EnsureResultBuffer(observerCount);
		_buffers.UploadActors(gpuActors);
		_buffers.UploadObservers(gpuObservers);
		_buffers.UploadTerrainOpacity(state.World!, minX, minY, minZ, sizeX, sizeY, sizeZ);

		var shortlistLimit = Math.Max(config.FullShortlistLimit, config.SimplifiedShortlistLimit);

		var pushConstants = new GpuPushConstants
		{
			ActorCount = (uint)actorCount,
			ObserverCount = (uint)observerCount,
			ShortlistLimit = (uint)shortlistLimit,
			WorldMinX = minX,
			WorldMinY = minY,
			WorldMinZ = minZ,
			WorldSizeX = sizeX,
			WorldSizeY = sizeY,
			WorldSizeZ = sizeZ,
		};

		var pushBytes = new byte[Marshal.SizeOf<GpuPushConstants>()];
		MemoryMarshal.Write(pushBytes, in pushConstants);

		var actorUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = 0,
		};
		actorUniform.AddId(_buffers.ActorBuffer);

		var observerUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = 1,
		};
		observerUniform.AddId(_buffers.ObserverBuffer);

		var resultUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = 2,
		};
		resultUniform.AddId(_buffers.ResultBuffer);

		var samplerUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.SamplerWithTexture,
			Binding = 3,
		};
		samplerUniform.AddId(_buffers.TerrainSampler);
		samplerUniform.AddId(_buffers.TerrainTexture);

		var uniformSet = _rd.UniformSetCreate(
			new Godot.Collections.Array<RDUniform> { actorUniform, observerUniform, resultUniform, samplerUniform },
			_shader, 0);

		var computeList = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(computeList, _pipeline);
		_rd.ComputeListBindUniformSet(computeList, uniformSet, 0);
		_rd.ComputeListSetPushConstant(computeList, pushBytes, (uint)pushBytes.Length);
		_rd.ComputeListDispatch(computeList, (uint)observerCount, 1, 1);
		_rd.ComputeListEnd();

		_rd.Submit();
		_rd.Sync();

		var resultBytes = _buffers.DownloadResults(observerCount);
		var resultSpan = MemoryMarshal.Cast<byte, GpuVisionResult>(resultBytes);

		for (var i = 0; i < observerCount; i++)
		{
			var req = requests[i];
			var visibleActors = new List<Actor>();
			ref readonly var res = ref resultSpan[i];
			var visCount = Math.Min(res.VisibleCount, 12u);
			for (uint v = 0; v < visCount; v++)
			{
				var actorIdx = res.GetVisibleIndex((int)v);
				if (actorIdx < actorCount)
					visibleActors.Add(actorSnapshots[(int)actorIdx].Actor);
			}
			result[req.Observer.Id] = visibleActors;
		}

		if (uniformSet.IsValid)
			_rd.FreeRid(uniformSet);

		return result;
	}

	private static void ComputeWorldBounds(
		IReadOnlyList<VisionActorSnapshot> actors,
		int verticalRange,
		out int minX, out int minY, out int minZ,
		out int sizeX, out int sizeY, out int sizeZ)
	{
		if (actors.Count == 0)
		{
			minX = minY = minZ = 0;
			sizeX = sizeY = sizeZ = 1;
			return;
		}

		var xMin = int.MaxValue;
		var xMax = int.MinValue;
		var yMin = int.MaxValue;
		var yMax = int.MinValue;
		var zMin = int.MaxValue;
		var zMax = int.MinValue;

		foreach (var a in actors)
		{
			if (a.X < xMin) xMin = a.X;
			if (a.X > xMax) xMax = a.X;
			if (a.Y < yMin) yMin = a.Y;
			if (a.Y > yMax) yMax = a.Y;
			if (a.Z < zMin) zMin = a.Z;
			if (a.Z > zMax) zMax = a.Z;
		}

		const int pad = 16;
		minX = xMin - pad;
		minY = yMin - pad;
		minZ = zMin - verticalRange - 1;
		sizeX = Math.Min(xMax - xMin + 2 * pad + 1, 512);
		sizeY = Math.Min(yMax - yMin + 2 * pad + 1, 512);
		sizeZ = Math.Min(zMax - zMin + 2 * verticalRange + 3, 64);
	}

	private static uint EncodeFaction(string faction) => faction switch
	{
		Factions.Player => 0u,
		Factions.Hostile => 1u,
		Factions.Friendly => 2u,
		_ => 3u,
	};

	public void Dispose()
	{
		_buffers.Dispose();
		if (_pipeline.IsValid)
			_rd.FreeRid(_pipeline);
		if (_shader.IsValid)
			_rd.FreeRid(_shader);
		_pipeline = default;
		_shader = default;
		_initialized = false;
	}
}
