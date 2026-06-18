// ============================================================================
//  RRHHNovedades — Infra prod (estándar Espert)
//  Tier M: Container App (.NET 10 Blazor Server) + PostgreSQL Flexible + Key Vault.
//  Región user-facing: Brazil South. DB en red privada (VNet), sin acceso público.
//  ACR y Log Analytics propios del proyecto (igual que gastos/facturación; el "shared"
//  del estándar todavía no existe — es deuda futura de consolidación).
//  Scope: resource group (rg-rrhh-prod). El RG y el ACR los crea el script de deploy.
// ============================================================================

targetScope = 'resourceGroup'

@description('Región de los recursos.')
param location string = 'brazilsouth'

@description('Nombre de proyecto para el naming {tipo}-{proyecto}-{env}.')
param project string = 'rrhh'

@description('Entorno.')
@allowed([ 'prod', 'staging', 'dev' ])
param env string = 'prod'

@description('Nombre del ACR (lo crea el script antes, para poder buildear la imagen).')
param acrName string

@description('Imagen del contenedor a desplegar (acr/imagen:tag).')
param containerImage string

@description('Usuario administrador de PostgreSQL.')
param pgAdminUser string = 'rrhhadmin'

@description('Password del administrador de PostgreSQL.')
@secure()
param pgAdminPassword string

// --- Naming estándar -------------------------------------------------------
var suffix = '${project}-${env}'
var vnetName = 'vnet-${suffix}'
var pgName = 'pg-${suffix}'
var pgDbName = 'rrhhnovedades'
var kvName = 'kv-${suffix}'
var miName = 'id-${suffix}'
var caeName = 'cae-${suffix}'
var caName = 'ca-${suffix}'
var logName = 'log-${suffix}'

var tags = {
  proyecto: project
  env: env
  app: 'RRHHNovedades'
}

// ============================================================================
//  Observabilidad: Log Analytics del proyecto.
// ============================================================================
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ============================================================================
//  Red privada: VNet con subred para Container Apps y subred delegada a Postgres.
// ============================================================================
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [ '10.20.0.0/16' ] }
    subnets: [
      {
        // Container Apps (Consumption) necesita una subred propia de al menos /23,
        // delegada al servicio Microsoft.App/environments.
        name: 'snet-infra'
        properties: {
          addressPrefix: '10.20.0.0/23'
          delegations: [
            {
              name: 'cae-delegation'
              properties: { serviceName: 'Microsoft.App/environments' }
            }
          ]
        }
      }
      {
        // Subred delegada exclusivamente a Postgres Flexible.
        name: 'snet-pg'
        properties: {
          addressPrefix: '10.20.2.0/24'
          delegations: [
            {
              name: 'pg-delegation'
              properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' }
            }
          ]
        }
      }
    ]
  }
}

resource snetInfra 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: 'snet-infra'
}
resource snetPg 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: 'snet-pg'
}

// Private DNS para que el FQDN del Postgres resuelva dentro de la VNet.
resource pgDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: '${pgName}.private.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource pgDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: pgDnsZone
  name: 'link-${vnetName}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
}

// ============================================================================
//  PostgreSQL Flexible (red privada, SSL, backups 7 días).
// ============================================================================
resource pg 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: pgName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms' // Burstable: alcanza para ~200 empleados / 1 fila por día.
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: pgAdminUser
    administratorLoginPassword: pgAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' } // Burstable no soporta HA. Subir de tier para activarla.
    network: {
      delegatedSubnetResourceId: snetPg.id
      privateDnsZoneArmResourceId: pgDnsZone.id
      publicNetworkAccess: 'Disabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
  }
  dependsOn: [ pgDnsLink ]
}

resource pgDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: pg
  name: pgDbName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// ============================================================================
//  Identidad + Key Vault (secretos accedidos por Managed Identity).
// ============================================================================
resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: miName
  location: location
  tags: tags
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true // acceso por RBAC, no access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// La MI puede LEER secretos del Vault (Key Vault Secrets User).
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
resource kvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, mi.id, kvSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// La MI puede PULL del ACR del proyecto (AcrPull).
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
resource acrRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, mi.id, acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ============================================================================
//  Container Apps: environment (con VNet + logs) + la app.
// ============================================================================
resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: caeName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: snetInfra.id
      internal: false // ingress público (con TLS administrado)
    }
    zoneRedundant: false
  }
}

resource ca 'Microsoft.App/containerApps@2024-03-01' = {
  name: caName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${mi.id}': {} }
  }
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // Blazor Server usa un circuito SignalR por usuario → afinidad de sesión obligatoria.
        stickySessions: { affinity: 'sticky' }
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: mi.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'KeyVault__Uri', value: kv.properties.vaultUri }
            { name: 'Humand__UseMock', value: 'false' }
            // Le decimos a Azure.Identity qué identidad usar (la user-assigned).
            { name: 'AZURE_CLIENT_ID', value: mi.properties.clientId }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/ready', port: 8080 }
              periodSeconds: 15
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        // CRÍTICO: 1 sola réplica. El scheduler de partes corre in-process; con >1 réplica
        // se manda el WhatsApp duplicado. Además Blazor Server quiere afinidad estable.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [ acrRole ]
}

// --- Outputs (los usa el script para cargar secretos y verificar) ----------
output keyVaultName string = kv.name
output keyVaultUri string = kv.properties.vaultUri
output pgFqdn string = pg.properties.fullyQualifiedDomainName
output pgDatabase string = pgDbName
output managedIdentityName string = mi.name
output containerAppName string = ca.name
output containerAppFqdn string = ca.properties.configuration.ingress.fqdn
