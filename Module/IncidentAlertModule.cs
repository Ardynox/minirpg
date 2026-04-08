using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

/// <summary>
/// 事件通知 HUD：在屏幕顶部中央显示临时通知条。
/// 监听 incident_alert / incident_wanderer / incident_trader 等事件。
/// 通知自动淡出消失。
/// </summary>
public sealed class IncidentAlertModule
{
	private readonly Control _root;
	private readonly VBoxContainer _alertList;
	private readonly List<AlertEntry> _active = [];

	private const float DisplayDuration = 4.0f;
	private const float FadeDuration = 1.0f;
	private const int MaxAlerts = 3;

	public IncidentAlertModule(Control root)
	{
		_root = root;
		_alertList = root.GetNode<VBoxContainer>("VBox");
	}

	public static Control CreateControl(Theme? theme)
	{
		var root = new Control
		{
			Name = "IncidentAlerts",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorLeft = 0.5f,
			AnchorTop = 0f,
			AnchorRight = 0.5f,
			AnchorBottom = 0f,
			OffsetLeft = -200,
			OffsetTop = 12,
			OffsetRight = 200,
			CustomMinimumSize = new Vector2(400, 0),
			Theme = theme,
		};

		var vbox = new VBoxContainer
		{
			Name = "VBox",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		vbox.AddThemeConstantOverride("separation", 4);
		root.AddChild(vbox);

		return root;
	}

	/// <summary>处理 GameEvent 列表，提取事件通知。</summary>
	public void ProcessEvents(List<GameEvent> events)
	{
		foreach (var evt in events)
		{
			var text = evt.Type switch
			{
				"incident_alert" => $"⚠ {evt.InteractionName}",
				"incident_spawn" => $"⚔ {evt.InteractionName}: {evt.TargetActorName} 出现了",
				"incident_wanderer" => $"👤 {evt.TargetActorName} 到访",
				"incident_trader" => $"🏪 {evt.TargetActorName} 到访",
				"crop_mature" => $"🌾 {evt.ItemName} 已成熟",
				"mental_break" => $"💢 {evt.InitiatorActorName} 精神崩溃",
				_ => null,
			};

			if (text != null)
				ShowAlert(text, ResolveCategoryColor(evt.Type));
		}
	}

	/// <summary>显示一条通知。</summary>
	public void ShowAlert(string text, Color color)
	{
		// 超过上限时移除最旧的
		while (_active.Count >= MaxAlerts)
		{
			RemoveEntry(_active[0]);
		}

		var entry = new AlertEntry(text, color, DisplayDuration);
		_alertList.AddChild(entry.Panel);
		_active.Add(entry);
	}

	/// <summary>每帧更新：处理淡出和移除。</summary>
	public void Update(float delta)
	{
		for (var i = _active.Count - 1; i >= 0; i--)
		{
			var entry = _active[i];
			entry.Elapsed += delta;

			if (entry.Elapsed > DisplayDuration + FadeDuration)
			{
				RemoveEntry(entry);
				continue;
			}

			if (entry.Elapsed > DisplayDuration)
			{
				var fadeProgress = (entry.Elapsed - DisplayDuration) / FadeDuration;
				entry.Panel.Modulate = new Color(1, 1, 1, 1f - fadeProgress);
			}
		}
	}

	private void RemoveEntry(AlertEntry entry)
	{
		_alertList.RemoveChild(entry.Panel);
		entry.Panel.QueueFree();
		_active.Remove(entry);
	}

	private static Color ResolveCategoryColor(string eventType) => eventType switch
	{
		"incident_alert" or "incident_spawn" => new Color(1f, 0.4f, 0.3f),
		"incident_wanderer" => new Color(0.4f, 0.9f, 0.5f),
		"incident_trader" => new Color(0.5f, 0.8f, 1f),
		"crop_mature" => new Color(0.8f, 0.9f, 0.4f),
		"mental_break" => new Color(1f, 0.3f, 0.5f),
		_ => UIColors.TextNormal,
	};

	private sealed class AlertEntry
	{
		public PanelContainer Panel { get; }
		public float Elapsed { get; set; }

		public AlertEntry(string text, Color color, float _)
		{
			Panel = new PanelContainer
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				CustomMinimumSize = new Vector2(380, 32),
			};

			var style = new StyleBoxFlat
			{
				BgColor = new Color(0.08f, 0.08f, 0.12f, 0.92f),
				BorderColor = color * 0.7f,
				BorderWidthLeft = 2,
				BorderWidthTop = 1,
				BorderWidthRight = 2,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 4,
				CornerRadiusTopRight = 4,
				CornerRadiusBottomLeft = 4,
				CornerRadiusBottomRight = 4,
				ContentMarginLeft = 12,
				ContentMarginTop = 6,
				ContentMarginRight = 12,
				ContentMarginBottom = 6,
			};
			Panel.AddThemeStyleboxOverride("panel", style);

			var label = new Label
			{
				Text = text,
				HorizontalAlignment = HorizontalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			label.AddThemeColorOverride("font_color", color);
			Panel.AddChild(label);
		}
	}
}
