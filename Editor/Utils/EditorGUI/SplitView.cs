using System;
using UnityEngine;
using UnityEditor;

namespace Yueby.Utils
{
    /// <summary>
    /// 可拖拽分割的双面板视图，支持水平或垂直分割
    /// </summary>
    public class SplitView
    {
        // 拖动状态
        private bool isDragging = false;

        // 拖拽线颜色和宽度
        private Color splitterColor = new(0f, 0f, 0f, 0.15f);
        private Color handleColor = new(1f, 1f, 1f, 0.2f);
        private float splitterWidth = 1f;
        private float handleSize = 8f;

        // 分割位置和限制
        private float splitterPosition;
        private float minSplitterPosition = 100f;
        private float maxSplitterPosition = -1f; // -1表示自动计算

        // 拖拽交互区域宽度
        private float dragAreaWidth = 10f;

        // 当前分割方向（水平或垂直）
        private SplitOrientation orientation;

        // 绘制委托
        private Action<Rect> drawLeftPanel;
        private Action<Rect> drawRightPanel;

        // 用于保存位置的键
        private string editorPrefsKey;

        /// <summary>
        /// 分割方向枚举
        /// </summary>
        public enum SplitOrientation
        {
            Horizontal, // 垂直分割线，左右布局
            Vertical    // 水平分割线，上下布局
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="initialSplitterPosition">初始分割位置</param>
        /// <param name="orientation">分割方向</param>
        /// <param name="editorPrefsKey">用于保存分割位置的EditorPrefs键</param>
        public SplitView(float initialSplitterPosition, SplitOrientation orientation = SplitOrientation.Horizontal, string editorPrefsKey = null)
        {
            this.orientation = orientation;
            this.editorPrefsKey = editorPrefsKey;

            // 如果有EditorPrefs键，尝试从EditorPrefs加载保存的位置
            if (!string.IsNullOrEmpty(editorPrefsKey) && EditorPrefs.HasKey(editorPrefsKey))
            {
                splitterPosition = EditorPrefs.GetFloat(editorPrefsKey, initialSplitterPosition);
            }
            else
            {
                splitterPosition = initialSplitterPosition;
            }
        }

        /// <summary>
        /// 设置拖拽线的外观
        /// </summary>
        /// <param name="color">拖拽线颜色</param>
        /// <param name="width">拖拽线宽度</param>
        /// <param name="handleSize">拖拽线手柄大小</param>
        public void SetSplitterStyle(Color color, float width = 2f, float handleSize = 10f)
        {
            splitterColor = color;
            splitterWidth = width;
            this.handleSize = handleSize;
        }

        /// <summary>
        /// 设置分割位置限制
        /// </summary>
        /// <param name="min">最小位置</param>
        /// <param name="max">最大位置（-1表示自动计算）</param>
        public void SetPositionLimits(float min, float max = -1)
        {
            minSplitterPosition = min;
            maxSplitterPosition = max;
        }

        /// <summary>
        /// 设置面板绘制回调
        /// </summary>
        /// <param name="leftPanelDrawer">左（上）面板绘制函数</param>
        /// <param name="rightPanelDrawer">右（下）面板绘制函数</param>
        public void SetPanelDrawers(Action<Rect> leftPanelDrawer, Action<Rect> rightPanelDrawer)
        {
            drawLeftPanel = leftPanelDrawer;
            drawRightPanel = rightPanelDrawer;
        }

        /// <summary>
        /// 获取当前分割位置
        /// </summary>
        public float SplitterPosition
        {
            get { return splitterPosition; }
            set
            {
                splitterPosition = value;
                if (!string.IsNullOrEmpty(editorPrefsKey))
                {
                    EditorPrefs.SetFloat(editorPrefsKey, splitterPosition);
                }
            }
        }

        /// <summary>
        /// 绘制分割视图
        /// </summary>
        /// <param name="rect">要绘制的矩形区域</param>
        public void OnGUI(Rect rect)
        {
            // 计算最大分割位置（如果未设置）
            float actualMaxPosition = maxSplitterPosition;
            if (actualMaxPosition < 0)
            {
                actualMaxPosition = orientation == SplitOrientation.Horizontal ?
                    rect.width - minSplitterPosition :
                    rect.height - minSplitterPosition;
            }

            // 处理鼠标事件
            ProcessMouseEvents(rect, actualMaxPosition);


            // 绘制面板内容
            DrawPanels(rect);

            // 绘制拖拽线
            DrawSplitter(rect);

        }

        /// <summary>
        /// 处理鼠标事件
        /// </summary>
        private void ProcessMouseEvents(Rect rect, float maxPosition)
        {
            Event currentEvent = Event.current;

            // 计算拖拽线的区域
            Rect splitterRect = orientation == SplitOrientation.Horizontal ?
                new Rect(rect.x + splitterPosition - dragAreaWidth / 2, rect.y, dragAreaWidth, rect.height) :
                new Rect(rect.x, rect.y + splitterPosition - dragAreaWidth / 2, rect.width, dragAreaWidth);

            // 鼠标指针样式
            EditorGUIUtility.AddCursorRect(splitterRect, orientation == SplitOrientation.Horizontal ?
                MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && splitterRect.Contains(currentEvent.mousePosition))
            {
                isDragging = true;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseUp && isDragging)
            {
                isDragging = false;
                currentEvent.Use();

                // 保存位置
                if (!string.IsNullOrEmpty(editorPrefsKey))
                {
                    EditorPrefs.SetFloat(editorPrefsKey, splitterPosition);
                }
            }
            else if (isDragging && currentEvent.type == EventType.MouseDrag)
            {
                // 更新分割位置
                if (orientation == SplitOrientation.Horizontal)
                {
                    splitterPosition += currentEvent.delta.x;
                }
                else
                {
                    splitterPosition += currentEvent.delta.y;
                }

                // 限制在合理范围内
                splitterPosition = Mathf.Clamp(splitterPosition, minSplitterPosition, maxPosition);

                currentEvent.Use();
                // 强制重绘
                if (currentEvent.type == EventType.MouseDrag)
                {
                    GUI.changed = true;
                }
            }
        }

        /// <summary>
        /// 绘制拖拽线
        /// </summary>
        private void DrawSplitter(Rect rect)
        {
            EditorUI.SetColor(splitterColor, () =>
            {
                if (orientation == SplitOrientation.Horizontal)
                {
                    // 垂直分割线（左右布局）
                    Rect splitterRect = new(rect.x + splitterPosition - splitterWidth / 2, rect.y, splitterWidth, rect.height);
                    GUI.DrawTexture(splitterRect, EditorGUIUtility.whiteTexture);
                }
                else
                {
                    // 水平分割线（上下布局）
                    Rect splitterRect = new(rect.x, rect.y + splitterPosition - splitterWidth / 2, rect.width, splitterWidth);
                    GUI.DrawTexture(splitterRect, EditorGUIUtility.whiteTexture);
                }

            });

            EditorUI.SetColor(handleColor, () =>
            {
                if (orientation == SplitOrientation.Horizontal)
                {

                    // 可选：在分割线中间绘制手柄
                    float handleY = rect.y + rect.height / 2 - handleSize / 2;
                    Rect handleRect = new(rect.x + splitterPosition - handleSize / 4, handleY, handleSize / 2, handleSize);
                    GUI.DrawTexture(handleRect, EditorGUIUtility.whiteTexture);
                }
                else
                {
                    // 可选：在分割线中间绘制手柄
                    float handleX = rect.x + rect.width / 2 - handleSize / 2;
                    Rect handleRect = new(handleX, rect.y + splitterPosition - handleSize / 4, handleSize, handleSize / 2);
                    GUI.DrawTexture(handleRect, EditorGUIUtility.whiteTexture);
                }

            });
        }

        /// <summary>
        /// 绘制两个面板内容
        /// </summary>
        private void DrawPanels(Rect rect)
        {
            Rect leftPanelRect, rightPanelRect;

            if (orientation == SplitOrientation.Horizontal)
            {
                // 左右布局
                leftPanelRect = new Rect(rect.x, rect.y, splitterPosition - splitterWidth / 2, rect.height);
                rightPanelRect = new Rect(rect.x + splitterPosition + splitterWidth / 2, rect.y,
                    rect.width - splitterPosition - splitterWidth / 2, rect.height);
            }
            else
            {
                // 上下布局
                leftPanelRect = new Rect(rect.x, rect.y, rect.width, splitterPosition - splitterWidth / 2);
                rightPanelRect = new Rect(rect.x, rect.y + splitterPosition + splitterWidth / 2,
                    rect.width, rect.height - splitterPosition - splitterWidth / 2);
            }

            // 调用绘制回调
            if (drawLeftPanel != null)
            {
                drawLeftPanel(leftPanelRect);
            }

            if (drawRightPanel != null)
            {
                drawRightPanel(rightPanelRect);
            }
        }
    }


}