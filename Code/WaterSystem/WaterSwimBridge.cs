using System;
using Sandbox.Audio;
using Sandbox.Movement;

namespace WaterSystem;

/// <summary>
/// Enables builtin swimming. Put this on the player (or use Water Manager auto-add).
/// It only wires <see cref="MoveModeSwim"/> / tags — actual swim movement is handled by
/// PlayerController when touching <c>water</c>-tagged triggers on Water Body / River Path.
/// Also drives enter/exit splash, underwater loop, DSP, and underwater post process.
/// </summary>
[Title( "Water Presence" )]
[Category( "Water" )]
[Icon( "pool" )]
public class WaterSwimBridge : Component
{
	/// <summary>Player eye depth (units below surface) before underwater FX/audio turns on.</summary>
	public const float UnderwaterEyeDepth = 2f;

	[Property, Group( "Target" )] public GameObject TargetObject { get; set; }

	/// <summary>Adds Sandbox.Movement.MoveModeSwim so the engine can switch into swim in water triggers.</summary>
	[Property, Group( "Builtin Swim" )] public bool EnableBuiltinSwim { get; set; } = true;

	[Property, Group( "Builtin Swim" )] public bool AutoFindRigidbody { get; set; } = true;
	[Property, Group( "Builtin Swim" )] public Rigidbody TargetBody { get; set; }

	/// <summary>Optional soft river drift while the engine reports swimming (0 = pure builtin only).</summary>
	[Property, Group( "Builtin Swim" ), Range( 0f, 1f )] public float SwimCurrentScale { get; set; }

	[Property, Group( "Optional Modes" )] public Component LandMovementComponent { get; set; }
	[Property, Group( "Optional Modes" )] public Component SwimMovementComponent { get; set; }
	[Property, Group( "Optional Modes" )] public bool DisableLandWhenSwimming { get; set; }
	[Property, Group( "Optional Modes" )] public bool EnableSwimWhenSwimming { get; set; } = true;

	[Property, Group( "Non-Player Assist" )] public bool ApplyBuoyancyAssist { get; set; }
	[Property, Group( "Non-Player Assist" ), Range( 0f, 200f )] public float BuoyancyAssist { get; set; } = 40f;
	[Property, Group( "Non-Player Assist" )] public bool ApplyUnderwaterDragAssist { get; set; }
	[Property, Group( "Non-Player Assist" ), Range( 0f, 1f )] public float UnderwaterDragAssist { get; set; } = 0.2f;

	[Property, Group( "Audio" )] public SoundEvent EnterWaterSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent ExitWaterSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent UnderwaterLoopSound { get; set; }
	[Property, Group( "Audio" ), Range( 0f, 2f )] public float UnderwaterLoopVolume { get; set; } = 0.55f;
	[Property, Group( "Audio" )] public SoundEvent BuoyantLoopSound { get; set; }
	[Property, Group( "Audio" ), Range( 0f, 2f )] public float BuoyantLoopVolume { get; set; } = 0.45f;
	[Property, Group( "Audio" )] public SoundEvent EdgeRunLoopSound { get; set; }
	[Property, Group( "Audio" ), Range( 0f, 2f )] public float EdgeRunLoopVolume { get; set; } = 0.4f;
	[Property, Group( "Audio" )] public SoundEvent EdgeSloshSound { get; set; }
	[Property, Group( "Audio" ), Range( 0f, 2f )] public float EdgeSloshVolume { get; set; } = 0.7f;
	[Property, Group( "Audio" ), Range( 5f, 200f )] public float EdgeSloshMinSpeed { get; set; } = 12f;
	[Property, Group( "Audio" ), Range( 0.1f, 1.2f )] public float EdgeSloshInterval { get; set; } = 0.38f;
	[Property, Group( "Audio" ), Range( 0.1f, 1.2f )] public float EdgeSloshRunInterval { get; set; } = 0.28f;
	[Property, Group( "Audio" )] public bool ApplyUnderwaterDsp { get; set; } = true;

	[Property, Group( "Look" )] public bool EnableUnderwaterPostProcess { get; set; } = true;
	[Property, Group( "Look" )] public Color UnderwaterTint { get; set; } = new Color( 0.08f, 0.28f, 0.48f );
	[Property, Group( "Look" ), Range( 0f, 1f )] public float UnderwaterDarken { get; set; } = 0.4f;
	[Property, Group( "Look" )] public bool EnableUnderwaterFog { get; set; } = true;
	[Property, Group( "Look" ), Range( 50f, 4000f )] public float UnderwaterFogStart { get; set; } = 40f;
	[Property, Group( "Look" ), Range( 100f, 8000f )] public float UnderwaterFogEnd { get; set; } = 700f;

	[Property, Group( "State" )] public bool IsInWater { get; private set; }
	[Property, Group( "State" )] public bool IsSubmerged { get; private set; }
	[Property, Group( "State" )] public bool IsSwimming { get; private set; }
	[Property, Group( "State" )] public bool IsUnderwater { get; private set; }
	[Property, Group( "State" )] public bool IsCameraUnderwater { get; private set; }
	[Property, Group( "State" )] public bool IsNearWaterEdge { get; private set; }
	[Property, Group( "State" )] public WaterZoneType CurrentZone { get; private set; } = WaterZoneType.None;
	[Property, Group( "State" )] public MoveModeSwim SwimMode { get; private set; }

	/// <summary>Inspector alias.</summary>
	public bool AutoAddSwimMoveMode
	{
		get => EnableBuiltinSwim;
		set => EnableBuiltinSwim = value;
	}

	PlayerController _player;
	WaterSample _lastSample;
	bool _wasSubmerged;
	bool _splashSeeded;
	bool _wasCameraUnderwater;
	bool _wasBuoyant;
	bool _wasNearEdge;
	bool _hasSloshPos;
	SoundHandle _underwaterLoop;
	SoundHandle _buoyantLoop;
	SoundHandle _edgeRunLoop;
	DspProcessor _underwaterDsp;
	GradientFog _underwaterFog;
	bool _fogOwned;
	TimeSince _sinceEdgeSlosh;
	Vector3 _lastSloshPos;

	protected override void OnStart()
	{
		ResolveTargets();
		EnsureDefaultSounds();
		if ( EnableBuiltinSwim )
			EnsureBuiltinSwim();
	}

	protected override void OnEnabled()
	{
		ResolveTargets();
		EnsureDefaultSounds();
		if ( EnableBuiltinSwim )
			EnsureBuiltinSwim();
	}

	protected override void OnDisabled()
	{
		ClearUnderwaterFx();
	}

	protected override void OnDestroy()
	{
		ClearUnderwaterFx();
	}

	protected override void OnUpdate()
	{
		var tracked = TargetObject ?? GameObject;
		if ( tracked is null )
			return;

		if ( EnableBuiltinSwim && SwimMode is null )
			EnsureBuiltinSwim();

		EvaluateZone( tracked.WorldPosition );
		ApplyOptionalModeToggles();
		ApplyNonPlayerPhysicsAssist();
		ApplyPresenceTag( tracked );
		UpdatePlayerSplash();
		UpdateCameraUnderwaterFx();
		UpdateBuoyantAudio();
		UpdateEdgeAudio( tracked );
	}

	protected override void OnFixedUpdate()
	{
		// Builtin MoveModeSwim owns player motion. Optional soft current only.
		if ( !EnableBuiltinSwim || SwimCurrentScale <= 0.001f || !IsSwimming )
			return;

		var body = TargetBody ?? _player?.Body ?? _player?.GetComponent<Rigidbody>();
		if ( body is null || !body.Enabled || !body.MotionEnabled )
			return;

		if ( !_lastSample.Hit || _lastSample.Flow.LengthSquared < 0.01f )
			return;

		body.ApplyForce( _lastSample.Flow * (SwimCurrentScale * body.Mass) );
	}

	void ResolveTargets()
	{
		TargetObject ??= GameObject;
		_player = TargetObject?.GetComponent<PlayerController>()
			?? TargetObject?.GetComponentInParent<PlayerController>()
			?? GetComponentInParent<PlayerController>();

		if ( AutoFindRigidbody )
			TargetBody ??= TargetObject?.GetComponent<Rigidbody>()
				?? _player?.Body
				?? _player?.GetComponent<Rigidbody>();
	}

	void EnsureDefaultSounds()
	{
		EnterWaterSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_splash.sound" );
		ExitWaterSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_splash.sound" );
		UnderwaterLoopSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/underwater_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/underwater.sound" );
		BuoyantLoopSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/buoyant_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_flow_loop1.sound" );
		EdgeRunLoopSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_edge_run_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_run.sound" );
		EdgeSloshSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_slosh_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_splash.sound" );
	}

	/// <summary>Wires the engine swim move mode onto the player — that is what actually swims.</summary>
	public void EnsureBuiltinSwim()
	{
		ResolveTargets();
		var host = _player?.GameObject ?? TargetObject ?? GameObject;
		if ( host is null )
			return;

		SwimMode = host.GetComponent<MoveModeSwim>() ?? host.GetComponentInChildren<MoveModeSwim>();
		if ( SwimMode is null )
			SwimMode = host.GetOrAddComponent<MoveModeSwim>();

		SwimMode.Enabled = true;
	}

	CameraComponent ResolveCamera()
	{
		if ( Scene?.Camera is not null )
			return Scene.Camera;

		return _player?.GetComponentInChildren<CameraComponent>()
			?? TargetObject?.GetComponentInChildren<CameraComponent>()
			?? GetComponentInChildren<CameraComponent>();
	}

	/// <summary>
	/// Underwater probe uses player eyes (third person camera is often above water).
	/// </summary>
	Vector3 GetSubmersionProbe()
	{
		if ( _player is not null )
			return _player.EyePosition;

		var cam = ResolveCamera();
		if ( cam is not null )
			return cam.WorldPosition;

		var tracked = TargetObject ?? GameObject;
		return tracked is null ? WorldPosition : tracked.WorldPosition + Vector3.Up * 64f;
	}

	void EvaluateZone( Vector3 worldPosition )
	{
		CurrentZone = WaterZoneType.None;
		_lastSample = WaterSample.Miss;

		if ( WaterSystemManager.TrySample( Scene, worldPosition, out var sample ) )
		{
			_lastSample = sample;
			CurrentZone = sample.Zone;
		}

		IsSubmerged = IsSubmergedZone( CurrentZone );
		IsSwimming = CurrentZone == WaterZoneType.Swim || CurrentZone == WaterZoneType.Underwater;
		IsUnderwater = CurrentZone == WaterZoneType.Underwater;

		// Engine swim mode = actually in the water volume (not the dry edge band).
		if ( _player is not null && _player.IsSwimming )
		{
			IsSubmerged = true;
			IsSwimming = true;
		}

		// in_water tag + splash use submerged only — Edge band is dry bank.
		IsInWater = IsSubmerged;

		IsCameraUnderwater = EvaluatePlayerUnderwater();
		if ( IsCameraUnderwater )
			IsUnderwater = true;

		IsNearWaterEdge = EvaluateNearEdge();
	}

	static bool IsSubmergedZone( WaterZoneType zone )
		=> zone is WaterZoneType.Swim or WaterZoneType.Surface or WaterZoneType.Underwater;

	bool EvaluateNearEdge()
	{
		// Edge ambience is for dry bank walking — never while submerged.
		if ( IsSubmerged || (_player is not null && _player.IsSwimming) )
			return false;

		if ( CurrentZone == WaterZoneType.Edge )
			return true;

		// Hugging the inner bank while still dry.
		if ( _lastSample.Hit && _lastSample.LateralNormalized >= 0.72f )
			return true;

		return false;
	}

	bool EvaluatePlayerUnderwater()
	{
		var probe = GetSubmersionProbe();

		if ( WaterSystemManager.TrySample( Scene, probe, out var eyeSample ) && eyeSample.Hit )
		{
			if ( eyeSample.DepthInWater >= UnderwaterEyeDepth )
				return true;
		}

		// Swimming with eyes below last known surface (covers sparse samples / bank edges).
		if ( (IsSwimming || (_player is not null && _player.IsSwimming))
			&& _lastSample.Hit
			&& probe.z < _lastSample.SurfaceHeight - UnderwaterEyeDepth )
			return true;

		return false;
	}

	void UpdatePlayerSplash()
	{
		if ( _player is null )
		{
			_wasSubmerged = false;
			_splashSeeded = false;
			return;
		}

		// Splash follows engine swim triggers — not the dry edge sample band.
		var submerged = _player.IsSwimming;

		// First valid sample only seeds state so spawn / load never fake an enter splash.
		if ( !_splashSeeded )
		{
			_wasSubmerged = submerged;
			_splashSeeded = true;
			return;
		}

		if ( submerged && !_wasSubmerged )
			PlayLocal( EnterWaterSound );

		if ( !submerged && _wasSubmerged )
			PlayLocal( ExitWaterSound );

		_wasSubmerged = submerged;
	}

	void UpdateCameraUnderwaterFx()
	{
		if ( IsCameraUnderwater == _wasCameraUnderwater )
		{
			if ( IsCameraUnderwater )
			{
				StartUnderwaterLoop();
				RefreshUnderwaterLook();
			}

			return;
		}

		if ( IsCameraUnderwater )
		{
			StartUnderwaterLoop();
			SetUnderwaterDsp( true );
			SetUnderwaterLook( true );
		}
		else
		{
			StopUnderwaterLoop();
			SetUnderwaterDsp( false );
			SetUnderwaterLook( false );
		}

		_wasCameraUnderwater = IsCameraUnderwater;
	}

	void ClearUnderwaterFx()
	{
		StopUnderwaterLoop();
		StopBuoyantLoop();
		StopEdgeRunLoop();
		SetUnderwaterDsp( false );
		SetUnderwaterLook( false );
		_wasCameraUnderwater = false;
		_wasBuoyant = false;
		_wasNearEdge = false;
	}

	void UpdateBuoyantAudio()
	{
		// Surface swim / floating — not bank edge, not submerged-underwater FX.
		var buoyant = IsSubmerged
			&& !IsCameraUnderwater
			&& CurrentZone != WaterZoneType.Edge
			&& (IsSwimming || CurrentZone == WaterZoneType.Swim || CurrentZone == WaterZoneType.Surface);

		if ( buoyant == _wasBuoyant )
		{
			if ( buoyant )
				StartBuoyantLoop();
			return;
		}

		if ( buoyant )
			StartBuoyantLoop();
		else
			StopBuoyantLoop();

		_wasBuoyant = buoyant;
	}

	void UpdateEdgeAudio( GameObject tracked )
	{
		var nearEdge = IsNearWaterEdge && !IsSubmerged && !IsCameraUnderwater;

		if ( !nearEdge )
		{
			if ( _wasNearEdge )
				StopEdgeRunLoop();
			_wasNearEdge = false;
			_hasSloshPos = false;
			return;
		}

		StartEdgeRunLoop();
		TryPlayEdgeSlosh( tracked );

		if ( !_wasNearEdge )
		{
			_lastSloshPos = tracked.WorldPosition;
			_hasSloshPos = true;
			_sinceEdgeSlosh = EdgeSloshInterval;
		}

		_wasNearEdge = true;
	}

	void StartEdgeRunLoop()
	{
		EdgeRunLoopSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_edge_run_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_run.sound" );
		if ( EdgeRunLoopSound is null )
			return;

		if ( _edgeRunLoop is not null && _edgeRunLoop.IsValid() && _edgeRunLoop.IsPlaying )
		{
			_edgeRunLoop.Volume = EdgeRunLoopVolume;
			return;
		}

		_edgeRunLoop = Sound.Play( EdgeRunLoopSound );
		if ( _edgeRunLoop is null || !_edgeRunLoop.IsValid() )
			return;

		_edgeRunLoop.Volume = EdgeRunLoopVolume;
		_edgeRunLoop.SpacialBlend = 0f;
		_edgeRunLoop.ListenLocal = true;
		_edgeRunLoop.OcclusionEnabled = false;
	}

	void StopEdgeRunLoop()
	{
		if ( _edgeRunLoop is null )
			return;

		if ( _edgeRunLoop.IsValid() )
			_edgeRunLoop.Stop();
		_edgeRunLoop = null;
	}

	float GetHorizontalMoveSpeed( GameObject tracked )
	{
		var body = _player?.Body ?? TargetBody ?? tracked?.GetComponent<Rigidbody>();
		if ( body is not null && body.Enabled )
		{
			var vel = body.Velocity;
			vel.z = 0f;
			if ( vel.Length > 0.5f )
				return vel.Length;
		}

		if ( !_hasSloshPos || tracked is null )
			return 0f;

		var delta = tracked.WorldPosition - _lastSloshPos;
		delta.z = 0f;
		return delta.Length / MathF.Max( 0.001f, Time.Delta );
	}

	void TryPlayEdgeSlosh( GameObject tracked )
	{
		if ( tracked is null )
			return;

		EdgeSloshSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_slosh_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/water_splash.sound" );
		if ( EdgeSloshSound is null )
			return;

		var speed = GetHorizontalMoveSpeed( tracked );
		_lastSloshPos = tracked.WorldPosition;
		_hasSloshPos = true;

		if ( speed < EdgeSloshMinSpeed )
			return;

		var interval = speed >= 120f ? EdgeSloshRunInterval : EdgeSloshInterval;
		if ( _sinceEdgeSlosh < interval )
			return;

		_sinceEdgeSlosh = 0;
		var handle = Sound.Play( EdgeSloshSound );
		if ( handle is null || !handle.IsValid() )
			return;

		handle.Volume = EdgeSloshVolume;
		handle.SpacialBlend = 0f;
		handle.ListenLocal = true;
		handle.OcclusionEnabled = false;
	}

	void StartBuoyantLoop()
	{
		BuoyantLoopSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/buoyant_player.sound" );
		if ( BuoyantLoopSound is null )
			return;

		if ( _buoyantLoop is not null && _buoyantLoop.IsValid() && _buoyantLoop.IsPlaying )
		{
			_buoyantLoop.Volume = BuoyantLoopVolume;
			return;
		}

		_buoyantLoop = Sound.Play( BuoyantLoopSound );
		if ( _buoyantLoop is null || !_buoyantLoop.IsValid() )
			return;

		_buoyantLoop.Volume = BuoyantLoopVolume;
		_buoyantLoop.SpacialBlend = 0f;
		_buoyantLoop.ListenLocal = true;
		_buoyantLoop.OcclusionEnabled = false;
	}

	void StopBuoyantLoop()
	{
		if ( _buoyantLoop is null )
			return;

		if ( _buoyantLoop.IsValid() )
			_buoyantLoop.Stop();
		_buoyantLoop = null;
	}

	void StartUnderwaterLoop()
	{
		StopBuoyantLoop();
		_wasBuoyant = false;

		UnderwaterLoopSound ??= ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/underwater_player.sound" )
			?? ResourceLibrary.Get<SoundEvent>( "sounds/ambient/water/underwater.sound" );
		if ( UnderwaterLoopSound is null )
			return;

		if ( _underwaterLoop is not null && _underwaterLoop.IsValid() && _underwaterLoop.IsPlaying )
		{
			_underwaterLoop.Volume = UnderwaterLoopVolume;
			return;
		}

		_underwaterLoop = Sound.Play( UnderwaterLoopSound );
		if ( _underwaterLoop is null || !_underwaterLoop.IsValid() )
			return;

		_underwaterLoop.Volume = UnderwaterLoopVolume;
		_underwaterLoop.SpacialBlend = 0f;
		_underwaterLoop.ListenLocal = true;
		_underwaterLoop.OcclusionEnabled = false;
	}

	void StopUnderwaterLoop()
	{
		if ( _underwaterLoop is null )
			return;

		if ( _underwaterLoop.IsValid() )
			_underwaterLoop.Stop();
		_underwaterLoop = null;
	}

	void SetUnderwaterDsp( bool enabled )
	{
		if ( !ApplyUnderwaterDsp )
			return;

		var gameMixer = Mixer.FindMixerByName( "Game" ) ?? Mixer.FindMixerByName( "SFX" );
		if ( gameMixer is null )
			return;

		if ( enabled )
		{
			_underwaterDsp ??= new DspProcessor { Effect = "water.small" };
			gameMixer.AddProcessor( _underwaterDsp );
		}
		else if ( _underwaterDsp is not null )
		{
			gameMixer.RemoveProcessor( _underwaterDsp );
		}
	}

	void SetUnderwaterLook( bool enabled )
	{
		var cam = ResolveCamera();
		if ( cam is null || !cam.GameObject.IsValid() )
			return;

		if ( EnableUnderwaterPostProcess )
		{
			var fx = cam.GameObject.GetOrAddComponent<WaterUnderwaterEffect>();
			fx.Tint = UnderwaterTint;
			fx.Darken = UnderwaterDarken;
			fx.Amount = enabled ? 1f : 0f;
			fx.Enabled = enabled;
		}

		if ( EnableUnderwaterFog )
			SetUnderwaterFog( cam, enabled );
	}

	void RefreshUnderwaterLook()
	{
		var cam = ResolveCamera();
		if ( cam is null || !cam.GameObject.IsValid() )
			return;

		if ( EnableUnderwaterPostProcess )
		{
			var fx = cam.GameObject.GetComponent<WaterUnderwaterEffect>();
			if ( fx is not null )
			{
				fx.Tint = UnderwaterTint;
				fx.Darken = UnderwaterDarken;
				fx.Amount = 1f;
				fx.Enabled = true;
			}
		}

		if ( EnableUnderwaterFog && _underwaterFog is not null && _fogOwned )
		{
			_underwaterFog.Color = UnderwaterTint;
			_underwaterFog.StartDistance = UnderwaterFogStart;
			_underwaterFog.EndDistance = UnderwaterFogEnd;
		}
	}

	void SetUnderwaterFog( CameraComponent cam, bool enabled )
	{
		if ( enabled )
		{
			_underwaterFog ??= cam.GameObject.GetOrAddComponent<GradientFog>();
			_fogOwned = true;
			_underwaterFog.Enabled = true;
			_underwaterFog.Color = UnderwaterTint;
			_underwaterFog.StartDistance = UnderwaterFogStart;
			_underwaterFog.EndDistance = UnderwaterFogEnd;
			_underwaterFog.Height = 8000f;
			_underwaterFog.FalloffExponent = 1.2f;
			_underwaterFog.VerticalFalloffExponent = 1.1f;
		}
		else if ( _fogOwned && _underwaterFog is not null )
		{
			_underwaterFog.Enabled = false;
		}
	}

	void PlayLocal( SoundEvent sound )
	{
		if ( sound is null )
			return;

		var handle = Sound.Play( sound );
		if ( handle is null || !handle.IsValid() )
			return;

		handle.ListenLocal = true;
		handle.SpacialBlend = 0f;
		handle.OcclusionEnabled = false;
		handle.Volume = 1f;
	}

	void ApplyOptionalModeToggles()
	{
		if ( LandMovementComponent is not null && DisableLandWhenSwimming )
			LandMovementComponent.Enabled = !IsSwimming;

		if ( SwimMovementComponent is not null && EnableSwimWhenSwimming )
			SwimMovementComponent.Enabled = IsSwimming;
	}

	void ApplyNonPlayerPhysicsAssist()
	{
		if ( _player is not null )
			return;

		if ( TargetBody is null || !TargetBody.Enabled || !TargetBody.MotionEnabled )
			return;

		if ( ApplyBuoyancyAssist && IsSwimming )
		{
			var multiplier = IsUnderwater ? 1.2f : 1f;
			TargetBody.ApplyForce( Vector3.Up * BuoyancyAssist * TargetBody.Mass * multiplier );
		}

		if ( ApplyUnderwaterDragAssist && IsUnderwater )
			TargetBody.Velocity *= MathX.Clamp( 1f - UnderwaterDragAssist, 0f, 1f );
	}

	void ApplyPresenceTag( GameObject tracked )
	{
		if ( IsInWater )
		{
			tracked.Tags.Add( WaterRuntime.InWaterTag );
			_player?.GameObject?.Tags.Add( WaterRuntime.InWaterTag );
		}
		else
		{
			tracked.Tags.Remove( WaterRuntime.InWaterTag );
			_player?.GameObject?.Tags.Remove( WaterRuntime.InWaterTag );
		}
	}

	/// <summary>
	/// Adds Water Presence to each player. Presence then enables builtin MoveModeSwim.
	/// Does not implement swimming itself.
	/// </summary>
	public static void EnsurePlayersReady( Scene scene )
	{
		if ( scene is null )
			return;

		foreach ( var player in scene.GetAllComponents<PlayerController>() )
		{
			if ( player is null || player.GameObject is null )
				continue;

			var host = player.GameObject;
			var presence = host.GetComponent<WaterSwimBridge>() ?? host.GetComponentInChildren<WaterSwimBridge>();
			if ( presence is null )
			{
				presence = host.GetOrAddComponent<WaterSwimBridge>();
				presence.TargetObject = host;
				presence.EnableBuiltinSwim = true;
			}

			if ( presence.EnableBuiltinSwim )
				presence.EnsureBuiltinSwim();
		}
	}
}
