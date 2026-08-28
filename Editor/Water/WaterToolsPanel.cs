using System;

namespace Sandbox.Editor;

/// <summary>
/// Spawn and convert water types only. Path / nudge / link editing lives on River Path.
/// </summary>
public class WaterToolsPanel : Widget
{
	public WaterToolsPanel( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 10;

		var header = Layout.AddRow();
		header.Spacing = 8;
		header.Add( new Label.Header( "Water" ) );
		header.Add( new Label( "Spawn & setup" )
		{
			Color = Theme.Text.WithAlpha( 0.6f )
		} );
		header.AddStretchCell();

		Layout.Add( new Label( "Use the River Path scene tool overlay to edit rivers. Spawn or convert objects below." )
		{
			Color = Theme.Text.WithAlpha( 0.65f )
		} );

		AddSection( "Setup",
			("Manager", WaterSystemMenu.CreateManager),
			("Convert to Volume", WaterSystemMenu.ConvertSelectedToVolume),
			("Convert to River", WaterSystemMenu.ConvertSelectedToRiver),
			("Add Water Presence", WaterSystemMenu.AddWaterPresence) );

		AddSection( "Volumes",
			("Water", WaterSystemMenu.CreateWater),
			("Large Water", WaterSystemMenu.CreateLargeWater),
			("Pool", WaterSystemMenu.CreatePool) );

		AddSection( "Rivers",
			("River", WaterSystemMenu.CreateRiver),
			("Stream", WaterSystemMenu.CreateStream) );

		Layout.AddStretchCell();
	}

	void AddSection( string title, params (string Label, Action Clicked)[] actions )
	{
		Layout.Add( new Label.Header( title ) );
		Layout.AddSpacingCell( 2 );

		Layout row = null;
		var count = 0;
		foreach ( var action in actions )
		{
			if ( count % 4 == 0 )
			{
				row = Layout.AddRow();
				row.Spacing = 6;
			}

			var button = new Button( action.Label );
			button.Clicked = action.Clicked;
			row.Add( button, 1 );
			count++;
		}

		Layout.AddSpacingCell( 6 );
	}
}
