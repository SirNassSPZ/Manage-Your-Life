// ---------------------------------------------------------------------------
// Ressources de l'environnement (portée : groupe de ressources).
// Paliers gratuits / serverless / Consommation uniquement (§10.1).
// Aucun secret en dur : identités managées + Key Vault (§10, règle 16).
// ---------------------------------------------------------------------------
param region string
param environnement string
param suffixe string
param deployeurObjectId string
param deployeurLogin string

var court = take(suffixe, 6)

// Identifiants de rôles intégrés (constants Azure).
var roleBlobDataOwner = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var roleQueueDataContributor = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var roleKeyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

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
  name: 'stdc${environnement}${suffixe}'
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

// ---------- Key Vault : secrets (§10). RBAC, jamais de secret en dur ----------
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
  name: 'func-dc-${environnement}-${court}'
  location: region
  kind: 'functionapp'
  identity: { type: 'SystemAssigned' } // identité managée : ni clé ni chaîne de connexion en clair
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
        // Stockage du runtime par identité managée — aucune clé de compte en clair.
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
        // Prêt pour l'Étape 3 (§8), consommé par les adaptateurs, jamais par le cœur (règle 4).
        { name: 'KEY_VAULT_URI', value: keyVault.properties.vaultUri }
        { name: 'SQL_SERVER_FQDN', value: sqlServer.properties.fullyQualifiedDomainName }
        { name: 'SQL_DATABASE', value: sqlDb.name }
        { name: 'BLOB_ACCOUNT', value: storage.name }
        { name: 'BLOB_CONTAINER', value: conteneurPieces.name }
      ]
    }
  }
}

// ---------- Attributions de rôles à l'identité du Function App ----------
// Runtime Functions par identité : Blob + Queue sur le compte de stockage.
resource roleBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, roleBlobDataOwner)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleBlobDataOwner)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, roleQueueDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleQueueDataContributor)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Lecture des secrets (Étape 3) — l'app lit ses secrets dans Key Vault (règle 16).
resource roleKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, roleKeyVaultSecretsUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output nomFunctionApp string = functionApp.name
output urlFunctionApp string = 'https://${functionApp.properties.defaultHostName}'
