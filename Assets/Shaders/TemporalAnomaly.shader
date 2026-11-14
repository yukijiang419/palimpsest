Shader "Caffeine/Temporal Anomaly"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HDR] _AnomalyEdgeColor ("Fault Edge", Color) = (0.28, 0.22, 0.5, 1)
        _AnomalyPhase ("Phase", Float) = 0
        _AnomalyInterval ("Interval (seconds)", Float) = 7
        _AnomalyDuration ("Burst (seconds)", Float) = 0.25
        _AnomalyOffset ("Band Offset (metres)", Float) = 0.08
        _AnomalyFullBlink ("Whole Model Blink", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }
        LOD 250
        Cull Back

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        fixed4 _Color;
        half3 _AnomalyEdgeColor;
        float _AnomalyPhase;
        float _AnomalyInterval;
        float _AnomalyDuration;
        float _AnomalyOffset;
        float _AnomalyFullBlink;
        float _TemporalAnomalyClock;
        float _TemporalAnomalyEnabled;

        float CycleTime()
        {
            float interval = max(_AnomalyInterval, 0.1);
            return frac((_TemporalAnomalyClock + _AnomalyPhase) / interval) * interval;
        }

        float Burst()
        {
            return _TemporalAnomalyEnabled * (1.0 - step(_AnomalyDuration, CycleTime()));
        }

        // use two short gaps during each burst
        float MissingPulse()
        {
            float t = CycleTime();
            float first = step(0.12 * _AnomalyDuration, t) *
                (1.0 - step(0.48 * _AnomalyDuration, t));
            float second = step(0.64 * _AnomalyDuration, t) *
                (1.0 - step(0.79 * _AnomalyDuration, t));
            return Burst() * saturate(first + second);
        }

        float Stripe(float worldY)
        {
            float band = frac(worldY * 7.0 + _AnomalyPhase * 0.37);
            return step(0.22, band) * (1.0 - step(0.67, band));
        }

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        // shift horizontal bands in world space
        void vert(inout appdata_full v)
        {
            float worldY = mul(unity_ObjectToWorld, v.vertex).y;
            float band = Stripe(worldY);
            float direction = step(0.5, frac(worldY * 3.1 + _AnomalyPhase)) * 2.0 - 1.0;
            float3 worldShift = float3(Burst() * band * direction * _AnomalyOffset, 0, 0);
            v.vertex.xyz += mul((float3x3)unity_WorldToObject, worldShift);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            float stripe = Stripe(IN.worldPos.y);
            // clip missing bands, or the whole model when full blink is enabled
            float missing = MissingPulse() * lerp(stripe, 1.0, _AnomalyFullBlink);
            clip(min(albedo.a - 0.35, 0.5 - missing));

            o.Albedo = albedo.rgb;
            o.Metallic = 0.12;
            o.Smoothness = 0.32;
            o.Emission = _AnomalyEdgeColor * Burst() * stripe *
                (1.0 - MissingPulse()) * 0.7;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
