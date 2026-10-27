using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 让石头等模型底部自动采样地形 splat 材质，实现与地形的自然过渡。
///
/// 使用步骤：
///   1. 把石头材质的 Shader 换成 Custom/TerrainBlendRock（贴图会自动保留）。
///   2. 在石头根节点（或地形上）挂上本组件，拖入 Terrain 和石头 Renderer。
///   3. 调整 Blend Range（混合遮罩范围）控制过渡带大小。
///
/// 说明：
///   - 只采样地形的前 4 层 splat（RGBA 控制图）。
///   - 过渡按高度：石头底部向上 Blend Range 米内逐渐融入地形。
///   - Match Ground Normal 会修改网格的「顶点法线」（几何法线，非贴图法线），
///     让接触地面的顶点法线朝地面法线过渡，光照更自然。
///   - Debug Normal 在场景视图里画出顶点法线线轴（绿色，类似 Maya 的法线显示）。
/// </summary>
[ExecuteAlways]
public class TerrainToMeshBlend : MonoBehaviour
{
    [Header("地形")]
    [Tooltip("要采样的地形。留空则自动查找当前场景的地形")]
    public Terrain terrain;

    [Header("目标")]
    [Tooltip("要融合的石头渲染器。留空则自动查找本物体及子物体的 Renderer")]
    public Renderer[] targetRenderers;

    [Header("过渡")]
    [Tooltip("混合遮罩范围(米)：石头底部向上多高范围内做地形混合。同时也作为顶点法线的影响范围")]
    public float blendRange = 0.4f;

    [Tooltip("整体混合强度，0=不混合，1=完全混合")]
    [Range(0f, 1f)]
    public float blendAmount = 1f;

    [Tooltip("过渡区强度，控制石头底部融入地形的程度")]
    [Range(0f, 1f)]
    public float blendStrength = 1f;

    [Tooltip("遮罩对比度：1=不变，>1 过渡更锐利(变窄)，<1 过渡更柔和")]
    public float blendMaskContrast = 1f;

    [Tooltip("噪声遮罩的平铺大小，值越小噪声斑块越大、越稀疏")]
    public float noiseScale = 2f;

    [Tooltip("噪声随机程度，0=平滑直线过渡，1=完全由噪声驱动的随机过渡")]
    [Range(0f, 1f)]
    public float noiseAmount = 0.5f;

    [Header("顶点法线")]
    [Tooltip("开启后接触地面的顶点法线(几何法线)朝地面法线过渡，让光照融合更自然")]
    public bool matchGroundNormal = true;

    [Header("调试")]
    [Tooltip("在场景视图画出顶点法线线轴(绿色)，观察法线方向")]
    public bool debugNormal = false;

    [Tooltip("把过渡区遮罩显示为黑白图(白=混合过渡区，黑=非过渡区)")]
    public bool debugBlendMask = false;

    [Header("贴图")]
    [Tooltip("合成地形权重图的分辨率(越大越精确，也越慢)")]
    public int controlMapResolution = 512;

    [Header("自动")]
    [Tooltip("是否自动把石头材质 Shader 换成融合 Shader")]
    public bool autoAssignShader = true;

    [Tooltip("融合用的 Shader(通常选 Custom/TerrainBlendRock)")]
    public Shader blendShader;

    [Tooltip("运行时每帧刷新(石头会移动时开启)")]
    public bool autoUpdate = false;

    Texture2D _controlMap;
    MaterialPropertyBlock _mpb;
    TerrainData _cachedTerrainData;
    Dictionary<MeshFilter, NormalState> _normalStates = new Dictionary<MeshFilter, NormalState>();

    static readonly int k_ControlMap  = Shader.PropertyToID("_ControlMap");
    static readonly int k_Splat0      = Shader.PropertyToID("_Splat0");
    static readonly int k_Splat1      = Shader.PropertyToID("_Splat1");
    static readonly int k_Splat2      = Shader.PropertyToID("_Splat2");
    static readonly int k_Splat3      = Shader.PropertyToID("_Splat3");
    static readonly int k_SplatNormal0 = Shader.PropertyToID("_SplatNormal0");
    static readonly int k_SplatNormal1 = Shader.PropertyToID("_SplatNormal1");
    static readonly int k_SplatNormal2 = Shader.PropertyToID("_SplatNormal2");
    static readonly int k_SplatNormal3 = Shader.PropertyToID("_SplatNormal3");
    static readonly int k_SplatNormalScale0 = Shader.PropertyToID("_SplatNormalScale0");
    static readonly int k_SplatNormalScale1 = Shader.PropertyToID("_SplatNormalScale1");
    static readonly int k_SplatNormalScale2 = Shader.PropertyToID("_SplatNormalScale2");
    static readonly int k_SplatNormalScale3 = Shader.PropertyToID("_SplatNormalScale3");
    static readonly int k_SplatScale0 = Shader.PropertyToID("_SplatScale0");
    static readonly int k_SplatScale1 = Shader.PropertyToID("_SplatScale1");
    static readonly int k_SplatScale2 = Shader.PropertyToID("_SplatScale2");
    static readonly int k_SplatScale3 = Shader.PropertyToID("_SplatScale3");
    static readonly int k_TerrainPos  = Shader.PropertyToID("_TerrainPos");
    static readonly int k_TerrainSize = Shader.PropertyToID("_TerrainSize");
    static readonly int k_BlendStartY = Shader.PropertyToID("_BlendStartY");
    static readonly int k_BlendRange  = Shader.PropertyToID("_BlendRange");
    static readonly int k_BlendAmount = Shader.PropertyToID("_TerrainBlendAmount");
    static readonly int k_BlendStrength = Shader.PropertyToID("_BlendStrength");
    static readonly int k_BlendMaskContrast = Shader.PropertyToID("_BlendMaskContrast");
    static readonly int k_NoiseScale    = Shader.PropertyToID("_NoiseScale");
    static readonly int k_NoiseAmount   = Shader.PropertyToID("_NoiseAmount");
    static readonly int k_BlendEnabled  = Shader.PropertyToID("_BlendEnabled");
    static readonly int k_DebugBlendMask = Shader.PropertyToID("_DebugBlendMask");

    class NormalState
    {
        public Mesh originalMesh;        // 原始 sharedMesh 资产（用于恢复）
        public Mesh instance;            // 修改法线用的实例副本
        public Vector3[] originalNormals;// 原始顶点法线备份
    }

    void OnEnable()
    {
        _mpb = new MaterialPropertyBlock();
        Setup();
    }

    void OnDisable()
    {
        RestoreVertexNormals();
    }

    void OnValidate()
    {
        if (enabled)
            Setup();
    }

    void LateUpdate()
    {
        if (autoUpdate)
            ApplyToAll();
    }

    public void Setup()
    {
        ResolveReferences();

        if (terrain == null)
        {
            Debug.LogWarning("[TerrainBlend] 未找到 Terrain，请在组件上手动拖入地形。", this);
            return;
        }

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            Debug.LogWarning("[TerrainBlend] 未找到目标 Renderer，请在组件上手动指定石头渲染器。", this);
            return;
        }

        if (_cachedTerrainData != terrain.terrainData || _controlMap == null)
        {
            BuildMaps();
            _cachedTerrainData = terrain.terrainData;
        }

        ApplyToAll();

        // 顶点法线（几何法线）匹配地面
        if (matchGroundNormal)
            ApplyVertexNormals();
        else
            RestoreVertexNormals();
    }

    void ResolveReferences()
    {
        if (terrain == null)
            terrain = GetComponent<Terrain>();
        if (terrain == null)
            terrain = Terrain.activeTerrain;
        if (terrain == null)
            terrain = FindObjectOfType<Terrain>();

        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>();

        if (blendShader == null)
            blendShader = Shader.Find("Custom/TerrainBlendRock");
    }

    void BuildMaps()
    {
        TerrainData td = terrain.terrainData;
        int aw = td.alphamapWidth;
        int ah = td.alphamapHeight;
        int res = Mathf.Max(1, controlMapResolution);

        // —— 控制图（前 4 层 splat 权重）——
        float[,,] alpha = td.GetAlphamaps(0, 0, aw, ah);
        int layerCount = alpha.GetLength(2);

        if (_controlMap == null || _controlMap.width != res || _controlMap.height != res)
        {
            if (_controlMap != null)
                DestroyImmediate(_controlMap);
            _controlMap = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            _controlMap.name = "TerrainControlMap";
            _controlMap.wrapMode = TextureWrapMode.Clamp;
        }

        Color[] cp = new Color[res * res];
        for (int y = 0; y < res; y++)
        {
            int ay = Mathf.Clamp(Mathf.FloorToInt((float)y / res * ah), 0, ah - 1);
            for (int x = 0; x < res; x++)
            {
                int ax = Mathf.Clamp(Mathf.FloorToInt((float)x / res * aw), 0, aw - 1);
                float r = layerCount > 0 ? alpha[ay, ax, 0] : 1f;
                float g = layerCount > 1 ? alpha[ay, ax, 1] : 0f;
                float b = layerCount > 2 ? alpha[ay, ax, 2] : 0f;
                float a = layerCount > 3 ? alpha[ay, ax, 3] : 0f;
                cp[y * res + x] = new Color(r, g, b, a);
            }
        }
        _controlMap.SetPixels(cp);
        _controlMap.Apply(false, false);

        Debug.Log("[TerrainBlend] 贴图合成完成: 控制图 " + res + "x" + res + " (地形层数=" + layerCount + ")", this);
    }

    void ApplyToAll()
    {
        if (terrain == null || targetRenderers == null)
            return;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        TerrainData td = terrain.terrainData;
        TerrainLayer[] layers = td.terrainLayers;

        Debug.Log("[TerrainBlend] 应用地形数据到 " + targetRenderers.Length + " 个 Renderer, 地形 layer 数=" +
                  (layers != null ? layers.Length : 0), this);

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null)
                continue;

            if (autoAssignShader && blendShader != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].shader != blendShader)
                    {
                        mats[m].shader = blendShader;
                        Debug.Log("[TerrainBlend] 已自动把材质 '" + mats[m].name + "' 的 Shader 切换为 " + blendShader.name, this);
                    }
                }
                r.sharedMaterials = mats;
            }

            _mpb.Clear();
            _mpb.SetTexture(k_ControlMap, _controlMap != null ? _controlMap : Texture2D.whiteTexture);
            _mpb.SetTexture(k_Splat0, GetDiffuse(layers, 0));
            _mpb.SetTexture(k_Splat1, GetDiffuse(layers, 1));
            _mpb.SetTexture(k_Splat2, GetDiffuse(layers, 2));
            _mpb.SetTexture(k_Splat3, GetDiffuse(layers, 3));
            _mpb.SetTexture(k_SplatNormal0, GetNormal(layers, 0));
            _mpb.SetTexture(k_SplatNormal1, GetNormal(layers, 1));
            _mpb.SetTexture(k_SplatNormal2, GetNormal(layers, 2));
            _mpb.SetTexture(k_SplatNormal3, GetNormal(layers, 3));
            _mpb.SetFloat(k_SplatNormalScale0, GetNormalScale(layers, 0));
            _mpb.SetFloat(k_SplatNormalScale1, GetNormalScale(layers, 1));
            _mpb.SetFloat(k_SplatNormalScale2, GetNormalScale(layers, 2));
            _mpb.SetFloat(k_SplatNormalScale3, GetNormalScale(layers, 3));
            _mpb.SetVector(k_SplatScale0, ToVec4(GetTileSize(layers, 0)));
            _mpb.SetVector(k_SplatScale1, ToVec4(GetTileSize(layers, 1)));
            _mpb.SetVector(k_SplatScale2, ToVec4(GetTileSize(layers, 2)));
            _mpb.SetVector(k_SplatScale3, ToVec4(GetTileSize(layers, 3)));
            _mpb.SetVector(k_TerrainPos, terrain.transform.position);
            _mpb.SetVector(k_TerrainSize, td.size);
            _mpb.SetFloat(k_BlendStartY, r.bounds.min.y);
            _mpb.SetFloat(k_BlendRange, blendRange);
            _mpb.SetFloat(k_BlendAmount, blendAmount);
            _mpb.SetFloat(k_BlendStrength, blendStrength);
            _mpb.SetFloat(k_BlendMaskContrast, blendMaskContrast);
            _mpb.SetFloat(k_NoiseScale, noiseScale);
            _mpb.SetFloat(k_NoiseAmount, noiseAmount);
            _mpb.SetFloat(k_BlendEnabled, 1f);
            _mpb.SetFloat(k_DebugBlendMask, debugBlendMask ? 1f : 0f);
            r.SetPropertyBlock(_mpb);

            Debug.Log("[TerrainBlend] Renderer '" + r.name + "' 材质 Shader = " +
                      (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "(无材质)") +
                      ", blendRange=" + blendRange + ", blendAmount=" + blendAmount, this);
        }
    }

    // —— 顶点法线（几何法线）修改：接触地面的顶点法线朝地面法线过渡 ——
    void ApplyVertexNormals()
    {
        if (terrain == null || targetRenderers == null)
            return;

        if (_normalStates == null)
            _normalStates = new Dictionary<MeshFilter, NormalState>();

        Vector3 terrainPos = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;
        float range = Mathf.Max(blendRange, 0.001f);

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null)
                continue;

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null)
                continue;

            Mesh original = mf.sharedMesh;
            if (original == null)
                continue;

            if (!original.isReadable)
            {
                Debug.LogWarning("[TerrainBlend] 网格 '" + original.name + "' 未开启 Read/Write，无法修改顶点法线。请在模型 Import Settings 勾选 Read/Write Enabled。", this);
                continue;
            }

            NormalState state;
            if (!_normalStates.TryGetValue(mf, out state) || state == null || state.instance == null)
            {
                state = new NormalState();
                state.originalMesh = original;
                state.instance = Instantiate(original);
                state.instance.name = original.name + "_NormalBlend";
                state.originalNormals = original.normals;
                _normalStates[mf] = state;
                mf.sharedMesh = state.instance;
            }

            // 每次从原始法线出发重新计算，避免多次修改叠加
            Vector3[] verts = state.instance.vertices;
            Vector3[] normals = (Vector3[])state.originalNormals.Clone();

            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 worldPos = mf.transform.TransformPoint(verts[v]);
                float groundH = terrain.SampleHeight(worldPos);
                float dist = worldPos.y - groundH;

                // 只有接近地面的顶点才处理
                if (dist > range)
                    continue;

                float t = Mathf.Clamp01(1f - dist / range);

                float nx = Mathf.Clamp01((worldPos.x - terrainPos.x) / terrainSize.x);
                float nz = Mathf.Clamp01((worldPos.z - terrainPos.z) / terrainSize.z);
                Vector3 groundN = terrain.terrainData.GetInterpolatedNormal(nx, nz);

                // 世界空间地面法线 → 模型本地空间
                Vector3 localGroundN = mf.transform.InverseTransformDirection(groundN);

                normals[v] = Vector3.Slerp(normals[v], localGroundN, t).normalized;
            }

            state.instance.normals = normals;
            // 法线已改，必须重算切线，否则 TBN 不正交，法线贴图转世界空间会失真(产生交界线)
            state.instance.RecalculateTangents();
        }
    }

    void RestoreVertexNormals()
    {
        if (_normalStates == null)
            return;

        foreach (KeyValuePair<MeshFilter, NormalState> kv in _normalStates)
        {
            MeshFilter mf = kv.Key;
            NormalState state = kv.Value;

            if (mf != null && state != null && state.originalMesh != null)
                mf.sharedMesh = state.originalMesh;

            if (state != null && state.instance != null)
            {
                if (Application.isPlaying)
                    Destroy(state.instance);
                else
                    DestroyImmediate(state.instance);
            }
        }
        _normalStates.Clear();
    }

    void OnDrawGizmos()
    {
        if (!debugNormal || targetRenderers == null)
            return;

        Gizmos.color = Color.green;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null)
                continue;

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
                continue;

            Mesh mesh = mf.sharedMesh;
            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;

            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 wp = mf.transform.TransformPoint(verts[v]);
                Vector3 wn = mf.transform.TransformDirection(norms[v]);
                Gizmos.DrawLine(wp, wp + wn * 0.3f);
            }
        }
    }

    static Texture2D GetDiffuse(TerrainLayer[] layers, int index)
    {
        if (layers == null || index >= layers.Length || layers[index] == null)
            return Texture2D.whiteTexture;
        return layers[index].diffuseTexture != null ? layers[index].diffuseTexture : Texture2D.whiteTexture;
    }

    static Texture2D GetNormal(TerrainLayer[] layers, int index)
    {
        if (layers == null || index >= layers.Length || layers[index] == null)
            return Texture2D.normalTexture;
        return layers[index].normalMapTexture != null ? layers[index].normalMapTexture : Texture2D.normalTexture;
    }

    static float GetNormalScale(TerrainLayer[] layers, int index)
    {
        if (layers == null || index >= layers.Length || layers[index] == null)
            return 1f;
        return layers[index].normalScale;
    }

    static Vector2 GetTileSize(TerrainLayer[] layers, int index)
    {
        if (layers == null || index >= layers.Length || layers[index] == null)
            return Vector2.one;
        Vector2 s = layers[index].tileSize;
        if (s.x <= 0.0001f) s.x = 1f;
        if (s.y <= 0.0001f) s.y = 1f;
        return s;
    }

    static Vector4 ToVec4(Vector2 v)
    {
        return new Vector4(v.x, v.y, 0f, 0f);
    }
}
