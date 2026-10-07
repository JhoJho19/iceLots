Shader "Custom/UI/IceDoubleShine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Shine)]
        _ShineColor ("Shine Color", Color) = (0.8, 0.95, 1, 1)
        _ShineIntensity ("Shine Intensity", Range(0, 3)) = 0.8
        _ShineWidth ("Shine Width", Range(0.01, 0.5)) = 0.01
        _WaveDuration ("Wave Duration", Range(0.1, 2)) = 0.7
        _SecondWaveDelay ("Second Wave Delay", Range(0, 2)) = 0.2
        _PauseAfterSecond ("Pause After Second", Range(0, 10)) = 3.0
        _ShineTilt ("Shine Tilt", Range(-2, 2)) = 0.45

        [Header(UI)]
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)]
        _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                float4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _TextureSampleAdd;

            fixed4 _Color;
            fixed4 _ShineColor;

            float _ShineIntensity;
            float _ShineWidth;
            float _WaveDuration;
            float _SecondWaveDelay;
            float _PauseAfterSecond;
            float _ShineTilt;

            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;

                return OUT;
            }

            float GetShineWave(
                float currentTime,
                float startTime,
                float2 uv)
            {
                float elapsed = currentTime - startTime;

                float active =
                    step(0.0, elapsed) *
                    step(elapsed, _WaveDuration);

                float phase =
                    saturate(elapsed / max(_WaveDuration, 0.001));

                float minCoord = min(0.0, _ShineTilt);
                float maxCoord = 1.0 + max(0.0, _ShineTilt);

                float coord =
                    uv.x +
                    uv.y * _ShineTilt;

                float travelPadding = _ShineWidth * 2.0;

                float center = lerp(
                    minCoord - travelPadding,
                    maxCoord + travelPadding,
                    phase
                );

                float distanceToCenter =
                    abs(coord - center);

                float shine = 1.0 -
                    smoothstep(
                        _ShineWidth * 0.15,
                        _ShineWidth,
                        distanceToCenter
                    );

                float core = 1.0 -
                    smoothstep(
                        0.0,
                        _ShineWidth * 0.2,
                        distanceToCenter
                    );

                shine += core * 0.65;

                return shine * active;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 color =
                    (tex2D(_MainTex, IN.texcoord) +
                     _TextureSampleAdd) *
                    IN.color;

                float cycleDuration =
                    _SecondWaveDelay +
                    _PauseAfterSecond;

                float cycleTime =
                    fmod(_Time.y, cycleDuration);

                float wave1 = GetShineWave(
                    cycleTime,
                    0.0,
                    IN.texcoord
                );

                float wave2 = GetShineWave(
                    cycleTime,
                    _SecondWaveDelay,
                    IN.texcoord
                );

                float shine =
                    saturate(wave1 + wave2);

                color.rgb +=
                    _ShineColor.rgb *
                    shine *
                    _ShineIntensity *
                    color.a;

                #ifdef UNITY_UI_CLIP_RECT
                    color.a *= UnityGet2DClipping(
                        IN.worldPosition.xy,
                        _ClipRect
                    );
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif

                return color;
            }

            ENDCG
        }
    }
}