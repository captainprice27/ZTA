targetScope = 'resourceGroup'

@description('Azure region for deployment')
param location string = resourceGroup().location

@description('SQL server name')
param sqlServerName string

@secure()
@description('SQL admin login password')
param sqlAdminPassword string

@description('SQL admin login username')
param sqlAdminLogin string = 'ztaadmin'

@description('SQL database name for ZTA policy store')
param sqlDatabaseName string = 'ZtaPolicy'

@description('Network security group name')
param nsgName string = 'zta-gateway-nsg'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource nsg 'Microsoft.Network/networkSecurityGroups@2023-11-01' = {
  name: nsgName
  location: location
  properties: {
    securityRules: [
      {
        name: 'allow-web-443'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '443'
        }
      }
    ]
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseId string = sqlDatabase.id
output nsgId string = nsg.id
