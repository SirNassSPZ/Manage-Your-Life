// ---------------------------------------------------------------------------
// Alerte de budget (Azure Cost Management) — §10.3, règle 16.
// C'est la première ressource déployée : le seul garde-fou de coût une fois le
// crédit initial épuisé (le plafond de dépenses saute en paiement à l'usage, §10.2).
// Filtrée sur le groupe de ressources de l'environnement ; alertes par e-mail
// à 50 % (prévu), 90 % et 100 % (réel).
// ---------------------------------------------------------------------------
targetScope = 'subscription'

param nom string
param plafond int
param email string
param groupeCible string

@description('Début de la période, premier jour d\'un mois (contrainte de l\'API Consumption).')
param dateDebut string

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: nom
  properties: {
    category: 'Cost'
    amount: plafond
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '${dateDebut}T00:00:00Z'
    }
    filter: {
      dimensions: {
        name: 'ResourceGroupName'
        operator: 'In'
        values: [groupeCible]
      }
    }
    notifications: {
      prevu50: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Forecasted'
        contactEmails: [email]
      }
      reel90: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 90
        thresholdType: 'Actual'
        contactEmails: [email]
      }
      reel100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [email]
      }
    }
  }
}
