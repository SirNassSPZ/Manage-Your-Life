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
--rappels                 passage de notification sans fenêtre (échéances de demain), puis quitte
--digest                  idem, en forçant le point de la semaine + le rappel d'export
--donnees <dossier>       monte l'app sur un AUTRE dossier que celui de l'utilisateur
```

`--mode` accepte maintenant **n'importe quel intitulé de sous-vue**, accents et casse indifférents
(« Par categorie » trouve « Par catégorie »). Attention à PowerShell : `Start-Process -ArgumentList`
**ne met pas les guillemets** autour d'un élément contenant une espace — il faut les écrire soi-même,
sinon `Par catégorie` arrive en deux arguments et le mode est ignoré en silence.

`--donnees` est ce qui permet de photographier un écran garni sans écrire une ligne de démonstration
dans la vraie base (D-026).

`--rappels` / `--digest` ne sont **pas** des modes outil : si l'app est déjà ouverte, ils s'effacent
sans rien faire — c'est elle qui notifie, pour qu'un seul processus touche à `local.db` (D-023).

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
| Finances — par catégorie | ✅ (groupes pliables, sous-totaux, « Sans catégorie ») |
| Corbeille | ✅ (restauration 1 geste, purge en 2 temps) |
| Sauvegarde (export / import) | ✅ (barre latérale, contre l'état de synchro) |
| Notifications locales | ✅ (toasts, 2 tâches planifiées, journal de dédup) |

Scénarios de parité §12 : **les trois passent** contre l'API déployée (`--parite`).

## 4. Ce qu'il reste — par ordre de valeur

### 4.1 Notifications locales — ✅ fait (D-023)

Les cinq points sont réglés. Un seul s'est écarté du plan noté ici, et pour une raison de fond :
**`--rappels` / `--digest` ne sont pas des modes outil.** Les y mettre aurait posé un second
processus sur la vraie `local.db` — ce que D-019 interdit. `--parite` s'en tire parce qu'il monte
des postes jetables ; ces modes-ci lisent les données réelles. Ils s'effacent donc devant
l'instance en place, qui fait ses propres passages (au démarrage, puis un par heure). Tout est
dans D-023.

- `PlanificateurRappels.Derouler(...)` porte l'enchaînement complet — décider, écarter, remettre,
  noter — et reçoit la remise en paramètre : c'est testé sur la CI Linux, l'app Apple reprendra des
  tests plutôt que du code Windows.
- `JournalRappels` (`rappels.json`, hors du schéma synchronisé) dédoublonne et porte le drapeau
  **désactivable** du rappel d'export.
- `Services/ServiceToasts.cs` remet, et rien d'autre.
- `apps/windows/PlanifierRappels.ps1` déclare les deux tâches planifiées (`-Supprimer` pour retirer).
- Rappel mensuel d'export (§5.7) : il voyage avec le passage hebdomadaire, clé mensuelle.

### 4.2 Sauvegarde — ✅ fait (D-024)

Le trou repéré en livrant 4.1 : l'export avait un service, pas de porte. Bloc « SAUVEGARDE » dans la
barre latérale, contre l'état de synchro — les deux répondent à la même question. Export, import
(refusé si l'installation n'est pas vierge, §5.7), et la case **désactivable** du rappel mensuel.

### 4.3 Groupement pliable par catégorie — ✅ fait (D-025)

Sous-vue « Par catégorie » de Finances, qui était déclarée mais inerte. Le Calendrier n'est
volontairement pas groupé : il a déjà cet axe par ses filtres, et ses lectures sont organisées par
jour. Voir D-025 et la note ajoutée à I-004.

### 4.4 Étape 4 — point d'arrêt

**Tout est fait.** S'arrêter et demander une validation humaine avant l'Étape 5 (app Apple).
Ne pas enchaîner.

**Un seul chemin n'est couvert par aucun test automatique** : la boîte de dialogue de fichier
elle-même (`SelecteurFichierWindows`). Le piège connu — en application non empaquetée, le sélecteur
exige le HWND, sinon `E_INVALIDARG` — est traité par `InitializeWithWindow`, mais seul un clic
humain le prouve. Un export réel à faire une fois avant de clore l'étape.

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
- **D-023** — notifications locales : **qui a le droit d'ouvrir la base**. Précise D-019. C'est la décision structurante de l'Étape 4h — la lire avant de toucher aux modes sans interface ou aux tâches planifiées.
- **Q-002** — une envie d'achat **ne porte pas de montant** (§3.1). La maquette et I-003 ont été corrigés.

## 7. Périmètre — ce qui a été demandé mais NE DOIT PAS être codé

Consigné dans `docs/idees.md`, en attente d'une décision de périmètre :

- **I-004** — onglet to-do list, « listes » créables et catégorisables, planifications. Le §13 n'inclut pas la tâche dans la saisie V1 ; §5.2 et §5.3 les placent en **V2**.
- **I-005** — favoris sur les notes (aucun champ `favori` dans le §3.1 ; une catégorie « Favoris » ferait le travail sans rien ajouter), et **mise en forme riche + images** dans les notes, qui contredit le §5.5 mot pour mot.

L'utilisateur a choisi de **livrer la V1 d'abord**. Ne pas rouvrir sans décision explicite.

> **Décision explicite reçue (2026-07-26)** : une fois l'Étape 4 close, reprendre `docs/idees.md`
> **point par point**. Les items V2/V3 restent soumis à la règle de `CLAUDE.md` — la spec fait foi,
> donc modifier `docs/specification.md` d'abord, signaler le changement, coder ensuite.

## 8. Le trou repéré à l'Étape 4h — l'export n'avait pas d'interface · **bouché (D-024)**

`ServiceExport` et `ServiceImport` sont écrits, testés, et exercés par le scénario 3 de `--parite`.
Mais **aucune vue ne les appelle** : dans toute la coquille et la présentation, les seuls appels à
`Exporter` / `Importer` sont dans `ScenariosParite.cs`. L'utilisateur n'a donc aucun moyen
d'exporter ses données depuis l'application.

Deux raisons d'en faire une question de fin d'Étape 4 plutôt qu'une idée à consigner :

1. Le §13 range « **export/import complet côté client** avec rappel mensuel » dans la **V1**, et
   `CLAUDE.md` le liste dans le « fini quand » de l'Étape 4.
2. Le rappel mensuel d'export vient d'être livré (§4.1). Une notification qui invite à un geste
   qu'aucun écran ne propose est une promesse creuse.

Le « fini quand » de l'Étape 4 est **satisfait** au sens littéral (les scénarios de parité passent,
export et import compris) — c'est la lecture par scénarios qui a laissé passer l'angle mort. Un
scénario automatisé n'a pas besoin de bouton.

**Leçon à garder pour l'Étape 5.** Une fonction peut être testée de bout en bout et rester
inatteignable. Pour chaque module V1 de l'app Apple, se poser les deux questions séparément :
*est-ce que le service marche ?* et *par où l'utilisateur y accède-t-il ?*

## 9. Élargissement de la V1 — projets et confrontation (D-027)

**Décision de l'utilisateur du 2026-07-26**, après clôture de l'Étape 4 : I-001 (projets personnels)
et I-003 (confrontation d'une envie au budget) passent de V2 à **V1**. Le coût — chaque module
écrit deux fois et repassé aux scénarios §12 — a été énoncé avant et accepté. **La spec a été
modifiée d'abord** (§3.1, §5.1bis, §5.2, §5.3, §5.4, §8, §13), comme l'exige `CLAUDE.md`.

**Aucune migration.** Le schéma §9 portait déjà `montant_centimes` nullable, `projet_id`,
`priorite`, `ordre_manuel`, la table `projets` et `categories.origine = 'projet'`. C'est le
dividende du choix « stabilité du schéma » de la V1 : les entités V2 étaient au schéma dès le
départ pour que ce jour-là ne coûte rien. **Règle 18 non sollicitée.**

### Ce qui a été livré

| Couche | Ce qui a changé |
|---|---|
| `/core` | `CalculateurProjection.Confronter` (§5.1bis) ; validation : prix facultatif sur l'envie, tâche V1 obligatoirement rattachée à un projet |
| `/api` | `GET /projection/confrontation` + DTO ; `ServiceApi.Confronter` |
| `App` | `IClientApi.Confronter` ; `ServiceLecture.Projets / CorbeilleProjets / TachesDeProjet` |
| `Presentation` | `VueModeleProjets`, `VueModeleSaisie`, confrontation dans `VueModeleFinances`, calendriers de projets séparés dans la coquille |
| `Windows` | `VueProjets`, formulaire de saisie en surimpression, bouton « Ça rentre ? », 6 convertisseurs |

### Le garde-fou s'est déplacé — à surveiller

Avant, une envie ne pouvait pas porter de montant : l'interdiction du champ **était** la
protection. Désormais le champ existe, et ce qui protège la projection est son **exclusion du
calcul** (`CalculateurProjection` ignore tout ce qui n'est pas `EstFinancier`). C'est plus fragile
qu'une interdiction de champ. Deux tests le défendent explicitement, un dans le cœur et un à
travers l'API — **ne pas les supprimer en croyant à des doublons.**

### Un troisième trou trouvé au passage : il n'y avait pas de saisie

Le bouton « Ajouter » de la vue Finances **n'était relié à rien**. Hors onboarding et notes,
l'application n'avait **aucun chemin de création** — ni facture, ni paiement, ni revenu, ni
rendez-vous, ni envie. Le §13 range pourtant la saisie typée en V1.

C'est le même défaut que l'export (§8) : un service juste, testé, et inatteignable. **Trois fois de
suite maintenant.** Pour l'Étape 5, poser les deux questions séparément sur chaque module :
*est-ce que le service marche ?* et *par où l'utilisateur y accède-t-il ?*

### Chemins non couverts par un test automatique

- La **boîte de dialogue de fichier** (export / import) — déjà signalé au §4.4.
- Le **clic sur « Ça rentre ? »** : le verdict vient du serveur déployé, donc non exercé hors ligne.
  La décision, elle, est testée dans le cœur et à travers l'API.

### Pièges neufs, à ajouter au §5

- **`x:Bind` TwoWay d'un `bool` vers `CheckBox.IsChecked` (`bool?`) à travers un chemin imbriqué
  laisse la case en état INDÉTERMINÉ** — un rond barré au lieu d'une coche, sans aucune erreur.
  Convertisseur `BoolCoche` obligatoire. Vu et corrigé sur « Me le rappeler chaque mois ».
- **`Windows.System.VirtualKey` ne se résout pas** depuis le namespace `DeuxiemeCerveau.Windows` :
  `Windows.` retombe sur notre propre racine. `global::Windows.System.VirtualKey`. Même piège que
  `global::Windows.UI.Color`, déjà documenté dans `HexPinceau`.
- **L'`Id` d'une entité neuve est `Guid.Empty` tant que `Saisie.Enregistrer` ne l'a pas posé.**
  Lier deux entités entre elles en lisant l'Id avant l'enregistrement casse le lien **en silence**.
  Poser l'`Id` explicitement à la construction quand on relie (`projet.CategorieId`).
- **`TexteVersVisibilite` ne juge que des chaînes.** Lui passer un objet le rend toujours
  invisible, sans erreur — d'où `ObjetVersVisibilite` / `ObjetAbsentVersVisibilite`.
- **Un modèle de vue qui avale le `ResultatSaisie` d'un rejet** donne un bouton qui ne fait rien
  sans le dire. Toujours regarder le résultat et le porter à l'écran.

## 10. Quatre corrections d'architecture d'information (D-028)

Demandées par l'utilisateur après usage réel. Aucune n'ajoute de donnée.

1. **« Budget projeté » n'est plus un onglet** — c'est une sous-vue de Finances. La spec n'en a
   jamais fait un onglet : c'était la maquette. Écart assumé, comme D-021. **L'app Apple doit
   reprendre la même structure.**
2. **Les envies ne s'affichent plus partout** — seulement en vue d'ensemble et dans leur propre
   sous-vue. En regard, « Entrées » et « Sorties » groupent par catégorie de leur seul sens.
3. **Les sept prochains jours sont une grille** de sept colonnes, plus une liste. §5.4 précisé.
4. **Un projet a sa vue calendrier** dans l'onglet Projets. §5.3 précisé.

### Le piège le plus coûteux de la session

Un convertisseur utilisé dans une vue **sans être déclaré dans ses ressources** lève
« Cannot find a resource with the given key » **à l'exécution**, ce qui interrompt
`Bindings.Initialize()` et laisse **TOUTE la vue non liée**. La vue Finances a tourné ainsi
plusieurs heures : le mois n'apparaissait plus dans l'entête, et rien ne le signalait à l'écran.

Le fichier `Coquille.xaml` documentait déjà exactement ce piège, en tête. Il a quand même été
repayé — parce que **le contrôle des erreurs cherchait au mauvais endroit** : le motif de date
utilisé pour lire `demarrage.log` (`T0[4-9]`) ne couvrait pas les heures de l'après-midi, et
rendait « 0 erreur » sur un journal qui en contenait trente.

**Deux réflexes à garder :**
- Après toute édition de XAML, vérifier que chaque `{StaticResource}` est déclaré. Un balayage
  suffit et prend une seconde :

```bash
python3 -c "import re,glob,io,os; j=set(re.findall(r'x:Key=\"([^\"]+)\"', io.open('apps/windows/DeuxiemeCerveau.Windows/Ressources/Jetons.xaml',encoding='utf-8').read())); [print(os.path.basename(f), sorted({m for m in re.findall(r'\{StaticResource (\w+)\}', io.open(f,encoding='utf-8').read())} - set(re.findall(r'x:Key=\"([^\"]+)\"', io.open(f,encoding='utf-8').read())) - j)) for f in glob.glob('apps/windows/DeuxiemeCerveau.Windows/Vues/*.xaml')]"
```

- Lire `demarrage.log` en comparant le **nombre de lignes avant / après** le lancement, jamais en
  filtrant sur une heure écrite à la main.
