---
name: garde-fou-synchro
description: Règles obligatoires de synchronisation local-first du projet Deuxième Cerveau. This skill should be used whenever work touches saving or editing user data, the local database, the outbox, push or pull, sync cursors, conflict resolution, deletion or the trash, or offline behaviour — in the API, the Windows app, or the Apple app. Also applies when writing or reviewing tests for any of these.
---

# Synchronisation local-first — garde-fou

Cette couche est écrite **deux fois** (Swift et C#). C'est le morceau le plus risqué du projet : une divergence entre les deux implémentations provoque des pertes de données silencieuses.

## Avant d'écrire une seule ligne

**Lire `docs/specification.md` §6 en entier** (Synchronisation multi-appareils), plus §5.6 (Corbeille) et §5.7 (Export) si la tâche les touche.

La spec est la source unique. **Ne jamais implémenter la synchro d'une app en s'inspirant du code de l'autre app** : les deux implémentations doivent dériver du même texte, sinon elles divergent lentement. Si un comportement semble manquer dans la spec, s'arrêter et demander — ne pas improviser.

## Les cinq règles jamais violées

1. **Écriture locale d'abord.** Toute saisie va dans la base locale immédiatement, avant tout réseau. La confirmation à l'utilisateur = l'écriture locale. Jamais d'écriture directe au serveur à la saisie.
2. **Rien n'est jamais effacé.** Une suppression met `supprime = true`. Seule exception : la purge manuelle depuis la corbeille (§5.6).
3. **Le perdant d'un conflit est archivé.** L'API arbitre (dernière écriture gagne sur `date_modification` UTC) ; la version écartée part au `change_log`, jamais à la poubelle.
4. **Push idempotent et atomique.** Un `change_id` déjà vu est ignoré silencieusement. Un lot s'applique entièrement ou pas du tout.
5. **Pull par curseur.** `server_seq` est posé par le serveur, jamais par le client. Le curseur est le point de reprise après coupure.

## Signaux d'alerte

Si le code en cours d'écriture fait l'une de ces choses, **s'arrêter** :

- attendre une réponse réseau avant de confirmer une saisie à l'utilisateur ;
- un `DELETE` SQL sur `elements`, `categories`, `projets` ou `budgets` ;
- écraser une version en conflit sans écrire au `change_log` ;
- rejouer un lot sans vérifier `change_id` ;
- générer `server_seq` côté client ;
- une outbox en mémoire seulement (elle doit survivre au redémarrage).

## Vérification avant de conclure

Les scénarios de parité §12 concernés doivent passer, en particulier : saisie hors-ligne puis reconnexion ; modification concurrente sur deux appareils ; suppression puis restauration ; coupure réseau en plein push sans doublon.
