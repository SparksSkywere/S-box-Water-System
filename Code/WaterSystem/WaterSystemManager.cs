using System;
using System.Collections.Generic;

namespace WaterSystem;

/// <summary>
/// Optional scene coordinator for water queries and river linking.
/// Volumes and rivers work without this; add it when you want one place to sample from.
/// </summary>
[Title( "Water System Manager" )]
[Category( "Water" )]
[Icon( "waves" )]
public class WaterSystemManager : Component
{
	[Property] public Color GlobalWaterColor { get; set; } = new Color( 0.12f, 0.42f, 0.68f, 0.74f );
	[Property, Range( 0f, 1f )] public float AmbientSoundVolume { get; set; } = 0.5f;
	[Property] public bool AutoConnectRivers { get; set; } = true;
	/// <summary>Adds Water Presence to players so builtin MoveModeSwim can run in water triggers.</summary>
	[Property, Title( "Auto Add Water Presence" )] public bool AutoPreparePlayerSwim { get; set; } = true;
	[Property, Range( 10f, 3000f )] public float RiverConnectionDistance { get; set; } = 300f;
	[Property] public bool ShowDebugViz { get; set; }

	public new static WaterSystemManager Active { get; private set; }

	static readonly List<IWaterSource> Sources = new();

	public IReadOnlyList<IWaterSource> WaterSources => Sources;

	/// <summary>Runtime registry only — must not be [Property] or every Register dirties the scene.</summary>
	public List<RiverPathComponent> Rivers { get; private set; } = new();

	TimeSince _sincePlayerSwimCheck;

	protected override void OnStart()
	{
		if ( Active is not null && Active != this )
		{
			GameLog.Warning( "WaterSystemManager: Multiple managers detected, keeping the first one." );
			return;
		}

		Active = this;
		Refresh();
		if ( AutoPreparePlayerSwim )
			WaterSwimBridge.EnsurePlayersReady( Scene );

		_sincePlayerSwimCheck = 0;
	}

	protected override void OnUpdate()
	{
		if ( Active != this )
			return;

		if ( AutoPreparePlayerSwim && _sincePlayerSwimCheck > 2f )
		{
			_sincePlayerSwimCheck = 0;
			WaterSwimBridge.EnsurePlayersReady( Scene );
		}

		if ( AutoConnectRivers )
			AutoConnectRiverFlows();
	}

	protected override void OnDestroy()
	{
		if ( Active != this )
			return;

		Active = null;
		Sources.Clear();
	}

	public static void Register( IWaterSource source )
	{
		if ( source is null )
			return;

		if ( !Sources.Contains( source ) )
			Sources.Add( source );

		if ( source is RiverPathComponent river && Active is not null && !Active.Rivers.Contains( river ) )
			Active.Rivers.Add( river );
	}

	public static void Unregister( IWaterSource source )
	{
		if ( source is null )
			return;

		Sources.Remove( source );

		if ( source is RiverPathComponent river )
			Active?.Rivers.Remove( river );

		PruneInvalidSources();
	}

	static void PruneInvalidSources()
	{
		Sources.RemoveAll( s => s is null || !IsSourceAlive( s ) );
		Active?.Rivers.RemoveAll( r => !r.IsValid() );
	}

	static bool IsSourceAlive( IWaterSource source )
	{
		if ( source is null )
			return false;

		if ( source is Component component )
			return component.IsValid() && source.GameObject.IsValid();

		return source.GameObject.IsValid();
	}

	public void Refresh()
	{
		Sources.RemoveAll( s => s is null || s.GameObject is null );
		if ( Scene is null )
			return;

		foreach ( var body in Scene.GetAllComponents<WaterBody>() )
			Register( body );

		AutoRegisterRivers();
	}

	public void AutoRegisterRivers()
	{
		Rivers.Clear();
		if ( Scene is null )
			return;

		foreach ( var river in Scene.GetAllComponents<RiverPathComponent>() )
		{
			if ( river is null || river.GameObject is null )
				continue;

			Register( river );
		}
	}

	/// <summary>Samples the strongest water source at a world position.</summary>
	public static bool TrySample( Scene scene, Vector3 worldPosition, out WaterSample sample )
	{
		sample = WaterSample.Miss;
		var bestScore = float.MinValue;

		foreach ( var source in EnumerateSources( scene ) )
		{
			if ( source is null || !source.Enabled )
				continue;

			if ( !source.TrySample( worldPosition, out var next ) || !next.Hit )
				continue;

			var score = Score( next );
			if ( score > bestScore )
			{
				bestScore = score;
				sample = next;
			}
		}

		return sample.Hit;
	}

	/// <summary>
	/// Nearest water surface point for proximity ambience (works outside the edge band).
	/// </summary>
	public static bool TryGetNearestSurface( Scene scene, Vector3 worldPosition, out Vector3 surfacePoint, out float distance )
	{
		surfacePoint = worldPosition;
		distance = float.MaxValue;
		var found = false;

		foreach ( var source in EnumerateSources( scene ) )
		{
			if ( source is null || !source.Enabled )
				continue;

			Vector3 candidate;
			if ( source is RiverPathComponent river )
			{
				if ( !river.TryFindClosestOnPath( worldPosition, out var nearest, out _, out _, out _ ) )
					continue;

				candidate = nearest + Vector3.Up * river.SurfaceOffset;
			}
			else if ( source is WaterBody body )
			{
				candidate = body.GetNearestSurfacePoint( worldPosition );
			}
			else
			{
				continue;
			}

			var d = (worldPosition - candidate).Length;
			if ( d >= distance )
				continue;

			distance = d;
			surfacePoint = candidate;
			found = true;
		}

		return found;
	}

	public void AutoConnectRiverFlows()
	{
		for ( var i = 0; i < Rivers.Count; i++ )
		{
			var source = Rivers[i];
			if ( source is null || !source.Enabled || !source.AutoConnectOutflow )
				continue;

			var sourceExit = source.GetExitPoint();
			RiverPathComponent bestTarget = null;
			var bestDistance = float.MaxValue;

			for ( var j = 0; j < Rivers.Count; j++ )
			{
				if ( i == j )
					continue;

				var target = Rivers[j];
				if ( target is null || !target.Enabled )
					continue;

				var dist = (sourceExit - target.GetEntryPoint()).Length;
				var allowed = MathF.Min( RiverConnectionDistance, source.OutflowConnectDistance );
				if ( dist <= allowed && dist < bestDistance )
				{
					bestDistance = dist;
					bestTarget = target;
				}
			}

			source.ConnectToRiver( bestTarget );
		}
	}

	static IEnumerable<IWaterSource> EnumerateSources( Scene scene )
	{
		if ( Sources.Count > 0 )
		{
			for ( var i = Sources.Count - 1; i >= 0; i-- )
			{
				if ( Sources[i] is null || Sources[i].GameObject is null )
					Sources.RemoveAt( i );
			}

			return Sources;
		}

		if ( scene is null )
			return Array.Empty<IWaterSource>();

		var list = new List<IWaterSource>();
		list.AddRange( scene.GetAllComponents<WaterBody>() );
		list.AddRange( scene.GetAllComponents<RiverPathComponent>() );
		return list;
	}

	static float Score( WaterSample sample )
	{
		var zone = sample.Zone switch
		{
			WaterZoneType.Underwater => 4000f,
			WaterZoneType.Swim => 3000f,
			WaterZoneType.Surface => 2000f,
			WaterZoneType.Edge => 1000f,
			_ => 0f
		};

		return zone + sample.Submersion * 100f;
	}
}
