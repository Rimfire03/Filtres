using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;

namespace FiltresApp.ViewModels;

/// <summary>Onglet « Utilisateurs » des Paramètres : liste des sessions Windows autorisées en lecture/écriture
/// (voir <see cref="AccessControl"/>). À gauche les utilisateurs connus, à droite les utilisateurs autorisés.</summary>
public partial class SettingsViewModel
{
    public ObservableCollection<string> KnownUsers { get; } = [];
    public ObservableCollection<string> WriterUsers { get; } = [];

    [ObservableProperty] private string? _selectedKnownUser;
    [ObservableProperty] private string? _selectedWriterUser;
    [ObservableProperty] private string _newUserName = string.Empty;
    [ObservableProperty] private string _accessStatusMessage = string.Empty;

    public string AccessIdentityText =>
        $"Session Windows : {AccessControl.CurrentIdentity}" + (App.AccessAllowed ? "  (accès lecture/écriture approuvé)" : "  (accès non approuvé : lecture seule)");

    /// <summary>Seul un utilisateur autorisé et non en lecture seule peut modifier la liste.</summary>
    public bool CanEditAccess => App.IsWritable && App.AccessAllowed;

    [RelayCommand]
    private void ReloadAccess()
    {
        try
        {
            var lists = AccessControl.Load(App.DbFactory.Target);
            var known = lists.Known.Concat(lists.Writers).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(k => !lists.Writers.Contains(k, StringComparer.OrdinalIgnoreCase));
            Fill(KnownUsers, known);
            Fill(WriterUsers, lists.Writers);
            AccessStatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            AccessStatusMessage = "Liste des utilisateurs illisible : " + ex.Message;
        }
    }

    private static void Fill(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var v in values.OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)) target.Add(v);
    }

    private static void Insert(ObservableCollection<string> target, string value)
    {
        if (target.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        var index = 0;
        while (index < target.Count && string.Compare(target[index], value, StringComparison.CurrentCultureIgnoreCase) < 0) index++;
        target.Insert(index, value);
    }

    /// <summary>Flèche « Ajouter » : déplace l'utilisateur connu sélectionné vers les utilisateurs autorisés.</summary>
    [RelayCommand]
    private void AddAccessUser()
    {
        if (SelectedKnownUser is not { } user) return;
        KnownUsers.Remove(user);
        Insert(WriterUsers, user);
    }

    /// <summary>Flèche « Supprimer » : retire l'utilisateur autorisé sélectionné (il reste dans les utilisateurs connus).</summary>
    [RelayCommand]
    private void RemoveAccessUser()
    {
        if (SelectedWriterUser is not { } user) return;
        WriterUsers.Remove(user);
        Insert(KnownUsers, user);
    }

    /// <summary>Crée un utilisateur pas encore connecté (ou un domaine « @domaine ») : reconnu à sa première connexion.</summary>
    [RelayCommand]
    private void CreateAccessUser()
    {
        var name = NewUserName.Trim();
        if (name.Length == 0) return;
        if (name == "@")
        {
            AccessStatusMessage = "Saisissez un domaine après le « @ » (exemple : @entreprise).";
            return;
        }
        if (WriterUsers.Contains(name, StringComparer.OrdinalIgnoreCase) || KnownUsers.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            AccessStatusMessage = $"« {name} » est déjà dans la liste.";
            return;
        }
        Insert(KnownUsers, name);
        SelectedKnownUser = name;
        NewUserName = string.Empty;
        AccessStatusMessage = $"« {name} » ajouté aux utilisateurs connus : utilisez la flèche pour lui donner l'accès en écriture, puis enregistrez.";
    }

    [RelayCommand]
    private void SaveAccess()
    {
        if (!App.GuardWritable()) return;
        if (!App.AccessAllowed) return;

        if (WriterUsers.Count == 0)
        {
            AccessStatusMessage = "La liste des utilisateurs autorisés ne peut pas être vide.";
            return;
        }

        if (!AccessControl.IsCurrentUserAllowed(WriterUsers) &&
            !App.Dialogs.ShowConfirm("Utilisateurs autorisés",
                $"Votre session « {AccessControl.CurrentIdentity} » n'est plus dans la liste : vous passerez en lecture seule à la prochaine ouverture.\n\nEnregistrer quand même ?"))
            return;

        try
        {
            AccessControl.Save(App.DbFactory.Target, WriterUsers, KnownUsers.Concat(WriterUsers));
            AccessStatusMessage = "Utilisateurs enregistrés. Les changements s'appliquent à la prochaine ouverture de l'application sur chaque poste.";
        }
        catch (Exception ex)
        {
            AccessStatusMessage = "Enregistrement impossible : " + ex.Message;
        }
    }
}
