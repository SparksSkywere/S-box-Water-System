using System;
using System.Linq;

namespace WaterSystem;

/// <summary>
/// Spline river with editable local path points, flow arrows, bank sides, and inflow/outflow links.
/// Control points stay local to this object so the channel moves with its parent.
/// </summary>
[Title( "River Path" )]
[Category( "Water" )]
[Icon( "water" )]
public class RiverPathComponent : Component, Component.ExecuteInEditor, IWaterSource
{
	[Property, Group( "River" )] public string RiverName { get; set; } = "River";

	[Property, Group( "Channel" ), Range( 10f, 2000f )] public float Width { get; set; } = 180f;
	[Property, Group( "Channel" ), Range( 10f, 1000f )] public float Depth { get; set; } = 80f;
	[Property, Group( "Channel" ), Range( 0.1f, 10f ), Title( "Channel Scale" )] public float ChannelScale { get; set; } = 1f;
	[Property, Group( "Channel" ), Range( 0f, 400f )] public float CurrentSpeed { get; set; } = 28f;
	[Property, Group( "Channel" )] public Vector3 FlowDirection { get; set; } = Vector3.Forward;
	/// <summary>When true, fluid flows opposite the path tangent without moving control points.</summary>
	[Property, Group( "Channel" ), Title( "Flow Reversed" )] public bool FlowReversed { get; set; }
	[Property, Group( "Channel" ), Range( 10f, 8000f )] public float FlowLength { get; set; } = 1200f;
	[Property, Group( "Channel" ), Range( 1f, 500f ), Title( "Edit Step" )] public float EditStep { get; set; } = 50f;

	/// <summary>Spline points in local space. Edit via the River Path scene overlay.</summary>
	[Property, Group( "Spline" )] public List<Vector3> ControlPoints { get; set; } = new();

	/// <summary>Speed scale at each control point (1 = CurrentSpeed). Lerped along the path.</summary>
	[Property, Group( "Spline" )] public List<float> PointSpeedScales { get; set; } = new();

	[Property, Group( "Spline" )] public bool UseSplinePath { get; set; } = true;
	[Property, Group( "Spline" ), Range( 2f, 24f ), Title( "Flow Sample Density" )] public int SplineSamplesPerSegment { get; set; } = 8;

	/// <summary>Active control point for gizmo edit (-1 = none). Editor-only; never saved to scene.</summary>
	int _selectedControlPointIndex = -1;
	public int SelectedControlPointIndex
	{
		get => _selectedControlPointIndex;
		set => _selectedControlPointIndex = value;
	}

	/// <summary>Active river being edited (set by hierarchy selection or point gizmo click).</summary>
	public static RiverPathComponent EditorActiveRiver { get; set; }

	bool IsActiveEditorRiver => this.IsValid() && EditorActiveRiver == this;

	bool IsControlPointSelected( int index ) =>
		IsActiveEditorRiver && HasSelectedControlPoint && index == SelectedControlPointIndex;

	public bool HasSelectedControlPoint =>
		ControlPoints is not null
		&& SelectedControlPointIndex >= 0
		&& SelectedControlPointIndex < ControlPoints.Count;

	[Property, Group( "Links" )] public bool AutoConnectOutflow { get; set; } = true;
	[Property, Group( "Links" )] public RiverPathComponent OutflowRiver { get; set; }
	[Property, Group( "Links" )] public List<RiverPathComponent> InflowRivers { get; set; } = new();
	[Property, Group( "Links" ), Range( 10f, 4000f )] public float OutflowConnectDistance { get; set; } = 800f;
	/// <summary>Distance along the outflow river path where this river joins (not always the start).</summary>
	[Property, Group( "Links" ), ReadOnly] public float OutflowAttachDistance { get; set; }
	[Property, Group( "Links" ), Range( 1, 4 ), Title( "Junction Snap Points" )] public int JunctionSnapPoints { get; set; } = 2;
	[Property, Group( "Links" ), Range( 32f, 800f ), Title( "Junction Blend Length" )] public float JunctionBlendLength { get; set; } = 180f;
	/// <summary>When true, Apply Outflow / Snap To River may reshape end points. Nearest never moves points.</summary>
	[Property, Group( "Links" )] public bool SnapJunctionOnConnect { get; set; } = false;

	[Property, Group( "Geometry" ), Range( 32f, 2048f )] public float GeometryTraceRange { get; set; } = 768f;
	[Property, Group( "Geometry" ), Range( 8f, 256f )] public float FloorSnapHeight { get; set; } = 96f;
	[Property, Group( "Geometry" ), Range( 0f, 128f )] public float FloorClearance { get; set; } = 8f;

	[Property, Group( "Fluid" ), Range( 1f, 14000f )] public float FluidDensity { get; set; } = 1000f;
	[Property, Group( "Fluid" ), Range( 0f, 20f )] public float LinearDrag { get; set; } = 2f;
	[Property, Group( "Fluid" ), Range( 0f, 20f )] public float AngularDrag { get; set; } = 1.2f;
	[Property, Group( "Fluid" ), Range( -500f, 500f )] public float SurfaceOffset { get; set; }
	[Property, Group( "Fluid" ), Range( 0f, 64f )] public float WaveAmplitude { get; set; } = 3f;
	[Property, Group( "Fluid" ), Range( 0.01f, 8f )] public float WaveFrequency { get; set; } = 1.1f;
	[Property, Group( "Fluid" )] public bool AllowSwimming { get; set; } = true;
	[Property, Group( "Fluid" )] public bool AffectRigidbodies { get; set; } = true;
	[Property, Group( "Fluid" ), Range( 0f, 200f )] public float EdgeBandWidth { get; set; } = 25f;
	[Property, Group( "Fluid" ), Range( 5f, 300f )] public float SwimDepthThreshold { get; set; } = 32f;
	[Property, Group( "Fluid" ), Range( 0f, 100f )] public float SurfaceBandHeight { get; set; } = 20f;

	[Property, Group( "Look" )] public Material WaterMaterial { get; set; }
	[Property, Group( "Look" )] public Color WaterColor { get; set; } = new Color( 0.18f, 0.46f, 0.72f, 0.72f );
	[Property, Group( "Look" ), Range( 0f, 1f )] public float Transparency { get; set; } = 0.72f;
	[Property, Group( "Look" )] public bool CreateSurfaceVisual { get; set; } = true;
	[Property, Group( "Look" )] public bool ShowPathGizmos { get; set; } = true;
	[Property, Group( "Look" )] public bool ShowFlowArrows { get; set; } = true;
	[Property, Group( "Look" )] public bool ShowBankGuides { get; set; } = true;
	[Property, Group( "Look" ), Range( 40f, 400f )] public float FlowArrowSpacing { get; set; } = 120f;
	[Property, Group( "Look" ), Range( 12f, 120f ), Title( "Point Gizmo Size" )] public float GizmoSize { get; set; } = 36f;
	[Property, Group( "Look" ), Range( 0f, 128f ), Title( "Brush Overlap" )] public float BrushOverlap { get; set; } = 48f;

	[Property, Group( "Audio" )] public SoundEvent AmbientSound { get; set; }
	[Property, Group( "Audio" ), Range( 0f, 2f )] public float AmbientVolume { get; set; } = 0.55f;
	[Property, Group( "Audio" ), Range( 100f, 6000f ), Title( "Ambient Hear Distance" )] public float AmbientHearDistance { get; set; } = 1800f;
	[Property, Group( "Audio" ), Range( 0f, 1500f ), Title( "Ambient Full Volume Distance" )] public float AmbientFullVolumeDistance { get; set; } = 220f;

	[Property, Group( "Setup" )] public bool AutoRegisterWithManager { get; set; } = true;
	[Property, Group( "Setup" ), Title( "Ensure Water Presence" )] public bool EnsureSwimOnRebuild { get; set; } = true;

	/// <summary>Cached path length from the last path rebuild. Not saved — updating as a Property dirtied the scene every gizmo frame.</summary>
	public float PathLength { get; private set; }

	public float ScaledWidth => MathF.Max( 4f, Width * MathF.Max( 0.05f, ChannelScale ) );
	public float ScaledDepth => MathF.Max( 4f, Depth * MathF.Max( 0.05f, ChannelScale ) );

	readonly List<Vector3> _sampledPath = new();
	readonly List<float> _sampledDistances = new();
	int _lastSync;
	bool _built;
	bool _pointsDirty;
	bool _draggingPoint;
	bool _needsFullBrushRebuild = true;
	int _pendingPointClick = -1;
	bool _pathBackgroundClicked;
	bool _teardown;
	bool _runtimeMeshesBuilt;
	SoundHandle _ambientLoop;

	public string DisplayName => string.IsNullOrWhiteSpace( RiverName ) ? GameObject?.Name ?? "River" : RiverName;

	protected override void OnEnabled()
	{
		_teardown = false;
		ClearControlPointSelection();
		_needsFullBrushRebuild = true;

		// Editor undo/restore must not touch native WaterVolume or rebuild brushes.
		if ( WaterRuntime.IsEditorSession )
		{
			_built = false;
			_runtimeMeshesBuilt = false;
			return;
		}

		SanitizeRiverLinks();
		Rebuild();
		if ( AutoRegisterWithManager )
			WaterSystemManager.Register( this );
	}

	protected override void OnStart()
	{
		if ( WaterRuntime.IsEditorSession )
			return;

		SanitizeRiverLinks();
		MarkLegacySegmentsNotSaved();

		if ( AutoRegisterWithManager )
			WaterSystemManager.Register( this );

		if ( !_runtimeMeshesBuilt )
			Rebuild();
	}

	protected override void OnDisabled()
	{
		_teardown = true;
		WaterAmbientAudio.Stop( ref _ambientLoop );
		ReleaseEditorSelection();
		if ( !WaterRuntime.IsEditorSession )
			WaterSystemManager.Unregister( this );
	}

	protected override void OnDestroy()
	{
		_teardown = true;
		CleanupRiverLinks();
		if ( GameObject.IsValid() )
			WaterRuntime.InvalidateBuiltSize( GameObject );
		WaterAmbientAudio.Stop( ref _ambientLoop );
		ReleaseEditorSelection();
		if ( !WaterRuntime.IsEditorSession )
			WaterSystemManager.Unregister( this );
	}

	void CleanupRiverLinks()
	{
		OutflowRiver = null;
		OutflowAttachDistance = 0f;
		InflowRivers?.Clear();
	}

	void SanitizeRiverLinks()
	{
		if ( !IsOutflowValid() )
		{
			OutflowRiver = null;
			OutflowAttachDistance = 0f;
		}

		if ( InflowRivers is null || InflowRivers.Count == 0 )
			return;

		for ( var i = InflowRivers.Count - 1; i >= 0; i-- )
		{
			var upstream = InflowRivers[i];
			if ( !upstream.IsValid() || !upstream.GameObject.IsValid() )
				InflowRivers.RemoveAt( i );
		}
	}

	void MarkLegacySegmentsNotSaved()
	{
		if ( !GameObject.IsValid() )
			return;

		foreach ( var child in GameObject.Children )
		{
			if ( child.IsValid() && child.Name.StartsWith( WaterRuntime.SegmentPrefix ) )
				WaterRuntime.MarkRuntimeGenerated( child );
		}
	}

	bool ShouldDrawEditorGizmos() =>
		!WaterRuntime.IsEditorSession || IsActiveEditorRiver;

	void ReleaseEditorSelection()
	{
		if ( EditorActiveRiver == this )
			EditorActiveRiver = null;

		SelectedControlPointIndex = -1;
	}

	bool IsOutflowValid() =>
		OutflowRiver.IsValid() && OutflowRiver.GameObject.IsValid();

	bool CanRebuild() =>
		!_teardown && GameObject.IsValid() && Enabled && !WaterRuntime.IsDeserializing( GameObject );

	protected override void OnUpdate()
	{
		if ( WaterRuntime.IsEditorSession || !CanRebuild() )
			return;

		if ( !_built || _pointsDirty || _lastSync != GetSyncHash() || !_runtimeMeshesBuilt )
			Rebuild();

		UpdateAmbientAudio();
	}

	protected override void DrawGizmos()
	{
		if ( !ShowPathGizmos || !CanRebuild() || !ShouldDrawEditorGizmos() )
			return;

		MarkLegacySegmentsNotSaved();
		EnsureDefaultControlPoints();
		RebuildPath();

		_pendingPointClick = -1;
		_pathBackgroundClicked = false;

		// Draw in this object's local space so moving the parent never splits path from mesh.
		Gizmo.Transform = WorldTransform;
		DrawPathDeselectHitboxes();
		DrawEditablePoints();
		DrawPathAndBanksLocal();
		DrawFlowArrowsLocal();
		DrawConnectionLinksLocal();
		ApplyPointSelectionClick();
	}

	public void ClearControlPointSelection() => SelectedControlPointIndex = -1;

	public void ActivateForEditor()
	{
		if ( EditorActiveRiver.IsValid() && EditorActiveRiver != this )
			EditorActiveRiver.ClearControlPointSelection();

		EditorActiveRiver = this;
	}

	public void SelectFirstControlPoint()
	{
		EnsureDefaultControlPoints();
		if ( ControlPoints is null || ControlPoints.Count == 0 )
		{
			ClearControlPointSelection();
			return;
		}

		SelectedControlPointIndex = 0;
	}

	void UpdateAmbientAudio()
	{
		if ( !WaterAmbientAudio.TryGetListener( Scene, out var listener, out var underwater ) )
		{
			WaterAmbientAudio.Stop( ref _ambientLoop );
			return;
		}

		RebuildPath();
		if ( !TryFindClosestOnPath( listener, out var nearest, out _, out _, out _ ) )
		{
			WaterAmbientAudio.Stop( ref _ambientLoop );
			return;
		}

		var surface = nearest + Vector3.Up * SurfaceOffset;
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

	void ApplyPointSelectionClick()
	{
		if ( _pendingPointClick >= 0 )
		{
			ActivateForEditor();

			// Click same handle again to deselect.
			if ( _pendingPointClick == SelectedControlPointIndex )
				ClearControlPointSelection();
			else
				SelectedControlPointIndex = _pendingPointClick;
			return;
		}

		if ( _pathBackgroundClicked && IsActiveEditorRiver )
			ClearControlPointSelection();
	}

	void DrawPathDeselectHitboxes()
	{
		if ( !IsActiveEditorRiver || _sampledPath.Count < 2 )
			return;

		for ( var i = 0; i < _sampledPath.Count - 1; i++ )
		{
			var a = _sampledPath[i] + Vector3.Up * SurfaceOffset;
			var b = _sampledPath[i + 1] + Vector3.Up * SurfaceOffset;
			var delta = b - a;
			if ( delta.LengthSquared < 0.001f )
				continue;

			var mid = (a + b) * 0.5f;
			var hitRadius = MathF.Max( 14f, delta.Length * 0.5f + 8f );

			using ( Gizmo.Scope( $"river_path_hit_{i}", new Transform( mid ) ) )
			{
				// Sit behind point handles so point picks win when overlapping.
				Gizmo.Hitbox.DepthBias = 0.15f;
				Gizmo.Hitbox.Sphere( new Sphere( Vector3.Zero, hitRadius ) );

				if ( Gizmo.HasClicked && Gizmo.IsHovered )
					_pathBackgroundClicked = true;
			}
		}
	}

	public void Rebuild()
	{
		if ( !CanRebuild() )
			return;

		EnsureDefaultControlPoints();
		AnchorSourceToObjectOrigin();
		SyncPointSpeedScales();
		RebuildPath();

		if ( WaterRuntime.IsEditorSession )
		{
			_runtimeMeshesBuilt = false;
		}
		else
		{
			RebuildBrushes( transformsOnly: _draggingPoint && !_needsFullBrushRebuild );
			_runtimeMeshesBuilt = true;
		}

		_lastSync = GetSyncHash();
		_built = true;
		_pointsDirty = false;
	}

	/// <summary>Current speed at a world position (base speed × lerped point scales).</summary>
	public float GetSpeedAtPosition( Vector3 worldPosition )
	{
		RebuildPath();
		if ( !TryGetClosestPathInfo( ToLocal( worldPosition ), out _, out _, out _, out var distanceAlong ) )
			return CurrentSpeed;

		return CurrentSpeed * GetSpeedScaleAtDistance( DistanceAlongFlow( distanceAlong ) );
	}

	public Vector3 GetPositionAlongPath( float distance )
	{
		RebuildPath();
		if ( _sampledPath.Count < 2 )
			return WorldPosition;

		var target = MathX.Clamp( distance, 0f, PathLength );
		var walked = 0f;
		for ( var i = 0; i < _sampledPath.Count - 1; i++ )
		{
			var a = _sampledPath[i];
			var b = _sampledPath[i + 1];
			var seg = (a - b).Length;
			if ( walked + seg >= target )
			{
				var t = seg > 0.001f ? (target - walked) / seg : 0f;
				return ToWorld( Vector3.Lerp( a, b, t ) );
			}

			walked += seg;
		}

		return ToWorld( _sampledPath[_sampledPath.Count - 1] );
	}

	public void AddControlPoint( Vector3 worldPosition )
	{
		ControlPoints.Add( ToLocal( worldPosition ) );
		PointSpeedScales.Add( 1f );
		SelectedControlPointIndex = ControlPoints.Count - 1;
		Rebuild();
	}

	public void InsertControlPoint( int index, Vector3 worldPosition )
		=> InsertControlPointLocal( index, ToLocal( worldPosition ) );

	/// <summary>Insert a path point in local space (preferred — avoids stale transform round-trips on spawn).</summary>
	public void InsertControlPointLocal( int index, Vector3 localPosition )
	{
		if ( index < 0 || index > ControlPoints.Count )
			return;

		// Keep index 0 reserved for the object origin.
		if ( index == 0 )
			index = 1;

		ControlPoints.Insert( index, localPosition );
		var scale = 1f;
		if ( PointSpeedScales.Count > 0 )
		{
			var prev = PointSpeedScales[Math.Clamp( index - 1, 0, PointSpeedScales.Count - 1 )];
			var next = PointSpeedScales[Math.Clamp( index, 0, PointSpeedScales.Count - 1 )];
			scale = (prev + next) * 0.5f;
		}

		PointSpeedScales.Insert( Math.Clamp( index, 0, PointSpeedScales.Count ), scale );
		SelectedControlPointIndex = index;
		_needsFullBrushRebuild = true;
		AnchorSourceToObjectOrigin();
		Rebuild();
	}

	public void SetControlPointWorld( int index, Vector3 worldPosition )
	{
		if ( index <= 0 || index >= ControlPoints.Count )
			return;

		ControlPoints[index] = ToLocal( worldPosition );
		SelectedControlPointIndex = index;
		_pointsDirty = true;
	}

	public void NudgeSelectedControlPoint( Vector3 localDelta )
	{
		if ( !HasSelectedControlPoint )
			return;

		EnsureDefaultControlPoints();
		var selected = SelectedControlPointIndex;
		// Point 0 is the object origin — nudge the GameObject instead of the path.
		if ( selected == 0 )
		{
			WorldPosition += WorldRotation * localDelta;
			return;
		}

		ControlPoints[selected] += localDelta;
		Rebuild();
	}

	public void RemoveControlPointAt( int index )
	{
		// Never remove the source point — it is the object origin.
		if ( index <= 0 || index >= ControlPoints.Count )
			return;

		ControlPoints.RemoveAt( index );
		if ( index < PointSpeedScales.Count )
			PointSpeedScales.RemoveAt( index );

		EnsureDefaultControlPoints();
		if ( HasSelectedControlPoint && SelectedControlPointIndex >= ControlPoints.Count )
			ClearControlPointSelection();
		else if ( HasSelectedControlPoint )
			SelectedControlPointIndex = Math.Clamp( SelectedControlPointIndex, 0, ControlPoints.Count - 1 );

		Rebuild();
	}

	public void RemoveSelectedControlPoint()
	{
		// Always keep the source point.
		if ( ControlPoints.Count <= 1 || !HasSelectedControlPoint )
			return;

		var selected = SelectedControlPointIndex;
		if ( selected == 0 )
		{
			SelectedControlPointIndex = 1;
			selected = 1;
		}

		RemoveControlPointAt( selected );
	}

	public void AddControlPointAfterSelected( float spacing = 200f )
	{
		EnsureDefaultControlPoints();
		var selected = HasSelectedControlPoint
			? SelectedControlPointIndex
			: Math.Max( 0, ControlPoints.Count - 1 );
		var basePoint = ControlPoints[selected];

		// First path point: stretch out along flow so the channel appears.
		if ( ControlPoints.Count == 1 )
			spacing = MathF.Max( spacing, FlowLength );

		var direction = selected < ControlPoints.Count - 1
			? (ControlPoints[selected + 1] - basePoint).Normal
			: ControlPoints.Count > 1
				? (basePoint - ControlPoints[selected - 1]).Normal
				: LocalFlowDirection();
		InsertControlPointLocal( selected + 1, basePoint + direction * spacing );
	}

	public void AddControlPointBeforeSelected( float spacing = 200f )
	{
		EnsureDefaultControlPoints();
		var selected = HasSelectedControlPoint
			? SelectedControlPointIndex
			: Math.Max( 0, ControlPoints.Count - 1 );
		// Inserting before the source would fight the object pivot — add after source instead.
		if ( selected <= 0 )
		{
			AddControlPointAfterSelected( spacing );
			return;
		}

		var basePoint = ControlPoints[selected];
		var direction = selected > 0
			? (basePoint - ControlPoints[selected - 1]).Normal
			: ControlPoints.Count > 1
				? (ControlPoints[1] - basePoint).Normal
				: LocalFlowDirection();
		InsertControlPointLocal( selected, basePoint - direction * spacing );
	}

	public void ResetControlPointsToStraightLine()
	{
		EnsureDefaultControlPoints( resetToStraightLine: true );
		ClearControlPointSelection();
		Rebuild();
	}

	public void SelectNextControlPoint()
	{
		if ( ControlPoints.Count == 0 )
			return;

		if ( !HasSelectedControlPoint )
		{
			SelectedControlPointIndex = 0;
			return;
		}

		SelectedControlPointIndex = (SelectedControlPointIndex + 1) % ControlPoints.Count;
	}

	public void SelectPreviousControlPoint()
	{
		if ( ControlPoints.Count == 0 )
			return;

		if ( !HasSelectedControlPoint )
		{
			SelectedControlPointIndex = ControlPoints.Count - 1;
			return;
		}

		SelectedControlPointIndex = (SelectedControlPointIndex - 1 + ControlPoints.Count) % ControlPoints.Count;
	}

	public void SetSelectedPointSpeedScale( float scale )
	{
		if ( !HasSelectedControlPoint )
			return;

		SyncPointSpeedScales();
		PointSpeedScales[SelectedControlPointIndex] = MathF.Max( 0f, scale );
		Rebuild();
	}

	/// <summary>Wire this river into another. Joins at the nearest point on the target path (not always the start).</summary>
	public void ConnectToRiver( RiverPathComponent downstream )
	{
		ConnectToRiver( downstream, snapJunction: false );
	}

	public void ConnectToRiver( RiverPathComponent downstream, bool snapJunction )
	{
		if ( downstream is null || downstream == this )
		{
			if ( OutflowRiver is not null )
				ClearOutflow();
			return;
		}

		// Auto-connect runs every frame — skip when the link is already correct.
		if ( !snapJunction && OutflowRiver == downstream )
			return;

		if ( OutflowRiver.IsValid() && OutflowRiver != downstream )
			OutflowRiver.UnregisterInflow( this );

		OutflowRiver = downstream;
		downstream.RegisterInflow( this );

		var from = GetSelectedPointWorld();
		var exit = GetExitPoint();
		// Prefer selected if it's closer to the target channel than our exit.
		var selectedDist = downstream.GetDistanceToChannel( from, out _ );
		var exitDist = downstream.GetDistanceToChannel( exit, out _ );
		var anchor = selectedDist <= exitDist ? from : exit;

		if ( !downstream.TryFindClosestOnPath( anchor, out var attachWorld, out var attachDir, out var along, out _ ) )
		{
			attachWorld = downstream.GetEntryPoint();
			attachDir = downstream.GetEntryDirection();
			along = 0f;
		}

		OutflowAttachDistance = along;

		if ( snapJunction )
			SnapJunctionToOutflow( attachWorld, attachDir );

		if ( WaterRuntime.IsEditorSession )
			RebuildPath();
		else
			Rebuild();
	}

	public void ClearOutflow()
	{
		if ( !WaterRuntime.IsEditorSession && OutflowRiver.IsValid() )
			OutflowRiver.UnregisterInflow( this );

		OutflowRiver = null;
		OutflowAttachDistance = 0f;
	}

	public void RegisterInflow( RiverPathComponent upstream )
	{
		InflowRivers ??= new List<RiverPathComponent>();
		if ( upstream is null || upstream == this )
			return;

		if ( !InflowRivers.Contains( upstream ) )
			InflowRivers.Add( upstream );
	}

	public void UnregisterInflow( RiverPathComponent upstream )
	{
		InflowRivers?.Remove( upstream );
	}

	/// <summary>
	/// Pull the end of this path into the outflow channel so flow meets at the nearest
	/// point (1–N control points depending on Junction Snap Points).
	/// </summary>
	public void SnapJunctionToOutflow( Vector3 attachWorld, Vector3 targetFlowDir )
	{
		EnsureDefaultControlPoints();
		if ( ControlPoints.Count < 1 )
			return;

		if ( targetFlowDir.LengthSquared < 0.0001f )
			targetFlowDir = Vector3.Forward;
		else
			targetFlowDir = targetFlowDir.Normal;

		var snapCount = Math.Clamp( JunctionSnapPoints, 1, 4 );
		var blend = MathF.Max( 32f, JunctionBlendLength );

		// Approach direction: from previous point toward the join, else opposite of target flow.
		Vector3 approach;
		if ( ControlPoints.Count >= 2 )
		{
			var prevWorld = ToWorld( ControlPoints[Math.Max( 0, ControlPoints.Count - 2 )] );
			approach = attachWorld - prevWorld;
			if ( approach.LengthSquared < 1f )
				approach = -targetFlowDir;
			else
				approach = approach.Normal;
		}
		else
		{
			approach = -targetFlowDir;
		}

		// Ensure we have enough tail points to shape the junction.
		while ( ControlPoints.Count < snapCount + 1 )
		{
			var tip = ControlPoints.Count > 0 ? ControlPoints[ControlPoints.Count - 1] : Vector3.Zero;
			ControlPoints.Add( tip + LocalFlowDirection() * (blend / snapCount) );
			PointSpeedScales.Add( 1f );
		}

		var last = ControlPoints.Count - 1;
		for ( var s = 0; s < snapCount; s++ )
		{
			var idx = last - (snapCount - 1 - s);
			if ( idx <= 0 )
				continue;

			// s=0 farthest upstream of join, s=snapCount-1 sits on the attach point.
			var t = snapCount == 1 ? 1f : s / (float)(snapCount - 1);
			var back = (1f - t) * blend;
			// Blend our approach into the target flow so water turns into the main channel.
			var dir = Vector3.Lerp( approach, targetFlowDir, t * 0.85f ).Normal;
			var world = attachWorld - dir * back;
			ControlPoints[idx] = ToLocal( world );
		}

		ControlPoints[0] = Vector3.Zero;
		AnchorSourceToObjectOrigin();
		SelectedControlPointIndex = ControlPoints.Count - 1;
		_needsFullBrushRebuild = true;
	}

	/// <summary>Snap the selected control point onto the nearest other river channel.</summary>
	public void SnapSelectedToNearestRiver()
	{
		if ( Scene is null || !HasSelectedControlPoint )
			return;

		EnsureDefaultControlPoints();
		var selected = SelectedControlPointIndex;
		var from = ToWorld( ControlPoints[selected] );

		RiverPathComponent best = null;
		var bestDist = float.MaxValue;
		Vector3 attachWorld = from;
		Vector3 attachDir = Vector3.Forward;
		var along = 0f;

		foreach ( var other in Scene.GetAllComponents<RiverPathComponent>() )
		{
			if ( other is null || other == this || !other.Enabled )
				continue;

			if ( !other.TryFindClosestOnPath( from, out var nearest, out var dir, out var distAlong, out var dist ) )
				continue;

			if ( dist >= bestDist )
				continue;

			bestDist = dist;
			best = other;
			attachWorld = nearest;
			attachDir = dir;
			along = distAlong;
		}

		if ( best is null || bestDist > MathF.Max( OutflowConnectDistance, ScaledWidth * 2f ) )
		{
			GameLog.Warning( $"River '{DisplayName}': no nearby river channel to snap point {selected} onto." );
			return;
		}

		if ( selected == 0 )
		{
			WorldPosition = attachWorld;
			ControlPoints[0] = Vector3.Zero;
		}
		else
		{
			ControlPoints[selected] = ToLocal( attachWorld );
			ControlPoints[0] = Vector3.Zero;
		}

		// If snapping the exit (or near-exit), also wire the outflow at that attach.
		if ( selected >= ControlPoints.Count - 2 )
		{
			ConnectToRiver( best, snapJunction: false );
			OutflowAttachDistance = along;
			if ( SnapJunctionOnConnect )
				SnapJunctionToOutflow( attachWorld, attachDir );
		}

		_needsFullBrushRebuild = true;
		Rebuild();
		GameLog.Info( $"River '{DisplayName}': snapped pt {selected} onto '{best.DisplayName}' (along {along:0})." );
	}

	public Vector3 GetEntryPoint()
	{
		EnsureDefaultControlPoints();
		return ToWorld( ControlPoints[0] );
	}

	public Vector3 GetExitPoint()
	{
		EnsureDefaultControlPoints();
		return ToWorld( ControlPoints[ControlPoints.Count - 1] );
	}

	public Vector3 GetSelectedPointWorld()
	{
		EnsureDefaultControlPoints();
		if ( !HasSelectedControlPoint )
			return GetExitPoint();

		return ToWorld( ControlPoints[SelectedControlPointIndex] );
	}

	/// <summary>
	/// Shortest distance from a world position to this river's channel centerline.
	/// </summary>
	public float GetDistanceToChannel( Vector3 worldPosition, out float lateralDistance )
	{
		if ( !TryFindClosestOnPath( worldPosition, out var nearest, out _, out _, out var dist ) )
		{
			lateralDistance = float.MaxValue;
			return float.MaxValue;
		}

		lateralDistance = HorizontalLength( worldPosition - nearest );
		return dist;
	}

	/// <summary>Closest point on this river path (sampled centerline), with flow direction and distance along path.</summary>
	public bool TryFindClosestOnPath(
		Vector3 worldPosition,
		out Vector3 nearestWorld,
		out Vector3 flowDirWorld,
		out float distanceAlong,
		out float distance )
	{
		RebuildPath();
		nearestWorld = WorldPosition;
		flowDirWorld = WorldRotation * LocalFlowDirection();
		distanceAlong = 0f;
		distance = float.MaxValue;

		if ( ControlPoints is null || ControlPoints.Count == 0 )
			return false;

		if ( ControlPoints.Count == 1 || _sampledPath.Count < 2 )
		{
			nearestWorld = ToWorld( ControlPoints[0] );
			distance = (worldPosition - nearestWorld).Length;
			distanceAlong = 0f;
			return true;
		}

		if ( !TryGetClosestPathInfo( ToLocal( worldPosition ), out _, out var nearestLocal, out var localDir, out distanceAlong ) )
			return false;

		nearestWorld = ToWorld( nearestLocal );
		flowDirWorld = (WorldRotation * localDir).Normal;
		distance = (worldPosition - nearestWorld).Length;
		return true;
	}

	public Vector3 GetOutflowAttachPoint()
	{
		if ( !IsOutflowValid() )
			return GetExitPoint();

		OutflowRiver.RebuildPath();
		return OutflowRiver.GetPositionAlongPath( OutflowAttachDistance );
	}

	public Vector3 GetOutflowAttachDirection()
	{
		if ( !IsOutflowValid() )
			return GetExitDirection();

		// Use raw path tangent at the attach — avoid recursive outflow blending.
		if ( OutflowRiver.TryFindClosestOnPath( GetOutflowAttachPoint(), out _, out var dir, out _, out _ ) )
			return dir;

		return OutflowRiver.GetEntryDirection();
	}

	public Vector3 GetEntryDirection()
	{
		RebuildPath();
		if ( _sampledPath.Count < 2 )
			return WorldRotation * LocalFlowDirection();

		return (ToWorld( _sampledPath[1] ) - ToWorld( _sampledPath[0] )).Normal;
	}

	public Vector3 GetExitDirection()
	{
		RebuildPath();
		if ( _sampledPath.Count < 2 )
			return WorldRotation * LocalFlowDirection();

		return (ToWorld( _sampledPath[_sampledPath.Count - 1] ) - ToWorld( _sampledPath[_sampledPath.Count - 2] )).Normal;
	}

	public Vector3 GetFlowDirectionAtPosition( Vector3 worldPosition )
	{
		RebuildPath();
		if ( _sampledPath.Count < 2 )
			return WorldRotation * SignedLocalFlowDirection( LocalFlowDirection() );

		if ( !TryGetClosestPathInfo( ToLocal( worldPosition ), out var segmentIndex, out _, out var localDir, out _ ) )
			return WorldRotation * SignedLocalFlowDirection( LocalFlowDirection() );

		var direction = (WorldRotation * localDir).Normal;
		var tailBlendStart = _sampledPath.Count * 0.75f;
		if ( IsOutflowValid() && segmentIndex >= tailBlendStart )
		{
			var blend = (segmentIndex - tailBlendStart) / MathF.Max( 1f, _sampledPath.Count - tailBlendStart );
			direction = Vector3.Lerp( direction, GetOutflowAttachDirection(), MathX.Clamp( blend, 0f, 1f ) ).Normal;
		}

		return direction;
	}

	/// <summary>
	/// Bank side relative to flow: -1 left bank, 0 center channel, +1 right bank.
	/// </summary>
	public float GetBankSideAtPosition( Vector3 worldPosition )
	{
		RebuildPath();
		if ( !TryGetClosestPathInfo( ToLocal( worldPosition ), out _, out var nearestLocal, out var localDir, out _ ) )
			return 0f;

		var offset = ToLocal( worldPosition ) - nearestLocal;
		offset.z = 0f;
		var right = Vector3.Cross( localDir, Vector3.Up );
		if ( right.LengthSquared < 0.0001f )
			return 0f;

		right = right.Normal;
		var lateral = Vector3.Dot( offset, right );
		var halfWidth = ScaledWidth * 0.5f;
		if ( MathF.Abs( lateral ) < halfWidth * 0.25f )
			return 0f;

		return MathF.Sign( lateral );
	}

	public WaterZoneType GetZoneAtPoint( Vector3 worldPosition, out float lateralDistance, out Vector3 nearestPoint, out float depthInWater )
	{
		RebuildPath();
		lateralDistance = float.MaxValue;
		nearestPoint = WorldPosition;
		depthInWater = 0f;

		if ( _sampledPath.Count < 2 )
			return WaterZoneType.None;

		if ( !TryGetClosestPathInfo( ToLocal( worldPosition ), out _, out var nearestLocal, out _, out _ ) )
			return WaterZoneType.None;

		lateralDistance = HorizontalLength( ToLocal( worldPosition ) - nearestLocal );
		nearestPoint = ToWorld( nearestLocal );
		var halfWidth = ScaledWidth * 0.5f;
		var waterSurfaceZ = nearestPoint.z + SurfaceOffset;
		depthInWater = waterSurfaceZ - worldPosition.z;
		return WaterRuntime.ClassifyZone( depthInWater, lateralDistance, halfWidth, CreateRuntimeConfig() );
	}

	public WaterZoneType GetZoneAtPoint( Vector3 worldPosition ) => GetZoneAtPoint( worldPosition, out _, out _, out _ );

	public bool TrySample( Vector3 worldPosition, out WaterSample sample )
	{
		sample = WaterSample.Miss;
		var zone = GetZoneAtPoint( worldPosition, out var lateral, out var nearest, out var depth );
		if ( zone == WaterZoneType.None )
			return false;

		var halfWidth = MathF.Max( 1f, Width * 0.5f );
		var flowDir = GetFlowDirectionAtPosition( worldPosition );
		var speed = GetSpeedAtPosition( worldPosition );
		var bankSide = GetBankSideAtPosition( worldPosition );

		sample = new WaterSample
		{
			Hit = true,
			Source = this,
			Zone = zone,
			SurfaceHeight = nearest.z + SurfaceOffset,
			DepthInWater = depth,
			Submersion = MathX.Clamp( depth / MathF.Max( 1f, Depth ), 0f, 1f ),
			Flow = flowDir * speed,
			FluidDensity = FluidDensity,
			FlowSpeed = speed,
			LateralNormalized = MathX.Clamp( lateral / halfWidth, 0f, 2f ),
			BankSide = bankSide
		};
		return true;
	}

	/// <summary>
	/// One swim brush per control-point edge (not per spline sample).
	/// Brushes overlap slightly so the channel has no gaps.
	/// </summary>
	GameObject AcquirePooledSegment( string name )
	{
		foreach ( var child in GameObject.Children )
		{
			if ( !child.IsValid() || !child.Name.StartsWith( WaterRuntime.SegmentPrefix ) )
				continue;

			if ( child.Enabled )
				continue;

			child.Name = name;
			WaterRuntime.MarkRuntimeGenerated( child );
			return child;
		}

		var segment = new GameObject( GameObject, true, name );
		WaterRuntime.MarkRuntimeGenerated( segment );
		return segment;
	}

	void RebuildBrushes( bool transformsOnly = false )
	{
		if ( !CanRebuild() )
			return;

		RebuildPath();

		// One brush per control edge — every edge must get a live swim trigger.
		var needed = Math.Max( 0, ControlPoints.Count - 1 );
		var children = GameObject.Children
			.Where( c => c is not null && c.Name.StartsWith( WaterRuntime.SegmentPrefix ) )
			.OrderBy( c => c.Name )
			.ToList();

		foreach ( var segment in children )
			WaterRuntime.MarkRuntimeGenerated( segment );

		while ( children.Count > needed )
		{
			var extra = children[children.Count - 1];
			children.RemoveAt( children.Count - 1 );
			WaterRuntime.ReleaseSegmentGameObject( extra );
			_needsFullBrushRebuild = true;
		}

		var config = CreateRuntimeConfig();
		// Overlap seals swim-trigger gaps between brushes.
		var overlap = MathF.Max( 32f, BrushOverlap );
		var walked = 0f;

		// Remove old splash relays that can fire on load.
		WaterRuntime.DestroySplashRelays( GameObject );

		if ( !transformsOnly || _needsFullBrushRebuild )
		{
			WaterRuntime.InvalidateBuiltSize( GameObject );
			foreach ( var existing in children )
				WaterRuntime.InvalidateBuiltSize( existing );
		}

		for ( var i = 0; i < needed; i++ )
		{
			var a = ControlPoints[i];
			var b = ControlPoints[i + 1];
			var mid = (a + b) * 0.5f;
			var delta = b - a;
			var span = delta.Length;
			var length = MathF.Max( 8f, span + overlap );
			var localDir = delta.LengthSquared > 0.0001f ? delta.Normal : LocalFlowDirection();
			var worldDir = (WorldRotation * SignedLocalFlowDirection( localDir )).Normal;
			var speed = CurrentSpeed * GetSpeedScaleAtDistance( DistanceAlongFlow( walked + span * 0.5f ) );
			walked += span;

			GameObject child;
			if ( i < children.Count && children[i].IsValid() )
				child = children[i];
			else
			{
				child = AcquirePooledSegment( $"{WaterRuntime.SegmentPrefix}{i:00}" );
				_needsFullBrushRebuild = true;
			}

			child.Enabled = true;

			child.Name = $"{WaterRuntime.SegmentPrefix}{i:00}";
			child.LocalPosition = mid + Vector3.Up * SurfaceOffset;
			child.LocalRotation = Rotation.LookAt( localDir, Vector3.Up );
			child.LocalScale = Vector3.One;

			var w = ScaledWidth;
			var d = ScaledDepth;
			var segmentSize = new Vector3( length, w, d );
			var segmentCenter = new Vector3( 0f, 0f, -d * 0.5f );

			// Always keep swim trigger + tag alive (even during drag transforms).
			WaterRuntime.ApplySwimTag( child, AllowSwimming );
			WaterRuntime.ConfigureBoxTrigger( child, segmentSize, segmentCenter );
			// Kill legacy per-segment meshes (holes/seams came from leftover segment sheets).
			WaterRuntime.DestroySegmentVisuals( child );
			WaterRuntime.DestroySplashRelays( child );

			if ( transformsOnly && !_needsFullBrushRebuild )
			{
				WaterRuntime.UpdateFluidVelocity( child, worldDir * speed );
				continue;
			}

			WaterRuntime.ConfigureEngineWater( child, config, worldDir * speed, AffectRigidbodies );
		}

		// Host is not a swim volume — only segment brushes are.
		WaterRuntime.ApplySwimTag( GameObject, false );
		RebuildPath();
		// Dense sampled path + mitered ribbon = continuous sheet from above.
		WaterRuntime.ConfigureRiverRibbon(
			GameObject,
			_sampledPath.Count >= 2 ? _sampledPath : ControlPoints,
			ScaledWidth,
			SurfaceOffset,
			config,
			WaterMaterial,
			CreateSurfaceVisual );
		_needsFullBrushRebuild = false;

		if ( EnsureSwimOnRebuild && Scene is not null && !WaterRuntime.IsEditorSession )
			WaterSwimBridge.EnsurePlayersReady( Scene );
	}

	WaterConfig CreateRuntimeConfig()
	{
		var config = new WaterConfig
		{
			Width = ScaledWidth,
			Length = FlowLength,
			Depth = ScaledDepth,
			CurrentSpeed = CurrentSpeed,
			CurrentDirection = FlowDirection,
			FluidDensity = FluidDensity,
			LinearDrag = LinearDrag,
			AngularDrag = AngularDrag,
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

	void EnsureDefaultControlPoints( bool resetToStraightLine = false )
	{
		ControlPoints ??= new List<Vector3>();
		PointSpeedScales ??= new List<float>();

		if ( resetToStraightLine )
		{
			var direction = LocalFlowDirection();
			ControlPoints.Clear();
			ControlPoints.Add( Vector3.Zero );
			ControlPoints.Add( direction * FlowLength );
			PointSpeedScales.Clear();
			PointSpeedScales.Add( 1f );
			PointSpeedScales.Add( 1f );
			return;
		}

		// Source-only rivers are valid: point 0 moves the object until you add path points.
		if ( ControlPoints.Count == 0 )
		{
			ControlPoints.Add( Vector3.Zero );
			PointSpeedScales.Add( 1f );
		}

		SyncPointSpeedScales();
	}

	/// <summary>Spawn / place mode: only the source point. Move the object, then add points to build the channel.</summary>
	public void InitSourceOnly()
	{
		ControlPoints = new List<Vector3> { Vector3.Zero };
		PointSpeedScales = new List<float> { 1f };
		SelectedControlPointIndex = 0;
		_needsFullBrushRebuild = true;
		_draggingPoint = false;
		AnchorSourceToObjectOrigin();
		RebuildPath();
		_pointsDirty = true;
		_built = false;
	}

	void SyncPointSpeedScales()
	{
		PointSpeedScales ??= new List<float>();
		while ( PointSpeedScales.Count < ControlPoints.Count )
			PointSpeedScales.Add( 1f );

		while ( PointSpeedScales.Count > ControlPoints.Count )
			PointSpeedScales.RemoveAt( PointSpeedScales.Count - 1 );
	}

	void RebuildPath()
	{
		_sampledPath.Clear();
		_sampledDistances.Clear();
		EnsureDefaultControlPoints();
		if ( ControlPoints.Count < 2 )
			return;

		if ( !UseSplinePath || ControlPoints.Count < 3 )
		{
			_sampledPath.AddRange( ControlPoints );
		}
		else
		{
			for ( var i = 0; i < ControlPoints.Count - 1; i++ )
			{
				var p0 = i > 0 ? ControlPoints[i - 1] : ControlPoints[i];
				var p1 = ControlPoints[i];
				var p2 = ControlPoints[i + 1];
				var p3 = i + 2 < ControlPoints.Count ? ControlPoints[i + 2] : ControlPoints[i + 1];
				for ( var s = 0; s < SplineSamplesPerSegment; s++ )
				{
					var t = s / (float)SplineSamplesPerSegment;
					_sampledPath.Add( CatmullRom( p0, p1, p2, p3, t ) );
				}
			}

			_sampledPath.Add( ControlPoints[ControlPoints.Count - 1] );
		}

		PathLength = 0f;
		_sampledDistances.Add( 0f );
		for ( var i = 0; i < _sampledPath.Count - 1; i++ )
		{
			PathLength += (_sampledPath[i] - _sampledPath[i + 1]).Length;
			_sampledDistances.Add( PathLength );
		}

		if ( PathLength < 0.01f )
			PathLength = FlowLength;
	}

	float GetSpeedScaleAtDistance( float distanceAlong )
	{
		SyncPointSpeedScales();
		if ( ControlPoints.Count == 0 || PointSpeedScales.Count == 0 )
			return 1f;

		if ( ControlPoints.Count == 1 )
			return MathF.Max( 0f, PointSpeedScales[0] );

		// Approximate control-point distances along the polyline of control points.
		var distances = new float[ControlPoints.Count];
		distances[0] = 0f;
		var total = 0f;
		for ( var i = 0; i < ControlPoints.Count - 1; i++ )
		{
			total += (ControlPoints[i] - ControlPoints[i + 1]).Length;
			distances[i + 1] = total;
		}

		if ( total < 0.01f )
			return MathF.Max( 0f, PointSpeedScales[0] );

		var tDist = MathX.Clamp( distanceAlong, 0f, PathLength ) / PathLength * total;
		for ( var i = 0; i < distances.Length - 1; i++ )
		{
			if ( tDist > distances[i + 1] )
				continue;

			var span = distances[i + 1] - distances[i];
			var t = span > 0.001f ? (tDist - distances[i]) / span : 0f;
			return MathX.Lerp( PointSpeedScales[i], PointSpeedScales[i + 1], t );
		}

		return MathF.Max( 0f, PointSpeedScales[PointSpeedScales.Count - 1] );
	}

	bool TryGetClosestPathInfo( Vector3 localPos, out int segmentIndex, out Vector3 nearestLocal, out Vector3 localDir, out float distanceAlong )
	{
		segmentIndex = 0;
		nearestLocal = Vector3.Zero;
		localDir = LocalFlowDirection();
		distanceAlong = 0f;

		if ( _sampledPath.Count < 2 )
			return false;

		var bestDist = float.MaxValue;
		var walked = 0f;
		for ( var i = 0; i < _sampledPath.Count - 1; i++ )
		{
			var a = _sampledPath[i];
			var b = _sampledPath[i + 1];
			var closest = ClosestOnSegment( localPos, a, b );
			var dist = (localPos - closest).Length;
			var segLen = (a - b).Length;
			if ( dist < bestDist )
			{
				bestDist = dist;
				segmentIndex = i;
				nearestLocal = closest;
				localDir = SignedLocalFlowDirection( segLen > 0.001f ? (b - a).Normal : localDir );
				var alongSeg = segLen > 0.001f ? (closest - a).Length : 0f;
				distanceAlong = walked + alongSeg;
			}

			walked += segLen;
		}

		return true;
	}

	Vector3 LocalFlowDirection()
	{
		return FlowDirection.LengthSquared > 0.0001f ? FlowDirection.Normal : Vector3.Forward;
	}

	Vector3 SignedLocalFlowDirection( Vector3 geometryDir )
	{
		if ( geometryDir.LengthSquared < 0.0001f )
			geometryDir = LocalFlowDirection();
		return FlowReversed ? -geometryDir : geometryDir;
	}

	float DistanceAlongFlow( float geometryDistance )
	{
		if ( !FlowReversed || PathLength < 0.01f )
			return geometryDistance;
		return MathF.Max( 0f, PathLength - geometryDistance );
	}

	Vector3 ToLocal( Vector3 world ) => WorldTransform.PointToLocal( world );
	Vector3 ToWorld( Vector3 local ) => WorldTransform.PointToWorld( local );

	static float HorizontalLength( Vector3 v )
	{
		v.z = 0f;
		return v.Length;
	}

	int GetSyncHash()
	{
		unchecked
		{
			var hash = 17;
			hash = hash * 31 + Width.GetHashCode();
			hash = hash * 31 + Depth.GetHashCode();
			hash = hash * 31 + ChannelScale.GetHashCode();
			hash = hash * 31 + BrushOverlap.GetHashCode();
			hash = hash * 31 + CurrentSpeed.GetHashCode();
			hash = hash * 31 + FlowDirection.GetHashCode();
			hash = hash * 31 + (FlowReversed ? 1 : 0);
			hash = hash * 31 + FlowLength.GetHashCode();
			hash = hash * 31 + (ControlPoints?.Count ?? 0);
			if ( ControlPoints is not null )
			{
				for ( var i = 0; i < ControlPoints.Count; i++ )
					hash = hash * 31 + ControlPoints[i].GetHashCode();
			}

			hash = hash * 31 + (PointSpeedScales?.Count ?? 0);
			if ( PointSpeedScales is not null )
			{
				for ( var i = 0; i < PointSpeedScales.Count; i++ )
					hash = hash * 31 + PointSpeedScales[i].GetHashCode();
			}

			hash = hash * 31 + UseSplinePath.GetHashCode();
			hash = hash * 31 + SplineSamplesPerSegment;
			hash = hash * 31 + FluidDensity.GetHashCode();
			hash = hash * 31 + LinearDrag.GetHashCode();
			hash = hash * 31 + AngularDrag.GetHashCode();
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
			hash = hash * 31 + (IsOutflowValid() ? OutflowRiver.GameObject.Id.GetHashCode() : 0);
			return hash;
		}
	}

	/// <summary>
	/// Point 0 is always the object origin. Moving the GameObject moves the river source.
	/// </summary>
	void AnchorSourceToObjectOrigin()
	{
		if ( ControlPoints is null || ControlPoints.Count == 0 )
			return;

		var shift = ControlPoints[0];
		if ( shift.LengthSquared < 0.0001f )
		{
			ControlPoints[0] = Vector3.Zero;
			return;
		}

		// Absorb source drift into the object transform instead of zeroing blindly.
		WorldPosition += WorldRotation * shift;
		for ( var i = 0; i < ControlPoints.Count; i++ )
			ControlPoints[i] -= shift;

		ControlPoints[0] = Vector3.Zero;
	}

	void DrawEditablePoints()
	{
		// Large handles so you can pick any control point easily.
		var radius = MathF.Max( 20f, GizmoSize );
		var hitRadius = radius * 2.2f;
		var draggingThisFrame = false;

		for ( var i = 0; i < ControlPoints.Count; i++ )
		{
			var local = ControlPoints[i] + Vector3.Up * SurfaceOffset;

			using ( Gizmo.Scope( $"river_pt_{i}", new Transform( local ) ) )
			{
				Gizmo.Hitbox.DepthBias = -0.1f;
				Gizmo.Hitbox.Sphere( new Sphere( Vector3.Zero, hitRadius ) );

				var selected = IsControlPointSelected( i );
				if ( i == 0 )
					Gizmo.Draw.Color = selected ? Color.Yellow : Color.Green;
				else if ( i == ControlPoints.Count - 1 )
					Gizmo.Draw.Color = selected ? Color.Yellow : Color.Red;
				else
					Gizmo.Draw.Color = selected ? Color.Yellow : Color.Orange;

				if ( Gizmo.IsHovered )
					Gizmo.Draw.Color = Color.Cyan;

				Gizmo.Draw.SolidSphere( Vector3.Zero, selected ? radius * 1.25f : radius );

				// Click any visible handle to activate this river and select the point.
				if ( Gizmo.HasClicked && Gizmo.IsHovered )
					_pendingPointClick = i;

				if ( !selected )
					continue;

				if ( !Gizmo.Control.Position( "move", Vector3.Zero, out var delta ) )
					continue;

				if ( delta.LengthSquared < 0.0001f )
					continue;

				draggingThisFrame = true;
				_draggingPoint = true;

				if ( i == 0 )
				{
					WorldPosition += WorldRotation * delta;
					ControlPoints[0] = Vector3.Zero;
				}
				else
				{
					ControlPoints[i] += delta;
					ControlPoints[0] = Vector3.Zero;
					_pointsDirty = true;
				}
			}
		}

		// When the drag ends, rebuild meshes once so length/overlap stay sealed.
		if ( _draggingPoint && !draggingThisFrame )
		{
			_draggingPoint = false;
			_needsFullBrushRebuild = true;
			Rebuild();
		}
	}

	void DrawPathAndBanksLocal()
	{
		if ( _sampledPath.Count < 2 )
			return;

		var halfWidth = ScaledWidth * 0.5f;
		var depth = ScaledDepth;
		var surfaceUp = Vector3.Up * SurfaceOffset;
		var bottomDown = Vector3.Up * (SurfaceOffset - depth);
		Gizmo.Draw.LineThickness = 2f;

		for ( var i = 0; i < _sampledPath.Count - 1; i++ )
		{
			var a = _sampledPath[i] + surfaceUp;
			var b = _sampledPath[i + 1] + surfaceUp;
			var aBottom = _sampledPath[i] + bottomDown;
			var bBottom = _sampledPath[i + 1] + bottomDown;
			var dir = b - a;
			if ( dir.LengthSquared < 0.001f )
				continue;

			dir = dir.Normal;
			var right = Vector3.Cross( dir, Vector3.Up );
			if ( right.LengthSquared < 0.001f )
				continue;
			right = right.Normal;

			Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.9f );
			Gizmo.Draw.Line( a, b );

			if ( !ShowBankGuides )
				continue;

			var aLeft = a - right * halfWidth;
			var aRight = a + right * halfWidth;
			var bLeft = b - right * halfWidth;
			var bRight = b + right * halfWidth;
			var aLeftBottom = aBottom - right * halfWidth;
			var aRightBottom = aBottom + right * halfWidth;
			var bLeftBottom = bBottom - right * halfWidth;
			var bRightBottom = bBottom + right * halfWidth;

			// Surface banks
			Gizmo.Draw.Color = Color.Yellow.WithAlpha( 0.75f );
			Gizmo.Draw.Line( aLeft, bLeft );
			Gizmo.Draw.Line( aRight, bRight );

			// Bottom banks + vertical depth edges (outline only).
			Gizmo.Draw.Color = Color.Yellow.WithAlpha( 0.45f );
			Gizmo.Draw.Line( aLeftBottom, bLeftBottom );
			Gizmo.Draw.Line( aRightBottom, bRightBottom );
			Gizmo.Draw.Line( aLeft, aLeftBottom );
			Gizmo.Draw.Line( aRight, aRightBottom );
			if ( i == _sampledPath.Count - 2 )
			{
				Gizmo.Draw.Line( bLeft, bLeftBottom );
				Gizmo.Draw.Line( bRight, bRightBottom );
			}

			// End-cap cross sections so depth reads clearly at tips.
			if ( i == 0 )
			{
				Gizmo.Draw.Line( aLeft, aRight );
				Gizmo.Draw.Line( aLeftBottom, aRightBottom );
			}

			if ( i == _sampledPath.Count - 2 )
			{
				Gizmo.Draw.Line( bLeft, bRight );
				Gizmo.Draw.Line( bLeftBottom, bRightBottom );
			}

			if ( EdgeBandWidth > 0.5f )
			{
				Gizmo.Draw.Color = Color.White.WithAlpha( 0.35f );
				Gizmo.Draw.Line( a - right * (halfWidth + EdgeBandWidth), b - right * (halfWidth + EdgeBandWidth) );
				Gizmo.Draw.Line( a + right * (halfWidth + EdgeBandWidth), b + right * (halfWidth + EdgeBandWidth) );
			}
		}
	}

	void DrawFlowArrowsLocal()
	{
		if ( !ShowFlowArrows || _sampledPath.Count < 2 || PathLength < 1f )
			return;

		var spacing = MathF.Max( 40f, FlowArrowSpacing );
		var arrowLen = MathF.Max( 24f, GizmoSize * 2.5f );
		Gizmo.Draw.LineThickness = 2.5f;
		Gizmo.Draw.Color = new Color( 0.2f, 0.95f, 0.45f );

		for ( var d = spacing * 0.5f; d < PathLength; d += spacing )
		{
			if ( !TryGetLocalPointAlongPath( d, out var pos, out var dir ) )
				continue;

			pos += Vector3.Up * (SurfaceOffset + 4f);
			var tip = pos + dir * arrowLen;
			var right = Vector3.Cross( dir, Vector3.Up );
			if ( right.LengthSquared < 0.001f )
				right = Vector3.Cross( dir, Vector3.Right );
			right = right.Normal;

			Gizmo.Draw.Line( pos, tip );
			Gizmo.Draw.Line( tip, tip - dir * (arrowLen * 0.35f) + right * (arrowLen * 0.22f) );
			Gizmo.Draw.Line( tip, tip - dir * (arrowLen * 0.35f) - right * (arrowLen * 0.22f) );
		}

		if ( ControlPoints.Count > 0 )
		{
			Gizmo.Draw.Color = Color.Green;
			Gizmo.Draw.SolidSphere( ControlPoints[0] + Vector3.Up * (SurfaceOffset + 8f), MathF.Max( 6f, GizmoSize * 0.7f ) );
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.SolidSphere( ControlPoints[ControlPoints.Count - 1] + Vector3.Up * (SurfaceOffset + 8f), MathF.Max( 6f, GizmoSize * 0.7f ) );
		}
	}

	void DrawConnectionLinksLocal()
	{
		// Links to other rivers must be world-space (different objects).
		Gizmo.Transform = new Transform( Vector3.Zero, Rotation.Identity, Vector3.One );
		Gizmo.Draw.LineThickness = 2f;

		if ( IsOutflowValid() )
		{
			Gizmo.Draw.Color = new Color( 1f, 0.45f, 0.15f );
			var attach = GetOutflowAttachPoint() + Vector3.Up * OutflowRiver.SurfaceOffset;
			// Draw from whichever is closer to the join: selected point or exit tip.
			var fromSelected = GetSelectedPointWorld() + Vector3.Up * SurfaceOffset;
			var fromExit = GetExitPoint() + Vector3.Up * SurfaceOffset;
			var from = (fromSelected - attach).Length <= (fromExit - attach).Length ? fromSelected : fromExit;
			Gizmo.Draw.Line( from, attach );
			Gizmo.Draw.LineThickness = 3f;
			Gizmo.Draw.SolidSphere( attach, MathF.Max( 10f, GizmoSize * 0.5f ) );
			Gizmo.Draw.SolidSphere( from, MathF.Max( 8f, GizmoSize * 0.35f ) );
		}

		if ( InflowRivers is null )
			return;

		Gizmo.Draw.Color = new Color( 0.4f, 0.75f, 1f );
		for ( var i = 0; i < InflowRivers.Count; i++ )
		{
			var inflow = InflowRivers[i];
			if ( !inflow.IsValid() || inflow.GameObject is null )
				continue;

			Gizmo.Draw.Line(
				inflow.GetExitPoint() + Vector3.Up * inflow.SurfaceOffset,
				GetEntryPoint() + Vector3.Up * SurfaceOffset );
		}
	}

	bool TryGetLocalPointAlongPath( float distance, out Vector3 localPos, out Vector3 localDir )
	{
		localPos = Vector3.Zero;
		localDir = LocalFlowDirection();
		if ( _sampledPath.Count < 2 )
			return false;

		var target = MathX.Clamp( distance, 0f, PathLength );
		var walked = 0f;
		for ( var i = 0; i < _sampledPath.Count - 1; i++ )
		{
			var a = _sampledPath[i];
			var b = _sampledPath[i + 1];
			var seg = (a - b).Length;
			if ( walked + seg >= target )
			{
				var t = seg > 0.001f ? (target - walked) / seg : 0f;
				localPos = Vector3.Lerp( a, b, t );
				localDir = SignedLocalFlowDirection( seg > 0.001f ? (b - a).Normal : localDir );
				return true;
			}

			walked += seg;
		}

		localPos = _sampledPath[_sampledPath.Count - 1];
		if ( _sampledPath.Count >= 2 )
			localDir = SignedLocalFlowDirection( _sampledPath[_sampledPath.Count - 1] - _sampledPath[_sampledPath.Count - 2] );
		return true;
	}

	// —— Object inspector tools (also mirrored in the River Path scene overlay) ——

	public float EffectiveEditStep => MathF.Max( 1f, EditStep );

	public void ScaleChannel( float multiplier )
	{
		ChannelScale = MathX.Clamp( ChannelScale * multiplier, 0.1f, 10f );
		Rebuild();
	}

	public void AdjustWidth( float delta )
	{
		Width = MathX.Clamp( Width + delta, 10f, 2000f );
		Rebuild();
	}

	public void AdjustDepth( float delta )
	{
		Depth = MathX.Clamp( Depth + delta, 10f, 1000f );
		Rebuild();
	}

	public void AdjustLength( float delta )
	{
		FlowLength = MathX.Clamp( FlowLength + delta, 10f, 8000f );
		if ( ControlPoints.Count == 2 && ControlPoints[0].LengthSquared < 0.01f )
		{
			ControlPoints[1] = LocalFlowDirection() * FlowLength;
			Rebuild();
			return;
		}

		Rebuild();
	}

	/// <summary>Build a fresh straight path from this object's origin.</summary>
	public void CreateStraightPath() => ResetControlPointsToStraightLine();

	/// <summary>Strip channel mesh/segments but keep two end points.</summary>
	public void ClearPathMesh()
	{
		if ( GameObject is null )
			return;

		foreach ( var child in GameObject.Children.ToList() )
		{
			if ( child is not null && child.Name.StartsWith( WaterRuntime.SegmentPrefix ) )
				WaterRuntime.SafeDestroyGameObject( child );
		}

		_built = false;
		Rebuild();
	}

	/// <summary>Remove path down to source + one end point (straight default length).</summary>
	public void RemovePath()
	{
		ClearOutflow();
		InflowRivers?.Clear();
		EnsureDefaultControlPoints( resetToStraightLine: true );
		SelectedControlPointIndex = 0;
		Rebuild();
	}

	/// <summary>
	/// Connect outflow to the nearest other river at the closest path point (not forced to start).
	/// Link only — never moves control points or spawns junction points.
	/// </summary>
	public void ConnectToNearestRiver()
	{
		if ( Scene is null )
			return;

		EnsureDefaultControlPoints();
		RebuildPath();

		var selectedWorld = GetSelectedPointWorld();
		var exit = GetExitPoint();
		var halfWidth = ScaledWidth * 0.5f;

		RiverPathComponent best = null;
		var bestScore = float.MaxValue;
		var bestDist = float.MaxValue;
		var bestAlong = 0f;
		var maxDist = MathF.Max( OutflowConnectDistance, halfWidth * 4f + 128f );

		foreach ( var other in Scene.GetAllComponents<RiverPathComponent>() )
		{
			if ( other is null || other == this || !other.Enabled || other.GameObject is null )
				continue;

			other.RebuildPath();

			var otherHalf = other.ScaledWidth * 0.5f;
			var overlapRadius = halfWidth + otherHalf + MathF.Max( 32f, BrushOverlap );

			EvaluateAttachCandidate( selectedWorld, other, overlapRadius, ref best, ref bestScore, ref bestDist, ref bestAlong );
			EvaluateAttachCandidate( exit, other, overlapRadius, ref best, ref bestScore, ref bestDist, ref bestAlong );
		}

		if ( best is null || bestScore > maxDist )
		{
			GameLog.Warning(
				$"River '{DisplayName}': no nearby river to connect " +
				$"(from selected pt {SelectedControlPointIndex} / exit, max {maxDist:0}u)." );
			return;
		}

		// Link only — orange line in the viewport; do not reshape the path.
		if ( OutflowRiver.IsValid() && OutflowRiver != best )
			OutflowRiver.UnregisterInflow( this );

		OutflowRiver = best;
		best.RegisterInflow( this );
		OutflowAttachDistance = bestAlong;

		GameLog.Info( $"River '{DisplayName}' linked → '{best.DisplayName}' at path distance {bestAlong:0}u." );
	}

	void EvaluateAttachCandidate(
		Vector3 fromWorld,
		RiverPathComponent other,
		float overlapRadius,
		ref RiverPathComponent best,
		ref float bestScore,
		ref float bestDist,
		ref float bestAlong )
	{
		if ( !other.TryFindClosestOnPath( fromWorld, out _, out _, out var along, out var dist ) )
			return;

		other.GetDistanceToChannel( fromWorld, out var lateral );
		var score = lateral <= overlapRadius ? dist * 0.1f : dist;
		if ( score >= bestScore )
			return;

		bestScore = score;
		bestDist = dist;
		best = other;
		bestAlong = along;
	}

	/// <summary>Drop the selected point onto floor/wall mesh under it.</summary>
	public void SnapSelectedToGeometry()
	{
		if ( !HasSelectedControlPoint )
			return;

		EnsureDefaultControlPoints();
		var idx = SelectedControlPointIndex;
		if ( !TryProjectPointToGeometry( ToWorld( ControlPoints[idx] ), out var projected ) )
		{
			GameLog.Warning( $"River '{DisplayName}': no geometry under point {idx}." );
			return;
		}

		ApplyProjectedPoint( idx, projected );
		Rebuild();
	}

	/// <summary>Project every control point onto world geometry and center in corridors between walls.</summary>
	public void SnapAllPointsToGeometry()
	{
		EnsureDefaultControlPoints();
		var changed = 0;
		for ( var i = 0; i < ControlPoints.Count; i++ )
		{
			var world = ToWorld( ControlPoints[i] );
			if ( !TryProjectPointToGeometry( world, out var projected ) )
				continue;

			projected = CenterInCorridor( projected, i );
			ApplyProjectedPoint( i, projected );
			changed++;
		}

		if ( changed == 0 )
		{
			GameLog.Warning( $"River '{DisplayName}': geometry snap found no hits." );
			return;
		}

		_needsFullBrushRebuild = true;
		Rebuild();
		GameLog.Info( $"River '{DisplayName}': projected {changed} point(s) onto world mesh." );
	}

	void ApplyProjectedPoint( int index, Vector3 world )
	{
		if ( index <= 0 )
		{
			WorldPosition = world;
			ControlPoints[0] = Vector3.Zero;
			return;
		}

		ControlPoints[index] = ToLocal( world );
		ControlPoints[0] = Vector3.Zero;
		_needsFullBrushRebuild = true;
	}

	bool TryProjectPointToGeometry( Vector3 world, out Vector3 projected )
	{
		projected = world;
		if ( Scene is null )
			return false;

		var up = MathF.Max( 16f, FloorSnapHeight );
		var down = MathF.Max( up * 4f, GeometryTraceRange );
		var start = world + Vector3.Up * up;
		var end = world - Vector3.Up * down;

		var tr = Scene.Trace.Ray( start, end )
			.WithoutTags( "water", "trigger", "player" )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !tr.Hit )
			return false;

		projected = tr.HitPosition + tr.Normal * FloorClearance;
		// Prefer keeping water sitting on floors more than sticking to walls.
		if ( Vector3.Dot( tr.Normal, Vector3.Up ) < 0.35f )
		{
			var floorStart = tr.HitPosition + tr.Normal * 12f + Vector3.Up * up;
			var floorEnd = floorStart - Vector3.Up * down;
			var floor = Scene.Trace.Ray( floorStart, floorEnd )
				.WithoutTags( "water", "trigger", "player" )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();
			if ( floor.Hit && Vector3.Dot( floor.Normal, Vector3.Up ) > 0.35f )
				projected = floor.HitPosition + Vector3.Up * FloorClearance;
		}

		return true;
	}

	/// <summary>Push a point toward the middle of left/right wall hits so flow sits in the corridor.</summary>
	Vector3 CenterInCorridor( Vector3 world, int pointIndex )
	{
		if ( Scene is null )
			return world;

		var dir = LocalFlowDirection();
		if ( pointIndex > 0 && pointIndex < ControlPoints.Count )
		{
			var prev = ToWorld( ControlPoints[Math.Max( 0, pointIndex - 1 )] );
			var next = ToWorld( ControlPoints[Math.Min( ControlPoints.Count - 1, pointIndex + 1 )] );
			var along = next - prev;
			if ( along.LengthSquared > 1f )
				dir = (WorldRotation.Inverse * along.Normal);
		}

		var worldDir = (WorldRotation * dir).Normal;
		var right = Vector3.Cross( worldDir, Vector3.Up );
		if ( right.LengthSquared < 0.001f )
			right = Vector3.Cross( worldDir, Vector3.Right );
		right = right.Normal;

		var range = MathF.Max( 64f, GeometryTraceRange * 0.5f );
		var origin = world + Vector3.Up * 24f;
		var leftHit = Scene.Trace.Ray( origin, origin - right * range )
			.WithoutTags( "water", "trigger", "player" )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		var rightHit = Scene.Trace.Ray( origin, origin + right * range )
			.WithoutTags( "water", "trigger", "player" )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !leftHit.Hit || !rightHit.Hit )
			return world;

		var mid = (leftHit.HitPosition + rightHit.HitPosition) * 0.5f;
		mid.z = world.z;
		return mid;
	}

	public void ClearAllLinks()
	{
		ClearOutflow();
		if ( InflowRivers is null || InflowRivers.Count == 0 )
			return;

		foreach ( var inflow in InflowRivers.ToList() )
			inflow?.ClearOutflow();

		InflowRivers.Clear();
	}

	// Scene overlay helpers — wired in RiverPathToolWindow.
	public void EditorWider() => AdjustWidth( EffectiveEditStep );
	public void EditorNarrower() => AdjustWidth( -EffectiveEditStep );
	public void EditorDeeper() => AdjustDepth( EffectiveEditStep * 0.5f );
	public void EditorShallower() => AdjustDepth( -EffectiveEditStep * 0.5f );
	public void EditorSourceOnly() => InitSourceOnly();
	public void EditorCreatePath() => CreateStraightPath();

	public void EditorRebuildPath()
	{
		_needsFullBrushRebuild = true;
		_draggingPoint = false;
		Rebuild();
	}

	public void SwapFlowDirection()
	{
		FlowReversed = !FlowReversed;
		Rebuild();
	}

	public void EditorSwapFlow() => SwapFlowDirection();
	public void EditorAddPointBefore() => AddControlPointBeforeSelected();
	public void EditorAddPointAfter() => AddControlPointAfterSelected();
	public void EditorRemoveSelectedPoint() => RemoveSelectedControlPoint();
	public void EditorConnectNearest() => ConnectToNearestRiver();
	public void EditorClearOutflow() => ClearOutflow();

	static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	static Vector3 ClosestOnSegment( Vector3 point, Vector3 segStart, Vector3 segEnd )
	{
		var segVec = segEnd - segStart;
		var pointVec = point - segStart;
		var segLenSq = segVec.LengthSquared;
		if ( segLenSq <= 0f )
			return segStart;

		var t = MathX.Clamp( Vector3.Dot( pointVec, segVec ) / segLenSq, 0f, 1f );
		return segStart + segVec * t;
	}
}
