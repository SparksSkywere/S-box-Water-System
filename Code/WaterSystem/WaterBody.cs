using System;

namespace WaterSystem;

/// <summary>
/// Water volume. Place it, set Size, tweak color — swim and buoyancy wire up automatically.
/// Origin is the center; Size X/Y is the footprint, Size Z is depth.
/// </summary>
[Title( "Water Body" )]
[Category( "Water" )]
[Icon( "water_drop" )]
public sealed class WaterBody : Component, Component.ExecuteInEditor, IWaterSource
{
	[Property, Group( "Shape" )] public string WaterName { get; set; } = "Water";
	[Property, Group( "Shape" )] public WaterShapeType Shape { get; set; } = WaterShapeType.Box;

	/// <summary>Brush size in world units. Changing this only rescales the mesh — it will not disappear.</summary>
	[Property, Group( "Shape" )] public Vector3 Size { get; set; } = new Vector3( 512f, 512f, 128f );

	[Property, Group( "Fluid" ), Range( 1f, 14000f )] public float FluidDensity { get; set; } = 1000f;
	[Property, Group( "Fluid" ), Range( 0f, 20f )] public float LinearDrag { get; set; } = 2.1f;
	[Property, Group( "Fluid" ), Range( 0f, 20f )] public float AngularDrag { get; set; } = 1.3f;
	[Property, Group( "Fluid" )] public Vector3 Current { get; set; }
	[Property, Group( "Fluid" ), Range( -256f, 256f )] public float SurfaceOffset { get; set; }
	[Property, Group( "Fluid" ), Range( 0f, 64f )] public float WaveAmplitude { get; set; } = 4f;
	[Property, Group( "Fluid" ), Range( 0.01f, 8f )] public float WaveFrequency { get; set; } = 0.8f;
	[Property, Group( "Fluid" )] public bool AllowSwimming { get; set; } = true;
	[Property, Group( "Fluid" )] public bool AffectRigidbodies { get; set; } = true;
	[Property, Group( "Fluid" ), Range( 4f, 300f )] public float SwimDepthThreshold { get; set; } = 36f;
	[Property, Group( "Fluid" ), Range( 0f, 200f )] public float SurfaceBandHeight { get; set; } = 16f;
	[Property, Group( "Fluid" ), Range( 0f, 200f )] public float EdgeBandWidth { get; set; } = 24f;

	[Property, Group( "Look" )] public Color WaterColor { get; set; } = new Color( 0.12f, 0.42f, 0.68f, 0.74f );
	[Property, Group( "Look" ), Range( 0f, 1f )] public float Transparency { get; set; } = 0.74f;
	[Property, Group( "Look" )] public Material WaterMaterial { get; set; }
	[Property, Group( "Look" )] public bool CreateSurfaceVisual { get; set; } = true;
	[Property, Group( "Look" )] public bool ShowVolumeGizmo { get; set; } = true;

	[Property, Group( "Audio" )] public SoundEvent AmbientSound { get; set; }
	[Property, Group( "Audio" ), Range( 0f, 2f )] public float AmbientVolume { get; set; } = 0.55f;
	[Property, Group( "Audio" ), Range( 100f, 6000f ), Title( "Ambient Hear Distance" )] public float AmbientHearDistance { get; set; } = 1800f;
	[Property, Group( "Audio" ), Range( 0f, 1500f ), Title( "Ambient Full Volume Distance" )] public float AmbientFullVolumeDistance { get; set; } = 220f;

	[Property, Group( "Setup" )] public bool AutoRegisterWithManager { get; set; } = true;

	[Button( "Copy Settings", "content_copy" ), Group( "Settings Clipboard" )]
	public void EditorCopySettings() => WaterSettingsClipboard.CopyFrom( this );

	[Button( "Paste Settings", "content_paste" ), Group( "Settings Clipboard" )]
	public void EditorPasteSettings() => WaterSettingsClipboard.PasteTo( this );

	Vector3 _lastSize;
	WaterShapeType _lastShape;
	int _lastSync;
	bool _built;
	bool _teardown;
	bool _runtimeMeshesBuilt;
	SoundHandle _ambientLoop;

	public string DisplayName => string.IsNullOrWhiteSpace( WaterName ) ? GameObject?.Name ?? "Water" : WaterName;
	public float SurfaceHeight => GetSurfaceHeight( WorldPosition );

	protected override void OnEnabled()
	{
		_teardown = false;
		if ( WaterRuntime.IsEditorSession )
		{
			_built = false;
			_runtimeMeshesBuilt = false;
			return;
		}

		Build();
		if ( AutoRegisterWithManager )
			WaterSystemManager.Register( this );
	}

	protected override void OnStart()
	{
		if ( WaterRuntime.IsEditorSession )
			return;

		if ( AutoRegisterWithManager )
			WaterSystemManager.Register( this );

		if ( !_runtimeMeshesBuilt )
			Build();
	}

	protected override void OnDisabled()
	{
		_teardown = true;
		WaterAmbientAudio.Stop( ref _ambientLoop );
		if ( !WaterRuntime.IsEditorSession )
			WaterSystemManager.Unregister( this );
	}

	protected override void OnDestroy()
	{
		_teardown = true;
		if ( GameObject.IsValid() )
			WaterRuntime.InvalidateBuiltSize( GameObject );
		WaterAmbientAudio.Stop( ref _ambientLoop );
		if ( !WaterRuntime.IsEditorSession )
			WaterSystemManager.Unregister( this );
	}

	protected override void OnUpdate()
	{
		if ( WaterRuntime.IsEditorSession || _teardown || !GameObject.IsValid() )
			return;

		if ( !_built || _lastSize != Size || _lastShape != Shape || _lastSync != GetSyncHash() || !_runtimeMeshesBuilt )
			Build();

		UpdateAmbientAudio();
	}

	protected override void DrawGizmos()
	{
		if ( !ShowVolumeGizmo )
			return;

		Gizmo.Transform = WorldTransform;
		Gizmo.Draw.LineThickness = 1.5f;
		Gizmo.Draw.Color = WaterColor.WithAlpha( 0.95f );

		var half = Size * 0.5f;
		var local = new BBox( -half, half );
		Gizmo.Draw.LineBBox( local );
		Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.25f );
		Gizmo.Draw.SolidBox( local );
	}

	/// <summary>Rebuilds trigger, buoyancy, and visuals from current inspector values.</summary>
	public void Build()
	{
		if ( _teardown || !GameObject.IsValid() )
			return;

		var clamped = WaterRuntime.MaxSize( Size );
		if ( Size != clamped )
			Size = clamped;

		if ( WaterRuntime.IsEditorSession )
		{
			_runtimeMeshesBuilt = false;
			_lastSize = Size;
			_lastShape = Shape;
			_lastSync = GetSyncHash();
			_built = true;
			return;
		}

		ApplyRuntimeSettings();
		_runtimeMeshesBuilt = true;

		_lastSize = Size;
		_lastShape = Shape;
		_lastSync = GetSyncHash();
		_built = true;
	}

	public bool TrySample( Vector3 worldPosition, out WaterSample sample )
	{
		sample = WaterSample.Miss;
		if ( !Enabled || GameObject is null || !GameObject.Enabled )
			return false;

		if ( !ContainsPoint( worldPosition, out var lateral, out var halfExtent ) )
			return false;

		var surface = GetSurfaceHeight( worldPosition );
		var depth = surface - worldPosition.z;
		var config = CreateRuntimeConfig();
		var zone = WaterRuntime.ClassifyZone( depth, lateral, halfExtent, config );
		if ( zone == WaterZoneType.None )
			return false;

		sample = new WaterSample
		{
			Hit = true,
			Source = this,
			Zone = zone,
			SurfaceHeight = surface,
			DepthInWater = depth,
			Submersion = MathX.Clamp( depth / MathF.Max( 1f, Size.z ), 0f, 1f ),
			Flow = Current,
			FluidDensity = FluidDensity,
			LateralNormalized = MathX.Clamp( lateral / MathF.Max( 1f, halfExtent ), 0f, 2f )
		};
		return true;
	}

	public bool ContainsPoint( Vector3 worldPosition ) => ContainsPoint( worldPosition, out _, out _ );

	public float GetSurfaceHeight( Vector3 worldPosition )
	{
		return WorldPosition.z + Size.z * 0.5f + SurfaceOffset;
	}

	/// <summary>Closest point on the water surface (top face / sphere shell) for ambience.</summary>
	public Vector3 GetNearestSurfacePoint( Vector3 worldPosition )
	{
		var local = WorldTransform.PointToLocal( worldPosition );

		if ( Shape == WaterShapeType.Sphere )
		{
			var radius = MathF.Max( Size.x, MathF.Max( Size.y, Size.z ) ) * 0.5f;
			var offset = local;
			if ( offset.LengthSquared < 0.0001f )
				offset = Vector3.Up;
			offset = offset.Normal * radius;
			// Prefer the upper hemisphere for surface ambience.
			if ( offset.z < 0f )
				offset.z = MathF.Abs( offset.z );
			return WorldTransform.PointToWorld( offset );
		}

		var half = Size * 0.5f;
		var surfaceLocal = new Vector3(
			MathX.Clamp( local.x, -half.x, half.x ),
			MathX.Clamp( local.y, -half.y, half.y ),
			half.z + SurfaceOffset );
		return WorldTransform.PointToWorld( surfaceLocal );
	}

	void ApplyRuntimeSettings()
	{
		// Keep host scale identity — Size is the brush size.
		GameObject.LocalScale = Vector3.One;

		var config = CreateRuntimeConfig();
		WaterRuntime.ApplySwimTag( GameObject, AllowSwimming );

		Vector3 visualSize;
		if ( Shape == WaterShapeType.Sphere )
		{
			var diameter = MathF.Max( Size.x, MathF.Max( Size.y, Size.z ) );
			WaterRuntime.ConfigureSphereTrigger( GameObject, diameter * 0.5f, Vector3.Zero );
			visualSize = new Vector3( diameter, diameter, diameter );
		}
		else
		{
			WaterRuntime.ConfigureBoxTrigger( GameObject, Size, Vector3.Zero );
			visualSize = Size;
		}

		WaterRuntime.ConfigureEngineWater( GameObject, config, Current, AffectRigidbodies );
		WaterRuntime.InvalidateBuiltSize( GameObject );
		WaterRuntime.HideVolumeMeshes( GameObject );
		WaterRuntime.DestroySplashRelays( GameObject );
		WaterRuntime.ConfigureSurfaceVisual(
			GameObject,
			Vector3.Zero,
			visualSize,
			Rotation.Identity,
			config,
			WaterMaterial,
			CreateSurfaceVisual );
	}

	void UpdateAmbientAudio()
	{
		if ( !WaterAmbientAudio.TryGetListener( Scene, out var listener, out var underwater ) )
		{
			WaterAmbientAudio.Stop( ref _ambientLoop );
			return;
		}

		var surface = GetNearestSurfacePoint( listener );
		var sound = WaterAmbientAudio.ResolveDefault( AmbientSound );
		WaterAmbientAudio.Update(
			ref _ambientLoop,
			sound,
			AmbientVolume,
			AmbientHearDistance,
			AmbientFullVolumeDistance,
			surface,
			listener,
			underwater );
	}

	bool ContainsPoint( Vector3 worldPosition, out float lateral, out float halfExtent )
	{
		var local = WorldTransform.PointToLocal( worldPosition );
		var delta = local;

		if ( Shape == WaterShapeType.Sphere )
		{
			halfExtent = MathF.Max( Size.x, MathF.Max( Size.y, Size.z ) ) * 0.5f;
			lateral = new Vector3( delta.x, delta.y, 0f ).Length;
			return delta.Length <= halfExtent + EdgeBandWidth;
		}

		var half = Size * 0.5f;
		halfExtent = MathF.Max( half.x, half.y );
		var dx = MathF.Abs( delta.x ) - half.x;
		var dy = MathF.Abs( delta.y ) - half.y;
		lateral = dx <= 0f && dy <= 0f ? 0f : MathF.Max( 0f, MathF.Max( dx, dy ) );

		var insideX = MathF.Abs( delta.x ) <= half.x + EdgeBandWidth;
		var insideY = MathF.Abs( delta.y ) <= half.y + EdgeBandWidth;
		var minZ = -half.z;
		var maxZ = half.z + SurfaceBandHeight;
		return insideX && insideY && local.z >= minZ && local.z <= maxZ;
	}

	int GetSyncHash()
	{
		unchecked
		{
			var hash = 17;
			hash = hash * 31 + Size.GetHashCode();
			hash = hash * 31 + Shape.GetHashCode();
			hash = hash * 31 + FluidDensity.GetHashCode();
			hash = hash * 31 + LinearDrag.GetHashCode();
			hash = hash * 31 + AngularDrag.GetHashCode();
			hash = hash * 31 + Current.GetHashCode();
			hash = hash * 31 + SurfaceOffset.GetHashCode();
			hash = hash * 31 + WaveAmplitude.GetHashCode();
			hash = hash * 31 + WaveFrequency.GetHashCode();
			hash = hash * 31 + SwimDepthThreshold.GetHashCode();
			hash = hash * 31 + SurfaceBandHeight.GetHashCode();
			hash = hash * 31 + EdgeBandWidth.GetHashCode();
			hash = hash * 31 + WaterColor.GetHashCode();
			hash = hash * 31 + Transparency.GetHashCode();
			hash = hash * 31 + (AllowSwimming ? 1 : 0);
			hash = hash * 31 + (AffectRigidbodies ? 1 : 0);
			hash = hash * 31 + (CreateSurfaceVisual ? 1 : 0);
			hash = hash * 31 + (WaterMaterial?.ResourcePath?.GetHashCode() ?? 0);
			return hash;
		}
	}

	WaterConfig CreateRuntimeConfig()
	{
		var config = new WaterConfig
		{
			Width = Size.x,
			Length = Size.y,
			Depth = Size.z,
			FluidDensity = FluidDensity,
			LinearDrag = LinearDrag,
			AngularDrag = AngularDrag,
			CurrentSpeed = Current.Length,
			CurrentDirection = Current,
			SurfaceOffset = SurfaceOffset,
			WaveAmplitude = WaveAmplitude,
			WaveFrequency = WaveFrequency,
			AllowSwimming = AllowSwimming,
			AffectRigidbodies = AffectRigidbodies,
			SwimDepthThreshold = SwimDepthThreshold,
			SurfaceBandHeight = SurfaceBandHeight,
			EdgeBandWidth = EdgeBandWidth,
			WaterColor = WaterColor,
			Transparency = Transparency
		};
		config.Validate();
		return config;
	}
}
