Shader "Caffeine/Enemy Death Dissolve"
{
    Properties
    {
        // The runtime death-effect component copies these values from each
        // character material so the enemy keeps its original appearance.
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)

        // Moves the clipping boundary from below the model to above it.
        // The range extends beyond 1 so noise cannot leave fragments behind.
        _Dissolve ("Dissolve", Range(0, 1.35)) = 0

        // Controls the bright band surrounding the active dissolve boundary.
        _EdgeColor ("Edge Color", Color) = (0.1, 0.9, 1, 1)
        _EdgeWidth ("Edge Width", Range(0.005, 0.25)) = 0.08

        // Noise breaks up the otherwise straight, horizontal clipping line.
        _NoiseScale ("Noise Scale", Range(0.5, 20)) = 6

        // Adds a small animated displacement near the active boundary.
        _GlitchStrength ("Glitch Strength", Range(0, 0.15)) = 0.035

        // World-space renderer bounds supplied by EnemyDeathEffect.cs.
        _MinY ("Bounds Min Y", Float) = -1
        _MaxY ("Bounds Max Y", Float) = 1
    }

    SubShader
    {
        // AlphaTest keeps visible pixels opaque, casts clipped shadows, and
        // avoids the sorting issues associated with transparent materials.
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 250
        Cull Back

        // Shared functions and material variables are declared in CGINCLUDE so
        // the forward and shadow passes evaluate exactly the same animation and
        // dissolve boundary instead of maintaining two copies of the logic.
        CGINCLUDE
        #include "UnityCG.cginc"

        // _MainTex_ST stores Unity's material tiling and offset. fixed/half are
        // sufficient for colours and controls, while world bounds remain float
        // to avoid visible height precision errors on large scene coordinates.
        sampler2D _MainTex;
        float4 _MainTex_ST;
        fixed4 _Color;
        fixed4 _EdgeColor;
        half _Dissolve;
        half _EdgeWidth;
        half _NoiseScale;
        half _GlitchStrength;
        float _MinY;
        float _MaxY;

        // Produces a repeatable pseudo-random value for a three-dimensional
        // integer coordinate. No texture lookup is required for the noise.
        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        // Trilinearly interpolated value noise softens the dissolve outline
        // while keeping the effect inexpensive enough for several enemies.
        float ValueNoise(float3 p)
        {
            // Split the sample into its integer lattice cell and fractional
            // location inside that cell.
            float3 cell = floor(p);
            float3 local = frac(p);

            // Cubic Hermite smoothing removes hard seams between noise cells.
            local = local * local * (3.0 - 2.0 * local);

            // Hash the eight corners of the surrounding 3D lattice cell.
            float n000 = Hash31(cell + float3(0, 0, 0));
            float n100 = Hash31(cell + float3(1, 0, 0));
            float n010 = Hash31(cell + float3(0, 1, 0));
            float n110 = Hash31(cell + float3(1, 1, 0));
            float n001 = Hash31(cell + float3(0, 0, 1));
            float n101 = Hash31(cell + float3(1, 0, 1));
            float n011 = Hash31(cell + float3(0, 1, 1));
            float n111 = Hash31(cell + float3(1, 1, 1));

            // Interpolate along X, then Y, then Z to obtain continuous 3D
            // value noise from the eight discrete corner values.
            float nx00 = lerp(n000, n100, local.x);
            float nx10 = lerp(n010, n110, local.x);
            float nx01 = lerp(n001, n101, local.x);
            float nx11 = lerp(n011, n111, local.x);
            return lerp(lerp(nx00, nx10, local.y), lerp(nx01, nx11, local.y), local.z);
        }

        // Returns the animated object-space position. Both the visible and
        // shadow passes call this function, keeping the silhouette consistent.
        float3 ApplyGlitch(float3 positionOS, float3 normalOS)
        {
            // Convert world height to 0..1 using the complete character bounds.
            float worldY = mul(unity_ObjectToWorld, float4(positionOS, 1.0)).y;
            float height01 = saturate((worldY - _MinY) / max(_MaxY - _MinY, 0.001));

            // Restrict displacement to a narrow band around the dissolve edge.
            // The sine wave creates the horizontal digital-glitch movement.
            float activeBand = 1.0 - saturate(abs(height01 - _Dissolve) * 12.0);
            float glitch = sin(worldY * 43.0 + _Time.y * 31.0) * activeBand * _GlitchStrength;
            positionOS.xz += normalOS.xz * glitch;
            return positionOS;
        }

        // Returns signed distance from the noisy dissolve boundary. Negative
        // values are removed with clip() in both rendering passes.
        float DissolveDistance(float3 worldPosition)
        {
            // Raising _Dissolve moves the boundary upward. Procedural noise
            // offsets it locally to create an irregular disintegration edge.
            float height01 = saturate((worldPosition.y - _MinY) / max(_MaxY - _MinY, 0.001));
            float noise = ValueNoise(worldPosition * _NoiseScale) * 0.18;
            float threshold = _Dissolve - noise;

            // Positive means the fragment is above the boundary and survives;
            // negative means it lies in the already dissolved region.
            return height01 - threshold;
        }
        ENDCG

        // Handwritten forward pass: transforms vertices, evaluates the noisy
        // mask per fragment, and applies probe plus main-light shading.
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            // These entry points are handwritten vertex/fragment programs—not
            // a Surface Shader or Shader Graph generated implementation.
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            // Unity generates variants for the supported forward lights,
            // shadows, scene fog, and GPU-instanced enemies.
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct AppData
            {
                // Per-vertex mesh attributes read from the enemy's skinned mesh.
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertexToFragment
            {
                // Clip position is consumed by rasterisation. The remaining
                // values are interpolated across each triangle for the fragment
                // program; TEXCOORD slots are generic interpolator registers.
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                half3 worldNormal : TEXCOORD2;
                SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VertexToFragment vert(AppData input)
            {
                VertexToFragment output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(VertexToFragment, output);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Apply the animated deformation in object space, then convert
                // the result to world and homogeneous clip coordinates.
                float3 positionOS = ApplyGlitch(input.vertex.xyz, input.normal);
                float3 positionWS = mul(unity_ObjectToWorld, float4(positionOS, 1.0)).xyz;
                output.pos = UnityWorldToClipPos(positionWS);
                output.worldPosition = positionWS;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                // Supply Unity's generated shadow lookup and fog coordinates.
                TRANSFER_SHADOW_WPOS(output, positionWS);
                UNITY_TRANSFER_FOG(output, output.pos);
                return output;
            }

            fixed4 frag(VertexToFragment input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float signedDistance = DissolveDistance(input.worldPosition);

                // Discard every fragment below the current noisy boundary.
                // This is a binary cutout, so it avoids transparency sorting.
                clip(signedDistance);

                // Start with the enemy's original texture and tint.
                fixed4 albedo = tex2D(_MainTex, input.uv) * _Color;

                // smoothstep changes from zero to one across _EdgeWidth;
                // inversion selects the surviving strip nearest the boundary.
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, signedDistance);

                // The time-varying sine wave modulates the edge brightness to
                // resemble a scanning archive reconstruction error.
                float scanline = 0.7 + 0.3 * sin(input.worldPosition.y * 90.0 - _Time.y * 24.0);

                // Evaluate Lambert diffuse lighting explicitly. ShadeSH9 adds
                // ambient/light-probe illumination for dynamic enemies.
                half3 normalWS = normalize(input.worldNormal);
                half3 lightDirection = normalize(UnityWorldSpaceLightDir(input.worldPosition));
                half nDotL = saturate(dot(normalWS, lightDirection));
                UNITY_LIGHT_ATTENUATION(attenuation, input, input.worldPosition);
                half3 ambient = ShadeSH9(half4(normalWS, 1.0));
                half3 lighting = ambient + _LightColor0.rgb * nDotL * attenuation;

                // Tint the boundary toward cyan, then add an unlit emissive
                // contribution so the effect stays readable in deep shadow.
                half3 surfaceColor = lerp(albedo.rgb, _EdgeColor.rgb, edge * 0.65);
                half3 edgeEmission = _EdgeColor.rgb * edge * (2.0 + scanline);
                fixed4 outputColor = fixed4(surfaceColor * lighting + edgeEmission, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, outputColor);
                return outputColor;
            }
            ENDCG
        }

        // Handwritten shadow pass uses the same deformation and dissolve mask,
        // so disappearing pieces stop casting shadows at the correct moment.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            // A separate explicit vertex/fragment pair writes only shadow-map
            // depth; no automatically generated Surface Shader pass is used.
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            struct ShadowAppData
            {
                // UVs are unnecessary because the procedural cutout is based on
                // world position rather than texture alpha.
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowInterpolators
            {
                V2F_SHADOW_CASTER;
                float3 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowInterpolators vertShadow(ShadowAppData input)
            {
                ShadowInterpolators output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(ShadowInterpolators, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Reuse the visible pass deformation before projecting the
                // vertex from the light's point of view into the shadow map.
                float3 positionOS = ApplyGlitch(input.vertex.xyz, input.normal);
                float4 positionOS4 = float4(positionOS, 1.0);
                output.worldPosition = mul(unity_ObjectToWorld, positionOS4).xyz;
                output.pos = UnityClipSpaceShadowCasterPos(positionOS4, input.normal);
                output.pos = UnityApplyLinearShadowBias(output.pos);
                return output;
            }

            float4 fragShadow(ShadowInterpolators input) : SV_Target
            {
                // Applying the same procedural clip prevents dissolved pixels
                // from leaving a solid, undeformed shadow behind.
                clip(DissolveDistance(input.worldPosition));
                SHADOW_CASTER_FRAGMENT(input)
            }
            ENDCG
        }
    }

    FallBack Off
}
