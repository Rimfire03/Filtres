using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.ViewModels;

/// <summary>Onglet « Commande » de Paramètres : mode de calcul du besoin de commande, propre au site courant
/// (voir <see cref="OrderNeedSettings"/>). Enregistré dès qu'une valeur change.</summary>
public partial class SettingsViewModel
{
    public IReadOnlyList<KeyValuePair<int, string>> OrderMonthChoices { get; } =
        ConsultedMonthOption.MonthLabels.Select((label, i) => new KeyValuePair<int, string>(i + 1, label)).ToList();
    public IReadOnlyList<int> SafetyMonthChoices { get; } = Enumerable.Range(0, 7).ToList();
    public IReadOnlyList<int> SafetyPercentChoices { get; } = Enumerable.Range(0, 41).Select(i => i * 5).ToList();

    [ObservableProperty] private bool _orderDeliveryMode = true;
    [ObservableProperty] private int _orderDelivery1 = 3;
    [ObservableProperty] private int _orderDelivery2 = 9;
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
        OrderDelivery1 = s.Delivery1;
        OrderDelivery2 = s.Delivery2;
        OrderSafetyMonths = s.SafetyMonths;
        OrderSafetyPercent = s.SafetyPercent;
        _loadingOrderNeed = false;
        RefreshOrderNeedSummary();
    }

    partial void OnOrderDeliveryModeChanged(bool value) { OnPropertyChanged(nameof(OrderEveryChangeMode)); SaveOrderNeed(); }
    partial void OnOrderDelivery1Changed(int value) => SaveOrderNeed();
    partial void OnOrderDelivery2Changed(int value) => SaveOrderNeed();
    partial void OnOrderSafetyMonthsChanged(int value) => SaveOrderNeed();
    partial void OnOrderSafetyPercentChanged(int value) => SaveOrderNeed();

    private void SaveOrderNeed()
    {
        if (_loadingOrderNeed) return;
        if (!App.GuardWritable())
        {
            LoadOrderNeed();
            return;
        }
        OrderNeedSettings.Save(App.Db, new OrderNeedSettings(OrderDeliveryMode ? OrderNeedMode.Delivery : OrderNeedMode.EveryChange,
            OrderDelivery1, OrderDelivery2, OrderSafetyMonths, OrderSafetyPercent));
        RefreshOrderNeedSummary();
    }

    private void RefreshOrderNeedSummary()
    {
        var s = OrderNeedSettings.Current;
        string Span(int[] w) => $"{ConsultedMonthOption.MonthLabels[w[0] - 1]} à {ConsultedMonthOption.MonthLabels[w[^1] - 1]}";
        OrderNeedSummary = s.Mode == OrderNeedMode.EveryChange
            ? $"Besoin = quantité en place + {s.SafetyPercent} % d'un changement (arrondi au-dessus), pour chaque filtre rattaché."
            : $"Commande livrée en {ConsultedMonthOption.MonthLabels[s.Delivery1 - 1].ToLower()} : couvre les changements de {Span(s.Window(s.Delivery1, s.Delivery2)).ToLower()}.\n" +
              $"Commande livrée en {ConsultedMonthOption.MonthLabels[s.Delivery2 - 1].ToLower()} : couvre les changements de {Span(s.Window(s.Delivery2, s.Delivery1)).ToLower()}.\n" +
              "Besoin d'un filtre = (nombre de changements prévus sur ces mois + 1) × quantité en place.";
    }
}
