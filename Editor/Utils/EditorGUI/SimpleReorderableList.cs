using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEditor;

using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Yueby.Utils
{
    /// <summary>
    /// 简化版的可重排序列表，没有拖放功能
    /// </summary>
    public class SimpleReorderableList
    {
        /// <summary>
        /// 是否显示添加按钮
        /// </summary>
        public bool AddButtonEnabled { get; set; } = true;

        /// <summary>
        /// 是否显示删除按钮
        /// </summary>
        public bool RemoveButtonEnabled { get; set; } = true;

        /// <summary>
        /// 是否自动增加数组大小
        /// </summary>
        public bool AutoIncreaseArraySize { get; set; } = true;

        /// <summary>
        /// 列表标题
        /// </summary>
        public string Title { get; set; } = "列表";

        /// <summary>
        /// 是否无边框
        /// </summary>
        public bool IsNoBorder { get; set; } = false;

        /// <summary>
        /// 是否是对象引用类型
        /// </summary>
        private bool _isPPTR = false;
        public UnityAction<ReorderableList> OnAdd;
        public UnityAction<Object, int> OnChanged;
        public Func<Rect, int, bool, bool, float> OnDraw;
        public UnityAction<ReorderableList, Object> OnRemove;
        public UnityAction<int> OnRemoveBefore;
        public UnityAction<Object, int> OnSelected;
        public UnityAction OnDrawTitle;
        public UnityAction OnHeaderBottomDraw;
        public float[] ElementHeights;

        private IList _elements;
        private SerializedProperty _serializedProperty;
        private SerializedObject _serializedObject;
        private bool _isUsingSerializedProperty;

        public UnityAction<int> OnElementHeightCallback;
        public ReorderableList List { get; }

        private Vector2 _scrollPosition = Vector2.zero;

        /// <summary>
        /// 创建一个新的简化版可重排序列表（使用IList）
        /// </summary>
        /// <param name="elements">列表数据源</param>
        /// <param name="elementType">列表元素类型</param>
        /// <param name="elementHeight">元素高度</param>
        public SimpleReorderableList(IList elements, Type elementType, float elementHeight)
        {
            _elements = elements;
            _isUsingSerializedProperty = false;
            ElementHeights = new float[elements.Count];

            List = new ReorderableList(elements, elementType, true, false, false, false)
            {
                headerHeight = 0,
                footerHeight = 0,
                elementHeight = elementHeight,
                drawElementCallback = OnListDraw,

                onSelectCallback = reorderableList => OnSelected?.Invoke(null, reorderableList.index),
                onAddCallback = list =>
                {
                    OnAdd?.Invoke(list);
                    Array.Resize(ref ElementHeights, elements.Count);
                    GUIUtility.ExitGUI();
                },
                onRemoveCallback = reorderableList =>
                {
                    OnRemoveBefore?.Invoke(reorderableList.index);
                    if (reorderableList.index >= 0 && reorderableList.index < elements.Count)
                    {
                        elements.RemoveAt(reorderableList.index);
                    }
                    OnRemove?.Invoke(reorderableList, null);
                    if (reorderableList.count > 0 && reorderableList.index != 0)
                        reorderableList.index--;

                    Array.Resize(ref ElementHeights, elements.Count);
                    GUIUtility.ExitGUI();
                },
                onChangedCallback = list => { OnChanged?.Invoke(null, list.index); },
                elementHeightCallback = index =>
                {
                    if (index < 0 || index > ElementHeights.Length - 1) return 0;
                    if (EditorWindow.focusedWindow != null)
                        EditorWindow.focusedWindow.Repaint();

                    OnElementHeightCallback?.Invoke(index);
                    Array.Resize(ref ElementHeights, elements.Count);
                    var height = ElementHeights[index];

                    return height;
                }
            };

            if (elements.Count > 0)
            {
                List.index = 0;
            }
        }

        /// <summary>
        /// 创建一个新的简化版可重排序列表（使用SerializedProperty）
        /// </summary>
        /// <param name="serializedObject">序列化对象</param>
        /// <param name="serializedProperty">序列化属性</param>
        /// <param name="isPPTR">是否是对象引用类型</param>
        public SimpleReorderableList(SerializedObject serializedObject, SerializedProperty serializedProperty, bool isPPTR = false)
        {
            _serializedObject = serializedObject;
            _serializedProperty = serializedProperty;
            _isPPTR = isPPTR;
            _isUsingSerializedProperty = true;
            ElementHeights = new float[serializedProperty.arraySize];

            List = new ReorderableList(serializedObject, serializedProperty, true, false, false, false)
            {
                headerHeight = 0,
                footerHeight = 0,
                drawElementCallback = OnListDraw,
                onMouseUpCallback = list =>
                {
                    var item = isPPTR ? serializedProperty.GetArrayElementAtIndex(list.index).objectReferenceValue : null;
                    OnSelected?.Invoke(item, list.index);
                },

                onAddCallback = list =>
                {
                    // 根据AutoIncreaseArraySize设置决定是否自动增加数组大小
                    if (AutoIncreaseArraySize)
                    {
                        serializedProperty.arraySize++;
                    }

                    OnAdd?.Invoke(list);

                    if (List.count <= 1)
                    {
                        List.index = 0;
                        List.onMouseUpCallback?.Invoke(List);
                    }

                    // 确保元素高度数组大小正确
                    if (ElementHeights.Length != _serializedProperty.arraySize)
                    {
                        Array.Resize(ref ElementHeights, _serializedProperty.arraySize);
                        // 为新元素设置默认高度
                        if (_serializedProperty.arraySize > 0)
                        {
                            ElementHeights[_serializedProperty.arraySize - 1] = EditorGUIUtility.singleLineHeight + 6;
                        }
                    }
                },
                onRemoveCallback = reorderableList =>
                {
                    var item = isPPTR ? serializedProperty.GetArrayElementAtIndex(reorderableList.index).objectReferenceValue : null;
                    if (isPPTR)
                        serializedProperty.GetArrayElementAtIndex(reorderableList.index).objectReferenceValue = null;
                    serializedProperty.DeleteArrayElementAtIndex(reorderableList.index);

                    OnRemove?.Invoke(reorderableList, item);
                    if (reorderableList.count > 0 && reorderableList.index != 0)
                    {
                        if (reorderableList.index == reorderableList.count)
                        {
                            reorderableList.index--;
                        }

                        List.onMouseUpCallback?.Invoke(List);
                    }

                    // 移除后调整高度数组
                    if (ElementHeights.Length != _serializedProperty.arraySize)
                    {
                        Array.Resize(ref ElementHeights, _serializedProperty.arraySize);
                    }
                },
                onChangedCallback = list =>
                {
                    var item = isPPTR
                        ? serializedProperty.GetArrayElementAtIndex(list.index).objectReferenceValue
                        : null;
                    OnChanged?.Invoke(item, list.index);
                },
                elementHeightCallback = index =>
                {
                    // 边界检查
                    if (index < 0 || index >= ElementHeights.Length)
                    {
                        // 确保数组大小匹配，避免频繁调整大小
                        if (_serializedProperty != null && ElementHeights.Length != _serializedProperty.arraySize)
                        {
                            Array.Resize(ref ElementHeights, _serializedProperty.arraySize);
                            // 为新元素设置默认高度
                            if (index < _serializedProperty.arraySize)
                            {
                                ElementHeights[index] = EditorGUIUtility.singleLineHeight + 6;  // 默认行高
                            }
                        }

                        // 如果索引仍然无效，返回默认高度
                        if (index < 0 || index >= ElementHeights.Length)
                        {
                            return EditorGUIUtility.singleLineHeight + 6;
                        }
                    }

                    // 减少重绘调用，避免循环重绘
                    // 只在特定情况下重绘窗口
                    // if (EditorWindow.focusedWindow != null && Event.current.type == EventType.Layout)
                    //    EditorWindow.focusedWindow.Repaint();

                    // 调用高度回调（如果有）
                    OnElementHeightCallback?.Invoke(index);

                    // 返回缓存的高度
                    return ElementHeights[index];
                }
            };

            if (serializedProperty.arraySize > 0)
            {
                List.index = 0;
                List.onMouseUpCallback?.Invoke(List);
            }
        }

        /// <summary>
        /// 绘制列表
        /// </summary>
        /// <param name="options">布局选项</param>
        public void DoLayout(params GUILayoutOption[] options)
        {
            EditorGUILayout.BeginVertical(options);
            {
                if (!string.IsNullOrEmpty(Title))
                    EditorUI.TitleLabelField(Title);

                if (IsNoBorder)
                    EditorGUILayout.BeginVertical(options);
                else
                    EditorGUILayout.BeginVertical("Badge", options);
                EditorGUILayout.Space();
                DrawContent(OnDrawTitle);
                EditorGUILayout.Space();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 绘制列表内容
        /// </summary>
        /// <param name="titleDraw">标题绘制回调</param>
        private void DrawContent(UnityAction titleDraw = null)
        {
            // 如果使用SerializedProperty，确保它是最新的
            if (_isUsingSerializedProperty && _serializedObject != null)
            {
                _serializedObject.Update();
            }
            // 绘制标题头
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.BeginHorizontal("Badge", GUILayout.Width(25), GUILayout.Height(18));
                {
                    EditorGUILayout.LabelField($"{List.count}", EditorStyles.centeredGreyMiniLabel,
                        GUILayout.Width(25), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();

                titleDraw?.Invoke();

                var addIcon = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_toolbar plus" : "toolbar plus");

                if (AddButtonEnabled && GUILayout.Button(addIcon, GUILayout.Width(22), GUILayout.Height(18)))
                    //添加
                    List.onAddCallback?.Invoke(List);

                EditorGUI.BeginDisabledGroup(List.count == 0 || List.index == -1);
                var removeIcon = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_toolbar minus" : "toolbar minus");
                if (RemoveButtonEnabled && GUILayout.Button(removeIcon, GUILayout.Width(22), GUILayout.Height(18)))
                    List.onRemoveCallback?.Invoke(List);
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            if (OnHeaderBottomDraw != null)
            {
                EditorUI.Line(LineType.Horizontal, 2, 0);
                OnHeaderBottomDraw.Invoke();
            }

            EditorUI.Line(LineType.Horizontal, 2, 0);

            // 绘制列表内容
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(false));
            if (List.count == 0)
                EditorGUILayout.HelpBox("列表为空!", MessageType.Info);
            else
                List?.DoLayoutList();

            EditorGUILayout.EndScrollView();

            // 如果使用SerializedProperty，应用修改
            if (_isUsingSerializedProperty && _serializedObject != null)
            {
                _serializedObject.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// 刷新元素高度
        /// </summary>
        public void RefreshElementHeights()
        {
            if (_isUsingSerializedProperty)
            {
                if (_serializedProperty == null) return;

                Array.Resize(ref ElementHeights, _serializedProperty.arraySize);
                for (int i = 0; i < _serializedProperty.arraySize; i++)
                {
                    if (OnDraw != null)
                    {
                        var rect = new Rect(0, 0, 100, EditorGUIUtility.singleLineHeight); // 临时矩形用于获取高度
                        ElementHeights[i] = OnDraw.Invoke(rect, i, false, false);
                    }
                    else
                    {
                        ElementHeights[i] = EditorGUIUtility.singleLineHeight;
                    }
                }
            }
            else
            {
                if (_elements == null) return;

                Array.Resize(ref ElementHeights, _elements.Count);
                for (int i = 0; i < _elements.Count; i++)
                {
                    if (OnDraw != null)
                    {
                        var rect = new Rect(0, 0, 100, EditorGUIUtility.singleLineHeight); // 临时矩形用于获取高度
                        ElementHeights[i] = OnDraw.Invoke(rect, i, false, false);
                    }
                    else
                    {
                        ElementHeights[i] = EditorGUIUtility.singleLineHeight;
                    }
                }
            }
        }

        /// <summary>
        /// 绘制列表项
        /// </summary>
        private void OnListDraw(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (OnDraw == null) return;
            if (index < 0 || index > ElementHeights.Length - 1) return;

            var height = OnDraw.Invoke(rect, index, isActive, isFocused);
            ElementHeights[index] = height;
            if (_isUsingSerializedProperty)
            {
                Array.Resize(ref ElementHeights, _serializedProperty.arraySize);
            }
            else
            {
                Array.Resize(ref ElementHeights, _elements.Count);
            }
        }
    }
}
