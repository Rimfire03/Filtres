# Suivi maintenance filtres à air

Application Windows (.NET 8 / WPF) qui remplace le classeur Excel de suivi de
maintenance des filtres à air. Stockage SQLite portable, exécutable unique
autonome (aucune installation requise).

## Structure de la solution

```
FiltresApp.sln
src/
  FiltresApp.Core/        Modèles, DbContext EF Core, services métier (calcul
                           des échéances, export PDF/Excel)
  FiltresApp/              Application WPF (MVVM, CommunityToolkit.Mvvm)
```

## Prérequis pour builder

- SDK .NET 8. Sur ce poste, le SDK est installé localement (pas dans le PATH
  système) : avant toute commande `dotnet`, exécuter
  ```powershell
  $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
  ```

## Builder

```powershell
dotnet build FiltresApp.sln -c Debug
```

## Lancer en développement

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet run --project src\FiltresApp\FiltresApp.csproj
```

> Remarque : si vous lancez directement le `.exe` compilé (et non via `dotnet
> run`) sur ce poste, définissez `DOTNET_ROOT` comme ci-dessus, sinon Windows
> peut résoudre un ancien runtime .NET globalement installé au lieu du SDK 8
> local. Ce problème ne concerne PAS l'exécutable publié en autonome
> (self-contained), qui embarque son propre runtime.

## Publier l'exécutable portable

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
dotnet publish src\FiltresApp\FiltresApp.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false
```

Le résultat se trouve dans :
```
src\FiltresApp\bin\Release\net8.0-windows\win-x64\publish\
  FiltresApp.exe        <- exécutable unique, autonome (~190 Mo)
  data\filtres.db        <- base de données SQLite pré-remplie (voir plus bas)
  LatoFont\               <- police embarquée utilisée par la génération PDF
```

Pour distribuer l'application, copier tout le contenu de ce dossier
`publish\` (l'exe a besoin du dossier `data\` et, après premier lancement, du
fichier `settings.json` généré à côté de lui). L'exécutable démarre sans
qu'aucune version de .NET ne soit installée sur la machine cible.

## Emplacement des données (mode portable)

- `settings.json` est créé au premier lancement **à côté de l'exécutable**
  (jamais dans le registre ni dans `%AppData%`). Seule exception : le choix des
  colonnes affichées et leurs largeurs, propres à chaque ordinateur (voir « Affichage des grilles »). Il contient :
  - `DatabasePath` : chemin (relatif ou absolu) vers le fichier SQLite.
    Par défaut `data\filtres.db`, relatif au dossier de l'exe.
  - `PdfExportPath` : dossier de destination des exports PDF (remplace le
    chemin réseau codé en dur de l'ancienne macro `exportcmd`).
- Ces deux chemins sont modifiables dans l'écran **Paramètres** de
  l'application ; le bouton "Enregistrer les paramètres" recharge
  immédiatement la base de données au nouvel emplacement.
- La base fournie (`data\filtres.db`) a été pré-remplie via l'import du
  classeur Excel source réel (voir ci-dessous), afin que l'utilisateur
  retrouve immédiatement toutes ses données actuelles.

## Utilisation à plusieurs (base sur disque réseau)

La base est prévue pour être placée sur un disque réseau partagé. Pour éviter toute corruption ou
perte de données quand plusieurs personnes ont l'application ouverte en même temps, l'accès suit la
règle **« un seul rédacteur, plusieurs lecteurs »** :

- Le **premier** poste qui ouvre la base obtient l'accès en **lecture/écriture**. Il garde ouvert en
  exclusivité un fichier `filtres.db.lock` créé à côté de la base (qui contient le nom de
  l'utilisateur et du poste).
- Les postes suivants s'ouvrent en **lecture seule** : dès l'ouverture, un message s'affiche
  « Données en lecture seule : fichier actuellement utilisé par @nom_de_session_windows », puis un
  bandeau jaune reste en haut de la fenêtre pour le rappeler ; les boutons de modification (ajouter, modifier, supprimer,
  enregistrer un remplacement, case « Réalisé »...) sont désactivés, et la connexion
  SQLite elle-même est ouverte en lecture seule (aucune écriture possible, même par erreur).
  La consultation, l'impression et les exports PDF/Excel restent disponibles.
- Les postes en lecture seule voient les modifications du rédacteur en changeant d'écran (les
  données sont relues à chaque chargement).
- Quand le rédacteur ferme l'application, le verrou est libéré ; un autre utilisateur doit alors
  **relancer** l'application pour obtenir l'accès en écriture. Le verrou est aussi libéré
  automatiquement par Windows si l'application plante ou si le poste est éteint : il n'y a jamais de
  fichier `.lock` à supprimer à la main.

Recommandations :
- Placer la base sur un vrai partage réseau Windows (SMB), **jamais** dans un dossier synchronisé
  (OneDrive, Dropbox, Google Drive...) : la synchronisation corromprait le fichier SQLite.
- Les utilisateurs doivent avoir les droits en écriture sur le dossier de la base (création du
  fichier `.lock` et du journal SQLite). Un poste sans ces droits s'ouvre en lecture seule.
- Ne pas activer le mode WAL de SQLite : il ne fonctionne pas sur un disque réseau.

## Suppression de l'historique d'une année

Écran **Paramètres** → carte « Supprimer l'historique d'une année », avec deux fonctions séparées :

- **Filtres G4 plissés, G4 plan, G3 et Charbon** : supprime les remplacements (cases « Réalisé » /
  dates) de l'année choisie. Les filtres F7 à H13 ne sont pas touchés.
- **Filtres F7 à H13** : supprime les remplacements datés de l'année choisie, uniquement pour ces
  filtres.

Chaque liste ne propose que les années qui ont réellement un historique. Les filtres eux-mêmes ne
sont jamais supprimés. Une confirmation indique le nombre de remplacements concernés, et une copie
de la base est faite juste avant, à côté du fichier
(`filtres.db.avant-suppression-historique-<année>-<date>.bak`). Désactivé sur un poste en lecture
seule.

## Version de la base de données et mises à jour automatiques

La base porte un numéro de version (stocké dans le fichier SQLite, `PRAGMA user_version`, plus la
version du logiciel qui l'a mise à jour en dernier dans la table `DbInfo`). Les deux versions —
logiciel et base — sont affichées en haut de l'écran **Paramètres**.

- **Après une mise à jour du logiciel**, si la base est dans une version inférieure à celle attendue,
  le premier poste qui l'ouvre **en écriture** **propose la mise à jour** au lancement (liste des
  modifications à appliquer, réponse Oui/Non). Si l'utilisateur refuse, le démarrage s'arrête avec un
  message explicite (la base reste inchangée) ; il suffit de relancer et d'accepter. Pour une base
  créée avant ce système (version 0), la liste affiche toutes les étapes à partir de 1 : les étapes 1 à 5
  sont déjà présentes et ne modifient rien. Avant toute modification, une copie complète est faite à
  côté du fichier : `filtres.db.avant-maj-v<ancienne version>-<date>.bak` (à supprimer à la main une fois la
  mise à jour validée). Chaque étape est appliquée dans une transaction : en cas d'erreur, la base
  reste à la dernière version réussie et le démarrage s'arrête avec le détail de l'erreur.
- **Démarrage bloqué avec un message explicite** si le poste n'a pas la bonne version :
  - logiciel **trop ancien** pour la base (déjà mise à jour par une version plus récente) : le
    message indique la version de la base, la version du logiciel qui l'a mise à jour et demande
    d'installer la dernière version. Rien n'est modifié dans la base ;
  - logiciel **plus récent** que la base alors que ce poste est en **lecture seule** (le poste qui a
    l'accès en écriture utilise encore l'ancienne version) : le message indique qui détient l'accès
    en écriture et explique que la base sera mise à jour au prochain lancement du logiciel à jour sur
    un poste en écriture.
- En pratique : **mettre à jour tous les postes**, puis lancer d'abord le logiciel sur un poste quand
  personne d'autre ne l'a ouvert.
- Les versions du logiciel antérieures à ce système ne contrôlent pas la version de la base : elles
  ne sont pas bloquées et doivent être mises à jour en priorité.

Pour les développeurs : les évolutions du schéma se déclarent dans la liste `Migrations` de
`DbContextFactory` (`src\FiltresApp.Core\Services\DbContextFactory.cs`). Ne jamais modifier ni
renuméroter une migration déjà publiée, toujours en ajouter une nouvelle à la fin, et écrire les
modifications de données en SQL brut (pas via le modèle EF, qui aura évolué). Une base neuve est créée
directement à la dernière version. Les versions 1 à 5 reprennent les mises à jour faites avant ce
système (idempotentes) ; la version 6 ajoute la colonne « Destination » ; la version 7 supprime la
colonne « Unité » et son contenu (récupérable dans la sauvegarde `.bak` faite avant la mise à jour).
La version 8 ajoute la colonne « Inventaire », la 9 les familles Inventaire/Commande, la 10 la
colonne « Type » des filtres F7 à H13.

## Import Excel (supprimé)

Le système d'import a été **supprimé** : bouton « Importer depuis Excel » de l'écran Paramètres,
outil en ligne de commande `FiltresApp.ImportCli`, et services `ExcelImportService` /
`ArchiveImportService`. Les données déjà importées (classeur courant et archives 2014-2025) restent
en base ; elles se gèrent désormais uniquement depuis l'application. Les sections historiques plus
bas qui décrivent ces imports sont conservées pour mémoire. Le code supprimé reste consultable dans
l'historique git.

## Affichage des grilles

- **Retour à la ligne** : un texte plus long que la largeur de sa cellule passe à la ligne, et la
  hauteur de la ligne s'adapte automatiquement (32 px minimum).
- **Colonnes affichées, au choix de chaque ordinateur** : clic droit sur n'importe quel en-tête de
  colonne → cocher/décocher les colonnes à afficher, ou « Afficher toutes les colonnes ». Au moins
  une colonne reste toujours visible. Le choix est mémorisé séparément pour chaque écran, sur
  l'ordinateur (et la session Windows) de l'utilisateur, dans
  `%LocalAppData%\FiltresApp\grilles.json` : il n'est pas partagé avec les autres postes, même si
  l'exécutable et la base sont sur le disque réseau. Disponible sur les écrans de filtres, Liste K7,
  Inventaire, Commande et dans la fenêtre « Rattacher des filtres ». Les impressions et exports
  gardent toutes les colonnes.
- **Largeur des colonnes mémorisée** : quand l'utilisateur élargit ou rétrécit une colonne à la souris,
  la largeur est enregistrée au même endroit (par écran et par ordinateur) et restaurée à la prochaine
  ouverture. Clic droit sur un en-tête → « Réinitialiser les largeurs » pour revenir aux largeurs
  d'origine.

## Fonctionnalités par écran

**Puce « Lié » (écrans Filtres G4 plissés, G4 plan, G3 et Charbon)** : première colonne de la
grille, puce **verte** si le filtre est rattaché à une ligne de Commande / Inventaire (bouton
« Rattacher des filtres... » de l'écran Commande), **rouge** sinon. Le survol de la puce indique la
ligne concernée. Recalculée à chaque ouverture de l'écran. Les filtres F7 à H13 ne sont pas
rattachables et n'ont donc pas de puce.

Chaque catégorie de filtre à périodicité mensuelle (G4 plissé, G4 plan, G3,
Charbon) propose : liste/grille, filtre d'affichage par mois (équivalent
`F_sheet`), enregistrement d'un remplacement réalisé via une boîte de dialogue
(échéance recalculée automatiquement en lecture seule), **case à cocher
"Réalisé" directement dans la grille** pour le mois en cours (voir plus bas),
impression (feuille de terrain avec cases à cocher, voir plus bas), export Excel de
l'année, et CRUD complet.

Filtres F7 à H13 (colonnes : nom de la centrale, dimension, **type** — texte libre saisi dans
« Modifier », migration v10 —, qté en place, dernier changement, nb remplacements) :
historique de remplacements (qté + date) sans périodicité
fixe (pas de case « Réalisé » par mois pour cette feuille, voir plus bas),
CRUD, impression, export Excel de l'année.

Liste K7 (lieux regroupés par famille éditable, voir plus bas), Inventaire,
Commande chmy : CRUD en grille, impression ; Inventaire et Commande chmy
proposent en plus un export PDF vers le dossier configuré dans les Paramètres
(équivalent `exportcmd`, sans chemin réseau codé en dur). **Depuis la fusion
Inventaire / Commande chmy (voir plus bas), ces deux écrans affichent et
modifient les mêmes lignes.**

> L'onglet « Commande chmy » s'appelle désormais simplement **« Commande »** dans
> l'application (titre de l'écran et des impressions/exports compris). Le reste de ce
> README conserve l'ancien nom dans les sections historiques ; la feuille du classeur
> Excel source s'appelait « Commande chmy ».

**Colonnes des écrans Commande et Inventaire** (même table `OrderLine`, seuls les libellés
affichés ont changé, les données existantes sont conservées telles quelles) :

| Libellé affiché         | Champ en base   | Remarque                                     |
|-------------------------|-----------------|----------------------------------------------|
| Dimension               | `Designation`   | ex-« Désignation »                           |
| Destination             | `Destination`   | nouveau champ, vide pour les lignes existantes (colonne ajoutée automatiquement au démarrage du poste rédacteur) |
| Type                    | `Dimension`     | ex-« Dimension »                             |
| Référence fournisseur   | `Notes`         | ex-« Notes », placée après Type              |

- **Menu rapide de rattachement (Commande)** : clic droit sur une cellule « Filtres liés » → liste
  cochable des filtres déjà rattachés à la ligne, puis des filtres de dimension correspondante
  (même comparaison approximative que la fenêtre complète, 20 au maximum). Cocher rattache, décocher
  retire ; un filtre déjà rattaché à une autre ligne est signalé (« déjà rattaché à … ») et lui est
  retiré s'il est coché. Dernière entrée : « Rattacher des filtres... (tous les filtres) » pour la
  fenêtre complète. Non modifiable en lecture seule.
- **Commande** : Dimension, Destination, Type, Référence fournisseur, Filtres liés,
  **Quantité à commander en dernier**. La colonne « Filtres liés » reste dans la grille mais n'est jamais
  imprimée ni exportée en PDF.
- **Inventaire** : Dimension, Destination, Type, Référence fournisseur, Besoin mars (calculé),
  Besoin septembre (calculé), **Inventaire**, Quantité. Dans cet écran, **« Quantité » est calculée** :
  plus grande valeur entre Besoin mars et Besoin septembre, moins Inventaire (vide si aucun filtre
  n'est rattaché, 0 si le stock dépasse le besoin), mise à jour dès qu'une valeur d'inventaire
  est validée ; elle n'est donc plus saisie dans la fenêtre « Modifier » de l'Inventaire.
- **Quantité à commander (Commande)** : même valeur calculée que la « Quantité » de l'Inventaire
  (max(Besoin mars, Besoin septembre) − Inventaire, 0 si négatif, vide si aucun filtre rattaché),
  reprise à l'impression et dans le PDF. Elle n'est plus saisie dans « Modifier » ; l'ancienne
  quantité saisie (champ `OrderLine.Quantite`) reste en base mais n'est plus affichée. Les colonnes « Besoin » ont été déplacées
  depuis Commande ; elles restent calculées à partir des filtres rattachés à la ligne (bouton
  « Rattacher des filtres... » de l'écran Commande).
  La colonne « Inventaire » (nombre entier, migration v8) se saisit **directement dans la grille** :
  un clic dans la cellule (fond bleuté) suffit pour taper la valeur ; elle est enregistrée avec
  Entrée, Tab ou en cliquant ailleurs, Échap annule la saisie en cours, et une cellule vidée efface
  la valeur. Une saisie qui n'est pas un nombre entier est refusée avec un message. En lecture seule,
  la cellule n'est pas modifiable. La valeur figure aussi dans la fenêtre « Modifier », l'impression
  et l'export PDF de l'Inventaire. La colonne
  « Unité » a été supprimée (écran, fenêtres d'édition de Commande et Inventaire, impression, export
  PDF et base de données, migration v7).
- Les fenêtres d'ajout/modification, l'impression et l'export PDF suivent le même ordre et les
  mêmes libellés.

**Familles (Inventaire et Commande)** — automatiques par défaut, avec choix manuel possible.

- **Automatique** : la famille d'une ligne est déduite de la catégorie des filtres qui lui sont
  rattachés (bouton « Rattacher des filtres... » de l'écran Commande), voir le tableau ci-dessous.
- **Manuel** : dans la fenêtre « Modifier » (ou « Ajouter »), le champ « Famille » propose
  « Automatique (d'après les filtres rattachés : …) » — qui rappelle la famille calculée — ou une
  famille au choix (Filtres G4 plissés, Filtres G4 plan, Filtres G3, Charbon, Sans famille). Un
  choix manuel prime sur le calcul automatique, même si les filtres rattachés changent ensuite ;
  revenir sur « Automatique » pour réactiver le calcul. Une ligne ajoutée pendant qu'une famille
  est filtrée reçoit cette famille par défaut. Stocké dans `OrderLine.FamilyOverride` (migration
  v11).

Famille automatique :

| Filtres rattachés à la ligne                       | Famille              |
|----------------------------------------------------|----------------------|
| uniquement des filtres de « Filtres G4 plissés »   | Filtres G4 plissés   |
| uniquement des filtres de « Filtres G4 plan »      | Filtres G4 plan      |
| uniquement des filtres de « Filtres G3 »           | Filtres G3           |
| uniquement des filtres de « Charbon »              | Charbon              |
| des filtres de plusieurs catégories                | Plusieurs familles   |
| aucun filtre                                       | Sans famille         |

Les filtres F7 à H13 ne sont pas rattachables et n'ont donc pas de famille pour le moment.

- Les lignes sont **regroupées sous un bandeau titre par famille** (même style que la Liste K7),
  dans l'ordre du tableau ci-dessus.
- Liste « Famille » en haut de chaque écran pour **filtrer** sur une famille (chaque écran garde son
  propre filtre).
- L'impression et l'export PDF suivent le filtre en cours (famille ajoutée au titre) et reprennent
  le regroupement : une ligne titre « — NOM DE LA FAMILLE — » avant les lignes de chaque famille.
- Historique : une première version (migration v9) permettait de créer des familles à la main. Elle
  a été remplacée par ce calcul automatique ; la table `OrderFamilies` et la colonne
  `OrderLines.OrderFamilyId` restent dans les bases déjà mises à jour mais ne sont plus utilisées.

"Pour devis" et "Filtres à refacturer" ont été supprimés définitivement de
l'application (interface, code et données), voir plus bas.

### Alignement des colonnes de grilles et format d'affichage de la périodicité

Dans toutes les grilles (`DataGrid`) de l'application (écrans G4 plissé/G4 plan/G3/Charbon,
F7-H13, Liste K7, Inventaire, Commande chmy, ainsi que les fenêtres "Consulter l'historique..."
et "Rattacher des filtres...") :

- La **première colonne d'identification de la ligne** (emplacement/lieu/désignation/nom de la
  centrale selon l'écran — pas nécessairement la toute première colonne déclarée quand une case à
  cocher technique de sélection la précède, ex. "Rattaché" dans `FilterLinkWindow`) reste alignée
  à **gauche** (comportement par défaut).
- **Toutes les autres colonnes** (dimension, type, quantité, périodicité, dates, compteurs,
  case à cocher, etc.) ont leur contenu **centré** horizontalement et verticalement, pour une
  lecture plus homogène des valeurs courtes. Ceci est appliqué via deux styles réutilisables
  définis une seule fois dans `Styles/Controls.xaml` plutôt que dupliqués dans chaque écran :
  `CenteredCellStyle` (pour l'`ElementStyle` des `DataGridTextColumn`, cible `TextBlock`) et
  `CenteredCheckBoxCellStyle` (pour l'`ElementStyle` des `DataGridCheckBoxColumn`, cible
  `CheckBox`). Les colonnes à `DataGridTemplateColumn` (case à cocher "Réalisé", `DatePicker`
  "Date du changement", etc.) centrent directement leur contrôle interne dans le `DataTemplate`.

Par ailleurs, la colonne "Périodicité" des grilles de filtres (G4 plissé/G4 plan/G3/Charbon,
`PeriodicFilter.PeriodicityDisplay`, utilisée aussi dans le sélecteur de rattachement
`FilterLinkWindow`) affiche désormais les mois de périodicité sous forme de **chiffres séparés
par `/`** (ex. `1/3/5/7/9/11`) au lieu des noms de mois abrégés utilisés précédemment (ex. "Jan,
Mar, Mai..."). Seul l'affichage a changé : la valeur stockée en base (`PeriodicFilter.Periodicity`,
format `/1/3/5/7/9/11/`) est inchangée. La colonne "Périodicité" du panneau "Familles K7" (écran
Liste K7, `K7Family.PeriodicityDisplay`) n'est pas concernée par ce changement de format, qui ne
visait que la colonne "Périodicité" des grilles de filtres.

### Sélecteur d'année et historique (remplace l'ancienne remise à zéro annuelle)

L'ancienne fonctionnalité de remise à zéro / effacement (`Vidange`,
`Vidangecharbon`, `raz_inventaire`, boîte de dialogue de confirmation par
saisie du mot "effacer") a été **entièrement supprimée** : aucune donnée n'est
plus jamais effacée en masse par l'application.

À la place, un **sélecteur d'année** apparaît en haut de la barre latérale
(visible depuis tous les écrans, `MainViewModel.YearContext` /
`App.YearContext`, partagé par toute l'application) :
- Changer d'année ne supprime rien : l'historique de chaque année reste
  consultable en resélectionnant cette année (les remplacements sont stockés
  avec un numéro de mois **et** une année, voir "Modèle de données" ci-dessous).
- Si une année n'a pas encore de ligne de suivi pour un filtre donné, elle est
  simplement considérée comme "non réalisée" à l'affichage (aucune ligne n'est
  créée en base tant que l'utilisateur ne coche pas la case) : c'est
  l'initialisation "à la volée" la plus simple et la plus robuste.

Dans chaque grille de suivi (G4 plissé, G4 plan, G3, Charbon), les colonnes « Réalisé » et « Date
du changement » portent **toujours sur le mois en cours** (date du jour), pour l'année choisie dans
la barre latérale ; le mois concerné est rappelé au-dessus de la grille. L'ancien sélecteur « Mois
consulté » a été supprimé. Pour enregistrer un remplacement sur un autre mois, utiliser le bouton
« Enregistrer un remplacement », qui demande le mois. Le filtre d'affichage « Filtrer par mois »
est inchangé.
- **Cocher** la case "Réalisé" d'une ligne fixe
  automatiquement la date de changement à la date du jour (éditable ensuite
  dans la colonne "Date du changement" juste à côté) et crée si besoin la
  ligne de suivi pour (mois en cours, année consultée).
- **Décocher** la case réinitialise le statut et **vide la date** : par choix
  délibéré, on supprime la ligne de suivi du mois plutôt que de garder une
  ligne "réalisée sans date", ambiguë. L'historique des autres mois/années
  n'est jamais touché.

La feuille F7 à H13 n'a pas de périodicité mensuelle fixe dans le modèle de
données d'origine (historique de remplacements ponctuels, pas un suivi mois
par mois) : elle n'a donc pas de case à cocher par mois, mais son sélecteur
d'année filtre "Dernier changement" et "Nb remplacements" aux remplacements
datés de l'année consultée, et elle est incluse dans l'export Excel de
l'année comme les 4 autres catégories.

### Export Excel de l'année

Bouton "Exporter l'année en Excel" (présent sur les écrans de suivi de
filtres et dans Paramètres, avec sélection d'année) : génère un classeur
`.xlsx` via ClosedXML, avec une feuille par catégorie (G4 plissés, G4 plan,
G3, F7-H13, Charbon) contenant emplacement, dimension, qté en place,
périodicité (et compteur d'heures pour Charbon), puis le détail mensuel
réalisé/date de l'année sélectionnée (F7-H13 : liste des remplacements datés
de l'année). Le fichier est déposé dans le **même dossier configurable que
l'export PDF** (`AppSettings.PdfExportPath`, écran Paramètres) : il
s'agissait déjà du dossier d'export générique de l'application, ajouter un
réglage séparé pour un simple changement de format de fichier n'apportait
rien.

### Impression (écrans G4 plissé, G4 plan, G3 et Charbon)

Le bouton « Imprimer » est la seule impression de ces écrans (l'ancien bouton « Imprimer le mois
consulté » a été supprimé avec le sélecteur « Mois consulté »). Il imprime **uniquement ce qui est
visible** :

- les lignes affichées (donc filtrées par « Filtrer par mois » si un mois est choisi ; le mois est
  alors ajouté au titre) ;
- les colonnes de la grille, dans le même ordre, **sauf celles masquées sur ce poste** (clic droit
  sur un en-tête). La puce « Lié » est imprimée « Oui » / « Non », la case « Réalisé » « Oui » ou
  vide ;
- plus une dernière colonne « Fait » avec une **grande case à cocher vierge** (environ 1,8 cm) par
  ligne, à cocher à la main sur le terrain.

Les autres écrans gardent leur impression habituelle, et l'export Excel de l'année est inchangé.

## Choix d'implémentation notables

- **EF Core + SQLite** avec `EnsureCreated()` (pas de migrations) : adapté à
  une application mono-poste dont le schéma initial est stable ; à revoir si
  des évolutions de schéma ultérieures sont nécessaires (passer à
  `dotnet ef migrations`).
- **DbContext unique partagé** pendant toute la session de l'application
  (composition root dans `App.xaml.cs`), plus simple à gérer qu'un contexte
  par écran pour une application mono-utilisateur hors ligne.
- **Colonnes "prévu" non stockées** : recalculées à l'affichage
  (`PeriodicFilter.NextDueDate`), ce qui évite de répliquer les formules
  Excel `MAX`/`COUNTA` d'origine, parfois irrégulières (ex. colonnes
  "COMPTEUR D'HEURES" insérées de façon non systématique dans la feuille
  Charbon).
- **Export PDF** via QuestPDF (licence Community) plutôt que d'automatiser
  Excel/imprimante virtuelle, pour un rendu fiable et sans dépendance externe.
- **Dialogues de saisie/édition génériques** (`DynamicEditWindow`) pilotés
  par une liste de champs déclarative, pour éviter de dupliquer une fenêtre
  XAML par type d'entité (7 entités au total).
- **Import Excel** : la mise en correspondance des colonnes mensuelles
  ("changement réalisé en Janvier", "Date de changement en Février", ...) se
  fait par détection du nom du mois dans l'en-tête de chaque colonne plutôt
  que par position fixe, car la feuille "Charbon" insère de façon irrégulière
  des colonnes "COMPTEUR D'HEURES" qui décalent les colonnes suivantes selon
  les mois.
- **Modèle de données du suivi mensuel** : `FilterReplacement` (table enfant
  de `PeriodicFilter`, clé étrangère `PeriodicFilterId`) porte déjà, depuis la
  conception initiale de l'application, un couple `Month` + `Year` explicite
  (et non des colonnes fixes "Janvier réalisé"/"Janvier date" sur
  `PeriodicFilter` lui-même). **Aucune migration de schéma n'a donc été
  nécessaire** pour ajouter le sélecteur d'année : il suffisait de filtrer les
  remplacements déjà en base par année à l'affichage/à l'écriture. Les 1042
  lignes importées à l'origine (275 `PeriodicFilter` + 203 `OpacimetricFilter`
  + 73 `K7Location` + 161 `InventoryLine` + 300 `OrderLine` + 30
  `RefacturingLine`) et leurs 560 `FilterReplacement` / 238
  `OpacimetricReplacement` associés sont restées intactes ; les
  `FilterReplacement` existants étaient déjà tous rattachés à l'année 2026
  (année d'exécution de l'import initial).
- **Git** : `git` n'est pas installé sur ce poste (aucun exécutable trouvé, ni
  winget/choco/scoop pour l'installer) ; le dépôt n'a donc pas pu être
  initialisé ni committé comme demandé. Dès que Git sera disponible, lancer
  `git init`, `git add -A`, `git commit -m "Initial commit"` à la racine du
  projet.
- **Correction feuille "Filtres F7 a H13" (import `OpacimetricFilter`)** : la
  feuille source n'est pas une table unique mais un enchaînement de
  sous-sections (une par classe/type de filtre : F7, F9, H10, H11, blocs
  Maxilam/Sofilam, E12, H14...), chacune précédée d'une ligne de titre
  ("MAINTENANCE FILTRES ...") et/ou d'une ligne d'en-tête répétée ("NOM DE LA
  CENTRALE D'AIR | ..."), et souvent suivie d'une mini-table de stock
  ("Dimensions ... | Stock réel | Stock Mini | Qté à commander") sans rapport
  avec une centrale. L'import initial traitait chaque ligne non vide de la
  colonne A comme une centrale, ce qui important à tort ces lignes d'en-tête/
  titre/stock : en particulier les lignes de la mini-table de stock, où la
  colonne A contient en réalité un code de **dimension** (ex.
  `592 x 592 x 292`) et non un nom de centrale — d'où des lignes où
  "Nom de la centrale" affichait une dimension. `ExcelImportService.
  ImportOpacimetricSheet` détecte désormais et ignore ces lignes non-données
  (en-tête répétée, titre de section, total, mini-table de stock) pour ne
  garder que les véritables lignes centrale/dimension/quantité, ce qui évite
  le bug pour tout futur ré-import. Les 203 lignes `OpacimetricFilter` déjà en
  base ont été corrigées sur place (script ponctuel, `UPDATE` uniquement,
  aucune ligne supprimée/recréée) : les 127 lignes réellement liées à une
  centrale étaient déjà correctes (colonnes non permutées dans le fichier
  source réel), et les 76 lignes correspondant à des en-têtes/titres/lignes de
  stock ont été réétiquetées clairement (préfixe `[Section]`/`[Total]`/`[Stock
  filtres]` dans `Location`, note explicative) plutôt que supprimées, pour ne
  pas rompre les `OpacimetricReplacement` déjà liés par clé étrangère.
  Compteurs inchangés après correction : 203 `OpacimetricFilter`, 238
  `OpacimetricReplacement`.

### Rattachement manuel filtre <-> ligne de commande et calcul du besoin semestriel

L'écran **"Commande chmy"** (modèle `OrderLine`, `OrderListViewModel`/`OrderView`)
permet de **rattacher manuellement** une ou plusieurs lignes de filtres à
périodicité (G4 plissé, G4 plan, G3, Charbon — pas F7-H13, qui n'a pas de
notion de périodicité/besoin dans l'Excel d'origine) à une ligne de commande,
afin de calculer automatiquement son besoin de commande semestriel.

- **Rattachement manuel, pas de matching automatique** : bouton "Rattacher des
  filtres..." sur l'écran → ouvre un sélecteur (`FilterLinkWindow`) listant
  tous les filtres des 4 catégories concernées (catégorie / emplacement /
  dimension / type / qté en place / périodicité affichés pour identification,
  avec un champ de recherche libre étant donné le volume : ~275+50+100+10
  filtres selon les catégories) et des cases à cocher. L'utilisateur choisit
  explicitement lesquels rattacher ; aucun algorithme ne devine le
  rattachement à partir de la dimension.
- **Modèle de données** : nouvelle table de liaison many-to-many
  `OrderLinePeriodicFilter` (`OrderLineId` + `PeriodicFilterId`), navigations
  `OrderLine.FilterLinks` / pas de navigation inverse sur `PeriodicFilter`
  (non nécessaire). Un même filtre peut être rattaché à plusieurs lignes de
  commande et réciproquement.
- **Migration de schéma sans perte de données** : l'application n'utilise pas
  `dotnet ef migrations` (voir "Choix d'implémentation" plus bas), et
  `Database.EnsureCreated()` ne fait rien sur une base déjà existante même si
  le modèle a évolué. `DbContextFactory.EnsureDatabaseCreated()` exécute donc
  désormais, après `EnsureCreated()`, un `CREATE TABLE IF NOT EXISTS` /
  `CREATE INDEX IF NOT EXISTS` explicite pour la table
  `OrderLinePeriodicFilters` : sur une base neuve, `EnsureCreated()` l'a déjà
  créée et ces instructions ne font rien ; sur la base existante
  (1042 lignes + historique), elles ajoutent uniquement la nouvelle table,
  sans toucher aux données déjà présentes. Vérifié par un test de non
  régression (comptage avant/après sur une copie de `filtres.db` : tous les
  effectifs — 275 `PeriodicFilter`, 203 `OpacimetricFilter`, 73 `K7Location`,
  161 `InventoryLine`, 300 `OrderLine`, 30 `RefacturingLine`, 560
  `FilterReplacement`, 238 `OpacimetricReplacement` — identiques après
  l'ajout de la table).
- **Calcul du besoin (`OrderNeedCalculationService`, dans `FiltresApp.Core`)**
  reproduit fidèlement la logique semestrielle de l'Excel d'origine (colonnes
  F/G des feuilles G4 plissés / G4 plan / G3 / Charbon, formule
  `=(COUNTA(fenêtre des colonnes "changement prévu en [mois]")+1)*Qté en
  place`), vérifiée sur le classeur source (`Filtres 2026.xlsm`, feuille
  "Filtres G4 plissés", ligne 5) :
  - **Besoin pour septembre** (inventaire du 15/07 au 31/07) : fenêtre de
    mois = **août → mars** (août, sept, oct, nov, déc, jan, fév, mars).
  - **Besoin pour mars** (inventaire du 15/01 au 31/01) : fenêtre de mois =
    **février → septembre** (fév, mars, avr, mai, juin, juil, août, sept).
  - Pour chaque filtre rattaché : `(nombre d'échéances de la périodicité
    tombant dans la fenêtre + 1) × quantité en place` ; le besoin de la ligne
    de commande est la **somme** sur tous les filtres qui lui sont rattachés.
  - Les deux fenêtres se chevauchent volontairement (comme dans l'Excel
    d'origine, qui les définit indépendamment plutôt que comme complémentaires
    exactes) : c'est la marge de sécurité déjà présente dans le classeur
    source, reproduite telle quelle plutôt que "corrigée".
  - Calcul recalculé en direct à l'affichage (propriétés `[NotMapped]`
    `OrderLine.NeedMars` / `NeedSeptembre`, `null` tant qu'aucun filtre n'est
    rattaché) : se met donc à jour automatiquement si le rattachement change,
    ou si un filtre rattaché change de quantité en place / périodicité.
  - Le modèle `OrderLine` n'a pas de champ "stock actuel" distinct de
    `Quantite` (qui est la quantité à commander saisie manuellement, un champ
    de saisie libre déjà existant, pas un stock) : pas de colonne "à
    commander = besoin − stock" ajoutée pour ne pas inventer un champ qui
    n'existe pas dans le modèle d'origine. Les deux besoins calculés
    (mars / septembre) sont affichés à titre indicatif à côté de la quantité
    saisie manuellement, que l'utilisateur ajuste lui-même si besoin.
- **UI** : sur la grille "Commande chmy"/"pour devis", colonnes "Filtres
  liés" (ex. "3 filtres liés"), "Besoin mars (calculé)", "Besoin septembre
  (calculé)" ; inclus aussi dans l'impression et l'export PDF de l'écran.

#### Affinements du rattachement manuel (filtrage par dimension, indicateur de conflit, exclusivité)

Trois affinements ont été apportés au sélecteur de rattachement (`FilterLinkWindow`) et à son
enregistrement, sans toucher au calcul du besoin ni à la table de liaison existante :

1. **Ne proposer que les filtres de dimension correspondante**
   (`FiltresApp.Core.Services.DimensionMatchService`) : par défaut, le sélecteur n'affiche plus tous les
   filtres des 4 catégories, mais uniquement ceux dont la dimension correspond (comparaison tolérante) à
   celle de la ligne de commande en cours.
   - **Constat contre-intuitif sur les données réelles**, vérifié avant d'implémenter (461 `OrderLine`,
     275 `PeriodicFilter`) : le champ `OrderLine.Dimension` ne contient **pas** une dimension physique
     mais un code de catégorie/média hérité du classeur d'origine (ex. "PLG G4", "PJG G4", "MCF G3",
     "MTPS F7", "FLX U15") - comparer ce champ tel quel au format `PeriodicFilter.Dimension`
     ("1035x698x45", etc.) donne **0 correspondance** sur toute la base. La dimension physique réelle
     d'une ligne de commande est en fait le préfixe numérique de son champ `Designation` (ex.
     "630X325x47*  MAGNOLIAS", "415x385x45  CTA DE CLERAMBAULT" - héritage direct de la colonne
     "Dimensions" de la feuille Excel d'origine, qui contenait dimension + emplacement dans la même
     cellule). `DimensionMatchService.GetOrderLineDimensionKey` extrait donc ce préfixe (motif
     `NNNxNNN[xNN]`, séparateurs `x`/`X`/`×`/`*` tolérés, espaces ignorés, virgule décimale acceptée) et
     le compare, normalisé (minuscules, sans espaces, virgule → point), au même motif extrait de
     `PeriodicFilter.Dimension` (qui contient lui aussi parfois du texte parasite après les chiffres, ex.
     "535x345x20   4poches de 300"). Vérifié sur les données réelles : 261 des 396 lignes de commande où
     un motif de dimension a pu être extrait trouvent au moins un filtre correspondant (138 lignes ont un
     motif mais aucun filtre installé de cette dimension exacte - stock/inventaire sans filtre en place -
     et 62 lignes n'ont aucun motif de dimension identifiable dans leur désignation).
   - Si la ligne de commande n'a pas de dimension identifiable, ou si aucun filtre ne correspond, un
     message explicite apparaît ("Aucun filtre de dimension correspondante trouvé...") plutôt qu'une liste
     vide sans explication, et une case à cocher **"Afficher tous les filtres (toutes dimensions)"**
     permet de voir tous les filtres des 4 catégories en secours (comportement par défaut = filtrage par
     dimension, comme demandé).
   - **Comparaison approximative** (évolution ultérieure, remplace la comparaison exacte décrite
     ci-dessus) : colonne « Dimension » de la ligne (champ `Designation`), à défaut colonne « Type »
     (champ `Dimension`), comparée à la colonne « Dimension » du filtre. Chaque valeur est acceptée à
     **±5 mm** près (`DimensionMatchService.ToleranceMm`), largeur et hauteur peuvent être inversées
     (592x287 = 287x592), et l'épaisseur n'est comparée que si les deux côtés en ont une (592x592 =
     592x592x45).
   - **Option dans Paramètres** (carte « Rattachement des filtres », `AppSettings.LinkDimensionFilterEnabled`,
     enregistrée dans `settings.json`) : décochée, la fenêtre s'ouvre directement avec tous les filtres
     affichés (case « Afficher tous les filtres » cochée d'office).
   - Un filtre déjà rattaché à la ligne en cours reste toujours visible dans la liste filtrée même s'il ne
     correspond plus à la dimension calculée (ex. dimension modifiée après coup), pour ne jamais masquer
     un rattachement existant à l'utilisateur au risque qu'il le perde sans s'en rendre compte.
2. **Indicateur rouge pour les filtres déjà rattachés à une autre ligne** : le sélecteur interroge
   désormais aussi les rattachements existants vers d'**autres** lignes de commande
   (`OrderListViewModel.LinkFilters`) et les transmet à chaque `FilterPickItem`
   (`LinkedElsewhereLabel` / `IsLinkedElsewhere`). Dans `FilterLinkWindow`, ces lignes sont surlignées
   d'un fond rouge clair (`#FDE8E8`, cohérent avec `BrushDanger`/`BrushDangerDark` déjà définis dans
   `Styles/Colors.xaml`) et affichent une colonne "Rattachement existant" avec le texte "Déjà rattaché
   à : <désignation de l'autre ligne>" en rouge foncé gras, pour que l'utilisateur voie l'existence d'un
   rattachement avant de le modifier.
3. **Rattachement exclusif (un filtre = une seule ligne à la fois)**
   (`FiltresApp.Core.Services.FilterLinkService.SetLinks`) : la table de liaison many-to-many
   `OrderLinePeriodicFilter` est **conservée telle quelle en base** (aucune migration de schéma
   destructrice - option la plus simple, choisie car la vérification sur la base réelle a montré 0 ligne
   dans `OrderLinePeriodicFilters` : la fonctionnalité de rattachement, très récente, n'avait encore
   jamais été utilisée, donc aucune donnée de rattachement multiple à migrer). La règle d'exclusivité est
   appliquée au niveau applicatif, avant chaque enregistrement : quand un filtre nouvellement coché était
   déjà rattaché à une **autre** ligne de commande, cet ancien rattachement est supprimé avant de créer le
   nouveau (vérifié par un test dédié sur une copie de la base : rattacher un filtre à la ligne A puis à
   la ligne B laisse bien exactement 1 ligne dans `OrderLinePeriodicFilters`, rattachée à B, et 0 ligne
   restante pour A). `OrderListViewModel.LinkFilters` délègue désormais tout l'enregistrement à ce
   service plutôt que de manipuler `OrderLinePeriodicFilters` directement. Le calcul du besoin
   (`OrderNeedCalculationService`) n'a pas eu besoin d'être modifié : il continue de sommer les filtres de
   `OrderLine.FilterLinks`, qui ne contient plus jamais qu'un seul rattachement actif par filtre.

### Option "Changé tous les 15 jours" (G4 plissé uniquement, besoin de commande doublé)

Sur l'écran **"Filtres G4 plissés"** uniquement (pas G4 plan, G3, Charbon ni F7-H13 — cette option n'a
été demandée que pour cette catégorie), un **clic droit sur une ligne du tableau** ouvre un menu
contextuel avec une case à cocher **"Changé tous les 15 jours (besoin doublé)"**.

- **Modèle de données** : nouvelle propriété booléenne `PeriodicFilter.ChangedEvery15Days`
  (`src\FiltresApp.Core\Models\PeriodicFilter.cs`), `false` par défaut. Persistée immédiatement en base
  au clic (pas de bouton "Enregistrer" séparé), suivant le même pattern que la case à cocher "Réalisé"
  déjà existante sur la grille (`PeriodicFilterListViewModel.SetChangedEvery15Days` /
  `PeriodicFilterRowViewModel.ChangedEvery15Days`).
- **Migration de schéma sans perte de données** : même pattern additif que les évolutions précédentes
  (voir section "Rattachement manuel..." ci-dessus) : `DbContextFactory.EnsureChangedEvery15DaysColumn`
  ajoute la colonne `"ChangedEvery15Days" INTEGER NOT NULL DEFAULT 0` à la table `PeriodicFilters` via
  `ALTER TABLE` protégé par une vérification `PRAGMA table_info`, uniquement si elle n'existe pas déjà.
  Vérifié par un test de non régression (comptage avant/après sur une copie de `filtres.db`) : les 275
  `PeriodicFilter` existants sont inchangés après l'ajout de la colonne, tous initialisés à
  `ChangedEvery15Days = false`.
- **Impact sur le calcul du besoin (`OrderNeedCalculationService.NeedForFilter`)** : pour un filtre
  ayant l'option activée, le besoin calculé — `(nombre d'échéances de la périodicité dans la fenêtre + 1)
  × quantité en place` — est **multiplié par 2** avant d'être sommé dans le besoin de la ligne de commande
  rattachée. S'applique identiquement aux deux fenêtres ("Besoin pour mars" et "Besoin pour septembre"),
  puisque les deux passent par cette même méthode.
- **Indicateur visuel dans la grille** : une courte colonne "15j" (juste après "Périodicité") affiche
  "×2 / 15j" pour les filtres concernés (vide sinon), avec une info-bulle "Changement tous les 15 jours —
  besoin de commande doublé". La ligne entière est en plus surlignée d'un jaune pâle (`#FFF9C4`), avec le
  même mécanisme de style (`DataTrigger` sur `DataGridRow`) que le surlignage rouge "déjà rattaché" de
  `FilterLinkWindow.xaml` (voir section "Affinements du rattachement manuel" ci-dessus).
- **Restriction à G4 plissé** : `PeriodicFilterView.xaml` est une vue partagée par les 4 catégories
  périodiques. Le menu contextuel et la colonne indicateur ne sont donc affichés que si
  `PeriodicFilterListViewModel.ShowChangedEvery15DaysOption` (`_category == FilterCategory.G4Plisse`) est
  vraie : le `MenuItem` du menu contextuel est masqué (binding de `Visibility`) pour les autres
  catégories, et la colonne "15j" elle-même est masquée par code-behind
  (`PeriodicFilterView.xaml.cs`, `OnDataContextChanged`) — une `DataGridColumn` n'étant pas dans l'arbre
  visuel, sa visibilité ne peut pas être liée directement en XAML à une propriété du ViewModel.

### Suppression définitive de « pour devis » et « filtres à refacturer »

Une première évolution avait retiré ces deux écrans de la navigation tout en
conservant leurs données en base par précaution (voir historique Git/README
antérieur). L'utilisateur a ensuite **confirmé explicitement vouloir une
suppression complète et définitive**, données comprises : c'est ce qui a été
fait le 25/09/2026.

- **Données supprimées en base** (script ponctuel EF Core exécuté sur la base
  réelle après vérification sur une copie, voir plus bas pour les effectifs) :
  - Les **145 lignes `OrderLine`** de type `OrderDocumentType.PourDevis`
    (aucune n'était rattachée à un filtre via `OrderLinePeriodicFilter` au
    moment de la suppression : 0 ligne de liaison orpheline à nettoyer).
  - Les **30 lignes** de la table `RefacturingLine`, puis la table
    `RefacturingLines` elle-même (`DROP TABLE`), pour une suppression complète
    de la structure et pas seulement des données.
- **Code mort supprimé** :
  - `src/FiltresApp.Core/Models/RefacturingLine.cs` : fichier supprimé du
    projet (le modèle et son `DbSet` n'existaient déjà plus dans
    `FiltresDbContext` que pour cette table).
  - `FiltresDbContext` : `DbSet<RefacturingLine> RefacturingLines` et la
    configuration `Ignore(r => r.PrixTotalHT)` retirés.
  - `OrderDocumentType` (`OrderLine.cs`) : la valeur `PourDevis` a été retirée
    de l'énumération (il ne reste plus que `CommandeChmy`).
  - `ExcelImportService` : les appels d'import de la feuille "pour devis"
    (`ImportOrderSheet(..., OrderDocumentType.PourDevis, ...)`) et de la
    feuille "filtres a refacturer" ont été retirés, ainsi que la méthode
    `ImportRefacturingSheet` et le compteur `RefacturingLinesImported` /
    `ImportResult.Total` associé. Un futur réimport du classeur Excel
    ignorera donc silencieusement ces deux feuilles si elles existent encore
    dans le fichier source.
  - `FiltresApp.ImportCli/Program.cs` : la ligne de récapitulatif "Lignes à
    refacturer" a été retirée.
  - `RefacturingListViewModel`/`RefacturingView` et l'entrée de navigation
    "Pour devis" avaient déjà été supprimés par l'évolution précédente ; il
    n'en restait aucune trace à nettoyer (vérifié par recherche exhaustive
    dans le code, XAML compris).
  - `DbContextFactory.EnsureDatabaseCreated()` reproduit désormais aussi ce
    nettoyage (suppression des `OrderLine` de type `PourDevis` par SQL brut —
    l'entité n'existant plus en code, `DocumentType = 1` est utilisé
    directement — et `DROP TABLE IF EXISTS "RefacturingLines"`), en filet de
    sécurité idempotent pour toute copie/sauvegarde antérieure de la base qui
    contiendrait encore ces données ; sans effet sur la base de production
    actuelle, déjà nettoyée.
- **Effectifs avant/après le nettoyage** (vérifiés sur une copie de
  `filtres.db` avant d'agir sur la base réelle, puis confirmés identiques sur
  la base réelle) :
  - `OrderLine` : **461 → 316** (316 "Commande chmy" inchangées, 145
    "pour devis" supprimées).
  - `RefacturingLine` : **30 → 0**, table supprimée.
  - `OrderLinePeriodicFilter` : **0 → 0** (aucun rattachement existant pour
    ces 145 lignes).
  - Toutes les autres tables (`PeriodicFilter` 275, `FilterReplacement` 560,
    `OpacimetricFilter` 203, `OpacimetricReplacement` 238, `K7Location` 73,
    `K7Family` 8, `InventoryLine` 161) : **inchangées**.
- **Écran "Commande chmy" non affecté** : `OrderListViewModel`/`OrderView`
  n'existent plus que pour `OrderDocumentType.CommandeChmy`, sans aucune
  modification de leur code métier ; le rattachement filtre <-> commande, le
  calcul de besoin mars/septembre, l'export PDF et l'impression continuent de
  fonctionner normalement (vérifié après le nettoyage).
- **Irréversible** : contrairement à l'évolution précédente, cette suppression
  ne peut pas être annulée simplement en rajoutant une entrée de navigation :
  les données et le modèle ont été supprimés. Un réimport complet depuis le
  classeur Excel d'origine (si le fichier `.xlsm` est toujours disponible)
  resterait la seule façon de récupérer ces informations.

### Fusion Inventaire / Commande chmy

Les écrans **"Inventaire"** et **"Commande chmy"** affichent et modifient
désormais **les mêmes lignes** en base : une référence ajoutée depuis l'un des
deux écrans apparaît automatiquement dans l'autre (et réciproquement), et les
modifier/supprimer depuis l'un affecte l'autre — ce sont deux vues d'un seul
jeu de données, pas deux listes synchronisées séparément (choix fait pour
éviter la complexité et les bugs d'une synchro bidirectionnelle entre deux
tables distinctes, comme demandé).

- **Modèle unifié** : l'entité `InventoryLine` (feuille "inventaire" d'origine)
  a été fusionnée dans l'entité `OrderLine` (feuilles "Commande chmy"/"pour
  devis" d'origine). `OrderLine` porte maintenant un champ `Unite` (qui
  n'existait que sur `InventoryLine`) en plus de ses champs existants
  (`Designation`, `Dimension`, `Quantite`, `Notes`, rattachements filtre,
  besoin mars/septembre calculé). Les lignes "Inventaire" et "Commande chmy"
  sont donc désormais toutes des `OrderLine` de type
  `OrderDocumentType.CommandeChmy`.
- **Deux ViewModels, une seule source de données** : `InventoryListViewModel`
  ("Inventaire") et `OrderListViewModel` ("Commande chmy") exécutent tous les
  deux `App.Db.OrderLines.Where(l => l.DocumentType == OrderDocumentType.CommandeChmy)`
  et écrivent dans la même table. Seules les grilles diffèrent : "Inventaire"
  met en avant Désignation/Dimension/Quantité/Unité/Notes (colonnes de stock),
  "Commande chmy" met en avant Désignation/Dimension/Quantité + les colonnes
  de rattachement filtre et de besoin mars/septembre calculé.
- **Rafraîchissement à la navigation** (`IReloadable`) : comme les deux écrans
  gardent chacun leur propre ViewModel mis en cache
  (`NavigationItem.GetOrCreateViewModel`), une ligne ajoutée sur l'un ne serait
  pas visible sur l'autre tant que son ViewModel n'est pas rechargé.
  `MainViewModel.OnSelectedItemChanged` recharge donc désormais les données
  (`IReloadable.Reload()`) à **chaque sélection** de l'écran dans la barre
  latérale : changer d'onglet suffit à voir les ajouts faits depuis l'autre
  écran, sans synchronisation en temps réel ni bus d'événements.
- **Migration de données (une fois, idempotente)**, effectuée par
  `DbContextFactory.EnsureDatabaseCreated()` (même mécanisme que l'ajout de la
  table `OrderLinePeriodicFilters`, voir plus haut : l'application n'utilise
  pas `dotnet ef migrations`) :
  1. Ajoute à `OrderLines` les colonnes `Unite` (texte) et
     `MigratedFromInventoryLineId` (entier nullable, marqueur technique non
     affiché en UI) si elles n'existent pas encore (`ALTER TABLE ... ADD COLUMN`).
  2. Copie chaque ligne `InventoryLine` pas encore migrée (marqueur
     `MigratedFromInventoryLineId` absent) vers `OrderLines`
     (`DocumentType = CommandeChmy`), à la suite des lignes "Commande chmy"
     existantes (`Ordre` continu). La table `InventoryLines` d'origine
     **n'est ni vidée ni modifiée** : elle reste une copie de sauvegarde
     inerte, plus lue par l'interface mais toujours en base.
  - Effectifs avant/après migration sur la base réelle (vérifiés par un test
    de non-régression sur une copie, migration exécutée deux fois de suite
    pour confirmer l'idempotence — aucune ligne dupliquée au second passage) :
    - `InventoryLine` : **161 → 161** (inchangé, copie de sauvegarde conservée).
    - `OrderLine` : **300 → 461** (155 "Commande chmy" + 145 "pour devis" →
      316 "Commande chmy" [155 d'origine + 161 migrées] + 145 "pour devis"
      inchangées). Les 161 lignes migrées portent toutes un
      `MigratedFromInventoryLineId` renseigné.
    - Tous les autres effectifs (`PeriodicFilter` 275, `FilterReplacement` 560,
      `OpacimetricFilter` 203, `OpacimetricReplacement` 238, `K7Location` 73,
      `RefacturingLine` 30) : inchangés.
- **Réimport Excel complet** (non exécuté dans le cadre de cette évolution,
  voir consigne "ne pas relancer l'import") : `ExcelImportService.Import`
  a été adapté pour qu'un futur réimport reste cohérent avec la fusion :
  la feuille "inventaire" continue d'alimenter `InventoryLines` (copie de
  sauvegarde) **et** alimente désormais aussi `OrderLines` (type
  `CommandeChmy`, `Ordre` à la suite de ceux de la feuille "Commande chmy")
  via `ExcelImportService.MergeInventoryIntoOrderLines`.
- **Export PDF / impression** : `InventoryListViewModel` et
  `OrderListViewModel` construisent chacun leurs propres en-têtes/lignes à
  partir des mêmes propriétés `OrderLine` (aucune dépendance à
  `InventoryLine`) : l'export PDF et l'impression des deux écrans continuent
  de fonctionner sans changement de `PdfExportService`/`PrintService`.

### Familles K7 et migration

La feuille "Liste K7" du classeur Excel d'origine n'avait pas de notion de
famille distincte : le nom de la famille et sa périodicité de remplacement
étaient concaténés dans une ligne "titre" de la colonne LIEU (ex.
"AP  RDC periodicités  1/4/7/10", "Urgences  periodicités  1/4/7/10",
"LITS  PORTES periodicités  1/4/7/10"), qui précédait les lignes de détail
(lieux) de cette famille jusqu'à la ligne titre suivante — confirmé en
inspectant les 73 lignes `K7Location` déjà en base (8 lignes "titre"
identifiées).

- **Nouvelle entité `K7Family`** (`Nom`, `Periodicite` au même format
  "/1/4/7/10/" que `PeriodicFilter.Periodicity`), CRUD complet depuis l'écran
  "Liste K7" (panneau "Familles K7" à droite de la grille des lieux :
  Ajouter/Modifier/Supprimer).
- **`K7Location` rattaché à sa famille** via une clé étrangère nullable
  `K7FamilyId` (+ navigation `Family`), éditable depuis le formulaire
  d'ajout/modification d'un lieu (liste déroulante des familles existantes,
  nouveau type de champ générique `EditFieldType.Combo` ajouté à
  `EditField`/`DynamicEditWindow` pour cet usage).
- **Regroupement visuel** : l'écran "Liste K7" groupe la grille des lieux par
  famille (`CollectionViewSource` + `GroupDescriptions`/`GroupStyle` WPF sur
  `K7Location.FamilyGroupLabel`, propriété calculée "Nom (périodicités : ...)"),
  approche standard WPF cohérente avec le reste de l'application (pas de
  dépendance supplémentaire).
- **Suppression d'une famille encore utilisée : bloquée avec message clair**
  (plutôt que détacher silencieusement les lieux, jugé plus sûr pour ne pas
  perdre le classement sans que l'utilisateur s'en rende compte) : le bouton
  "Supprimer" du panneau Familles vérifie d'abord qu'aucun lieu n'y est
  rattaché et affiche sinon "La famille '...' est encore rattachée à N
  lieu(x). Réaffectez-les d'abord..." ; contrainte également posée en base
  (`OnDelete(DeleteBehavior.Restrict)` sur `K7Location.Family`) comme filet de
  sécurité.
- **Migration des données existantes** (`K7FamilyReconstructionService`,
  partagée entre `DbContextFactory.EnsureDatabaseCreated()` pour la base déjà
  en production et `ExcelImportService` pour un futur réimport complet),
  **une seule fois** (n'agit que si `K7Families` est vide, donc jamais après
  la première exécution, pour ne jamais écraser un reclassement manuel fait
  ensuite par l'utilisateur) :
  - Détecte par expression régulière les lignes "titre de famille" (motif
    `<nom> periodicité(s) <mois séparés par />`, insensible à la casse et aux
    espaces multiples du classeur d'origine), en extrait le nom et la liste de
    mois, et crée une `K7Family` pour chacune.
  - Rattache chaque ligne de détail suivante à la famille du dernier titre
    rencontré (`K7FamilyId`), jusqu'au titre suivant.
  - **Les lignes "titre" elles-mêmes ne sont pas supprimées** (aucune perte de
    donnée) : marquées `IsFamilyHeader = true` et simplement masquées de
    l'affichage détaillé de leur famille (évite un doublon visuel avec l'en-tête
    de groupe généré depuis `K7Family.Nom`), tout en restant consultables/
    modifiables en base si besoin.
  - Si une ligne de détail précède le tout premier titre rencontré (cas non
    observé sur les données réelles, où la première ligne est déjà un titre),
    elle est rattachée à une famille de repli "Non classé" plutôt que perdue.
  - **Résultat sur la base réelle** : 8 familles reconstituées à partir des 8
    lignes "titre" détectées parmi les 73 `K7Location` ; les 73 lignes ont
    toutes reçu un `K7FamilyId` (aucune "Non classé" nécessaire, la première
    ligne de la feuille étant bien un titre). Migration vérifiée idempotente
    (exécutée deux fois de suite sur une copie : effectifs identiques, aucune
    famille dupliquée).
  - **Limite connue, à documenter pour l'utilisateur** : cette reconstitution
    est heuristique (basée sur un motif texte). Deux lignes "titre" partagent
    parfois un nom très proche avec des périodicités différentes (ex.
    "Urgences periodicités 1/2/3/4/5/6/7/8/9/10/11/12" et "Urgences
    periodicités 1/4/7/10" dans les données réelles) : elles deviennent deux
    familles distinctes du même nom plutôt qu'une seule, ce qui reflète
    fidèlement la structure du classeur d'origine mais peut mériter d'être
    fusionné ou renommé manuellement ensuite (Modifier/Supprimer dans le
    panneau Familles) si l'utilisateur préfère un classement différent.
    `K7Location` : 73 lignes avant/après, inchangées (aucune suppression,
    seul le rattachement `K7FamilyId`/`IsFamilyHeader` a été ajouté).

### Import des archives 2014-2025

> Historique : l'outil d'import décrit ci-dessous a depuis été supprimé (voir « Import Excel
> (supprimé) »).

En plus du classeur "courant" (`Filtres 2026.xlsm`, importé intégralement via `ExcelImportService`,
voir plus haut), 12 classeurs d'archives annuels (2014 à 2025,
`Y:\DTPS-HYGIENE\Carnet sanitaire air\03.maintenance des installations\3b.Préventive\Archives
filtres\`) ont été importés le 25/09/2026, mais avec une logique **différente et volontairement plus
restrictive** que l'import principal, sur demande explicite de l'utilisateur :

- **Aucun nouveau filtre n'est jamais créé.** Seul l'historique de remplacement
  (`FilterReplacement` pour G4 plissé/G4 plan/G3/Charbon, `OpacimetricReplacement` pour F7-H13) est
  importé, et uniquement lorsqu'une ligne d'archive a pu être rattachée avec confiance à un
  `PeriodicFilter`/`OpacimetricFilter` **déjà existant** en base.
- **`ArchiveImportService`** (`src/FiltresApp.Core/Services/ArchiveImportService.cs`) implémente ce
  matching tolérant (réutilise `DimensionMatchService.ExtractDimensionToken`/`Normalize`, plus une
  normalisation d'emplacement tolérante accents/espaces/casse propre au service) : pour chaque ligne
  d'archive (catégorie + emplacement + dimension), on cherche les filtres existants de la même
  catégorie dont l'emplacement normalisé correspond exactement ; si plusieurs filtres partagent le
  même emplacement (fréquent sur G3/F7-H13, plusieurs filtres installés au même endroit), la
  dimension normalisée départage ; si le résultat reste ambigu ou qu'aucun filtre ne correspond, la
  ligne n'est **pas** importée et est consignée dans le rapport plutôt que devinée.
- **Déduplication systématique avant insertion** : un `FilterReplacement` n'est ajouté que s'il
  n'existe pas déjà de ligne pour ce filtre + ce mois + cette année précise ; un
  `OpacimetricReplacement` n'est ajouté que s'il n'existe pas déjà de ligne avec la même quantité
  changée et la même date pour ce filtre. Vérifié idempotent : relancer l'import une seconde fois sur
  la même base n'ajoute plus aucune ligne (testé sur une copie).
- **Variations structurelles entre années détectées et gérées automatiquement**, sans intervention
  manuelle fichier par fichier :
  - La feuille F7-H13 s'appelle "Filtres F7-H10" en 2014-2017 puis "Filtres F7 a H13" à partir de
    2018 : détectée par préfixe normalisé ("filtres f7") plutôt que nom exact.
  - La feuille "Liste K7" a 3 colonnes (LIEU / changement réalisé / Nb de filtres) en 2014-2018 puis 4
    colonnes (LIEU / n°porte / changement réalisé / Nb de filtres) à partir de 2019.
  - Le nombre de colonnes mensuelles varie d'une année à l'autre (colonnes "BESOIN..."
    supplémentaires, "COMPTEUR D'HEURES" inséré de façon irrégulière sur Charbon) : sans impact car
    les colonnes mensuelles "réalisé"/"date" sont détectées par le nom du mois dans l'en-tête
    (`ExcelImportService.NormalizeHeader`/`FindMonthInHeader`, rendues `internal` pour être réutilisées
    telles quelles par `ArchiveImportService`), pas par position fixe.
  - La structure en sous-sections de la feuille F7-H13/F7-H10 (titres "MAINTENANCE FILTRES ...",
    en-têtes répétées "NOM DE LA CENTRALE D'AIR", mini-table de stock "Dimensions.../Stock réel...",
    lignes "TOTAL POCHE F7") est identique à celle déjà documentée pour l'import principal (voir
    plus haut, section sur `ImportOpacimetricSheet`) et présente à l'identique dans les 12 archives :
    la même logique de détection/exclusion de ces lignes non-données est reprise dans
    `ArchiveImportService.ImportOpacimetricArchiveSheet`.
- **Limite connue sur la feuille "Liste K7"** : le modèle `K7Location` ne porte qu'une seule date
  `ChangementRealise` (pas de table d'historique par année comme pour les autres feuilles). Importer
  un historique multi-année pour cette feuille aurait nécessité soit d'écraser cette date à chaque
  fichier traité (interdit par la consigne "ne jamais écraser"), soit d'ajouter une nouvelle table
  d'historique (hors périmètre demandé). `ArchiveImportService` se contente donc de compter les
  correspondances d'emplacement trouvées sur cette feuille à titre informatif (colonne "K7 OK/KO" de
  la sortie console de l'outil), sans écrire aucune donnée.
- **Rapport des lignes non rattachées** : `import-archives-non-rattaches.txt`, à la racine du projet,
  groupé par année puis par fichier, avec catégorie/emplacement/dimension exacts et la raison (aucun
  filtre existant à cet emplacement, ou emplacement ambigu entre plusieurs filtres existants). Sur les
  12 fichiers, la plupart des lignes non rattachées concernent les années les plus anciennes
  (emplacements renommés/supprimés depuis, filtres avec un nom légèrement différent du filtre actuel,
  etc.) : logique, le taux de correspondance augmente d'année en année à mesure qu'on se rapproche de
  2026 (ex. G4/G3/Charbon : 145/214 emplacements rattachés en 2014 contre 263/266 en 2025).
- **Pour relancer cet import** (ex. si de nouvelles années d'archives sont retrouvées, ou après avoir
  corrigé manuellement des filtres pour améliorer le taux de rattachement) :
  ```powershell
  $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
  dotnet run --project src\FiltresApp.ImportCli\FiltresApp.ImportCli.csproj -- archive `
    "chemin\vers\filtres.db" "chemin\vers\rapport.txt" `
    "chemin\vers\Archive1.xlsm" "chemin\vers\Archive2.xlsm" ...
  ```
  L'année de chaque fichier est déduite automatiquement de son nom (motif `20\d{2}`, tolérant aux
  variations d'espacement/casse des noms de fichiers réels, ex. "FILTRES  2014.xlsm"). Cette
  sous-commande n'écrase jamais rien : elle peut être relancée sans risque (idempotente, voir
  ci-dessus), y compris avec les mêmes fichiers déjà traités.
- **Sauvegarde avant écriture réelle** : `filtres.db.bak-before-archives-import` (à côté de
  `filtres.db`) contient une copie de la base juste avant cet import, conservée par précaution.
- **Effectifs avant/après** (aucun nouveau `PeriodicFilter`/`OpacimetricFilter`, uniquement de
  l'historique ajouté) : `PeriodicFilter` 275 → 275, `OpacimetricFilter` 203 → 203,
  `FilterReplacement` 560 → 7522 (+6962), `OpacimetricReplacement` 238 → 267 (+29). Le très faible
  nombre de nouveaux `OpacimetricReplacement` par rapport à `FilterReplacement` s'explique par le fait
  que la feuille F7-H13 du classeur 2026 (source de l'import principal) conservait déjà, dans ses
  colonnes "Qté changée"/"Date du chgment" répétées, une bonne partie de l'historique des années
  précédentes : la déduplication (qté + date identiques) a donc évité de recréer en double des lignes
  déjà présentes, ce qui est le comportement attendu.
- **Sélecteur d'année** : les écrans de suivi affichent désormais 2014 à 2027 dans le sélecteur
  d'année de la barre latérale (`YearContext` construit dynamiquement la liste à partir des années
  distinctes présentes dans `FilterReplacements`, voir section "Sélecteur d'année..." plus haut :
  aucune modification de code n'a été nécessaire pour que les nouvelles années apparaissent).

### Fenêtre "Consulter l'historique..." (clic droit, lecture seule)

Sur les 4 catégories à périodicité mensuelle (G4 plissé, G4 plan, G3, Charbon,
écran partagé `PeriodicFilterView`) et sur "Filtres F7 à H13"
(`OpacimetricFilterView`), un **clic droit sur une ligne du tableau** ouvre un
menu contextuel avec une option **"Consulter l'historique..."**, visible sur
**toutes** les catégories (contrairement à l'option "Changé tous les 15
jours" ci-dessus, restreinte à G4 plissé) : elle ouvre une fenêtre de
**lecture seule** (aucune édition possible, pour ne jamais créer une deuxième
voie d'écriture concurrente à la case à cocher "Réalisé" de la grille
principale) affichant l'historique complet d'un seul filtre.

- **En-tête** : nom/emplacement (libellé de colonne adapté à la catégorie,
  ex. "Nom de la centrale d'air" pour G4 plissé/F7-H13, "Emplacement" pour les
  autres) et dimension du filtre consulté.
- **Sélecteur d'année** (`ComboBox`) : liste uniquement les années où **ce
  filtre précis** a de l'historique en base (`FilterReplacement.Year`/
  `OpacimetricReplacement.DateChanged.Year` distincts pour ce
  `PeriodicFilterId`/`OpacimetricFilterId`), pas une liste fixe 2014-2026
  codée en dur : un filtre récent n'affiche que ses années réelles. Si le
  filtre n'a aucun historique, un message "Aucun historique disponible..."
  remplace le sélecteur/la grille plutôt que de les afficher vides. Par
  défaut, l'année sélectionnée à l'ouverture est celle de l'année consultée
  globale (`YearContext.Year`) si elle fait partie des années disponibles,
  sinon la plus récente.
- **G4 plissé / G4 plan / G3 / Charbon** (`FilterHistoryWindow`) : pour
  l'année choisie, les 12 mois (Janvier à Décembre, réutilise
  `PeriodicFilterListViewModel.MonthLabels`) avec, pour chacun, s'il a été
  réalisé et à quelle date (mois vide/"-" si aucune ligne `FilterReplacement`
  pour ce mois/cette année).
- **Filtres F7 à H13** (`OpacimetricHistoryWindow`) : cette feuille n'a pas de
  notion de mois fixe (voir plus haut, historique de remplacements ponctuels),
  donc pas de grille à 12 mois : liste chronologique des remplacements
  (`OpacimetricReplacement`) datés de l'année choisie, avec quantité changée
  et date. `OpacimetricFilterView` n'avait pas encore de menu contextuel : un
  menu minimal, ne contenant que cette option, a été ajouté sur la grille.
- **Implémentation** : `PeriodicFilterListViewModel.ShowHistory` /
  `OpacimetricFilterListViewModel.ShowHistory` chargent l'historique complet
  du filtre sélectionné depuis `App.Db` (`AsNoTracking`, pas de filtrage par
  année à la requête : c'est la fenêtre elle-même qui restreint l'affichage à
  l'année choisie dans son `ComboBox`, pour permettre de changer d'année sans
  recharger depuis la base) puis délèguent l'ouverture de la fenêtre à
  `IDialogService.ShowFilterHistory`/`ShowOpacimetricHistory`
  (`Services/DialogService.cs`), suivant le même pattern que
  `PickFilterLinks`/`FilterLinkWindow` déjà en place pour le rattachement
  filtre <-> commande. Commande exposée par ligne de grille
  (`PeriodicFilterRowViewModel.ShowHistoryCommand` /
  `OpacimetricFilterRowViewModel.ShowHistoryCommand`, `[RelayCommand]`), liée
  au `MenuItem` du `ContextMenu` (dont le `DataContext` est hérité de la ligne
  cliquée, comme pour "Changé tous les 15 jours").
- **Aucune migration de schéma nécessaire** : la fenêtre ne fait que lire les
  tables `FilterReplacement`/`OpacimetricReplacement` déjà existantes, sans
  ajouter de colonne ni de table.
- **Fichiers ajoutés** : `src/FiltresApp/Views/Dialogs/FilterHistoryWindow.xaml`
  (+ `.xaml.cs`), `src/FiltresApp/Views/Dialogs/OpacimetricHistoryWindow.xaml`
  (+ `.xaml.cs`).

### Immutabilité de l'historique passé lors d'un changement de périodicité

Vérification demandée : si un utilisateur modifie la `Periodicity` d'un
`PeriodicFilter` (ex. `/1/3/5/7/9/11/` → `/1/4/7/10/`), les lignes
`FilterReplacement` déjà enregistrées pour les mois/années passés ne doivent
**jamais** être recalculées ni modifiées — seule `NextDueDate` (calcul
prospectif, affiché dans la grille, non stocké) doit refléter la nouvelle
périodicité à partir de maintenant.

- **Déjà correct nativement, aucun changement de code nécessaire.**
  `MaintenanceScheduleService.GetNextDueDate` (et `GetLastReplacementDate`)
  ne fait que **lire** `filter.Replacements` (la liste des `FilterReplacement`
  déjà en base) pour déterminer le dernier changement réalisé, puis calcule
  la prochaine échéance en itérant sur les mois futurs jusqu'à trouver un mois
  présent dans la nouvelle `Periodicity` : à aucun moment il n'écrit, ne crée
  ni ne supprime de `FilterReplacement`. C'était prévisible vu que le README
  documentait déjà "Colonnes prévu non stockées : recalculées à l'affichage"
  et que `FilterReplacement` porte un couple `Month`+`Year` explicite,
  indépendant de la `Periodicity` du filtre parent (voir "Modèle de données du
  suivi mensuel" plus haut) : changer la périodicité ne touche qu'une colonne
  de `PeriodicFilter`, jamais les lignes enfants `FilterReplacement`.
- **Test concret effectué** (sur une **copie** de `filtres.db`, jamais sur
  l'originale ; scripts C# jetables non conservés dans le dépôt) :
  1. Copie de `src\FiltresApp\data\filtres.db` vers un fichier temporaire.
  2. Chargement du filtre G4 plissé avec le plus d'historique (Id=96,
     "CALMETTE 2 CTA Soufflage Obstétrique", 138 lignes `FilterReplacement`,
     `Periodicity` initiale `/1/2/3/4/5/6/7/8/9/10/11/12/`,
     `NextDueDate` calculé = **01/03/2026**).
  3. Modification de `Periodicity` en base vers `/1/4/7/10/` (`SaveChanges`),
     dans un contexte EF Core séparé du contexte de lecture.
  4. Rechargement dans un **nouveau** `DbContext` (simule un nouvel écran) :
     les **138** lignes `FilterReplacement` de ce filtre sont **identiques**
     avant/après (mêmes `Id`, `Month`, `Year`, `QuantityDone`, `DateDone` —
     échantillon vérifié ligne à ligne, ex. `Id=806 Month=1 Year=2014
     DateDone=06/01/2014` inchangé), et les effectifs globaux de la copie
     sont inchangés (`PeriodicFilter` 275, `FilterReplacement` 7522,
     `OpacimetricFilter` 203, `OpacimetricReplacement` 267). Seul
     `NextDueDate` a changé, passant de **01/03/2026** à **01/04/2026**,
     conformément à la nouvelle périodicité `/1/4/7/10/` — confirmant que
     seul le calcul prospectif est affecté.
  5. Les effectifs de la base réelle (`src\FiltresApp\data\filtres.db`),
     vérifiés en lecture seule avant et après l'ensemble de ces
     manipulations, sont restés inchangés (voir section suivante).
- **Conclusion** : aucune correction de code n'était nécessaire ; le
  comportement voulu (historique figé, échéance prospective seule affectée)
  est garanti par la conception déjà en place (`FilterReplacement` en table
  enfant à faits explicites, jamais recalculée).

### Lignes et colonnes de grille clairement visibles (écrans et impressions)

Le style global des `DataGrid` (`Styles/Controls.xaml`, appliqué automatiquement à tous les tableaux de
toutes les catégories/écrans sans style ad-hoc par vue) utilise désormais `GridLinesVisibility="All"`
avec `HorizontalGridLinesBrush`/`VerticalGridLinesBrush` et la bordure des en-têtes de colonnes réglées
sur une nouvelle couleur dédiée `BrushGridLine` (`Styles/Colors.xaml`, gris moyen `#9CA3AF`, plus marqué
que `BrushBorder` `#E5E7EB` déjà utilisé pour les contours de cartes/champs, afin de rester lisible sans
être agressif) : les bordures de cellules horizontales **et** verticales sont donc nettes sur tous les
écrans (filtres G4 plissés/plan, G3, Charbon, F7-H13, Liste K7, Inventaire/Commande chmy), sans qu'aucune
vue individuelle n'ait eu besoin d'être modifiée (aucune n'écrasait le style global). Les documents
imprimés (`PrintService`, impression générale) reprennent la
même couleur de bordure sur chaque cellule (bordure complète 0.75px au lieu du simple soulignement bas
précédent).

## Pistes d'amélioration

- Ajouter des migrations EF Core si le schéma doit évoluer après mise en
  production.
- Ajouter des tests automatisés (actuellement aucun projet de test).
- Rendre le reste des grilles éditable en ligne (l'édition passe encore par
  des boîtes de dialogue, sauf pour la case à cocher "Réalisé"/date du mois
  consulté sur les 4 catégories à périodicité mensuelle, éditable directement
  dans la grille) pour un usage plus proche du tableur d'origine.
- Ajouter un filtre par mois également sur la feuille Charbon dans le compteur
  d'heures (actuellement un seul compteur global par filtre, au lieu d'un
  historique par mois comme dans la feuille Excel d'origine).
- Réduire la taille de l'exécutable publié (~190 Mo, du fait du mode
  self-contained + composants EF Core/SQLite/ClosedXML/QuestPDF) via
  `PublishTrimmed` si compatibilité confirmée avec toutes les dépendances.
- Familles K7 : fusionner/renommer manuellement les familles issues de la
  reconstitution automatique si besoin (voir "Familles K7 et migration"), en
  particulier les cas où deux lignes "titre" du classeur d'origine partagent
  un nom très proche avec des périodicités différentes.
- "Pour devis" / "Filtres à refacturer" : supprimés définitivement (interface,
  code et données), voir "Suppression définitive de « pour devis » et
  « filtres à refacturer »". Si ces écrans devaient réapparaître un jour, il
  faudrait recréer le modèle/l'écran et réimporter les données depuis le
  classeur Excel d'origine (elles ne sont plus en base).
- Import des archives 2014-2025 : vérifier manuellement `import-archives-non-rattaches.txt` pour les
  801 lignes non rattachées (voir section "Import des archives 2014-2025"), en particulier sur les
  années les plus anciennes, et éventuellement corriger le nom/emplacement de filtres actuels pour
  permettre un rattachement rétroactif si l'historique manquant s'avère utile. Ajouter une table
  d'historique par année pour `K7Location` si un suivi multi-année devient nécessaire sur cette
  feuille (actuellement une seule date, voir limite documentée).
