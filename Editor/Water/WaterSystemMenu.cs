using System;
using System.Collections.Generic;
using WaterSystem;

namespace Sandbox.Editor;

/// <summary>
/// Spawns water volumes and rivers. Path / nudge / link editing is on RiverPathComponent.
/// </summary>
public static class WaterSystemMenu
{
	public static void CreateWater() => CreateVolume( "Water", new Vector3( 512f, 512f, 128f ) );
	public static void CreateLargeWater() => CreateVolume( "Water_Large", new Vector3( 2048f, 2048f, 256f ) );
	public static void CreatePool() => CreateVolume( "Pool", new Vector3( 256f, 512f, 64f ) );

	public static void CreateRiver() => CreateRiverObject( "River", width: 180f, depth: 80f, speed: 28f, length: 1200f );
	public static void CreateStream() => CreateRiverObject( "Stream", width: 110f, depth: 60f, speed: 70f, length: 900f );

	public static void CreateManager()
	{
		var scene = GetScene();
		if ( scene is null )
			return;

		if ( FindManager( scene ) is not null )
		{
			Log.Info( "Water: WaterSystemManager already exists." );
			return;
		}

		EnsureManager( scene );
	}

	public static void ConvertSelectedToVolume()
	{
		var scene = GetScene();
		if ( scene is null )
			return;

		EnsureManager( scene );
		var count = 0;
		foreach ( var obj in GetSelection() )
		{
		var body = obj.GetOrAddComponent<WaterBody>();
		body.WaterName = obj.Name;
		count++;
		}

		Log.Info( $"Water: Converted {count} object(s) into water volumes." );
	}

	public static void ConvertSelectedToRiver()
	{
		var scene = GetScene();
		if ( scene is null )
			return;

		EnsureManager( scene );
		var count = 0;
		foreach ( var obj in GetSelection() )
		{
			var river = obj.GetOrAddComponent<RiverPathComponent>();
			river.RiverName = obj.Name;
			river.InitSourceOnly();
			count++;
		}

		Log.Info( $"Water: Converted {count} object(s) into rivers (source point only — add points to shape)." );
	}

	public static void AddWaterPresence()
	{
		var count = 0;
		foreach ( var obj in GetSelection() )
		{
			obj.GetOrAddComponent<WaterSwimBridge>();
			count++;
		}

		Log.Info( $"Water: Added Water Presence to {count} object(s)." );
	}

	static void CreateVolume( string name, Vector3 size )
	{
		var scene = GetScene();
		if ( scene is null )
			return;

		EnsureManager( scene );
		GetSpawnTransform( out var position, out var rotation );

		var obj = scene.CreateObject();
		obj.Name = name;
		obj.WorldPosition = position;
		obj.WorldRotation = rotation;
		obj.WorldScale = Vector3.One;

		var body = obj.GetOrAddComponent<WaterBody>();
		body.WaterName = name;
		body.Size = size;

		Log.Info( $"Water: Created '{name}' at the current spawn point." );
	}

	static void CreateRiverObject( string name, float width, float depth, float speed, float length )
	{
		var scene = GetScene();
		if ( scene is null )
			return;

		EnsureManager( scene );
		GetSpawnTransform( out var position, out var rotation );

		var obj = scene.CreateObject();
		obj.Name = name;
		obj.WorldPosition = position;
		obj.WorldRotation = rotation;
		obj.WorldScale = Vector3.One;

		var river = obj.GetOrAddComponent<RiverPathComponent>();
		river.RiverName = name;
		river.Width = width;
		river.Depth = depth;
		river.CurrentSpeed = speed;
		river.FlowLength = length;
		river.FlowDirection = Vector3.Forward;
		river.InitSourceOnly();
		river.ActivateForEditor();
		river.SelectFirstControlPoint();

		Log.Info( $"Water: Created river '{name}' — drag green point 0 to place it, then Add Point to start the channel." );
	}

	static void GetSpawnTransform( out Vector3 position, out Rotation rotation )
	{
		position = Vector3.Zero;
		rotation = Rotation.Identity;

		var selection = WaterEditorSelection.GetSelectedGameObjects();
		if ( selection.Count == 0 )
			return;

		var selected = selection[0];
		if ( selected is null )
			return;

		position = selected.WorldPosition;
		rotation = selected.WorldRotation;
	}

	static IReadOnlyList<GameObject> GetSelection( bool warnIfEmpty = true )
	{
		var selection = WaterEditorSelection.GetSelectedGameObjects();
		if ( selection.Count == 0 )
		{
			if ( warnIfEmpty )
				Log.Warning( "Water: No objects selected." );

			return Array.Empty<GameObject>();
		}

		return selection;
	}

	static Scene GetScene()
	{
		var scene = SceneEditorSession.Active?.Scene;
		if ( scene is null )
			Log.Error( "Water: No active scene." );

		return scene;
	}

	/// <summary>Finds WaterSystemManager anywhere in the scene hierarchy.</summary>
	static WaterSystemManager FindManager( Scene scene )
	{
		if ( scene is null )
			return null;

		foreach ( var manager in scene.GetAllComponents<WaterSystemManager>() )
		{
			if ( manager is not null && manager.GameObject.IsValid() )
				return manager;
		}

		return null;
	}

	/// <summary>Creates WaterSystemManager in the scene hierarchy when missing.</summary>
	static WaterSystemManager EnsureManager( Scene scene )
	{
		if ( scene is null )
			return null;

		var existing = FindManager( scene );
		if ( existing is not null )
			return existing;

		var obj = scene.CreateObject();
		obj.Name = "WaterSystemManager";
		var manager = obj.GetOrAddComponent<WaterSystemManager>();
		Log.Info( "Water: Created WaterSystemManager." );
		return manager;
	}
}
