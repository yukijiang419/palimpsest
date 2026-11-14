Shader "Unlit/ShieldShader"
{
    Properties
    {
        //shield color and rim response
        _RimColor ("Rim Color", Color) = (0, 0, 1, 1)
        _RimPower ("Rim Power", Range(0, 10)) = 7
        _ShieldStrength ("Shield Strength", Range(0, 1)) = 1
        _DripSpeed ("Drip Speed", Range(0, 3)) = 0.3
        _DripLength ("Drip Length", Range(0, 0.5)) = 0.35
        _DripDetail ("Drip Count", Range(4, 40)) = 16
        _WobbleAmount ("Wobble Amount", Range(0, 0.08)) = 0.02
        _FillScale ("Shield Fill Scale", Range(1.0, 1.5)) = 1.05
        //dissolve when the shield breaks
        _DissolveScale ("Dissolve Noise Scale", Range(1, 20)) = 6
        _DissolveEdgeWidth ("Dissolve Edge Width", Range(0.01, 0.3)) = 0.08
        _DissolveEdgeColor ("Dissolve Edge Color", Color) = (0.35, 0.8, 1, 1)
        _DissolveEdgeIntensity ("Dissolve Edge Intensity", Range(1, 6)) = 3
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        //warning when the shield is almost gone
        _LowShieldThreshold ("Low Shield Threshold", Range(0.05, 1)) = 0.5
        _LowShieldColor ("Low Shield Color", Color) = (1, 0.15, 0.1, 1)
        _FlickerSpeed ("Flicker Speed", Range(1, 30)) = 12
        _FlickerStrength ("Flicker Strength", Range(0, 1)) = 0.85
        [HideInInspector] _ImpactDirection ("Impact direction", Vector) = (0, 1, 0, 0)
        [HideInInspector] _ImpactLocalized ("Localized impact", Float) = 0
        [HideInInspector] _ImpactProgress ("Impact progress", Float) = 0
        [HideInInspector] _ImpactStrength ("Impact strength", Float) = 0
        [HideInInspector] _ImpactColor ("Impact colour", Color) = (.3, .85, 1, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        //add the shield glow over the scene and add light
        Blend SrcAlpha One

        //do not write depth, so objects behind the shield are still drawn
        ZWrite Off

        //render the outside surface only
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _RimColor;
            float _RimPower;
            float _ShieldStrength;
            float _DripLength;
            float _DripDetail;
            float _DripSpeed;
            float _WobbleAmount;
            float _FillScale;
            float3 _ImpactDirection;
            float3 _ImpactColor;
            float _ImpactLocalized, _ImpactProgress, _ImpactStrength;
            float _Dissolve;
            float _DissolveScale;
            float _DissolveEdgeWidth;
            float4 _DissolveEdgeColor;
            float _DissolveEdgeIntensity;
            float _LowShieldThreshold;
            float4 _LowShieldColor;
            float _FlickerSpeed;
            float _FlickerStrength;

            // Hash and value-noise references: The Book of Shaders by Patricio Gonzalez Vivo and Jen Lowe.
            // https://thebookofshaders.com/10/ and https://thebookofshaders.com/11/
            // Adapted to 3D and layered/remapped for shield dissolve and low-shield flicker.
            //random value between 0 and 1 based on a 3D position
            float hash3(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            //smooth value noise between grid points
            float valueNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                //get a random value at each of the 8 cell corners
                float n000 = hash3(cell);
                float n100 = hash3(cell + float3(1, 0, 0));
                float n010 = hash3(cell + float3(0, 1, 0));
                float n110 = hash3(cell + float3(1, 1, 0));
                float n001 = hash3(cell + float3(0, 0, 1));
                float n101 = hash3(cell + float3(1, 0, 1));
                float n011 = hash3(cell + float3(0, 1, 1));
                float n111 = hash3(cell + float3(1, 1, 1));

                //interpolate between the corner values to get a smooth noise value
                float bottom= lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y);
                float top = lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y);
                return lerp(bottom, top, f.z);
            }

            //noise used to decide which parts of the shield burn first
            float dissolveNoise(float3 p)
            {
                //combine a large and a small layer of noise so the holes have detail
                float n = valueNoise(p) * 0.6 + valueNoise(p * 2.5 + 18.0) * 0.4;
                //spread the values out so the shield burns at an even speed
                return saturate((n - 0.2) / 0.6);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldnomal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float localY : TEXCOORD2;
                float3 localPos : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;

                //wobble the surface along the normals
                float wobble = sin(v.vertex.y * 6.0 + _Time.y * 2.0) * cos(v.vertex.x * 4.3 + _Time.y * 1.4);
                float4 displaced = v.vertex;
                displaced.xyz += v.normal * wobble * _WobbleAmount;

                //transform from object space to clip space
                o.pos = UnityObjectToClipPos(displaced);
                o.worldnomal = UnityObjectToWorldNormal(v.normal);

                //direction from the surface to the camera
                float3 worldPos = mul(unity_ObjectToWorld, displaced).xyz;
                o.viewDir = UnityWorldSpaceViewDir(worldPos);
                o.localY = displaced.y + 0.5;
                o.localPos = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                //cut the shield at the remaining shield height; draw the full shell when full or dissolving
                float edge = (_ShieldStrength >= 0.999 || _Dissolve > 0.001) ? _FillScale : _ShieldStrength;
                clip(edge - i.localY);

                //Fresnel rim light
                float3 n = normalize(i.worldnomal);
                float3 v = normalize(i.viewDir);
                float fresnel = pow(1.0 - saturate(dot(n, v)), _RimPower);

                //bright line at the shield level
                float waterline = smoothstep(edge - 0.04, edge, i.localY);

                //split the shield into vertical columns for the droplets
                float angle = atan2(i.localPos.z, i.localPos.x);
                float detail = floor(_DripDetail);
                float colCoord = (angle + UNITY_PI) / (2.0 * UNITY_PI) * detail;
                float columnId = floor(colCoord);
                float inCol = frac(colCoord) - 0.5;

                //give each column a random offset so the droplets fall out of sync
                float seed = frac(sin(columnId * 12.9898) * 43758.5453);
                float phase = frac(_Time.y * _DripSpeed + seed);
                float below = edge - i.localY;

                //draw a droplet sliding down below the shield level
                float bead = smoothstep(0.03, 0.0, abs(below - phase * _DripLength));
                bead *= smoothstep(0.25, 0.0, abs(inCol));
                bead *= 1.0 - phase;

                //burn holes in the shield with noise to dissolve it
                float burn = dissolveNoise(i.localPos * _DissolveScale) - _Dissolve * 1.05;
                clip(burn);

                //glowing edge around the burnt holes
                float burnEdge = (1.0 - smoothstep(0.0, _DissolveEdgeWidth, burn)) * step(0.001, _Dissolve);

                //warning when the shield is low
                float lowShield = 1.0 - smoothstep(_LowShieldThreshold * 0.6, _LowShieldThreshold, _ShieldStrength);
                //no warning while the shield is dissolving
                lowShield *= 1.0 - step(0.001, _Dissolve);
                //blend the rim colour towards the warning colour
                float3 shieldColor = lerp(_RimColor.rgb, _LowShieldColor.rgb, lowShield);

                //make the warning flicker like a failing light
                float flickerTime = _Time.y * _FlickerSpeed;
                float flickerNoise = valueNoise(float3(flickerTime, 0.0, 0.0)) * 0.6
                    + valueNoise(float3(flickerTime * 2.7, 5.0, 0.0)) * 0.4;
                //sharpen the noise into on/off cuts like a failing light
                float flickerPulse = smoothstep(0.35, 0.55, flickerNoise);
                float flicker = lerp(1.0, flickerPulse, _FlickerStrength * lowShield);

                float result = saturate(fresnel + waterline * 0.9 + bead * 0.8) * flicker;
                float hitDistance = distance(normalize(i.localPos), _ImpactDirection);
                float ripple = 1 - smoothstep(.03, .16, abs(hitDistance - _ImpactProgress * 1.8));
                float impact = lerp(.55, ripple, _ImpactLocalized) * _ImpactStrength;
                float3 color = shieldColor * flicker + _ImpactColor * impact * .8 + _DissolveEdgeColor.rgb * burnEdge * _DissolveEdgeIntensity;
                return fixed4(color, saturate(result + impact + burnEdge));
            }

            ENDCG
        }
    }
}
