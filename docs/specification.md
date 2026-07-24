# Deuxième Cerveau — Spécification technique v3.2

*Document de référence — cahier des charges pour la génération du code.*

> **Usage de ce document.** L'application sera développée par un agent IA, plateforme par plateforme (Apple natif, puis Windows natif). Le risque principal n'est pas la quantité de code, mais la **divergence silencieuse** entre les deux applications. Ce document définit un comportement unique que les deux apps doivent respecter **à l'identique**. Toute règle marquée « NON NÉGOCIABLE » doit être implémentée exactement de la même façon des deux côtés. En cas de doute, ce document fait foi, pas le code déjà écrit. Toute modification de règle se fait **d'abord ici**, ensuite dans le code.

---

## 1. Vision

Une application personnelle qui agit comme un second cerveau : elle centralise l'argent, l'organisation quotidienne et les projets de vie. L'utilisateur y dépose ce qui arrive, l'application le range, le rappelle au bon moment et projette clairement les mois à venir.

Principe directeur : **la saisie est simple, l'organisation est automatique.** Et une garantie de fond : **les données appartiennent à l'utilisateur** — elles sont toujours récupérables, intégralement, sans dépendre d'aucun service.

---

## 2. Plateformes & architecture générale

**Applications clientes (natif par plateforme — Chemin B) :**
- **Apple** (iPhone, iPad, Mac) : SwiftUI.
- **Windows** : .NET / WinUI.

**Backend centralisé sur Azure**, source de vérité unique. Les apps ne se parlent jamais entre elles : tout passe par l'API centrale, hébergée sur **Azure Functions** (choix motivé au §10 ; alternative App Service B1 documentée au même endroit).

```
┌─────────────────┐          ┌─────────────────┐
│  App Apple      │          │  App Windows    │
│  (SwiftUI)      │          │  (.NET / WinUI) │
│  + base locale  │          │  + base locale  │
│  + outbox       │          │  + outbox       │
│  + export local │          │  + export local │
└────────┬────────┘          └────────┬────────┘
         │     (synchro HTTPS)        │
         └──────────────┬─────────────┘
                        ▼
              ┌───────────────────┐
              │   API centrale    │  ← cœur métier isolé (§4),
              │  Azure Functions  │     écrit UNE seule fois
              └─────────┬─────────┘
                        ▼
        ┌───────────────┼───────────────┐
        ▼               ▼               ▼
  ┌──────────┐   ┌────────────┐   ┌──────────┐
  │Azure SQL │   │Blob Storage│   │Key Vault │
  │(vérité + │   │(pièces j.) │   │(secrets) │
  │ journal) │   └────────────┘   └──────────┘
  └──────────┘
```

**Règle d'architecture NON NÉGOCIABLE : les apps affichent et saisissent ; l'API décide.** Projections, arbitrage des conflits, validations, scoring : dans l'API, écrits une seule fois. Chaque morceau de logique laissé dans un client devra être écrit deux fois — et devient un risque de divergence.

**L'adresse de l'API est un paramètre de configuration** dans chaque app, jamais une valeur codée en dur : c'est ce qui permet de changer d'hébergeur sans toucher aux apps (§11).

---

## 3. Le contrat central : le modèle d'Élément

Une facture, un paiement, un revenu, un rendez-vous, une tâche, une envie, une note : **tout est un Élément**. Le « dispatch automatique » est un routage vers des vues et des filtres, jamais un déplacement de données. Les onglets Finances, Calendrier, Tâches, Projets et Note libre sont des fenêtres sur le même ensemble d'Éléments.

Les deux applications **doivent** représenter l'Élément avec exactement ces champs et cette sémantique.

### 3.1 Champs de l'Élément

**Identité et contenu**
- `id` : UUID, **généré sur l'appareil** à la saisie.
- `type` : `facture` | `paiement` | `revenu` | `tache` | `rendezvous` | `envie` | `note`.
- `titre` : texte court (≤ 300 caractères).
- `description` : texte libre facultatif.

**Temps** *(règles de fuseaux : §3.5)*
- `date_debut` : date/heure facultative (UTC).
- `date_fin` : date/heure facultative (UTC), pour les plages.
- `fuseau` : identifiant IANA (ex. `Europe/Paris`), obligatoire dès qu'une date est présente.
- `journee_entiere` : booléen (événement sur la journée, sans heure).
- `date_approximative` : booléen. `true` pour une envie sans date arrêtée — déclenche le rappel intelligent plutôt qu'une alerte fixe.
- `recurrence` : chaîne **RRULE (RFC 5545)** facultative. **NON NÉGOCIABLE : aucun format de récurrence maison.** L'expansion d'une RRULE se fait **dans le fuseau de l'Élément** (un loyer « le 5 » reste le 5, changements d'heure compris).

**Argent** (uniquement `facture`, `paiement`, `revenu`)
- `montant_centimes` : entier, en **centimes**. **NON NÉGOCIABLE : jamais de virgule flottante pour l'argent.**
- `devise` : ISO 4217 (`EUR` par défaut).
- `sens` : `entree` (revenu) | `sortie` (facture, paiement).

**Classement**
- `categories` : liste de références de catégories (§3.3).
- `projet_id` : référence facultative vers un projet (§3.2).
- `budget_id` : référence facultative vers un budget (§3.6) — **uniquement pour les sorties** (`facture`, `paiement`) ; une dépense est allouée à un seul budget au plus.

**Tâches**
- `est_obligatoire` : booléen.
- `score_points` : entier facultatif (points gagnés à l'accomplissement).
- `priorite` : `basse` | `normale` | `haute`.
- `ordre_manuel` : entier facultatif (ordre ajusté à la main).

**Pièces jointes et rappels**
- `pieces_jointes` : liste de références (§7).
- `rappels` : liste de `{ type: relatif | absolu, minutes_avant?: entier, date?: UTC }`.

**Statut** — valeurs autorisées **par type** (table exhaustive, à respecter à l'identique) :

| Type | Statuts autorisés |
|---|---|
| `facture`, `paiement` | `a_venir`, `paye`, `annule` |
| `revenu` | `attendu`, `recu`, `annule` |
| `tache` | `a_faire`, `fait`, `reporte`, `annule` |
| `rendezvous` | `planifie`, `fait`, `annule` |
| `envie` | `idee`, `planifiee`, `faite`, `abandonnee` |
| `note` | `active`, `archivee` |

**Champs de synchronisation et d'audit — NON NÉGOCIABLES** (mécanique : §6)
- `date_creation`, `date_modification` : UTC, posées par l'appareil.
- `appareil_source` : identifiant de l'appareil ayant fait la dernière modification.
- `version` : compteur croissant, incrémenté à chaque modification.
- `server_seq` : numéro de séquence global, **posé par le serveur** (jamais par le client).
- `supprime` : booléen — **une suppression est un marquage, jamais une destruction.**
- `date_suppression` : UTC, le cas échéant.

### 3.2 Entité Projet

Un objectif de vie (*commencer le MMA*, *arrêter de fumer*, *étudier pour les examens*).
- `id`, `nom`, `couleur`, `categorie`, `statut` (`actif` | `en_pause` | `termine`), champs d'audit/synchro identiques à l'Élément.
- **Un projet est un calendrier** : il apparaît automatiquement comme filtre dans le calendrier principal (§5.4).
- À la fermeture : quand un projet passe `termine` ou `en_pause`, ses tâches `a_faire` passent en `reporte` (rien n'est perdu, rien ne pollue les vues actives). Son calendrier-filtre reste disponible, désactivé par défaut.

### 3.3 Entité Catégorie / Calendrier (unifiée)

**NON NÉGOCIABLE : catégorie = label = calendrier.** Une seule notion, un seul système.
- `id`, `nom`, `couleur`, `origine` : `transversale` | `projet`.
- Chaque catégorie est un filtre affichable/masquable du calendrier principal.
- Catégories de départ livrées : école, santé, psychologie, sport, productivité, justice.

### 3.4 Réglage : le solde de référence

**Sans point de départ, aucune projection n'est possible.** Le budget projeté s'appuie sur un réglage utilisateur :
- `solde_reference_centimes` : entier (centimes).
- `solde_reference_date` : date (UTC).

L'utilisateur le saisit une fois (« aujourd'hui, j'ai X € ») et peut le **recaler** à tout moment — le recalage est l'unique geste de correction si la réalité et la projection s'écartent. Ce réglage est synchronisé comme le reste (dernier recalage gagne, historique conservé au journal).

### 3.5 Règles de temps — NON NÉGOCIABLES

- Tous les horodatages sont stockés en **UTC ISO 8601**.
- Tout Élément daté porte son **fuseau IANA** ; l'affichage et l'expansion des RRULE se font dans ce fuseau.
- Les comparaisons de synchro (§6) se font sur les horodatages UTC.

### 3.6 Entité Budget (enveloppes de dépenses)

Un **budget** est une enveloppe de dépenses plafonnée par période (ex. « Courses — 400 €/mois »).
- `id`, `nom`, `couleur`, `montant_periode_centimes` (entier, centimes), `periode` (`mensuel` en V1/V2 ; autres périodes plus tard), `statut` (`actif` | `archive`), champs d'audit/synchro identiques à l'Élément.
- **Allocation simple, délibérément.** Chaque dépense porte au plus un `budget_id`. L'alternative « budget = catégorie avec plafond » a été écartée : les catégories étant multiples par Élément, un même euro serait compté dans plusieurs enveloppes. L'allocation unique garantit qu'une dépense ne pèse que sur un seul budget. **Les catégories restent le système de classement et de filtrage ; les budgets sont le système de plafonds.**
- **Suivi** : pour chaque mois et chaque budget — alloué, **dépensé** (occurrences `paye`), **engagé** (occurrences `a_venir` du mois), reste. Calculé à la lecture par l'API, jamais stocké (même règle que le budget projeté).
- Validation serveur : `budget_id` n'est accepté que sur un Élément financier de `sens = sortie`.

---

## 4. Répartition de la logique & isolation du cœur métier

Cette répartition est le principal garde-fou contre la divergence des deux apps — et contre l'enfermement chez un hébergeur.

**API Azure (une seule fois — le maximum) :**
- Source de vérité (Azure SQL) + **journal des changements** (§6).
- Calcul du **budget projeté** (§5.1) — l'algorithme vit ici et uniquement ici.
- **Arbitrage des conflits** et archivage des perdants.
- Validation des Éléments (types, statuts autorisés, cohérence).
- Calcul des **scores** (V2).
- Émission des URL d'accès aux pièces jointes (§7).

**Chaque app cliente (le minimum, identique des deux côtés) :**
- Base **locale** (écriture immédiate) + **outbox** (§6).
- Vues, filtres, calendrier, formulaires.
- Synchro en tâche de fond (push/pull, §6).
- Expansion des RRULE **pour l'affichage seulement** (le calcul budgétaire reste serveur).
- Cache local des pièces jointes.
- **Export local complet** (§5.7) — sans dépendance au serveur.
- **Notifications locales** : chaque appareil planifie les rappels localement à partir des données synchronisées. Chaque appareil notifie (comportement type Apple Calendar). Le push serveur centralisé est explicitement reporté (V3).

**NON NÉGOCIABLE — Isolation du cœur métier.** Dans l'API, la logique métier (algorithme du budget, arbitrage des conflits, validations, scoring) est écrite comme une **bibliothèque de code ordinaire, sans aucune référence aux SDK Azure**. Elle ne sait pas qu'elle tourne sur Azure. Autour d'elle, une **fine couche d'adaptateurs** fait le lien avec Functions, SQL, Blob et Entra ID. Consigne explicite pour l'agent : *le projet « cœur » ne doit importer aucune bibliothèque Azure ; seuls les adaptateurs y ont droit.* C'est ce qui rend l'hébergeur interchangeable (§11) et la logique testable isolément (§12).

---

## 5. Modules fonctionnels

### 5.1 Finances (V1)

**Sources de revenu** — entrée unique ou récurrente (RRULE). **Sorties** — paiements ponctuels, factures, paiements récurrents. **Liste d'achats souhaités** — envies d'achat par catégories, confrontables au budget projeté.

**Budgets de dépenses (enveloppes)** — définition libre de budgets mensuels plafonnés (§3.6) ; chaque dépense allouable à une enveloppe ; vue de suivi mensuel alloué / dépensé / engagé / reste. *Le champ `budget_id` existe dans le modèle et le schéma dès la V1 (stabilité du schéma, pas de migration) ; la gestion des enveloppes et l'écran de suivi arrivent en V2.*

**Budget projeté — algorithme officiel (vit dans l'API, NON NÉGOCIABLE) :**

1. **Horizon** : 12 mois glissants à partir du mois courant (paramétrable plus tard).
2. **Point de départ** : le solde de référence (§3.4) à sa date.
3. **Expansion** : toutes les occurrences des Éléments financiers (RRULE développées dans le fuseau de chaque Élément) entre la date de référence et la fin de l'horizon. Les Éléments `annule` sont exclus ; les occurrences déjà `paye`/`recu` **comme** celles à venir sont incluses (le solde de référence, antérieur, ne les contient pas encore).
4. **Cascade mensuelle** : pour chaque mois M — solde d'ouverture (= clôture de M−1, ou solde de référence pour le premier segment) + entrées de M − sorties de M = **solde de clôture de M**.
5. **Report de déficit** : automatique par construction — une clôture négative devient l'ouverture du mois suivant. L'app **met en évidence** tout mois dont la clôture est négative.
6. **Jamais stocké** : la projection est calculée à la lecture. **NON NÉGOCIABLE.**

*Résultat pour l'utilisateur : « à la fin de tel mois, il me restera tant », mois par mois, avec les mois rouges visibles d'un coup d'œil.*

### 5.2 Organisation quotidienne

- **Rendez-vous** à date fixe, avec rappels — **V1** (le calendrier en a besoin pour exister).
- **Tâches** : priorité + ordre manuel (V2). Le tri « intelligent » : V3.
- **Score** des tâches obligatoires : V2.
- **Templates de planning** : V2.
- **Activités « envies »** avec rappel intelligent : V2.

### 5.3 Projets personnels (V2)

Tâches propres, label, calendrier dédié devenant filtre automatique du calendrier principal. Comportement de fermeture : §3.2.

### 5.4 Calendrier principal unifié (V1)

Onglet à part entière, inspiré d'Apple Calendar.
- Superposition de calendriers ; chaque catégorie est un filtre affichable/masquable.
- **Affiche dès la V1 :** les rendez-vous **et les échéances financières** (factures, paiements, revenus datés) — le calendrier montre la vie ET l'argent.
- Les calendriers de projets s'y ajoutent automatiquement en V2.

### 5.5 Note libre (V1)

Onglet brouillon : espace de texte libre, sans structure imposée.
- Une note = Élément `type = note` (texte dans `description`, ni date ni montant requis).
- Mêmes garanties de synchro que tout Élément : un brouillon ne se perd jamais.
- **V3 :** conversion d'une note en Élément typé (« payer loyer 800€ le 5 » → facture pré-remplie). En V1 : simple espace texte, aucune intelligence.

### 5.6 Corbeille (V1)

Conséquence visible du soft delete :
- Vue listant les Éléments `supprime = true`, avec **restauration** en un geste.
- **Purge définitive : manuelle uniquement, jamais automatique**, avec confirmation explicite. C'est la seule opération de destruction réelle de l'application.
- **La purge est arbitrée par le serveur et propagée** (v3.2, décision D-010). L'app envoie une demande de purge (`POST /purge`, §8), idempotente par `change_id` et atomique par lot. Le serveur n'accepte la purge que si l'entité est **encore dans la corbeille** (`supprime = true`) au moment où la demande l'atteint : si elle a été restaurée entre-temps, la purge est **refusée** — en cas de course, la conservation gagne toujours (le prochain pull restitue l'entité sur l'appareil qui avait purgé localement). Une purge acceptée : détruit l'état de l'entité, **caviarde ses payloads au journal** (les métadonnées — `server_seq`, `change_id`, `resultat` — sont conservées pour l'idempotence et la continuité des séquences), enregistre une **pierre tombale** (`purges`, §9) et reçoit un `server_seq` ordinaire — chaque appareil qui tire (§6.2) apprend la purge et supprime définitivement sa copie locale. La pierre tombale empêche toute résurrection : un changement retardataire visant une entité purgée est **refusé sans archivage du payload** (`refuse_purge`) — unique entorse assumée au filet 3, couverte par la confirmation explicite de l'utilisateur au moment de la purge. La purge d'un Élément purge ses pièces jointes (§7). Le réglage (§3.4) n'est pas purgeable (pas de corbeille). En local, l'app purge immédiatement (local-first) et vide de son outbox les changements en attente de l'entité purgée.

### 5.7 Export & récupération des données (V1) — NON NÉGOCIABLE

**Les données appartiennent à l'utilisateur.** Il doit pouvoir tout récupérer, à tout moment, même si le serveur est inaccessible ou l'abonnement Azure suspendu.

**L'export.**
- Produit une **archive ZIP** contenant : `donnees.json` (tous les Éléments, catégories, projets, réglages — solde de référence compris) + un dossier `pieces_jointes/` avec les fichiers d'origine.
- **Complet, corbeille incluse** : les Éléments `supprime = true` sont exportés et marqués comme tels. Un export qui perd silencieusement des données supprimées n'est pas une récupération.
- **Format ouvert et lisible sans l'application** : du JSON documenté, des fichiers ordinaires. Critère de réussite : si l'app disparaît, l'archive reste lisible telle quelle.
- **Exécuté côté client, depuis la base locale, sans réseau.** C'est le point décisif : un export qui dépendrait de l'API deviendrait inaccessible au moment précis où on en a besoin (suspension, panne, départ d'Azure). Chaque app sait exporter seule. **Aucune dépendance serveur autorisée pour l'export.**

**L'import.**
- Une archive d'export peut être **réimportée dans une installation vierge** pour reconstituer l'état complet (V1 : import dans une installation vide uniquement ; la fusion avec des données existantes n'est pas requise).

**L'habitude.**
- L'app propose un **rappel mensuel d'export** (notification locale, désactivable) encourageant à conserver l'archive hors de l'application — disque externe, autre cloud. C'est la protection contre le seul cas qu'aucune architecture ne couvre : la perte du compte ou l'erreur humaine.

---

## 6. Synchronisation multi-appareils — NON NÉGOCIABLE

L'endroit exact où les applications perdent des données. À implémenter **à l'identique** dans les deux apps, mot pour mot.

### 6.1 Les trois filets de sécurité

**Filet 1 — Écriture locale d'abord, toujours.** Toute saisie est écrite dans la base locale **immédiatement**, avant tout réseau. La confirmation à l'utilisateur = l'écriture locale. La synchro se fait ensuite, en tâche de fond. **Interdiction absolue d'écrire directement sur le serveur à la saisie.**

**Filet 2 — Rien n'est jamais effacé, seulement marqué** (`supprime = true`), en local comme sur Azure SQL. Seule exception : la purge manuelle depuis la corbeille (§5.6).

**Filet 3 — Le perdant d'un conflit est archivé.** L'API est l'arbitre unique. La version écartée est conservée intégralement au journal des changements, récupérable.

### 6.2 Protocole concret

**Enregistrement.** Chaque appareil possède un `appareil_id` **généré localement au premier lancement** — la saisie hors-ligne (filet 1) ne peut pas attendre un aller-retour serveur — et s'enregistre à la première connexion (`POST /devices/register`), où il **adopte** l'`appareil_id` renvoyé par le serveur. Les entités déjà créées hors-ligne conservent l'id provisoire comme simple marqueur de provenance (sans effet sur l'arbitrage, qui tranche sur `date_modification`). Voir D-015.

**Outbox locale.** Chaque modification locale (création, édition, marquage supprimé) produit une entrée d'outbox : `{ change_id: UUID généré localement, element_id, version, payload complet, date_modification, appareil_id }`. L'outbox est persistante : elle survit au redémarrage de l'app.

**Push.** L'app envoie son outbox **par lots ordonnés** (`POST /sync/push`). Le serveur :
1. **Idempotence** : tout `change_id` déjà vu est ignoré silencieusement (une coupure réseau peut faire renvoyer un lot — il ne doit jamais s'appliquer deux fois). **NON NÉGOCIABLE.**
2. **Atomicité par lot** : le lot s'applique entièrement ou pas du tout.
3. **Arbitrage** : si l'Élément a été modifié entre-temps par un autre appareil → conflit. Règle : **la dernière écriture gagne**, comparée sur `date_modification` (UTC) ; à égalité, l'ordre d'arrivée serveur tranche. **La version perdante est archivée au journal.** (Les horloges d'appareils peuvent diverger légèrement — c'est précisément pourquoi l'archivage du perdant est obligatoire : même un arbitrage imparfait reste réparable. Les horloges logiques type vecteurs/CRDT sont explicitement écartées : complexité injustifiée pour un usage mono-personne.)
4. Chaque changement appliqué reçoit un **`server_seq`** global strictement croissant, et le journal conserve `{ server_seq, change_id, element_id, payload, appareil_id, resultat: applique | perdant_archive }`.
5. L'app vide de son outbox les changements confirmés.

**Pull.** L'app demande `GET /sync/pull?since={dernier server_seq connu}` et reçoit tous les Éléments modifiés depuis son curseur, plus le nouveau curseur. Elle applique en local, sauvegarde le curseur. La reprise après coupure est automatique : le curseur **est** le point de reprise.

**Purge (§5.6).** Hors du lot de push, par la route dédiée `POST /purge` — idempotente par `change_id`, atomique par lot. Le serveur vérifie la corbeille, détruit, caviarde le journal, pose la pierre tombale ; le pull transporte les purges comme n'importe quel changement (`server_seq` ordinaire). Un changement poussé vers une entité purgée est refusé (`refuse_purge`) : l'app abandonne l'entrée d'outbox et supprime définitivement sa copie locale.

**Cycle.** Push puis pull, déclenchés : à l'ouverture de l'app, après toute saisie (différé de quelques secondes), au retour du réseau, et périodiquement en arrière-plan.

### 6.3 Garantie d'ensemble

Perdre une saisie exigerait de percer les trois filets à la fois : elle est sur le disque local avant le réseau (filet 1), rien ne la détruit côté base (filet 2), et même un conflit mal arbitré la garde au journal (filet 3). L'idempotence et le curseur rendent les coupures réseau inoffensives. L'export local (§5.7) ajoute un quatrième niveau, indépendant du serveur.

**Vigilance Chemin B :** cette couche est écrite deux fois (Swift, C#). C'est le morceau le plus risqué du projet — l'agent doit suivre ce chapitre **mot pour mot** des deux côtés.

---

## 7. Pièces jointes (V1)

Photos de factures, documents, attachés à un Élément.
- **Flux d'envoi** : enregistrement local immédiat (cache) → demande d'URL d'envoi à l'API (`GET /attachments/upload-url`) → téléversement **direct vers Blob Storage** via URL signée temporaire (SAS) → confirmation à l'API (`POST /attachments/confirm`). L'envoi échoué se réessaie en tâche de fond ; la pièce reste en cache local tant qu'elle n'est pas confirmée.
- **Lecture** : `GET /attachments/{id}/download-url` → URL signée temporaire → téléchargement direct, mis en cache localement.
- Limite : 25 Mo par pièce, tout type de fichier.
- Soft delete aligné sur l'Élément parent ; purge manuelle uniquement.
- Les pièces jointes en cache local alimentent l'export (§5.7) ; une pièce non présente en cache est téléchargée au moment de l'export si le réseau est disponible, sinon signalée comme manquante dans l'archive.

---

## 8. Contrat d'API (V1)

Hébergement : **Azure Functions** (plan Consommation) — chaque route ci-dessous est une fonction déclenchée par HTTP. Authentification : jeton **Entra ID** (compte Microsoft personnel de l'utilisateur), en `Bearer` sur chaque appel. Rafraîchissement automatique par les apps.

| Méthode & route | Rôle |
|---|---|
| `POST /devices/register` | Enregistre l'appareil, renvoie `appareil_id` |
| `POST /sync/push` | Reçoit un lot d'outbox ; idempotent ; atomique ; renvoie les `change_id` confirmés + conflits archivés |
| `GET /sync/pull?since={seq}` | Renvoie les Éléments/catégories/projets modifiés depuis le curseur, **les purges (§5.6)**, + nouveau curseur |
| `POST /purge` | Purge définitive depuis la corbeille (§5.6) ; idempotente ; atomique ; refusée si l'entité a été restaurée ou est inconnue |
| `GET /projection/budget?mois=12` | Renvoie la projection mensuelle (§5.1) : ouverture, entrées, sorties, clôture par mois |
| `PUT /settings/solde-reference` | Recale le solde de référence (§3.4) |
| `GET /attachments/upload-url` | URL SAS d'envoi |
| `POST /attachments/confirm` | Confirme un envoi terminé |
| `GET /attachments/{id}/download-url` | URL SAS de lecture |

Format : JSON, dates en UTC ISO 8601, montants en centimes entiers. Toute évolution du contrat se fait **d'abord dans ce tableau**. L'export (§5.7) est volontairement absent de ce contrat : il est strictement côté client.

---

## 9. Schéma de base (Azure SQL — point de départ)

DDL de référence, que l'agent affine sans en changer la sémantique :

```sql
CREATE TABLE elements (
  id                UNIQUEIDENTIFIER PRIMARY KEY,
  type              NVARCHAR(20)  NOT NULL,
  titre             NVARCHAR(300) NOT NULL,
  description       NVARCHAR(MAX) NULL,
  date_debut        DATETIME2     NULL,          -- UTC
  date_fin          DATETIME2     NULL,          -- UTC
  fuseau            NVARCHAR(64)  NULL,          -- IANA
  journee_entiere   BIT NOT NULL DEFAULT 0,
  date_approximative BIT NOT NULL DEFAULT 0,
  recurrence        NVARCHAR(500) NULL,          -- RRULE
  montant_centimes  BIGINT        NULL,
  devise            CHAR(3)       NULL,
  sens              NVARCHAR(10)  NULL,          -- entree | sortie
  projet_id         UNIQUEIDENTIFIER NULL,
  budget_id         UNIQUEIDENTIFIER NULL,
  est_obligatoire   BIT NOT NULL DEFAULT 0,
  score_points      INT           NULL,
  priorite          NVARCHAR(10)  NULL,
  ordre_manuel      INT           NULL,
  statut            NVARCHAR(20)  NOT NULL,
  date_creation     DATETIME2     NOT NULL,
  date_modification DATETIME2     NOT NULL,
  appareil_source   UNIQUEIDENTIFIER NOT NULL,
  version           INT           NOT NULL,
  server_seq        BIGINT        NOT NULL,      -- index pour le pull
  supprime          BIT NOT NULL DEFAULT 0,
  date_suppression  DATETIME2     NULL
);
CREATE INDEX ix_elements_seq ON elements(server_seq);

CREATE TABLE categories (
  id UNIQUEIDENTIFIER PRIMARY KEY,
  nom NVARCHAR(100) NOT NULL,
  couleur CHAR(7) NOT NULL,
  origine NVARCHAR(15) NOT NULL,                 -- transversale | projet
  -- + mêmes champs d'audit/synchro que elements
);

CREATE TABLE element_categories (
  element_id  UNIQUEIDENTIFIER NOT NULL,
  categorie_id UNIQUEIDENTIFIER NOT NULL,
  PRIMARY KEY (element_id, categorie_id)
);

CREATE TABLE projets (
  id UNIQUEIDENTIFIER PRIMARY KEY,
  nom NVARCHAR(150) NOT NULL,
  couleur CHAR(7) NOT NULL,
  categorie_id UNIQUEIDENTIFIER NULL,
  statut NVARCHAR(15) NOT NULL,                  -- actif | en_pause | termine
  -- + mêmes champs d'audit/synchro
);

CREATE TABLE budgets (
  id UNIQUEIDENTIFIER PRIMARY KEY,
  nom NVARCHAR(100) NOT NULL,
  couleur CHAR(7) NOT NULL,
  montant_periode_centimes BIGINT NOT NULL,
  periode NVARCHAR(15) NOT NULL,                 -- mensuel
  statut NVARCHAR(15) NOT NULL,                  -- actif | archive
  -- + mêmes champs d'audit/synchro
);

CREATE TABLE attachments (
  id UNIQUEIDENTIFIER PRIMARY KEY,
  element_id UNIQUEIDENTIFIER NOT NULL,
  nom_fichier NVARCHAR(255) NOT NULL,
  taille_octets BIGINT NOT NULL,
  blob_path NVARCHAR(400) NOT NULL,
  confirme BIT NOT NULL DEFAULT 0,
  -- + mêmes champs d'audit/synchro
);

CREATE TABLE devices (
  id UNIQUEIDENTIFIER PRIMARY KEY,
  nom NVARCHAR(100) NOT NULL,
  plateforme NVARCHAR(20) NOT NULL,
  date_enregistrement DATETIME2 NOT NULL
);

CREATE TABLE change_log (                        -- journal = archive des perdants
  server_seq  BIGINT IDENTITY PRIMARY KEY,
  change_id   UNIQUEIDENTIFIER NOT NULL UNIQUE,  -- idempotence
  element_id  UNIQUEIDENTIFIER NOT NULL,
  payload     NVARCHAR(MAX) NOT NULL,            -- version complète (JSON)
  appareil_id UNIQUEIDENTIFIER NOT NULL,
  resultat    NVARCHAR(20) NOT NULL,             -- applique | perdant_archive | purge | refuse_purge
  recu_le     DATETIME2 NOT NULL
);

CREATE TABLE purges (                            -- pierres tombales (§5.6, migration 002)
  entite      NVARCHAR(20) NOT NULL,
  entite_id   UNIQUEIDENTIFIER NOT NULL,
  server_seq  BIGINT NOT NULL,                   -- transporté par le pull
  change_id   UNIQUEIDENTIFIER NOT NULL UNIQUE,
  appareil_id UNIQUEIDENTIFIER NOT NULL,
  purge_le    DATETIME2 NOT NULL,
  PRIMARY KEY (entite, entite_id)
);

CREATE TABLE settings (
  cle NVARCHAR(50) PRIMARY KEY,                  -- ex. solde_reference
  valeur NVARCHAR(MAX) NOT NULL,
  date_modification DATETIME2 NOT NULL
);
```

Les bases **locales** (SQLite/équivalent sur chaque appareil) reprennent la même structure, plus la table `outbox`.

**Évolution du schéma — NON NÉGOCIABLE.** Toute évolution passe par des **migrations numérotées, additives uniquement** (ajout de tables ou de colonnes acceptant NULL ou une valeur par défaut ; jamais de suppression ni de renommage en V1–V3). Chaque base — Azure SQL comme chaque base locale — enregistre son numéro de version de schéma et applique les migrations manquantes au démarrage. La liste des migrations est définie une seule fois (dans le cœur) et répliquée à l'identique des deux côtés : deux apps aux schémas divergents sont une cause directe de perte de données.

---

## 10. Hébergement Azure : architecture, offres et coûts

### 10.1 Ressources

- **API — Azure Functions, plan Consommation.** Choix motivé par le gratuit permanent : 1 million de requêtes par mois sans frais, largement au-dessus du besoin d'une app mono-utilisateur (quelques milliers d'appels de synchro par mois). Pas de quota de temps quotidien qui couperait l'API.
  - *Alternative documentée : App Service.* Son palier **gratuit est inadapté** (limite d'une heure de calcul par jour — une API de synchro doit répondre à tout moment). Le premier palier utilisable (B1, serveur toujours allumé) coûte de l'ordre de 13 $/mois. À ne choisir que si le style « API classique qui tourne en continu » est préféré au style Functions, en connaissance du coût.
- **Base — Azure SQL Database, palier Serverless.** Gratuit **en permanence** dans les quotas du compte : jusqu'à 10 bases, 100 000 secondes de calcul vCore par mois et 32 Go de stockage chacune — très au-delà du besoin. La base se met en pause automatiquement en cas d'inactivité ; la reprise ajoute une latence de quelques secondes au premier appel, acceptable car l'app est local-first et n'attend jamais le serveur pour afficher. La restauration ponctuelle (point-in-time restore) incluse sert de sauvegarde de dernier recours, en plus des filets de la synchro et de l'export.
- **Authentification — Entra ID** (compte Microsoft personnel) : gratuit en permanence. Ne pas utiliser de fonctionnalités *Premium* (payantes, exclues des crédits).
- **Fichiers — Blob Storage** : 5 Go gratuits pendant 12 mois, puis quelques centimes par Go. Accès par URL SAS temporaires uniquement (jamais de conteneur public).
- **Secrets — Key Vault + Managed Identity. NON NÉGOCIABLE : aucun secret en dur dans le code.** Gratuit 12 mois, puis coût négligeable (facturation à la transaction ; l'app lit ses secrets rarement).
- **Sortie réseau** : 100 Go/mois gratuits en permanence — sans objet à l'échelle d'un utilisateur.
- **Région** : France Central ou West Europe (latence, RGPD).
- **Déploiement** : GitHub Actions, jamais de déploiement manuel. Infrastructure décrite en fichiers (Bicep), versionnée avec le code.

### 10.2 Choix de l'offre de compte

Deux offres pertinentes, **avec le même socle de services gratuits permanents** :

- **Compte gratuit classique** : 200 $ de crédit sur 30 jours, carte bancaire obligatoire, puis passage obligé en paiement à l'utilisation (le plafond de dépenses saute — l'alerte de budget devient le seul garde-fou). Stable, sans condition de statut.
- **Azure for Students** : 100 $ de crédit sur 12 mois, **sans carte bancaire** (aucune facture possible ; au pire, suspension de l'abonnement), renouvelable tant que le statut étudiant est vérifié, réservé à un usage non commercial. Attention à ne pas confondre avec « Students Starter » (sans crédit, catalogue restreint).

**Décision pour ce projet : compte gratuit classique** (statut étudiant non disponible à ce jour ; pas de migration prévue si le statut est acquis plus tard — les services gratuits étant identiques, une migration n'apporterait rien et ferait courir un risque de perte de données inutile).

### 10.3 Discipline de coûts — NON NÉGOCIABLE

- **Alerte de budget (Azure Cost Management) posée le premier jour**, avant toute autre ressource.
- Paliers Serverless / Consommation / gratuits **systématiquement** ; toute ressource facturable doit être un choix explicite documenté ici.
- Cible : coût mensuel proche de zéro en régime de croisière (seuls Blob et Key Vault deviennent facturés après 12 mois, pour quelques centimes).

---

## 11. Portabilité : quitter Azure doit rester possible

La protection ne vient pas du choix de l'hébergeur mais de trois propriétés de conception :

1. **Les données ne sont jamais captives.** Copie locale complète sur chaque appareil (filet 1) + export client complet (§5.7). Le départ d'Azure commence avec les données déjà en main.
2. **Le modèle est standard.** SQL relationnel, RRULE, JSON, UTC : rien de propriétaire. Le schéma du §9 se transpose sur PostgreSQL ou autre avec des ajustements mineurs.
3. **Le cœur métier est isolé** (§4). Seuls les adaptateurs connaissent Azure. Migrer = réécrire la fine couche d'adaptateurs, pas la logique.

**Pièces à remplacer en cas de départ** (chacune a des équivalents directs partout) : l'hébergement des Functions (repackager l'API pour un autre hébergeur — Railway, Render, machine virtuelle, voire serveur personnel : le trafic mono-utilisateur n'exige rien de particulier) ; Entra ID (autre fournisseur d'identité) ; Blob (tout stockage d'objets équivalent). Les apps, elles, ne changent que **l'adresse de l'API** (paramètre de configuration, §2).

Ordre de grandeur d'une migration complète : quelques jours de travail d'agent, zéro perte de données.

**Évolution vers un produit commercial (perspective).** L'architecture n'interdit pas une commercialisation future ; elle la prépare passivement. Survivraient tels quels : le modèle d'Élément, le cœur métier isolé, le protocole de synchro, et les deux apps natives (qui deviendraient un atout produit). Devraient changer : passage multi-utilisateurs côté serveur (un identifiant de propriétaire sur chaque table, chaque requête filtrée — changement mécanique grâce au modèle unifié), authentification grand public (inscription/connexion clients à la place du compte Microsoft personnel), facturation et abonnements, distribution par les stores (comptes développeur et commissions), montée vers des paliers Azure payants, et obligations légales complètes (RGPD, CGU — l'export §5.7 couvrant déjà la portabilité des données exigée). Décision assumée : **ne rien construire de tout cela maintenant.** Un multi-utilisateurs prématuré ralentirait la V1 sans bénéfice ; la seule préparation utile est déjà dans ce document — isolation du cœur (§4), contrat d'API propre (§8), formats standards (§3.5), export (§5.7).

---

## 12. Tests & parité entre les deux apps

La logique étant concentrée dans l'API — et isolée dans un cœur sans dépendance Azure (§4), donc testable directement — la stratégie a trois étages :

1. **Tests du cœur métier, exhaustifs** (écrits une fois, portent l'essentiel) : algorithme du budget (cas : mois négatif en cascade, récurrences tordues — dernier jour du mois, tous les 2 mois —, recalage du solde, changements d'heure), arbitrage des conflits, idempotence du push (renvoyer deux fois le même lot = même état), atomicité.
2. **Jeux de référence (« golden files »)** : entrées connues → projection attendue, vérifiés à chaque déploiement.
3. **Scénarios de parité croisée**, à dérouler sur les deux apps avant chaque livraison — l'état final doit être **identique** :
   - Saisie sur A hors-ligne → reconnexion → visible sur B.
   - Modification du même Élément sur A et B hors-ligne → reconnexion → même gagnant partout, perdant présent au journal.
   - Suppression sur A → corbeille sur B → restauration sur B → restauré sur A.
   - Coupure réseau en plein push → reprise → aucun doublon (idempotence).
   - Pièce jointe envoyée depuis A avec coupure en cours d'envoi → reprise → lisible sur B.
   - **Export depuis A (réseau coupé) → import sur une installation vierge → contenu identique, corbeille et pièces jointes en cache comprises.**

L'agent doit exécuter ces scénarios et corriger toute divergence **avant** livraison.

---

## 13. Découpage en versions

**Ne pas tout construire en parallèle.** Livrer un socle qui a de la valeur seul, puis empiler.

**V1 — Le socle utile, sûr et récupérable.**
Modèle d'Élément complet ; saisie typée (facture, paiement, revenu, rendez-vous, note) ; **budget projeté** de bout en bout avec solde de référence ; **calendrier principal** affichant rendez-vous + échéances financières, avec filtres par catégories ; **note libre** ; **pièces jointes** ; **corbeille** ; rappels par **notifications locales** (rendez-vous et échéances) ; **export/import complet côté client** avec rappel mensuel ; **synchronisation complète** (les trois filets, outbox, push/pull, journal) — la synchro et l'export font partie de la V1, car ce sont eux qui protègent les données.

**V2 — L'organisation.**
Tâches (priorité, ordre manuel), score, templates de planning, activités « envies » et rappel intelligent, projets personnels avec calendriers-filtres automatiques, liste d'achats confrontée au budget, **budgets de dépenses (gestion des enveloppes et suivi mensuel, §3.6)**, import avec fusion dans des données existantes.

**V3 — L'intelligence.**
Agencement automatique des tâches (optimisation sous contraintes — en dernier), rappel intelligent affiné, conversion des notes libres en Éléments typés (saisie en langage naturel), notifications push centralisées si nécessaire.

---

## 14. Règles NON NÉGOCIABLES — liste de contrôle pour l'agent

À respecter **à l'identique dans les deux applications** :

1. Tout est un Élément ; le dispatch est un routage de vues, jamais un déplacement de données (§3).
2. Catégorie = label = calendrier : une seule notion (§3.3).
3. Les apps affichent et saisissent ; l'API décide (§2, §4).
4. **Le cœur métier n'importe aucune bibliothèque Azure** ; seuls les adaptateurs connaissent l'hébergeur (§4).
5. Argent en centimes entiers, jamais en flottant (§3.1).
6. Récurrence en RRULE (RFC 5545), expansée dans le fuseau de l'Élément (§3.1, §3.5).
7. Horodatages en UTC ; fuseau IANA porté par chaque Élément daté (§3.5).
8. Statuts strictement conformes à la table par type (§3.1).
9. Le budget projeté (§5.1) et le suivi des budgets (§3.6) vivent dans l'API et ne sont jamais stockés.
10. Synchro filet 1 : écriture locale d'abord, réseau ensuite en tâche de fond (§6).
11. Synchro filet 2 : rien n'est jamais effacé, seulement marqué ; purge manuelle uniquement, depuis la corbeille (§5.6, §6).
12. Synchro filet 3 : l'API arbitre ; tout perdant de conflit est archivé au journal (§6).
13. Push idempotent (`change_id` unique) et atomique par lot ; pull par curseur `server_seq` (§6.2).
14. Pièces jointes : local d'abord, envoi en tâche de fond via URL SAS, cache tant que non confirmé (§7).
15. **Export complet côté client, sans dépendance serveur, corbeille incluse, dès la V1** (§5.7). L'adresse de l'API est un paramètre de configuration, jamais codée en dur (§2, §11).
16. Aucun secret en dur ; Key Vault + Managed Identity (§10). Alerte de budget posée avant toute autre ressource (§10.3).
17. Les scénarios de parité du §12 — export/import compris — passent sur les deux apps avant toute livraison.
18. Migrations de schéma numérotées et **additives uniquement**, appliquées au démarrage, identiques sur Azure SQL et les bases locales (§9).

---

*Fil conducteur : la valeur est dans la justesse du modèle, la cohérence entre les deux applications, et la certitude que les données restent à l'utilisateur. Ce document est le garant des trois — il vaut plus que le code.*

---

## Historique des révisions

- **v3.2 (2026-07-23)** — La purge manuelle est arbitrée par le serveur et propagée : route `POST /purge` (§8), pierre tombale `purges` et caviardage du journal (§5.6, §9), refus `refuse_purge` des changements retardataires (§6.2). Comble l'absence de propagation de la purge dans le contrat v3.1 (question Q-001, décision D-010 de `docs/decisions.md`).
- **v3.1** — Version de référence initiale du dépôt.
