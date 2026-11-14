Shader "Caffeine/Tree Wind"
{
    Properties
    {
        // Material prop
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Strength", Range(0, 2)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.15
        [HideInInspector] [HDR] _RimColor ("Archive Rim", Color) = (0.08, 0.42, 0.58, 1)
        [HideInInspector] _RimStrength ("Rim Strength", Range(0, 1)) = 0.08
        [HideInInspector] _RimPower ("Rim Tightness", Range(0.5, 8)) = 4

        // Grass uses the same wind with its own material response.
        [Enum(Bark, 0, Leaf, 1, Grass, 2)] _IsLeaf ("Surface Type", Float) = 0
        
        
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        // local-space height mask; defaults fit CommonTree_4 (-0.25 to 9.2)
        _BendBaseHeight ("Fixed Root Height", Float) = -0.25
        _TreeHeight ("Tree Height", Range(0.1, 30)) = 9.45
        [HideInInspector] _BendExponent ("Bend Falloff", Range(0.5, 4)) = 1.7
        _SwayStrengthScale ("Sway Amplitude Multiplier", Range(0, 2)) = 1
        _SwaySpeedScale ("Sway Speed Multiplier", Range(0, 4)) = 1
        // small, fast leaf motion layered over the shared tree sway
        _LeafFlutterStrength ("Leaf Flutter Amplitude (m)", Range(0, 0.25)) = 0.04
        _LeafFlutterSpeed ("Leaf Flutter Speed (rad/s)", Range(0, 20)) = 7

        // Archive faults spread across leaf surfaces and canopy scanline bands.
        _LeafGlitchStrength ("Leaf Glitch Strength", Range(0, 1)) = 1
        _LeafGlitchSpread ("Canopy Fault Coverage", Range(0, 1)) = 0.25
        _LeafGlitchInterval ("Leaf Glitch Interval (s)", Range(1, 15)) = 8
        _LeafGlitchDuration ("Leaf Glitch Burst (s)", Range(0.05, 1)) = 0.55
        [HDR] _LeafGlitchColor ("Leaf Glitch Edge", Color) = (0.18, 0.65, 0.8, 1)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    // material inputs shared by the visible and shadow passes
    sampler2D _MainTex;
    float4 _MainTex_ST;
    sampler2D _BumpMap;
    float4 _BumpMap_ST;
    fixed4 _Color;
    half _NormalScale;
    half _Smoothness;
    half _SpecularStrength;
    fixed4 _RimColor;
    half _RimStrength;
    half _RimPower;
    half _Cutoff;
    float _IsLeaf;
    float _BendBaseHeight;
    float _TreeHeight;
    float _BendExponent;
    float _SwayStrengthScale;
    float _SwaySpeedScale;
    float _LeafFlutterStrength;
    float _LeafFlutterSpeed;
    float _LeafGlitchStrength;
    float _LeafGlitchSpread;
    float _LeafGlitchInterval;
    float _LeafGlitchDuration;
    fixed4 _LeafGlitchColor;

    // This is set by wind controller componet 
    float4 _GlobalTreeWindDirection;
    float _GlobalTreeWindStrength;
    float _GlobalTreeWindSpeed;
    float _GlobalTreeSecondarySwaySpeedRatio;
    float _GlobalTreeSecondarySwayWeight;
    float4 _GlobalTreeSwayWaveVector;
    float4 _GlobalTreeFlutterWaveVector;
    float _GlobalTreeGustStrength;
    float _GlobalTreeGustFrequency;

    // blend a small cross-wind component into the leaf flutter direction
    static const float FlutterCrossWindWeight = 0.2;

    static const float LeafGlitchTickRate = 18.0;
    static const float LeafGlitchBandsPerMetre = 8.0;
    static const float LeafGlitchBandThreshold = 0.45;

    // Avoid glitching the same clusters every burst.
    static const float2 LeafGlitchCycleOffset = float2(13.1, 7.3);

    // Blinn-Phong exponent range: a higher exponent produces a tighter highlight
    static const half BroadHighlightExponent = 8.0;
    static const half TightHighlightExponent = 128.0;

    // model vertex attributes; the instance ID selects each tree's transform
    struct TreeAppData
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float4 tangent : TANGENT;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    // helper function to clamp a value to [0, 1] without branching
    float Clamp01(float value)
    {
        return clamp(value, 0.0, 1.0);
    }

    // Position hash retained from the original shader; fixed algorithm coefficients.
    // Original reference: https://www.shadertoy.com/view/ldB3zc
    float HashTreePosition(float2 horizontalPosition)
    {
        float value = sin(dot(horizontalPosition, float2(127.1, 311.7)));
        return frac(value * 43758.5453);
    }

    float LeafGlitchCycleTime(float treePhase)
    {
        float interval = max(_LeafGlitchInterval, 1.0);
        return frac((_Time.y + treePhase) / interval) * interval;
    }

    float LeafGlitchMask(float3 worldPosition, float treePhase)
    {
        float leafOnly = step(0.5, _IsLeaf) * (1.0 - step(1.5, _IsLeaf));

        float interval = max(_LeafGlitchInterval, 1.0);
        float phaseTime = _Time.y + treePhase;
        float cycle = phaseTime / interval;
        float cycleIndex = floor(cycle);
        float cycleTime = frac(cycle) * interval;
        float burst = 1.0 - step(_LeafGlitchDuration, cycleTime);

        // Group leaves into 1 m cells in world XZ.
        float2 cluster = floor(worldPosition.xz) + treePhase + cycleIndex * LeafGlitchCycleOffset;
        float selectedCluster = step(1.0 - _LeafGlitchSpread, HashTreePosition(cluster));

        float tickIndex = floor(phaseTime * LeafGlitchTickRate);
        float bandIndex = floor(worldPosition.y * LeafGlitchBandsPerMetre);
        float selectedBand = step(LeafGlitchBandThreshold, HashTreePosition(float2(bandIndex, tickIndex)));
        return leafOnly * selectedCluster * burst * selectedBand * _LeafGlitchStrength;
    }

    float LeafGlitchMissing(float treePhase)
    {
        // Pulse windows are fractions of the burst duration.
        float burstProgress = LeafGlitchCycleTime(treePhase) / max(_LeafGlitchDuration, 0.05);
        float firstPulse = step(0.12, burstProgress) * (1.0 - step(0.48, burstProgress));
        float secondPulse = step(0.64, burstProgress) * (1.0 - step(0.79, burstProgress));
        return firstPulse + secondPulse;
    }

    // Phase in radians: omega*t + dot(k, x) + phi0; |k| = 2*pi / wavelength.
    // https://en.wikipedia.org/wiki/Sinusoidal_plane_wave#Alternative_representations
    // Positive time makes the spatial pattern travel opposite to k.
    float CalculateWavePhase(float timeSeconds, float angularSpeed, float phaseOffset,
        float3 worldPosition, float3 waveVector)
    {
        return timeSeconds * angularSpeed + phaseOffset + dot(worldPosition, waveVector);
    }

    struct TreeWindData
    {
        float3 originalWorldPosition; // the vertex position before any wind deformation
        float2 horizontalWindDirection; // the wind direction in the horizontal plane (x, z)
        float treePhase; // a per-tree phase offset for the wind oscillation
        float heightCoefficient; // a normalized height weight for the vertex, in [0, 1]
    };

    // Prepare the shared inputs 
    TreeWindData PrepareTreeWindData(float4 objectPosition)
    {
        TreeWindData wind;

        // Convert the original vertex from object space to world space before deformation.
        wind.originalWorldPosition = mul(unity_ObjectToWorld, objectPosition).xyz;
        
        wind.horizontalWindDirection = _GlobalTreeWindDirection.xz;

        // Transform the model origin to world space
        float3 treeWorldOrigin = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;

        // Map the position hash from [0, 1) to a phase offset in [0, 2*pi) radians.
        wind.treePhase = HashTreePosition(treeWorldOrigin.xz) * (2.0 * UNITY_PI);

        // Normalize local height above the fixed root to [0,1]
        float height01 = Clamp01((objectPosition.y - _BendBaseHeight) / _TreeHeight);

        // Shape the height weight: roots remain fixed; exponents above 1 reduce lower-tree motion.
        wind.heightCoefficient = pow(height01, _BendExponent);

        return wind;
    }

    // Flutter displacement in world space, mostly along the leaf normal.
    float3 CalculateLeafFlutterOffset(TreeWindData wind, float3 objectNormal)
    {
        float3 worldNormal = UnityObjectToWorldNormal(objectNormal);
        float3 crossWindDirection = float3(-wind.horizontalWindDirection.y, 0.0, wind.horizontalWindDirection.x);
        float flutterPhase = CalculateWavePhase(_Time.y, _LeafFlutterSpeed, wind.treePhase,
            wind.originalWorldPosition, _GlobalTreeFlutterWaveVector.xyz);
        float leafFlutterFactor = sin(flutterPhase);
        float3 flutterDirection = normalize(lerp(worldNormal, crossWindDirection, FlutterCrossWindWeight));
        return flutterDirection * leafFlutterFactor * _LeafFlutterStrength * wind.heightCoefficient;
    }

    // Whole-tree sway displacement in world space.
    float3 CalculateTreeSwayOffset(TreeWindData wind)
    {
        // share the tree offset and per-vertex spatial phase across both sway layers
        float swaySpeed = _GlobalTreeWindSpeed * _SwaySpeedScale;
        float primaryPhase = CalculateWavePhase(_Time.y, swaySpeed, wind.treePhase,
            wind.originalWorldPosition, _GlobalTreeSwayWaveVector.xyz);
        float secondaryPhase = CalculateWavePhase(_Time.y,
            swaySpeed * _GlobalTreeSecondarySwaySpeedRatio, wind.treePhase,
            wind.originalWorldPosition, _GlobalTreeSwayWaveVector.xyz);

        // blend the oscillations within [-1, 1]
        float primarySway = sin(primaryPhase);
        float secondarySway = sin(secondaryPhase);
        float swayFactor = lerp(primarySway, secondarySway, Clamp01(_GlobalTreeSecondarySwayWeight));

        // remap sine from [-1, 1] to [0, 1]; gusts increase the base amplitude
        float gustPhase = CalculateWavePhase(_Time.y, _GlobalTreeGustFrequency, wind.treePhase,
            wind.originalWorldPosition, float3(0.0, 0.0, 0.0));
        float gustSway = sin(gustPhase);
        float gust = (gustSway + 1.0) * 0.5;
        float bend = swayFactor * _GlobalTreeWindStrength * _SwayStrengthScale * (1.0 + gust * _GlobalTreeGustStrength);
        float2 horizontalOffset = wind.horizontalWindDirection * bend * wind.heightCoefficient;
        return float3(horizontalOffset.x, 0.0, horizontalOffset.y);
    }

    // Both passes use the same position for bark, leaves and grass.
    float3 CalculateWindPosition(TreeWindData wind, float3 objectNormal)
    {
        float3 worldPosition = wind.originalWorldPosition + CalculateTreeSwayOffset(wind);
        if (_IsLeaf >= 1.0)
        {
            worldPosition += CalculateLeafFlutterOffset(wind, objectNormal);
        }
        float fault = LeafGlitchMask(worldPosition, wind.treePhase);
        float direction = step(0.5, frac(worldPosition.y * 3.1 + wind.treePhase)) * 2.0 - 1.0;
        worldPosition.x += fault * direction * 0.16;
        return worldPosition;
    }
    ENDCG

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "IgnoreProjector" = "True"
        }
        // cutout pixels are discarded; remaining pixels write normal opaque depth
        Cull [_Cull]
        ZWrite On

        // visible surface: vertex wind, texture sampling, lighting and fog
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            // interpolated UVs, world position and tangent basis for the fragment shader
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 bumpUV : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                half3 worldNormal : TEXCOORD3;
                half3 worldTangent : TEXCOORD4;
                half3 worldBitangent : TEXCOORD5;
                SHADOW_COORDS(6)
                UNITY_FOG_COORDS(7)
                float treePhase : TEXCOORD8;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(TreeAppData v)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_TRANSFER_INSTANCE_ID(v, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                TreeWindData wind = PrepareTreeWindData(v.vertex);

                float3 deformedWorldPos = CalculateWindPosition(wind, v.normal);
                
                output.pos = UnityWorldToClipPos(deformedWorldPos);
                output.worldPos = deformedWorldPos;
                output.uv = TRANSFORM_TEX(v.uv, _MainTex);
                output.bumpUV = TRANSFORM_TEX(v.uv, _BumpMap);
                output.treePhase = wind.treePhase;
                
                // retain the mesh's tangent basis for this small-displacement wind effect
                // tangent handedness and mirrored transforms determine the bitangent sign
                output.worldNormal = UnityObjectToWorldNormal(v.normal);
                output.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                output.worldBitangent = cross(output.worldNormal, output.worldTangent) * (v.tangent.w * unity_WorldTransformParams.w);
                
                // shadow lookup and fog must use the displaced vertex position
                TRANSFER_SHADOW_WPOS(output, deformedWorldPos);
                UNITY_TRANSFER_FOG(output, output.pos);
                return output;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // albedo (color)
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                if (_IsLeaf >= 1.0)
                {
                    clip(albedo.a - _Cutoff);
                }
                float fault = LeafGlitchMask(i.worldPos, i.treePhase);
                float missing = LeafGlitchMissing(i.treePhase);
                clip(0.58 - fault * missing);

                half3 tangentNormal = UnpackNormal(tex2D(_BumpMap, i.bumpUV));
                tangentNormal.xy *= _NormalScale;
                tangentNormal = normalize(tangentNormal);

                half3 worldNormal = normalize(
                    normalize(i.worldTangent) * tangentNormal.x +
                    normalize(i.worldBitangent) * tangentNormal.y +
                    normalize(i.worldNormal) * tangentNormal.z);

                if (_IsLeaf >= 1.0 && facing < 0.0)
                {
                    worldNormal = -worldNormal;
                }

                half3 lightDirection = normalize(UnityWorldSpaceLightDir(i.worldPos));
                half3 viewDirection = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half nDotL = Clamp01(dot(worldNormal, lightDirection));
                UNITY_LIGHT_ATTENUATION(attenuation, i, i.worldPos);
                
                // Blinn-Phong light model
                // ambient + diffuse + specular      
                
                // ambinet 
                half3 ambient = albedo.rgb * ShadeSH9(half4(worldNormal, 1.0));
                
                // diffuse
                half3 diffuse = albedo.rgb * _LightColor0.rgb * nDotL * attenuation;
                
                // specular
                half specularPower = lerp(BroadHighlightExponent, TightHighlightExponent, _Smoothness);
                half specularFactor = pow(Clamp01(dot(worldNormal, halfDirection)), specularPower);
                specularFactor *= _SpecularStrength * nDotL * attenuation;
                half3 specular = _LightColor0.rgb * specularFactor;

                // A subtle cyan rim ties foliage into the archive lighting while
                // retaining spherical-harmonic ambient light and real shadows.
                half rim = pow(1.0 - Clamp01(dot(worldNormal, viewDirection)), _RimPower);
                half3 archiveRim = _RimColor.rgb * rim * _RimStrength;

                half3 faultColor = lerp(_LeafGlitchColor.rgb, half3(0.75, 0.15, 0.6),
                    step(0.55, frac(i.worldPos.y * 3.7 + i.treePhase)));
                half3 faultEdge = faultColor * fault * (1.0 - missing * 0.6) * 2.2;
                fixed4 outputColor = fixed4(ambient + diffuse + specular + archiveRim + faultEdge, albedo.a);

                UNITY_APPLY_FOG(i.fogCoord, outputColor);
                return outputColor;
            }
            ENDCG
        }

        // shadow depth: reuse wind and alpha cutout so the shadow matches the tree
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float treePhase : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2fShadow vertShadow(TreeAppData v)
            {
                v2fShadow output;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2fShadow, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                TreeWindData wind = PrepareTreeWindData(v.vertex);

                float3 deformedWorldPos = CalculateWindPosition(wind, v.normal);
                // Unity's shadow macro expects object-space input and applies depth bias
                v.vertex.xyz = mul(unity_WorldToObject, float4(deformedWorldPos, 1.0)).xyz;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(output)
                output.uv = TRANSFORM_TEX(v.uv, _MainTex);
                output.worldPos = deformedWorldPos;
                output.treePhase = wind.treePhase;
                return output;
            }

            float4 fragShadow(v2fShadow i) : SV_Target
            {
                // discard the same leaf texels as the visible pass before writing depth
                if (_IsLeaf >= 1.0)
                {
                    clip(tex2D(_MainTex, i.uv).a * _Color.a - _Cutoff);
                }
                float fault = LeafGlitchMask(i.worldPos, i.treePhase);
                clip(0.58 - fault * LeafGlitchMissing(i.treePhase));
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    Fallback Off
}
