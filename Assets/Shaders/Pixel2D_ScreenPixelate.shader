Shader "Custom/Pixel2D_ScreenPixelate"
{
    Properties
    {
        _PixelSize ("Pixel Block Size", Range(1, 16)) = 3.0
        [Toggle(_POSTERIZE_ON)] _EnablePosterize ("Enable Color Posterize", Float) = 0
        _ColorSteps ("Color Steps / Levels", Range(2, 64)) = 16
        [Toggle(_SCANLINES_ON)] _EnableScanlines ("Enable Retro Scanlines", Float) = 0
        _ScanlineIntensity ("Scanline Darkness", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 100
        ZWrite Off
        Cull Off

        Pass
        {
            Name "PixelatePass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _POSTERIZE_ON
            #pragma shader_feature_local _SCANLINES_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PixelSize;
                float _ColorSteps;
                float _ScanlineIntensity;
            CBUFFER_END

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                if (_PixelSize > 1.0)
                {
                    float2 texel = _BlitTexture_TexelSize.xy * _PixelSize;
                    uv = (floor(uv / texel) + 0.5) * texel;
                }

                half4 col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointRepeat, uv, _BlitMipLevel);

                #if defined(_POSTERIZE_ON)
                if (_ColorSteps > 1.0)
                {
                    col.rgb = floor(col.rgb * _ColorSteps + 0.5) / _ColorSteps;
                }
                #endif

                #if defined(_SCANLINES_ON)
                float scanline = sin(input.texcoord.y * _BlitTexture_TexelSize.w * 3.14159265);
                scanline = (scanline * 0.5 + 0.5);
                col.rgb -= (1.0 - scanline) * _ScanlineIntensity;
                #endif

                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

