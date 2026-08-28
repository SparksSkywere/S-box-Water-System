HEADER
{
	Description = "Underwater post-process tint, darken, and desaturate.";
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 vTexCoord : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( ( PROGRAM == VFX_PROGRAM_PS ) )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
		o.vTexCoord = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"
	#include "postprocess/functions.hlsl"

	Texture2D colorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

	float g_flAmount < Attribute( "Amount" ); Default( 1.0 ); >;
	float3 g_vTint < Attribute( "Tint" ); Default3( 0.15, 0.35, 0.55 ); >;
	float g_flDarken < Attribute( "Darken" ); Default( 0.35 ); >;
	float g_flDesaturate < Attribute( "Desaturate" ); Default( 0.25 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = colorBuffer.SampleLevel( g_sBilinearMirror, i.vTexCoord.xy, 0 );
		float amount = saturate( g_flAmount );
		if ( amount <= 0.001 )
			return color;

		float luma = dot( color.rgb, float3( 0.299, 0.587, 0.114 ) );
		float3 desat = lerp( color.rgb, luma.xxx, saturate( g_flDesaturate ) );
		float3 tinted = lerp( desat, desat * g_vTint * 2.0, 0.65 );
		tinted *= 1.0 - saturate( g_flDarken );

		color.rgb = lerp( color.rgb, tinted, amount );
		return color;
	}
}
