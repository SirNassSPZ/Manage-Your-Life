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
- **Function App Windows, plan Consommation Y1**, .NET 8 isolé, identité managée système ; **stockage du runtime par identité** (`AzureWebJobsStorage__accountName`, aucune clé de compte en clair) — d'où les rôles Blob Data Owner + Queue Data Contributor.
- **Blob** : conteneur `pieces-jointes` privé (`allowBlobPublicAccess = false`, §7 : SAS uniquement). **Key Vault** en mode RBAC, rôle *Secrets User* pour l'app (Étape 3).
- **Journalisation** : Log Analytics PerGB2018 + Application Insights (workspace) avec échantillonnage — sous la franchise permanente de 5 Go/mois d'ingestion, coût attendu nul à notre échelle.
- **Nommage** : `rg-dc-{env}`, suffixe `uniqueString(abonnement, env)` pour les noms globaux (storage, SQL, Key Vault, Functions) — reproductible et sans collision.
