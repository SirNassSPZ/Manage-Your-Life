---
name: garde-fou-architecture
description: Règles d'architecture et de structure du code du projet Deuxième Cerveau — isolation du cœur métier, adaptateurs Azure, répartition serveur/client, testabilité. This skill should be used when creating projects or files, deciding where a piece of logic belongs, adding a dependency or package, writing anything in /core or /api, touching Azure services (Functions, SQL, Blob, Key Vault, Entra ID), or writing tests for business logic.
---

# Architecture du code — garde-fou

Deux propriétés à préserver en permanence : le cœur métier reste **indépendant d'Azure** (portabilité), et la logique reste **au serveur** (non-divergence des deux apps).

## Avant de décider où va un morceau de code

**Lire `docs/specification.md` §4** (Répartition de la logique & isolation du cœur métier), et §11 (Portabilité) si la tâche touche à un service Azure.

## Règle 1 — Le cœur n'importe aucune bibliothèque Azure

`/core` contient la logique métier pure : modèle d'Élément, algorithme du budget projeté, suivi des budgets, arbitrage des conflits, validations, migrations. **Aucun `using Azure.*`, aucun SDK cloud, aucun accès réseau, aucune chaîne de connexion.**

`/api` contient les adaptateurs : Functions, accès SQL, Blob, Key Vault, Entra ID. Les adaptateurs appellent le cœur ; le cœur n'appelle jamais un adaptateur.

Test de validation : *ce code métier compilerait-il et passerait-il ses tests sans aucune ressource Azure ?* Si non, il est au mauvais endroit.

## Règle 2 — Les apps affichent et saisissent ; l'API décide

Vit dans l'API (écrit **une fois**) : projections budgétaires, suivi des enveloppes, arbitrage des conflits, validations, scoring.

Vit dans les apps (écrit **deux fois**, donc à minimiser) : base locale et outbox, vues et filtres, formulaires, synchro, notifications locales, export.

Avant de placer de la logique dans une app, se demander : *devra-t-elle être réécrite à l'identique en Swift et en C# ?* Si oui et qu'elle pourrait vivre au serveur, la déplacer.

Exceptions autorisées par la spec : expansion des RRULE **pour l'affichage seulement**, et l'export (§5.7) qui doit fonctionner sans réseau.

## Règle 3 — Ce qui ne se stocke jamais

Les données dérivées se calculent à la lecture : projection budgétaire, suivi des budgets, scores. Aucune colonne, aucun cache persistant pour ces valeurs.

## Règle 4 — Configuration et secrets

L'adresse de l'API est un **paramètre de configuration** dans chaque app, jamais codée en dur. Aucun secret dans le code ni dans git : user-secrets en local, Key Vault + Managed Identity en déployé.

## Signaux d'alerte

- un import Azure dans `/core` ;
- un calcul de solde ou de projection dupliqué dans une app ;
- une colonne SQL qui stocke un total, un solde projeté ou un score ;
- une URL d'API ou une chaîne de connexion en dur ;
- un test de logique métier qui exige une ressource Azure pour tourner.
