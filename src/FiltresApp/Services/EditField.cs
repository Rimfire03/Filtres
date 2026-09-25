namespace FiltresApp.Services;

public enum EditFieldType
{
    Text,
    MultilineText,
    Int,
    Decimal,
    Date,
    Months,
    Combo
}

public class EditField
{
    public required string Label { get; init; }
    public required EditFieldType Type { get; init; }
    public required Func<object?> GetValue { get; init; }
    public required Action<object?> SetValue { get; init; }
    public bool Required { get; init; }

    /// <summary>Libellés affichés pour <see cref="EditFieldType.Combo"/> uniquement. GetValue/SetValue
    /// portent l'index sélectionné dans cette liste.</summary>
    public List<string>? ComboItems { get; init; }

    public static EditField Text(string label, Func<string> get, Action<string> set, bool required = false) => new()
    {
        Label = label,
        Type = EditFieldType.Text,
        GetValue = () => get(),
        SetValue = v => set((string?)v ?? string.Empty),
        Required = required
    };

    public static EditField NullableText(string label, Func<string?> get, Action<string?> set) => new()
    {
        Label = label,
        Type = EditFieldType.Text,
        GetValue = () => get(),
        SetValue = v => set(string.IsNullOrWhiteSpace((string?)v) ? null : (string)v!)
    };

    public static EditField Multiline(string label, Func<string?> get, Action<string?> set) => new()
    {
        Label = label,
        Type = EditFieldType.MultilineText,
        GetValue = () => get(),
        SetValue = v => set(string.IsNullOrWhiteSpace((string?)v) ? null : (string)v!)
    };

    public static EditField IntField(string label, Func<int> get, Action<int> set, bool required = false) => new()
    {
        Label = label,
        Type = EditFieldType.Int,
        GetValue = () => get(),
        SetValue = v => set(v is int i ? i : (int.TryParse(v?.ToString(), out var parsed) ? parsed : 0)),
        Required = required
    };

    public static EditField NullableInt(string label, Func<int?> get, Action<int?> set) => new()
    {
        Label = label,
        Type = EditFieldType.Int,
        GetValue = () => get(),
        SetValue = v => set(string.IsNullOrWhiteSpace(v?.ToString()) ? null : int.TryParse(v!.ToString(), out var parsed) ? parsed : null)
    };

    public static EditField DecimalField(string label, Func<decimal> get, Action<decimal> set) => new()
    {
        Label = label,
        Type = EditFieldType.Decimal,
        GetValue = () => get(),
        SetValue = v => set(decimal.TryParse(v?.ToString(), out var parsed) ? parsed : 0m)
    };

    public static EditField DateField(string label, Func<DateOnly?> get, Action<DateOnly?> set) => new()
    {
        Label = label,
        Type = EditFieldType.Date,
        GetValue = () => get()?.ToDateTime(TimeOnly.MinValue),
        SetValue = v => set(v is DateTime dt ? DateOnly.FromDateTime(dt) : null)
    };

    public static EditField MonthsField(string label, Func<List<int>> get, Action<List<int>> set) => new()
    {
        Label = label,
        Type = EditFieldType.Months,
        GetValue = () => get(),
        SetValue = v => set((List<int>)v!)
    };

    /// <summary>Liste déroulante générique (ex. sélection d'une famille K7). <paramref name="items"/> est
    /// la liste des libellés affichés ; get/set portent l'index sélectionné dans cette liste.</summary>
    public static EditField ComboField(string label, List<string> items, Func<int> get, Action<int> set) => new()
    {
        Label = label,
        Type = EditFieldType.Combo,
        ComboItems = items,
        GetValue = () => get(),
        SetValue = v => set(v is int i ? i : 0)
    };
}
