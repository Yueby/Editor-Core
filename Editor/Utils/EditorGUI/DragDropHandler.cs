using System;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Yueby.Utils
{
    /// <summary>
    /// 拖拽状态枚举
    /// </summary>
    public enum DragDropState
    {
        /// <summary>无拖拽</summary>
        None,
        /// <summary>拖拽进入区域</summary>
        Enter,
        /// <summary>拖拽在区域内</summary>
        Over,
        /// <summary>拖拽离开区域</summary>
        Exit,
        /// <summary>放下</summary>
        Drop
    }

    /// <summary>
    /// 拖拽处理器类，用于处理编辑器中的拖拽操作
    /// </summary>
    public class DragDropHandler
    {
        #region 属性

        /// <summary>
        /// 拖拽区域
        /// </summary>
        public Rect DropArea { get; set; }

        /// <summary>
        /// 当前拖拽状态
        /// </summary>
        public DragDropState CurrentState { get; private set; } = DragDropState.None;

        /// <summary>
        /// 接受的对象类型
        /// </summary>
        public Type[] AcceptedTypes { get; set; }

        /// <summary>
        /// 是否显示拖拽区域视觉效果
        /// </summary>
        public bool ShowVisualFeedback { get; set; } = true;

        /// <summary>
        /// 是否只在拖拽时显示拖拽区域
        /// </summary>
        public bool ShowOnlyWhenDragging { get; set; } = true;

        /// <summary>
        /// 高亮颜色
        /// </summary>
        public Color HighlightColor { get; set; } = new Color(0.3f, 0.6f, 0.9f);

        /// <summary>
        /// 边框宽度
        /// </summary>
        public float BorderWidth { get; set; } = 2f;

        /// <summary>
        /// 是否接受当前拖拽的对象
        /// </summary>
        public bool IsAccepted { get; private set; }

        /// <summary>
        /// 是否当前有拖拽操作
        /// </summary>
        public bool IsDragging { get; private set; }

        /// <summary>
        /// 自定义绘制函数，在拖拽区域上方绘制文本或其他UI元素
        /// </summary>
        /// <param name="rect">拖拽区域矩形</param>
        /// <param name="state">当前拖放状态</param>
        /// <param name="isAccepted">是否接受当前拖拽的对象</param>
        public Action<Rect, DragDropState, bool> OnDrawOverlay;

        /// <summary>
        /// 对象放下事件
        /// </summary>
        /// <param name="objects">被放下的对象数组</param>
        public Action<Object[]> OnObjectsDropped;

        #endregion

        // 注意：我们不再使用droppedObjects字段，而是在回调中直接使用本地变量

        #region 构造函数

        /// <summary>
        /// 创建一个新的拖拽处理器
        /// </summary>
        public DragDropHandler()
        {
        }

        /// <summary>
        /// 创建一个新的拖拽处理器
        /// </summary>
        /// <param name="dropArea">拖拽区域</param>
        public DragDropHandler(Rect dropArea)
        {
            DropArea = dropArea;
        }

        /// <summary>
        /// 创建一个新的拖拽处理器
        /// </summary>
        /// <param name="dropArea">拖拽区域</param>
        /// <param name="acceptedTypes">接受的对象类型</param>
        public DragDropHandler(Rect dropArea, Type[] acceptedTypes)
        {
            DropArea = dropArea;
            AcceptedTypes = acceptedTypes;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 在指定区域内处理拖拽
        /// </summary>
        /// <param name="rect">拖拽区域</param>
        public void DrawDragAndDrop(Rect rect)
        {
            // 设置拖拽区域
            DropArea = rect;

            // 获取当前事件
            Event currentEvent = Event.current;

            // 如果是拖拽相关事件，则处理
            if (currentEvent.type == EventType.DragUpdated ||
                currentEvent.type == EventType.DragPerform ||
                currentEvent.type == EventType.DragExited)
            {
                // 处理拖拽事件
                ProcessDragEvents();
            }

            // 在重绘事件中绘制UI，无论拖拽对象是否在拖拽区域内
            if (currentEvent.type == EventType.Repaint)
            {
                DrawDragDropFeedback();
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 处理拖拽事件
        /// </summary>
        private void ProcessDragEvents()
        {
            // 获取当前事件
            Event currentEvent = Event.current;
            EventType eventType = currentEvent.type;

            // 重置状态
            DragDropState previousState = CurrentState;

            // 检测是否有拖拽操作（全局检测，不受区域限制）
            bool hasDragOperation = eventType == EventType.DragUpdated || eventType == EventType.DragPerform;

            // 如果有拖拽操作，则设置拖拽状态
            if (hasDragOperation)
            {
                IsDragging = true;
            }
            else if (eventType == EventType.DragExited)
            {
                // 拖拽操作结束
                IsDragging = false;
                IsAccepted = false;
                CurrentState = DragDropState.None;

                // 使用事件，确保重绘
                currentEvent.Use();
            }

            // 如果正在拖拽，则处理拖拽事件
            if (IsDragging)
            {
                // 检查拖拽对象是否在接受区域内
                if (hasDragOperation && DropArea.Contains(currentEvent.mousePosition))
                {
                    // 检查拖拽对象类型是否符合要求
                    IsAccepted = CheckObjectTypesAcceptable(DragAndDrop.objectReferences, AcceptedTypes);

                    // 设置拖放视觉效果
                    DragAndDrop.visualMode = IsAccepted ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                    // 更新状态
                    CurrentState = eventType == EventType.DragPerform ? DragDropState.Drop : DragDropState.Over;

                    // 如果是放下操作且可以接受
                    if (eventType == EventType.DragPerform && IsAccepted)
                    {
                        DragAndDrop.AcceptDrag();

                        // 保存当前拖放的对象到本地变量
                        Object[] objectsToPass = DragAndDrop.objectReferences;

                        // 在下一帧调用回调，避免GUI Layout错误
                        EditorApplication.delayCall += () => {
                            try
                            {
                                // 检查回调和对象是否有效
                                if (OnObjectsDropped != null && objectsToPass != null && objectsToPass.Length > 0)
                                {
                                    // 复制对象数组，避免引用已释放的对象
                                    var objectsCopy = new Object[objectsToPass.Length];
                                    for (int i = 0; i < objectsToPass.Length; i++)
                                    {
                                        objectsCopy[i] = objectsToPass[i];
                                    }

                                    OnObjectsDropped.Invoke(objectsCopy);
                                }

                                // 恢复鼠标样式
                                DragAndDrop.visualMode = DragAndDropVisualMode.None;
                                EditorGUIUtility.SetWantsMouseJumping(0);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"Error in drag and drop callback: {ex.Message}\n{ex.StackTrace}");
                            }
                            finally
                            {
                                // 确保即使发生异常也能恢复鼠标样式
                                DragAndDrop.visualMode = DragAndDropVisualMode.None;
                                EditorGUIUtility.SetWantsMouseJumping(0);
                            }
                        };

                        currentEvent.Use();

                    }

                    currentEvent.Use();
                }
                else if (hasDragOperation && previousState == DragDropState.Over)
                {
                    // 如果之前在区域内，现在不在，则是离开
                    CurrentState = DragDropState.Exit;
                    IsAccepted = false; // 重置接受状态

                    // 使用事件，确保重绘
                    currentEvent.Use();
                }
                else if (hasDragOperation && DropArea.Contains(currentEvent.mousePosition) && previousState == DragDropState.None)
                {
                    // 如果之前不在区域内，现在在，则是进入
                    CurrentState = DragDropState.Enter;
                    currentEvent.Use();
                }
                else if (hasDragOperation && !DropArea.Contains(currentEvent.mousePosition) && previousState == DragDropState.None)
                {
                    // 如果拖拽对象不在区域内，且之前也不在，保持Exit状态
                    CurrentState = DragDropState.Exit;
                    IsAccepted = false;
                }
            }
            else
            {
                // 没有拖拽操作
                CurrentState = DragDropState.None;
                IsAccepted = false;
            }

            // 不再返回拖放的对象，而是通过OnObjectsDropped回调通知
        }

        /// <summary>
        /// 绘制拖放反馈界面
        /// </summary>
        private void DrawDragDropFeedback()
        {
            // 判断是否显示拖拽相关UI
            bool shouldShowDragUI = !ShowOnlyWhenDragging || IsDragging;

            // 保存当前的GUI状态
            var oldEnabled = GUI.enabled;
            var oldColor = GUI.color;
            var oldDepth = GUI.depth;

            // 绘制拖放区域（确保显示在最上层）
            if (ShowVisualFeedback && shouldShowDragUI)
            {
                // 设置为最高层绘制
                GUI.depth = 0;

                // 只在拖拽时绘制视觉反馈
                if (IsDragging)
                {
                    // 绘制半透明背景，使用非常低的透明度，以便能看到下方的内容
                    var bgColor = IsAccepted ?
                        new Color(HighlightColor.r, HighlightColor.g, HighlightColor.b, 0.1f) :
                        new Color(0.8f, 0.2f, 0.2f, 0.1f); // 不接受时显示红色

                    // 绘制背景
                    EditorGUI.DrawRect(DropArea, bgColor);

                    // 绘制边框
                    var borderColor = IsAccepted ? HighlightColor : new Color(0.8f, 0.2f, 0.2f, 0.8f);
                    DrawRectangleBorder(DropArea, BorderWidth, borderColor);

                    // 不再显示文本，由外部通过OnDrawOverlay回调来绘制
                }
            }

            // 恢复原来的GUI状态
            GUI.enabled = oldEnabled;
            GUI.color = oldColor;
            GUI.depth = oldDepth;

            // 调用上层绘制函数（只在需要显示拖拽相关UI时调用）
            if (shouldShowDragUI)
            {
                OnDrawOverlay?.Invoke(DropArea, CurrentState, IsAccepted);
            }
        }

        /// <summary>
        /// 绘制矩形边框
        /// </summary>
        private void DrawRectangleBorder(Rect rect, float borderWidth, Color borderColor)
        {
            // 上边框
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, borderWidth), borderColor);
            // 下边框
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - borderWidth, rect.width, borderWidth), borderColor);
            // 左边框
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, borderWidth, rect.height), borderColor);
            // 右边框
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - borderWidth, rect.y, borderWidth, rect.height), borderColor);
        }

        /// <summary>
        /// 检查拖拽的对象类型是否可接受
        /// </summary>
        private bool CheckObjectTypesAcceptable(Object[] dragObjects, Type[] acceptedTypes)
        {
            // 如果没有指定接受类型，则接受所有对象
            if (acceptedTypes == null || acceptedTypes.Length == 0)
            {
                return dragObjects != null && dragObjects.Length > 0;
            }

            // 检查每个拖拽对象是否符合接受类型
            foreach (Object obj in dragObjects)
            {
                if (obj == null) continue;

                bool isAcceptable = false;
                foreach (Type acceptedType in acceptedTypes)
                {
                    if (acceptedType.IsAssignableFrom(obj.GetType()))
                    {
                        isAcceptable = true;
                        break;
                    }
                }

                if (!isAcceptable)
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
