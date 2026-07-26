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

## Q-002 — Une envie d'achat peut-elle porter un montant ? — ~~**NON**~~ → **OUI, facultatif** (renversée)
**Statut : RENVERSÉE le 2026-07-26 par D-027.** Ce qui suit décrit l'état du 2026-07-25 et reste utile pour comprendre *pourquoi* la question s'est posée ; **il ne décrit plus le comportement attendu**. Voir **D-027**.

**Retenu : option (a).** L'envie reste un souhait **nommé, sans montant**. Aucun changement de modèle, aucune migration, aucune règle nouvelle à écrire deux fois. Un prix se matérialise le jour où l'utilisateur décide d'acheter, sous forme de **sortie datée** — qui entre alors naturellement dans le budget projeté (§5.1).

**Corrections appliquées** : `docs/maquette.html` n'affiche plus de prix sur les envies, et le paragraphe fautif d'`idees.md` I-003 porte un correctif. Le comportement du cœur, lui, était déjà juste — un test le fige (`Une_envie_ne_peut_pas_porter_de_montant_en_V1`).

**Contradiction.** Trois documents disent trois choses :
- **`specification.md` §3.1** — « **Argent** (uniquement `facture`, `paiement`, `revenu`) ». Le montant est donc **interdit** sur une envie.
- **Le cœur** applique le §3.1 à la lettre : enregistrer une `envie` avec un montant est **rejeté** (`montant_interdit`). C'est le comportement réel, couvert par un test.
- **`docs/maquette.html`** affiche pourtant des prix sur les envies (« Casque audio — 180 € »), et **`idees.md` I-003** l'affirme aussi : « une envie peut porter un **montant** et des catégories ».

**Conséquence aujourd'hui.** Le panneau « Envies d'achat » de la vue Finances liste les envies **sans prix**. C'est conforme au code et au §3.1, mais en écart avec la maquette.

**Options.**
- **(a) Tenir le §3.1.** L'envie reste un simple souhait nommé ; un prix se matérialise le jour où l'on décide d'acheter, sous forme de sortie datée (ce que dit déjà I-003 pour la V1). Corriger la maquette et I-003. **Aucun changement de modèle, aucune migration.**
- **(b) Autoriser le montant sur `envie`.** Modifier le §3.1 **d'abord** (spec-first), assouplir le validateur, et l'implémenter à l'identique côté Swift. Additif (le champ existe déjà en base), donc sans migration destructive — mais cela ouvre la question du `sens` et du `budget_id` sur une envie, et rapproche le sujet de la confrontation au budget, qui est **V2** (I-003).

**À trancher par l'utilisateur.** Tant que la question est ouverte, le code suit le §3.1 — la spec fait foi sur le code.

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
> **Affinée par D-022** (2026-07-25) : la couche de présentation (modèles de vue, mise en forme, composition) est sortie de la coquille WinUI dans un quatrième projet `net8.0`, testé en CI Linux. Le point 2 ci-dessous se lit désormais « coquille WinUI = XAML, convertisseurs, MSAL, HWND, toasts ».

L'app Windows (`/apps/windows`) est scindée en deux, pour maximiser ce qui est testable et minimiser ce qui est écrit deux fois (Swift/C#) :

1. **`DeuxiemeCerveau.App`** — cœur applicatif **cross-plateforme** (`net8.0`, **sans WinUI**), donc compilable et **testé en CI Linux**. Il porte les parties risquées de l'app : base locale SQLite, outbox, client de synchro (§6), services de lecture, export/import local (§5.7). Référence `/core` (modèle, JSON canonique, migrations, validations, projection, RRULE), **jamais** `/api` (règle 4 : le client ne connaît pas le serveur).
2. **`DeuxiemeCerveau.Windows`** — coquille **WinUI** (`net8.0-windows`, Windows uniquement) : vues XAML, navigation, services plateforme (notifications, sélecteurs de fichiers, chemin de la base). Elle **affiche et saisit** seulement (garde-fou-architecture, règle 2) ; toute la logique vit dans le cœur applicatif.
3. **`DeuxiemeCerveau.App.Tests`** (`net8.0`) — couvre les scénarios de parité solo (§12) indépendants de l'UI. Référence `/api` **en test uniquement**, pour adosser le fake d'API au vrai `ServiceApi` in-process (aller-retour client↔serveur réaliste) — jamais en production.

Choix structurants :
- **Base locale montée par les MÊMES migrations que le serveur** (`ListeMigrations` du cœur, dialecte `Sqlite`) via une cible d'application locale (`CibleMigrationLocale`) — jamais de schéma transcrit à la main (D-008 : une divergence de schéma = perte de données silencieuse). Le stockage des entités reprend le motif du serveur (payload canonique + colonnes typées §9).
- **`server_seq` jamais généré côté client** (§6.2) : 0 tant qu'une entité n'a pas été tirée du serveur (le pull pose la valeur autoritaire).
- **Filet 1 dès la persistance** : `BaseLocale` est le point d'écriture locale immédiate ; le réseau vient ensuite (client de synchro, incrément 4d).

Cette structure vaut pour la seule app Windows (C#) ; l'app Apple (Swift, §5) réimplémente la même couche §6 depuis la **spec**, jamais depuis ce code (garde-fou-synchro).

## D-015 — Client de synchro : identité d'appareil et acheminement du réglage
**Statut : validée** (2026-07-24, décision déléguée par l'utilisateur) · Étape 4 · spec §6.2, §8 (clarification portée dans la spec)

- **`appareil_id` généré localement, puis adopté du serveur.** Pour honorer le filet 1 (saisie hors-ligne immédiate), le client génère un `appareil_id` provisoire dès le premier lancement (stocké dans `sync_etat`). À la première connexion, `POST /devices/register` renvoie un id que le client **adopte** pour la suite. Les entités créées hors-ligne gardent l'id provisoire en `appareil_source` — un marqueur de provenance sans effet sur l'arbitrage (qui tranche sur `date_modification`, §6.2.3) ni sur l'idempotence (par `change_id`). Le §6.2 de la spec a été clarifié en conséquence (spec-first).
- **Un seul aiguillage d'entités, partagé.** La désérialisation/validation/sérialisation par type est extraite en `AiguilleurEntites` dans `/core`, utilisée par le serveur (`ProcesseurPush`) **et** par le client (saisie, application du pull). Éviter deux aiguillages qui divergeraient est une précaution directe contre le risque n° 1 (garde-fou-synchro).
- **Le réglage du solde transite par la synchro ordinaire.** Le client pousse **toutes** les entités synchronisées — y compris le `reglage` (§3.4) — via l'outbox → `POST /sync/push`, et les reçoit via `GET /sync/pull` (le serveur traite et renvoie le `reglage` comme toute entité, D-006). La route dédiée `PUT /settings/solde-reference` (§8) reste au contrat pour un recalage direct, mais n'est pas le chemin de ce client : un seul flux (outbox/curseur) porte tout, ce qui simplifie et uniformise la reprise après coupure.
- **DTOs du fil côté client.** L'app définit ses propres DTOs de réponse (format §8), comme le fera l'app Apple en Swift. Ils sont validés contre la sortie **réelle** du serveur par les tests (aller-retour JSON canonique via le vrai `ServiceApi` en process), ce qui fait échouer franchement toute divergence de nom de champ.

## D-016 — Pièces jointes côté client (§7) : cache local, blob_path déterministe, envoi en tâche de fond
**Statut : validée** (2026-07-24, décision déléguée par l'utilisateur) · Étape 4 · spec §7, §5.7

- **Local-first (filet 1).** « Joindre » met le fichier **en cache local immédiatement** et crée les métadonnées (`PieceJointe`, `confirme = false`) — qui se synchronisent comme toute entité — **sans aucun réseau**. La confirmation à l'utilisateur = l'écriture locale.
- **`blob_path` déterministe côté client** = `{element_id}/{attachment_id}`, **miroir exact** du schéma de l'API (`ServiceApi.PreparerEnvoiPiece`). C'est ce qui permet de créer les métadonnées **hors-ligne** (avec leur `blob_path`) avant tout appel réseau. Couplage assumé et documenté : le schéma est stable ; si l'API le changeait, il faudrait le refléter ici.
- **Envoi en tâche de fond**, réessayable : `GET /attachments/upload-url` (URL SAS) → **téléversement direct vers Blob** → `POST /attachments/confirm` → passage `confirme = true` (qui se synchronise). Une pièce déjà confirmée est ignorée ; un envoi coupé repart au prochain cycle (idempotent par nature).
- **Lecture** : depuis le cache si présent, sinon `GET /attachments/{id}/download-url` → téléchargement → mise en cache.
- **Alimente l'export (§5.7)** : les fichiers présents dans le cache sont inclus dans l'archive ZIP (`pieces_jointes/{id}__{nom}`) ; une pièce absente du cache est omise (repérable par sa présence dans `donnees.json` sans fichier — « signalée manquante »). L'import restaure les fichiers dans le cache.
- **Adaptateurs (règle 4)** : `IStockageFichiersLocal` (disque en prod / mémoire en test) et `ITransfertBlob` (HTTP en prod / mémoire en test). Le cœur reste sans dépendance ; le binaire ne transite jamais par l'API.

## D-017 — Confirmation « payé/reçu » : Éléments ponctuels + sélection multiple en V1 ; par occurrence récurrente reporté V2
**Statut : validée** (2026-07-24, décision de l'utilisateur : **Option A**) · **V1 = confirmation des Éléments ponctuels (+ sélection multiple)** ; **Option B (registre d'occurrences) planifiée V2** · Étape 4 · spec §3.1, §3.4, §5.1, §3.6

**Constat (la spec fait foi).**
- **Déjà en V1, sans rien ajouter :** confirmer qu'un paiement/une facture est réglé, ou qu'un revenu est arrivé, **est** un changement de **statut** de l'Élément (§3.1) : `facture`/`paiement` : `a_venir` → `paye` ; `revenu` : `attendu` → `recu`. Cela passe par la saisie locale-d'abord et la synchro ordinaire (§6). Le budget projeté (§5.1) et le suivi d'enveloppe (§3.6 : « dépensé = occurrences `paye` ») s'appuient sur ce statut.
- **Sélection multiple (« confirmer en lot ») :** simple commodité d'UI qui applique le même changement de statut à N Éléments sélectionnés — **aucun** changement de modèle. Compatible V1.
- **Trou dans la spec :** le statut vit sur **l'Élément entier**. Pour un Élément **récurrent** (RRULE), les occurrences sont **développées pour l'affichage seulement** (§4) et **ne sont pas stockées** — il n'existe donc **aucun** moyen, dans le modèle actuel, de marquer « le loyer de juillet est payé mais pas celui d'août ». Or §5.1 et §3.6 **parlent** d'« occurrences `paye` », ce qui présuppose un statut par occurrence absent du §3.1. Incohérence réelle à trancher.

**Lecture fidèle au périmètre (recommandée par défaut).** Le §3.4 désigne le **recalage du solde** comme « **l'unique geste de correction** si la réalité et la projection s'écartent ». Donc la réponse V1 native pour le récurrent : on **n'coche pas** chaque échéance ; la projection suppose qu'elles ont lieu, et tout écart réel se corrige par un recalage. → **V1 = confirmation `payé/reçu` sur les Éléments ponctuels (+ sélection multiple) ; la confirmation par occurrence d'une série récurrente est reportée.**

**Options si l'utilisateur veut la confirmation par occurrence dès la V1 (élargit le périmètre → exige de modifier la spec d'abord, CLAUDE.md) :**
- **Option A — Report V2 (recommandée).** Tenir la V1 telle que ci-dessus ; consigner la confirmation par occurrence en V2. Aucun risque, aucune migration.
- **Option B — Registre de confirmations d'occurrence (additif).** Nouvelle entité synchronisée `ConfirmationOccurrence { element_id, date_occurrence, statut, champs d'audit }`, **additive** (règle 18, pas de migration destructive), lue par la projection et le suivi d'enveloppe. Évite de matérialiser/altérer les RRULE. Coût : une entité de plus à implémenter **à l'identique dans les deux apps** (risque n° 1) + règles d'arbitrage.
- **Option C — Matérialisation d'occurrence (exception de série).** Cocher une occurrence la **détache** en un Élément daté propre (`paye`), la série récurrente recevant un `EXDATE`. Standard « éditer une occurrence » des agendas, mais introduit EXDATE/overrides absents de la spec — plus lourd et plus risqué.

**Décision retenue (2026-07-24).** **Option A.** En **V1** : un Élément **ponctuel** (`facture`/`paiement`/`revenu` **non récurrent**) peut être confirmé `payé`/`reçu` (transition de statut §3.1), et une **sélection multiple** applique la confirmation en lot. Pour les **séries récurrentes**, la V1 **ne coche pas** chaque échéance : la projection les suppose réalisées et tout écart se corrige par **recalage** (§3.4). La confirmation **par occurrence** (Option B, entité additive `ConfirmationOccurrence`) est **planifiée V2** — elle sera cadrée dans la spec avant d'être codée (spec-first) ; les Options C (matérialisation/EXDATE) est écartée. Conséquence pour le code (Palier 1) : `ServiceSaisie` expose une confirmation qui **refuse** un Élément récurrent avec un message clair (« recale ton solde pour un ajustement »), et une confirmation en lot sur les ponctuels.

## D-018 — Quatre ajouts V1 « légers » à la saisie et aux rappels (presets, suggestion de catégorie, saisie rapide segmentée, digest hebdo)
**Statut : validée** (2026-07-24, décision de l'utilisateur — « Palier 0 » de la feuille de route) · Étape 4f · spec §3.1, §3.3, §4, §5.1

Quatre affinements d'**expérience** validés, choisis parce qu'ils **réduisent la friction de saisie et créent l'habitude** (les deux causes n°1 de réussite/échec d'une app de finances) **sans toucher au modèle de données** (§3) ni au protocole de synchro (§6) — donc sans risque de divergence entre apps (risque n° 1) :

1. **Charges de départ pré-remplies (I-rec #2).** À l'onboarding, un **catalogue de presets** (loyer, électricité, internet, abonnement, salaire…) propose des Éléments à compléter (montant + jour), au lieu de formulaires vides. Ce n'est **que du contenu de départ** : chaque preset crée un Élément **standard** (§3.1), rien de nouveau dans le schéma. Vit dans le cœur applicatif cross-plateforme (testable), réutilisé à l'identique par l'app Apple.
2. **Suggestion automatique de catégorie (I-rec #4).** Une **fonction pure** `label → catégorie suggérée` (ex. « Loyer » → *Logement*), **modifiable** et **facultative** (sans catégorie, la saisie fonctionne). Aucune contrainte nouvelle ; les catégories restent celles du §3.3. Testable dans le cœur.
3. **Saisie rapide segmentée (I-rec #3).** Un formulaire **rapide en une ligne, à champs séparés** (montant / libellé / date / récurrence), **pas** de parsing de texte libre (le langage naturel reste **V3**, §13). Produit un Élément typé ordinaire via `ServiceSaisie`.
4. **Digest hebdomadaire (I-rec #13).** Une **notification locale** récurrente (« point du dimanche »), planifiée par chaque appareil **à partir des données synchronisées** — strictement dans le cadre des **notifications locales du §4** (aucun push serveur, reporté V3). Rare et actionnable, pour créer le rendez-vous d'habitude.

**Garde-fou.** Ces quatre points restent V1 **parce qu'**ils n'ajoutent ni entité, ni champ, ni règle de synchro. Toute dérive (un preset qui stockerait un plafond = enveloppe §3.6 V2 ; une suggestion qui deviendrait une catégorisation auto imposée ; un parsing NLP) sortirait du périmètre et repasserait par une décision.

## D-019 — Coquille WinUI : `Main` écrit à la main plutôt que généré
**Statut : à valider** · Étape 4f · **consignée après coup** (2026-07-25) — la décision avait été prise et implémentée pendant le développement local de la coquille, mais jamais écrite ici. Reconstituée depuis le code (`Programme.cs`, `DeuxiemeCerveau.Windows.csproj`).

**Justification resserrée (2026-07-25), après vérification de la documentation Microsoft.** Des trois motifs invoqués dans le code, **un seul** exige réellement un `Main` maison :

| Motif | Exige `DISABLE_XAML_GENERATED_MAIN` ? |
|---|---|
| **Instance unique / redirection d'activation** | **Oui.** Documenté : « it must do so as early as possible, and before initializing any windows […] the app must define DISABLE_XAML_GENERATED_MAIN, and write a custom Main ». |
| Modes sans interface `--rappels` / `--digest` | Non — `OnLaunched` peut faire le travail et sortir sans jamais créer de fenêtre. |
| Activation par toast | Non — `GetActivatedEventArgs` est lisible depuis `OnLaunched`. |

**Pourquoi l'instance unique n'est pas un confort ici.** `BaseLocale` détient **une** `SqliteConnection` sur `%LOCALAPPDATA%\DeuxiemeCerveau\local.db`. Deux processus lancés en parallèle (un toast, une tâche planifiée, un double-clic) écriraient dans la même base et la même outbox : c'est un risque pour les filets 1 et 2, pas une gêne d'ergonomie.

**Deux pièges à traiter au moment de l'implémenter**, signalés par la documentation et non gérés aujourd'hui :
- `RedirectActivationToAsync` est **asynchrone et ne doit pas être attendu sur un thread STA**. Le `Main` actuel est `[STAThread] static void` ; en C#/WinUI la parade documentée est de le déclarer `async`.
- `UnregisterKey` avant l'arrêt, sinon une instance en cours de fermeture peut recevoir une redirection.
- La documentation précise que le mécanisme ne fonctionne qu'en **x64** : déjà satisfait (`PlatformTarget=x64`, `RuntimeIdentifier=win-x64`).

**Dette assumée.** Aucune des trois fonctions n'est implémentée à ce jour : le `Main` maison ne rapporte donc rien pour l'instant, il ne fait que préparer l'instance unique. C'est un choix, pas un oubli — le réécrire plus tard coûterait plus cher que de le garder.

- `DISABLE_XAML_GENERATED_MAIN` est activé et `Programme.Main` est écrit à la main.
- Conséquence obligatoire : `WinRT.ComWrappersSupport.InitializeComWrappers()` doit être appelé en premier ; sans lui, toute activation COM WinRT échoue en `E_NOINTERFACE` dès `new App()`.
- L'application est **non empaquetée** (`WindowsPackageType=None`), dépendante du framework (`WindowsAppSDKSelfContained=false`, valeur explicite car le paquet Base a `true` par défaut), avec `RuntimeIdentifier=win-x64` — sans RID, les DLL natives du bootstrapper et de WinUI ne sont pas copiées et le lancement échoue en `DllNotFoundException`.
- **Fait depuis (D-023)** : les modes `--rappels` / `--digest` existent, ainsi que l'activation par toast. Ils ne sont **pas** des modes outil — ils s'effacent devant l'instance en place au lieu de l'ignorer, précisément pour tenir l'invariant ci-dessus. Voir D-023.
- La **dette assumée** ci-dessus est donc levée : le `Main` maison porte désormais les trois fonctions qui le justifiaient.

## D-020 — La coquille WinUI vit hors de `DeuxiemeCerveau.sln`
**Statut : à valider** · Étape 4f · **consignée après coup** (2026-07-25), reconstituée depuis `DeuxiemeCerveau.Windows.csproj`.

`DeuxiemeCerveau.Windows` est **exclu de la solution**. La CI applicative tourne sur `ubuntu-latest` et WinUI ne compile que sous Windows (D-014) : l'inclure casserait `dotnet build` et `dotnet test` à la racine pour tout le monde. La coquille se compile par **chemin de `.csproj`**, dans un job `windows-latest` dédié — même motif que les projets de `tools/`.

**Correction (2026-07-25) : le job n'existait pas.** Cette décision décrivait un garde-fou qui n'avait jamais été écrit — `ci.yml` ne contenait qu'un job `ubuntu-latest` sur la solution, d'où la coquille est justement exclue. Elle n'était donc couverte par **rien, pas même une compilation**, et pouvait partir cassée sans aucun signal. Le job `coquille` a été ajouté.

**Alternative examinée et écartée : les filtres de solution (`.slnf`).** C'est le mécanisme documenté, et il garderait la coquille visible dans l'IDE et dans `dotnet sln list`. Écarté parce qu'il ajoute un fichier à tenir à jour à chaque nouveau projet, et que le support de `.slnf` par `dotnet test` reste flou (`dotnet sln` ne l'accepte que depuis le SDK 9.0.3xx). L'exclusion simple + un job dédié couvre le même besoin sans cette fragilité.

**Le risque résiduel est traité par D-022.** L'exclusion voulait dire que toute logique glissée dans la coquille échappait aux tests. Ce risque est désormais structurel, pas seulement surveillé en revue : la logique de présentation vit dans `DeuxiemeCerveau.Presentation`, qui est **dans** la solution et donc testé sur la CI Linux. Il ne reste dans la coquille que ce qui exige Windows — et le job `coquille` se contente délibérément de le compiler.

## D-021 — Contraste : l'encre secondaire s'écarte de la maquette
**Statut : validée** (2026-07-25, décision de l'utilisateur : **lisibilité**) · Étape 4f · spec §5.4

`docs/maquette.html` pose `--ink-3: #9A938C` pour les libellés en petites capitales. Mesuré sur `--surface-2` (`#EFEDEA`) : **2,6:1**, très en dessous du seuil WCAG AA de **4,5:1** pour du petit texte. Or cette encre porte de l'information qu'on lit vraiment : en-têtes de jours du calendrier, « SOLDE DE RÉFÉRENCE », intitulés de section, état de synchro.

**Retenu.** `Encre3` passe à **`#6E6862`** — 4,7:1 sur `Surface2`, 5,5:1 sur blanc, même ton chaud, et les trois niveaux d'encre restent distincts à l'œil.

Un **quatrième niveau**, `Encre4` (l'ancien `#9A938C`), est conservé pour l'estompage réellement voulu : les jours hors du mois affiché dans la grille du calendrier. Ce sont du **contexte adjacent**, pas du texte à lire — la seule place où l'encre la plus pâle se justifie.

**Portée.** La maquette reste la référence de ton, de disposition et de palette ; c'est un écart **ponctuel et documenté**, pas une réécriture. **L'app Apple doit reprendre les mêmes valeurs** — une divergence de contraste entre les deux apps serait exactement le risque n° 1. `docs/maquette.html` n'est volontairement pas modifiée : elle garde la trace de la proposition d'origine.

## D-022 — Couche de présentation extraite dans un projet testable
**Statut : validée** (2026-07-25, décision de l'utilisateur : « le meilleur code possible ») · Étape 4f · **affine D-014** · spec §4, §12

**Constat.** Les modèles de vue de la coquille n'importaient **aucun** `Microsoft.UI` : ils ne dépendaient de WinUI que par leur emplacement. Or ils portent de la vraie logique — calcul de la grille du calendrier (décalage du lundi, six semaines, regroupement par jour **local**, débordement), lecture d'un montant saisi (la porte d'entrée de la règle 5), mémoire des filtres masqués, mise en forme française. Rien de tout cela n'était testé, puisque la coquille vit hors solution (D-020) et que la CI tourne sur Linux. Un bug de saisie d'argent y dormait : `« 12,34,56 »` était lu **123 456 €**.

**Décision.** D-014 passe de trois projets à quatre :

1. **`DeuxiemeCerveau.App`** (`net8.0`) — cœur applicatif : base locale, outbox, synchro §6, lecture, export/import. Inchangé.
2. **`DeuxiemeCerveau.Presentation`** (`net8.0`, **nouveau**) — modèles de vue, `Format`, `AccesDonnees`, `IFournisseurJeton`, `OptionsApp`, `Composition`. **Dans la solution**, donc compilé et testé sur la CI Linux avec le cœur.
3. **`DeuxiemeCerveau.Windows`** (`net8.0-windows`) — uniquement ce qui **exige** Windows : XAML, convertisseurs `IValueConverter`, MSAL, HWND, toasts, sélecteurs de fichiers.
4. Les projets de tests correspondants.

**Ce qui rend la présentation testable.** `Composition` ne construit plus ce qu'elle utilise : elle **reçoit** sa configuration (`OptionsApp`), son fournisseur de jetons (`IFournisseurJeton`) et sa pile HTTP. Lire `appsettings.json` et parler à MSAL sont des affaires d'hôte. Un test monte donc le **vrai** graphe — vraie base SQLite, vraies migrations, vrais services — sur un dossier temporaire, avec `FournisseurJetonAbsent` : c'est-à-dire l'état **hors ligne**, qui est le mode nominal de l'app (filet 1), pas un cas de repli.

**Règle de placement, vérifiable.** Si un `using Microsoft.UI` apparaît dans `DeuxiemeCerveau.Presentation`, le code est au mauvais endroit. Inversement, toute logique qui apparaît dans `DeuxiemeCerveau.Windows` doit descendre d'un cran — le job CI `coquille` ne fait que **compiler**, il ne teste rien, et c'est délibéré (règle 2 : la coquille affiche et saisit, rien d'autre).

**Conséquence pour l'app Apple.** La couche de présentation est écrite deux fois (C#, Swift) — c'est le risque n° 1. Des tests sur le comportement attendu (grille du calendrier, arrondis, filtres) documentent désormais ce que la version Swift doit reproduire, au lieu de laisser le code C# faire foi.

**Écarté :** un projet de tests `net8.0-windows` exécuté sur le seul job Windows. Il aurait laissé la logique de présentation hors de la CI principale, donc invisible pour l'app Apple et hors des 441 tests de référence.

## D-023 — Notifications locales : qui a le droit d'ouvrir la base
**Statut : à valider** · Étape 4h · spec §4, §5.7 · **précise D-019**

**Le point dur n'est pas le toast, c'est le processus.** Remettre une notification demande un déclenchement quand l'app est fermée, donc une tâche planifiée, donc **un second processus**. Or D-019 a établi qu'un seul processus doit toucher `%LOCALAPPDATA%\DeuxiemeCerveau\local.db` : `BaseLocale` ouvre une `SqliteConnection` en mode rollback par défaut — ni WAL, ni `busy_timeout` — et applique les migrations à l'ouverture, qui est une écriture. Deux processus concurrents, et c'est `SQLITE_BUSY` immédiat au mieux.

**Écarté : traiter `--rappels` comme un mode outil.** C'était le plan noté en passation. `--parite` s'en tire parce qu'il monte des postes **jetables** dans des dossiers temporaires ; `--rappels` doit lire les vraies données de l'utilisateur. Les mettre dans `ModesOutil` — donc hors de la redirection d'instance — aurait posé un second processus sur la base réelle, exactement ce que D-019 interdit.

**Écarté aussi : passer la base en WAL + `busy_timeout`.** SQLite saurait alors encaisser un lecteur concurrent. Mais cela change une infrastructure partagée et testée pour le confort d'un seul appel, et l'app Apple devrait reprendre le même réglage sans que la spec ne l'exige nulle part. L'invariant « **un seul processus ouvre jamais `local.db`** » s'énonce, se vérifie et se retranscrit en Swift ; un réglage de pragma, beaucoup moins.

**Retenu — le passage s'exécute là où la base est déjà ouverte.**

| Situation | Ce qui se passe |
|---|---|
| App fermée, tâche planifiée déclenchée | Le processus fait le passage **sans fenêtre**, puis sort |
| App ouverte, tâche planifiée déclenchée | Le processus **s'efface** sans rien ouvrir |
| App ouverte | Elle fait ses propres passages : un au démarrage, puis un par heure |

Sans la minuterie horaire, une app laissée ouverte depuis lundi ne notifierait plus rien de la semaine. Les deux chemins appellent le **même** `PlanificateurRappels.Derouler` et partagent le même journal : le doublon est impossible, quel que soit celui qui arrive en premier.

`InstanceEnPlace()` lit `AppInstance.GetInstances()` **sans s'enregistrer**, contrairement à `FindOrRegisterForKey` : un mode de notification qui prendrait la clé deviendrait la cible des redirections pendant la seconde où il tourne, et avalerait un double-clic de l'utilisateur sans jamais ouvrir de fenêtre.

**La décision reste dans la couche testée.** `PlanificateurRappels.Derouler(composition, journal, maintenant, forcerDigest, remettre)` porte tout l'enchaînement — décider, écarter ce qui a déjà sonné, remettre, noter — et reçoit la remise en paramètre. `DeuxiemeCerveau.Windows` ne fournit que `ServiceToasts.Remettre`. C'est D-022 appliqué : l'app Apple reprend un enchaînement décrit par des tests, pas par du code Windows.

**Le journal (`rappels.json`) est hors du schéma synchronisé.** Un fichier JSON à côté de la base, délibérément : le §4 confie les rappels à chaque appareil (« chaque appareil notifie »). Ce qui a sonné ici ne regarde pas les autres appareils, et la règle 18 n'a pas à porter une table pour ça. Il retient les clés remises (purgées à 90 jours) et le drapeau **désactivable** du rappel d'export.

**On note après la remise, jamais avant.** Si Windows refuse le toast — notifications coupées, `Show` en échec — rien n'est noté et le rappel repassera. Marquer comme remis ce que personne n'a vu perdrait le rappel pour de bon.

**Rappel mensuel d'export (§5.7) : aucune planification propre.** Il voyage avec le passage hebdomadaire, sa clé porte le mois (`export-2026-07`), le journal écarte les suivants. Il arrive donc au moment où l'utilisateur fait déjà le point, et cela fait une tâche planifiée de moins.

**Deux tâches planifiées qui se recouvrent, volontairement.** Quotidienne `--rappels` à 08:00, hebdomadaire `--digest` le dimanche à 09:00 (`apps/windows/PlanifierRappels.ps1`, tâches utilisateur, sans élévation — les notifications sont refusées aux applications élevées). Le dimanche, la quotidienne ferait déjà le digest via `EstJourDuDigest`. Le recouvrement achète une chose précise : avec `-StartWhenAvailable`, une machine éteinte le dimanche déclenche quand même le digest au rallumage — ce qu'un simple contrôle du jour de la semaine ne saurait pas rattraper, puisqu'on serait lundi.

**Deux ordres imposés par la documentation Windows App SDK**, et ils se contredisent avec l'aisance : `NotificationInvoked` s'abonne **avant** `Register()`, et `Register()` s'appelle **avant** `GetActivatedEventArgs()` — celui-là même que la redirection d'instance unique utilise. D'où l'appel à `ServiceToasts.Enregistrer()` très tôt dans `Programme.Main`. L'app étant **non empaquetée**, `Register()` est ce qui lui fabrique une identité et inscrit le serveur COM qui permet à Windows de relancer l'exe au clic ; sans lui, rien ne s'affiche.

**Dette réglée au passage.** Recevoir une redirection d'activation ne faisait rien : un second lancement disparaissait en silence. Il fallait le traiter de toute façon — cliquer un toast relance l'exe et passe par ce chemin. `Programme.SurActivation` ramène désormais la fenêtre devant, en la restaurant d'abord si elle était réduite.

## D-024 — L'export avait un service, pas de porte
**Statut : à valider** · Étape 4i · spec §5.7, §13

**Constat.** `ServiceExport` et `ServiceImport` étaient écrits, testés, et exercés par le scénario 3 de `--parite` — mais **aucune vue ne les appelait**. Dans toute la coquille, les seuls appels à `Exporter` / `Importer` étaient dans `ScenariosParite.cs`. L'utilisateur n'avait aucun moyen d'exporter ses données.

Le « fini quand » de l'Étape 4 était pourtant satisfait au sens littéral : les scénarios de parité passent, export et import compris. C'est la lecture **par scénarios** qui a laissé passer l'angle mort — un scénario automatisé n'a pas besoin de bouton. Le §13 range pourtant « export/import complet côté client » en **V1**, et livrer le rappel mensuel d'export (D-023) rendait le trou intenable : une notification qui invite à un geste qu'aucun écran ne propose est une promesse creuse.

**Placement : contre l'état de synchro, pas dans un écran à soi.** La barre latérale porte déjà le bloc « où en sont mes données » — état de synchro, compte Entra. L'export répond à la même question. Une zone de plus dans la barre du haut aurait modifié l'architecture d'information de la maquette pour un geste qu'on fait une fois par mois ; le vrai théâtre de l'action est la boîte de dialogue système, pas un écran d'application.

**Le sélecteur de fichier est la seule partie côté Windows.** `ISelecteurFichier` rend un `Stream`, pas un chemin : c'est le contrat de `ServiceExport.Exporter(Stream)`, et cela laisse un test brancher un `MemoryStream`. L'implémentation Windows existe parce qu'en application **non empaquetée**, un `FileSavePicker` n'a pas de fenêtre parente implicite et lève `E_INVALIDARG` tant qu'on ne lui a pas passé le HWND (`InitializeWithWindow`).

Elle utilise `File.Create` / `File.OpenRead` sur `StorageFile.Path` plutôt que les extensions de flux WinRT : `OpenStreamForWriteAsync` a disparu du .NET moderne. `Create` tronque, ce qui règle au passage le cas d'une archive plus courte écrasant une plus longue. Un fournisseur virtuel (OneDrive à la demande) n'expose pas de chemin : le dire franchement vaut mieux qu'un `ArgumentException` nu.

**L'import refuse une installation non vierge.** La V1 ne fusionne pas (§5.7) et `ServiceImport` écrase entité par entité : lancé sur des données existantes, il produirait un mélange que rien ne saurait défaire. « Vierge » se mesure sur le dépôt **brut**, corbeille comprise — un poste qui n'aurait que des Éléments supprimés n'est pas vierge, et un import y écraserait une corbeille encore récupérable (filet 2).

**Le rappel mensuel est désactivable, comme l'exige le §5.7** — une case dans le même bloc, adossée au drapeau de `JournalRappels` (D-023).

## D-025 — Groupement par catégorie : mise en forme, pas modèle
**Statut : à valider** · Étape 4i · spec §3.3, §5.1 · **répond à I-004 point 1**

Troisième point de la demande I-004, le seul qui restait dans le périmètre V1. **Aucun champ, aucune entité, aucune règle de synchro** : les Éléments portent déjà leurs catégories (§3.3), la vue ne fait que les ranger.

**Finances seulement, et c'est un choix.** La demande parlait aussi du Calendrier. Il a déjà son axe par catégorie — les filtres « Mes calendriers » de la barre latérale, qui sont le mécanisme du §5.4 — et ses deux lectures sont organisées par **jour**. Y superposer un groupement par catégorie ferait entrer deux axes en concurrence dans la même vue. Finances, elle, avait une liste plate et une sous-vue « Par catégorie » **déjà déclarée dans la barre latérale mais inerte** : elle retombait sur « Tout ».

**Un Élément à plusieurs catégories apparaît sous chacune.** Les sous-totaux ne s'additionnent donc pas au total du mois. C'est assumé, et cohérent avec ce que la vue faisait déjà : ce sont des étiquettes de liste, pas des projections (règle 9).

**« Sans catégorie » est un groupe de plein droit**, pas un oubli — c'est souvent le plus gros, et le voir est ce qui donne envie de ranger. Il recueille aussi les Éléments dont la catégorie est passée à la corbeille : sans ce rattrapage, leurs mouvements s'évaporeraient de la vue.

**Le pliage se souvient.** Les groupes sont reconstruits à chaque chargement ; sans mémoire, changer de mois rouvrirait tout ce que l'utilisateur vient de fermer — le même piège que les filtres de calendrier.

**La commande de pliage vit sur le groupe, pas sur le modèle de vue parent.** Liée depuis un gabarit, une `RelayCommand<T>` du parent reçoit le DataContext hérité tant que l'élément n'est pas posé et lève dans `CanExecute` — c'est exactement le défaut qui noie `demarrage.log` depuis la vue Calendrier. Sans paramètre, le piège n'existe pas.

## D-026 — `--donnees` : photographier sans écrire chez l'utilisateur
**Statut : à valider** · Étape 4i

`--donnees <chemin>` monte l'application sur un autre dossier de données que celui de l'utilisateur. Vérifier un écran garni exigeait sinon d'écrire des lignes de démonstration dans la vraie base — inacceptable — ou de se contenter d'un écran vide, qui ne prouve rien.

`--mode` accepte désormais **n'importe quel intitulé de sous-vue**, et non plus les trois seuls noms du calendrier. La comparaison se fait sur les lettres nues, sans accents : les intitulés sont accentués (« Par catégorie ») et la page de codes de la console les massacre avant même que l'argument n'arrive.

## D-027 — Élargissement de la V1 : projets personnels et confrontation au budget
**Statut : à valider** · Étape 4j · **renverse Q-002** · **modifie la spec** §3.1, §5.1bis, §5.2, §5.3, §5.4, §8, §13

**Décision de périmètre, prise par l'utilisateur le 2026-07-26**, après livraison de l'app Windows. Deux idées consignées passent de V2 à **V1** :

- **I-001 — projets personnels** : tâches propres, label, calendrier dédié devenant filtre automatique (§5.3).
- **I-003 — confrontation d'une envie au budget projeté** (§5.1bis).

**Le coût a été énoncé avant la décision et accepté** : tout ce qui entre en V1 est écrit **deux fois** (C# et Swift) et doit passer les scénarios de parité §12. L'Étape 5 grossit d'autant. L'alternative proposée — écrire les sections de spec maintenant, coder après la V1 — a été écartée.

**La spec a été modifiée d'abord** (CLAUDE.md), avant toute ligne de code.

### Ce que l'élargissement ne coûte pas : aucune migration

Le schéma §9 porte **déjà** tout ce qu'il faut : `montant_centimes` est `NULL`-able, `projet_id`, `priorite` et `ordre_manuel` existent, la table `projets` existe, et `categories.origine` accepte déjà `projet`. C'est le dividende du choix « stabilité du schéma » pris en V1 (§3.2, D-006) : les entités V2 étaient au schéma dès le départ précisément pour que ce jour-là ne coûte pas de migration. **Règle 18 non sollicitée.**

### Le montant de l'envie — renversement de Q-002

Q-002 avait tranché « pas de montant sur une envie », et c'était le bon appel **à ce moment-là** : la confrontation était V2, donc le champ n'aurait servi à rien et aurait coûté un aller-retour dans les deux apps. La confrontation entrant en V1, le nombre devient nécessaire.

`montant_centimes` et `devise` deviennent **facultatifs** sur une `envie`. `sens` reste **interdit** : une envie n'est pas une sortie, c'est une sortie *éventuelle*.

**Le garde-fou se déplace, il ne disparaît pas.** Avant, c'était l'absence du champ. Désormais c'est l'**exclusion stricte de la projection nominale** : une envie, montant ou pas, n'entre jamais dans `/projection/budget`. C'est ce que les tests doivent défendre, et c'est plus fragile qu'une interdiction de champ — donc à couvrir explicitement, pas incidemment.

### La confrontation vit dans l'API

Même motif que le budget projeté (§4, règle 2) : c'est un calcul, il s'écrit une fois, les deux apps l'affichent. Une confrontation calculée côté client serait le risque n° 1 en action — deux implémentations d'une arithmétique de cascade qui divergent d'un centime.

Elle rend **les deux cascades**, pas un booléen : l'app doit pouvoir montrer l'écart mois par mois. Et elle **n'écrit rien** — ni Élément, ni occurrence, ni trace (règle 9).

### La frontière tâche V1 / tâche V2, rendue vérifiable

Un projet sans ses tâches n'est qu'une étiquette : la tâche entre donc en V1. Mais l'onglet to-do autonome (I-004) reste V2, et l'utilisateur ne l'a pas demandé ici.

La frontière est posée pour être **contrôlable par une assertion**, pas par du jugement : **en V1, une `tache` porte toujours un `projet_id`.** Une tâche sans projet est refusée par le cœur. Le jour où I-004 est décidé, la règle saute — et c'est un seul endroit.

## D-028 — Quatre corrections d'architecture d'information
**Statut : à valider** · Étape 4k · demandées par l'utilisateur après usage réel · spec §5.1, §5.3, §5.4

Quatre demandes qui portent sur **où les choses vivent**, pas sur ce qu'elles calculent. Aucune n'ajoute de donnée : ce sont les mêmes occurrences, lues autrement.

### 1. « Budget projeté » cesse d'être un onglet — écart avec la MAQUETTE, pas avec la spec

**La spec ne fait nulle part du budget projeté un onglet.** Le §5.1 le traite comme une lecture des **finances** ; c'est `docs/maquette.html` qui lui a donné une entrée dans la barre du haut, et le code a suivi. Or la projection répond à la même question que le reste de Finances — « où va mon argent » — et un onglet séparé oblige à faire l'aller-retour entre deux écrans pour comparer un mois et sa projection.

Il devient donc une **sous-vue de Finances**, aux côtés de « Vue d'ensemble », « Entrées », « Sorties » et « Par catégorie ». La barre du haut perd une entrée et gagne en lisibilité.

**C'est un écart assumé avec la maquette, comme D-021** : la maquette reste la référence de ton et de palette, pas un plan d'architecture d'information figé. **L'app Apple doit reprendre la même structure** — une divergence d'IA entre les deux apps serait le risque n° 1 sous une autre forme.

### 2. Les envies ne s'affichent plus partout

Le panneau « Envies d'achat » se montrait dans **toutes** les sous-vues de Finances, y compris « Entrées » et « Sorties » où il n'a rien à voir avec ce qu'on regarde. Il occupait 270 px de la largeur pour rien.

Il ne s'affiche plus que dans « Vue d'ensemble » et dans sa propre sous-vue « Envies d'achat » — laquelle était **déclarée dans la barre latérale mais retombait sur « Tout » ** (le même défaut que « Par catégorie » avant D-025). En regard, « Entrées » et « Sorties » gagnent le **groupement par catégorie de leur seul sens**, ce que la place libérée permet enfin.

### 3. Les sept prochains jours passent en grille

Le §5.4 dit « inspiré d'Apple Calendar » et la maquette montre un segment « Mois · Semaine · Jour ». La vue livrée était une **liste de jours empilés** : on ne lit pas une semaine en la faisant défiler. Elle devient une grille de sept colonnes, même grammaire visuelle que la grille du mois. Le §5.4 a été précisé en conséquence.

### 4. Un projet a sa vue calendrier

Le filtre du calendrier principal (§5.4) répond à « qu'est-ce qui arrive cette semaine, tous sujets confondus ». Il ne répond pas à « où en est ce projet dans le temps », qui demande de ne voir **que** lui. Le §5.3 a été précisé : la vue calendrier du projet est une lecture de plus sur les mêmes occurrences, filtrée sur le calendrier du projet.

**Aucune entité, aucun champ, aucune migration** pour les quatre.
