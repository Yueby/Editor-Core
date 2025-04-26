using UnityEngine;
using UnityEditor;
using Yueby.Utils;
using System;

namespace Yueby.Examples
{
    /// <summary>
    /// 简单的拖拽示例窗口
    /// 演示如何使用DragDropHandler类实现拖放功能
    /// </summary>
    public class SimpleDragDropExample : EditorWindow
    {
        // 拖放处理器
        private DragDropHandler materialDropHandler;

        // 已拖放的材质
        private Material[] droppedMaterials;

        // 设置
        private Color dropAreaColor = new Color(0.3f, 0.6f, 0.9f);
        private Vector2 scrollPosition;

        [MenuItem("Examples/Simple Drag Drop Example")]
        public static void ShowWindow()
        {
            var window = GetWindow<SimpleDragDropExample>("拖拽示例");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            // 初始化拖拽处理器
            materialDropHandler = new DragDropHandler
            {
                AcceptedTypes = new[] { typeof(Material) },
                HighlightColor = dropAreaColor,
                ShowVisualFeedback = true,
                ShowOnlyWhenDragging = true // 只在拖拽时显示拖拽区域
            };

            // 添加自定义绘制回调，用于显示拖放提示文本
            materialDropHandler.OnDrawOverlay = (rect, state, isAccepted) =>
            {
                // 创建文本样式
                var labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };

                // 根据不同状态显示不同文本
                if (state != DragDropState.None) // 只要有拖拽操作就显示文本
                {
                    if (isAccepted && (state == DragDropState.Over || state == DragDropState.Enter))
                    {
                        // 可以接受的拖拽对象，且在区域内
                        labelStyle.normal.textColor = dropAreaColor;
                        GUI.Label(rect, "可以拖放材质到这里", labelStyle);
                    }
                    else if (!isAccepted && (state == DragDropState.Over || state == DragDropState.Enter))
                    {
                        // 不可接受的拖拽对象，且在区域内
                        labelStyle.normal.textColor = new Color(0.8f, 0.2f, 0.2f, 0.8f);
                        GUI.Label(rect, "不支持该类型的对象", labelStyle);
                    }
                    else if (state == DragDropState.Exit)
                    {
                        // 拖拽对象离开区域
                        labelStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                        GUI.Label(rect, "请将材质拖放到这里", labelStyle);
                    }
                }
            };

            // 注册拖放事件回调
            materialDropHandler.OnObjectsDropped = objects =>
            {
                // 转换为材质数组
                droppedMaterials = new Material[objects.Length];
                for (int i = 0; i < objects.Length; i++)
                {
                    droppedMaterials[i] = objects[i] as Material;
                }

                Debug.Log($"拖放了 {objects.Length} 个材质");
            };
        }

        private void OnGUI()
        {
            // 开始滚动视图
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // 绘制标题
            EditorGUILayout.Space(10);
            GUILayout.Label("拖拽示例", EditorStyles.boldLabel);

            // 绘制说明
            EditorGUILayout.HelpBox("这个示例展示了如何使用DragDropHandler类实现拖放功能。\n" +
                                   "您可以将材质拖放到下方的设置区域中。", MessageType.Info);

            // 设置区域
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("设置区域" + (materialDropHandler.IsDragging ? " (正在拖拽)" : ""), EditorStyles.boldLabel);

            // 绘制设置项
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(5);

            // 设置项
            EditorGUILayout.LabelField("这是设置区域的内容", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("请将材质拖放到这个区域");
            EditorGUILayout.Space(5);

            dropAreaColor = EditorGUILayout.ColorField("拖放区域颜色", dropAreaColor);
            materialDropHandler.HighlightColor = dropAreaColor;

            // 拖拽显示设置
            materialDropHandler.ShowOnlyWhenDragging = EditorGUILayout.Toggle("只在拖拽时显示", materialDropHandler.ShowOnlyWhenDragging);

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();

            // 获取设置区域的矩形
            Rect settingsRect = GUILayoutUtility.GetLastRect();

            // 处理拖放
            materialDropHandler.DrawDragAndDrop(settingsRect);

            // 注意：拖放的对象已经在OnObjectsDropped回调中处理

            // 显示已拖放的材质
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("已拖放的材质", EditorStyles.boldLabel);

            if (droppedMaterials != null && droppedMaterials.Length > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                foreach (var material in droppedMaterials)
                {
                    if (material == null) continue;

                    EditorGUILayout.BeginHorizontal();

                    // 显示材质预览
                    var previewRect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
                    EditorGUI.DrawPreviewTexture(previewRect, AssetPreview.GetAssetPreview(material));

                    // 显示材质信息
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(material.name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Shader: {material.shader.name}");
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();

                // 清空按钮
                if (GUILayout.Button("清空列表"))
                {
                    droppedMaterials = null;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("尚未拖放任何材质。", MessageType.Info);
            }

            // 结束滚动视图
            EditorGUILayout.EndScrollView();

            // 重绘窗口以更新UI
            if (materialDropHandler.IsDragging)
            {
                Repaint();
            }
        }
    }
}
