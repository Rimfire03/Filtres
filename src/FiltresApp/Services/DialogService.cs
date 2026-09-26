using System.Windows;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;
using FiltresApp.Views.Dialogs;

namespace FiltresApp.Services;

public interface IDialogService
{
    void ShowMessage(string title, string message);
    bool ShowConfirm(string title, string message);
    bool EditFields(string title, List<EditField> fields);
    List<FilterRef>? PickFilterLinks(List<FilterPickItem> items);
    void ShowFilterHistory(string locationLabel, string location, string dimension, List<FilterReplacement> replacements);
}

public class DialogService : IDialogService
{
    public void ShowMessage(string title, string message)
    {
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool ShowConfirm(string title, string message)
    {
        return MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public bool EditFields(string title, List<EditField> fields)
    {
        var window = new DynamicEditWindow(title, fields) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true;
    }

    public List<FilterRef>? PickFilterLinks(List<FilterPickItem> items)
    {
        var window = new FilterLinkWindow(items) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.SelectedFilters : null;
    }

    public void ShowFilterHistory(string locationLabel, string location, string dimension, List<FilterReplacement> replacements)
    {
        var window = new FilterHistoryWindow(locationLabel, location, dimension, replacements) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }
}
