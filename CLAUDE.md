# CLAUDE.md — Deuxième Cerveau

## Mission
Construire l'application personnelle « Deuxième Cerveau » : deux applications natives (Windows en .NET/WinUI, Apple en SwiftUI) synchronisées via une API centrale sur Azure. **Périmètre : V1 uniquement**, telle que définie dans la spécification.

## Document de référence
- La spécification complète est dans `docs/specification.md`. **La lire intégralement avant d'écrire la moindre ligne de code.**
- **La spec fait foi sur le code.** Si un comportement doit changer : modifier la spec d'abord, signaler le changement, puis coder.
- Les 18 règles NON NÉGOCIABLES (§14 de la spec) s'appliquent **à l'identique dans les deux apps**. Le risque n° 1 du projet est la divergence silencieuse entre l'app Windows et l'app Apple — la spec existe pour l'empêcher.
- Toute idée hors périmètre V1 : la consigner dans `docs/idees.md`, **ne pas la coder**.
- Toute décision technique non couverte par la spec : la consigner dans `docs/decisions.md` et la soumettre à validation avant de continuer.

## Structure du dépôt
```
/docs               specification.md, idees.md, decisions.md
/core               bibliothèque métier .NET — AUCUNE dépendance Azure (règle 4)
/api                Azure Functions (adaptateurs autour de /core)
/apps/windows       app native WinUI (.NET)
/apps/apple         app native SwiftUI (compilation sur Mac/Xcode)
/infra              Bicep (infrastructure as code)
/.github/workflows  CI + déploiement
```

## Ordre de construction V1 — avec points d'arrêt
**S'arrêter à la fin de chaque étape et demander une validation humaine avant de passer à la suivante.**

### Étape 1 — Cœur métier (`/core`)
Modèle d'Élément (§3), algorithme du budget projeté (§5.1), arbitrage des conflits et idempotence (§6.2), validations (statuts §3.1, `budget_id` §3.6), migrations de schéma (§9). Tests exhaustifs + golden files (§12) dans `/core/tests`.
**Fini quand :** tous les tests passent, y compris les cas tordus — mois négatifs en cascade, RRULE « dernier jour du mois » et « tous les 2 mois », changements d'heure, double envoi d'un même lot (idempotence).

### Étape 2 — Infrastructure (`/infra`)
Bicep : groupe de ressources dev (`rg-dc-dev`), **alerte de budget créée en premier**, SQL serverless, Storage, Key Vault, Functions. Région France Central ou West Europe. Paliers gratuits/serverless uniquement (§10) — toute ressource facturable est interdite sauf décision documentée.
**Fini quand :** déploiement reproductible via GitHub Actions, une fonction « ping » répond en ligne.

### Étape 3 — API (`/api`)
Le contrat §8 exactement, branché sur `/core`. Schéma SQL §9 + migrations numérotées. Authentification Entra ID (MSAL) — **l'intégrer maintenant, pas à la fin** : c'est l'une des parties les plus délicates du projet.
**Fini quand :** les tests de contrat passent contre l'instance dev déployée — push idempotent (renvoyer deux fois le même lot = même état), pull par curseur, projection conforme aux golden files.

### Étape 4 — App Windows (`/apps/windows`)
V1 complète : base locale + outbox, saisie typée, calendrier avec filtres, note libre, corbeille, pièces jointes, export/import local, notifications locales, synchro complète — **§6 implémenté mot pour mot**.
**Fini quand :** les scénarios de parité exécutables sur un seul appareil passent — saisie hors-ligne puis reconnexion, coupure réseau en plein push sans doublon, export réseau coupé puis import sur installation vierge (contenu identique, corbeille comprise).

### Étape 5 — App Apple (`/apps/apple`)
Fonctionnellement identique, en suivant **la spec** mot pour mot (pas le code Windows comme référence pour la synchro : la source est §6, afin que les deux implémentations dérivent du même texte). Compilation et tests sur Mac/Xcode.
**Fini quand :** mêmes scénarios solo que l'étape 4.

### Étape 6 — Parité croisée
Dérouler tous les scénarios du §12 entre les deux apps réelles.
**Fini quand :** état final identique partout, perdants de conflit présents au journal, aucun doublon après coupures, export/import croisé vérifié.

## Règles d'exécution
- **Périmètre V1 verrouillé.** Rien de V2/V3 (§13), même si « c'est facile à ajouter ».
- **Environnements séparés :** `rg-dc-dev` pour développer et tester ; `rg-dc-prod` créé seulement une fois l'étape 6 validée. Ne jamais tester contre la prod.
- **Secrets :** jamais dans le code ni dans git. En local : user-secrets / `.env` ignoré par git. Déployé : Key Vault + Managed Identity (règle 16).
- **Migrations :** numérotées, additives uniquement, appliquées au démarrage, identiques sur Azure SQL et les bases locales (règle 18).
- **Pièges connus :**
  - Palier gratuit d'App Service interdit (1 h de calcul/jour = API morte le reste de la journée) ; l'API vit sur Functions, plan Consommation.
  - Reprise SQL serverless = latence de quelques secondes au premier appel après pause : prévoir un retry côté API ; l'UI locale n'attend de toute façon jamais le serveur (local-first).
  - Ne jamais stocker les données dérivées — projection budgétaire et suivi des budgets se calculent à la lecture (règle 9).
  - Argent en centimes entiers, jamais de flottants (règle 5). Récurrences en RRULE expansées dans le fuseau de l'Élément (règle 6).

## Definition of Done V1
Les scénarios de parité §12 passent sur les deux apps réelles ; export → import vérifié ; alerte de budget active ; aucune des 18 règles §14 violée ; `docs/decisions.md` à jour.
