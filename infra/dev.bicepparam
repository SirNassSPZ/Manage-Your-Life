// Paramètres de l'environnement de développement (rg-dc-dev).
// Ne contient aucun secret : uniquement des identifiants et des réglages.
using './main.bicep'

param environnement = 'dev'
// West Europe refusait les nouveaux clients (région saturée, 2026-07-23) ;
// France Central est l'autre région autorisée par la spec (§10.1, RGPD/latence).
param region = 'francecentral'

// E-mail des alertes de budget (§10.3) — modifiable librement.
param emailAlerte = 'mnasrinasreddin.contact@gmail.com'
param plafondBudget = 5

// Administrateur Entra ID de SQL = le service principal qui déploie (§8).
// Fourni par le workflow via la variable d'environnement DEPLOYER_OBJECT_ID.
param deployeurObjectId = readEnvironmentVariable('DEPLOYER_OBJECT_ID', '')
param deployeurLogin = 'github-deploy'
