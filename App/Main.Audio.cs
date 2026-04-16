using System.Collections.Generic;
using MiniRPG.Core.Config;
using MiniRPG.Module.Audio;

namespace MiniRPG;

public partial class Main
{
	private MusicCoordinator? _musicCoordinator;
	private StingerPlayer? _stingerPlayer;
	private AudioSettingsModule? _audioSettings;

	private void InitAudioSystem()
	{
		var busSetup = new AudioBusSetup();
		busSetup.EnsureInitialized();

		AudioBusSetup.SetMasterVolume(AppSettingsStore.LoadMasterVolume());
		AudioBusSetup.SetMusicVolume(AppSettingsStore.LoadMusicVolume());
		AudioBusSetup.SetSfxVolume(AppSettingsStore.LoadSfxVolume());

		_musicCoordinator = new MusicCoordinator();
		AddChild(_musicCoordinator);
		_musicCoordinator.Bind(_state);

		_stingerPlayer = new StingerPlayer();
		AddChild(_stingerPlayer);

		if (_audioSettings == null)
		{
			_audioSettings = new AudioSettingsModule(_settingsPanelModule.AudioSettingsHost);
			_audioSettings.MasterVolumeChanged += AppSettingsStore.SaveMasterVolume;
			_audioSettings.MusicVolumeChanged += AppSettingsStore.SaveMusicVolume;
			_audioSettings.SfxVolumeChanged += AppSettingsStore.SaveSfxVolume;
		}

		_audioSettings.SyncFromBus();
	}

	private void DispatchAudioEvents(List<GameEvent> events)
	{
		_musicCoordinator?.OnTurnAdvanced(events);

		if (_stingerPlayer != null)
		{
			foreach (var e in events)
				_stingerPlayer.PlayFromEvent(e.Type, e.TargetId, _state.PlayerId);
		}
	}

	private void OnSessionStartedAudio()
	{
		_musicCoordinator?.Bind(_state);
	}

	private void OnSessionEndedAudio()
	{
		_musicCoordinator?.Unbind();
	}
}
