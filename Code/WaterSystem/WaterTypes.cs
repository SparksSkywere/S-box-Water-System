namespace WaterSystem;

/// <summary>
/// Depth band used by sampling, swim helpers, and tools that look for water.
/// </summary>
public enum WaterZoneType
{
	None,
	Edge,
	Surface,
	Swim,
	Underwater
}

/// <summary>
/// Collision and visual shape for a placed volume.
/// </summary>
public enum WaterShapeType
{
	Box,
	Sphere
}
