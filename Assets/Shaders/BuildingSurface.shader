Shader "Caffeine/Building Surface"
{
    Properties
    {
        // surface appearance
        _SurfaceTex ("Surface Albedo", 2D) = "white" {}
        _RoughnessTex ("Surface Roughness", 2D) = "white" {}
        
        // surface color
        _SurfaceColor ("Surface Color", Color) = (1, 1, 1, 1)
        _RoofColor ("Roof Color", Color) = (0.42, 0.42, 0.42, 1)
        
        // window with lighting
        _LitWindowColor ("Lit Window Color", Color) = (1, 0.58, 0.24, 1)
        _SurfaceTiling ("Surface Tiling (tiles/metre)", Range(0.02, 2)) = 0.5

        // procedural window layout and interior
        _WindowWidth ("Window Cell Width (m)", Range(1, 8)) = 2.8
        _FloorHeight ("Floor Height (m)", Range(2, 8)) = 3.2
        _FrameRatio ("Window Frame Ratio", Range(0.02, 0.35)) = 0.14
        _InteriorDepth ("Interior Depth (m)", Range(0.5, 10)) = 4
        _LitWindowRatio ("Lit Window Ratio", Range(0, 1)) = 0.1

        // surface classification and response
        _RoofNormalThreshold ("Roof Normal Threshold", Range(0.5, 0.95)) = 0.7
        _GlassSmoothness ("Glass Smoothness", Range(0, 1)) = 0.82
        _WallSmoothness ("Wall Smoothness", Range(0, 1)) = 0.18

        // shared archive-lighting accents
        [HDR] _AtmosphereColor ("Atmosphere Fill", Color) = (0.035, 0.055, 0.09, 1)
        _AtmosphereStrength ("Atmosphere Strength", Range(0, 1)) = 0.16
        [HDR] _RimColor ("Archive Rim", Color) = (0.12, 0.68, 0.9, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.08
        _RimPower ("Rim Tightness", Range(0.5, 8)) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _SurfaceTex;
        sampler2D _RoughnessTex;
        fixed4 _SurfaceColor;
        fixed4 _RoofColor;
        fixed4 _LitWindowColor;
        half _SurfaceTiling;
        half _WindowWidth;
        half _FloorHeight;
        half _FrameRatio;
        half _InteriorDepth;
        half _LitWindowRatio;
        half _RoofNormalThreshold;
        half _GlassSmoothness;
        half _WallSmoothness;
        fixed4 _AtmosphereColor;
        fixed4 _RimColor;
        half _AtmosphereStrength;
        half _RimStrength;
        half _RimPower;

        static const float NumericEpsilon = 0.0001;
        static const float ProjectionSharpness = 8.0;
        static const float WindowEdgeSoftness = 0.015;
        static const float RoomLightHashOffset = 19.37;
        static const float RoughnessInfluence = 0.2;
        static const float WindowOcclusion = 0.82;
        static const float EmissionStrength = 0.45;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
        };

        // stable per-room random value
        // source: https://github.com/tixl3d/Resources/blob/master/hash-functions.hlsl
        float StableHash(float2 value)
        {
            value = frac(value * float2(0.1031, 0.1030));
            value += dot(value, value.yx + 33.33);
            return frac((value.x + value.y) * value.x);
        }

        // world-space triplanar texture projection
        fixed4 SampleWorldTexture(sampler2D textureSampler, float3 position, float3 normal)
        {
            float3 weights = pow(abs(normal), ProjectionSharpness);
            weights /= max(weights.x + weights.y + weights.z, NumericEpsilon);

            fixed4 xProjection = tex2D(textureSampler, position.zy * _SurfaceTiling);
            fixed4 yProjection = tex2D(textureSampler, position.xz * _SurfaceTiling);
            fixed4 zProjection = tex2D(textureSampler, position.xy * _SurfaceTiling);
            return xProjection * weights.x + yProjection * weights.y + zProjection * weights.z;
        }

        // ray-box interior mapping for one normalized room cell
        fixed3 TraceRoom(float2 cellUv, float3 rayDirection, float roomHash)
        {
            float3 origin = float3(cellUv, 0.0);
            float3 direction = rayDirection;
            direction.z = max(direction.z, NumericEpsilon);

            // find the first wall reached by the view ray
            float backTime = 1.0 / direction.z;
            float sideTime = direction.x >= 0.0
                ? (1.0 - origin.x) / max(direction.x, NumericEpsilon)
                : (0.0 - origin.x) / min(direction.x, -NumericEpsilon);
            float verticalTime = direction.y >= 0.0
                ? (1.0 - origin.y) / max(direction.y, NumericEpsilon)
                : (0.0 - origin.y) / min(direction.y, -NumericEpsilon);

            float hitTime = min(backTime, min(sideTime, verticalTime));
            float3 hit = origin + direction * max(hitTime, 0.0);
            float hitBack = step(backTime, min(sideTime, verticalTime));
            float hitSide = (1.0 - hitBack) * step(sideTime, verticalTime);
            float hitVertical = 1.0 - hitBack - hitSide;

            // shade the back, side and vertical surfaces differently to create depth
            fixed3 baseWall = lerp(fixed3(0.19, 0.21, 0.23), fixed3(0.34, 0.29, 0.24), roomHash);
            fixed3 backWall = baseWall * (0.68 + 0.14 * step(0.5, frac(hit.x * 3.0 + roomHash)));
            fixed3 sideWall = baseWall * 0.52;
            fixed3 floorCeiling = lerp(baseWall * 0.32, baseWall * 0.76, step(0.0, direction.y));

            float backPanel = step(0.94, frac(hit.x * 3.0 + roomHash * 7.0));
            backWall = lerp(backWall, fixed3(0.055, 0.06, 0.065), backPanel * step(0.12, hit.y) * step(hit.y, 0.84));
            return backWall * hitBack + sideWall * hitSide + floorCeiling * hitVertical;
        }

        void surf(Input IN, inout SurfaceOutputStandard output)
        {
            // sample the surface and separate horizontal roof surfaces
            float3 normal = normalize(IN.worldNormal);
            float3 surfaceSample = SampleWorldTexture(_SurfaceTex, IN.worldPos, normal).rgb;
            float roughness = SampleWorldTexture(_RoughnessTex, IN.worldPos, normal).r;
            float roofMask = step(_RoofNormalThreshold, abs(normal.y));

            // build a metre-scaled window grid in world space
            float3 horizontalNormal = normalize(float3(normal.x, 0.0, normal.z) + float3(NumericEpsilon, 0.0, NumericEpsilon));
            float3 tangent = normalize(cross(float3(0.0, 1.0, 0.0), horizontalNormal));
            float surfaceU = dot(IN.worldPos, tangent);
            float surfaceV = IN.worldPos.y;
            float2 cellCoordinate = float2(floor(surfaceU / _WindowWidth), floor(surfaceV / _FloorHeight));
            float2 cellUv = frac(float2(surfaceU / _WindowWidth, surfaceV / _FloorHeight));

            float2 borderDistance = min(cellUv, 1.0 - cellUv);
            float frameEdge = min(borderDistance.x, borderDistance.y);
            float windowMask = smoothstep(
                _FrameRatio - WindowEdgeSoftness,
                _FrameRatio + WindowEdgeSoftness,
                frameEdge) * (1.0 - roofMask);

            // transform the view ray into room space and trace the interior
            float3 rayIntoBuilding = -normalize(IN.viewDir);
            float3 roomRay = float3(
                dot(rayIntoBuilding, tangent) * (_InteriorDepth / _WindowWidth),
                rayIntoBuilding.y * (_InteriorDepth / _FloorHeight),
                dot(rayIntoBuilding, -horizontalNormal));
            float roomHash = StableHash(cellCoordinate);
            fixed3 roomColor = TraceRoom(cellUv, roomRay, roomHash);

            fixed3 surfaceColor = surfaceSample * _SurfaceColor.rgb;
            fixed3 roofColor = surfaceSample * _RoofColor.rgb;
            fixed3 wallAndWindow = lerp(surfaceColor, roomColor, windowMask);

            // choose a stable subset of rooms for emissive windows
            float litWindow = step(1.0 - _LitWindowRatio, roomHash) * windowMask;
            float lightingVariation = 0.65 + 0.35 * StableHash(cellCoordinate + RoomLightHashOffset);

            output.Albedo = lerp(wallAndWindow, roofColor, roofMask);
            output.Metallic = 0.0;
            output.Smoothness = lerp(
                saturate(_WallSmoothness + (1.0 - roughness) * RoughnessInfluence),
                _GlassSmoothness,
                windowMask);
            output.Occlusion = lerp(1.0, WindowOcclusion, windowMask);
            // Match general materials with a restrained cool fill and edge light.
            // Lit windows remain the dominant source of building emission.
            float rim = pow(1.0 - saturate(dot(normal, normalize(IN.viewDir))), _RimPower);
            fixed3 archiveLight = _AtmosphereColor.rgb * _AtmosphereStrength
                + _RimColor.rgb * rim * _RimStrength;
            output.Emission = _LitWindowColor.rgb * litWindow * lightingVariation * EmissionStrength
                + archiveLight;
            output.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Standard"
}
