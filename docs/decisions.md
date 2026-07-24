# Décisions techniques

> Consigne (CLAUDE.md) : toute décision technique non couverte par la spec est consignée ici et soumise à validation.
> Statuts : **À valider** (proposée, implémentée en attendant le point d'arrêt de l'étape) · **Validée** · **Refusée**.

---

## D-001 — Pile technique du cœur métier
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

- `/core` : bibliothèque **.NET 8** (`DeuxiemeCerveau.Core`), C#, **aucune dépendance de production** (BCL uniquement) — conforme à la règle 4 (aucune bibliothèque Azure ; on va plus loin : aucune bibliothèque du tout).
- Tests : xUnit ; `Microsoft.Data.Sqlite` **dans le projet de tests uniquement** (pour exécuter réellement les migrations locales).
- Vocabulaire du domaine en français (types, propriétés), aligné mot pour mot sur la spec.

## D-002 — Fuseaux horaires : `TimeZoneInfo` + identifiants IANA
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

- Identifiants IANA résolus via `TimeZoneInfo` (ICU sur toutes les plateformes .NET 8). `UTC` accepté comme alias explicite ; les identifiants **Windows** (« Romance Standard Time ») sont **rejetés** pour empêcher toute divergence entre appareils.
- Heure locale **inexistante** (passage à l'heure d'été) : décalée de la durée du saut (02:30 → 03:30 pour un saut d'une heure), convention standard.
- Heure locale **ambiguë** (retour à l'heure d'hiver) : **première occurrence** retenue (l'instant UTC le plus tôt, offset le plus grand).
- Ces deux conventions doivent être implémentées **à l'identique** côté Swift (spécifiées ici pour ça).

## D-003 — RRULE : sous-ensemble RFC 5545 implémenté dans le cœur, rejet bruyant du reste
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

Le format est RRULE (RFC 5545), sans format maison (règle 6). Pour garantir une **parité vérifiable** entre les deux apps, le cœur implémente un sous-ensemble précis plutôt que de dépendre du comportement d'une bibliothèque tierce différente de chaque côté :

- Supporté : `FREQ=DAILY|WEEKLY|MONTHLY|YEARLY`, `INTERVAL`, `COUNT`, `UNTIL` (forme date ou date-heure UTC `…Z`), `BYMONTHDAY` (positif et négatif, ex. `-1` = dernier jour du mois), `BYDAY` (jours simples pour WEEKLY et MONTHLY ; ordinaux `2MO`, `-1FR` pour MONTHLY), `BYMONTH` (YEARLY), `WKST` (défaut `MO`).
- **Rejeté bruyamment** (erreur de validation, jamais d'à-peu-près silencieux) : `SECONDLY|MINUTELY|HOURLY`, `BYSETPOS`, `BYWEEKNO`, `BYYEARDAY`, `BYHOUR/BYMINUTE/BYSECOND`, `BYDAY` sur YEARLY, `BYMONTHDAY` sur DAILY/WEEKLY, toute partie inconnue.
- Sémantique RFC : les dates invalides sont **sautées** (mensuel « le 31 » saute février ; annuel « 29 février » n'existe que les années bissextiles) ; « dernier jour du mois » s'exprime avec `BYMONTHDAY=-1`.
- `DTSTART` = `date_debut` de l'Élément, converti dans son fuseau ; **la première occurrence est toujours DTSTART** et compte pour 1 dans `COUNT`.
- `UNTIL` : forme date-heure comparée sur l'instant UTC de l'occurrence (borne incluse) ; forme date comparée sur la date locale (borne incluse).
- L'expansion produit des heures **locales** (« le loyer du 5 reste le 5 »), converties en UTC selon D-002.

## D-004 — Budget projeté : précisions d'algorithme
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

La spec (§5.1) fixe l'algorithme ; précisions nécessaires à une implémentation déterministe :

- `solde_reference_date` est une **date calendaire** ; l'instant de référence est ce jour à 00:00 UTC. Une occurrence est comptée si son instant UTC est **≥** l'instant de référence (le solde saisi « au matin du jour J » ne contient pas encore les occurrences du jour J).
- Chaque occurrence est rattachée au **mois calendaire local** (fuseau de l'Élément) — cohérent avec ce que l'utilisateur voit.
- La cascade démarre au **mois de la date de référence**, même s'il précède le premier mois affiché (les flux intermédiaires sont intégrés au solde d'ouverture du premier mois affiché).
- Cas limite : occurrence postérieure à l'instant de référence mais dont le mois local précède le mois de référence (fuseaux très à l'ouest) → rattachée au premier mois de la cascade.
- Mois affichés **antérieurs au mois de référence** (solde de référence daté dans le futur) : renvoyés avec `avant_reference = true`, flux non calculés et soldes `null` — aucune projection n'est possible avant le point de départ (§3.4).
- Exclus de la projection : Éléments `annule`, Éléments `supprime = true` (corbeille), Éléments financiers sans date ou sans montant. Tous les autres statuts sont inclus (`paye`/`recu` comme `a_venir`/`attendu`), fenêtre seule décisive — conforme §5.1.
- Solde de référence **négatif autorisé** (découvert réel).

## D-005 — Arbitrage des conflits : précisions
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

- **Détection** : conflit si la version entrante est ≤ à la version courante du serveur (modification concurrente depuis la même base) ; sinon application directe.
- **Résolution** : dernière écriture gagne sur `date_modification` (UTC). **Égalité stricte : le changement déjà appliqué (premier arrivé au serveur) gagne**, l'entrant est archivé — déterministe et stable au rejeu.
- Version appliquée après conflit gagné : `max(version entrante, version courante + 1)` — le compteur ne régresse jamais.
- **Journal** : toute écriture est journalisée. Pour un changement appliqué : payload **canonique appliqué** (sans `server_seq`, porté par la colonne). Pour un perdant : payload **reçu tel quel** (`resultat = perdant_archive`). Le perdant d'un conflit gagné par l'entrant reste récupérable à son entrée de journal d'origine — le filet 3 est garanti dans les deux sens.
- Un lot dont **un** changement est invalide est **rejeté en entier** (atomicité §6.2) avec la liste des erreurs par `change_id`. Les conflits ne sont pas des erreurs.
- Le moteur n'est pas thread-safe : l'adaptateur (API) sérialise les lots (usage mono-personne ; la transaction SQL assure l'atomicité).

## D-006 — Entités synchronisées au-delà de l'Élément
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

- Le moteur de synchro est **générique** : `element`, `categorie`, `projet`, `budget`, `piece_jointe`, `reglage` partagent les mêmes champs d'audit/synchro et le même arbitrage (« écrit une seule fois »). Le pull renvoie toutes ces entités (§8 cite Éléments/catégories/projets ; pièces jointes et réglages en ont autant besoin — scénario §12 pièce jointe lisible sur B, et §3.4 réglage « synchronisé comme le reste »).
- `change_log` reçoit une colonne `entite` (défaut `'element'`) ; `element_id` devient l'identifiant générique d'entité (nom de colonne conservé, §9 affiné sans changement de sémantique).
- Le **recalage du solde** (`PUT /settings/solde-reference`) passe par le même chemin d'arbitrage (un changement d'entité `reglage`, idempotent par `change_id`, LWW par `date_modification`, journalisé). Identifiant d'entité : UUIDv5 déterministe dérivé de la clé `solde_reference`.
- **Fermeture de projet** (§3.2) : appliquée côté serveur au moment du push. Les tâches `a_faire` non supprimées du projet passent `reporte` via des changements induits à `change_id` **déterministe** (UUIDv5 du couple changement déclencheur + tâche) — le rejeu du lot ne les réapplique pas. `date_modification` et appareil hérités du changement déclencheur ; version de tâche incrémentée. Déclenchement : toute application d'un projet en statut `termine` ou `en_pause` (pas de détection de transition — idempotent par construction, les tâches déjà `reporte` ne matchent plus).

## D-007 — JSON canonique
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

- Noms de champs : **français en `snake_case`**, exactement ceux de la spec (§3.1, §9).
- Dates : UTC ISO 8601 avec suffixe `Z` (`2026-07-23T10:00:00Z`, fractions de seconde omises si nulles) ; `solde_reference_date` en date pure `AAAA-MM-JJ`.
- Montants : entiers (centimes). Un montant non entier dans un payload est une **erreur de désérialisation** → lot rejeté.
- Champs `null` omis à l'écriture ; listes vides présentes (`[]`).
- **Champs inconnus rejetés** à la lecture d'un payload : un appareil en retard de migration échoue **bruyamment** au lieu d'écraser silencieusement des champs qu'il ignore (payload complet + LWW rendraient la perte invisible). Les deux apps migrent leur schéma au démarrage (règle 18), le cas est donc transitoire.
- Les `rappels` sont portés par le payload de l'Élément et stockés en colonne JSON (`rappels`) — §9 affiné (aucune table dédiée nécessaire en V1).

## D-008 — Migrations : deux dialectes par migration
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

- Chaque migration numérotée porte **deux scripts équivalents** : T-SQL (Azure SQL) et SQLite (bases locales). La liste vit dans le cœur (`ListeMigrations`) ; côté Swift elle est **répliquée à l'identique** depuis ce fichier de référence.
- Un test de parité structurelle vérifie que les deux dialectes définissent les **mêmes tables et colonnes** (aux types près), les tables locales supplémentaires (`outbox`, `sync_etat`) étant explicitement listées.
- Table `schema_migrations` (numéro, nom, date d'application) sur chaque base ; l'exécuteur applique les migrations manquantes **dans l'ordre** au démarrage et refuse toute liste non contiguë.
- Affinements du §9 (sémantique inchangée) : colonne `entite` sur `change_log` ; champs d'audit/synchro explicités sur `categories`, `projets`, `budgets`, `attachments` ; `settings` complétée des champs de synchro ; colonne JSON `rappels` sur `elements` ; index `ix_*_seq` sur chaque table synchronisée.

## D-009 — Validations : points tranchés
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 1

- Statuts : seule l'**appartenance** à la table §3.1 est validée (aucun graphe de transitions imposé — la spec n'en définit pas).
- Types financiers (`facture`, `paiement`, `revenu`) : `montant_centimes` (entier ≥ 0), `devise` (format ISO 4217 `[A-Z]{3}`) et `sens` **requis** ; `sens` doit être cohérent avec le type (`revenu` → `entree`, `facture`/`paiement` → `sortie`). Ces trois champs sont **interdits** sur les autres types.
- `budget_id` : uniquement `facture`/`paiement` (Élément financier de sens `sortie`, §3.6).
- `fuseau` obligatoire dès qu'une date est présente, et **interdit sans date** (cohérence stricte) ; `date_fin` exige `date_debut` et `date_fin ≥ date_debut` ; `journee_entiere` exige `date_debut` ; `recurrence` exige `date_debut`.
- `journee_entiere` : convention de stockage — `date_debut` = minuit local du jour, converti en UTC.
- Champs de tâche (`priorite`, `score_points`, `ordre_manuel`, `est_obligatoire = true`) : **réservés au type `tache`** ; `date_approximative = true` : **réservé au type `envie`** (§3.1).
- Rappels : `relatif` → `minutes_avant` requis (≥ 0), `date` interdite ; `absolu` → `date` requise, `minutes_avant` interdit.
- Audit : `version ≥ 1`, `date_modification ≥ date_creation`, `appareil_source` non vide, `supprime = true` ⇔ `date_suppression` présente.
- Taille de lot push plafonnée à 500 changements (défensif).

## D-012 — API (Étape 3) : choix concrets
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 3 · spec §8

- **L'API est un adaptateur mince autour du cœur (règle 4).** Les endpoints §8 désérialisent, appellent les processeurs du cœur (`ProcesseurPush`, `ProcesseurPull`, `ProcesseurReglage`, `ProcesseurPurge`, `CalculateurProjection`), sérialisent. Aucune logique métier dans `/api`.
- **Sérialisation des lots par l'adaptateur** (D-005 : le moteur n'est pas thread-safe). Un verrou de processus sérialise les opérations mutantes (push, purge, recalage) ; en SQL, la transaction assure en plus l'atomicité.
- **Construction en incréments** : 3a endpoints + DTOs sur magasin mémoire (tests de contrat locaux) ; 3b magasin Azure SQL + migrations au démarrage ; 3c authentification Entra ID + pièces jointes. Le magasin mémoire n'est **jamais déployé** (données non partagées) — le déploiement attend le magasin SQL.
- **Stockage serveur = tables typées §9, source de vérité unique.** Le magasin SQL (`MagasinSynchroSql`) mappe `EtatEntite` ↔ lignes typées ; le payload canonique est **reconstruit à la lecture** depuis les colonnes (pas de JSON redondant, pas de donnée dérivée stockée — règle 9). Fidélité vérifiée par des tests aller-retour.
- **Accès de l'API à SQL par identité managée** : l'identité du Function App devient un *contained user* Entra dans la base, avec les droits CRUD. Le T-SQL de création est exécuté **par le CI** (connecté en tant que service principal, administrateur Entra de la base) — **aucune manip utilisateur**.
- **Authentification Entra ID (§8), intégrée dès maintenant** : middleware validant le jeton Bearer (issuer/audience/signature via métadonnées OIDC). Comptes Microsoft personnels + organisation. Configurable : bypass en local et dans les tests unitaires (jeton non requis hors ligne). **Manip utilisateur unique** : créer l'inscription d'application « API » (audience/scope) — fournie en un lot une fois le code d'auth en place. Les **tests de contrat CI** s'authentifient avec un jeton du service principal (même locataire), la connexion par compte personnel étant exercée par les vraies apps (Étapes 4-5).
- **Enregistrement d'appareil** (`POST /devices/register`) : table `devices` (§9, non synchronisée) ; renvoie un `appareil_id` (UUID serveur).
- **Pièces jointes** (§7) : URL SAS Blob à durée limitée. Génération par **délégation d'utilisateur** (SAS signé via l'identité managée) de préférence à la clé de compte, quand disponible.

## Q-001 — Question ouverte : propagation de la purge manuelle
**Statut : tranchée par D-010** (2026-07-23)

La purge définitive (§5.6) est la seule destruction réelle, mais le contrat §8 v3.1 n'exposait **aucune route de purge**. Une purge locale seule ferait « ressusciter » l'entité au pull suivant. → Résolue par la décision D-010 ci-dessous, intégrée à la spec **v3.2** (modifiée d'abord, conformément à la consigne).

## D-010 — Purge arbitrée par le serveur, propagée par le pull, protégée par pierre tombale
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · spec v3.2 (§5.6, §6.2, §8, §9)

Choix retenu : option (a) enrichie — route dédiée `POST /purge` en V1, avec destruction réelle et anti-résurrection. Principes, dans l'ordre de priorité :

1. **La conservation gagne toute course.** Une purge n'est acceptée que si l'entité est **encore `supprime = true`** quand la demande atteint le serveur. Restaurée entre-temps → purge **refusée** (`refusee`), et l'appareil qui avait purgé localement récupère l'entité au pull suivant. Une purge d'entité inconnue est refusée aussi (jamais de destruction par identifiant deviné).
2. **Destruction réelle, protocole intact.** Purge acceptée : état supprimé ; payloads du journal de cette entité **caviardés** (`{"purge":true}`) en conservant `server_seq`/`change_id`/`resultat` (idempotence et continuité des séquences) ; **pierre tombale** dans `purges` (migration 002) ; l'événement consomme un `server_seq` ordinaire et le **pull transporte les purges** — chaque appareil supprime définitivement sa copie locale.
3. **Anti-résurrection.** Tout changement poussé vers une entité tombale est refusé **sans archivage du payload** (`refuse_purge`) — entorse unique et assumée au filet 3, couverte par la confirmation explicite de la purge ; l'app abandonne l'entrée d'outbox et purge sa copie locale.
4. **Alignement §7** : la purge d'un Élément purge ses pièces jointes (changements induits à `change_id` UUIDv5 déterministes, rejouables). Le blob lui-même est détruit par l'adaptateur (Étape 3). Le `reglage` n'est pas purgeable.
5. **Idempotence et atomicité** comme le push : `change_id` par demande, lot tout-ou-rien pour les erreurs de validation (les refus sont des résultats, pas des erreurs).

Écarté : (b) purge différée en V2 — laisserait la « seule destruction réelle » inopérante en multi-appareils ; (c) suppression serveur sans tombale — résurrection garantie par le premier appareil resté hors-ligne.

## D-011 — Infrastructure Étape 2 : choix concrets
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 2 · spec §10

- **Région : West Europe** (`westeurope`) — l'un des deux choix autorisés (§10.1), retenu pour la disponibilité la plus large des offres gratuites ; basculer sur `francecentral` reste un paramètre.
- **Authentification GitHub → Azure : fédération OIDC** (aucun secret stocké, cohérent règle 16). Trois identifiants non secrets dans les secrets GitHub : `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. Le déploiement ne part que de la branche `main` (credential fédérée liée à `refs/heads/main`).
- **Droits du service principal** : *Contributeur* (déployer) + ***Role Based Access Control Administrator*** sur l'abonnement — nécessaire pour que Bicep attribue les rôles à l'identité managée du Function App ; plus étroit qu'*Owner*.
- **Ordre de déploiement garanti** : le module `budget.bicep` (alerte 50 % prévu / 90 % / 100 % réel, filtrée sur `rg-dc-dev`, plafond 5 €/mois paramétrable) est une dépendance explicite de toutes les autres ressources (§10.3 : l'alerte d'abord).
- **SQL serverless palier gratuit permanent** : `GP_S_Gen5_2`, `useFreeLimit = true`, `freeLimitExhaustionBehavior = AutoPause` (quota épuisé → pause, jamais de facturation), pause auto à 60 min, 32 Go. **Entra ID uniquement** (`azureADOnlyAuthentication`) — aucun mot de passe SQL n'existe ; l'administrateur est le service principal de déploiement (les migrations passeront par lui, Étape 3).
- **Function App Windows, plan Consommation Y1**, .NET 8 isolé, identité managée système (conservée pour l'Étape 3 : accès SQL + Key Vault par identité).
- **Stockage du runtime Functions par chaîne de connexion** (révisé le 2026-07-23 après échec de déploiement). Le montage initial « stockage par identité managée » était doublement problématique : (1) il exigeait le rôle *RBAC Administrator* pour attribuer Blob/Queue Data à l'identité — permission que le service principal *Contributeur* n'a pas, et dont la propagation a fait échouer plusieurs déploiements ; (2) surtout, sur **plan Consommation Windows**, le partage de contenu (`WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`) **n'accepte pas l'identité managée** — le démarrage aurait échoué de toute façon. Le runtime utilise donc une chaîne de connexion (clé injectée par Bicep via `listKeys`, jamais dans git). **Les trois attributions de rôles sont retirées** : le déploiement ne requiert plus que *Contributeur*. Le rôle *RBAC Administrator* ajouté à la main devient inutile (peut être retiré pour le moindre privilège). Distinction assumée : le stockage *runtime* (bookkeeping de l'hôte Functions) par clé ; les secrets *applicatifs* (SQL, Étape 3) par Key Vault + identité managée — l'esprit de la règle 16 (aucun secret dans git) est préservé. En Étape 3, l'accès de l'app au Key Vault se fera par **politique d'accès** (`accessPolicies`, permission incluse dans *Contributeur*) plutôt que par attribution de rôle RBAC, pour rester déployable sans *RBAC Administrator*.
- **Blob** : conteneur `pieces-jointes` privé (`allowBlobPublicAccess = false`, §7 : SAS uniquement). **Key Vault** en mode RBAC, rôle *Secrets User* pour l'app (Étape 3).
- **Journalisation** : Log Analytics PerGB2018 + Application Insights (workspace) avec échantillonnage — sous la franchise permanente de 5 Go/mois d'ingestion, coût attendu nul à notre échelle.
- **Nommage** : `rg-dc-{env}`, suffixe `uniqueString(abonnement, env)` pour les noms globaux (storage, SQL, Key Vault, Functions) — reproductible et sans collision.

## D-013 — Pièces jointes (§7, §8) : URL SAS signées par la clé du runtime, métadonnées via la synchro
**Statut : validée** (2026-07-23, décision déléguée par l'utilisateur) · Étape 3 · spec §7, §8

Précise (et corrige) la note de D-012 qui préférait la *délégation d'utilisateur* : ce chemin est écarté, pour la même raison que le stockage runtime (D-011) — signer une SAS par délégation exige que l'identité du Function App porte un rôle *Storage Blob Data* sur le compte, or l'attribution de rôle RBAC échappe au service principal *Contributeur* qui déploie. Retenu à la place :

1. **Séparation binaire / métadonnées.** Le fichier vit dans Blob Storage ; ses **métadonnées** (`PieceJointe` : `element_id`, `nom_fichier`, `taille_octets`, `blob_path`, `confirme`) sont une **entité synchronisée à part entière** (`EntiteSynchro.PieceJointe`, D-006) qui transite par le push/pull ordinaire (§6.2) — même audit, même arbitrage, même journal. L'API des pièces jointes ne fait donc que **courtier des URL SAS** ; elle n'ouvre aucune seconde voie d'écriture des métadonnées.
2. **SAS signées par la clé de compte du runtime.** `StockagePiecesBlob` construit un `BlobContainerClient` depuis la chaîne `AzureWebJobsStorage` (déjà injectée par Bicep via `listKeys`, jamais dans git — l'esprit de la règle 16 est préservé, comme pour le stockage runtime) et signe des SAS de **15 min** : écriture (`Write|Create`) pour l'envoi, lecture (`Read`) pour le téléchargement. Aucune attribution de rôle requise → déployable avec le seul *Contributeur*.
3. **`GET /attachments/upload-url`** valide la taille (≤ 25 Mo, §7), dérive un `blob_path` **opaque** des identifiants (`{element_id}/{attachment_id}`, jamais du nom de fichier), renvoie `{attachment_id, blob_path, upload_url, expire_le}`. Le client téléverse en direct, pousse la `PieceJointe` par la synchro, puis appelle **`POST /attachments/confirm`** qui vérifie la présence réelle du binaire (envoi terminé) et renvoie sa taille. **`GET /attachments/{id}/download-url`** lit le `blob_path` dans les métadonnées synchronisées et renvoie une SAS de lecture ; pièce inconnue ou supprimée → 404.
4. **Testabilité (règle 4).** L'interface `IStockagePieces` isole Azure ; l'implémentation mémoire (`StockagePiecesMemoire`) couvre le local et les tests (jamais déployée). Le cœur reste sans dépendance Azure.

Migration V2 possible sans rupture de contrat : basculer vers la délégation d'utilisateur le jour où le déploiement dispose de *RBAC Administrator* — seul `StockagePiecesBlob` change, le §8 est inchangé.

## D-014 — Structure de l'app Windows : cœur applicatif cross-plateforme + coquille WinUI
**Statut : validée** (2026-07-24, décision déléguée par l'utilisateur) · Étape 4 · spec §4, §6

L'app Windows (`/apps/windows`) est scindée en deux, pour maximiser ce qui est testable et minimiser ce qui est écrit deux fois (Swift/C#) :

1. **`DeuxiemeCerveau.App`** — cœur applicatif **cross-plateforme** (`net8.0`, **sans WinUI**), donc compilable et **testé en CI Linux**. Il porte les parties risquées de l'app : base locale SQLite, outbox, client de synchro (§6), services de lecture, export/import local (§5.7). Référence `/core` (modèle, JSON canonique, migrations, validations, projection, RRULE), **jamais** `/api` (règle 4 : le client ne connaît pas le serveur).
2. **`DeuxiemeCerveau.Windows`** — coquille **WinUI** (`net8.0-windows`, Windows uniquement) : vues XAML, navigation, services plateforme (notifications, sélecteurs de fichiers, chemin de la base). Elle **affiche et saisit** seulement (garde-fou-architecture, règle 2) ; toute la logique vit dans le cœur applicatif.
3. **`DeuxiemeCerveau.App.Tests`** (`net8.0`) — couvre les scénarios de parité solo (§12) indépendants de l'UI. Référence `/api` **en test uniquement**, pour adosser le fake d'API au vrai `ServiceApi` in-process (aller-retour client↔serveur réaliste) — jamais en production.

Choix structurants :
- **Base locale montée par les MÊMES migrations que le serveur** (`ListeMigrations` du cœur, dialecte `Sqlite`) via une cible d'application locale (`CibleMigrationLocale`) — jamais de schéma transcrit à la main (D-008 : une divergence de schéma = perte de données silencieuse). Le stockage des entités reprend le motif du serveur (payload canonique + colonnes typées §9).
- **`server_seq` jamais généré côté client** (§6.2) : 0 tant qu'une entité n'a pas été tirée du serveur (le pull pose la valeur autoritaire).
- **Filet 1 dès la persistance** : `BaseLocale` est le point d'écriture locale immédiate ; le réseau vient ensuite (client de synchro, incrément 4d).

Cette structure vaut pour la seule app Windows (C#) ; l'app Apple (Swift, §5) réimplémente la même couche §6 depuis la **spec**, jamais depuis ce code (garde-fou-synchro).
