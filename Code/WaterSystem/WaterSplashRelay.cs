using System;
using System.Collections.Generic;

namespace WaterSystem;

/// <summary>
/// Optional enter/exit splash for non-player bodies in water triggers.
/// Player splash is handled by Water Presence. Relays are stripped from rivers/volumes by default.
/// </summary>
[Title( "Water Splash Relay" )]
[Category( "Water" )]
[Icon( "hearing" )]
public sealed class WaterSplashRelay : Component, Component.ITriggerListener
{
	[Property] public SoundEvent EnterSound { get; set; }
	[Property] public SoundEvent ExitSound { get; set; }
	[Property, Range( 0.05f, 2f )] public float MinInterval { get; set; } = 0.15f;
	/// <summary>Ignore trigger events briefly after spawn so load/rebuild overlaps don't splash.</summary>
	[Property, Range( 0f, 5f )] public float StartupGraceSeconds { get; set; } = 1.25f;

	static readonly Dictionary<Guid, int> Occupancy = new();
	static readonly Dictionary<Guid, TimeSince> LastSplash = new();

	TimeSince _sinceEnabled;

	protected override void OnStart()
	{
		EnterSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_splash.sound" );
		ExitSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_splash.sound" );
		_sinceEnabled = 0;
	}

	protected override void OnEnabled()
	{
		_sinceEnabled = 0;
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( _sinceEnabled < StartupGraceSeconds )
			return;

		if ( !TryGetRoot( other, out var root ) )
			return;

		// Player splash is handled by Water Presence to avoid double fire.
		if ( root.Tags.Has( WaterRuntime.PlayerTag ) )
			return;

		var id = root.Id;
		Occupancy.TryGetValue( id, out var count );
		count++;
		Occupancy[id] = count;
		if ( count != 1 )
			return;

		if ( LastSplash.TryGetValue( id, out var since ) && since < MinInterval )
			return;

		LastSplash[id] = 0;
		Play( EnterSound, other );
	}

	public void OnTriggerExit( Collider other )
	{
		if ( _sinceEnabled < StartupGraceSeconds )
			return;

		if ( !TryGetRoot( other, out var root ) )
			return;

		if ( root.Tags.Has( WaterRuntime.PlayerTag ) )
			return;

		var id = root.Id;
		if ( !Occupancy.TryGetValue( id, out var count ) )
			return;

		count--;
		if ( count <= 0 )
		{
			Occupancy.Remove( id );
			if ( LastSplash.TryGetValue( id, out var since ) && since < MinInterval * 0.5f )
				return;

			LastSplash[id] = 0;
			Play( ExitSound, other );
		}
		else
		{
			Occupancy[id] = count;
		}
	}

	static bool TryGetRoot( Collider other, out GameObject root )
	{
		root = null;
		if ( other is null || !other.GameObject.IsValid() )
			return false;

		root = other.GameObject.Root;
		return root is not null;
	}

	static void Play( SoundEvent sound, Collider other )
	{
		if ( sound is null )
			return;

		other.GameObject.PlaySound( sound );
	}
}
