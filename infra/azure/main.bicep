// =============================================================================
// cerdikMY — Azure infrastructure (Bicep)
//
// Provisions the full production stack on Azure Container Apps:
//   - Azure Container Registry (images for api/web/worker)
//   - Log Analytics workspace (Container Apps env logs)
//   - Container Apps managed environment
//   - 3 container apps: api (external 8080), web (external 8080), worker (no ingress)
//   - Azure SQL Server + Database
//   - Storage Account + blob container "cerdik-media"
//
// App configuration is injected as container-app secrets + env vars using the
// ASP.NET Core "Section__Key" (double-underscore) convention so that, e.g.,
// ConnectionStrings__Default maps onto configuration key "ConnectionStrings:Default".
//
// Deploy:
//   az deployment group create -g <rg> -f main.bicep -p @main.parameters.json \
//     -p containerImageApi=... containerImageWeb=... containerImageWorker=...
// =============================================================================

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Short prefix used to derive resource names (lowercase letters/numbers).')
@minLength(3)
@maxLength(11)
param namePrefix string = 'cerdik'

@description('Full image reference for the API container app, e.g. <acr>.azurecr.io/cerdik-api:latest')
param containerImageApi string

@description('Full image reference for the Web container app.')
param containerImageWeb string

@description('Full image reference for the Worker container app.')
param containerImageWorker string

@description('SQL Server administrator login name.')
param sqlAdminLogin string

@description('SQL Server administrator password.')
@secure()
param sqlAdminPassword string

@description('JWT access-token signing secret (min 32 chars).')
@secure()
param jwtAccessSecret string

@description('JWT refresh-token signing secret (min 32 chars).')
@secure()
param jwtRefreshSecret string

@description('AI provider key, e.g. openai | azureopenai | anthropic | mock.')
param aiProvider string = 'openai'

@description('OpenAI API key (leave empty if not using OpenAI).')
@secure()
param aiOpenAiApiKey string = ''

@description('Payments provider key, e.g. billplz | curlec | stripe.')
param paymentsProvider string = 'billplz'

// -----------------------------------------------------------------------------
// Derived names. Storage account names must be 3-24 chars, lowercase alnum.
// -----------------------------------------------------------------------------
var uniqueSuffix = uniqueString(resourceGroup().id)
var acrName = toLower('${namePrefix}acr${uniqueSuffix}')
var logAnalyticsName = '${namePrefix}-logs'
var environmentName = '${namePrefix}-cae'
var sqlServerName = toLower('${namePrefix}-sql-${uniqueSuffix}')
var sqlDatabaseName = 'cerdikmy'
var storageAccountName = toLower(take('${namePrefix}st${uniqueSuffix}', 24))
var mediaContainerName = 'cerdik-media'

var apiAppName = '${namePrefix}-api'
var webAppName = '${namePrefix}-web'
var workerAppName = '${namePrefix}-worker'

var containerPort = 8080

// -----------------------------------------------------------------------------
// Azure Container Registry.
// -----------------------------------------------------------------------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// -----------------------------------------------------------------------------
// Log Analytics workspace (backing store for the Container Apps environment).
// -----------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// -----------------------------------------------------------------------------
// Container Apps managed environment.
// -----------------------------------------------------------------------------
resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// -----------------------------------------------------------------------------
// Azure SQL Server + Database.
// -----------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'S0'
    tier: 'Standard'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

// Allow other Azure services (incl. Container Apps) to reach SQL. The
// 0.0.0.0 "AllowAllAzureIps" rule is the standard way to permit Azure-internal
// traffic without locking to specific egress IPs.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// -----------------------------------------------------------------------------
// Storage Account + blob container for media.
// -----------------------------------------------------------------------------
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource mediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: mediaContainerName
  properties: {
    publicAccess: 'None'
  }
}

// -----------------------------------------------------------------------------
// Connection strings assembled from provisioned resources.
// -----------------------------------------------------------------------------
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

var acrLoginServer = acr.properties.loginServer
var acrCredentials = acr.listCredentials()

// Shared registry config for all apps (admin creds via secret reference).
var registries = [
  {
    server: acrLoginServer
    username: acrCredentials.username
    passwordSecretRef: 'acr-password'
  }
]

var acrPasswordSecret = {
  name: 'acr-password'
  value: acrCredentials.passwords[0].value
}

// -----------------------------------------------------------------------------
// API container app — external ingress on 8080.
// -----------------------------------------------------------------------------
resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: containerPort
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: registries
      secrets: [
        acrPasswordSecret
        {
          name: 'connectionstrings-default'
          value: sqlConnectionString
        }
        {
          name: 'jwt-access-secret'
          value: jwtAccessSecret
        }
        {
          name: 'jwt-refresh-secret'
          value: jwtRefreshSecret
        }
        {
          name: 'ai-openai-apikey'
          value: aiOpenAiApiKey
        }
        {
          name: 'storage-azure-connectionstring'
          value: storageConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImageApi
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:${containerPort}'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'connectionstrings-default'
            }
            {
              name: 'Jwt__AccessSecret'
              secretRef: 'jwt-access-secret'
            }
            {
              name: 'Jwt__RefreshSecret'
              secretRef: 'jwt-refresh-secret'
            }
            {
              name: 'Ai__Provider'
              value: aiProvider
            }
            {
              name: 'Ai__OpenAiApiKey'
              secretRef: 'ai-openai-apikey'
            }
            {
              name: 'Storage__Provider'
              value: 'azure'
            }
            {
              name: 'Storage__AzureConnectionString'
              secretRef: 'storage-azure-connectionstring'
            }
            {
              name: 'Payments__Provider'
              value: paymentsProvider
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

// -----------------------------------------------------------------------------
// Web container app — external ingress on 8080.
// -----------------------------------------------------------------------------
resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: webAppName
  location: location
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: containerPort
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: registries
      secrets: [
        acrPasswordSecret
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: containerImageWeb
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:${containerPort}'
            }
            {
              name: 'API_BASE_URL'
              value: 'https://${apiApp.properties.configuration.ingress.fqdn}'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

// -----------------------------------------------------------------------------
// Worker container app — no ingress.
// -----------------------------------------------------------------------------
resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerAppName
  location: location
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      secrets: [
        acrPasswordSecret
        {
          name: 'connectionstrings-default'
          value: sqlConnectionString
        }
        {
          name: 'ai-openai-apikey'
          value: aiOpenAiApiKey
        }
        {
          name: 'storage-azure-connectionstring'
          value: storageConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: containerImageWorker
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'connectionstrings-default'
            }
            {
              name: 'Ai__Provider'
              value: aiProvider
            }
            {
              name: 'Ai__OpenAiApiKey'
              secretRef: 'ai-openai-apikey'
            }
            {
              name: 'Storage__Provider'
              value: 'azure'
            }
            {
              name: 'Storage__AzureConnectionString'
              secretRef: 'storage-azure-connectionstring'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

// -----------------------------------------------------------------------------
// Outputs.
// -----------------------------------------------------------------------------
@description('Public FQDN of the API container app.')
output apiFqdn string = apiApp.properties.configuration.ingress.fqdn

@description('Public FQDN of the Web container app.')
output webFqdn string = webApp.properties.configuration.ingress.fqdn

@description('Login server of the Azure Container Registry.')
output acrLoginServer string = acrLoginServer
