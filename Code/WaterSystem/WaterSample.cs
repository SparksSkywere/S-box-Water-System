namespace WaterSystem;

/// <summary>
/// Anything that can answer "is this point in water?" — volumes and rivers.
/// </summary>
public interface IWaterSource
{
	GameObject GameObject { get; }
	string DisplayName { get; }
	bool Enabled { get; }
	bool TrySample( Vector3 worldPosition, out WaterSample sample );
}

/// <summary>
/// One sample of water at a world position.
/// </summary>
public struct WaterSample
{
	public bool Hit;
	public IWaterSource Source;
	public WaterZoneType Zone;
	public float SurfaceHeight;
	public float DepthInWater;
	public float Submersion;
	public Vector3 Flow;
	public float FluidDensity;

	/// <summary>Scalar flow speed at the sample (units/sec).</summary>
	public float FlowSpeed;

	/// <summary>0 at centerline, 1 at bank edge, above 1 in the shore band.</summary>
	public float LateralNormalized;

	/// <summary>-1 left bank, 0 center channel, +1 right bank (relative to flow).</summary>
	public float BankSide;

	public static WaterSample Miss => default;
}
