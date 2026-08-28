using System;
using System.Collections.Generic;
using WaterSystem;

namespace Sandbox.Editor;

/// <summary>
/// Compact overlay for the selected river path. Gizmos handle point moves.
/// </summary>
public class RiverPathToolWindow : WidgetWindow
{
	static RiverPathToolWindowSettings _settings;

	RiverPathComponent _river;
	Label _status;
	Widget _mainPanel;
	Widget _collapsedBar;
	readonly Dictionary<string, bool> _expanded = new();

	static RiverPathToolWindowSettings Settings => _settings ??= RiverPathToolWindowSettings.Load();

	public RiverPathToolWindow()
	{
		WindowTitle = "River Path";
		SetWindowIcon( "water" );
		MinimumSize = new Vector2( 220, 72 );
		Size = new Vector2( 260, 160 );
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 4;

		_collapsedBar = Layout.Add( new Widget( this ) );
		_collapsedBar.Layout = Layout.Row();
		_collapsedBar.Layout.Spacing = 4;
		_collapsedBar.Visible = false;

		var showButton = new Button( "Show River Path" );
		showButton.Clicked = () => SetWindowHidden( false );
		_collapsedBar.Layout.Add( showButton, 1 );

		_mainPanel = Layout.Add( new Widget( this ) );
		_mainPanel.Layout = Layout.Column();
		_mainPanel.Layout.Spacing = 4;

		var headerRow = _mainPanel.Layout.AddRow();
		headerRow.Spacing = 4;

		var hideButton = new Button( "Hide" );
		hideButton.Clicked = () => SetWindowHidden( true );
		headerRow.Add( hideButton );

		_status = _mainPanel.Layout.Add( new Label( "Select a River Path" )
		{
			Color = Theme.Text.WithAlpha( 0.7f )
		} );

		AddCollapsibleSection( "Points", defaultExpanded: true,
			("Add Before", () => _river?.EditorAddPointBefore()),
			("Add After", () => _river?.EditorAddPointAfter()),
			("Remove", () => _river?.EditorRemoveSelectedPoint()) );

		AddCollapsibleSection( "Path", defaultExpanded: false,
			("Source Only", () => _river?.EditorSourceOnly()),
			("Straight", () => _river?.EditorCreatePath()),
			("Swap Flow", () => _river?.EditorSwapFlow()),
			("Rebuild", () => _river?.EditorRebuildPath()) );

		AddCollapsibleSection( "Scale", defaultExpanded: false,
			("W+", () => _river?.EditorWider()),
			("W-", () => _river?.EditorNarrower()),
			("D+", () => _river?.EditorDeeper()),
			("D-", () => _river?.EditorShallower()) );

		AddCollapsibleSection( "Links", defaultExpanded: false,
			("Nearest", () => _river?.EditorConnectNearest()),
			("Clear Out", () => _river?.EditorClearOutflow()) );

		SetWindowHidden( Settings.WindowHidden );
	}

	public void Bind( RiverPathComponent river )
	{
		_river = river;
		RefreshStatus( river );
	}

	public void RefreshStatus( RiverPathComponent river )
	{
		if ( _status is null )
			return;

		if ( river is null || !river.IsValid() || !river.GameObject.IsValid() )
		{
			_status.Text = "Select a River Path";
			return;
		}

		var outflow = river.OutflowRiver.IsValid() ? river.OutflowRiver.DisplayName : "none";
		var lastIndex = river.ControlPoints.Count > 0 ? river.ControlPoints.Count - 1 : 0;
		var pt = river.HasSelectedControlPoint
			? $"{river.SelectedControlPointIndex}/{lastIndex}"
			: "none";
		_status.Text =
			$"{river.DisplayName}  ·  pts {river.ControlPoints?.Count ?? 0}  ·  pt {pt}  ·  " +
			$"W {river.ScaledWidth:0}  D {river.ScaledDepth:0}  ·  out {outflow}";
	}

	void SetWindowHidden( bool hidden )
	{
		_mainPanel.Visible = !hidden;
		_collapsedBar.Visible = hidden;
		MinimumSize = hidden ? new Vector2( 140, 36 ) : new Vector2( 220, 72 );
		if ( hidden )
			Size = new Vector2( 180, 40 );
		Settings.SetWindowHidden( hidden );
	}

	void AddCollapsibleSection( string title, bool defaultExpanded, params (string Label, Action Clicked)[] actions )
	{
		var expanded = Settings.GetExpanded( title, defaultExpanded );
		_expanded[title] = expanded;

		var section = new Widget( _mainPanel );
		section.Layout = Layout.Column();
		section.Layout.Spacing = 2;

		var body = new Widget( section );
		body.Layout = Layout.Column();
		body.Layout.Spacing = 2;
		body.Visible = expanded;

		var header = new Button( SectionTitle( title, expanded ) );
		header.Clicked = () =>
		{
			var open = !_expanded[title];
			_expanded[title] = open;
			body.Visible = open;
			header.Text = SectionTitle( title, open );
			Settings.SetExpanded( title, open );
		};
		section.Layout.Add( header );

		Layout row = null;
		var count = 0;
		foreach ( var action in actions )
		{
			if ( count % 3 == 0 )
			{
				row = body.Layout.AddRow();
				row.Spacing = 4;
			}

			var button = new Button( action.Label );
			button.Clicked = action.Clicked;
			row.Add( button, 1 );
			count++;
		}

		section.Layout.Add( body );
		_mainPanel.Layout.Add( section );
		_mainPanel.Layout.AddSpacingCell( 2 );
	}

	static string SectionTitle( string title, bool expanded )
		=> (expanded ? "▾  " : "▸  ") + title;
}
