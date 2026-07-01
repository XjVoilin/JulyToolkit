# JulyToolkit

可复用 UI/表现层工具组件（`com.july.toolkit`）。JulyGame 之上的扩展包，提供即插即用的 UGUI 组件、动画工具、场景过渡协调器，以及 Figma → Unity Prefab 编辑器工具。

> **本文档描述框架的真实行为，与 `Runtime/` 与 `Editor/` 代码一一对应。**

## 程序集

| 程序集 | 说明 | 依赖 |
|--------|------|------|
| `JulyToolkit.Runtime` | 运行时 UI/动画组件 | JulyGame + JulyArch + DOTween + UniTask + TMP |
| `JulyToolkit.Spine` | Spine 动画组件（可选） | Runtime + Spine Runtime |
| `JulyToolkit.Editor` | PS2UGUI 编辑器工具 | Runtime + UnityEditor |

Spine 模块独立 asmdef，未安装 Spine 的项目可不引用 `JulyToolkit.Spine`。

## 模块概览

### Component — 通用交互组件

| 组件 | 说明 |
|------|------|
| `UISmartButton` | 缩放反馈 + 点击冷却 + 音效（继承 `ArchBehaviour`，可访问 Arch 能力） |
| `UIInputBlocker` | 全屏输入拦截（加载中禁用交互） |
| `WorldAnchoredUI` | 世界坐标锚定 UI（3D 物体头顶信息） |

### UGUI — 增强控件

| 组件 | 说明 |
|------|------|
| `UIToggleGroup` / `UIToggleButton` / `UIToggleItem` | 互斥/多选 Toggle 组 |
| `UIPageNavigator` / `UIPageDot` | 分页导航 + 过渡动画（Fade / Slide / Scale） |
| `FixedHandleScrollRect` / `AutoHideScrollbar` | ScrollRect 扩展 |
| `UIGrayGroup` | 子节点统一置灰（Shader） |
| `UIArtNumber` | 艺术数字（Sprite 逐位渲染） |
| `WebImage` | 远程图片异步加载 |
| `UIBtnEffectGroup` / `UIBtnEffectScale` 等 | 按钮点击特效组合 |
| `UIItemSlot` | 通用物品格子 |

### SimpleAnimations — DOTween 待机动画

| 组件 | 说明 |
|------|------|
| `ScalePulse` | 缩放脉冲 |
| `UIFloatIdle` | 上下浮动 |

均继承 `SimpleAnimationBase`，实现 `ISimpleAnimation` 接口。

### SpriteAnimation

| 组件 | 说明 |
|------|------|
| `SpriteFrameAnimator` | 多 Clip 精灵帧动画播放器（类似 Animation 组件） |

### Spine（可选）

| 组件 | 说明 |
|------|------|
| `SpineAnimAutoPlay` | SkeletonAnimation：intro → loop 自动播放 |
| `SpineGraphicAutoPlay` | SkeletonGraphic 版本 |

### SceneTransition — 场景过渡

| 组件 | 说明 |
|------|------|
| `SceneTransitionHandler` | 静态协调器：EnterAsync → 业务加载 → ExitAsync |
| `ISceneTransitionView` | 过渡面板契约（Loading 动画、进度条） |

### Editor/PS2UGUI — Figma 导入

| 工具 | 说明 |
|------|------|
| `PS2UGUIGenerator` | Figma JSON → Unity Prefab / UIWindow |
| `ArtImporter` | 批量导入美术 ZIP（复制资源 + 生成 Prefab） |
| `PS2UGUISettings` | ScriptableObject 配置（字体路径、默认输出目录） |

使用前需创建 `PS2UGUISettings` 资产（`Create → JulyGF/PS2UGUI Settings`），配置字体材质搜索目录和默认输出路径。

## 使用示例

```csharp
// UISmartButton — 挂到 Button GameObject，Inspector 配置缩放/冷却/音效
// 代码侧监听
smartButton.onClick.AddListener(() => Debug.Log("Clicked"));

// 场景过渡
SceneTransitionHandler.Initialize(loadingWindowId: 9001, "UILoading");
await SceneTransitionHandler.EnterAsync(ct: ct);
await LoadResourcesAndSwitchSceneAsync(ct);
await SceneTransitionHandler.ExitAsync(ct: ct);

// Sprite 帧动画
animator.Play("idle");       // 播放指定 clip
animator.Play("attack", loop: false, onComplete: () => animator.Play("idle"));

// PS2UGUI（Editor）
// 1. 配置 PS2UGUISettings
// 2. 选中 Figma 导出的 JSON → 右键 PS2UGUI/生成 UIWindow
// 或：ArtImporter.Import(sourcePath, targetBase, isWindow: true);
```

## 层级关系

```
JulyCommon + JulyArch + JulyEvents
         │
      JulyGame (UISystem, AudioSystem, ...)
         │
     JulyToolkit (UI 组件 + 动画 + Editor 工具)
         │
      项目 Prefab / 场景
```

## 约定

| 约定 | 说明 |
|------|------|
| UISmartButton 需 ArchContext 就绪 | 音效走 `GetSystem<AudioSystem>()` |
| Spine 组件按需引用 | 未安装 Spine 时不要引用 `JulyToolkit.Spine` asmdef |
| PS2UGUI 输出路径由 Settings 控制 | 盒子/小游戏路径在项目 Settings 中配置 |
| 组件即插即用 | 无需注册 System，挂到 Prefab 即可 |

## 依赖

- `com.july.game` — UISystem、AudioSystem 等
- `com.july.arch` — ArchBehaviour
- `com.july.common` / `com.july.events`
- DOTween、UniTask、TextMeshPro
- Spine Runtime（可选，仅 `JulyToolkit.Spine`）
