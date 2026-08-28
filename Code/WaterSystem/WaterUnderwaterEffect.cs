namespace WaterSystem;

/// <summary>
/// Blue underwater tint applied while the camera is submerged.
/// Uses local Amount (not GetWeighted volumes) so it works when toggled on the camera.
/// </summary>
[Title( "Underwater Post Process" )]
[Category( "Water" )]
[Icon( "water_drop" )]
public sealed class WaterUnderwaterEffect : BasePostProcess<WaterUnderwaterEffect>
{
	const string ShaderPath = "shaders/postprocess/pp_underwater.shader";

	[Property, Range( 0f, 1f )] public float Amount { get; set; } = 1f;
	[Property] public Color Tint { get; set; } = new Color( 0.12f, 0.32f, 0.52f );
	[Property, Range( 0f, 1f )] public float Darken { get; set; } = 0.35f;
	[Property, Range( 0f, 1f )] public float Desaturate { get; set; } = 0.25f;

	static Material _shaderMaterial;
	static bool _shaderLoadAttempted;

	public override void Render()
	{
		// Prefer volume blend when present; otherwise use the camera-local Amount.
		var amount = GetWeighted( x => x.Amount );
		if ( amount.AlmostEqual( 0f ) )
			amount = Amount;

		if ( !Enabled || amount.AlmostEqual( 0f ) )
			return;

		var shader = GetShaderMaterial();
		if ( shader is null )
			return;

		Attributes.Set( "Amount", amount );
		Attributes.Set( "Tint", new Vector3( Tint.r, Tint.g, Tint.b ) );
		Attributes.Set( "Darken", Darken );
		Attributes.Set( "Desaturate", Desaturate );

		var blit = BlitMode.WithBackbuffer( shader, Sandbox.Rendering.Stage.AfterPostProcess, 200, false );
		Blit( blit, "Underwater" );
	}

	static Material GetShaderMaterial()
	{
		if ( _shaderMaterial is not null )
			return _shaderMaterial;

		if ( _shaderLoadAttempted )
			return null;

		_shaderLoadAttempted = true;
		_shaderMaterial = Material.FromShader( ShaderPath );
		return _shaderMaterial;
	}
}
