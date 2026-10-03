using System.Windows;
using System.Windows.Controls;
using FiltresApp.Services;

namespace FiltresApp.Views.Dialogs;

public partial class DynamicEditWindow : Window
{
    private readonly List<EditField> _fields;
    private readonly List<Func<bool>> _readers = new();
    /// <summary>Valeur "en direct" (index de combo, texte tapé...) de chaque champ, lue sans attendre la
    /// validation - utilisée pour réévaluer <see cref="EditField.EnabledWhenFieldEquals"/> des autres
    /// champs à chaque changement (voir <see cref="RefreshConditionalFields"/>).</summary>
    private readonly Dictionary<EditField, Func<object?>> _liveGetters = new();
    private readonly Dictionary<EditField, Control> _controls = new();
    public string WindowTitle { get; }

    public DynamicEditWindow(string title, List<EditField> fields)
    {
        InitializeComponent();
        WindowTitle = title;
        Title = title;
        TitleText.Text = title;
        _fields = fields;
        BuildFields();
        RefreshConditionalFields();
    }

    private void BuildFields()
    {
        foreach (var field in _fields)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            container.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold });

            switch (field.Type)
            {
                case EditFieldType.Text:
                {
                    var tb = new TextBox
                    {
                        Text = field.GetValue()?.ToString() ?? string.Empty,
                        CharacterCasing = field.Uppercase ? CharacterCasing.Upper : CharacterCasing.Normal
                    };
                    container.Children.Add(tb);
                    _controls[field] = tb;
                    _liveGetters[field] = () => tb.Text;
                    tb.TextChanged += (_, _) => RefreshConditionalFields();
                    _readers.Add(() => { field.SetValue(tb.Text); return true; });
                    break;
                }
                case EditFieldType.MultilineText:
                {
                    var tb = new TextBox
                    {
                        Text = field.GetValue()?.ToString() ?? string.Empty,
                        AcceptsReturn = true,
                        Height = 70,
                        TextWrapping = TextWrapping.Wrap
                    };
                    container.Children.Add(tb);
                    _controls[field] = tb;
                    _liveGetters[field] = () => tb.Text;
                    tb.TextChanged += (_, _) => RefreshConditionalFields();
                    _readers.Add(() => { field.SetValue(tb.Text); return true; });
                    break;
                }
                case EditFieldType.Int:
                {
                    var tb = new TextBox { Text = field.GetValue()?.ToString() ?? string.Empty };
                    container.Children.Add(tb);
                    _readers.Add(() =>
                    {
                        if (field.Required && string.IsNullOrWhiteSpace(tb.Text)) return false;
                        field.SetValue(tb.Text);
                        return true;
                    });
                    break;
                }
                case EditFieldType.Decimal:
                {
                    var tb = new TextBox { Text = field.GetValue()?.ToString() ?? "0" };
                    container.Children.Add(tb);
                    _readers.Add(() => { field.SetValue(tb.Text); return true; });
                    break;
                }
                case EditFieldType.Date:
                {
                    var dp = new DatePicker();
                    if (field.GetValue() is DateTime dt) dp.SelectedDate = dt;
                    container.Children.Add(dp);
                    _readers.Add(() => { field.SetValue(dp.SelectedDate); return true; });
                    break;
                }
                case EditFieldType.Months:
                {
                    var wrap = new WrapPanel();
                    var current = (field.GetValue() as List<int>) ?? new List<int>();
                    var checkboxes = new List<CheckBox>();
                    string[] names = { "Jan", "Fév", "Mar", "Avr", "Mai", "Jun", "Jul", "Aoû", "Sep", "Oct", "Nov", "Déc" };
                    for (var m = 1; m <= 12; m++)
                    {
                        var cb = new CheckBox
                        {
                            Content = names[m - 1],
                            Tag = m,
                            IsChecked = current.Contains(m),
                            Margin = new Thickness(0, 0, 12, 4)
                        };
                        checkboxes.Add(cb);
                        wrap.Children.Add(cb);
                    }
                    container.Children.Add(wrap);
                    _readers.Add(() =>
                    {
                        var selected = checkboxes.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToList();
                        field.SetValue(selected);
                        return true;
                    });
                    break;
                }
                case EditFieldType.Checklist:
                {
                    var wrap = new WrapPanel();
                    var current = (field.GetValue() as List<int>) ?? new List<int>();
                    var checkboxes = new List<CheckBox>();
                    var items = field.ComboItems ?? new List<string>();
                    for (var i = 0; i < items.Count; i++)
                    {
                        var cb = new CheckBox
                        {
                            Content = items[i],
                            Tag = i,
                            IsChecked = current.Contains(i),
                            Margin = new Thickness(0, 0, 16, 4)
                        };
                        checkboxes.Add(cb);
                        wrap.Children.Add(cb);
                    }
                    container.Children.Add(wrap);
                    _readers.Add(() =>
                    {
                        var selected = checkboxes.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToList();
                        if (field.Required && selected.Count == 0) return false;
                        field.SetValue(selected);
                        return true;
                    });
                    break;
                }
                case EditFieldType.Combo:
                {
                    var combo = new ComboBox();
                    foreach (var item in field.ComboItems ?? new List<string>()) combo.Items.Add(item);
                    var currentIndex = field.GetValue() is int ci ? ci : 0;
                    combo.SelectedIndex = combo.Items.Count == 0 ? -1 : Math.Clamp(currentIndex, 0, combo.Items.Count - 1);
                    container.Children.Add(combo);
                    _controls[field] = combo;
                    _liveGetters[field] = () => combo.SelectedIndex;
                    combo.SelectionChanged += (_, _) => RefreshConditionalFields();
                    _readers.Add(() =>
                    {
                        if (field.Required && combo.SelectedIndex < 0) return false;
                        field.SetValue(combo.SelectedIndex);
                        return true;
                    });
                    break;
                }
            }

            FieldsPanel.Children.Add(container);
        }
    }

    /// <summary>Réévalue, pour chaque champ déclarant <see cref="EditField.EnabledWhenFieldEquals"/>,
    /// s'il doit rester accessible selon la valeur en direct du champ dont il dépend - un champ désactivé
    /// est vidé (donc enregistré vide/null à la validation).</summary>
    private void RefreshConditionalFields()
    {
        foreach (var field in _fields)
        {
            if (field.EnabledWhenFieldEquals is not { } controller || field.EnabledPredicate is not { } predicate) continue;
            if (!_controls.TryGetValue(field, out var control)) continue;

            var controllingValue = _liveGetters.TryGetValue(controller, out var getter) ? getter() : null;
            var enabled = predicate(controllingValue);
            control.IsEnabled = enabled;
            if (!enabled && control is TextBox tb) tb.Text = string.Empty;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var reader in _readers)
        {
            if (!reader())
            {
                MessageBox.Show(this, "Merci de renseigner tous les champs obligatoires.", "Champs manquants",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
