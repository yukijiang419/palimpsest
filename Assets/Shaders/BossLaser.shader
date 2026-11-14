// Boss sky-laser attack. One shader, two looks selected by _Mode:
//   0 = ground warning on a quad: a thin pulsing ring that pulses faster as _Progress approaches the strike.
//   1 = laser beam on a cylinder: bright core facing the camera, soft edges, and light bands scrolling down.
// _Progress and _Alpha are driven per instance by BossController through a MaterialPropertyBlock.
Shader "Caffeine/BossLaser"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.3, 0.2, 1)
        [Enum(Warning,0,Beam,1)] _Mode ("Mode", Float) = 0
        _Progress ("Warning Progress", Range(0, 1)) = 0
        _Alpha ("Alpha", Range(0, 1)) = 1
        _RingWidth ("Ring Width", Range(0.01, 0.3)) = 0.08
        _BandCount ("Beam Band Count", Range(1, 30)) = 10
        _BandSpeed ("Beam Band Speed", Range(0, 20)) = 8
        _CoreWhite ("Beam Core Whiteness", Range(0, 1)) = 0.25
        // Alpha blending keeps dark colours (deep red) dark; additive (One) would brighten them against the floor.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        // Blend mode comes from the material (alpha blend by default); never writes depth so overlapping strikes blend.
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Mode;
            float _Progress;
            float _Alpha;
            float _RingWidth;
            float _BandCount;
            float _BandSpeed;
            float _CoreWhite;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float facing : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                // How directly this part of the cylinder faces the camera: 1 at the centre line, 0 at the silhouette.
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 toCamera = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz);
                o.facing = abs(dot(worldNormal, toCamera));
                return o;
            }

            // Branches are written inline with clip() rather than as helper functions with discard,
            // which the shader compiler rejects when both are reached through one expression.
            fixed4 frag(v2f i) : SV_Target
            {
                if (_Mode < 0.5)
                {
                    // Warning: just a thin ring, no fill. 0 at the centre, 1 at the quad's inscribed circle.
                    float d = length(i.uv - 0.5) * 2.0;
                    clip(1.0 - d);
                    // Pulses faster as the strike gets closer.
                    float pulse = 0.65 + 0.35 * sin(_Time.y * (8.0 + 12.0 * _Progress));
                    float ring = smoothstep(1.0 - _RingWidth, 1.0 - _RingWidth * 0.5, d) * (1.0 - smoothstep(0.96, 1.0, d));
                    return fixed4(_Color.rgb, saturate(ring * pulse * _Alpha));
                }

                // Beam: brighter where the cylinder faces the camera, with light bands scrolling down.
                float core = pow(i.facing, 3.0);
                float bands = 0.6 + 0.4 * frac(i.uv.y * _BandCount + _Time.y * _BandSpeed);
                float3 rgb = lerp(_Color.rgb, float3(1, 1, 1), core * _CoreWhite);
                return fixed4(rgb, saturate(core * 1.4 + 0.15) * bands * _Alpha);
            }
            ENDCG
        }
    }
}
