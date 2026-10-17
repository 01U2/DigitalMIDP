namespace DigitalMIDP.ViewModels;

/// <summary>
/// The stable identity string for each static grid column - used as the column-preferences
/// persistence key (<see cref="ColumnSelectorViewModel"/>), the filter identity key
/// (<see cref="FileFilterViewModel"/>/<see cref="AttributeFilterViewModel.ColumnKey"/>), and the
/// reserved-name check that keeps a dynamic attribute column from being built twice
/// (<c>MainWindow.IsReservedColumnName</c>). Centralized here because all three places have to agree
/// on the exact same strings - a typo in just one of them used to fail silently (a filter that never
/// matches its column, or a preference that never loads) rather than a compile error.
/// </summary>
public static class ColumnKeys
{
    public const string Name = "Name";
    public const string DeliverableTitle = "Deliverable Title";
    public const string Size = "Size";
    public const string Created = "Created";
    public const string Modified = "Modified";
    public const string PathInProject = "PathInProject";
    public const string Stage = "Stage";

    /// <summary>Attribute names that default to visible the first time a project is opened - see <see cref="ColumnSelectorViewModel"/>.</summary>
    public const string Revision = "Revision";
    public const string Status = "Status";
}
