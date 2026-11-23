using System.Collections;

namespace Isotope.UI.Core.V1Protocol;

/// <summary>
/// The items collection an items-host strategy writes (<c>IList&lt;object&gt;</c>). The generator emits
/// <c>ItemsList.Of(c.Items)</c>: a generic list passes through, and Avalonia's <c>ItemCollection</c>, which implements
/// only the non-generic <see cref="IList"/>, is adapted.
/// </summary>
public static class ItemsList
{
    /// <summary>A list that already is an <c>IList&lt;object&gt;</c>.</summary>
    public static IList<object> Of<T>(T list) where T : IList<object> => list;

    /// <summary>A non-generic list, adapted.</summary>
    public static IList<object> Of(IList list) => list as IList<object> ?? new Adapter(list);

    private sealed class Adapter(IList inner) : IList<object>
    {
        public object this[int index] { get => inner[index]!; set => inner[index] = value; }
        public int Count => inner.Count;
        public bool IsReadOnly => inner.IsReadOnly;
        public void Add(object item) => inner.Add(item);
        public void Clear() => inner.Clear();
        public bool Contains(object item) => inner.Contains(item);
        public void CopyTo(object[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);
        public IEnumerator<object> GetEnumerator()
        {
            foreach (var item in inner) yield return item!;
        }
        public int IndexOf(object item) => inner.IndexOf(item);
        public void Insert(int index, object item) => inner.Insert(index, item);
        public bool Remove(object item)
        {
            int i = inner.IndexOf(item);
            if (i < 0) return false;
            inner.RemoveAt(i);
            return true;
        }
        public void RemoveAt(int index) => inner.RemoveAt(index);
        IEnumerator IEnumerable.GetEnumerator() => inner.GetEnumerator();
    }
}
