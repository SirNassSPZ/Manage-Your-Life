# Passation — finir l'Étape 4 (app Windows) depuis Claude Code en local

> Ce document est le brief d'exécution pour terminer l'**Étape 4** (app Windows) depuis **Claude Code
> lancé dans un terminal sur une machine Windows**. Le cœur applicatif est déjà fait et testé sur Linux ;
> il ne reste que la **coquille WinUI** (qui ne se compile que sous Windows, décision **D-014**) et les
> **scénarios de parité §12**.

## 0. À lire AVANT d'écrire une ligne (obligatoire)

1. **`CLAUDE.md`** (racine) — mission, 18 règles, ordre de construction, périmètre V1 verrouillé.
2. **`docs/specification.md`** — LA spec fait foi. Lire surtout : §3 (modèle), §4 (répartition serveur/client + isolation du cœur), §5 (modules), **§6 (synchro — mot pour mot)**, §7 (pièces jointes), §9 (schéma/migrations), §12 (scénarios de parité), §14 (les 18 règles).
3. **`docs/decisions.md`** — surtout **D-014** (structure app), **D-015/D-016** (client synchro + pièces jointes), **D-017** (confirmation payé/reçu : ponctuels en V1, récurrents = recalage), **D-018** (4 ajouts V1 : presets, suggestion de catégorie, saisie rapide, digest hebdo).
4. **`docs/idees.md`** — hors périmètre : **I-001 projets = V2**, I-002 Gmail = hors sujet, **I-003 envie↔budget = V2**.
5. **`docs/maquette.html`** — **cible visuelle** (ouvrir dans un navigateur). Ton, disposition, nav en haut, sous-vues à gauche.
6. **Skills — vérifie et choisis le meilleur À CHAQUE tâche.** Il existe des skills pour **quantité de domaines, pas seulement le design** : interface, revue de code, tests, documents (Word/Excel/PDF/présentations), visualisation de données, sécurité, lancement de l'app, git, configuration… et la liste **change** d'une session à l'autre — ne te fie **jamais** à des noms écrits en dur. **Pour chaque tâche, quelle qu'elle soit**, regarde les skills réellement disponibles dans ta session et charge le(s) plus adapté(s) au sujet du moment ; s'il en existe un qui couvre ce que tu fais, utilise-le ; sinon, avance sans. Les **garde-fous du projet se chargent automatiquement** — les respecter à la lettre.

## 1. Où on en est

- **Cette machine Windows** peut compiler et **exécuter** WinUI (ce que l'environnement cloud Linux ne pouvait pas — d'où cette passation).
- **Fait et testé — 383 tests verts** (`dotnet test` à la racine) :
  - `/core` (cœur métier) + `/api` (déployée sur Azure) ;
  - **`/apps/windows/DeuxiemeCerveau.App`** — le **cœur applicatif cross-plateforme** (net8.0, sans WinUI) : base locale SQLite + migrations `/core`, outbox, **synchro §6.2** (`MoteurSynchro`), saisie locale-d'abord, lecture, calendrier, projection (lue du serveur), export/import ZIP, pièces jointes ;
  - **Palier 1, rangs 1-4** (moteurs d'UX, testés) : `ServiceAujourdhui`, `ServiceDemarrage` + `CatalogueDepart`, `SaisieRapide` + `SuggestionCategorie`, `ServiceConfirmation`.
- **Reste (cette passation)** : la coquille **`/apps/windows/DeuxiemeCerveau.Windows`** (WinUI) + les **services plateforme** + les **scénarios de parité §12** + le **point d'arrêt** de fin d'Étape 4.

## 2. Règle d'or — NON NÉGOCIABLE (garde-fou-architecture)

- **La coquille WinUI AFFICHE et SAISIT, rien d'autre.** Toute la logique vit **déjà** dans `DeuxiemeCerveau.App`. **Ne réimplémente RIEN** : pas de calcul de budget (il est **serveur**, §4), pas de logique de synchro (`MoteurSynchro` existe). Les vues appellent les services du cœur via des **ViewModels (MVVM)**.
- **Local-first (§6.1, filet 1)** : la saisie écrit **en local d'abord** (`ServiceSaisie`) et rend la main ; le réseau vient **en tâche de fond**. **Jamais** d'écriture serveur à la saisie.
- **Secrets (règle 16)** : URL de l'API + `ClientId`/`Tenant` Entra en **config** (`appsettings.json` + user-secrets en local), **jamais en dur, jamais dans git**. MSAL gère les jetons.
- **Périmètre V1 verrouillé** : **pas** de Projets, **pas** d'Enveloppes, **pas** de confrontation envie↔budget (tout ça = V2). La maquette les montre **étiquetés V2** — les afficher désactivés, ne pas les coder.
- **Français partout** : vocabulaire métier, commentaires, messages de commit.

## 3. Étape 4f — la coquille WinUI

### 3.1 Squelette + composition (DI)

`DeuxiemeCerveau.Windows` (net8.0-windows, **WinUI 3 / Windows App SDK**) existe déjà en partie (créé en 4a) — le compléter. Au démarrage, **composer** (injection de dépendances) :

- **`BaseLocale`** sur un fichier SQLite, ex. `%LOCALAPPDATA%\DeuxiemeCerveau\local.db` (les **migrations `/core` s'appliquent au démarrage**, dialecte `Sqlite`, via `CibleMigrationLocale` — **jamais** de schéma écrit à la main, D-008).
- `IdentiteAppareil`, `ServiceSaisie`, `ServiceLecture`, `ServiceCalendrier`, `ServicePurge`, `ServiceAujourdhui`, `ServiceDemarrage`, `ServiceConfirmation`, `ServicePiecesJointes`, `MoteurSynchro`.
- **`IClientApi` = `ClientApiHttp`** branché sur un `HttpClient` (`BaseAddress` = URL API en config ; en-tête `Authorization: Bearer <jeton MSAL>`).
- Adaptateurs fichiers (règle 4) : `IStockageFichiersLocal` = **`StockageFichiersDisque`** (cache local des pièces jointes), `ITransfertBlob` = **`TransfertBlobHttp`**.
- **Navigation** : **barre du haut** (Aujourd'hui · Calendrier · Finances · Budget projeté · Notes · Corbeille) + **barre latérale contextuelle** (sous-vues) — comme `docs/maquette.html`.

### 3.2 Vues — dans cet ordre, chacune branchée sur son service (aucune logique dans la vue)

| # | Vue | Service(s) du cœur à appeler | Notes |
|---|---|---|---|
| 1 | **Accueil « Aujourd'hui »** | `ServiceAujourdhui.SoldeDeReference()`, `.Aujourdhui(maintenant)`, `.ProchainsJours(maintenant, 7)` | Ouvrir sur le calme (solde + agenda), pas sur la dette. |
| 2 | **Onboarding 3 gestes** (1er lancement si `ServiceDemarrage.OnboardingRequis()`) | `ServiceDemarrage.DefinirSoldeReference(...)`, `.Modeles` (`CatalogueDepart`) | Poser le solde → 1-2 revenus → charges depuis les presets. |
| 3 | **Saisie d'un Élément** (+ saisie rapide) | `ServiceSaisie.Enregistrer`, `SaisieRapide.Composer(...)`, `SuggestionCategorie.Suggerer(...)` | Suggestion **facultative** ; saisie rapide **segmentée** (pas de langage naturel). |
| 4 | **Finances** (Vue d'ensemble + Par catégorie) | `ServiceLecture`, projection serveur (`IClientApi.Projeter`) pour le hero « il te restera » ; **confirmation** : `ServiceConfirmation.Confirmer` / `.ConfirmerLot` / `.AConfirmer` | La confirmation **refuse les récurrents** (D-017) — message clair vers le recalage. |
| 5 | **Calendrier** mensuel | `ServiceCalendrier.Occurrences(debut, fin, categoriesVisibles)` | RRULE développées **pour l'affichage seulement** (§4) + filtres de catégories. |
| 6 | **Budget projeté** | `IClientApi.Projeter(mois)` | Calcul **serveur** (§5.1) ; **mettre en évidence** les mois à clôture négative. |
| 7 | **Notes** / **Corbeille** | `ServiceLecture.Notes()` / `.Corbeille()`, `ServiceSaisie.Restaurer`, `ServicePurge` | Corbeille = restauration en un geste ; purge = confirmation explicite (§5.6). |

### 3.3 Services plateforme (les adaptateurs Windows à écrire)

- **Notifications locales** — toasts Windows (Windows App SDK / `CommunityToolkit.WinUI.Notifications`), **planifiées à partir des données synchronisées** (§4) : rappel la veille d'une échéance (avec action « marquer payé ») + **digest hebdo** (D-018). Chaque appareil notifie (pas de push serveur — V3).
- **Sélecteurs de fichiers** — `FileSavePicker` (export ZIP via `ServiceExport`), `FileOpenPicker` (import via `ServiceImport` ; pièces jointes via `ServicePiecesJointes.Joindre`).
- **Auth Entra ID (MSAL.NET)** — `PublicClientApplication`, `AcquireTokenSilent`/`Interactive`, scope de l'API ; injecter le **bearer** dans le `HttpClient` de `ClientApiHttp`. **Partie la plus délicate — l'intégrer tôt, pas à la fin.**
- **Synchro de fond** — appeler `MoteurSynchro.Synchroniser(nomAppareil, "windows")` **périodiquement** et **au retour du réseau** ; l'UI **n'attend jamais** le serveur (local-first). Prévoir un retry (reprise SQL serverless = quelques secondes au 1er appel après pause).

## 4. Étape 4g — parité solo (§12), puis POINT D'ARRÊT

Dérouler **sur l'app réelle**, un seul appareil :

1. **Saisie hors-ligne puis reconnexion** → tout remonte, **aucun doublon**.
2. **Coupure réseau en plein push** → au rétablissement, **même état final**, aucun doublon (idempotence par `change_id`).
3. **Export réseau coupé, puis import sur installation vierge** → **contenu identique**, corbeille comprise.

Quand les trois passent : **s'arrêter et demander une validation humaine** (fin d'Étape 4) **avant** l'Étape 5 (app Apple). Ne pas enchaîner sur l'Apple sans ce feu vert (CLAUDE.md, points d'arrêt).

## 5. Boucle de travail

- **À chaque tâche : vérifie les skills disponibles et convoque le meilleur** (cf. §0.6) — la liste évolue, ne code pas « au petit bonheur » si un skill adapté existe (design/UI, revue, etc.).
- Développer sur la branche de dev en cours (`claude/new-session-j5hrxg`) — `main` et elle sont à jour au même commit.
- **Construire et LANCER l'app à chaque vue** (tu as Windows) — vérifier le rendu contre `docs/maquette.html`.
- `dotnet test` à la racine doit **rester vert** (le cœur ne régresse pas).
- Commits **français**, descriptifs, au fil de l'eau. Pousser régulièrement.
- **Toute question non tranchée par la spec** → la consigner dans `docs/decisions.md` et demander validation. **Ne pas inventer, ne pas élargir la V1.**

## 6. Pièges connus (rappel spec)

- Palier gratuit App Service **interdit** — l'API vit sur Functions (déjà fait) ; côté client, prévoir un **retry** au réveil SQL serverless.
- **Ne jamais stocker les données dérivées** — projection et suivi budgétaire se calculent **à la lecture** (règle 9). La vue Budget lit `IClientApi.Projeter`, elle ne recalcule rien.
- **Argent en centimes entiers**, jamais de flottants (règle 5). Récurrences en **RRULE** développées dans le **fuseau de l'Élément** (règle 6).
- **Rien n'est effacé, seulement marqué `supprime`** (filet 2) ; seule exception : la purge manuelle depuis la corbeille (§5.6).
