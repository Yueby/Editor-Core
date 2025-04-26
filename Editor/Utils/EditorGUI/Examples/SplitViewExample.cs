using UnityEngine;
using UnityEditor;

namespace Yueby.Utils.Examples
{
    /// <summary>
    /// SplitView使用示例窗口
    /// </summary>
    public class SplitViewExample : EditorWindow
    {
        private SplitView horizontalSplitView;
        private SplitView verticalSplitView;
        private Vector2 leftScroll = Vector2.zero;
        private Vector2 rightScroll = Vector2.zero;
        private Vector2 bottomScroll = Vector2.zero;
        private string leftText = "左侧面板内容";
        private string rightText = "右侧面板内容";
        private string bottomText = "底部面板内容";
        
        [MenuItem("Tools/YuebyTools/GUIExamples/SplitViewExample")]
        public static void ShowWindow()
        {
            SplitViewExample window = GetWindow<SplitViewExample>();
            window.titleContent = new GUIContent("分割视图示例");
            window.Show();
        }
        
        private void OnEnable()
        {
            // 创建一个水平分割视图（左右布局）
            horizontalSplitView = new SplitView(
                position.width * 0.3f, 
                SplitView.SplitOrientation.Horizontal,
                "Yueby.SplitView.HorizontalPosition"
            );
            
            // 设置分割线样式
            horizontalSplitView.SetSplitterStyle(new Color(0.5f, 0.5f, 0.5f, 1.0f), 1f, 8f);
            
            // 设置位置限制
            horizontalSplitView.SetPositionLimits(100f, position.width - 100f);
            
            // 设置左右面板的绘制回调
            horizontalSplitView.SetPanelDrawers(DrawLeftPanel, DrawRightPanelWithVerticalSplit);
            
            // 创建一个垂直分割视图（上下布局）
            verticalSplitView = new SplitView(
                position.height * 0.7f,
                SplitView.SplitOrientation.Vertical,
                "Yueby.SplitView.VerticalPosition"
            );
            
            // 设置分割线样式
            verticalSplitView.SetSplitterStyle(new Color(0.5f, 0.5f, 0.5f, 1.0f), 1f, 8f);
            
            // 设置绘制回调
            verticalSplitView.SetPanelDrawers(DrawRightTopPanel, DrawRightBottomPanel);
        }
        
        private void OnGUI()
        {
            // 自动调整垂直分割视图的最大位置
            verticalSplitView.SetPositionLimits(100f, position.height - 100f);
            
            // 绘制整个分割视图
            horizontalSplitView.OnGUI(new Rect(0, 0, position.width, position.height));
        }
        
        /// <summary>
        /// 绘制左侧面板
        /// </summary>
        private void DrawLeftPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("左侧面板", EditorStyles.boldLabel);
            
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            
            leftText = EditorGUILayout.TextArea(leftText, GUILayout.ExpandHeight(true));
            
            EditorGUILayout.EndScrollView();
            
            if (GUILayout.Button("测试按钮"))
            {
                Debug.Log("左侧面板按钮被点击");
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// 绘制右侧面板（包含垂直分割）
        /// </summary>
        private void DrawRightPanelWithVerticalSplit(Rect rect)
        {
            // 在右侧面板中使用垂直分割视图
            verticalSplitView.OnGUI(rect);
        }
        
        /// <summary>
        /// 绘制右侧顶部面板
        /// </summary>
        private void DrawRightTopPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("右侧顶部面板", EditorStyles.boldLabel);
            
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            
            rightText = EditorGUILayout.TextArea(rightText, GUILayout.ExpandHeight(true));
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// 绘制右侧底部面板
        /// </summary>
        private void DrawRightBottomPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("右侧底部面板", EditorStyles.boldLabel);
            
            bottomScroll = EditorGUILayout.BeginScrollView(bottomScroll);
            
            bottomText = EditorGUILayout.TextArea(bottomText, GUILayout.ExpandHeight(true));
            
            EditorGUILayout.EndScrollView();
            
            if (GUILayout.Button("测试按钮"))
            {
                Debug.Log("底部面板按钮被点击");
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
} 