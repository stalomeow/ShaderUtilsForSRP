using System;
using UnityEditor;
using UnityEngine;

namespace Stalo.ShaderUtils.Editor
{
    public static class EditorGUIScopes
    {
        public readonly ref struct Scope<T>
        {
            private readonly Action<T> m_Setter;
            private readonly T m_PrevValue;

            public Scope(Action<T> setter, T value, T tempValue)
            {
                m_Setter = setter;
                m_PrevValue = value;

                setter(tempValue);
            }

            public void Dispose()
            {
                m_Setter(m_PrevValue);
            }
        }

        public static Scope<float> LabelWidth(float value = 0)
        {
            // EditorGUIUtility.labelWidth 小于等于 0 的数，相当于默认值

            return new Scope<float>(
                width => EditorGUIUtility.labelWidth = width,
                EditorGUIUtility.labelWidth,
                value);
        }

        public static Scope<bool> MixedValue(bool value)
        {
            return new Scope<bool>(
                v => EditorGUI.showMixedValue = v,
                EditorGUI.showMixedValue,
                value);
        }

        public static Scope<Color> Color(Color value)
        {
            return new Scope<Color>(
                v => GUI.color = v,
                GUI.color,
                value);
        }
    }
}
