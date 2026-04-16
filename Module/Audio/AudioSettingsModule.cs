using System;
using Godot;

namespace MiniRPG.Module.Audio;

public sealed class AudioSettingsModule
{
	private readonly VBoxContainer _container;
	private readonly HSlider _masterSlider;
	private readonly HSlider _musicSlider;
	private readonly HSlider _sfxSlider;
	private readonly Label _masterLabel;
	private readonly Label _musicLabel;
	private readonly Label _sfxLabel;
	private readonly Label _masterValueLabel;
	private readonly Label _musicValueLabel;
	private readonly Label _sfxValueLabel;

	public event Action<float>? MasterVolumeChanged;
	public event Action<float>? MusicVolumeChanged;
	public event Action<float>? SfxVolumeChanged;

	public AudioSettingsModule(Control parent)
	{
		_container = new VBoxContainer();
		_container.Name = "AudioSection";

		var sectionTitle = new Label();
		sectionTitle.Text = LocalizationService.TOrFallback("ui.settings.section.audio", "Audio");
		sectionTitle.AddThemeColorOverride("font_color", new Color(0.85f, 0.78f, 0.55f));
		_container.AddChild(sectionTitle);

		var separator = new HSeparator();
		separator.CustomMinimumSize = new Vector2(0, 8);
		_container.AddChild(separator);

		(_masterLabel, _masterSlider, _masterValueLabel) = CreateSliderRow("ui.settings.audio.master", "Master");
		(_musicLabel, _musicSlider, _musicValueLabel) = CreateSliderRow("ui.settings.audio.music", "Music");
		(_sfxLabel, _sfxSlider, _sfxValueLabel) = CreateSliderRow("ui.settings.audio.sfx", "SFX");

		_masterSlider.Value = AudioBusSetup.GetMasterVolume() * 100;
		_musicSlider.Value = AudioBusSetup.GetMusicVolume() * 100;
		_sfxSlider.Value = AudioBusSetup.GetSfxVolume() * 100;

		UpdateValueLabel(_masterValueLabel, _masterSlider.Value);
		UpdateValueLabel(_musicValueLabel, _musicSlider.Value);
		UpdateValueLabel(_sfxValueLabel, _sfxSlider.Value);

		_masterSlider.ValueChanged += v =>
		{
			var linear = (float)v / 100f;
			AudioBusSetup.SetMasterVolume(linear);
			UpdateValueLabel(_masterValueLabel, v);
			MasterVolumeChanged?.Invoke(linear);
		};

		_musicSlider.ValueChanged += v =>
		{
			var linear = (float)v / 100f;
			AudioBusSetup.SetMusicVolume(linear);
			UpdateValueLabel(_musicValueLabel, v);
			MusicVolumeChanged?.Invoke(linear);
		};

		_sfxSlider.ValueChanged += v =>
		{
			var linear = (float)v / 100f;
			AudioBusSetup.SetSfxVolume(linear);
			UpdateValueLabel(_sfxValueLabel, v);
			SfxVolumeChanged?.Invoke(linear);
		};

		parent.AddChild(_container);
	}

	public void RefreshTexts()
	{
		_masterLabel.Text = LocalizationService.TOrFallback("ui.settings.audio.master", "Master");
		_musicLabel.Text = LocalizationService.TOrFallback("ui.settings.audio.music", "Music");
		_sfxLabel.Text = LocalizationService.TOrFallback("ui.settings.audio.sfx", "SFX");
	}

	public void SyncFromBus()
	{
		_masterSlider.Value = AudioBusSetup.GetMasterVolume() * 100;
		_musicSlider.Value = AudioBusSetup.GetMusicVolume() * 100;
		_sfxSlider.Value = AudioBusSetup.GetSfxVolume() * 100;
	}

	private (Label title, HSlider slider, Label value) CreateSliderRow(string locKey, string fallback)
	{
		var row = new HBoxContainer();
		row.CustomMinimumSize = new Vector2(0, 32);

		var label = new Label();
		label.Text = LocalizationService.TOrFallback(locKey, fallback);
		label.CustomMinimumSize = new Vector2(80, 0);
		label.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		row.AddChild(label);

		var slider = new HSlider();
		slider.MinValue = 0;
		slider.MaxValue = 100;
		slider.Step = 1;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		slider.CustomMinimumSize = new Vector2(120, 0);
		row.AddChild(slider);

		var valueLabel = new Label();
		valueLabel.CustomMinimumSize = new Vector2(40, 0);
		valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(valueLabel);

		_container.AddChild(row);
		return (label, slider, valueLabel);
	}

	private static void UpdateValueLabel(Label label, double value)
	{
		label.Text = $"{(int)value}%";
	}
}
