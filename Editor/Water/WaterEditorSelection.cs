using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Sandbox.Editor;

/// <summary>Resolves the active scene editor selection.</summary>
public static class WaterEditorSelection
{
	public static List<GameObject> GetSelectedGameObjects()
	{
		var result = new List<GameObject>();

		var selectedProperty = typeof( EditorUtility ).GetProperty( "SelectedGameObjects", BindingFlags.Public | BindingFlags.Static );
		if ( TryExtract( selectedProperty?.GetValue( null ), result ) && result.Count > 0 )
			return result;

		var activeSession = SceneEditorSession.Active;
		if ( activeSession is null )
			return result;

		var activeType = activeSession.GetType();
		var directSelected = activeType.GetProperty( "SelectedGameObjects", BindingFlags.Public | BindingFlags.Instance );
		if ( TryExtract( directSelected?.GetValue( activeSession ), result ) && result.Count > 0 )
			return result;

		var selectionProperty = activeType.GetProperty( "Selection", BindingFlags.Public | BindingFlags.Instance );
		var selectionObject = selectionProperty?.GetValue( activeSession );
		if ( selectionObject is null )
			return result;

		var selectionType = selectionObject.GetType();
		var nestedSelected = selectionType.GetProperty( "SelectedGameObjects", BindingFlags.Public | BindingFlags.Instance );
		if ( TryExtract( nestedSelected?.GetValue( selectionObject ), result ) && result.Count > 0 )
			return result;

		var objectsProperty = selectionType.GetProperty( "Objects", BindingFlags.Public | BindingFlags.Instance );
		TryExtract( objectsProperty?.GetValue( selectionObject ), result );
		return result;
	}

	static bool TryExtract( object source, List<GameObject> destination )
	{
		if ( source is not IEnumerable enumerable )
			return false;

		foreach ( var item in enumerable )
		{
			if ( item is GameObject go && go is not null )
			{
				destination.Add( go );
				continue;
			}

			if ( item is null )
				continue;

			var itemType = item.GetType();
			var goProp = itemType.GetProperty( "GameObject", BindingFlags.Public | BindingFlags.Instance )
			          ?? itemType.GetProperty( "Object", BindingFlags.Public | BindingFlags.Instance );

			if ( goProp?.GetValue( item ) is GameObject wrapped && wrapped is not null )
				destination.Add( wrapped );
		}

		return true;
	}
}