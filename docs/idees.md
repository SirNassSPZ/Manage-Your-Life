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

## I-002 — Intégration Gmail (boîte mail dans l'app) — **hors périmètre de tout le projet (V1/V2/V3)**
**Consigné le 2026-07-24** · demandé par l'utilisateur pendant la maquette de l'onglet Finances.

Demande : intégrer **Gmail** dans l'application — accéder à sa boîte mail et afficher l'interface Gmail à l'intérieur du SaaS.

**Statut de périmètre.** Absent de la spécification (aucune mention dans les versions V1, V2 ou V3, §13). C'est un **domaine entièrement différent** (un client e-mail) qui sort de la mission « finances + organisation personnelle ». Implications lourdes : OAuth Google (une 2ᵉ pile d'authentification distincte d'Entra ID), l'API Gmail, une UI d'e-mail complète, et des enjeux de confidentialité (lecture de la boîte mail). Cela **redéfinirait ce qu'est le produit** et retarderait tout. **Ne pas coder** — décision de périmètre à trancher par l'utilisateur (et, si retenu un jour, à cadrer dans une version dédiée, pas la V1).

**Besoin sous-jacent à clarifier.** Souvent, « je veux Gmail » cache un besoin précis : *capturer les factures/reçus reçus par e-mail dans mes finances*. Si c'est ça, des voies **plus légères et dans le périmètre** existent : pièce jointe = photo/PDF d'une facture (§7, en cours) ; V3 = conversion d'une note en Élément typé. À confirmer avec l'utilisateur avant toute décision.

## I-003 — Envie d'achat : intégration/confrontation au budget projeté — **prévu V2 par la spec (§5.1, §13)**
**Consigné le 2026-07-24** · demandé par l'utilisateur pendant la maquette de l'onglet Finances.

Demande : pour chaque **envie d'achat**, pouvoir soit **l'intégrer au budget projeté du mois souhaité** (« est-ce que ça rentre en septembre ? ») soit **la laisser en suspens**, sans l'affecter à aucun budget.

**Statut de périmètre.** La spec place la **confrontation des envies au budget projeté** explicitement en **V2** (§5.1 : « envies d'achat par catégories, **confrontables** au budget projeté » ; §13 : « liste d'achats **confrontée au budget** » = V2). Donc la simulation « intégrer cette envie au mois M et voir l'impact sur la clôture projetée » est **V2, pas V1**.

**Ce qui EST déjà en V1 (le modèle le permet sans rien ajouter) :**
- L'`envie` est un type d'Élément de plein droit (§3.1), avec ses statuts `idee` (≈ *en suspens*), `planifiee`, `faite`, `abandonnee`. « Laisser en suspens » vs « planifier » se joue donc **déjà** sur le statut, en V1.
- Une envie peut porter un **montant** et des **catégories** → une simple **liste de souhaits classée** est montrable en V1 (c'est ce que fait la maquette).
- Si l'utilisateur **décide d'acheter**, il crée une **sortie datée** (paiement) au mois voulu : elle entre alors **naturellement** dans le budget projeté (§5.1) — sans mécanisme spécifique « envie ».

**Ce qui reste V2 :** le geste dédié « projeter *hypothétiquement* cette envie sur le mois M sans créer de vraie dépense, et afficher si ça passe » (la confrontation), et le rappel intelligent des envies (§5.2). À NE PAS coder en V1. La maquette le montre donc **étiqueté V2**.
