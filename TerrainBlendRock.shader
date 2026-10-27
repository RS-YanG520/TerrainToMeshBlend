Shader "Custom/TerrainBlendRock"
{
    Properties
    {
        // —— 石头自身 PBR（与 Standard 属性名一致，切换 shader 后贴图自动保留）——
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5

        // —— 地形混合（以下由 TerrainToMeshBlend 组件自动设置）——
        _BlendRange ("Blend Range", Float) = 0.4
        _BlendStrength ("Blend Strength", Range(0,1)) = 1.0
        _TerrainBlendAmount ("Terrain Blend Amount", Range(0,1)) = 1.0
        _BlendMaskContrast ("Blend Mask Contrast", Float) = 1.0

        _NoiseScale ("Noise Scale", Float) = 2.0
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.5

        [HideInInspector] _DebugBlendMask ("Debug Blend Mask", Float) = 0

        [HideInInspector] _ControlMap ("Control Map", 2D) = "white" {}
        [HideInInspector] _Splat0 ("Splat0", 2D) = "white" {}
        [HideInInspector] _Splat1 ("Splat1", 2D) = "white" {}
        [HideInInspector] _Splat2 ("Splat2", 2D) = "white" {}
        [HideInInspector] _Splat3 ("Splat3", 2D) = "white" {}
        [HideInInspector] _SplatNormal0 ("SplatNormal0", 2D) = "bump" {}
        [HideInInspector] _SplatNormal1 ("SplatNormal1", 2D) = "bump" {}
        [HideInInspector] _SplatNormal2 ("SplatNormal2", 2D) = "bump" {}
        [HideInInspector] _SplatNormal3 ("SplatNormal3", 2D) = "bump" {}
        [HideInInspector] _SplatNormalScale0 ("SplatNormalScale0", Float) = 1
        [HideInInspector] _SplatNormalScale1 ("SplatNormalScale1", Float) = 1
        [HideInInspector] _SplatNormalScale2 ("SplatNormalScale2", Float) = 1
        [HideInInspector] _SplatNormalScale3 ("SplatNormalScale3", Float) = 1
        [HideInInspector] _SplatScale0 ("SplatScale0", Vector) = (1,1,0,0)
        [HideInInspector] _SplatScale1 ("SplatScale1", Vector) = (1,1,0,0)
        [HideInInspector] _SplatScale2 ("SplatScale2", Vector) = (1,1,0,0)
        [HideInInspector] _SplatScale3 ("SplatScale3", Vector) = (1,1,0,0)
        [HideInInspector] _TerrainPos ("TerrainPos", Vector) = (0,0,0,0)
        [HideInInspector] _TerrainSize ("TerrainSize", Vector) = (1000,1,1000,0)
        [HideInInspector] _BlendStartY ("BlendStartY", Float) = 0
        [HideInInspector] _BlendEnabled ("Blend Enabled", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_local __ _NORMALMAP
        #pragma multi_compile_local __ _METALLICGLOSSMAP

        sampler2D _MainTex;
        sampler2D _BumpMap;
        float _BumpScale;
        sampler2D _MetallicGlossMap;
        half _Metallic;
        half _Glossiness;
        fixed4 _Color;

        sampler2D _ControlMap;
        sampler2D _Splat0, _Splat1, _Splat2, _Splat3;
        sampler2D _SplatNormal0, _SplatNormal1, _SplatNormal2, _SplatNormal3;
        float4 _SplatScale0, _SplatScale1, _SplatScale2, _SplatScale3;
        float _SplatNormalScale0, _SplatNormalScale1, _SplatNormalScale2, _SplatNormalScale3;
        float4 _TerrainPos;
        float4 _TerrainSize;
        float _BlendStartY;
        float _BlendEnabled;
        float _BlendRange;
        float _BlendStrength;
        float _TerrainBlendAmount;
        float _BlendMaskContrast;
        float _NoiseScale;
        float _NoiseAmount;
        float _DebugBlendMask;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            INTERNAL_DATA
        };

        // 用世界坐标采样地形 splat 混合色（前 4 层）
        fixed4 SampleTerrainColor(float3 worldPos)
        {
            float2 controlUV = (worldPos.xz - _TerrainPos.xz) / max(_TerrainSize.xz, float2(0.001, 0.001));
            fixed4 weights = tex2D(_ControlMap, controlUV);

            float total = weights.r + weights.g + weights.b + weights.a;
            if (total <= 0.0001) total = 1.0;
            weights /= total;

            float2 uv0 = worldPos.xz / max(_SplatScale0.xy, float2(0.001, 0.001));
            float2 uv1 = worldPos.xz / max(_SplatScale1.xy, float2(0.001, 0.001));
            float2 uv2 = worldPos.xz / max(_SplatScale2.xy, float2(0.001, 0.001));
            float2 uv3 = worldPos.xz / max(_SplatScale3.xy, float2(0.001, 0.001));

            fixed4 c = 0;
            c += weights.r * tex2D(_Splat0, uv0);
            c += weights.g * tex2D(_Splat1, uv1);
            c += weights.b * tex2D(_Splat2, uv2);
            c += weights.a * tex2D(_Splat3, uv3);
            return c;
        }

        // 采样地形 splat 法线（前 4 层），合成地面法线贴图
        float3 SampleTerrainNormal(float3 worldPos)
        {
            float2 controlUV = (worldPos.xz - _TerrainPos.xz) / max(_TerrainSize.xz, float2(0.001, 0.001));
            fixed4 weights = tex2D(_ControlMap, controlUV);

            float total = weights.r + weights.g + weights.b + weights.a;
            if (total <= 0.0001) total = 1.0;
            weights /= total;

            float2 uv0 = worldPos.xz / max(_SplatScale0.xy, float2(0.001, 0.001));
            float2 uv1 = worldPos.xz / max(_SplatScale1.xy, float2(0.001, 0.001));
            float2 uv2 = worldPos.xz / max(_SplatScale2.xy, float2(0.001, 0.001));
            float2 uv3 = worldPos.xz / max(_SplatScale3.xy, float2(0.001, 0.001));

            float3 n0 = UnpackScaleNormal(tex2D(_SplatNormal0, uv0), _SplatNormalScale0);
            float3 n1 = UnpackScaleNormal(tex2D(_SplatNormal1, uv1), _SplatNormalScale1);
            float3 n2 = UnpackScaleNormal(tex2D(_SplatNormal2, uv2), _SplatNormalScale2);
            float3 n3 = UnpackScaleNormal(tex2D(_SplatNormal3, uv3), _SplatNormalScale3);

            // 按 splat 权重混合切线空间法线 xy，重算 z（标准地形法线混合）
            float3 n;
            n.xy = n0.xy * weights.r + n1.xy * weights.g + n2.xy * weights.b + n3.xy * weights.a;
            n.z = sqrt(max(1e-5, 1.0 - saturate(dot(n.xy, n.xy))));
            return n;
        }

        // 世界空间平铺的程序化 value noise，让过渡边界更随机斑驳
        float TerrainNoiseHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float TerrainValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(TerrainNoiseHash(i), TerrainNoiseHash(i + float2(1, 0)), u.x),
                        lerp(TerrainNoiseHash(i + float2(0, 1)), TerrainNoiseHash(i + float2(1, 1)), u.x), u.y);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 rockColor = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 terrainColor = SampleTerrainColor(IN.worldPos);

            // 混合遮罩范围：石头底部向上 blendRange 米内从石头色渐变到地形色
            float h = IN.worldPos.y - _BlendStartY;
            float heightMask = 1.0 - saturate(h / max(_BlendRange, 0.001));

            // Noise 遮罩：叠加世界空间噪声，让过渡边界随机斑驳
            float terrainNoise = TerrainValueNoise(IN.worldPos.xz / max(_NoiseScale, 0.0001));
            float noiseMask = lerp(1.0, terrainNoise, saturate(_NoiseAmount));

            float blendMask = saturate(heightMask * noiseMask) * saturate(_TerrainBlendAmount) * saturate(_BlendStrength) * saturate(_BlendEnabled);
            blendMask = saturate((blendMask - 0.5) * _BlendMaskContrast + 0.5);

            o.Albedo = lerp(rockColor.rgb, terrainColor.rgb, blendMask);

            half rockMetallic = _Metallic;
            half rockSmoothness = _Glossiness;
            #ifdef _METALLICGLOSSMAP
                fixed4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
                rockMetallic = mg.r;
                rockSmoothness = mg.a * _Glossiness;
            #endif
            o.Metallic = lerp(rockMetallic, 0.0, blendMask);
            o.Smoothness = lerp(rockSmoothness, 0.1, blendMask);

            // 石头法线贴图
            #ifdef _NORMALMAP
                float3 rockNormal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            #else
                float3 rockNormal = float3(0, 0, 1);
            #endif

            // 过渡区混合石头法线贴图与地面法线贴图
            float3 groundNormal = SampleTerrainNormal(IN.worldPos);
            o.Normal = normalize(lerp(rockNormal, groundNormal, blendMask));

            // Debug：把过渡区遮罩显示为自发光黑白图(白=混合过渡区，黑=非过渡区)
            if (_DebugBlendMask > 0.5)
            {
                o.Albedo = 0;
                o.Emission = blendMask.xxx;
                o.Metallic = 0.0;
                o.Smoothness = 0.0;
            }

            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
