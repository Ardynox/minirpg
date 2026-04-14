using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public sealed class PanelLayoutService(PanelLayoutStore store, PanelButtonScaleService buttonScaleService)
{
	private readonly PanelLayoutStore _store = store;
	private readonly PanelButtonScaleService _buttonScaleService = buttonScaleService;
	private readonly Dictionary<string, RegisteredPanelState> _panels = [];

	public void Initialize()
	{
		_store.Load();
	}

	public void RegisterPanel(string panelId, PanelContainer panel)
	{
		var defaultMinimum = CaptureDefaultMinimumSize(panel);
		_panels[panelId] = new RegisteredPanelState(panel, defaultMinimum);
		_buttonScaleService.RegisterPanel(panelId, panel);
		ApplyAppearance(panelId);
	}

	public void UnregisterPanel(string panelId)
	{
		_panels.Remove(panelId);
		_buttonScaleService.UnregisterPanel(panelId);
	}

	public PanelAppearance GetResolvedAppearance(string panelId)
	{
		var state = GetRegisteredPanel(panelId);
		var width = state.DefaultMinimumSize.X;
		var height = state.DefaultMinimumSize.Y;
		var buttonScale = 1f;
		if (_store.TryGetAppearance(panelId, out var saved))
		{
			if (saved.Width is > 0f)
				width = saved.Width.Value;
			if (saved.Height is > 0f)
				height = saved.Height.Value;
			buttonScale = saved.ButtonScale ?? buttonScale;
		}

		return new PanelAppearance(width, height, buttonScale);
	}

	public PanelAppearance GetStoredAppearance(string panelId)
	{
		GetRegisteredPanel(panelId);
		if (!_store.TryGetAppearance(panelId, out var stored))
			return new PanelAppearance(null, null, null);

		return new PanelAppearance(
			stored.Width is > 0f ? stored.Width.Value : null,
			stored.Height is > 0f ? stored.Height.Value : null,
			stored.ButtonScale);
	}

	public void ApplyAppearance(string panelId)
	{
		var state = GetRegisteredPanel(panelId);
		var resolved = GetResolvedAppearance(panelId);
		var width = resolved.Width ?? state.DefaultMinimumSize.X;
		var height = resolved.Height ?? state.DefaultMinimumSize.Y;
		var buttonScale = resolved.ButtonScale ?? 1f;

		state.Panel.CustomMinimumSize = new Vector2(Math.Max(0f, width), Math.Max(0f, height));
		if (state.Panel.IsInsideTree())
			state.Panel.Size = state.Panel.GetCombinedMinimumSize();

		_buttonScaleService.ApplyScale(panelId, buttonScale);
	}

	public void SetWidth(string panelId, float width)
	{
		_store.SetWidth(panelId, Math.Max(0f, width));
		ApplyAndSave(panelId);
	}

	public void SetHeight(string panelId, float height)
	{
		_store.SetHeight(panelId, Math.Max(0f, height));
		ApplyAndSave(panelId);
	}

	public void SetButtonScale(string panelId, float buttonScale)
	{
		_store.SetButtonScale(panelId, Math.Clamp(buttonScale, 0.5f, 2.5f));
		ApplyAndSave(panelId);
	}

	public void ResetAppearance(string panelId)
	{
		_store.RemoveAppearance(panelId);
		ApplyAndSave(panelId);
	}

	private void ApplyAndSave(string panelId)
	{
		ApplyAppearance(panelId);
		_store.Save();
	}

	private RegisteredPanelState GetRegisteredPanel(string panelId)
	{
		if (_panels.TryGetValue(panelId, out var state))
			return state;

		throw new InvalidOperationException($"Panel '{panelId}' has not been registered with {nameof(PanelLayoutService)}.");
	}

	private static Vector2 CaptureDefaultMinimumSize(Control control)
	{
		var currentMinimum = control.CustomMinimumSize;
		var combinedMinimum = control.GetCombinedMinimumSize();
		return new Vector2(
			Math.Max(currentMinimum.X, combinedMinimum.X),
			Math.Max(currentMinimum.Y, combinedMinimum.Y));
	}

	private sealed class RegisteredPanelState(PanelContainer panel, Vector2 defaultMinimumSize)
	{
		public PanelContainer Panel { get; } = panel;
		public Vector2 DefaultMinimumSize { get; } = defaultMinimumSize;
	}
}
