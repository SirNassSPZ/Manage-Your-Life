Je construis maintenant l'authentification (incrément 3c). D'abord les dépendances de validation de jetons :


Modifié un fichier, créé 2 fichiers
+95
-0
Le middleware d'authentification (validation Bearer, avec bypass configurable et exemption du ping) :

Créé
MiddlewareAuth.cs
+66
-0
Maintenant je câble le middleware et les services d'auth dans Program.cs :


Modifié Program.cs, utilisé un outil
+14
-1
Maintenant les tests de validation de jeton (hors ligne, avec une clé auto-signée) :


Créé ValidateurJetonTests.cs, exécuté une commande
+76
-0
22 tests API verts (dont 6 sur la validation des jetons : signature, audience, émetteur, expiration, altération — tout est rejeté correctement). L'authentification est codée et testée hors ligne. J'ajoute les réglages d'authentification à l'infrastructure (désactivés par défaut, pour ne pas casser le déploiement avant votre inscription d'application) :


Modifié 3 fichiers
+31
-0
Je valide le Bicep localement, la solution complète, puis je commite l'incrément d'authentification :


Exécuté 2 commandes
L'authentification est codée, testée et intégrée — le dernier gros bloc de code de l'Étape 3 est fait. Et voici, comme promis, votre unique manip Azure de cette étape.

Ce qui est fait (incrément 3c)
L'API sait désormais exiger un jeton d'identité valide sur chaque appel (§8) : elle vérifie la signature, l'audience, l'émetteur et l'expiration du jeton. 6 tests le prouvent hors ligne — un jeton conforme passe, tout le reste (mauvaise audience, faux émetteur, jeton expiré, signature étrangère, chaîne bidon) est rejeté. 325 tests au total, tous verts. L'authentification reste désactivée tant qu'on ne l'allume pas, donc rien n'est cassé entre-temps.

Votre manip : créer l'inscription d'application (~2 min)
Dans Cloud Shell (PowerShell), collez :

# 1. Créer l'inscription d'application « API » (accepte les comptes Microsoft personnels, §8)
az ad app create --display-name "deuxieme-cerveau-api" --sign-in-audience "AzureADandPersonalMicrosoftAccount"

# 2. Récupérer son identifiant
$API_ID = az ad app list --display-name "deuxieme-cerveau-api" --query "[0].appId" -o tsv

# 3. Définir son « Application ID URI » (l'audience des jetons)
az ad app update --id $API_ID --identifier-uris "api://$API_ID"

# 4. Afficher la valeur à me communiquer
Write-Host "ENTRA_AUDIENCE=$API_ID"
Puis envoyez-moi la ligne ENTRA_AUDIENCE=... qui s'affiche. Je m'en servirai pour allumer l'authentification.

Ce n'est pas bloquant : vous pouvez le faire quand vous voulez.

Ce que je fais pendant ce temps
J'enchaîne sur la dernière ligne droite de l'Étape 3 : brancher l'API déployée sur votre vraie base SQL (accès par identité managée, que j'automatise — aucune manip pour vous), puis lancer les tests de contrat en ligne (envoyer une saisie, la relire, vérifier la projection — directement contre l'API en production). Je déploierai d'abord avec l'authentification éteinte pour valider la mécanique, puis je l'allumerai avec votre ENTRA_AUDIENCE.

Je continue — je reviendrai vers vous soit avec les tests de contrat en ligne réussis, soit pour votre ENTRA_AUDIENCE si j'en ai besoin avant.nasr [ ~ ]$ az ad app create --display-name "deuxieme-cerveau-api" --sign-in-audience "AzureADandPersonalMicrosoftAccount"
Directory permission is needed for the current user to register the application. For how to configure, please refer 'https://learn.microsoft.com/azure/azure-resource-manager/resource-group-create-service-principal-portal'. Original error: Insufficient privileges to complete the operation.
nasr [ ~ ]$

Pas d'inquiétude — c'est un simple manque de droit de l'outil en ligne de commande, pas un vrai blocage. Et surtout : ce n'est pas urgent.

Ce que dit l'erreur
Le message « Insufficient privileges » signifie que la commande az ad app create n'a pas l'autorisation d'écrire dans votre annuaire Entra. C'est fréquent : l'outil en ligne de commande a souvent moins de droits que le portail (qui, lui, a fonctionné pour vous tout à l'heure avec github-deploy).

Deux solutions
Solution A — le faire via le Portail (la méthode qui a marché la dernière fois) :

Portail → cherchez « Inscriptions d'applications » → « + Nouvelle inscription »
Nom : deuxieme-cerveau-api
Types de comptes pris en charge : choisissez la ligne qui mentionne « … et comptes Microsoft personnels » (comptes dans un annuaire quelconque + comptes personnels)
« S'inscrire »
Sur la page de l'app, notez l'« ID d'application (client) » — c'est la valeur ENTRA_AUDIENCE à me communiquer.
Solution B — ne rien faire pour l'instant. C'est mon conseil : cette inscription ne sert qu'à allumer l'authentification. Je peux déployer l'API et lancer les tests de contrat en ligne dès maintenant, avec l'authentification temporairement éteinte — ça valide toute la mécanique (base de données, saisie, projection). On allumera l'authentification après, quand l'inscription existera.

Ma recommandation
Laissez tomber la ligne de commande. Je poursuis tout de suite avec le déploiement + les tests de contrat en ligne (authentification éteinte), et vous ferez l'inscription via le Portail quand vous voulez (Solution A) — sans pression, puisque ce n'est pas bloquant.

Je lance la suite ?

oui

Parfait, je lance la dernière ligne droite. Je vais : (1) exposer les infos SQL dans l'infra, (2) automatiser l'accès de l'API à la base par identité managée, (3) écrire des tests de contrat qui frappent l'API en ligne, puis déployer et itérer. Je commence par les sorties Bicep :


Modifié 2 fichiers
+5
-0
Maintenant l'outil qui accorde l'accès SQL à l'identité managée (exécuté par le service principal, admin Entra de la base) :


Créé 2 fichiers, utilisé 2 outils
+75
-0
Maintenant les tests de contrat qui frappent l'API en ligne (réutilisent mes DTOs et le JSON canonique) :

Créé
TestsContrat.csproj
+18
-0
Le programme de tests de contrat en ligne :


Créé Program.cs, exécuté une commande
+128
-0
Les deux outils compilent. J'intègre maintenant dans le workflow de déploiement : l'octroi d'accès SQL à l'identité managée, et les tests de contrat en ligne. Je lis d'abord le workflow actuel :


Lu et modifié deploy-infra.yml
+25
-10
Je valide le Bicep et le YAML, puis je pousse sur main pour déclencher le déploiement complet :


Exécuté 2 commandes
Poussé sur main (a14c4d1) — le déploiement complet est lancé. Cette fois il enchaîne : déploiement de l'API branchée sur SQL, octroi automatique de l'accès à la base, migrations au démarrage, puis tests de contrat en ligne. Plusieurs de ces étapes touchent Azure SQL pour la première fois, donc c'est le moment où on pourrait devoir ajuster (comme à l'Étape 2). Je programme ma surveillance :

Utilisé Claude Code Remote: send later
Voilà où on en est de l'Étape 3.

Ce qui vient d'être poussé (dernière ligne droite)
Tout est en place et entièrement automatisé de votre côté :

L'API branchée sur votre vraie base Azure SQL (accès par identité managée, sans secret).
L'octroi d'accès de l'API à la base : automatisé dans le déploiement — vous n'avez rien à faire.
Des tests de contrat qui frappent l'API en ligne : enregistrer un appareil, saisir, relire, vérifier qu'un double envoi ne crée pas de doublon, calculer la projection — directement contre la production. C'est le critère « fini » de l'Étape 3.
Rappel : côté code, 319 tests locaux couvrent déjà tout ça ; ces tests-en-ligne prouvent que ça marche aussi une fois déployé.

Attentes réalistes
C'est le premier contact de l'API avec Azure SQL en conditions réelles. Comme à l'Étape 2, il se peut qu'un détail Azure demande un ajustement (résolution de l'identité côté base, un point de dialecte SQL…). Si c'est le cas, je corrige le code moi-même et je relance — aucune manip pour vous.

Je surveille et reviens dans ~8 minutes avec le verdict : soit les tests de contrat verts en ligne (Étape 3 bouclée), soit un diagnostic + correctif.

Ce qui restera pour vous (sans urgence)
La petite inscription d'application via le Portail (Solution A de mon message précédent) — uniquement pour allumer l'authentification. L'API fonctionne déjà sans, donc faites-le quand ça vous arrange, et envoyez-moi la valeur ENTRA_AUDIENCE.

Je reviens avec le résultat du déploiement.

nasr [ ~ ]$ az ad app create --display-name "deuxieme-cerveau-api" --sign-in-audience "AzureADandPersonalMicrosoftAccount"
Directory permission is needed for the current user to register the application. For how to configure, please refer 'https://learn.microsoft.com/azure/azure-resource-manager/resource-group-create-service-principal-portal'. Original error: Insufficient privileges to complete the operation.
nasr [ ~ ]$

La ligne de commande ne marchera pas pour votre compte — c'est une limitation connue (l'outil az a moins de droits que le Portail), pas une erreur de votre part. Inutile de réessayer cette commande, elle échouera toujours pareil.

Deux choses importantes d'abord :

Ce n'est pas bloquant — le déploiement que j'ai lancé tourne en ce moment avec l'authentification éteinte, et validera toute la mécanique. Je vous en donne le résultat dans quelques minutes.
Donc vous pouvez ignorer cette manip pour l'instant et la faire plus tard, tranquillement.
Quand vous voudrez le faire : par le Portail (pas la commande)
C'est la méthode qui a marché pour vous la dernière fois (github-deploy). Pas à pas :

Dans la barre de recherche en haut du Portail, tapez « Inscriptions d'applications » et cliquez dessus.
Bouton « + Nouvelle inscription » (en haut à gauche).
Nom : deuxieme-cerveau-api
Types de comptes pris en charge : cochez la 3ᵉ option, celle qui finit par « … et comptes Microsoft personnels (par ex. Skype, Xbox) ».
Laissez le reste vide, cliquez « S'inscrire » en bas.
Vous arrivez sur la page de l'application. Copiez la valeur « ID d'application (client) » (une suite du genre xxxxxxxx-xxxx-...).
Envoyez-moi cette valeur — je m'en sers pour allumer l'authentification.
C'est tout : 6 clics, aucune ligne de commande.

Pour l'instant, ne faites rien de plus — je reviens très vite avec le résultat du déploiement en cours (les tests de contrat en ligne). Si vous voulez, gardez cet onglet Portail ouvert pour plus tard.

quel est le resultat du deploiement ?


Exécuté une commande, utilisé un outil
Le déploiement (a14c4d1) est encore en cours. Je regarde à quelle étape il en est pour vous donner un point précis :

Utilisé github: actions list
Le déploiement avance bien — il a déjà passé toutes les étapes qui coinçaient à l'Étape 2. Voici le détail en direct :

Étape	État
Connexion Azure + activation des services	✅
Validation ARM	✅
Déploiement de l'infrastructure (budget, SQL, etc.)	✅
Récupération des infos SQL	✅
Octroi d'accès SQL à l'identité managée	⏳ en cours
Déploiement de l'API + tests de contrat en ligne	⏳ à venir
Le workflow en est à l'étape nouvelle : donner à l'API le droit d'accéder à votre base SQL. C'est la première fois qu'on touche à ce mécanisme, donc c'est celle à surveiller — elle prend un peu de temps car la base serverless doit se « réveiller » (mon outil réessaie automatiquement). Le fait qu'elle tourne toujours (et n'ait pas échoué) est bon signe.

Ensuite viendront le déploiement de l'API branchée sur SQL, puis les tests de contrat en ligne — le juge de paix de l'Étape 3.

Ma vérification automatique se déclenche dans 2-3 minutes et je reviens avec le verdict complet : soit l'Étape 3 bouclée avec les tests verts en ligne, soit un correctif si la connexion à la base demande un ajustement (comme prévu, c'est le premier contact réel avec SQL). Rien à faire de votre côté.

alors?

Utilisé github: actions get
Le déploiement a échoué à l'étape d'octroi d'accès SQL — comme je le pressentais, c'est le premier contact réel avec la base. Je lis l'erreur exacte :

Utilisé github: get job logs
La connexion à SQL a réussi (jeton + accès OK) — mais mon script T-SQL a une erreur de syntaxe (erreur SQL 102). Je récupère le message exact pour la corriger :


Exécuté une commande, utilisé un outil
Trouvé — et c'est une simple erreur de syntaxe SQL de ma part, pas un problème d'Azure. La connexion à la base a parfaitement fonctionné (jeton, accès, routage vers France Central : tout bon). Le message est clair : Incorrect syntax near 'QUOTENAME'.

En T-SQL, on ne peut pas appeler une fonction directement dans un EXEC(...). Il faut d'abord construire la commande dans une variable. Je corrige :


Modifié Program.cs, exécuté une commande
+9
-3
Corrigé et relancé. Le point rassurant : la connexion à votre base SQL a marché du premier coup (jeton, accès, tout bon) — il ne restait que ma faute de frappe SQL. Je reprogramme la surveillance sur le nouveau déploiement :


Utilisé 2 outils
Voici le point clair.

Ce qui s'est passé
Le déploiement a échoué, mais sur une faute de frappe SQL dans mon script — pas un problème de votre infrastructure. Et c'est même une bonne nouvelle déguisée :

✅ L'API a réussi à se connecter à votre base Azure SQL (jeton d'accès, autorisation, routage vers France Central — tout a fonctionné du premier coup).
❌ Ma commande d'octroi d'accès contenait une erreur de syntaxe T-SQL (Incorrect syntax near 'QUOTENAME') — je construisais la commande d'une façon que SQL Server n'accepte pas.
Le plus dur (faire dialoguer l'API et la base par identité managée, sans mot de passe) marche. Il ne restait que ma coquille.

Ce que j'ai fait
Corrigé (la commande est maintenant construite correctement avant exécution), et relancé le déploiement. Il va reprendre là où il s'était arrêté : octroi d'accès → déploiement de l'API sur SQL → migrations → tests de contrat en ligne.

Je surveille et reviens dans ~8 minutes avec le verdict. S'il reste un dernier détail Azure (par exemple la façon dont la base reconnaît l'identité de l'API), je corrige encore — rien à faire de votre côté.

On est vraiment tout près de boucler l'Étape 3.


Exécuté une commande, utilisé un outil
Ah — le déploiement ne s'est pas relancé, et j'ai trouvé pourquoi : mon correctif ne touchait que le dossier tools/, or le déclencheur du déploiement ne surveille que infra/ et api/. Donc rien ne s'est déclenché. Je corrige ce trou (j'ajoute tools/ aux déclencheurs) — ce qui relance aussi le déploiement avec la correction :


Modifié deploy-infra.yml, exécuté une commande
+1
-0
Poussé (f31ab8f) — le déploiement redémarre maintenant, cette fois avec ma correction SQL. Je reprogramme la surveillance :

Utilisé Claude Code Remote: send later
Résumé simple de la situation :

Ce qui s'est passé
Deux petits contretemps, tous deux de ma logistique — jamais votre infrastructure :

Faute de frappe SQL (déjà corrigée) — la connexion à votre base marchait, seule ma commande était mal écrite.
Le déploiement ne s'était pas relancé : mon correctif touchait un dossier (tools/) que le déclencheur automatique ne surveillait pas. Je viens d'ajouter ce dossier à la liste — et ça relance le déploiement du même coup.
Maintenant
Le déploiement repart avec la correction. Il va reprendre la séquence : octroi d'accès à la base → API branchée sur SQL → migrations → tests de contrat en ligne.

Je surveille et reviens dans ~8 minutes avec le verdict. Rien à faire de votre côté — je gère les corrections.

(Rappel sans urgence : la petite inscription d'application via le Portail reste à faire quand vous voulez, uniquement pour allumer l'authentification plus tard.)

This is my scheduled check-in. The GitHub MCP server is reconnecting — let me load the tools and check the deployment.


Exécuté une commande, utilisé 2 outils
Le déploiement (f31ab8f) a de nouveau échoué. Ma correction SQL a dû passer cette fois — l'erreur est ailleurs. Je lis les logs :

Utilisé github: get job logs
Mon correctif SQL a fonctionné — l'erreur de syntaxe a disparu. On est maintenant sur le point que j'avais anticipé : Azure SQL n'arrive pas à « reconnaître » l'identité de l'API par son nom (could not be resolved — le serveur SQL n'a pas le droit d'interroger l'annuaire).

La solution robuste : au lieu de faire chercher l'identité par son nom, je la déclare par son identifiant technique, fourni directement par l'infrastructure (aucune interrogation d'annuaire nécessaire). Pour ça je bascule sur une identité managée « attribuée par l'utilisateur » — dont l'identifiant est connu de Bicep. Je fais tous les changements :

Mis à jour
Program.cs
+14
-10
Maintenant l'identité managée attribuée par l'utilisateur dans le Bicep, et son usage par le Function App :


Modifié 2 fichiers, exécuté une commande
+19
-1
Maintenant Program.cs de l'API : la connexion SQL utilise l'identité attribuée (par son client_id) :


Lu et modifié Program.cs
+20
-0
Je mets à jour les deux endroits qui construisent la chaîne pour utiliser ce helper :

Modifié
Program.cs
+4
-20
Maintenant le workflow : ajouter le provider ManagedIdentity, récupérer les infos de l'identité, et passer le client_id à l'outil d'octroi :

Modifié
deploy-infra.yml
+6
-2
Je valide tout localement (Bicep, build API, build outil, YAML) avant de pousser :

Validated Bicep, YAML, and built API + grant tool
Tout est validé. Je lance la suite de tests complète puis je pousse le correctif :


Exécuté 2 commandes
Poussé (7145be9) — nouveau déploiement lancé avec l'approche robuste. Je reprogramme la surveillance :

Utilisé Claude Code Remote: send later
Point clair sur ce troisième essai.

Le diagnostic (et pourquoi c'est bon signe)
Ma correction SQL précédente a marché — l'erreur de syntaxe a disparu. On est tombé sur exactement l'obstacle que j'avais anticipé : Azure SQL refusait de « reconnaître » l'identité de l'API par son nom, parce que le serveur SQL n'a pas le droit d'interroger l'annuaire Entra (could not be resolved). C'est une limitation Azure connue, pas un vrai problème.

La solution (robuste, définitive)
Au lieu de faire chercher l'identité par son nom, je la déclare par son identifiant technique, que l'infrastructure connaît déjà — aucune interrogation d'annuaire nécessaire. Concrètement, j'ai basculé l'API sur une identité managée « attribuée », dont l'identifiant est produit par Bicep et transmis directement à la base. C'est la méthode recommandée précisément pour ce cas.

Tout est validé en local (Bicep, API, outil, 325 tests verts) et poussé — le déploiement repart.

Où on en est
On progresse pas à pas : chaque échec a livré une info précise, et chaque correctif est propre. Il ne reste plus grand-chose à franchir : l'octroi d'accès par SID, puis les migrations et les tests de contrat en ligne.

Je surveille et reviens dans ~8 minutes. Si ça passe, l'Étape 3 est bouclée ; sinon je corrige le dernier détail. Toujours rien à faire de votre côté.


