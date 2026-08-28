HEADER
{
	Description = "Source-1 style water surface. Continuous ribbon-friendly translucent sheet.";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"

	// Engine culls none — one triangle set is visible from above and below.
	RenderState( CullMode, NONE );
	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	float3 g_vColorTint < Attribute( "g_vColorTint" ); Default3( 0.12, 0.42, 0.68 ); >;
	float g_flTransparency < Attribute( "Transparency" ); Default( 0.74 ); >;
	float g_flWaveAmplitude < Attribute( "WaveAmplitude" ); Default( 4.0 ); >;
	float g_flWaveFrequency < Attribute( "WaveFrequency" ); Default( 0.8 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 worldPos = i.vPositionWithOffsetWs;
		float3 normalWs = normalize( i.vNormalWs );
		float3 viewDir = normalize( g_vCameraPositionWs - worldPos );

		float ndotv = dot( normalWs, viewDir );
		if ( ndotv < 0.0f )
		{
			normalWs = -normalWs;
			ndotv = -ndotv;
		}

		float fresnel = pow( saturate( 1.0f - ndotv ), 2.0f );
		float ripple = sin( ( worldPos.x + worldPos.y ) * 0.04f * g_flWaveFrequency + g_flTime * g_flWaveFrequency );
		ripple *= 0.035f * saturate( g_flWaveAmplitude * 0.08f );

		float3 tint = max( g_vColorTint, float3( 0.02f, 0.08f, 0.14f ) );
		float3 color = tint + fresnel * 0.2f + ripple;
		float alpha = saturate( g_flTransparency * ( 0.5f + fresnel * 0.4f ) );

		return float4( color, alpha );
	}
}
