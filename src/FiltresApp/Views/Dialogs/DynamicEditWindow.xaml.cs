using System.Windows;
using System.Windows.Controls;
using FiltresApp.Services;

namespace FiltresApp.Views.Dialogs;

public partial class DynamicEditWindow : Window
{
    private readonly List<EditField> _fields;
    private readonly List<Func<bool>> _readers = new();
    public string WindowTitle { get; }

    public DynamicEditWindow(string title, List<EditField> fields)
    {
        InitializeComponent();
        WindowTitle = title;
        Title = title;
        TitleText.Text = title;
        _fields = fields;
        BuildFields();
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
                    var tb = new TextBox { Text = field.GetValue()?.ToString() ?? string.Empty };
                    container.Children.Add(tb);
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
                case EditFieldType.Combo:
                {
                    var combo = new ComboBox();
                    foreach (var item in field.ComboItems ?? new List<string>()) combo.Items.Add(item);
                    var currentIndex = field.GetValue() is int ci ? ci : 0;
                    combo.SelectedIndex = combo.Items.Count == 0 ? -1 : Math.Clamp(currentIndex, 0, combo.Items.Count - 1);
                    container.Children.Add(combo);
                    _readers.Add(() =>
                    {
                        if (field.Required && combo.SelectedIndex < 0) return false;
                        field.SetValue(combo.SelectedIndex);
                        return true;
                    });
                    break;
                }
                case EditFieldType.MonthCombo:
                {
                    var combo = new ComboBox();
                    string[] names = { "1 - Janvier", "2 - Février", "3 - Mars", "4 - Avril", "5 - Mai", "6 - Juin",
                        "7 - Juillet", "8 - Août", "9 - Septembre", "10 - Octobre", "11 - Novembre", "12 - Décembre" };
                    foreach (var n in names) combo.Items.Add(n);
                    var currentMonth = field.GetValue() is int im ? im : 1;
                    combo.SelectedIndex = Math.Clamp(currentMonth - 1, 0, 11);
                    container.Children.Add(combo);
                    _readers.Add(() => { field.SetValue(combo.SelectedIndex + 1); return true; });
                    break;
                }
            }

            FieldsPanel.Children.Add(container);
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
