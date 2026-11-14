Shader "Caffeine/Temporal Reconstruction"
{
    Properties
    {
        _MainTex ("Surface", 2D) = "white" {}
        _Color ("Surface Tint", Color) = (.62,.60,.65,1)
        _AnomalyEdgeColor ("Reconstruction Edge", Color) = (.32,.21,.46,1)
        _AnomalyInterval ("Cycle Seconds", Float) = 6
        _AnomalyDuration ("Reconstruction Seconds", Float) = 1
        _AnomalyOffset ("Band Offset Metres", Float) = .06
        _AnomalyPhase ("Phase Seconds", Float) = 0
        [HideInInspector] _AnomalyFullBlink ("Legacy Property (Unused)", Float) = 0
        _FragmentCoverage ("Signal Integrity", Range(.55,1)) = .86
        [HideInInspector] _PassThroughHint ("Approach Hint", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _Color;
        float3 _AnomalyEdgeColor;
        float _AnomalyInterval, _AnomalyDuration, _AnomalyOffset, _AnomalyPhase, _FragmentCoverage;
        float _TemporalAnomalyClock, _TemporalAnomalyEnabled;
        float _PassThroughHint;
        float CycleTime()
        {
            float interval = max(.1, _AnomalyInterval);
            return frac((_TemporalAnomalyClock + _AnomalyPhase) / interval) * interval;
        }
        float Hash(float value) { return frac(sin(value * 12.9898) * 43758.5453); }
        float Glitch()
        {
            float t = CycleTime();
            float u = saturate(t / max(.1, _AnomalyDuration));
            float envelope = smoothstep(0, .06, u) * (1 - smoothstep(.84, 1, u));
            float tick = floor((_TemporalAnomalyClock + _AnomalyPhase) * 12);
            return max(_PassThroughHint, _TemporalAnomalyEnabled * envelope * (.4 + .6 * step(.35, Hash(tick))));
        }
        float BandShift(float worldY)
        {
            float tick = floor((_TemporalAnomalyClock + _AnomalyPhase) * 9);
            float row = floor(worldY * 3.5);
            float random = Hash(row * 7.13 + tick * 3.7 + _AnomalyPhase);
            return (random * 2 - 1) * step(.48, random) * Glitch();
        }
        float3 ShiftedWorld(float4 vertex)
        {
            float3 world = mul(unity_ObjectToWorld, vertex).xyz;
            // Camera-right displacement reads as a torn screen scanline from any approach.
            world += UNITY_MATRIX_V[0].xyz * BandShift(world.y) * _AnomalyOffset * 2;
            return world;
        }
        struct GhostInput { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct GhostOutput { float4 position : SV_POSITION; float worldY : TEXCOORD0; float side : TEXCOORD1; };
        GhostOutput GhostVertex(GhostInput input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            GhostOutput output;
            float3 world = ShiftedWorld(input.vertex);
            float side = step(.5, Hash(floor(world.y * 3.5) + _AnomalyPhase)) * 2 - 1;
            world += UNITY_MATRIX_V[0].xyz * side * _AnomalyOffset * Glitch() * 1.5;
            output.position = mul(UNITY_MATRIX_VP, float4(world, 1));
            output.worldY = world.y;
            output.side = side;
            return output;
        }
        float GhostAlpha(float y)
        {
            float row = frac(y * 5 + _TemporalAnomalyClock * 1.7);
            return Glitch() * (.10 + .25 * step(.7, row));
        }
        fixed4 GhostFragment(GhostOutput input) : SV_Target
        {
            float a=GhostAlpha(input.worldY); clip(a-.001);
            return fixed4(lerp(float3(.02,.8,1),float3(1,.05,.3),step(0,input.side)),a);
        }
        ENDCG

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard vertex:vert
        #pragma multi_compile_instancing
        struct Input { float2 uv_MainTex; float3 worldPos; };
        void vert(inout appdata_full v)
        { v.vertex = mul(unity_WorldToObject, float4(ShiftedWorld(v.vertex), 1)); }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float glitch = Glitch();
            clip(.65 - _PassThroughHint * step(.35, frac(IN.worldPos.y * 4)));
            float shift = BandShift(IN.worldPos.y);
            float2 uv = IN.uv_MainTex + float2(shift * .035, 0);
            float split = glitch * .008;
            fixed3 tex = fixed3(tex2D(_MainTex, uv + float2(split,0)).r,
                tex2D(_MainTex, uv).g, tex2D(_MainTex, uv - float2(split,0)).b) * _Color.rgb;
            float scan = step(.78, frac(IN.worldPos.y * 12 - _TemporalAnomalyClock * 2.5));
            float block = Hash(floor(IN.worldPos.y * 8) + floor(IN.worldPos.x * 5) * 13
                + floor(_TemporalAnomalyClock * 12) * 7);
            float corrupt = step(.86, block) * glitch;
            o.Albedo = tex * (1 - glitch * scan * .38 - corrupt * .25);
            o.Metallic = .16;
            o.Smoothness = .27;
            float3 channel = lerp(float3(.06,.55,.7),float3(.7,.07,.36),step(.5,block));
            o.Emission = _AnomalyEdgeColor * (.06 + scan * glitch * .5)
                + channel * corrupt * (.35 + 1 - _FragmentCoverage)
                + float3(.1, 1.8, 2.4) * _PassThroughHint;
            o.Alpha = 1;
        }
        ENDCG
        // Chromatic silhouettes separate briefly; the opaque original remains visible.
        Pass
        {
            Name "CHANNEL_SEPARATION"
            Tags { "LightMode"="Always" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex GhostVertex
            #pragma fragment GhostFragment
            #pragma multi_compile_instancing
            ENDCG
        }
    }
    Fallback "Diffuse"
}
