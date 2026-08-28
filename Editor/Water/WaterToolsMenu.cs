namespace Sandbox.Editor;

/// <summary>
/// Flat Tools list entry. Nested Tools/Water/... paths do not appear in the Tools window.
/// </summary>
public static class WaterToolsMenu
{
	[Menu( "Editor", "Tools/Water" )]
	public static void OpenWaterTools() => WaterToolsApp.OpenOrFocus();
}
