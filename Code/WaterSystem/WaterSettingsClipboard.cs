using System;

namespace WaterSystem;

/// <summary>
/// Editor clipboard for water look/fluid/channel settings.
/// Does not store path points, links, names, or transform/position.
/// </summary>
public static class WaterSettingsClipboard
{
	public static bool HasSettings { get; private set; }

	// Channel / shape
	public static float Width { get; private set; } = 180f;
	public static float Depth { get; private set; } = 80f;
	public static float ChannelScale { get; private set; } = 1f;
	public static float CurrentSpeed { get; private set; } = 28f;
	public static Vector3 FlowDirection { get; private set; } = Vector3.Forward;
	public static float FlowLength { get; private set; } = 1200f;
	public static float EditStep { get; private set; } = 50f;
	public static Vector3 BodySize { get; private set; } = new Vector3( 512f, 512f, 128f );
	public static WaterShapeType BodyShape { get; private set; } = WaterShapeType.Box;
	public static Vector3 BodyCurrent { get; private set; }

	// Spline options (not points)
	public static bool UseSplinePath { get; private set; } = true;
	public static int SplineSamplesPerSegment { get; private set; } = 4;
	public static bool AutoConnectOutflow { get; private set; } = true;
	public static float OutflowConnectDistance { get; private set; } = 300f;

	// Fluid
	public static float FluidDensity { get; private set; } = 1000f;
	public static float LinearDrag { get; private set; } = 2f;
	public static float AngularDrag { get; private set; } = 1.2f;
	public static float SurfaceOffset { get; private set; }
	public static float WaveAmplitude { get; private set; } = 3f;
	public static float WaveFrequency { get; private set; } = 1.1f;
	public static bool AllowSwimming { get; private set; } = true;
	public static bool AffectRigidbodies { get; private set; } = true;
	public static float EdgeBandWidth { get; private set; } = 25f;
	public static float SwimDepthThreshold { get; private set; } = 32f;
	public static float SurfaceBandHeight { get; private set; } = 20f;

	// Look
	public static string WaterMaterialPath { get; private set; }
	public static Color WaterColor { get; private set; } = new Color( 0.18f, 0.46f, 0.72f, 0.72f );
	public static float Transparency { get; private set; } = 0.72f;
	public static bool CreateSurfaceVisual { get; private set; } = true;
	public static bool ShowPathGizmos { get; private set; } = true;
	public static bool ShowFlowArrows { get; private set; } = true;
	public static bool ShowBankGuides { get; private set; } = true;
	public static bool ShowVolumeGizmo { get; private set; } = true;
	public static float FlowArrowSpacing { get; private set; } = 120f;
	public static float GizmoSize { get; private set; } = 36f;
	public static float BrushOverlap { get; private set; } = 24f;

	public static void CopyFrom( RiverPathComponent river )
	{
		if ( river is null )
			return;

		Width = river.Width;
		Depth = river.Depth;
		ChannelScale = river.ChannelScale;
		CurrentSpeed = river.CurrentSpeed;
		FlowDirection = river.FlowDirection;
		FlowLength = river.FlowLength;
		EditStep = river.EditStep;
		BodySize = new Vector3( river.Width, river.Width, river.Depth );
		BodyCurrent = river.FlowDirection.Normal * river.CurrentSpeed;

		UseSplinePath = river.UseSplinePath;
		SplineSamplesPerSegment = river.SplineSamplesPerSegment;
		AutoConnectOutflow = river.AutoConnectOutflow;
		OutflowConnectDistance = river.OutflowConnectDistance;

		CopyFluidLookFromRiver( river );
		HasSettings = true;
		GameLog.Info( $"Water: Copied settings from river '{river.DisplayName}' (path/position not included)." );
	}

	public static void CopyFrom( WaterBody body )
	{
		if ( body is null )
			return;

		BodySize = body.Size;
		BodyShape = body.Shape;
		BodyCurrent = body.Current;
		Width = MathF.Max( body.Size.x, body.Size.y );
		Depth = body.Size.z;
		ChannelScale = 1f;
		CurrentSpeed = body.Current.Length;
		FlowDirection = body.Current.LengthSquared > 0.0001f ? body.Current.Normal : Vector3.Forward;
		FlowLength = MathF.Max( body.Size.x, body.Size.y );

		FluidDensity = body.FluidDensity;
		LinearDrag = body.LinearDrag;
		AngularDrag = body.AngularDrag;
		SurfaceOffset = body.SurfaceOffset;
		WaveAmplitude = body.WaveAmplitude;
		WaveFrequency = body.WaveFrequency;
		AllowSwimming = body.AllowSwimming;
		AffectRigidbodies = body.AffectRigidbodies;
		EdgeBandWidth = body.EdgeBandWidth;
		SwimDepthThreshold = body.SwimDepthThreshold;
		SurfaceBandHeight = body.SurfaceBandHeight;

		WaterMaterialPath = body.WaterMaterial?.ResourcePath;
		WaterColor = body.WaterColor;
		Transparency = body.Transparency;
		CreateSurfaceVisual = body.CreateSurfaceVisual;
		ShowVolumeGizmo = body.ShowVolumeGizmo;

		HasSettings = true;
		GameLog.Info( $"Water: Copied settings from volume '{body.DisplayName}' (position not included)." );
	}

	public static void PasteTo( RiverPathComponent river )
	{
		if ( river is null || !HasSettings )
		{
			GameLog.Warning( "Water: Nothing to paste — copy settings from a water object first." );
			return;
		}

		river.Width = Width;
		river.Depth = Depth;
		river.ChannelScale = ChannelScale;
		river.CurrentSpeed = CurrentSpeed;
		river.FlowDirection = FlowDirection;
		river.FlowLength = FlowLength;
		river.EditStep = EditStep;

		river.UseSplinePath = UseSplinePath;
		river.SplineSamplesPerSegment = SplineSamplesPerSegment;
		river.AutoConnectOutflow = AutoConnectOutflow;
		river.OutflowConnectDistance = OutflowConnectDistance;

		river.FluidDensity = FluidDensity;
		river.LinearDrag = LinearDrag;
		river.AngularDrag = AngularDrag;
		river.SurfaceOffset = SurfaceOffset;
		river.WaveAmplitude = WaveAmplitude;
		river.WaveFrequency = WaveFrequency;
		river.AllowSwimming = AllowSwimming;
		river.AffectRigidbodies = AffectRigidbodies;
		river.EdgeBandWidth = EdgeBandWidth;
		river.SwimDepthThreshold = SwimDepthThreshold;
		river.SurfaceBandHeight = SurfaceBandHeight;

		river.WaterMaterial = LoadMaterial( WaterMaterialPath );
		river.WaterColor = WaterColor;
		river.Transparency = Transparency;
		river.CreateSurfaceVisual = CreateSurfaceVisual;
		river.ShowPathGizmos = ShowPathGizmos;
		river.ShowFlowArrows = ShowFlowArrows;
		river.ShowBankGuides = ShowBankGuides;
		river.FlowArrowSpacing = FlowArrowSpacing;
		river.GizmoSize = GizmoSize;
		river.BrushOverlap = BrushOverlap;

		river.Rebuild();
		GameLog.Info( $"Water: Pasted settings onto river '{river.DisplayName}'." );
	}

	public static void PasteTo( WaterBody body )
	{
		if ( body is null || !HasSettings )
		{
			GameLog.Warning( "Water: Nothing to paste — copy settings from a water object first." );
			return;
		}

		body.Size = BodySize.LengthSquared > 0.01f
			? BodySize
			: new Vector3( Width, Width, Depth );
		body.Shape = BodyShape;
		body.Current = BodyCurrent.LengthSquared > 0.0001f
			? BodyCurrent
			: FlowDirection.Normal * CurrentSpeed;

		body.FluidDensity = FluidDensity;
		body.LinearDrag = LinearDrag;
		body.AngularDrag = AngularDrag;
		body.SurfaceOffset = SurfaceOffset;
		body.WaveAmplitude = WaveAmplitude;
		body.WaveFrequency = WaveFrequency;
		body.AllowSwimming = AllowSwimming;
		body.AffectRigidbodies = AffectRigidbodies;
		body.EdgeBandWidth = EdgeBandWidth;
		body.SwimDepthThreshold = SwimDepthThreshold;
		body.SurfaceBandHeight = SurfaceBandHeight;

		body.WaterMaterial = LoadMaterial( WaterMaterialPath );
		body.WaterColor = WaterColor;
		body.Transparency = Transparency;
		body.CreateSurfaceVisual = CreateSurfaceVisual;
		body.ShowVolumeGizmo = ShowVolumeGizmo;

		body.Build();
		GameLog.Info( $"Water: Pasted settings onto volume '{body.DisplayName}'." );
	}

	static void CopyFluidLookFromRiver( RiverPathComponent river )
	{
		FluidDensity = river.FluidDensity;
		LinearDrag = river.LinearDrag;
		AngularDrag = river.AngularDrag;
		SurfaceOffset = river.SurfaceOffset;
		WaveAmplitude = river.WaveAmplitude;
		WaveFrequency = river.WaveFrequency;
		AllowSwimming = river.AllowSwimming;
		AffectRigidbodies = river.AffectRigidbodies;
		EdgeBandWidth = river.EdgeBandWidth;
		SwimDepthThreshold = river.SwimDepthThreshold;
		SurfaceBandHeight = river.SurfaceBandHeight;

		WaterMaterialPath = river.WaterMaterial?.ResourcePath;
		WaterColor = river.WaterColor;
		Transparency = river.Transparency;
		CreateSurfaceVisual = river.CreateSurfaceVisual;
		ShowPathGizmos = river.ShowPathGizmos;
		ShowFlowArrows = river.ShowFlowArrows;
		ShowBankGuides = river.ShowBankGuides;
		FlowArrowSpacing = river.FlowArrowSpacing;
		GizmoSize = river.GizmoSize;
		BrushOverlap = river.BrushOverlap;
	}

	static Material LoadMaterial( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		try
		{
			return Material.Load( path );
		}
		catch
		{
			return null;
		}
	}
}
