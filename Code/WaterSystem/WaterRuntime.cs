using System;
using System.Collections.Generic;
using System.Linq;

namespace WaterSystem;

/// <summary>
/// Shared builders for swim tags, triggers, buoyancy, and water visuals.
/// Tracks last built size so inspector Size edits do not thrash-rebuild the mesh.
/// </summary>
internal static class WaterRuntime
{
	internal const string WaterTag = "water";
	internal const string InWaterTag = "in_water";
	internal const string PlayerTag = "player";
	internal const string CharacterTag = "character";
	internal const string SurfaceChildName = "_WaterSurface";
	internal const string VolumeChildName = "_WaterVolume";
	internal const string SegmentPrefix = "WaterSeg_";
	internal const string DefaultMaterialPath = "materials/world/water/mat_water_default.vmat";
	internal const string FallbackMaterialPath = "materials/default/glass_default.vmat";

	internal static bool IsEditorSession => Game.IsEditor && !Game.IsPlaying;

	internal static bool IsDeserializing( GameObject go )
	{
		while ( go.IsValid() )
		{
			if ( (go.Flags & GameObjectFlags.Deserializing) != 0 )
				return true;

			go = go.Parent;
		}

		return false;
	}

	/// <summary>
	/// Procedural segment/visual objects must not be written into .scene files — undo
	/// restores them from snapshots and rebuild would fight native WaterVolume state.
	/// </summary>
	internal static void MarkRuntimeGenerated( GameObject go )
	{
		if ( !go.IsValid() )
			return;

		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
	}

	// Last baked size per visual child — do not use RenderBounds for this (unreliable mid-rebuild).
	static readonly Dictionary<Guid, Vector3> BuiltSizes = new();

	/// <summary>PlayerController swim mode looks for this trigger tag on the collider object.</summary>
	internal static void ApplySwimTag( GameObject go, bool allowSwimming )
	{
		if ( go is null )
			return;

		if ( allowSwimming )
			go.Tags.Add( WaterTag );
		else
			go.Tags.Remove( WaterTag );
	}

	internal static BoxCollider ConfigureBoxTrigger( GameObject go, Vector3 size, Vector3 center )
	{
		var sphere = go.GetComponent<SphereCollider>();
		if ( sphere.IsValid() )
			sphere.Enabled = false;

		var box = go.GetOrAddComponent<BoxCollider>();
		box.Enabled = true;
		box.Scale = MaxSize( size );
		box.Center = center;
		box.IsTrigger = true;
		TryAssignWaterSurface( box );
		return box;
	}

	internal static SphereCollider ConfigureSphereTrigger( GameObject go, float radius, Vector3 center )
	{
		var box = go.GetComponent<BoxCollider>();
		if ( box.IsValid() )
			box.Enabled = false;

		var sphere = go.GetOrAddComponent<SphereCollider>();
		sphere.Enabled = true;
		sphere.Radius = MathF.Max( 4f, radius );
		sphere.Center = center;
		sphere.IsTrigger = true;
		TryAssignWaterSurface( sphere );
		return sphere;
	}

	static void TryAssignWaterSurface( Collider collider )
	{
		if ( collider is null )
			return;

		try
		{
			collider.Surface ??= Surface.FindByName( "water" );
		}
		catch
		{
			// Surface library may be unavailable in some editor contexts.
		}
	}

	/// <summary>
	/// Engine WaterVolume for buoyancy/current. Players are ignored by FluidVelocity so swim
	/// control (MoveModeSwim) is not overridden by river push.
	/// </summary>
	internal static Sandbox.WaterVolume ConfigureEngineWater( GameObject go, WaterConfig config, Vector3 fluidVelocity, bool enabled )
	{
		// WaterVolume is native and crashes when undo restores saved segment children.
		// Triggers + ribbon are enough while editing; full physics applies in play mode.
		if ( IsEditorSession )
			return go.GetComponent<Sandbox.WaterVolume>();

		var water = go.GetOrAddComponent<Sandbox.WaterVolume>();
		water.Enabled = enabled;
		if ( !enabled )
			return water;

		config.Validate();
		water.FluidDensity = config.FluidDensity;
		water.LinearDrag = config.LinearDrag;
		water.AngularDrag = config.AngularDrag;
		water.FluidVelocity = fluidVelocity;
		water.SurfaceOffset = config.SurfaceOffset;
		water.WaveAmplitude = config.WaveAmplitude;
		water.WaveFrequency = config.WaveFrequency;
		// Builtin swim uses water triggers; keep FluidVelocity off players so they aren't shoved.
		water.IgnoreTags.Add( PlayerTag );
		water.IgnoreTags.Add( CharacterTag );
		water.Flags |= ComponentFlags.Hidden;
		return water;
	}

	internal static void UpdateFluidVelocity( GameObject go, Vector3 fluidVelocity )
	{
		if ( IsEditorSession )
			return;

		var water = go?.GetComponent<Sandbox.WaterVolume>();
		if ( water is null )
			return;

		water.FluidVelocity = fluidVelocity;
		water.IgnoreTags.Add( PlayerTag );
		water.IgnoreTags.Add( CharacterTag );
	}

	/// <summary>
	/// Thin top surface only. Full volume boxes show as walls underwater, so we never draw them.
	/// </summary>
	internal static void ConfigureSurfaceVisual(
		GameObject host,
		Vector3 localCenter,
		Vector3 size,
		Rotation localRotation,
		WaterConfig config,
		Material materialOverride,
		bool enabled )
	{
		if ( host is null )
			return;

		// Hide full-volume mesh (shows as walls underwater).
		SetChildEnabled( host, VolumeChildName, false );

		if ( !enabled )
		{
			SetChildEnabled( host, SurfaceChildName, false );
			return;
		}

		var visualSize = MaxSize( size );
		var tint = config.GetTint();
		var surfaceThickness = MathF.Max( 4f, visualSize.z * 0.02f );
		var surface = EnsureChild( host, SurfaceChildName );
		surface.LocalPosition = localCenter + localRotation * new Vector3( 0f, 0f, visualSize.z * 0.5f );
		surface.LocalRotation = localRotation;
		surface.LocalScale = Vector3.One;
		ApplyRenderer( surface, new Vector3( visualSize.x, visualSize.y, surfaceThickness ), materialOverride, tint, config );
	}

	/// <summary>
	/// One continuous surface ribbon along the river path (no per-segment overlap).
	/// Uses a mitered strip so corners don't leave gaps when viewed from above.
	/// </summary>
	internal static void ConfigureRiverRibbon(
		GameObject host,
		IReadOnlyList<Vector3> localPoints,
		float width,
		float surfaceOffset,
		WaterConfig config,
		Material materialOverride,
		bool enabled )
	{
		if ( host is null )
			return;

		SetChildEnabled( host, VolumeChildName, false );

		if ( !enabled || localPoints is null || localPoints.Count < 2 )
		{
			SetChildEnabled( host, SurfaceChildName, false );
			return;
		}

		var tint = config.GetTint();
		var material = ResolveSurfaceMaterial( materialOverride, tint, config );
		var surface = EnsureChild( host, SurfaceChildName );
		surface.LocalPosition = Vector3.Zero;
		surface.LocalRotation = Rotation.Identity;
		surface.LocalScale = Vector3.One;

		var renderer = surface.GetOrAddComponent<ModelRenderer>();
		renderer.Enabled = true;
		renderer.MaterialOverride = material;
		renderer.Tint = tint;

		// Slightly wider than the swim channel so banks don't flash gaps.
		var drawWidth = width + 8f;
		var hash = HashRibbon( localPoints, drawWidth, surfaceOffset );
		var id = surface.Id;
		if ( !RibbonHashes.TryGetValue( id, out var prev ) || prev != hash || renderer.Model is null )
		{
			renderer.Model = BuildRibbonModel( localPoints, drawWidth, surfaceOffset, material );
			RibbonHashes[id] = hash;
			BuiltSizes.Remove( id );
		}
	}

	/// <summary>Destroy leftover per-segment water meshes (legacy volume/surface children).</summary>
	internal static void DestroySegmentVisuals( GameObject host )
	{
		if ( !host.IsValid() )
			return;

		for ( var i = host.Children.Count - 1; i >= 0; i-- )
		{
			var child = host.Children[i];
			if ( !child.IsValid() )
				continue;
			if ( child.Name == VolumeChildName || child.Name == SurfaceChildName )
				child.Enabled = false;
		}

		var renderer = host.GetComponent<ModelRenderer>();
		if ( renderer.IsValid() )
			renderer.Enabled = false;
	}

	/// <summary>
	/// Player splash lives on Water Presence. Strip legacy relays so load/rebuild
	/// trigger storms don't play distant splash sounds.
	/// </summary>
	internal static void DestroySplashRelays( GameObject host )
	{
		if ( !host.IsValid() )
			return;

		foreach ( var relay in host.Components.GetAll<WaterSplashRelay>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( relay.IsValid() )
				relay.Enabled = false;
		}
	}

	/// <summary>
	/// Drops an extra segment brush. In the editor we disable instead of destroying so undo
	/// can restore the hierarchy without fighting runtime-created children.
	/// </summary>
	internal static void ReleaseSegmentGameObject( GameObject go )
	{
		if ( !go.IsValid() )
			return;

		InvalidateBuiltSize( go );
		if ( IsEditorSession )
		{
			go.Enabled = false;
			return;
		}

		go.Destroy();
	}

	internal static void SafeDestroyGameObject( GameObject go ) => ReleaseSegmentGameObject( go );

	static readonly Dictionary<Guid, int> RibbonHashes = new();

	static int HashRibbon( IReadOnlyList<Vector3> points, float width, float surfaceOffset )
	{
		unchecked
		{
			var hash = points.Count;
			hash = hash * 31 + width.GetHashCode();
			hash = hash * 31 + surfaceOffset.GetHashCode();
			for ( var i = 0; i < points.Count; i++ )
				hash = hash * 31 + points[i].GetHashCode();
			return hash;
		}
	}

	static Model BuildRibbonModel( IReadOnlyList<Vector3> localPoints, float width, float surfaceOffset, Material material )
	{
		material ??= Material.Load( "materials/dev/reflectivity_50b.vmat" );
		var half = MathF.Max( 4f, width * 0.5f );
		var count = localPoints.Count;
		var centers = new Vector3[count];
		var rights = new Vector3[count];

		for ( var i = 0; i < count; i++ )
			centers[i] = localPoints[i] + Vector3.Up * surfaceOffset;

		for ( var i = 0; i < count; i++ )
		{
			Vector3 dir;
			if ( i == 0 )
				dir = centers[1] - centers[0];
			else if ( i == count - 1 )
				dir = centers[count - 1] - centers[count - 2];
			else
				dir = (centers[i] - centers[i - 1]) + (centers[i + 1] - centers[i]);

			if ( dir.LengthSquared < 0.0001f )
				dir = Vector3.Forward;

			dir = dir.Normal;
			var right = Vector3.Cross( dir, Vector3.Up );
			if ( right.LengthSquared < 0.0001f )
				right = Vector3.Cross( dir, Vector3.Forward );
			rights[i] = right.Normal * half;
		}

		// Keep side orientation consistent along the strip (prevents twisted seams).
		for ( var i = 1; i < count; i++ )
		{
			if ( Vector3.Dot( rights[i], rights[i - 1] ) < 0f )
				rights[i] = -rights[i];
		}

		var vb = new VertexBuffer();
		vb.Init( true );
		var mins = centers[0];
		var maxs = centers[0];

		for ( var i = 0; i < count - 1; i++ )
		{
			var aL = centers[i] - rights[i];
			var aR = centers[i] + rights[i];
			var bL = centers[i + 1] - rights[i + 1];
			var bR = centers[i + 1] + rights[i + 1];

			AddRibbonQuad( vb, aL, aR, bR, bL );

			mins = Vector3.Min( mins, Vector3.Min( Vector3.Min( aL, aR ), Vector3.Min( bL, bR ) ) );
			maxs = Vector3.Max( maxs, Vector3.Max( Vector3.Max( aL, aR ), Vector3.Max( bL, bR ) ) );
		}

		var bounds = new BBox( mins - Vector3.One * 8f, maxs + Vector3.One * 8f );
		var mesh = new Mesh( material );
		mesh.CreateBuffers( vb, true );
		mesh.Bounds = bounds;

		return new ModelBuilder()
			.AddMesh( mesh )
			.WithViewBounds( bounds )
			.WithHullBounds( bounds )
			.Create();
	}

	static void AddRibbonQuad( VertexBuffer vb, Vector3 aL, Vector3 aR, Vector3 bR, Vector3 bL )
	{
		var n = Vector3.Up;
		var t = Vector3.Forward;
		var v0 = new Vertex( aL, n, t, new Vector4( 0f, 0f, 0f, 0f ) );
		var v1 = new Vertex( aR, n, t, new Vector4( 1f, 0f, 0f, 0f ) );
		var v2 = new Vertex( bR, n, t, new Vector4( 1f, 1f, 0f, 0f ) );
		var v3 = new Vertex( bL, n, t, new Vector4( 0f, 1f, 0f, 0f ) );

		vb.AddTriangle( v0, v1, v2 );
		vb.AddTriangle( v0, v2, v3 );
	}

	internal static WaterZoneType ClassifyZone( float depthInWater, float lateralDistance, float halfWidth, WaterConfig config )
	{
		if ( lateralDistance > halfWidth + config.EdgeBandWidth )
			return WaterZoneType.None;

		if ( lateralDistance > halfWidth )
			return WaterZoneType.Edge;

		if ( depthInWater < 0f )
			return depthInWater >= -config.SurfaceBandHeight ? WaterZoneType.Surface : WaterZoneType.None;

		if ( depthInWater < config.SwimDepthThreshold )
			return WaterZoneType.Swim;

		return WaterZoneType.Underwater;
	}

	internal static Material LoadDefaultMaterial()
	{
		try
		{
			var water = Material.Load( DefaultMaterialPath );
			if ( water is not null )
				return water;
		}
		catch
		{
			// Fall through to glass.
		}

		try
		{
			return Material.Load( FallbackMaterialPath );
		}
		catch
		{
			return null;
		}
	}

	internal static Vector3 MaxSize( Vector3 size )
	{
		return new Vector3(
			MathF.Max( 8f, MathF.Abs( size.x ) ),
			MathF.Max( 8f, MathF.Abs( size.y ) ),
			MathF.Max( 8f, MathF.Abs( size.z ) ) );
	}

	static GameObject EnsureChild( GameObject host, string name )
	{
		var child = host.Children.FirstOrDefault( c => c.Name == name );
		if ( child is null )
		{
			child = new GameObject( host, true, name );
			MarkRuntimeGenerated( child );
		}

		child.Enabled = true;
		child.Name = name;
		MarkRuntimeGenerated( child );
		return child;
	}

	static void SetChildEnabled( GameObject host, string name, bool enabled )
	{
		var child = host.Children.FirstOrDefault( c => c.Name == name );
		if ( child is not null )
			child.Enabled = enabled;
	}

	static void ApplyRenderer( GameObject child, Vector3 size, Material materialOverride, Color tint, WaterConfig config )
	{
		var renderer = child.GetOrAddComponent<ModelRenderer>();
		renderer.Enabled = true;
		child.LocalScale = Vector3.One;

		var material = ResolveSurfaceMaterial( materialOverride, tint, config );
		var materialChanged = renderer.MaterialOverride != material;
		renderer.MaterialOverride = material;
		renderer.Tint = tint;

		var id = child.Id;
		var needsMesh = materialChanged
			|| renderer.Model is null
			|| !BuiltSizes.TryGetValue( id, out var built )
			|| (built - size).Length > 0.5f;

		if ( needsMesh )
		{
			renderer.Model = BuildBoxModel( size, material );
			BuiltSizes[id] = size;
		}
	}

	static Material ResolveSurfaceMaterial( Material materialOverride, Color tint, WaterConfig config )
	{
		if ( materialOverride is not null )
		{
			// Only mutate explicit overrides — never write into the shared default asset.
			ApplyMaterialParams( materialOverride, tint, config );
			return materialOverride;
		}

		// Shared default .vmat — tint comes from ModelRenderer.Tint (CreateCopy without
		// a .vmat extension caused FixupResourceName spam and missing segments).
		return LoadDefaultMaterial();
	}

	internal static void HideVolumeMeshes( GameObject host )
	{
		if ( host is null )
			return;

		SetChildEnabled( host, VolumeChildName, false );
		SetChildEnabled( host, SurfaceChildName, false );
	}

	internal static void InvalidateBuiltSize( GameObject host )
	{
		if ( !host.IsValid() )
			return;

		BuiltSizes.Remove( host.Id );
		RibbonHashes.Remove( host.Id );
		foreach ( var child in host.Children )
		{
			if ( !child.IsValid() )
				continue;
			BuiltSizes.Remove( child.Id );
			RibbonHashes.Remove( child.Id );
		}
	}

	static void ApplyMaterialParams( Material material, Color tint, WaterConfig config )
	{
		if ( material is null )
			return;

		material.Set( "g_flModelTintAmount", 1f );
		material.Set( "g_vColorTint", new Vector3( tint.r, tint.g, tint.b ) );
		material.Set( "WaveAmplitude", config.WaveAmplitude );
		material.Set( "WaveFrequency", config.WaveFrequency );
		material.Set( "Transparency", config.Transparency );
	}

	static Model BuildBoxModel( Vector3 size, Material material )
	{
		material ??= Material.Load( "materials/dev/reflectivity_50b.vmat" );

		var bounds = new BBox( size * -0.5f, size * 0.5f );
		var vb = new VertexBuffer();
		vb.Init( true );
		vb.AddCube( Vector3.Zero, size, Rotation.Identity );

		var mesh = new Mesh( material );
		mesh.CreateBuffers( vb, true );
		mesh.Bounds = bounds;

		return new ModelBuilder()
			.AddMesh( mesh )
			.WithViewBounds( bounds )
			.WithHullBounds( bounds )
			.Create();
	}
}
