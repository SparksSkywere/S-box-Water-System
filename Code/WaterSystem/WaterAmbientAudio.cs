using System;

namespace WaterSystem;

/// <summary>
/// Shared proximity ambience for water sources (rivers / volumes).
/// Emitter follows the nearest surface point on that source toward the listener.
/// </summary>
internal static class WaterAmbientAudio
{
	internal static void Update(
		ref SoundHandle handle,
		SoundEvent sound,
		float volumeScale,
		float hearDistance,
		float fullVolumeDistance,
		Vector3 emitterWorld,
		Vector3 listenerWorld,
		bool muted )
	{
		if ( sound is null || muted )
		{
			Stop( ref handle );
			return;
		}

		var hear = MathF.Max( 50f, hearDistance );
		var distance = (listenerWorld - emitterWorld).Length;
		if ( distance > hear )
		{
			Stop( ref handle );
			return;
		}

		var full = MathX.Clamp( fullVolumeDistance, 0f, hear );
		var falloff = hear <= full
			? 1f
			: 1f - MathX.Clamp( (distance - full) / (hear - full), 0f, 1f );
		falloff *= falloff;

		var managerScale = WaterSystemManager.Active is not null
			? WaterSystemManager.Active.AmbientSoundVolume
			: 1f;
		var volume = MathF.Max( 0f, volumeScale ) * managerScale * falloff;
		if ( volume <= 0.01f )
		{
			Stop( ref handle );
			return;
		}

		if ( handle is null || !handle.IsValid() || !handle.IsPlaying )
		{
			handle = Sound.Play( sound, emitterWorld );
			if ( handle is null || !handle.IsValid() )
				return;

			handle.SpacialBlend = 1f;
			handle.ListenLocal = false;
			handle.OcclusionEnabled = true;
			handle.Distance = hear;
		}

		handle.Position = emitterWorld;
		handle.Volume = volume;
	}

	internal static void Stop( ref SoundHandle handle )
	{
		if ( handle is null )
			return;

		if ( handle.IsValid() )
			handle.Stop();
		handle = null;
	}

	internal static bool TryGetListener( Scene scene, out Vector3 worldPosition, out bool underwater )
	{
		worldPosition = default;
		underwater = false;
		if ( scene is null )
			return false;

		foreach ( var presence in scene.GetAllComponents<WaterSwimBridge>() )
		{
			if ( presence is null || !presence.Enabled )
				continue;

			var tracked = presence.TargetObject ?? presence.GameObject;
			if ( tracked is null || !tracked.IsValid() )
				continue;

			worldPosition = tracked.WorldPosition;
			underwater = presence.IsCameraUnderwater;
			return true;
		}

		if ( scene.Camera is not null )
		{
			worldPosition = scene.Camera.WorldPosition;
			return true;
		}

		return false;
	}

	internal static SoundEvent ResolveDefault( SoundEvent current )
		=> current
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_ambient.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_ambient_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/buoyant_player.sound" );
}
