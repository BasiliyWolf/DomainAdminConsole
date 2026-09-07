using System.ComponentModel;

namespace DomainAdminConsole.Models;

public sealed class SortableBindingList<T> : BindingList<T>
{
    private bool _isSorted;
    private PropertyDescriptor? _sortProperty;
    private ListSortDirection _sortDirection;

    public SortableBindingList() : base() { }
    public SortableBindingList(IList<T> list) : base(list) { }

    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _isSorted;
    protected override PropertyDescriptor? SortPropertyCore => _sortProperty;
    protected override ListSortDirection SortDirectionCore => _sortDirection;

    protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
    {
        if (Items is not List<T> list)
            return;

        list.Sort((left, right) => CompareValues(property.GetValue(left), property.GetValue(right), direction));
        _sortProperty = property;
        _sortDirection = direction;
        _isSorted = true;
        ResetBindings();
    }

    protected override void RemoveSortCore()
    {
        _isSorted = false;
        _sortProperty = null;
    }

    private static int CompareValues(object? left, object? right, ListSortDirection direction)
    {
        int result;
        if (ReferenceEquals(left, right)) result = 0;
        else if (left is null) result = -1;
        else if (right is null) result = 1;
        else if (left is string ls && right is string rs)
            result = StringComparer.CurrentCultureIgnoreCase.Compare(ls, rs);
        else if (left is IComparable comparable)
            result = comparable.CompareTo(right);
        else
            result = StringComparer.CurrentCultureIgnoreCase.Compare(left.ToString(), right.ToString());

        return direction == ListSortDirection.Ascending ? result : -result;
    }
}
