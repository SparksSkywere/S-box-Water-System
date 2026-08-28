using System;

namespace WaterSystem;

/// <summary>
/// Extra buoyancy for interactable / weighted rigidbodies in water.
/// Engine WaterVolume already floats most bodies; this scales force for heavy props.
/// </summary>
[Title( "Water Buoyant" )]
[Category( "Water" )]
[Icon( "anchor" )]
public sealed class WaterBuoyantObject : Component
{
	[Property, Range( 0f, 3f )] public float BuoyancyMultiplier { get; set; } = 1f;
	[Property, Range( 0f, 1f )] public float CurrentScale { get; set; } = 0.35f;
	[Property] public bool RequireSubmerged { get; set; } = true;

	Rigidbody _body;

	protected override void OnStart()
	{
		_body = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
	}

	protected override void OnFixedUpdate()
	{
		_body ??= GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
		if ( _body is null || !_body.Enabled || !_body.MotionEnabled )
			return;

		if ( !WaterSystemManager.TrySample( Scene, WorldPosition, out var sample ) || !sample.Hit )
			return;

		if ( RequireSubmerged && sample.DepthInWater < 2f )
			return;

		var mass = MathF.Max( 1f, _body.Mass );
		var sub = MathX.Clamp( sample.Submersion, 0.05f, 1.5f );

		// Scale lift for heavier props.
		var lift = Vector3.Up * (sample.FluidDensity * 0.01f * BuoyancyMultiplier * sub * mass);
		_body.ApplyForce( lift );

		if ( CurrentScale > 0.001f && sample.Flow.LengthSquared > 0.01f )
			_body.ApplyForce( sample.Flow * (CurrentScale * mass) );
	}
}
