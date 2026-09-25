using CommunityToolkit.Mvvm.ComponentModel;

namespace FiltresApp.ViewModels;

public partial class NavigationItem : ObservableObject
{
    public string Title { get; }
    public string Icon { get; }
    private readonly Func<object> _factory;
    private object? _viewModel;

    public NavigationItem(string title, string icon, Func<object> factory)
    {
        Title = title;
        Icon = icon;
        _factory = factory;
    }

    public object GetOrCreateViewModel() => _viewModel ??= _factory();

    public void Reset() => _viewModel = null;

    public override string ToString() => Title;
}
