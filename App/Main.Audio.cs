using System.Collections.Generic;
using MiniRPG.Module.Audio;

namespace MiniRPG;

public partial class Main
{
	private MusicCoordinator? _musicCoordinator;
	private StingerPlayer? _stingerPlayer;
	private AudioSettingsModule? _audioSettings;

	private void InitAudioSystem()
	{
		_musicCoordinator = new MusicCoordinator();
		AddChild(_musicCoordinator);
		_musicCoordinator.Bind(_state);

		_stingerPlayer = new StingerPlayer();
		AddChild(_stingerPlayer);
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
