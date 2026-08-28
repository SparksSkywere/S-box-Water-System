using WaterSystem;

namespace Sandbox.Editor;

/// <summary>
/// Scene overlay for the selected River Path.
/// </summary>
[Title( "River Path" )]
[Icon( "water" )]
[Alias( "river_path" )]
[Group( "1" )]
[Order( 1 )]
public class RiverPathEditorTool : EditorTool<RiverPathComponent>
{
	RiverPathToolWindow _window;
	RiverPathComponent _selected;

	public override void OnEnabled()
	{
		_window = new RiverPathToolWindow();
		AddOverlay( _window, TextFlag.RightBottom, 10 );
		SyncSelection( selectFirstPointOnChange: true );
	}

	public override void OnDisabled()
	{
		RiverPathEditorSelection.Clear();
		_window = null;
		_selected = null;
	}

	public override void OnUpdate()
	{
		// Point gizmo clicks activate rivers directly — keep the overlay in sync.
		RiverPathEditorSelection.SyncFromActiveRiver();
		_selected = RiverPathEditorSelection.BoundRiver ?? RiverPathComponent.EditorActiveRiver;
		_window?.Bind( _selected );
		_window?.RefreshStatus( _selected );
	}

	public override void OnSelectionChanged()
	{
		SyncSelection( selectFirstPointOnChange: true );
	}

	void SyncSelection( bool selectFirstPointOnChange )
	{
		if ( !RiverPathEditorSelection.SyncFromHierarchy( selectFirstPointOnChange ) )
		{
			_selected = null;
			_window?.Bind( null );
			return;
		}

		_selected = RiverPathEditorSelection.BoundRiver ?? RiverPathComponent.EditorActiveRiver;
		_window?.Bind( _selected );
	}
}
