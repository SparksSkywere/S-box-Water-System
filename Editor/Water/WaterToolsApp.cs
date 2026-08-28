namespace Sandbox.Editor;

/// <summary>
/// Editor window for spawning water volumes and rivers.
/// </summary>
[EditorApp( "Water", "waves", "Spawn water volumes, rivers, and swim helpers" )]
public class WaterToolsApp : BaseWindow
{
	public WaterToolsPanel Panel { get; private set; }

	static WaterToolsApp _instance;

	public WaterToolsApp()
	{
		_instance = this;
		WindowTitle = "Water";
		SetWindowIcon( "waves" );
		Size = new Vector2( 560, 420 );
		MinimumSize = new Vector2( 420, 320 );
		Layout = Layout.Column();
		Panel = new WaterToolsPanel( this );
		Layout.Add( Panel, 1 );
		DeleteOnClose = true;
		Show();
	}

	public override void OnDestroyed()
	{
		if ( _instance == this )
			_instance = null;

		base.OnDestroyed();
	}

	public static WaterToolsApp OpenOrFocus()
	{
		if ( _instance is not null && _instance.IsValid )
		{
			_instance.Focus( true );
			_instance.Raise();
			return _instance;
		}

		return new WaterToolsApp();
	}

	[EditorEvent.Hotload]
	void OnHotload()
	{
		if ( !IsValid || Layout is null )
			return;

		Layout.Clear( true );
		Panel = new WaterToolsPanel( this );
		Layout.Add( Panel, 1 );
	}
}
