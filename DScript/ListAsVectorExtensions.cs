using System;
using System.Collections.Generic;
using System.Text;

namespace DScript
{
    public static class ListAsVectorExtensions
    {
        public static void PushBack<T>(this List<T> list, T item)
        {
            list.Add(item);
        }

        public static void PopBack<T>(this List<T> list)
        {
            list.RemoveAt(list.Count - 1);
        }

        public static T Back<T>(this List<T> list)
        {
            return list[list.Count - 1];
        }
    }
}
