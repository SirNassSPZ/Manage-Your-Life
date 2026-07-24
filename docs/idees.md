# Idées hors périmètre V1

> Consigne (CLAUDE.md) : toute idée hors périmètre V1 est consignée ici, **jamais codée** sans décision.
> Format : une entrée par idée, avec la date et le contexte d'origine.

## I-001 — Projets personnels complets (module) — **prévu V2 par la spec (§5.3, §5.4)**
**Consigné le 2026-07-24** · demandé par l'utilisateur pendant la maquette d'interface (Étape 4f).

Vision de l'utilisateur (à réaliser telle quelle) : des **projets personnels** — ex. « commencer le sport à la salle », « commencer le MMA », « étudier pour les examens », « arrêter de fumer »… Chaque projet :
- porte ses **tâches propres** et un **label** ;
- possède son **calendrier dédié** ;
- ce calendrier de projet devient **automatiquement un filtre** affichable/masquable dans le calendrier principal (onglet à part entière), à côté des catégories.

**Statut de périmètre.** C'est **exactement** le §5.3 (« Tâches propres, label, calendrier dédié devenant filtre automatique du calendrier principal ») et le §5.4 (« Les calendriers de projets s'y ajoutent automatiquement en V2 ») — donc **V2**, pas V1. Les tâches à priorité + ordre manuel sont aussi V2 (§5.2).

**Déjà en place pour préparer la V2 :** l'entité `Projet` existe dans le modèle et le schéma dès la V1 (§3.2), et la **fermeture de projet** (report en cascade des tâches) est implémentée dans le cœur. Donc la V2 « projets » se branchera proprement, sans migration de schéma.

**Déjà disponible en V1 (partie de la demande qui EST dans le périmètre) :** créer des **catégories** de calendrier et les activer/désactiver comme **filtres** dans le calendrier principal, façon Apple (§5.4). C'est ce que montre la maquette (section « Calendriers » de la barre latérale).

**Décision requise (périmètre — appartient à l'utilisateur) :** garder « projets » en V2 (recommandé : livrer la V1 d'abord) **ou** décider d'élargir formellement la V1 pour l'inclure — ce qui exige de modifier la spec d'abord (CLAUDE.md), et d'accepter le délai + le risque supplémentaires (fonctionnalité à écrire à l'identique dans les deux apps).
