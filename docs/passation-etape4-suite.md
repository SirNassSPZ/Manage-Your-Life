# Passation — finir l'Étape 4 (app Windows), suite

> Prend la suite de `docs/passation-etape4-windows.md`, qui reste valable pour tout ce qui touche
> à la spec, aux 18 règles et à l'ordre de construction. Ce document ne décrit que **ce qui a
> changé depuis** et **ce qu'il reste à faire**.
>
> État de départ : branche `claude/new-session-j5hrxg`, commit **`b2a4fea`**, poussée.
> **507 tests verts** (`dotnet test` à la racine) · coquille WinUI compilée sans avertissement.

## 0. À lire avant d'écrire une ligne

1. **`CLAUDE.md`** — mission, 18 règles, périmètre V1 verrouillé, points d'arrêt.
2. **`docs/passation-etape4-windows.md`** — le brief d'origine, toujours la référence.
3. **`docs/specification.md`** — la spec fait foi sur le code. §4 (notifications locales), §5.4, §5.5, §5.6, §12, §14.
4. **`docs/decisions.md`** — **D-019 à D-022** sont récentes et structurantes ; **Q-002** est tranchée.
5. **`docs/idees.md`** — **I-004** et **I-005** : demandes de l'utilisateur triées contre la spec, consignées, **non codées**.
6. **Les skills** — vérifier ceux réellement disponibles dans la session et charger le plus adapté. `impeccable` demande de charger `reference/craft-floor.md` **juste avant d'éditer de l'UI** ; c'est facile à oublier.

## 1. Ce qui a changé dans la structure — D-022

L'app Windows est passée de **trois projets à quatre**. C'est le point le plus important à intégrer :

| Projet | Cible | Rôle |
|---|---|---|
| `DeuxiemeCerveau.App` | `net8.0` | Cœur applicatif : base locale, outbox, synchro §6, lecture, export/import |
| **`DeuxiemeCerveau.Presentation`** | `net8.0` | **Nouveau.** Modèles de vue, `Format`, `Composition`, planificateur de rappels |
| `DeuxiemeCerveau.Windows` | `net8.0-windows` | Uniquement ce qui **exige** Windows : XAML, convertisseurs, MSAL, HWND, toasts |
| `DeuxiemeCerveau.Presentation.Tests` | `net8.0` | 117 tests, sur la CI Linux |

**Règle de placement, vérifiable :** si un `using Microsoft.UI` apparaît dans `Presentation`, le code est au mauvais endroit. Inversement, toute logique qui apparaît dans `Windows` doit descendre d'un cran — le job CI `coquille` ne fait que **compiler**, il ne teste rien, et c'est délibéré.

`Composition.Creer(options, jetons, dossierDonnees?, manipulateurApi?)` **reçoit** sa configuration, son fournisseur de jetons et sa pile HTTP. Lire `appsettings.json` et parler à MSAL sont des affaires d'hôte. C'est ce qui rend le graphe montable dans un test — voir `FabriquePresentation`.

## 2. Outils déjà en place

L'exécutable porte des modes qui rendent la vérification visuelle possible :

```
--vue <Zone>              ouvre une zone (Aujourdhui, Calendrier, Finances, Notes, Corbeille…)
--mode <Mois|SeptJours|Gestion>   sous-vue de la zone Calendrier
--capture-delai <ms>      attendre avant de photographier (réveil serverless : jusqu'à ~61 s)
--capture <chemin.png>    photographie la fenêtre et quitte
--parite                  déroule les scénarios §12 contre le serveur déployé, puis quitte
```

`--capture` passe par `RenderTargetBitmap` : ni `CopyFromScreen` ni `PrintWindow` ne savent lire ce que WinUI compose via DirectComposition.

`--parite` monte des postes **jetables** (dossier temporaire détruit à la fin) : les données de l'utilisateur ne sont ni lues ni écrites.

## 3. État des vues

| Vue | État |
|---|---|
| Onboarding 3 gestes | ✅ |
| Aujourd'hui | ✅ (stat « mois rouge » depuis la projection serveur, en tâche de fond) |
| Calendrier — grille du mois | ✅ |
| Calendrier — 7 prochains jours | ✅ |
| Calendrier — gérer les calendriers | ✅ (créer, renommer, recolorier, corbeille) |
| Finances — vue d'ensemble | ✅ (confirmation payé/reçu, D-017) |
| Budget projeté | ✅ |
| Notes | ✅ |
| Corbeille | ✅ (restauration 1 geste, purge en 2 temps) |

Scénarios de parité §12 : **les trois passent** contre l'API déployée (`--parite`).

## 4. Ce qu'il reste — par ordre de valeur

### 4.1 Notifications locales — remise du toast (commencé, à finir)

**Fait :** `DeuxiemeCerveau.Presentation/PlanificateurRappels.cs` décide **quoi** notifier et **quand**, avec 7 tests. Il expose :
- `Echeances(composition, maintenant)` → les échéances de **demain** (la veille, pas le jour même)
- `Digest(composition, maintenant)` → le point du dimanche (D-018 #4), `null` si la semaine est vide
- `EstJourDuDigest(maintenant)`

**Reste à faire, dans `DeuxiemeCerveau.Windows` :**
1. Un `ServiceToasts` qui remet un `Rappel` via `AppNotificationManager` (Windows App SDK). Attention : l'app est **non empaquetée** (`WindowsPackageType=None`) — les notifications non empaquetées exigent d'enregistrer l'application (`AppNotificationManager.Default.Register()`), et l'icône/AUMID demande un peu de soin.
2. Les modes sans interface **`--rappels`** et **`--digest`** (constantes déjà déclarées : `PlanificateurRappels.OptionRappels` / `OptionDigest`). Ils doivent, comme `--parite`, s'exécuter **sans créer de fenêtre** puis `Environment.Exit`. **Les ajouter à `Programme.ModesOutil`**, sinon ils seront redirigés vers l'instance déjà ouverte et ne feront rien.
3. Une **tâche planifiée Windows** pour les déclencher (quotidienne pour `--rappels`, hebdomadaire pour `--digest`).
4. La **déduplication** : `Rappel.Cle` porte le jour. Il faut la persister quelque part (un fichier dans `Composition.DossierParDefaut` suffit) pour ne pas re-notifier au second déclenchement du jour.
5. Le **rappel mensuel d'export** (§5.7) n'est pas planifié du tout.

### 4.2 Groupement pliable par catégorie (demandé, non commencé)

Troisième point de la demande consignée en **I-004**, dans le périmètre V1 : grouper les listes de Finances et du Calendrier par catégorie, en colonnes pliables. C'est de la **mise en forme sur des données déjà là** — aucun champ, aucune entité.

### 4.3 Étape 4 — point d'arrêt

Quand 4.1 est fini : **s'arrêter et demander une validation humaine** avant l'Étape 5 (app Apple). Ne pas enchaîner.

## 5. Pièges rencontrés — ne pas les repayer

- **`x:Bind` exige que le modèle existe AVANT `InitializeComponent()`.** Poser la propriété d'abord, appeler `InitializeComponent()` ensuite.
- **`ItemsControl` ne prend pas en charge `x:Bind`** dans son `ItemTemplate` : les liaisons compilées y restent nulles, **en silence**. Utiliser `ItemsRepeater`, ou `{Binding}`.
- **Une ressource déclarée dans un `UserControl` est introuvable depuis un autre.** L'erreur ne sort qu'à l'exécution, au chargement du XAML. Tout ce qui est partagé va dans `Ressources/Jetons.xaml`.
- **Une propriété `static` n'est pas liable par `x:Bind`** — exposer un accès d'instance.
- **`Enum.TryParse<Presentation.VueModeles.Zone>` est ambigu** depuis le namespace `DeuxiemeCerveau.Windows` : qualifier complètement.
- **Un processus de l'app resté ouvert verrouille les DLL** et fait échouer la compilation suivante. `Get-Process DeuxiemeCerveau.Windows | Stop-Process`.
- **Les captures à délai court photographient l'appel réseau en vol.** Le réveil du SQL serverless prend jusqu'à ~61 s : utiliser `--capture-delai` généreusement avant de conclure qu'un écran est vide.
- **Ne jamais faire `git add -A` sur `.github`** : l'outillage y dépose des copies de skills. Elles sont désormais gitignorées, mais rester attentif.
- **Les apostrophes dans un here-string PowerShell `@'…'@` ne se doublent pas.** Les doubler laisse `l''app` dans le message de commit.

## 6. Décisions récentes à connaître

- **D-019** — `Main` écrit à la main. Justifié par la **seule** instance unique (implémentée et vérifiée) : deux processus sur la même base SQLite menacent les filets 1 et 2. Les modes outil sont exclus de la redirection.
- **D-020** — la coquille vit hors de la solution ; un job CI `windows-latest` la compile. Ce job **n'existait pas** avant, il existe maintenant.
- **D-021** — `Encre3` s'écarte de la maquette (`#6E6862` au lieu de `#9A938C`) pour tenir le seuil WCAG AA. **L'app Apple doit reprendre les mêmes valeurs.**
- **D-022** — extraction de la couche de présentation (§1 ci-dessus).
- **Q-002** — une envie d'achat **ne porte pas de montant** (§3.1). La maquette et I-003 ont été corrigés.

## 7. Périmètre — ce qui a été demandé mais NE DOIT PAS être codé

Consigné dans `docs/idees.md`, en attente d'une décision de périmètre :

- **I-004** — onglet to-do list, « listes » créables et catégorisables, planifications. Le §13 n'inclut pas la tâche dans la saisie V1 ; §5.2 et §5.3 les placent en **V2**.
- **I-005** — favoris sur les notes (aucun champ `favori` dans le §3.1 ; une catégorie « Favoris » ferait le travail sans rien ajouter), et **mise en forme riche + images** dans les notes, qui contredit le §5.5 mot pour mot.

L'utilisateur a choisi de **livrer la V1 d'abord**. Ne pas rouvrir sans décision explicite.
