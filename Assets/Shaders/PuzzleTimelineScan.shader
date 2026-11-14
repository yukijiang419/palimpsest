Shader "Caffeine/Puzzle Timeline Scan"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = .5
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _GlossMapScale ("Smoothness Scale", Float) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = .5
        [HideInInspector] _ScanOrigin ("Origin", Vector) = (0,0,0,0)
        [HideInInspector] _ScanRadius ("Radius", Float) = -1
        [HideInInspector] _ScanWidth ("Width", Float) = .65
        [HideInInspector] _ScanSide ("Side", Float) = 0
        [HideInInspector] _ScanColor ("Color", Color) = (.35,.67,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma multi_compile_local _ _NORMALMAP
        #pragma multi_compile_local _ _METALLICGLOSSMAP
        #pragma multi_compile_local _ _EMISSION
        #pragma multi_compile_local _ _ALPHATEST_ON
        #include "UnityStandardUtils.cginc"
        sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _OcclusionMap, _EmissionMap;
        fixed4 _Color, _EmissionColor, _ScanColor;
        float3 _ScanOrigin;
        float _ScanRadius, _ScanWidth, _ScanSide;
        half _BumpScale, _Metallic, _Glossiness, _GlossMapScale, _OcclusionStrength, _Cutoff;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float3 worldPos; };
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float signedDistance = _ScanRadius - distance(input.worldPos.xz, _ScanOrigin.xz);
            if (abs(_ScanSide) > .5) clip(signedDistance * _ScanSide);
            fixed4 surface = tex2D(_MainTex, input.uv_MainTex) * _Color;
            #ifdef _ALPHATEST_ON
                clip(surface.a - _Cutoff);
            #endif
            output.Albedo = surface.rgb;
            output.Alpha = surface.a;
            output.Metallic = _Metallic;
            output.Smoothness = _Glossiness;
            #ifdef _METALLICGLOSSMAP
                half4 mask = tex2D(_MetallicGlossMap, input.uv_MainTex);
                output.Metallic = mask.r;
                output.Smoothness = mask.a * _GlossMapScale;
            #endif
            #ifdef _NORMALMAP
                output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_BumpMap), _BumpScale);
            #endif
            output.Occlusion = lerp(1, tex2D(_OcclusionMap, input.uv_MainTex).g, _OcclusionStrength);
            #ifdef _EMISSION
                output.Emission = tex2D(_EmissionMap, input.uv_MainTex).rgb * _EmissionColor.rgb;
            #endif
            float band = 1 - smoothstep(0, max(.001, _ScanWidth), abs(signedDistance));
            output.Emission += _ScanColor.rgb * band * 1.5;
        }
        ENDCG
    }
    Fallback Off
}
