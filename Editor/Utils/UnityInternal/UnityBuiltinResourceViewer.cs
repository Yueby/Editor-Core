using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Yueby
{
    public class UnityBuiltinResourceViewer : EditorWindow
    {
        private const float ICON_SIZE = 48;
        private const float ICON_PADDING = 4;
        private const float LEFT_PANEL_WIDTH = 80;
        private const string PREFS_KEY_SELECTED_TAB = "UnityBuiltinResourceViewer_SelectedTab";
        private const string PREFS_KEY_SEARCH_TEXT = "UnityBuiltinResourceViewer_SearchText";
        private const string PREFS_KEY_VIEW_MODE = "UnityBuiltinResourceViewer_ViewMode";
        private const float MIN_ICON_SIZE = 24;
        private const float MAX_ICON_SIZE = 128;
        private const string PREFS_KEY_ICON_SIZE = "UnityBuiltinResourceViewer_IconSize";

        private const int BATCH_SIZE = 1000;
        private const int YIELD_INTERVAL = 100;
        private const int MAX_POOL_SIZE = 200;
        private const int SMALL_ICON_SIZE = 16;
        private const int MEDIUM_ICON_SIZE = 32;

        private enum IconCategory
        {
            All,
            Dark,
            Light,
            Small,
            Medium,
            Large
        }

        private class IconData
        {
            public GUIContent content;
            public Vector2 size;
            public bool isDark;
        }

        private class GridItem
        {
            public Rect rect;
            public string name;
            public bool isSelected;
            public Texture2D texture;
        }

        private List<string> allIconNames = new List<string>();
        private List<string> filteredIconNames = new List<string>();
        private List<GUIStyle> allStyles = new List<GUIStyle>();
        private List<GUIStyle> filteredStyles = new List<GUIStyle>();
        private int selectedTab = 0;
        private bool isStyleTab => selectedTab == 1;
        private bool isLoading = false;
        private float loadingProgress = 0f;
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle gridItemStyle;
        private GUIStyle selectedGridItemStyle;
        private bool stylesInitialized = false;
        private Texture2D boxTexture;
        private Texture2D selectedBoxTexture;
        private bool useListView = false;
        private int currentLoadingIndex = 0;
        private List<string> pendingIconNames = new List<string>();
        private bool isLoadingBatch = false;
        private float currentIconSize;
        private IconCategory currentCategory = IconCategory.All;
        private Vector2 gridScrollPosition;
        private string searchText = "";
        private string selectedIconName;
        private Texture2D selectedIconTexture;
        private Dictionary<string, IconData> iconDataCache = new Dictionary<string, IconData>();
        private List<GridItem> visibleItems = new List<GridItem>();
        private Queue<GridItem> itemPool = new Queue<GridItem>();
        private int visibleStartIndex;
        private int visibleEndIndex;
        private int columnCount;

        [MenuItem("Tools/YuebyTools/Utils/Builtin Resource Viewer", false, 1)]
        static void Init()
        {
            UnityBuiltinResourceViewer window = GetWindow<UnityBuiltinResourceViewer>();
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        void OnEnable()
        {
            LoadPrefs();
            LoadDataAsync();
        }

        void OnDisable()
        {
            SavePrefs();
            if (boxTexture != null)
            {
                DestroyImmediate(boxTexture);
                boxTexture = null;
            }
            if (selectedBoxTexture != null)
            {
                DestroyImmediate(selectedBoxTexture);
                selectedBoxTexture = null;
            }

            // 清理对象池
            visibleItems.Clear();
            itemPool.Clear();
        }

        void InitStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(4, 4, 8, 8)
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(4, 4, 4, 4)
            };

            boxTexture = CreateBoxTexture(new Color(0.2f, 0.2f, 0.2f, 0.1f));
            selectedBoxTexture = CreateBoxTexture(new Color(0.2f, 0.4f, 0.8f, 0.3f));

            gridItemStyle = new GUIStyle(GUI.skin.box)
            {
                margin = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(2, 2, 2, 2),
                normal = { background = boxTexture }
            };

            selectedGridItemStyle = new GUIStyle(gridItemStyle)
            {
                normal = { background = selectedBoxTexture }
            };

            stylesInitialized = true;
        }

        Texture2D CreateBoxTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        void LoadPrefs()
        {
            selectedTab = EditorPrefs.GetInt(PREFS_KEY_SELECTED_TAB, 0);
            searchText = EditorPrefs.GetString(PREFS_KEY_SEARCH_TEXT, "");
            useListView = EditorPrefs.GetBool(PREFS_KEY_VIEW_MODE, false);
            currentIconSize = EditorPrefs.GetFloat(PREFS_KEY_ICON_SIZE, ICON_SIZE);
        }

        void SavePrefs()
        {
            EditorPrefs.SetInt(PREFS_KEY_SELECTED_TAB, selectedTab);
            EditorPrefs.SetString(PREFS_KEY_SEARCH_TEXT, searchText);
            EditorPrefs.SetBool(PREFS_KEY_VIEW_MODE, useListView);
            EditorPrefs.SetFloat(PREFS_KEY_ICON_SIZE, currentIconSize);
        }

        void TryEnqueueToPool(GridItem item)
        {
            if (itemPool.Count < MAX_POOL_SIZE)
            {
                itemPool.Enqueue(item);
            }
        }

        GridItem GetOrCreateGridItem()
        {
            return itemPool.Count > 0 ? itemPool.Dequeue() : new GridItem();
        }

        async Task LoadNextBatch()
        {
            if (isLoadingBatch) return;
            isLoadingBatch = true;

            try
            {
                int endIndex = Mathf.Min(currentLoadingIndex + BATCH_SIZE, pendingIconNames.Count);
                var batchIcons = new List<(string name, GUIContent content)>();

                // 先批量获取所有图标内容
                for (int i = currentLoadingIndex; i < endIndex; i++)
                {
                    string iconName = pendingIconNames[i];
                    try
                    {
                        var content = EditorGUIUtility.IconContent(iconName);
                        if (content != null && content.image != null)
                        {
                            batchIcons.Add((iconName, content));
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Processing icon {iconName} failed: {e.Message}");
                    }

                    if ((i - currentLoadingIndex) % YIELD_INTERVAL == 0 && i > currentLoadingIndex)
                    {
                        await Task.Yield();
                    }
                }

                // 批量处理收集到的图标
                foreach (var (iconName, content) in batchIcons)
                {
                    allIconNames.Add(iconName);
                    var iconData = new IconData
                    {
                        content = content,
                        size = new Vector2(((Texture2D)content.image).width, ((Texture2D)content.image).height),
                        isDark = iconName.StartsWith("d_")
                    };
                    iconDataCache[iconName] = iconData;
                }

                currentLoadingIndex = endIndex;
            }
            finally
            {
                isLoadingBatch = false;
            }
        }

        async void LoadDataAsync()
        {
            isLoading = true;
            loadingProgress = 0f;

            try
            {
                // 预分配容量以减少重新分配
                allIconNames = new List<string>(2000);
                iconDataCache = new Dictionary<string, IconData>(2000);

                // 在主线程加载内置样式
                allStyles.Clear();
                var skin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);
                var uniqueStyles = new HashSet<string>();
                foreach (var style in skin.customStyles)
                {
                    if (style.normal.background != null && !uniqueStyles.Contains(style.name))
                    {
                        uniqueStyles.Add(style.name);
                        allStyles.Add(style);
                    }
                }
                loadingProgress = 0.1f;

                // 在主线程获取资源
                var editorAssetBundle = GetEditorAssetBundle();
                var iconsPath = GetIconsPath();
                var assetNames = editorAssetBundle.GetAllAssetNames();
                loadingProgress = 0.2f;

                // 预处理和过滤资源名称 - 优化过滤逻辑
                pendingIconNames = new List<string>(2000);
                foreach (var name in assetNames)
                {
                    if (name.StartsWith(iconsPath, StringComparison.OrdinalIgnoreCase) &&
                        (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                         name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
                    {
                        pendingIconNames.Add(Path.GetFileNameWithoutExtension(name));
                    }
                }
                pendingIconNames = pendingIconNames.Distinct().ToList();
                loadingProgress = 0.3f;

                // 处理图标 - 优化批处理逻辑
                currentLoadingIndex = 0;
                var batchIcons = new List<(string name, GUIContent content)>(BATCH_SIZE);

                while (currentLoadingIndex < pendingIconNames.Count)
                {
                    batchIcons.Clear();
                    int endIndex = Mathf.Min(currentLoadingIndex + BATCH_SIZE, pendingIconNames.Count);
                    
                    // 批量收集图标内容
                    for (int i = currentLoadingIndex; i < endIndex; i++)
                    {
                        string iconName = pendingIconNames[i];
                        try
                        {
                            var content = EditorGUIUtility.IconContent(iconName);
                            if (content != null && content.image != null)
                            {
                                batchIcons.Add((iconName, content));
                            }
                        }
                        catch (Exception)
                        {
                            // 忽略加载失败的图标
                            continue;
                        }

                        if ((i - currentLoadingIndex) % YIELD_INTERVAL == 0 && i > currentLoadingIndex)
                        {
                            loadingProgress = 0.3f + 0.7f * ((float)i / pendingIconNames.Count);
                            await Task.Yield();
                        }
                    }

                    // 批量处理收集到的图标
                    foreach (var (iconName, content) in batchIcons)
                    {
                        allIconNames.Add(iconName);
                        iconDataCache[iconName] = new IconData
                        {
                            content = content,
                            size = new Vector2(((Texture2D)content.image).width, ((Texture2D)content.image).height),
                            isDark = iconName.StartsWith("d_")
                        };
                    }

                    currentLoadingIndex = endIndex;
                    loadingProgress = 0.3f + 0.7f * ((float)currentLoadingIndex / pendingIconNames.Count);
                    await Task.Yield();
                }

                // 最终处理
                allIconNames.Sort();
                FilterItems();
            }
            catch (Exception e)
            {
                Debug.LogError($"Loading resources failed: {e}");
            }
            finally
            {
                isLoading = false;
                pendingIconNames.Clear();
                pendingIconNames.TrimExcess(); // 释放多余内存
                Repaint();
            }
        }

        void OnGUI()
        {
            if (!stylesInitialized)
            {
                InitStyles();
            }

            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawResizer();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();

            // 在加载时显示进度条
            if (isLoading)
            {
                var rect = position;
                rect.y = rect.height - 1; // 贴着窗口底部
                rect.height = 1; // 减小高度使其更细
                rect.x = 0; // 从窗口最左边开始
                rect.width = position.width; // 延伸到窗口最右边
                EditorGUI.ProgressBar(rect, loadingProgress, "");
            }

            if (GUI.changed)
            {
                Repaint();
            }
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            string[] tabs = new string[] { "Icons", "Styles" };
            using (new EditorGUI.DisabledScope(isLoading)) // 加载时禁用标签切换
            {
                EditorGUI.BeginChangeCheck();
                selectedTab = GUILayout.Toolbar(selectedTab, tabs, EditorStyles.toolbarButton, GUILayout.Width(200));
                if (EditorGUI.EndChangeCheck())
                {
                    selectedIconName = null;
                    selectedIconTexture = null;
                    FilterItems();
                }
            }

            if (!isStyleTab)
            {
                GUILayout.Space(10);
                using (new EditorGUI.DisabledScope(isLoading)) // 加载时禁用分类选择
                {
                    EditorGUI.BeginChangeCheck();
                    currentCategory = (IconCategory)EditorGUILayout.EnumPopup(currentCategory, EditorStyles.toolbarPopup, GUILayout.Width(80));
                    if (EditorGUI.EndChangeCheck())
                    {
                        FilterItems();
                    }
                }
            }

            GUILayout.FlexibleSpace();

            if (isLoading)
            {
                EditorGUILayout.LabelField($"Loading... {(loadingProgress * 100):F0}%", EditorStyles.miniLabel);
            }
            else if (isStyleTab)
            {
                EditorGUILayout.LabelField($"Styles: {allStyles.Count}", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField($"Icons: {filteredIconNames.Count}/{allIconNames.Count}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LEFT_PANEL_WIDTH), GUILayout.MaxWidth(LEFT_PANEL_WIDTH));

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.miniLabel);
            }

            if (selectedIconName != null)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // 预览区域宽度
                    float previewSize = LEFT_PANEL_WIDTH - 48; // 增加边距，确保不会撑开面板
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace(); // 使用FlexibleSpace来居中
                        Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize);
                        if (isStyleTab)
                        {
                            var style = filteredStyles.FirstOrDefault(s => s.name == selectedIconName);
                            if (style?.normal.background != null)
                            {
                                GUI.DrawTexture(previewRect, style.normal.background, ScaleMode.ScaleToFit);
                            }
                        }
                        else if (selectedIconTexture != null)
                        {
                            GUI.DrawTexture(previewRect, selectedIconTexture, ScaleMode.ScaleToFit);
                        }
                        GUILayout.FlexibleSpace(); // 使用FlexibleSpace来居中
                    }

                    EditorGUILayout.Space(1);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(2);
                        EditorGUILayout.LabelField("Name", EditorStyles.miniLabel);
                        GUILayout.Space(2);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(2);
                        EditorGUI.SelectableLabel(EditorGUILayout.GetControlRect(false, 28), selectedIconName, EditorStyles.textField);
                        GUILayout.Space(2);
                    }

                    if (!isStyleTab)
                    {
                        EditorGUILayout.Space(1);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(2);
                            EditorGUILayout.LabelField("Code", EditorStyles.miniLabel);
                            GUILayout.Space(2);
                        }
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(2);
                            string codeExample = selectedIconName.StartsWith("d_") ?
                                $"EditorGUIUtility.IconContent(\"{selectedIconName}\") // Dark" :
                                $"EditorGUIUtility.IconContent(\"{selectedIconName}\")";
                            EditorGUI.SelectableLabel(EditorGUILayout.GetControlRect(false, 32), codeExample, EditorStyles.textField);
                            GUILayout.Space(2);
                        }

                        if (selectedIconTexture != null)
                        {
                            EditorGUILayout.Space(1);
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Space(2);
                                EditorGUILayout.LabelField($"{selectedIconTexture.width}×{selectedIconTexture.height}", EditorStyles.miniLabel);
                                GUILayout.Space(2);
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.Space(1);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(2);
                            EditorGUILayout.LabelField("Code", EditorStyles.miniLabel);
                            GUILayout.Space(2);
                        }
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(2);
                            string codeExample = $"new GUIStyle(\"{selectedIconName}\")";
                            EditorGUI.SelectableLabel(EditorGUILayout.GetControlRect(false, 32), codeExample, EditorStyles.textField);
                            GUILayout.Space(2);
                        }
                    }
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"Select a {(isStyleTab ? "style" : "icon")}", MessageType.Info);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 搜索区域
            using (new EditorGUI.DisabledScope(isLoading)) // 加载时禁用搜索
            {
                EditorGUI.BeginChangeCheck();
                searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField);
                if (EditorGUI.EndChangeCheck())
                {
                    FilterItems();
                }
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    searchText = "";
                    FilterItems();
                }
            }

            GUILayout.Space(10);

            // 视图切换
            using (new EditorGUI.DisabledScope(isLoading)) // 加载时禁用视图切换
            {
                EditorGUI.BeginChangeCheck();
                useListView = GUILayout.Toggle(useListView, useListView ? "List" : "Grid", EditorStyles.toolbarButton, GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck())
                {
                    SavePrefs();
                }
            }

            // 网格视图缩放控制
            if (!useListView)
            {
                GUILayout.Space(10);
                using (new EditorGUI.DisabledScope(isLoading)) // 加载时禁用缩放
                {
                    EditorGUI.BeginChangeCheck();
                    currentIconSize = GUILayout.HorizontalSlider(currentIconSize, MIN_ICON_SIZE, MAX_ICON_SIZE, GUILayout.Width(100));
                    if (EditorGUI.EndChangeCheck())
                    {
                        SavePrefs();
                    }
                }
                GUILayout.Label($"{Mathf.RoundToInt(currentIconSize)}px", EditorStyles.miniLabel, GUILayout.Width(40));
            }

            EditorGUILayout.EndHorizontal();

            float contentAreaHeight = position.height - EditorStyles.toolbar.fixedHeight * 2;

            if (useListView)
            {
                DrawListView(contentAreaHeight);
            }
            else
            {
                DrawGridView(contentAreaHeight);
            }

            EditorGUILayout.EndVertical();
        }

        void DrawTextureInRect(Texture2D texture, Rect rect)
        {
            if (texture == null) return;

            // 如果图标尺寸小于等于显示区域，使用原始尺寸居中显示
            if (texture.width <= rect.width && texture.height <= rect.height)
            {
                float x = rect.x + (rect.width - texture.width) * 0.5f;
                float y = rect.y + (rect.height - texture.height) * 0.5f;
                GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
            }
            else
            {
                // 否则等比缩放显示
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            }
        }

        void DrawListView(float viewHeight)
        {
            var viewRect = GUILayoutUtility.GetRect(0, viewHeight);
            float viewWidth = viewRect.width - GUI.skin.verticalScrollbar.fixedWidth;

            int itemCount = isStyleTab ? filteredStyles.Count : filteredIconNames.Count;
            float itemHeight = EditorGUIUtility.singleLineHeight + 2; // 减小行间距
            float contentHeight = itemCount * itemHeight;

            gridScrollPosition = GUI.BeginScrollView(viewRect, gridScrollPosition,
                new Rect(0, 0, viewWidth, contentHeight));

            // 计算可见范围
            float scrollY = gridScrollPosition.y;
            int startIndex = Mathf.Max(0, Mathf.FloorToInt(scrollY / itemHeight) - 1);
            int endIndex = Mathf.Min(itemCount, startIndex + Mathf.CeilToInt(viewRect.height / itemHeight) + 2);

            // 回收所有项到对象池
            foreach (var item in visibleItems)
            {
                TryEnqueueToPool(item);
            }
            visibleItems.Clear();

            // 绘制可见项
            for (int i = startIndex; i < endIndex; i++)
            {
                string itemName = isStyleTab ? filteredStyles[i].name : filteredIconNames[i];
                bool isSelected = itemName == selectedIconName;

                // 从对象池获取或创建新项
                var item = GetOrCreateGridItem();
                visibleItems.Add(item);

                float yPos = i * itemHeight;
                float iconSize = EditorGUIUtility.singleLineHeight - 2; // 减小图标大小
                Rect itemRect = new Rect(0, yPos, viewWidth, itemHeight);
                Rect iconRect = new Rect(2, yPos + 1, iconSize, iconSize); // 减小左边距和上边距

                // 更新项数据
                item.rect = iconRect;
                item.name = itemName;
                item.isSelected = isSelected;

                // 绘制背景
                if (isSelected)
                {
                    EditorGUI.DrawRect(itemRect, new Color(0.2f, 0.4f, 0.8f, 0.3f));
                }
                else if (i % 2 == 0)
                {
                    EditorGUI.DrawRect(itemRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
                }

                // 绘制图标
                if (isStyleTab)
                {
                    var style = filteredStyles[i];
                    item.texture = style.normal.background;
                    if (item.texture != null)
                    {
                        DrawTextureInRect(item.texture, iconRect);
                    }
                }
                else
                {
                    var content = GetIconContent(item.name);
                    item.texture = content?.image as Texture2D;
                    if (item.texture != null)
                    {
                        DrawTextureInRect(item.texture, iconRect);
                    }
                }

                // 绘制名称
                Rect labelRect = new Rect(iconSize + 6, yPos + 1, // 减小文本左边距
                    viewWidth - iconSize - 8, EditorGUIUtility.singleLineHeight);
                GUI.Label(labelRect, new GUIContent(item.name, item.name), EditorStyles.label);

                // 处理点击
                if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
                {
                    if (isStyleTab)
                        SelectStyle(filteredStyles[i]);
                    else
                        SelectIcon(item.name);
                    Event.current.Use();
                }
            }

            GUI.EndScrollView();
        }

        void DrawGridView(float viewHeight)
        {
            Rect viewRect = GUILayoutUtility.GetRect(0, viewHeight);
            float viewWidth = viewRect.width - GUI.skin.verticalScrollbar.fixedWidth;
            float totalItemSize = currentIconSize + ICON_PADDING * 2;

            columnCount = Mathf.FloorToInt(viewWidth / totalItemSize);
            if (columnCount < 1) columnCount = 1;

            // 重新计算实际的图标间距，以便均匀分布
            float availableWidth = viewWidth - GUI.skin.verticalScrollbar.fixedWidth;
            float actualItemWidth = availableWidth / columnCount;
            float actualPadding = (actualItemWidth - currentIconSize) / 2;

            int itemCount = isStyleTab ? filteredStyles.Count : filteredIconNames.Count;
            int rowCount = Mathf.CeilToInt((float)itemCount / columnCount);
            float contentHeight = rowCount * totalItemSize;  // 移除标签高度

            gridScrollPosition = GUI.BeginScrollView(viewRect, gridScrollPosition,
                new Rect(0, 0, viewWidth, contentHeight));

            // 计算可见范围
            float scrollY = gridScrollPosition.y;
            int startRow = Mathf.Max(0, Mathf.FloorToInt(scrollY / totalItemSize) - 1);
            int endRow = Mathf.Min(rowCount, startRow + Mathf.CeilToInt(viewRect.height / totalItemSize) + 2);

            visibleStartIndex = startRow * columnCount;
            visibleEndIndex = Mathf.Min(itemCount, endRow * columnCount);

            // 回收所有项到对象池
            foreach (var item in visibleItems)
            {
                TryEnqueueToPool(item);
            }
            visibleItems.Clear();

            // 绘制可见项
            for (int i = visibleStartIndex; i < visibleEndIndex; i++)
            {
                int row = i / columnCount;
                int col = i % columnCount;

                float x = col * actualItemWidth + actualPadding;
                float y = row * totalItemSize + ICON_PADDING;

                string itemName = isStyleTab ? filteredStyles[i].name : filteredIconNames[i];
                bool isSelected = itemName == selectedIconName;

                // 从对象池获取或创建新项
                var item = GetOrCreateGridItem();
                visibleItems.Add(item);

                // 更新项数据
                Rect iconRect = new Rect(x, y, currentIconSize, currentIconSize);
                item.rect = iconRect;
                item.name = itemName;
                item.isSelected = isSelected;

                // 绘制背景和边框
                Rect itemRect = new Rect(x - actualPadding, y - ICON_PADDING, 
                    actualItemWidth, currentIconSize + ICON_PADDING * 2);
                if (isSelected)
                {
                    GUI.Box(itemRect, "", selectedGridItemStyle);
                }

                // 绘制图标
                if (isStyleTab)
                {
                    var style = filteredStyles[i];
                    item.texture = style.normal.background;
                }
                else
                {
                    var content = GetIconContent(item.name);
                    item.texture = content?.image as Texture2D;
                }

                if (item.texture != null)
                {
                    DrawTextureInRect(item.texture, iconRect);
                }

                // 处理点击
                if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
                {
                    if (isStyleTab)
                        SelectStyle(filteredStyles[i]);
                    else
                        SelectIcon(item.name);
                    Event.current.Use();
                }
            }

            GUI.EndScrollView();
        }

        void DrawResizer()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(2));
            GUI.Box(GUILayoutUtility.GetRect(2, position.height), "", "EyeDropperVerticalLine");
            EditorGUILayout.EndVertical();
        }

        void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (selectedIconName != null)
            {
                EditorGUILayout.LabelField($"Selected: {selectedIconName}");
            }
            GUILayout.FlexibleSpace();

            int totalCount = isStyleTab ? allStyles.Count : allIconNames.Count;
            int filteredCount = isStyleTab ? filteredStyles.Count : filteredIconNames.Count;

            if (filteredCount != totalCount)
            {
                EditorGUILayout.LabelField($"{filteredCount}/{totalCount}");
            }
            EditorGUILayout.EndHorizontal();
        }

        void FilterItems()
        {
            if (isStyleTab)
            {
                if (string.IsNullOrEmpty(searchText))
                {
                    filteredStyles = new List<GUIStyle>(allStyles);
                }
                else
                {
                    var searchTerms = searchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    filteredStyles = allStyles
                        .Where(style => searchTerms.All(term =>
                            style.name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();
                }
                return;
            }

            // 图标过滤
            var query = allIconNames.AsEnumerable();

            // 应用分类过滤
            switch (currentCategory)
            {
                case IconCategory.Dark:
                    query = query.Where(name => name.StartsWith("d_"));
                    break;
                case IconCategory.Light:
                    query = query.Where(name => !name.StartsWith("d_"));
                    break;
                case IconCategory.Small:
                    query = query.Where(name =>
                    {
                        var size = GetIconSize(name);
                        return size.x <= SMALL_ICON_SIZE && size.y <= SMALL_ICON_SIZE;
                    });
                    break;
                case IconCategory.Medium:
                    query = query.Where(name =>
                    {
                        var size = GetIconSize(name);
                        return size.x > SMALL_ICON_SIZE && size.x <= MEDIUM_ICON_SIZE &&
                               size.y > SMALL_ICON_SIZE && size.y <= MEDIUM_ICON_SIZE;
                    });
                    break;
                case IconCategory.Large:
                    query = query.Where(name =>
                    {
                        var size = GetIconSize(name);
                        return size.x > MEDIUM_ICON_SIZE || size.y > MEDIUM_ICON_SIZE;
                    });
                    break;
            }

            // 应用搜索过滤
            if (!string.IsNullOrEmpty(searchText))
            {
                var searchTerms = searchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                query = query.Where(name => searchTerms.All(term =>
                    name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            filteredIconNames = query.ToList();
        }

        GUIContent GetIconContent(string iconName)
        {
            if (iconDataCache.TryGetValue(iconName, out var iconData))
            {
                return iconData.content;
            }
            return null;
        }

        Vector2 GetIconSize(string iconName)
        {
            if (iconDataCache.TryGetValue(iconName, out var iconData))
            {
                return iconData.size;
            }
            return Vector2.zero;
        }

        void SelectIcon(string iconName)
        {
            if (selectedIconName == iconName) return;

            selectedIconName = iconName;
            var content = GetIconContent(iconName);
            selectedIconTexture = content?.image as Texture2D;
            GUI.changed = true;
            Repaint();
        }

        void SelectStyle(GUIStyle style)
        {
            if (selectedIconName == style.name) return;

            selectedIconName = style.name;
            selectedIconTexture = style.normal.background;
            GUI.changed = true;
            Repaint();
        }

        GUIStyle GetStyleByName(string styleName)
        {
            return allStyles.FirstOrDefault(s => s.name == styleName);
        }

        private static AssetBundle GetEditorAssetBundle()
        {
            var method = typeof(EditorGUIUtility).GetMethod("GetEditorAssetBundle", BindingFlags.NonPublic | BindingFlags.Static);
            return (AssetBundle)method.Invoke(null, null);
        }

        private static string GetIconsPath()
        {
#if UNITY_2018_3_OR_NEWER
            return UnityEditor.Experimental.EditorResources.iconsPath;
#else
            var assembly = typeof(EditorGUIUtility).Assembly;
            var editorResourcesUtility = assembly.GetType("UnityEditorInternal.EditorResourcesUtility");
            var iconsPathProperty = editorResourcesUtility.GetProperty(
                "iconsPath",
                BindingFlags.Static | BindingFlags.Public);
            return (string)iconsPathProperty.GetValue(null, null);
#endif
        }

        private static IEnumerable<string> EnumerateIcons(AssetBundle editorAssetBundle, string iconsPath)
        {
            foreach (var assetName in editorAssetBundle.GetAllAssetNames())
            {
                if (assetName.StartsWith(iconsPath, StringComparison.OrdinalIgnoreCase) &&
                    (assetName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     assetName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return assetName;
                }
            }
        }
    }
}