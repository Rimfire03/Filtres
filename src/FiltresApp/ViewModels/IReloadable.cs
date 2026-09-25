namespace FiltresApp.ViewModels;

/// <summary>Implémenté par les ViewModels dont les données doivent être rechargées depuis la base à
/// chaque (ré)sélection de l'écran dans la navigation, plutôt qu'une seule fois à la construction.
/// Utilisé notamment par <see cref="InventoryListViewModel"/> et <see cref="OrderListViewModel"/> qui
/// partagent désormais les mêmes lignes en base (écrans "Inventaire" et "Commande chmy") : sans cela,
/// une ligne ajoutée depuis l'un des deux écrans resterait invisible sur l'autre tant que l'application
/// n'est pas relancée.</summary>
public interface IReloadable
{
    void Reload();
}
