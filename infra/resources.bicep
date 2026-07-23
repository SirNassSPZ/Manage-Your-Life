// ---------------------------------------------------------------------------
// Ressources de l'environnement (portée : groupe de ressources).
// Paliers gratuits / serverless / Consommation uniquement (§10.1).
// Secrets applicatifs (SQL…) via Key Vault + identité managée en Étape 3 (règle 16) ;
// le stockage du RUNTIME Functions passe par une chaîne de connexion (contrainte de
// plateforme du plan Consommation Windows : le partage de contenu n'accepte pas l'identité
// managée). Cette clé est injectée par Bicep dans les réglages d'app — jamais dans git.
// ---------------------------------------------------------------------------
param region string
param environnement string
param suffixe string
param deployeurObjectId string
param deployeurLogin string

@description('Active l\'exigence d\'un jeton Entra ID sur chaque appel (§8). Faux tant que l\'inscription d\'application n\'existe pas.')
param authActivee bool = false

@description('Audience des jetons : identifiant de l\'inscription d\'application « API » (§8).')
param entraAudience string = ''

@description('Locataire Entra pour la validation des jetons (GUID) ; défaut « common ».')
param entraTenantId string = ''

var court = take(suffixe, 6)
var nomStockage = 'stdc${environnement}${suffixe}'
var nomFunc = 'func-dc-${environnement}-${court}'

// ---------- Journalisation : Log Analytics + Application Insights ----------
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-dc-${environnement}'
  location: region
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-dc-${environnement}'
  location: region
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

// ---------- Stockage : runtime Functions + pièces jointes (§7) ----------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: nomStockage
  location: region
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false // §7 : jamais de conteneur public, accès par URL SAS uniquement
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource conteneurPieces 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'pieces-jointes'
  properties: { publicAccess: 'None' }
}

var chaineStockage = 'DefaultEndpointsProtocol=https;AccountName=${nomStockage};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

// ---------- Key Vault : secrets (§10). Prêt pour l'Étape 3 ----------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-dc-${environnement}-${court}'
  location: region
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    publicNetworkAccess: 'Enabled'
  }
}

// ---------- Azure SQL serverless — palier gratuit permanent (§10.1) ----------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-dc-${environnement}-${court}'
  location: region
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // Authentification Entra ID uniquement (§8) — aucun mot de passe, aucun secret.
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: deployeurLogin
      sid: deployeurObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'db-dc-${environnement}'
  location: region
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    autoPauseDelay: 60 // pause après 1 h d'inactivité (§10.1) ; reprise en quelques secondes
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 Go, inclus dans le gratuit
    zoneRedundant: false
    useFreeLimit: true // 100 000 s vCore/mois gratuits en permanence (§10.1)
    freeLimitExhaustionBehavior: 'AutoPause' // quota épuisé → pause plutôt que facturation
  }
}

// Le Function App (et le CI) atteignent SQL : autoriser les services Azure.
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

// ---------- Functions : plan Consommation (§10.1) ----------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-dc-${environnement}'
  location: region
  sku: { name: 'Y1', tier: 'Dynamic' }
  properties: {}
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: nomFunc
  location: region
  kind: 'functionapp'
  identity: { type: 'SystemAssigned' } // conservée pour l'Étape 3 (SQL + Key Vault par identité)
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      netFrameworkVersion: 'v8.0'
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        // Stockage du runtime + partage de contenu (contrainte plateforme Consommation Windows).
        { name: 'AzureWebJobsStorage', value: chaineStockage }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: chaineStockage }
        { name: 'WEBSITE_CONTENTSHARE', value: toLower(nomFunc) }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
        // Prêt pour l'Étape 3 (§8), consommé par les adaptateurs, jamais par le cœur (règle 4).
        { name: 'KEY_VAULT_URI', value: keyVault.properties.vaultUri }
        { name: 'SQL_SERVER_FQDN', value: sqlServer.properties.fullyQualifiedDomainName }
        { name: 'SQL_DATABASE', value: sqlDb.name }
        { name: 'BLOB_ACCOUNT', value: storage.name }
        { name: 'BLOB_CONTAINER', value: conteneurPieces.name }
        // Authentification Entra ID (§8) — inactive tant que authActivee = false.
        { name: 'AUTH_ACTIVEE', value: toLower(string(authActivee)) }
        { name: 'ENTRA_AUDIENCE', value: entraAudience }
        { name: 'ENTRA_TENANT_ID', value: entraTenantId }
      ]
    }
  }
}

output nomFunctionApp string = nomFunc
output urlFunctionApp string = 'https://${functionApp.properties.defaultHostName}'
