// ---------------------------------------------------------------------------
// Deuxième Cerveau — Infrastructure (Étape 2)
// Portée : abonnement. Crée le groupe de ressources de l'environnement, puis
// l'ALERTE DE BUDGET EN PREMIER (§10.3, règle 16), puis les ressources — qui
// dépendent explicitement du budget pour garantir l'ordre.
// Paliers gratuits / serverless / Consommation uniquement (§10).
// ---------------------------------------------------------------------------
targetScope = 'subscription'

@description('Environnement : dev pour développer et tester ; prod seulement après l\'étape 6 (CLAUDE.md).')
@allowed(['dev', 'prod'])
param environnement string = 'dev'

@description('Région Azure — France Central ou West Europe (latence, RGPD, §10.1).')
@allowed(['westeurope', 'francecentral'])
param region string = 'westeurope'

@description('Adresse e-mail recevant l\'alerte de budget (§10.3).')
param emailAlerte string

@description('Plafond mensuel de l\'alerte de budget, dans la devise de facturation (§10.3). Volontairement bas : le régime de croisière vise ~0.')
param plafondBudget int = 5

@description('Object ID du principal qui déploie (service principal CI) — administrateur Entra ID de SQL (§8). Fourni par le workflow.')
param deployeurObjectId string

@description('Nom d\'affichage de l\'administrateur Entra ID de SQL.')
param deployeurLogin string = 'github-deploy'

@description('Active l\'exigence d\'un jeton Entra ID sur chaque appel (§8).')
param authActivee bool = false

@description('Audience des jetons : identifiant de l\'inscription d\'application « API » (§8).')
param entraAudience string = ''

@description('Locataire Entra pour la validation des jetons (GUID).')
param entraTenantId string = ''

@description('Premier jour du mois courant — début de la période de budget (calculé au déploiement).')
param dateDebutBudget string = '${utcNow('yyyy-MM')}-01'

var nomGroupe = 'rg-dc-${environnement}'
var suffixe = uniqueString(subscription().id, environnement)

resource groupe 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: nomGroupe
  location: region
}

// Alerte de budget POSÉE LE PREMIER JOUR, avant toute autre ressource (§10.3, règle 16).
module budget 'budget.bicep' = {
  scope: subscription()
  name: 'budget-dc-${environnement}'
  params: {
    nom: 'budget-dc-${environnement}'
    plafond: plafondBudget
    email: emailAlerte
    groupeCible: nomGroupe
    dateDebut: dateDebutBudget
  }
}

// Toutes les ressources facturables/serverless — jamais avant l'alerte de budget.
module ressources 'resources.bicep' = {
  scope: groupe
  name: 'ressources-dc-${environnement}'
  dependsOn: [budget]
  params: {
    region: region
    environnement: environnement
    suffixe: suffixe
    deployeurObjectId: deployeurObjectId
    deployeurLogin: deployeurLogin
    authActivee: authActivee
    entraAudience: entraAudience
    entraTenantId: entraTenantId
  }
}

output nomGroupe string = nomGroupe
output nomFunctionApp string = ressources.outputs.nomFunctionApp
output urlApi string = ressources.outputs.urlFunctionApp
output urlPing string = '${ressources.outputs.urlFunctionApp}/api/ping'
output sqlFqdn string = ressources.outputs.sqlFqdn
output sqlDatabase string = ressources.outputs.sqlDatabase
output identiteNom string = ressources.outputs.identiteNom
output identiteClientId string = ressources.outputs.identiteClientId
