using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.ViewModels;

/// <summary>Une ligne « livraison en [mois] » de l'onglet « Commande » de Paramètres.</summary>
public partial class OrderDeliveryRow : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public OrderDeliveryRow(SettingsViewModel owner, int month)
    {
        _owner = owner;
        _month = month;
    }

    [ObservableProperty] private int _month;

    partial void OnMonthChanged(int value) => _owner.SaveOrderNeed();

    [RelayCommand]
    private void Remove() => _owner.RemoveOrderDelivery(this);
}

/// <summary>Onglet « Commande » de Paramètres : mode de calcul du besoin de commande, propre au site courant
/// (voir <see cref="OrderNeedSettings"/>). Enregistré dès qu'une valeur change.</summary>
public partial class SettingsViewModel
{
    public IReadOnlyList<KeyValuePair<int, string>> OrderMonthChoices { get; } =
        ConsultedMonthOption.MonthLabels.Select((label, i) => new KeyValuePair<int, string>(i + 1, label)).ToList();
    public IReadOnlyList<int> SafetyMonthChoices { get; } = Enumerable.Range(0, 7).ToList();
    public IReadOnlyList<int> SafetyPercentChoices { get; } = Enumerable.Range(0, 41).Select(i => i * 5).ToList();

    [ObservableProperty] private bool _orderDeliveryMode = true;
    [ObservableProperty] private ObservableCollection<OrderDeliveryRow> _orderDeliveries = new();
    [ObservableProperty] private int _orderSafetyMonths = 2;
    [ObservableProperty] private int _orderSafetyPercent = 20;
    [ObservableProperty] private string _orderNeedSummary = string.Empty;

    private bool _loadingOrderNeed;

    public bool OrderEveryChangeMode
    {
        get => !OrderDeliveryMode;
        set => OrderDeliveryMode = !value;
    }

    private void LoadOrderNeed()
    {
        _loadingOrderNeed = true;
        var s = OrderNeedSettings.Current;
        OrderDeliveryMode = s.Mode == OrderNeedMode.Delivery;
        OrderDeliveries = new ObservableCollection<OrderDeliveryRow>(s.Deliveries.Select(m => new OrderDeliveryRow(this, m)));
        OrderSafetyMonths = s.SafetyMonths;
        OrderSafetyPercent = s.SafetyPercent;
        _loadingOrderNeed = false;
        RefreshOrderNeedSummary();
    }

    partial void OnOrderDeliveryModeChanged(bool value) { OnPropertyChanged(nameof(OrderEveryChangeMode)); SaveOrderNeed(); }
    partial void OnOrderSafetyMonthsChanged(int value) => SaveOrderNeed();
    partial void OnOrderSafetyPercentChanged(int value) => SaveOrderNeed();

    [RelayCommand]
    private void AddOrderDelivery()
    {
        // Premier mois encore libre, à partir de janvier.
        var free = Enumerable.Range(1, 12).FirstOrDefault(m => OrderDeliveries.All(d => d.Month != m));
        if (free == 0) return;
        OrderDeliveries.Add(new OrderDeliveryRow(this, free));
        SaveOrderNeed();
    }

    internal void RemoveOrderDelivery(OrderDeliveryRow row)
    {
        if (OrderDeliveries.Count <= 1) return;
        OrderDeliveries.Remove(row);
        SaveOrderNeed();
    }

    internal void SaveOrderNeed()
    {
        if (_loadingOrderNeed) return;
        if (!App.GuardWritable())
        {
            LoadOrderNeed();
            return;
        }
        OrderNeedSettings.Save(App.Db, new OrderNeedSettings(OrderDeliveryMode ? OrderNeedMode.Delivery : OrderNeedMode.EveryChange,
            OrderDeliveries.Select(d => d.Month).ToArray(), OrderSafetyMonths, OrderSafetyPercent));
        // Deux lignes sur le même mois : une seule livraison retenue, la liste affichée est remise à jour.
        if (OrderNeedSettings.Current.Deliveries.Length != OrderDeliveries.Count)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(LoadOrderNeed);
        else
            RefreshOrderNeedSummary();
    }

    private void RefreshOrderNeedSummary()
    {
        var s = OrderNeedSettings.Current;
        string Name(int m) => ConsultedMonthOption.MonthLabels[m - 1].ToLower();
        OrderNeedSummary = s.Mode == OrderNeedMode.EveryChange
            ? $"Une colonne « Besoin » : quantité en place + {s.SafetyPercent} % d'un changement (arrondi au-dessus), pour chaque filtre rattaché."
            : string.Join("\n", s.Deliveries.Select(d => { var w = s.Window(d); return $"Livraison en {Name(d)} : couvre les changements de {Name(w[0])} à {Name(w[^1])}."; }))
              + "\nBesoin d'un filtre = (nombre de changements prévus sur ces mois + 1) × quantité en place. Une colonne « Besoin » par livraison.";
    }
}
