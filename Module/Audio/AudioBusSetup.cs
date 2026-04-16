using System;
using Godot;

namespace MiniRPG.Module.Audio;

public sealed class AudioBusSetup
{
	private const string MasterBus = "Master";
	private const string MusicBus = "Music";
	private const string SfxBus = "SFX";

	private bool _initialized;

	public void EnsureInitialized()
	{
		if (_initialized) return;
		_initialized = true;

		EnsureBus(MusicBus, MasterBus);
		EnsureBus(SfxBus, MasterBus);

		AddReverbToMusic();
	}

	public static void SetMasterVolume(float linear)
	{
		var idx = AudioServer.GetBusIndex(MasterBus);
		if (idx >= 0)
			AudioServer.SetBusVolumeDb(idx, LinearToDb(linear));
	}

	public static void SetMusicVolume(float linear)
	{
		var idx = AudioServer.GetBusIndex(MusicBus);
		if (idx >= 0)
			AudioServer.SetBusVolumeDb(idx, LinearToDb(linear));
	}

	public static void SetSfxVolume(float linear)
	{
		var idx = AudioServer.GetBusIndex(SfxBus);
		if (idx >= 0)
			AudioServer.SetBusVolumeDb(idx, LinearToDb(linear));
	}

	public static float GetMusicVolume()
	{
		var idx = AudioServer.GetBusIndex(MusicBus);
		return idx >= 0 ? DbToLinear(AudioServer.GetBusVolumeDb(idx)) : 1f;
	}

	public static float GetSfxVolume()
	{
		var idx = AudioServer.GetBusIndex(SfxBus);
		return idx >= 0 ? DbToLinear(AudioServer.GetBusVolumeDb(idx)) : 1f;
	}

	public static float GetMasterVolume()
	{
		var idx = AudioServer.GetBusIndex(MasterBus);
		return idx >= 0 ? DbToLinear(AudioServer.GetBusVolumeDb(idx)) : 1f;
	}

	private static void EnsureBus(string busName, string sendTo)
	{
		var idx = AudioServer.GetBusIndex(busName);
		if (idx >= 0) return;

		AudioServer.AddBus();
		idx = AudioServer.BusCount - 1;
		AudioServer.SetBusName(idx, busName);

		var sendIdx = AudioServer.GetBusIndex(sendTo);
		if (sendIdx >= 0)
			AudioServer.SetBusSend(idx, sendTo);
	}

	private static void AddReverbToMusic()
	{
		var idx = AudioServer.GetBusIndex(MusicBus);
		if (idx < 0) return;

		if (AudioServer.GetBusEffectCount(idx) > 0)
			return;

		var reverb = new AudioEffectReverb
		{
			RoomSize = 0.6f,
			Damping = 0.4f,
			Spread = 0.8f,
			Wet = 0.15f,
			Dry = 0.85f,
		};
		AudioServer.AddBusEffect(idx, reverb);

		var eq = new AudioEffectEQ6();
		AudioServer.AddBusEffect(idx, eq);
	}

	private static float LinearToDb(float linear)
	{
		linear = Math.Max(linear, 0.0001f);
		return 20f * MathF.Log10(linear);
	}

	private static float DbToLinear(float db) =>
		MathF.Pow(10f, db / 20f);
}
