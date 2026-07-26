# Idées hors périmètre V1

> Consigne (CLAUDE.md) : toute idée hors périmètre V1 est consignée ici, **jamais codée** sans décision.
> Format : une entrée par idée, avec la date et le contexte d'origine.

## I-001 — Projets personnels complets (module) — ~~V2~~ → **ENTRÉ EN V1 le 2026-07-26 (D-027)**

> **Décision de périmètre prise par l'utilisateur.** La spec a été modifiée en conséquence : §5.3 passe en V1, §5.2 distingue la tâche **de projet** (V1) de la tâche hors projet (V2), §5.4 rend les calendriers de projets automatiques dès la V1, §13 est à jour. Ce qui suit est le texte d'origine, conservé pour la trace.

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

## I-003 — Envie d'achat : confrontation au budget projeté — ~~V2~~ → **ENTRÉE EN V1 le 2026-07-26 (D-027)**

> **Décision de périmètre prise par l'utilisateur.** La spec a été modifiée : §5.1bis définit l'algorithme officiel (dans l'API, jamais stocké), §3.1 autorise un `montant_centimes` **facultatif** sur une envie, §8 ajoute la route. **Q-002 est renversée** — voir D-027 pour le motif, et le correctif ci-dessous qui ne vaut plus. Ce qui suit est le texte d'origine, conservé pour la trace.

**Consigné le 2026-07-24** · demandé par l'utilisateur pendant la maquette de l'onglet Finances.

Demande : pour chaque **envie d'achat**, pouvoir soit **l'intégrer au budget projeté du mois souhaité** (« est-ce que ça rentre en septembre ? ») soit **la laisser en suspens**, sans l'affecter à aucun budget.

**Statut de périmètre.** La spec place la **confrontation des envies au budget projeté** explicitement en **V2** (§5.1 : « envies d'achat par catégories, **confrontables** au budget projeté » ; §13 : « liste d'achats **confrontée au budget** » = V2). Donc la simulation « intégrer cette envie au mois M et voir l'impact sur la clôture projetée » est **V2, pas V1**.

**Ce qui EST déjà en V1 (le modèle le permet sans rien ajouter) :**
- L'`envie` est un type d'Élément de plein droit (§3.1), avec ses statuts `idee` (≈ *en suspens*), `planifiee`, `faite`, `abandonnee`. « Laisser en suspens » vs « planifier » se joue donc **déjà** sur le statut, en V1.
- Une envie peut porter des **catégories** → une simple **liste de souhaits classée** est montrable en V1.

> **Correction (2026-07-25, Q-002).** Ce paragraphe affirmait aussi qu'une envie pouvait porter un **montant**. C'est **faux** : le §3.1 réserve l'argent aux types `facture`, `paiement` et `revenu`, et le cœur le fait respecter (`montant_interdit`). La maquette, qui affichait des prix sur les envies, a été corrigée. Un prix se matérialise le jour où l'on décide d'acheter, sous forme de **sortie datée** — laquelle entre naturellement dans le budget projeté.
- Si l'utilisateur **décide d'acheter**, il crée une **sortie datée** (paiement) au mois voulu : elle entre alors **naturellement** dans le budget projeté (§5.1) — sans mécanisme spécifique « envie ».

**Ce qui reste V2 :** le geste dédié « projeter *hypothétiquement* cette envie sur le mois M sans créer de vraie dépense, et afficher si ça passe » (la confrontation), et le rappel intelligent des envies (§5.2). À NE PAS coder en V1. La maquette le montre donc **étiqueté V2**.

## I-004 — Onglet « to-do list », listes personnalisables, planifications — **V2 par la spec (§5.2, §5.3, §13)**
**Consigné le 2026-07-25** · demandé par l'utilisateur pendant l'Étape 4f (vue Finances).

Demande, en quatre points : (1) catégoriser les éléments des onglets en colonnes pliables ou navigables ; (2) une vue calendrier mensuelle **et** une vue 7 jours ; (3) pouvoir personnaliser catégories, listes et planifications ; (4) un onglet **to-do list** permettant de créer des listes et de les catégoriser.

**Ce qui est DÉJÀ dans le périmètre V1 — donc codable tout de suite :**
- **La vue 7 jours du calendrier.** Le §5.4 fait du calendrier unifié un module V1 « inspiré d'Apple Calendar », et la maquette montre le segment « Mois · Semaine · Jour ». C'est un **mode d'affichage** des mêmes occurrences : aucun champ, aucune entité, aucune règle de synchro en plus. `ServiceAujourdhui.ProchainsJours(maintenant, 7)` existe déjà et est testé.
- **La personnalisation des catégories.** Le §3.3 définit la catégorie (nom, couleur, origine) et en fait le filtre du calendrier ; I-001 note déjà que « créer des catégories et les activer/désactiver comme filtres » **est** en V1. Il n'existe pourtant aujourd'hui **aucun écran** pour en créer, renommer ou recolorier une, ni pour en affecter une à un Élément. C'est un manque V1 réel, pas une extension.
- **Le groupement pliable par catégorie** dans les listes existantes (Finances, Calendrier) : de la mise en forme sur des données déjà là.

**Ce qui est HORS périmètre V1 :**
- **Les tâches / la to-do list.** Le §13 énumère la saisie V1 : « facture, paiement, revenu, rendez-vous, note » — **la tâche n'y est pas**. Le §5.2 place « Tâches : priorité + ordre manuel » en **V2**, et le score en V2. Le type `tache` existe bien dans le modèle (§3.1) et le schéma dès la V1 — c'est voulu (stabilité du schéma), mais le **module** est V2.
- **Les « listes » à créer et catégoriser.** La notion la plus proche dans la spec est le **Projet** (§5.3 : tâches propres, label, calendrier dédié devenant filtre automatique), explicitement **V2** — c'est déjà I-001. Une notion de « liste » distincte du projet n'existe nulle part dans la spec : l'introduire demanderait une entité de plus, à écrire **à l'identique dans les deux apps** (risque n° 1).
- **Les planifications / templates de planning.** §5.2 : **V2**.

**Coût réel d'un élargissement.** Tout ce qui entre en V1 est écrit **deux fois** (C# et Swift) et doit passer les scénarios de parité du §12 avant livraison. Ajouter tâches + listes + planifications, c'est un module entier de plus des deux côtés — c'est précisément ce que le découpage en versions cherche à éviter (« livrer un socle qui a de la valeur seul, puis empiler »).

**Décision requise (périmètre — appartient à l'utilisateur) :** livrer d'abord la V1 telle que définie, en y incluant les trois points ci-dessus qui en font déjà partie ; **ou** élargir formellement la V1 au module tâches/listes — ce qui exige de **modifier la spécification d'abord** (CLAUDE.md), et d'accepter le délai et le risque supplémentaires.

*Réponse (2026-07-25) : livrer la V1 d'abord. La vue 7 jours et la gestion des calendriers sont faites ; le groupement pliable reste à faire.*

*Suite (2026-07-26) : **les trois points V1 sont livrés.** Le groupement pliable est en place dans Finances, sous-vue « Par catégorie » (D-025) — elle était déclarée dans la barre latérale mais retombait sur « Tout ». Le Calendrier n'est volontairement pas groupé par catégorie : il a déjà cet axe par ses filtres (§5.4) et ses deux lectures sont organisées par jour ; superposer les deux mettrait deux axes en concurrence dans la même vue. **Il ne reste donc de I-004 que ce qui est hors périmètre V1** : tâches, listes, planifications.*

## I-005 — Notes à la hauteur de Notion : favoris, mise en forme riche, images — **hors périmètre V1 (§5.5)**
**Consigné le 2026-07-26** · demandé par l'utilisateur après la livraison de la vue Notes.

Demande, en trois points : (1) chaque onglet doit avoir **sa propre colonne latérale**, au lieu de subir les filtres du calendrier partout ; (2) pour Notes, une colonne à la Notion — voir toutes les notes, et les **favorites** épinglées en haut ; (3) une édition **aussi complète que Notion** — gras, italique, « clés » (titres / blocs), **images incorporées**.

**Point 1 — ce n'était pas une envie, c'était un défaut.** Les filtres de calendrier s'affichaient dans **toutes** les zones, y compris Notes et Corbeille où ils ne filtrent rien. Le §5.4 les rattache au calendrier. **Corrigé** : ils n'apparaissent que là où ils agissent réellement.

**Point 2 — les favoris n'existent pas dans le modèle.** Le §3.1 énumère les champs de l'Élément ; il n'y a **aucun** `favori`, et aucune notion d'épinglage. Deux voies :
- **(a) Sans rien ajouter** — une catégorie « Favoris » fait déjà le travail : catégorie = label (§3.3), elle est créable depuis « Gérer les calendriers », et trier les notes par appartenance à cette catégorie est de la **présentation pure**. Zéro champ, zéro migration, zéro règle à écrire deux fois. **Faisable en V1.**
- **(b) Un vrai champ `favori`** — additif (règle 18), donc sans migration destructive, mais c'est un champ de plus dans le modèle canonique, le JSON, les deux apps et les tests de parité. Exige de **modifier le §3.1 d'abord**.

**Point 3 — la mise en forme riche contredit le §5.5, littéralement.** Le §5.5 dit : « espace de texte libre, **sans structure imposée** », « une note = Élément `type = note` (**texte** dans `description`) », et conclut « **En V1 : simple espace texte, aucune intelligence** ». Passer à Notion signifie :
- **un format de contenu** (Markdown ? blocs JSON ?) là où la spec dit « texte ». C'est un changement du **modèle de données**, pas de l'interface — le `description` d'aujourd'hui est lu tel quel par l'export (§5.7), qui promet d'être « lisible sans l'application » ;
- **un éditeur riche écrit deux fois**, en WinUI et en SwiftUI, avec un comportement identique au caractère près (risque n° 1). C'est le composant le plus coûteux de toute l'application ;
- **les images incorporées** : le §7 gère bien des pièces jointes (25 Mo, SAS, cache local), mais **attachées à un Élément**, pas insérées dans un flux de texte. Une image dans le corps d'une note demande de lier un blob à une position dans le contenu — une notion absente de la spec.

**Ce qui est faisable en V1 sans toucher à la spec :** la colonne latérale propre à chaque onglet (point 1, fait), la liste des notes dans cette colonne façon Notion, et le tri « favoris en haut » par la voie (a). Le reste du point 3 est une **V2 à part entière**.

**Décision requise (périmètre) :** se contenter de la voie (a) pour les favoris et garder la note en texte simple jusqu'à la V1 livrée ; **ou** ouvrir un chantier « éditeur riche » — qui exige de réécrire le §5.5, de choisir un format de contenu, et d'accepter qu'il soit implémenté deux fois.
