using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace snapvox.foundation.core
{

    public static class ObjectExtensions
    {
        /// <summary>
        /// Perform a shallow/field-based clone. 
        /// In .NET 9 Native AOT, BinaryFormatter is forbidden.
        /// </summary>
        public static T Clone<T>(this T source)
        {
            if (source == null) return default;
            
            Type type = source.GetType();
            if (type.IsValueType || type == typeof(string)) return source;

            try
            {
                T destination = (T)Activator.CreateInstance(type);
                source.CloneTo(destination);
                return destination;
            }
            catch
            {
                return source;
            }
        }

        public static void CloneTo<T>(this T source, T destination)
        {
            var type = typeof(T);
            var myObjectFields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            foreach (var fieldInfo in myObjectFields)
            {
                fieldInfo.SetValue(destination, fieldInfo.GetValue(source));
            }

            var myObjectProperties = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            foreach (var propertyInfo in myObjectProperties)
            {
                if (propertyInfo.CanWrite)
                {
                    propertyInfo.SetValue(destination, propertyInfo.GetValue(source, null), null);
                }
            }
        }

        public static bool CompareLists<T>(IList<T> l1, IList<T> l2)
        {
            if (l1.Count != l2.Count)
            {
                return false;
            }

            int matched = 0;
            foreach (T item in l1)
            {
                if (!l2.Contains(item))
                {
                    return false;
                }

                matched++;
            }

            return matched == l1.Count;
        }
    }
}

