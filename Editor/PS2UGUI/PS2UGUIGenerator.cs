using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GooseMarket.Editor
{
    /// <summary>
    /// 从布局 JSON 一键生成 Unity UI Prefab
    /// 右键 _ps_data.json / _figma_data.json → PS2UGUI
    /// </summary>
    public static class PS2UGUIGenerator
    {
        // UITemplate.prefab 已不再需要，prefab 结构由代码直接创建
        private const string FontMaterialPath = "Assets/Game/Art/Fonts/";
        private const string DefaultFontAssetPath = "Assets/Game/Art/Fonts/Font_Main.asset";
        private const string GlobalSpriteRoot = "Assets/Game/Art/Textures/";
        private const string UIPrefabRoot = "Assets/Game/Art/Prefabs/UI/";
        private const int SlicePixelTolerance = 5;
        private const int SliceMinimumCenterPixelsPerSide = 2;
        private const float ScaleCompensationEpsilon = 0.0001f;
        private const float SerializedValueTruncateEpsilon = 0.000001f;
        private static readonly string[] SupportedJsonFileSuffixes = { "_ui_data.json", "_ps_data.json", "_figma_data.json" };
        private static readonly string[] PrefabNameSuffixes = { "_ui_data", "_ps_data", "_figma_data" };

        private const string FontMaterialPrefix = "Font_Main Material ";


        private static readonly Dictionary<int, int> OpacityMap = new Dictionary<int, int>
        {
            { 0, 0 }, { 5, -1 }, { 10, -1 }, { 15, -1 }, { 20, -1 },
            { 25, -1 }, { 30, -1 }, { 35, -1 }, { 40, -1 }, { 45, -1 },
            { 50, -1 }, { 55, -1 }, { 60, -1 }, { 65, -1 }, { 70, -1 },
            { 75, -1 }, { 80, -1 }, { 85, -1 }, { 90, -1 }, { 95, -1 },
            { 100, 100 }
        };

        // 运行时缓存：当前 JSON 所在目录（末尾含 /）
        private static string _currentJsonDirectory;

        #region 公开 API

        /// <summary>
        /// 程序化入口：从 JSON 文件生成 Prefab。
        /// 供 AI 自动化流程或外部脚本调用。
        /// </summary>
        /// <param name="jsonAssetPath">JSON 文件的 Asset 路径，如 "Assets/Game/MiniGames/Game101/Art/Textures/UIGame101HUD_figma_data.json"</param>
        /// <param name="isWindow">true = 框架 UIWindow（带 CanvasGroup），false = 普通 Prefab</param>
        public static void GenerateFromJson(string jsonAssetPath, bool isWindow)
        {
            if (!IsSupportedPrefabJsonPath(jsonAssetPath))
            {
                Debug.LogError($"PS2UGUI: 不支持的文件路径 → {jsonAssetPath}");
                return;
            }

            UnityData data = PS2UGUITranslate.Translate(jsonAssetPath);

            if (data == null || data.canvas == null || data.children == null)
            {
                Debug.LogError($"PS2UGUI: JSON 转换失败 → {jsonAssetPath}");
                return;
            }

            var uiName = GetPrefabNameFromJsonPath(jsonAssetPath);
            GeneratePrefab(data, jsonAssetPath, uiName, isWindow);
        }

        #endregion

        #region 菜单入口

        [MenuItem("Assets/PS2UGUI/生成 Prefab", false, 0)]
        private static void GenerateNormalPrefabMenuItem()
        {
            ExecuteGeneration(isWindow: false);
        }

        [MenuItem("Assets/PS2UGUI/生成 UIWindow", false, 1)]
        private static void GenerateWindowPrefabMenuItem()
        {
            ExecuteGeneration(isWindow: true);
        }

        [MenuItem("Assets/PS2UGUI/生成 Prefab", true)]
        private static bool GenerateNormalPrefabValidation()
        {
            return ValidateSelection();
        }

        [MenuItem("Assets/PS2UGUI/生成 UIWindow", true)]
        private static bool GenerateWindowPrefabValidation()
        {
            return ValidateSelection();
        }

        private static bool ValidateSelection()
        {
            var obj = Selection.activeObject;
            if (obj == null) return false;
            var path = AssetDatabase.GetAssetPath(obj);
            return IsSupportedPrefabJsonPath(path);
        }

        private static void ExecuteGeneration(bool isWindow)
        {
            var selected = Selection.activeObject;
            var path = AssetDatabase.GetAssetPath(selected);

            UnityData data = PS2UGUITranslate.Translate(path);

            if (data == null || data.canvas == null || data.children == null)
            {
                EditorUtility.DisplayDialog("PS2UGUI", "数据转换失败。", "确定");
                return;
            }

            var uiName = GetPrefabNameFromJsonPath(path);
            GeneratePrefab(data, path, uiName, isWindow);
        }

        internal static bool IsSupportedPrefabJsonPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (var suffix in SupportedJsonFileSuffixes)
            {
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static string GetPrefabNameFromJsonPath(string jsonPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(jsonPath);
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;

            foreach (var suffix in PrefabNameSuffixes)
            {
                if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var prefabName = fileName.Substring(0, fileName.Length - suffix.Length);
                return string.IsNullOrEmpty(prefabName) ? fileName : prefabName;
            }

            return fileName;
        }

        #endregion

        #region Prefab 生成

        /// <param name="isWindow">true = 框架 UIWindow（带 CanvasGroup），false = 普通 Prefab</param>
        internal static void GeneratePrefab(UnityData data, string jsonPath, string uiName, bool isWindow)
        {
            var dir = Path.GetDirectoryName(jsonPath)?.Replace("\\", "/");
            _currentJsonDirectory = string.IsNullOrEmpty(dir) ? GlobalSpriteRoot : dir + "/";

            var prefabPath = ResolvePrefabOutputPath(jsonPath, uiName);
            if (string.IsNullOrEmpty(prefabPath))
            {
                EditorUtility.DisplayDialog("PS2UGUI",
                    $"无法确定 Prefab 输出路径。\n\nJSON 位置: {jsonPath}\n请确保 JSON 位于 Art/Textures/ 目录下。", "确定");
                return;
            }

            if (AssetDatabase.GetMainAssetTypeAtPath(prefabPath) != null)
            {
                if (!EditorUtility.DisplayDialog("PS2UGUI",
                        $"目标 Prefab 已存在：\n{prefabPath}\n\n是否覆盖？", "覆盖", "取消"))
                {
                    return;
                }

                AssetDatabase.DeleteAsset(prefabPath);
            }

            // 确保输出目录存在
            var prefabDir = Path.GetDirectoryName(prefabPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(prefabDir) && !AssetDatabase.IsValidFolder(prefabDir))
            {
                CreateFolderRecursive(prefabDir);
            }

            // 创建 prefab 根节点
            var prefabRoot = CreatePrefabRoot(uiName, isWindow);

            if (data.children != null)
            {
                foreach (var child in data.children)
                    CreateNode(child, prefabRoot.transform);
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            GameObject.DestroyImmediate(prefabRoot);

            AssetDatabase.Refresh();

            var newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = newPrefab;
            EditorGUIUtility.PingObject(newPrefab);
            AssetDatabase.OpenAsset(newPrefab);

            var typeLabel = isWindow ? "UIWindow" : "Prefab";
            Debug.Log($"PS2UGUI: 已生成 {typeLabel} → {prefabPath}");
            _currentJsonDirectory = null;

            if (isWindow)
                GenerateWindowScript(uiName, prefabPath);
        }

        /// <summary>
        /// 创建 Prefab 根 GameObject。
        /// 普通 Prefab：仅 RectTransform
        /// UIWindow：RectTransform + CanvasGroup
        /// </summary>
        private static GameObject CreatePrefabRoot(string name, bool isWindow)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.layer = LayerMask.NameToLayer("UI");

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            if (isWindow)
            {
                var cg = root.AddComponent<CanvasGroup>();
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            return root;
        }

        #endregion

        #region 路径解析

        private const string MiniGamesSegment = "MiniGames/";
        private const string ArtTexturesSegment = "/Art/Textures";

        /// <summary>
        /// 从 JSON 路径解析 prefab 输出路径。
        /// 规则：
        ///   小游戏 JSON 在 MiniGames/GameXXX/Art/Textures/ → 输出到 MiniGames/GameXXX/Res/Prefabs/UI/{name}.prefab
        ///   盒子 JSON 在 Assets/Game/Art/Textures/ → 输出到 Assets/Game/Res/Prefabs/UI/{name}/{name}.prefab
        /// </summary>
        private static string ResolvePrefabOutputPath(string jsonPath, string prefabName)
        {
            var normalized = jsonPath.Replace("\\", "/");

            // 尝试从路径中提取 game root（MiniGames/GameXXX/ 或 Assets/Game/）
            var miniGamesIdx = normalized.IndexOf(MiniGamesSegment, StringComparison.OrdinalIgnoreCase);
            if (miniGamesIdx >= 0)
            {
                // 小游戏路径：找到 MiniGames/GameXXX/ 层级
                var afterMiniGames = normalized.Substring(miniGamesIdx + MiniGamesSegment.Length);
                var slashIdx = afterMiniGames.IndexOf('/');
                if (slashIdx > 0)
                {
                    var gameRoot = normalized.Substring(0, miniGamesIdx + MiniGamesSegment.Length + slashIdx);
                    return $"{gameRoot}/Res/Prefabs/UI/{prefabName}.prefab";
                }
            }

            // 盒子路径：从 Art/Textures 向上找到 Assets/Game
            var artIdx = normalized.IndexOf(ArtTexturesSegment, StringComparison.OrdinalIgnoreCase);
            if (artIdx >= 0)
            {
                var gameRoot = normalized.Substring(0, artIdx);
                return $"{gameRoot}/Res/Prefabs/UI/{prefabName}/{prefabName}.prefab";
            }

            return null;
        }

        /// <summary>
        /// 解析 sprite 完整路径（混合模式）。
        /// 无 /：本地 sprite，在 JSON 同目录查找。
        /// 有 /：跨目录 sprite，从 JSON 目录向上递归查找第一段目录名。
        /// </summary>
        private static string ResolveSpriteFullPath(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            var jsonDir = _currentJsonDirectory ?? GlobalSpriteRoot;

            if (!spriteName.Contains("/"))
            {
                return jsonDir + spriteName + ".png";
            }

            var firstSlash = spriteName.IndexOf('/');
            var firstSegment = spriteName.Substring(0, firstSlash);
            var dir = jsonDir.TrimEnd('/');

            while (!string.IsNullOrEmpty(dir) && dir.Contains("/"))
            {
                var candidate = dir + "/" + firstSegment;
                if (AssetDatabase.IsValidFolder(candidate))
                    return dir + "/" + spriteName + ".png";
                var lastSlash = dir.LastIndexOf('/');
                dir = lastSlash >= 0 ? dir.Substring(0, lastSlash) : "";
            }

            Debug.LogWarning($"PS2UGUI: 向上递归查找失败 → {spriteName}（从 {jsonDir} 开始）");
            return jsonDir + spriteName + ".png";
        }

        /// <summary>
        /// 递归创建 Asset 文件夹
        /// </summary>
        private static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                CreateFolderRecursive(parent);
            }

            var folderName = Path.GetFileName(folderPath);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        #endregion

        #region 节点创建

        private static void CreateNode(UnityNode nodeData, Transform parent)
        {
            GameObject go;

            switch (nodeData.type)
            {
                case "button":
                    go = CreateButtonNode(nodeData);
                    break;
                case "text":
                    go = CreateTextNode(nodeData);
                    break;
                case "slider":
                    go = CreateSliderNode(nodeData);
                    break;
                case "image":
                    go = CreateImageNode(nodeData);
                    break;
                case "prefab":
                    go = CreatePrefabNode(nodeData, parent.gameObject.scene);
                    break;
                case "node":
                default:
                    go = CreateEmptyNode(nodeData);
                    break;
            }

            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            if (rect == null)
                rect = go.AddComponent<RectTransform>();

            ApplyAnchorPreset(rect, nodeData);
            ApplyRotationZ(rect, nodeData, parent);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (nodeData.type == "text" && tmp != null)
                ApplyDesiredVisualFontSize(tmp, nodeData, parent);
            else
                ApplyDesiredVisualScale(rect, nodeData, parent);

            if (nodeData.useNativeSize)
            {
                var image = go.GetComponent<Image>();
                if (image != null && image.sprite != null)
                    image.SetNativeSize();
            }

            go.layer = LayerMask.NameToLayer("UI");

            if (!nodeData.active)
                go.SetActive(false);

            if (nodeData.children != null)
            {
                TextAreaResult textArea = default;
                if (nodeData.type == "button" && !string.IsNullOrEmpty(nodeData.spritePath))
                {
                    textArea = DetectButtonTextAreaFromSprite(nodeData.spritePath, nodeData.width, nodeData.height);
                }

                foreach (var child in nodeData.children)
                {
                    CreateNode(child, go.transform);

                    if (textArea.success && child.type == "text")
                    {
                        var childRect = go.transform.Find(child.name)?.GetComponent<RectTransform>();
                        if (childRect != null)
                        {
                            childRect.anchoredPosition = TruncateToTwoDecimals(new Vector2(textArea.offsetX, textArea.offsetY));
                            childRect.sizeDelta = TruncateToTwoDecimals(new Vector2(textArea.width, textArea.height));
                        }
                    }
                }
            }
        }

        private static GameObject CreateEmptyNode(UnityNode nodeData)
        {
            return new GameObject(nodeData.name);
        }

        private static GameObject CreateImageNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var image = go.AddComponent<Image>();
            image.type = nodeData.imageType == "tiled" ? Image.Type.Tiled : Image.Type.Simple;
            image.raycastTarget = nodeData.raycastTarget;

            LoadSprite(image, nodeData.spritePath);

            if (nodeData.imageType == "sliced")
                ApplySlicedType(image, nodeData.spritePath);

            ApplyColorAndOpacity(image, nodeData.colorHex, nodeData.opacity);

            if (nodeData.addUIButton)
            {
                go.AddComponent<UISmartButton>();
            }

            return go;
        }

        private static GameObject CreatePrefabNode(UnityNode nodeData, Scene destinationScene)
        {
            if (string.IsNullOrEmpty(nodeData.prefabPath))
                return new GameObject(nodeData.name);

            var fullPath = UIPrefabRoot + nodeData.prefabPath + ".prefab";
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (prefabAsset == null)
            {
                Debug.LogWarning($"PS2UGUI: 外部 Prefab 加载失败 → {fullPath}");
                return new GameObject(nodeData.name);
            }

            var prefabInstance = PrefabUtility.InstantiatePrefab(prefabAsset, destinationScene) as GameObject;
            if (prefabInstance == null)
            {
                Debug.LogWarning($"PS2UGUI: 外部 Prefab 实例化失败 → {fullPath}");
                return new GameObject(nodeData.name);
            }

            prefabInstance.name = nodeData.name;
            return prefabInstance;
        }

        private static GameObject CreateButtonNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var image = go.AddComponent<Image>();
            image.type = Image.Type.Simple;
            image.raycastTarget = true;

            LoadSprite(image, nodeData.spritePath);

            if (nodeData.imageType == "sliced")
                ApplySlicedType(image, nodeData.spritePath);

            ApplyColorAndOpacity(image, nodeData.colorHex, nodeData.opacity);

            go.AddComponent<UISmartButton>();

            return go;
        }

        private static GameObject CreateTextNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var tmp = go.AddComponent<TextMeshProUGUI>();

            ApplyDefaultFont(tmp);

            tmp.text = nodeData.text;
            tmp.fontSize = TruncateToTwoDecimals(nodeData.fontSize);
            tmp.raycastTarget = false;

            if (!string.IsNullOrEmpty(nodeData.colorHex))
            {
                if (ColorUtility.TryParseHtmlString(nodeData.colorHex, out Color color))
                {
                    int opacity = MapOpacity(nodeData.opacity);
                    if (opacity < 100)
                        color.a = opacity / 100f;
                    tmp.color = color;
                }
            }
            else if (nodeData.opacity < 100)
            {
                int opacity = MapOpacity(nodeData.opacity);
                var c = tmp.color;
                c.a = opacity / 100f;
                tmp.color = c;
            }

            tmp.alignment = MapAlignment(nodeData.alignment);

            if (!string.IsNullOrEmpty(nodeData.strokeColor) && nodeData.strokeWidth > 0)
            {
                var colorHex = nodeData.strokeColor.TrimStart('#').ToUpper();
                var widthStr = FormatStrokeWidth(nodeData.strokeWidth);
                var matFileName = $"{FontMaterialPrefix}{colorHex} {widthStr}.mat";
                var matPath = FontMaterialPath + matFileName;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    mat = CreateOutlineMaterial(matPath, tmp.font, nodeData.strokeColor, nodeData.strokeWidth);
                if (mat != null)
                    tmp.fontSharedMaterial = mat;
            }

            return go;
        }

        private static GameObject CreateSliderNode(UnityNode nodeData)
        {
            var go = new GameObject(nodeData.name);

            var image = go.AddComponent<Image>();
            image.type = Image.Type.Simple;
            image.raycastTarget = true;

            LoadSprite(image, nodeData.spritePath);
            ApplyColorAndOpacity(image, nodeData.colorHex, nodeData.opacity);

            var slider = go.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            if (!string.IsNullOrEmpty(nodeData.fillSpritePath))
            {
                var fillGo = new GameObject("Fill");
                fillGo.transform.SetParent(go.transform, false);
                fillGo.layer = LayerMask.NameToLayer("UI");

                var fillRect = fillGo.AddComponent<RectTransform>();
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;

                var fillImage = fillGo.AddComponent<Image>();
                fillImage.type = Image.Type.Filled;
                fillImage.raycastTarget = false;

                if (nodeData.fillDirection == "vertical")
                {
                    fillImage.fillMethod = Image.FillMethod.Vertical;
                    fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                }
                else
                {
                    fillImage.fillMethod = Image.FillMethod.Horizontal;
                    fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                }

                LoadSprite(fillImage, nodeData.fillSpritePath);

                slider.fillRect = fillRect;
            }

            return go;
        }

        #endregion

        #region 工具方法

        private static void ApplyDefaultFont(TextMeshProUGUI tmp)
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontAssetPath);
            if (fontAsset != null)
                tmp.font = fontAsset;
        }

        private static void ApplyAnchorPreset(RectTransform rect, UnityNode nodeData)
        {
            if (nodeData.anchorPreset == "stretch-all")
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = TruncateToTwoDecimals(new Vector2(nodeData.x, nodeData.y));
                rect.sizeDelta = TruncateToTwoDecimals(new Vector2(nodeData.width, nodeData.height));
            }
        }

        private static void ApplyRotationZ(RectTransform rect, UnityNode nodeData, Transform parent)
        {
            var eulerAngles = rect.localEulerAngles;
            var parentRotationZ = GetAccumulatedParentRotationZ(parent);
            var localRotationZ = ResolveCompensatedLocalRotation(nodeData.rotationZ, parentRotationZ);
            rect.localRotation = Quaternion.Euler(eulerAngles.x, eulerAngles.y, localRotationZ);
        }

        private static void ApplyDesiredVisualScale(RectTransform rect, UnityNode nodeData, Transform parent)
        {
            var parentScale = GetAccumulatedParentScale(parent);
            var baseScale = rect.localScale;

            rect.localScale = TruncateToTwoDecimals(new Vector3(
                ResolveCompensatedLocalScale(baseScale.x, nodeData.scaleX, parentScale.x),
                ResolveCompensatedLocalScale(baseScale.y, nodeData.scaleY, parentScale.y),
                baseScale.z));
        }

        private static void ApplyDesiredVisualFontSize(TextMeshProUGUI tmp, UnityNode nodeData, Transform parent)
        {
            var parentScale = GetAccumulatedParentScale(parent);

            if (Mathf.Abs(nodeData.scaleX - nodeData.scaleY) > ScaleCompensationEpsilon ||
                Mathf.Abs(parentScale.x - parentScale.y) > ScaleCompensationEpsilon)
            {
                Debug.LogWarning(
                    $"PS2UGUI: 文本节点 {nodeData.name} 检测到非等比缩放，将按 Y 轴进行字号补偿。" +
                    $" nodeScale=({nodeData.scaleX:F4}, {nodeData.scaleY:F4})," +
                    $" parentScale=({parentScale.x:F4}, {parentScale.y:F4})");
            }

            tmp.fontSize = TruncateToTwoDecimals(ResolveCompensatedFontSize(tmp.fontSize, nodeData.scaleY, parentScale.y));
        }

        private static Vector3 GetAccumulatedParentScale(Transform parent)
        {
            var accumulatedScale = Vector3.one;
            var current = parent;

            while (current != null)
            {
                accumulatedScale = Vector3.Scale(accumulatedScale, current.localScale);
                current = current.parent;
            }

            return accumulatedScale;
        }

        private static float GetAccumulatedParentRotationZ(Transform parent)
        {
            var accumulatedRotation = 0f;
            var current = parent;

            while (current != null)
            {
                accumulatedRotation = NormalizeAngle(accumulatedRotation + NormalizeAngle(current.localEulerAngles.z));
                current = current.parent;
            }

            return accumulatedRotation;
        }

        private static float ResolveCompensatedLocalScale(float baseScale, float desiredVisualScale, float parentAccumulatedScale)
        {
            if (Mathf.Abs(parentAccumulatedScale) <= ScaleCompensationEpsilon)
                return baseScale * desiredVisualScale;

            return baseScale * desiredVisualScale / parentAccumulatedScale;
        }

        private static float ResolveCompensatedLocalRotation(float desiredVisualRotation, float parentAccumulatedRotation)
        {
            return NormalizeAngle(desiredVisualRotation - parentAccumulatedRotation);
        }

        private static float ResolveCompensatedFontSize(float baseFontSize, float desiredVisualScaleY, float parentAccumulatedScaleY)
        {
            var safeDesiredScaleY = Mathf.Abs(desiredVisualScaleY);
            var safeParentAccumulatedScaleY = Mathf.Abs(parentAccumulatedScaleY);

            if (safeParentAccumulatedScaleY <= ScaleCompensationEpsilon)
                return baseFontSize * safeDesiredScaleY;

            return baseFontSize * safeDesiredScaleY / safeParentAccumulatedScaleY;
        }

        private static Vector2 TruncateToTwoDecimals(Vector2 value)
        {
            return new Vector2(TruncateToTwoDecimals(value.x), TruncateToTwoDecimals(value.y));
        }

        private static Vector3 TruncateToTwoDecimals(Vector3 value)
        {
            return new Vector3(TruncateToTwoDecimals(value.x), TruncateToTwoDecimals(value.y), TruncateToTwoDecimals(value.z));
        }

        private static float TruncateToTwoDecimals(float value)
        {
            double adjustedValue = value;
            if (Mathf.Abs(value) > 0f)
                adjustedValue += Math.Sign(value) * SerializedValueTruncateEpsilon;

            return (float)(Math.Truncate(adjustedValue * 100d) / 100d);
        }

        private static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        private static void LoadSprite(Image image, string spritePath)
        {
            if (string.IsNullOrEmpty(spritePath)) return;

            var fullPath = ResolveSpriteFullPath(spritePath);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);

            if (sprite != null)
            {
                image.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"PS2UGUI: Sprite 加载失败 → {fullPath}");
            }
        }

        private static void ApplyColorAndOpacity(Image image, string colorHex, int rawOpacity)
        {
            int opacity = MapOpacity(rawOpacity);

            if (!string.IsNullOrEmpty(colorHex))
            {
                if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
                {
                    if (opacity < 100)
                        color.a = opacity / 100f;
                    image.color = color;
                    return;
                }
            }

            if (opacity < 100)
            {
                var color = image.color;
                color.a = opacity / 100f;
                image.color = color;
            }
        }

        private static TextAlignmentOptions MapAlignment(string alignment)
        {
            switch (alignment)
            {
                case "left-top": return TextAlignmentOptions.TopLeft;
                case "center-top": return TextAlignmentOptions.Top;
                case "right-top": return TextAlignmentOptions.TopRight;
                case "left-middle": return TextAlignmentOptions.Left;
                case "center-middle": return TextAlignmentOptions.Center;
                case "right-middle": return TextAlignmentOptions.Right;
                case "left-bottom": return TextAlignmentOptions.BottomLeft;
                case "center-bottom": return TextAlignmentOptions.Bottom;
                case "right-bottom": return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        private static Material CreateOutlineMaterial(string matPath, TMP_FontAsset font, string strokeColorHex, float strokeWidth)
        {
            if (font == null || font.material == null) return null;

            var mat = new Material(font.material);
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetFloat("_UnderlayOffsetX", 0);
            mat.SetFloat("_UnderlayOffsetY", 0);
            mat.SetFloat("_UnderlaySoftness", 0);
            mat.SetFloat("_UnderlayDilate", strokeWidth * 0.1f);

            if (ColorUtility.TryParseHtmlString(strokeColorHex, out Color color))
                mat.SetColor("_UnderlayColor", new Color(color.r, color.g, color.b, 1f));

            var dir = Path.GetDirectoryName(matPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"PS2UGUI: 自动创建描边 Material (Underlay) → {matPath}");
            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        private static string FormatStrokeWidth(float width)
        {
            int intVal = (int)width;
            if (Mathf.Approximately(width, intVal))
                return intVal.ToString("D2");
            var s = width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return s.Replace(".", "").PadLeft(2, '0');
        }

        private static int MapOpacity(int psOpacity)
        {
            int rounded = (int)(Math.Round(psOpacity / 5.0) * 5);
            rounded = Math.Max(0, Math.Min(100, rounded));

            if (OpacityMap.TryGetValue(rounded, out int mapped) && mapped >= 0)
                return mapped;

            return psOpacity;
        }

        #endregion

        #region 九宫格检测

        private struct SliceBorderResult
        {
            public bool success;
            public int left, right, top, bottom;
        }

        private struct TextAreaResult
        {
            public bool success;
            public float offsetX, offsetY, width, height;
        }

        private static SliceBorderResult DetectSliceBorder(Texture2D texture)
        {
            var result = new SliceBorderResult();

            int w = texture.width;
            int h = texture.height;
            if (w <= 2 || h <= 2)
                return result;

            var pixels = texture.GetPixels32();

            bool lrFound = TryDetectAxisBorder(
                w,
                (index, referenceIndex) => ColumnEquals(pixels, index, referenceIndex, w, h),
                out int left, out int right);
            if (lrFound)
            {
                result.left = left;
                result.right = right;
            }

            bool tbFound = TryDetectAxisBorder(
                h,
                (index, referenceIndex) => RowEquals(pixels, index, referenceIndex, w),
                out int bottom, out int top);
            if (tbFound)
            {
                result.top = top;
                result.bottom = bottom;
            }

            result.success = lrFound || tbFound;
            return result;
        }

        private static bool TryDetectAxisBorder(int size, Func<int, int, bool> lineEquals, out int startBorder, out int endBorder)
        {
            startBorder = -1;
            endBorder = -1;

            int centerStart = (size - 1) / 2;
            int centerEnd = size / 2;
            int minIndex = centerStart - (SliceMinimumCenterPixelsPerSide - 1);
            int maxIndex = centerEnd + (SliceMinimumCenterPixelsPerSide - 1);

            if (minIndex <= 0 || maxIndex >= size - 1)
                return false;

            for (int offset = 0; offset < SliceMinimumCenterPixelsPerSide; offset++)
            {
                if (!lineEquals(centerStart - offset, centerStart))
                    return false;

                if (!lineEquals(centerEnd + offset, centerEnd))
                    return false;
            }

            startBorder = minIndex;
            endBorder = size - 1 - maxIndex;
            return startBorder > 0 && startBorder < size / 2 && endBorder > 0 && endBorder < size / 2;
        }

        private static TextAreaResult DetectButtonTextArea(Texture2D texture)
        {
            var result = new TextAreaResult();

            int w = texture.width;
            int h = texture.height;
            if (w <= 1 || h <= 1)
                return result;

            var pixels = texture.GetPixels32();

            int centerCol = w / 2;
            int centerRow = h / 2;
            Color32 centerPixel = pixels[centerRow * w + centerCol];

            int leftBound = 0;
            for (int col = centerCol - 1; col >= 0; col--)
            {
                if (!PixelEquals(pixels[centerRow * w + col], centerPixel))
                {
                    leftBound = col + 1;
                    break;
                }
            }

            int rightBound = w - 1;
            for (int col = centerCol + 1; col < w; col++)
            {
                if (!PixelEquals(pixels[centerRow * w + col], centerPixel))
                {
                    rightBound = col - 1;
                    break;
                }
            }

            int bottomBound = 0;
            for (int row = centerRow - 1; row >= 0; row--)
            {
                if (!PixelEquals(pixels[row * w + centerCol], centerPixel))
                {
                    bottomBound = row + 1;
                    break;
                }
            }

            int topBound = h - 1;
            for (int row = centerRow + 1; row < h; row++)
            {
                if (!PixelEquals(pixels[row * w + centerCol], centerPixel))
                {
                    topBound = row - 1;
                    break;
                }
            }

            float textAreaWidth = rightBound - leftBound + 1;
            float textAreaHeight = topBound - bottomBound + 1;
            float textAreaCenterX = (leftBound + rightBound) / 2f - (w - 1) / 2f;
            float textAreaCenterY = (bottomBound + topBound) / 2f - (h - 1) / 2f;

            if (textAreaWidth > 2 && textAreaHeight > 2 &&
                (textAreaWidth < w || textAreaHeight < h))
            {
                result.success = true;
                result.offsetX = textAreaCenterX;
                result.offsetY = textAreaCenterY;
                result.width = textAreaWidth;
                result.height = textAreaHeight;
            }

            return result;
        }

        private static TextAreaResult RemapTextAreaToButtonSize(
            TextAreaResult pixelResult, int spriteW, int spriteH,
            float buttonW, float buttonH, Vector4 border)
        {
            if (!pixelResult.success)
                return pixelResult;

            float halfSpriteW = (spriteW - 1) / 2f;
            float halfSpriteH = (spriteH - 1) / 2f;
            float pxLeft = halfSpriteW + pixelResult.offsetX - pixelResult.width / 2f;
            float pxRight = halfSpriteW + pixelResult.offsetX + pixelResult.width / 2f;
            float pxBottom = halfSpriteH + pixelResult.offsetY - pixelResult.height / 2f;
            float pxTop = halfSpriteH + pixelResult.offsetY + pixelResult.height / 2f;

            float uiLeft = RemapAxis(pxLeft, spriteW, buttonW, border.x, border.z);
            float uiRight = RemapAxis(pxRight, spriteW, buttonW, border.x, border.z);
            float uiBottom = RemapAxis(pxBottom, spriteH, buttonH, border.y, border.w);
            float uiTop = RemapAxis(pxTop, spriteH, buttonH, border.y, border.w);

            float newWidth = uiRight - uiLeft;
            float newHeight = uiTop - uiBottom;
            float newOffsetX = (uiLeft + uiRight) / 2f - buttonW / 2f;
            float newOffsetY = (uiBottom + uiTop) / 2f - buttonH / 2f;

            if (newWidth <= 2 || newHeight <= 2)
                return new TextAreaResult { success = false };

            return new TextAreaResult
            {
                success = true,
                offsetX = newOffsetX,
                offsetY = newOffsetY,
                width = newWidth,
                height = newHeight
            };
        }

        private static float RemapAxis(float px, int spriteSize, float buttonSize, float borderStart, float borderEnd)
        {
            if (borderStart + borderEnd <= 0 || borderStart + borderEnd >= spriteSize)
                return px * buttonSize / spriteSize;

            float spriteCenter = spriteSize - borderStart - borderEnd;
            float buttonCenter = buttonSize - borderStart - borderEnd;

            if (buttonCenter <= 0)
                return px * buttonSize / spriteSize;

            if (px < borderStart)
                return px;
            else if (px > spriteSize - borderEnd)
                return buttonSize - (spriteSize - px);
            else
            {
                if (spriteCenter <= 0) return px;
                return borderStart + (px - borderStart) * buttonCenter / spriteCenter;
            }
        }

        private static TextAreaResult DetectButtonTextAreaFromSprite(string spritePath, float buttonW, float buttonH)
        {
            var fullPath = ResolveSpriteFullPath(spritePath);
            var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer == null)
                return default;

            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            if (texture == null)
            {
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
                return default;
            }

            var result = DetectButtonTextArea(texture);

            if (result.success)
            {
                Vector4 border = importer.spriteBorder;
                result = RemapTextAreaToButtonSize(result, texture.width, texture.height, buttonW, buttonH, border);
            }

            if (!wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            return result;
        }

        private static bool ColumnEquals(Color32[] pixels, int colA, int colB, int w, int h)
        {
            for (int row = 0; row < h; row++)
            {
                if (!SlicePixelEquals(pixels[row * w + colA], pixels[row * w + colB]))
                    return false;
            }
            return true;
        }

        private static bool RowEquals(Color32[] pixels, int rowA, int rowB, int w)
        {
            int offsetA = rowA * w;
            int offsetB = rowB * w;
            for (int col = 0; col < w; col++)
            {
                if (!SlicePixelEquals(pixels[offsetA + col], pixels[offsetB + col]))
                    return false;
            }
            return true;
        }

        private static bool SlicePixelEquals(Color32 a, Color32 b)
        {
            bool aVisible = a.a > 0;
            bool bVisible = b.a > 0;
            if (aVisible != bVisible) return false;
            if (!aVisible) return true;

            return ChannelEqualsWithinTolerance(a.r, b.r)
                && ChannelEqualsWithinTolerance(a.g, b.g)
                && ChannelEqualsWithinTolerance(a.b, b.b)
                && ChannelEqualsWithinTolerance(a.a, b.a);
        }

        private static bool ChannelEqualsWithinTolerance(byte a, byte b)
        {
            return Math.Abs(a - b) <= SlicePixelTolerance;
        }

        private static bool PixelEquals(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        private static void ApplySlicedType(Image image, string spritePath)
        {
            var fullPath = ResolveSpriteFullPath(spritePath);
            var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"PS2UGUI: 九宫格处理失败，TextureImporter 不存在 → {fullPath}");
                return;
            }

            Vector4 existingBorder = importer.spriteBorder;
            if (existingBorder.x > 0 || existingBorder.y > 0 || existingBorder.z > 0 || existingBorder.w > 0)
            {
                image.type = Image.Type.Sliced;
                return;
            }

            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            if (texture == null)
            {
                Debug.LogWarning($"PS2UGUI: 九宫格检测失败，纹理加载失败 → {fullPath}");
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
                return;
            }

            var borderResult = DetectSliceBorder(texture);

            if (borderResult.success)
            {
                if (!wasReadable)
                    importer.isReadable = false;

                importer.spriteBorder = new Vector4(
                    borderResult.left, borderResult.bottom,
                    borderResult.right, borderResult.top);
                importer.SaveAndReimport();

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);
                if (sprite != null)
                    image.sprite = sprite;

                image.type = Image.Type.Sliced;
            }
            else
            {
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }

                Debug.LogWarning($"PS2UGUI: 九宫格 border 检测失败 → {fullPath}");
            }
        }

        #endregion

        #region UIWindow 脚本生成

        private const string WindowScriptBasePath = "Assets/Game/Scripts/Views/Windows";
        private const string PrefsKeyPendingClassName = "PS2UGUI.PendingWindowClassName";
        private const string PrefsKeyPendingPrefabPath = "PS2UGUI.PendingWindowPrefabPath";

        private static void GenerateWindowScript(string className, string prefabPath)
        {
            var scriptFolder = $"{WindowScriptBasePath}/{className}";
            var scriptPath = $"{scriptFolder}/{className}.cs";

            if (File.Exists(scriptPath.Replace("Assets/", Application.dataPath + "/")))
            {
                Debug.Log($"PS2UGUI: 脚本已存在，跳过生成 → {scriptPath}");
                AttachScriptToPrefab(prefabPath, className);
                return;
            }

            var fullFolder = scriptFolder.Replace("Assets/", Application.dataPath + "/");
            if (!Directory.Exists(fullFolder))
                Directory.CreateDirectory(fullFolder);

            var code = $@"using JulyArch;

namespace GooseMarket
{{
    public class {className} : GameUIView
    {{
        protected override void OnBeforeOpen()
        {{
            base.OnBeforeOpen();
        }}

        protected override void OnClose()
        {{
            base.OnClose();
        }}
    }}
}}
";
            File.WriteAllText(scriptPath.Replace("Assets/", Application.dataPath + "/"), code);
            Debug.Log($"PS2UGUI: 已生成 View 脚本 → {scriptPath}");

            EditorPrefs.SetString(PrefsKeyPendingClassName, className);
            EditorPrefs.SetString(PrefsKeyPendingPrefabPath, prefabPath);

            AssetDatabase.ImportAsset(scriptPath);
            AssetDatabase.Refresh();
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            var className = EditorPrefs.GetString(PrefsKeyPendingClassName, "");
            var prefabPath = EditorPrefs.GetString(PrefsKeyPendingPrefabPath, "");

            if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(prefabPath))
                return;

            EditorPrefs.DeleteKey(PrefsKeyPendingClassName);
            EditorPrefs.DeleteKey(PrefsKeyPendingPrefabPath);

            EditorApplication.delayCall += () => AttachScriptToPrefab(prefabPath, className);
        }

        private static void AttachScriptToPrefab(string prefabPath, string className)
        {
            var scriptType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(className) ?? a.GetType($"GooseMarket.{className}"))
                .FirstOrDefault(t => t != null);

            if (scriptType == null)
            {
                Debug.LogWarning($"PS2UGUI: 未找到脚本类型 {className}，请手动挂载");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogWarning($"PS2UGUI: 无法加载 Prefab → {prefabPath}");
                return;
            }

            if (root.GetComponent(scriptType) == null)
            {
                root.AddComponent(scriptType);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"PS2UGUI: 已挂载 {className} → {prefabPath}");
            }

            PrefabUtility.UnloadPrefabContents(root);
        }

        #endregion
    }
}
