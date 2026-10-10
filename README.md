# Suivi maintenance filtres à air

Application Windows (.NET 8 / WPF) de suivi de maintenance des filtres à air, courroies et
roulements. Stockage SQLite portable, exécutable unique autonome (aucune installation requise).

## Structure de la solution

```
FiltresApp.sln
src/
  FiltresApp.Core/   Modèles, DbContext EF Core, services métier (sans WPF)
  FiltresApp/         Application WPF (MVVM, CommunityToolkit.Mvvm)
```

## Builder, lancer, publier

Le SDK .NET 8 est installé localement sur ce poste (pas dans le PATH système) : avant toute
commande `dotnet`, exécuter :

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
```

**Builder** :
```powershell
dotnet build FiltresApp.sln -c Debug
```

**Lancer en développement** :
```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet run --project src\FiltresApp\FiltresApp.csproj
```
(`DOTNET_ROOT` n'est nécessaire que si on lance directement le `.exe` compilé plutôt que via
`dotnet run`, pour éviter que Windows résolve un ancien runtime global ; sans effet sur
l'exécutable publié en autonome, qui embarque son propre runtime.)

**Publier l'exécutable portable** :
```powershell
dotnet publish src\FiltresApp\FiltresApp.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None
```

Résultat dans `src\FiltresApp\bin\Release\net8.0-windows\win-x64\publish\` : `FiltresApp.exe`
(~83 Mo, seul fichier attendu à la racine du dossier distribué) + `FiltreData\`. Le dossier
`LatoFont\` copié par QuestPDF doit être déplacé à la main dans `FiltreData\LatoFont\` avant de
créer le `.zip` de release (l'application le fait aussi automatiquement elle-même au premier
lancement si ce déplacement a été oublié). Aucun fichier `.db` n'est inclus dans une release
(le `.csproj` ne le copie qu'en Debug) : `settings.json` et `filtres.db` sont créés au premier
lancement, où l'utilisateur choisit de créer une base vide ou d'en sélectionner une existante.

Pour distribuer : copier tout le contenu du dossier `publish\` (l'exe a besoin de `FiltreData\`
à côté de lui).

**Version installable (MSI)** : chaque release publie aussi `FiltresApp-v<version>-win-x64.msi`
(installation par machine dans `Program Files\FiltresApp`, données dans `FiltreData\` à côté de l'exe,
mise à jour automatique via le MSI). Généré et signé par `.\tools\Build-Installer.ps1`, appelé par
`.\tools\Release.ps1`. Prérequis sur le poste de publication : `dotnet tool install --global wix --version 4.0.6`
puis `wix extension add -g WixToolset.Util.wixext/4.0.6`. Source : `installer\Product.wxs`.

**Signer l'exécutable** (à faire après `dotnet publish` et **avant** de créer le `.zip`) :
```powershell
.\tools\Sign-Release.ps1
```
Signature Authenticode SHA-256 + horodatage de l'exe publié, avec le **certificat global de l'éditeur**
(auto-signé, sujet `TomLine prod&co`, valable jusqu'en 2031 — le même pour tous les projets TomLine),
cherché dans le magasin `Cert:\CurrentUser\My` du poste de publication : la clé privée n'est jamais dans
ce dépôt. Sauvegarde (clé privée protégée par mot de passe) dans le dépôt privé
`Rimfire03/TomLine-signing-keys` et dans `%USERPROFILE%\.tomline-signing\` ; le mot de passe n'est que dans
le gestionnaire de mots de passe du propriétaire. Sur un autre poste, restaurer avec `Import-PfxCertificate`
(procédure dans le README de ce dépôt privé).

Un certificat auto-signé garantit l'intégrité de l'exe (toute modification après signature est
détectée) mais Windows/SmartScreen ne reconnaît l'éditeur que si `TomLine-signature.cer` est installé
comme **éditeur approuvé** et **autorité racine approuvée** sur le poste utilisateur (une fois, par
exemple par stratégie de groupe) ; sans cela, l'avertissement habituel s'affiche encore au lancement.

## Emplacement des données

Seul l'exécutable reste à la racine du dossier publié : tout le reste (base de données, exports,
réglages, police PDF) est dans le sous-dossier **`FiltreData\`**, à côté de l'exe.

- `FiltreData\settings.json` : chemins (base de données, dossier d'export PDF) et options,
  modifiables dans l'écran **Paramètres**. Changer le chemin de la base redémarre
  automatiquement l'application pour l'ouvrir à son nouvel emplacement.
- `FiltreData\filtres.db` : base SQLite.
- `FiltreData\CLUF.txt` : contrat de licence utilisateur final (le logiciel reste la propriété de
  TomLine prod&co). Jamais livré dans une release : le texte est intégré à l'exécutable
  (`src\FiltresApp\Legal\CLUF.txt`) et ce fichier n'est écrit qu'à son acceptation. Tant qu'il est
  absent, ou différent du texte intégré (contrat modifié dans une nouvelle release), le contrat est
  présenté au démarrage ; le refuser ferme l'application. Toute modification du texte de
  `Legal\CLUF.txt` (hors fins de ligne) fera donc réaccepter le contrat sur tous les postes.
- Le choix des colonnes affichées et leurs largeurs (par écran) sont propres à chaque poste,
  stockés dans `%LocalAppData%\FiltresApp\grilles.json` (pas dans `FiltreData\`, donc pas
  partagés entre postes même si l'exécutable et la base sont sur un disque réseau).

## Utilisation à plusieurs (base sur disque réseau)

La base est prévue pour être placée sur un disque réseau partagé (SMB, jamais un dossier
synchronisé type OneDrive/Dropbox, qui corromprait le fichier SQLite). Accès en
**« un seul rédacteur, plusieurs lecteurs »** :

- Le **premier** poste qui ouvre la base obtient l'accès en **lecture/écriture** (verrou
  `filtres.db.lock`, libéré automatiquement à la fermeture de l'application, même en cas de
  plantage ou d'extinction du poste).
- Les postes suivants s'ouvrent en **lecture seule** : bandeau jaune de rappel, boutons de
  modification désactivés, connexion SQLite elle-même ouverte en lecture seule. Consultation,
  impression et exports restent disponibles. Il faut relancer l'application pour retenter
  l'accès en écriture une fois le rédacteur sorti.
- Les utilisateurs doivent avoir les droits d'écriture sur le dossier de la base. Ne jamais
  activer le mode WAL de SQLite (ne fonctionne pas sur un disque réseau).
## Statistiques

Menu **Statistiques** (au-dessus de Paramètres), libre de licence : vue d'analyse du site courant et de
l'année du sélecteur. Les onglets Filtres périodiques / Encrassement / Courroies / Roulements ne sont
affichés que si le module est couvert par la licence et activé. Les calculs sont dans
`StatisticsService` (Core), l'écran dans `StatisticsView` / `StatisticsViewModel`, les graphiques par
LiveCharts2 (SkiaSharp). Export Excel : un classeur, une feuille par tableau (`StatisticsExcelExport`).

- **Filtres périodiques** : charge prévisionnelle mensuelle (filtres à changer, quantité à fournir avec
  option « 15 jours », filtres lavables exclus), charge par vue, taux de réalisation, échéances en
  retard (mois écoulés de l'année), heures de fonctionnement entre deux changements.
- **Encrassement** : changements par année et par variété, durée de vie réelle par filtre, filtres sans
  changement depuis N mois.
- **Courroies** : changements par année (soufflage / extraction), stock minimum par type = 2 × la quantité
  en place de la centrale qui en utilise le plus, intervalle moyen par centrale.
- **Roulements** : changements par année (avant / arrière / volute), intervalle par centrale, unités sans
  changement depuis N ans.

## Utilisation à plusieurs (serveur de base de données)

Alternative au fichier SQLite : **Paramètres > Base de données > « Réglages BDD avancés... »**.

- Moteurs gérés : **PostgreSQL (recommandé)**, MariaDB / MySQL, Microsoft SQL Server. Plusieurs
  postes peuvent se connecter au même serveur ; comme avec un fichier, **seul le premier connecté
  est en lecture/écriture**, les suivants sont en lecture seule (écritures refusées côté logiciel).
  Le verrou est une ligne `WriteLock` de la table `DbInfo` (jeton, session, poste, date), renouvelée
  toutes les 15 s par le poste rédacteur et libérée à sa fermeture ; elle expire 90 s après un
  plantage ou une coupure.
- Réglages : moteur, serveur, port, base, utilisateur, mot de passe (enregistré chiffré par DPAPI
  dans `FiltreData\settings.json`, lisible seulement par la session Windows qui l'a saisi), SSL.
  « Tester la connexion » vérifie le serveur avant d'enregistrer.
- La base et les tables sont créées au premier lancement si l'utilisateur du serveur en a le droit.
- **Migration** : « Migrer la base actuelle vers ce serveur » copie toutes les données (dans une seule
  transaction, avec contrôle du nombre de lignes par table). La base d'origine n'est pas modifiée.
- « Exporter la base de données » produit toujours un fichier SQLite complet, y compris depuis un
  serveur ; « Importer une sauvegarde » le recharge dans le serveur.
- Au démarrage, si le serveur ne répond pas, le logiciel propose de réessayer ou de repasser sur le
  fichier SQLite local.
- Mises à jour du schéma : un serveur naît au dernier schéma (créé par EF Core). Toute nouvelle
  migration ajoutée à `DatabaseMigrations.All` doit avoir son équivalent portable dans
  `DatabaseMigrations.ServerMigrations`, sinon le démarrage sur serveur échoue explicitement.

## Modules et écrans

Chaque module listé ci-dessous est **activable/désactivable** depuis Paramètres → carte
"Modules" (désactiver un module ne supprime jamais ses données, il est juste masqué du menu) ;
une roue dentée à côté de chaque case ouvre ses réglages spécifiques.

- **Module "Filtre"** (menu dépliant « Changement filtre périodique » : G4 plissés, G4 plan, G3, Charbon
  et vues créées ; menu dépliant « Changement sur encrassement » : variétés F7 à H14 ; Liste K7,
  Inventaire, Commande — considérés comme un seul module) :
  - **Menus dépliants** (migration 27) : « Changement filtre périodique » regroupe les quatre vues d'origine
    (table `PeriodicViews`, non supprimables) et celles que l'utilisateur crée — même fonctionnement que les
    variétés du menu « Changement sur encrassement » (anciennement « Filtres F7 à H14 ») : page de gestion
    atteinte par le menu parent en mode édition (bouton « Gérer les vues » sur chaque vue), titre / icône /
    libellé de colonne / position modifiables. Une vue créée fonctionne comme une vue G4 (suivi mensuel,
    famille de Commande avec besoin calculé automatiquement ; Charbon, lui, n'a pas de besoin calculé).
    Chaque filtre périodique appartient à une vue (`PeriodicFilters.PeriodicViewId`) ; les titres de vues sont
    relus par `PeriodicViewRegistry` (jamais codés en dur : une vue renommée garde ses familles de Commande).
    Titres, icônes (vues et variétés, à choisir dans une liste déroulante, voir `MenuIcons`) et état plié / déplié
    au lancement des deux menus se règlent dans
    « Paramètres du module Filtre » (tables `MenuEntries`, `PeriodicViews.Icon`, `FilterVarieties.Icon`,
    communs à tous les postes) ; les menus dépliants n'ont pas d'icône devant leur titre. Un titre est unique
    parmi les menus, vues et variétés. Le renommage d'une variété suit les lignes de Commande qui l'ont
    choisie comme famille.
  - **G4 plissés / G4 plan / G3 / Charbon** : grille par mois consulté (case "Réalisé" + date
    directement dans la grille), échéance recalculée automatiquement, impression (feuille de
    terrain avec case à cocher "Fait"), export Excel de l'année, historique complet par clic
    droit ("Consulter l'historique...", avec suppression d'un enregistrement), couleur de
    ligne, mode édition (masque Ajouter/Modifier/Supprimer par défaut, pour éviter les
    modifications accidentelles sur le terrain).
  - **"Filtres F7 à H14"** : menu dépliant où l'utilisateur crée librement des variétés de
    filtres (familles créées à la main, historique de remplacements par date sans périodicité
    mensuelle fixe, rattachement à Commande/Inventaire).
  - **Liste K7** : lieux regroupés par famille éditable.
  - **Inventaire / Commande** : deux vues d'un même jeu de données (`OrderLine`) ; rattachement
    manuel de filtres à une ligne de commande pour calculer le besoin semestriel, familles
    automatiques (déduites des filtres rattachés) ou manuelles.
- **Module "Courroies"** : nom de la centrale, type, nombre de courroies de **soufflage** et
  d'**extraction** (une quantité propre à chaque fonction, 0 = fonction absente), historique de
  remplacements par date — même disposition que "Filtres F7 à H14". À la saisie de la date du
  changement, une petite fenêtre demande quelles fonctions ont été changées (comme les roulements) ;
  la quantité enregistrée est la somme des fonctions cochées. Migration 25 : l'ancien nombre unique
  est repris comme quantité de soufflage (à ajuster dans « Modifier »). Chaque fonction a son propre type
  (référence) de courroies (migration 26 : l'ancien type unique est repris pour chaque fonction qui a une
  quantité ; un « A / B » issu d'une fusion manuelle est coupé en soufflage / extraction). Colonne « Famille » (masquée par défaut, clic droit
  sur un en-tête pour l'afficher) : liste déroulante qui change la famille en un clic ; colonnes masquables,
  redimensionnables et réordonnables à la souris (ordre mémorisé sur le poste, « Réinitialiser l'ordre des
  colonnes » au clic droit), comme les écrans de filtres. Règle à la saisie
  (« Modifier ») : un type renseigné impose au moins 1 courroie pour la fonction, l'absence de type impose 0.
- **Module "Roulements"** : nom de la centrale, type de centrale (courroies / entraînement
  direct), références des roulements avant/arrière/volute (pas de volute en entraînement
  direct), historique précisant lesquels des trois roulements ont été changés à chaque date.

Tous les écrans de grille partagent : retour à la ligne automatique, choix des colonnes
affichées et de leur largeur par poste (clic droit sur un en-tête de colonne), lignes/colonnes
de grille nettes, alignement centré des colonnes de données (la colonne d'identification de la
ligne reste à gauche).

## Paramètres

- **Modules** : activer/désactiver chaque module, roue dentée vers ses réglages spécifiques.
- **Général** : carte « Base de données » avec versions, taille et **date de la dernière saisie
  enregistrée** (« Dernière saisie enregistrée le jj/mm/aa à hh:mm » : relevée dans la base elle-même
  — table `DbInfo`, clé `LastWriteAt`, tenue à jour par des déclencheurs SQLite posés sur chaque table de
  données à l'ouverture par le poste rédacteur, donc valable pour tous les postes et tous les chemins
  d'écriture ; relue toutes les 10 s ; aucune date tant qu'aucune saisie n'a été faite depuis la pose des
  déclencheurs) ; chemins (base de données, export PDF), logo de l'entreprise (stocké en base,
  commun à tous les postes), mises à jour automatiques (vérifie les releases GitHub, sauvegarde
  la base avant d'installer), sauvegarde manuelle (export/import d'un fichier `.db`).
- **Réglages du module Filtre** (via sa roue dentée) : rattachement filtre↔commande par
  dimension, export Excel de l'année, page de garde du bon de commande PDF (Commande / Inventaire),
  colonnes imprimées par écran, couleurs de ligne
  (palette partagée, proposée au clic droit sur toute grille), suppression définitive de
  l'historique d'une année (avec sauvegarde préalable).

## Version de la base de données

La base porte un numéro de version (`PRAGMA user_version` + table `DbInfo`), affiché en haut de
Paramètres à côté de la version du logiciel. Après une mise à jour du logiciel, le premier poste
qui ouvre la base **en écriture** propose d'appliquer les migrations manquantes (liste détaillée,
confirmation, sauvegarde automatique avant modification). Un poste dont le logiciel est trop
ancien ou trop récent par rapport à la base voit un message explicite plutôt qu'un plantage.

Pour les développeurs : les évolutions de schéma se déclarent dans `DatabaseMigrations.All`
(`src\FiltresApp.Core\Data\Migrations\DatabaseMigrations.cs`) — ne jamais modifier ni renuméroter
une migration déjà publiée, toujours en ajouter une nouvelle à la fin, en SQL brut (le modèle EF
aura évolué entre-temps). `SchemaInspector` permet de vérifier si une colonne/table existe déjà,
pour rendre chaque migration idempotente.

## Choix d'implémentation notables

- **EF Core + SQLite**, `EnsureCreated()` pour une base neuve (schéma complet directement à la
  dernière version) + migrations manuelles en SQL brut pour une base existante (pas
  `dotnet ef migrations`).
- **`DbContext` unique partagé** pendant toute la session (composition root dans `App.xaml.cs`).
- **Colonnes "prochaine échéance" non stockées** : recalculées à l'affichage à partir de
  l'historique des remplacements, jamais écrites en base.
- **Export PDF** via QuestPDF (licence Community), **export Excel** via ClosedXML.
- **Dialogues de saisie/édition génériques** (`DynamicEditWindow` + `EditField`) pilotés par une
  liste de champs déclarative, pour éviter une fenêtre XAML par type d'entité.

## Architecture du code

**`FiltresApp.Core`** (sans WPF) :
- `Models/` : entités EF Core. `Data/FiltresDbContext.cs` : le contexte.
- `Data/Migrations/DatabaseMigrations.cs` + `Data/SchemaInspector.cs` : schéma et son évolution.
- `Services/` : règles métier (suivi des remplacements, rattachement filtres/commande, calcul du
  besoin, formats de dimension...) et exports Excel/PDF.

**`FiltresApp`** (WPF, MVVM) :
- `App.xaml.cs` + fichiers partiels (`App.Database.cs`, `App.CompanyLogo.cs`, `App.Modules.cs`,
  `App.Updates.cs`) : démarrage, lecture seule, contrôle de version.
- `ViewModels/` : un ViewModel par écran, découpés en fichiers partiels par sujet pour les gros
  écrans (ex. `*.Families.cs`, `SettingsViewModel.Logo.cs`/`.History.cs`/`.Updates.cs`).
- `Services/` : dialogues, impression, préférences de colonnes, chargement d'image.
- Éléments réutilisés par plusieurs écrans : styles de grille communs (`Styles/Controls.xaml`, dont
  `FlashingCellStyle`), `OrderLineListViewModelBase` (Inventaire/Commande), `LinkedFilterRowViewModel`
  (puce "Lié"), `ConsultedMonthOption`, `PrintService.BuildGroupedRows`.
- "Filtres F7 à H14", "Courroies" et "Roulements" partagent la même mécanique (familles créées à la main,
  remplacements datés sans périodicité fixe) : `TrackedItemListViewModel` (écran : mode édition, familles,
  Ajouter/Modifier/Dupliquer/Supprimer, couleur de ligne), `TrackedItemRowViewModel` (ligne : saisie
  d'une date avec flash vert, colonnes de l'année consultée) et `ReplacementHistoryWindow` ("Consulter
  l'historique..."), s'appuyant sur les interfaces `IFamilyTrackedItem` / `INamedFamily` /
  `IDatedReplacement` des modèles. Chaque module n'écrit que ses requêtes, son formulaire, sa saisie d'un
  remplacement et son impression.

## Pistes d'amélioration

- Ajouter des tests automatisés (actuellement aucun projet de test).
- Rendre le reste des grilles éditable en ligne (seules les cases "Réalisé"/date du mois
  consulté le sont actuellement ; le reste passe par des boîtes de dialogue).
- Réduire la taille de l'exécutable publié (self-contained + EF Core/SQLite/ClosedXML/QuestPDF)
  via `PublishTrimmed` si la compatibilité est confirmée avec toutes les dépendances.
- Réglages spécifiques aux modules Courroies/Roulements (actuellement sans réglage propre, à
  l'inverse du module Filtre).
