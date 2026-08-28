using System;

namespace WaterSystem;

/// <summary>
/// Snapshot of look and physics passed into the shared runtime builders.
/// </summary>
public sealed class WaterConfig
{
	public float Width { get; set; } = 512f;
	public float Length { get; set; } = 512f;
	public float Depth { get; set; } = 128f;
	public float CurrentSpeed { get; set; }
	public Vector3 CurrentDirection { get; set; } = Vector3.Forward;
	public float FluidDensity { get; set; } = 1000f;
	public float LinearDrag { get; set; } = 2.2f;
	public float AngularDrag { get; set; } = 1.4f;
	public float SurfaceOffset { get; set; }
	public float WaveAmplitude { get; set; }
	public float WaveFrequency { get; set; } = 1.2f;
	public Color WaterColor { get; set; } = new Color( 0.12f, 0.42f, 0.72f, 0.72f );
	public float Transparency { get; set; } = 0.72f;
	public float SwimDepthThreshold { get; set; } = 36f;
	public float SurfaceBandHeight { get; set; } = 16f;
	public float EdgeBandWidth { get; set; } = 24f;
	public bool AllowSwimming { get; set; } = true;
	public bool AffectRigidbodies { get; set; } = true;

	public void Validate()
	{
		Width = MathF.Max( 8f, Width );
		Length = MathF.Max( 8f, Length );
		Depth = MathF.Max( 8f, Depth );
		CurrentSpeed = MathF.Max( 0f, CurrentSpeed );
		FluidDensity = MathF.Max( 1f, FluidDensity );
		LinearDrag = MathF.Max( 0f, LinearDrag );
		AngularDrag = MathF.Max( 0f, AngularDrag );
		Transparency = MathX.Clamp( Transparency, 0f, 1f );
		WaveAmplitude = MathF.Max( 0f, WaveAmplitude );
		WaveFrequency = MathF.Max( 0.01f, WaveFrequency );
		SwimDepthThreshold = MathF.Max( 1f, SwimDepthThreshold );
		SurfaceBandHeight = MathF.Max( 0f, SurfaceBandHeight );
		EdgeBandWidth = MathF.Max( 0f, EdgeBandWidth );
	}

	public Vector3 GetFluidVelocity( Vector3 fallbackDirection )
	{
		var dir = CurrentDirection.LengthSquared > 0.0001f ? CurrentDirection.Normal : fallbackDirection;
		if ( dir.LengthSquared <= 0.0001f )
			dir = Vector3.Forward;

		return dir * CurrentSpeed;
	}

	public Color GetTint()
	{
		var tint = WaterColor;
		tint.a = Transparency;
		return tint;
	}
}
