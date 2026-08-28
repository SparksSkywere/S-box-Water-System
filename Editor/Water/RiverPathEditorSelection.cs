using System.Collections.Generic;
using WaterSystem;

namespace Sandbox.Editor;

/// <summary>
/// Resolves River Path selection from hierarchy clicks and keeps gizmo-driven selection sticky.
/// </summary>
public static class RiverPathEditorSelection
{
	static RiverPathComponent _bound;
	static int _lastHierarchySelectionHash;

	public static RiverPathComponent BoundRiver => _bound;

	public static bool SyncFromHierarchy( bool selectFirstPointOnChange )
	{
		var selectedObjects = WaterEditorSelection.GetSelectedGameObjects();
		var selectionHash = GetSelectionHash( selectedObjects );
		var selectionChanged = selectionHash != _lastHierarchySelectionHash;
		_lastHierarchySelectionHash = selectionHash;

		var river = ResolveSelectedRiver();
		if ( river.IsValid() )
		{
			var changed = river != _bound;
			if ( changed )
				_bound?.ClearControlPointSelection();

			_bound = river;
			river.ActivateForEditor();
			if ( selectFirstPointOnChange && changed )
				river.SelectFirstControlPoint();
			return true;
		}

		if ( selectedObjects.Count == 0 )
		{
			if ( RiverPathComponent.EditorActiveRiver.IsValid() )
			{
				_bound = RiverPathComponent.EditorActiveRiver;
				return true;
			}

			if ( _bound.IsValid() )
				return true;

			if ( selectionChanged )
				Clear();
			return false;
		}

		if ( RiverPathComponent.EditorActiveRiver.IsValid() && !selectionChanged )
		{
			_bound = RiverPathComponent.EditorActiveRiver;
			return true;
		}

		if ( selectionChanged )
			Clear();
		else if ( _bound.IsValid() )
			return true;

		return false;
	}

	public static void SyncFromActiveRiver()
	{
		var river = RiverPathComponent.EditorActiveRiver;
		if ( !river.IsValid() )
			return;

		if ( river == _bound )
			return;

		_bound = river;
	}

	public static void Clear()
	{
		_bound?.ClearControlPointSelection();
		_bound = null;
		_lastHierarchySelectionHash = 0;
		RiverPathComponent.EditorActiveRiver = null;
	}

	public static RiverPathComponent ResolveSelectedRiver()
	{
		var selectedObjects = WaterEditorSelection.GetSelectedGameObjects();
		if ( selectedObjects.Count == 0 )
			return null;

		var selectedSet = new HashSet<GameObject>();
		foreach ( var selected in selectedObjects )
		{
			if ( selected.IsValid() )
				selectedSet.Add( selected );
		}

		foreach ( var selected in selectedObjects )
		{
			if ( !selected.IsValid() )
				continue;

			var river = selected.GetComponent<RiverPathComponent>();
			if ( river.IsValid() )
				return river;

			river = FindRiverOnAncestors( selected );
			if ( river.IsValid() )
				return river;
		}

		foreach ( var selected in selectedObjects )
		{
			if ( !selected.IsValid() )
				continue;

			var rivers = CollectRiversInHierarchy( selected );
			if ( rivers.Count == 0 )
				continue;

			if ( rivers.Count == 1 )
				return rivers[0];

			foreach ( var river in rivers )
			{
				if ( selectedSet.Contains( river.GameObject ) )
					return river;
			}

			return rivers[0];
		}

		return null;
	}

	static RiverPathComponent FindRiverOnAncestors( GameObject go )
	{
		var parent = go.Parent;
		while ( parent.IsValid() )
		{
			var river = parent.GetComponent<RiverPathComponent>();
			if ( river.IsValid() )
				return river;

			parent = parent.Parent;
		}

		return null;
	}

	static List<RiverPathComponent> CollectRiversInHierarchy( GameObject root )
	{
		var rivers = new List<RiverPathComponent>();
		CollectRiversRecursive( root, rivers );
		return rivers;
	}

	static void CollectRiversRecursive( GameObject node, List<RiverPathComponent> rivers )
	{
		if ( !node.IsValid() )
			return;

		var river = node.GetComponent<RiverPathComponent>();
		if ( river.IsValid() )
			rivers.Add( river );

		foreach ( var child in node.Children )
			CollectRiversRecursive( child, rivers );
	}

	static int GetSelectionHash( List<GameObject> selectedObjects )
	{
		unchecked
		{
			var hash = 17;
			foreach ( var go in selectedObjects )
			{
				if ( !go.IsValid() )
					continue;
				hash = hash * 31 + go.Id.GetHashCode();
			}
			return hash;
		}
	}
}
