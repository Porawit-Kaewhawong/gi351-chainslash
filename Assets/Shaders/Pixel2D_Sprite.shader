Shader "Custom/Pixel2D_Sprite"
{
    Properties
    {
        [Header(Main Settings)]
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Screen Pixel Snap", Float) = 0
        [MaterialToggle] _SnapToPixelGrid ("Snap to World Pixel Grid", Float) = 0
        _PixelsPerUnit ("Pixels Per Unit", Float) = 16.0
        [MaterialToggle] _ZWrite ("ZWrite", Float) = 0

        [Header(Pixelation)]
        [MaterialToggle] _EnablePixelate ("Enable UV Pixelation", Float) = 0
        _PixelBlockSize ("Pixel Block Size", Range(1, 16)) = 1

        [Header(Outline)]
        [MaterialToggle] _EnableOutline ("Enable Outline", Float) = 0
        _OutlineColor ("Outline Color", Color) = (1, 0.95, 0.35, 1)
        _OutlineThickness ("Outline Thickness (Pixels)", Range(0, 10)) = 1
        _OutlineAlphaThreshold ("Outline Alpha Threshold", Range(0.01, 0.99)) = 0.1
        [KeywordEnum(Outer, Inner, Both)] _OutlineMode ("Outline Mode", Float) = 0

        [Header(Hit Flash)]
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)

        [Header(Color Quantization)]
        [MaterialToggle] _EnableQuantize ("Enable Retro Quantize", Float) = 0
        _ColorSteps ("Color Levels", Range(2, 32)) = 8

        [Header(Dither Dissolve)]
        [MaterialToggle] _EnableDither ("Enable Dither Dissolve", Float) = 0
        _DitherFade ("Dither Fade Amount", Range(0, 1)) = 0

        // Required legacy and sprite properties for Unity SpriteRenderer
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    // =========================================================================
    // Universal Render Pipeline (URP 2D / Forward) SubShader
    // =========================================================================
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex PixelSpriteVertex
            #pragma fragment PixelSpriteFragment

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE
            #pragma multi_compile _ PIXELSNAP_ON

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                half4 _FlashColor;
                float _EnableOutline;
                float _OutlineThickness;
                float _OutlineAlphaThreshold;
                float _OutlineMode;
                float _EnablePixelate;
                float _PixelBlockSize;
                float _SnapToPixelGrid;
                float _PixelsPerUnit;
                float _FlashAmount;
                float _EnableQuantize;
                float _ColorSteps;
                float _EnableDither;
                float _DitherFade;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            Varyings PixelSpriteVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 worldPos = TransformObjectToWorld(input.positionOS);
                if (_SnapToPixelGrid > 0.5 && _PixelsPerUnit > 0.0)
                {
                    worldPos.xy = floor(worldPos.xy * _PixelsPerUnit + 0.5) / _PixelsPerUnit;
                }

                output.positionCS = TransformWorldToHClip(worldPos);

                #if defined(PIXELSNAP_ON)
                float2 hpos = output.positionCS.xy / output.positionCS.w;
                hpos = floor(hpos * _ScreenParams.xy * 0.5 + 0.5) / (_ScreenParams.xy * 0.5);
                output.positionCS = float4(hpos * output.positionCS.w, output.positionCS.zw);
                #endif

                output.uv = input.uv;
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            half4 PixelSpriteFragment(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // 1. Pixelation (UV texel clamping)
                if (_EnablePixelate > 0.5 && _PixelBlockSize > 1.0)
                {
                    float2 texel = _MainTex_TexelSize.xy * _PixelBlockSize;
                    uv = (floor(uv / texel) + 0.5) * texel;
                }

                half4 mainTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half4 finalColor = mainTexColor * input.color;

                // 2. Pixel-Perfect Outline
                if (_EnableOutline > 0.5)
                {
                    float2 offset = _MainTex_TexelSize.xy * _OutlineThickness;

                    float aN  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  offset.y)).a;
                    float aS  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0,  offset.y)).a;
                    float aW  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(offset.x, 0)).a;
                    float aE  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(offset.x, 0)).a;

                    float aNW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-offset.x,  offset.y)).a;
                    float aNE = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( offset.x,  offset.y)).a;
                    float aSW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-offset.x, -offset.y)).a;
                    float aSE = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( offset.x, -offset.y)).a;

                    float maxNeighborAlpha = max(max(max(aN, aS), max(aW, aE)), max(max(aNW, aNE), max(aSW, aSE)));
                    float minNeighborAlpha = min(min(min(aN, aS), min(aW, aE)), min(min(aNW, aNE), min(aSW, aSE)));

                    bool isOpaque = finalColor.a >= _OutlineAlphaThreshold;
                    bool isOutline = false;

                    if (_OutlineMode < 0.5) // Outer (0)
                    {
                        isOutline = (!isOpaque && maxNeighborAlpha >= _OutlineAlphaThreshold);
                    }
                    else if (_OutlineMode < 1.5) // Inner (1)
                    {
                        isOutline = (isOpaque && minNeighborAlpha < _OutlineAlphaThreshold);
                    }
                    else // Both (2)
                    {
                        isOutline = (!isOpaque && maxNeighborAlpha >= _OutlineAlphaThreshold) ||
                                    (isOpaque && minNeighborAlpha < _OutlineAlphaThreshold);
                    }

                    if (isOutline)
                    {
                        finalColor.rgb = _OutlineColor.rgb;
                        finalColor.a = _OutlineColor.a * input.color.a;
                    }
                }

                // 3. Retro Dither Dissolve
                if (_EnableDither > 0.5 && _DitherFade > 0.001)
                {
                    static const float bayer4x4[16] = {
                         0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                        12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                         3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                        15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
                    };

                    uint2 screenPos = uint2(input.positionCS.xy) % 4;
                    float threshold = bayer4x4[screenPos.y * 4 + screenPos.x];
                    if (_DitherFade > threshold)
                    {
                        discard;
                    }
                }

                // 4. Hit Flash
                finalColor.rgb = lerp(finalColor.rgb, _FlashColor.rgb, _FlashAmount);

                // 5. Retro Color Quantization (Posterization)
                if (_EnableQuantize > 0.5 && _ColorSteps > 1.0)
                {
                    finalColor.rgb = floor(finalColor.rgb * _ColorSteps + 0.5) / _ColorSteps;
                }

                return finalColor;
            }
            ENDHLSL
        }
    }

    // =========================================================================
    // Built-in Render Pipeline Fallback SubShader
    // =========================================================================
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _FlashColor;
            float _FlashAmount;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                c.rgb = lerp(c.rgb, _FlashColor.rgb, _FlashAmount);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
