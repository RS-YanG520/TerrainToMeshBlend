# TerrainToMeshBlend（岩石地形过渡插件）

让岩石等静态模型底部与 Unity 地形自然过渡：颜色、法线贴图、几何顶点法线都从模型平滑过渡到地形，消除模型与地面之间的生硬交界。

## 文件结构

```
TerrainToMeshBlend/
├── TerrainBlendRock.shader   渲染混合 shader（Shader 名：Custom/TerrainBlendRock）
├── TerrainToMeshBlend.cs     组件脚本（采样地形数据 + 修改顶点法线）
└── builtin_shaders-2022.3.59f1/  Unity 内置 shader 参考包（只读对照，不参与同步）
```

对应的运行时代码位置（同步目标）：

```
Assets/
└── TerrainBlend/
    ├── TerrainBlendRock.shader
    └── TerrainToMeshBlend.cs
```

## 使用方法

1. 把石头材质的 Shader 换成 `Custom/TerrainBlendRock`（贴图会自动保留；组件勾选 Auto Assign Shader 时会自动切换）。
2. 在石头根节点（或地形上）挂上 `TerrainToMeshBlend` 组件，拖入 `Terrain` 和石头 `Renderer`（留空会自动查找）。
3. 调整 `Blend Range` 等参数控制过渡效果。

### 前提

- 顶点法线匹配功能需要模型在 Import Settings 里勾选 **Read/Write Enabled**，否则该功能会跳过并告警。
- 只采样地形的前 4 层 splat（RGBA 控制图）。

## 参数说明

### 过渡

| 参数 | 说明 |
|---|---|
| `Blend Range` | 混合遮罩范围（米）：石头底部向上多高范围内做混合，同时作为顶点法线影响范围 |
| `Blend Amount` | 整体混合强度，0=不混合，1=完全混合 |
| `Blend Strength` | 过渡区强度，等比缩放整个遮罩 |
| `Blend Mask Contrast` | 遮罩对比度：1=不变，>1 过渡更锐利(变窄)，<1 更柔和 |
| `Noise Scale` | 噪声遮罩平铺大小，值越小斑块越大越稀疏 |
| `Noise Amount` | 噪声随机程度，0=平滑直线过渡，1=完全随机 |

### 顶点法线

| 参数 | 说明 |
|---|---|
| `Match Ground Normal` | 开启后接触地面的顶点法线（几何法线）朝地面法线过渡，光照更自然 |

### 调试

| 参数 | 说明 |
|---|---|
| `Debug Normal` | 场景视图画出顶点法线线轴（绿色），类似 Maya 的法线显示 |
| `Debug Blend Mask` | 把过渡区遮罩显示为自发光黑白图（白=混合区，黑=非混合区） |

### 贴图 / 自动

| 参数 | 说明 |
|---|---|
| `Control Map Resolution` | 合成地形权重图分辨率（越大越精确，也越慢） |
| `Auto Assign Shader` | 自动把石头材质 Shader 换成融合 Shader |
| `Blend Shader` | 融合用的 Shader（通常选 Custom/TerrainBlendRock） |
| `Auto Update` | 运行时每帧刷新（石头会移动时开启） |

## 工作原理

数据流：**脚本采样地形 → 通过 MaterialPropertyBlock 传给 shader → shader 计算混合遮罩并混合**

1. `TerrainToMeshBlend.BuildMaps()`：读取 `TerrainData.GetAlphamaps`，把前 4 层 splat 权重合成一张 RGBA control map 纹理。
2. `TerrainToMeshBlend.ApplyToAll()`：把 control map、各层 diffuse / normal 贴图、tileSize、地形位置尺寸等参数通过 `MaterialPropertyBlock` 写入每个石头 Renderer。
3. `TerrainBlendRock.shader` 的 `surf()`：
   - 用世界坐标采样地形颜色（`SampleTerrainColor`）和地面法线贴图（`SampleTerrainNormal`）。
   - 计算 `blendMask = 高度遮罩 × 噪声遮罩 × 各强度`，再做对比度调节。
   - 用 `blendMask` 混合颜色、Metallic、Smoothness、法线贴图。
4. `TerrainToMeshBlend.ApplyVertexNormals()`（独立于 shader）：把接触地面的网格顶点法线用 `Slerp` 过渡到地面法线（`GetInterpolatedNormal`），并 `RecalculateTangents()` 保持 TBN 正交，避免交界线。

## 开发约定

- **源码目录**：`TerrainToMeshBlend/`（本目录，位于工程根目录下，与 `Assets/` 同级）。
- **同步目标**：`Assets/TerrainBlend/`。
- 在本目录修改代码后，手动同步（复制）到 `Assets/TerrainBlend/`，Unity 才会重新编译生效。
