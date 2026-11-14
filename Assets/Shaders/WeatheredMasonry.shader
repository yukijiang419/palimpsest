Shader "Campus/Weathered Masonry"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Color ("Stone Colour", Color) = (0.6, 0.57, 0.51, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Glossiness ("Smoothness", Range(0, 1)) = 0.15
        _TextureScale ("World Texture Scale", Range(0.1, 4)) = 1
        _GrainScale ("Weathering Scale", Range(0.1, 8)) = 1
        _GrainStrength ("Weathering Strength", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Metallic, _Glossiness, _GrainScale, _GrainStrength, _TextureScale;
        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };
        half Hash(float2 p)
        {
            float3 q = frac(float3(p.xyx) * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }
        half Noise(float2 p)
        {
            float2 cell = floor(p);
            float2 blend = frac(p);
            blend = blend * blend * (3.0 - 2.0 * blend);
            half a = lerp(Hash(cell), Hash(cell + float2(1, 0)), blend.x);
            half b = lerp(Hash(cell + float2(0, 1)), Hash(cell + 1), blend.x);
            return lerp(a, b, blend.y);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half3 axis = abs(IN.worldNormal);
            float2 p = axis.y > axis.x && axis.y > axis.z ? IN.worldPos.xz
                : axis.x > axis.z ? IN.worldPos.zy : IN.worldPos.xy;
            half large = Noise(p * _GrainScale * 0.55);
            half fine = Noise(p * _GrainScale * 5.0);
            half weather = (large - 0.5) * 0.48 + (fine - 0.5) * 0.15;
            half3 baseColor = tex2D(_MainTex, p * _TextureScale).rgb * _Color.rgb;
            o.Albedo = baseColor * (1.0 + _GrainStrength * weather);
            o.Metallic = _Metallic;
            o.Smoothness = saturate(_Glossiness + _GrainStrength * (fine - 0.5) * 0.07);
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
