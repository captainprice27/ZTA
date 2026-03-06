# Azure Infra Starter

Deploy the SQL + NSG starter resources:

```bash
az deployment group create \
  --resource-group <your-rg> \
  --template-file main.bicep \
  --parameters @parameters.dev.json
```

The gateway kill-switch service can then be wired to this NSG by setting:

- `AzureBlock:SubscriptionId`
- `AzureBlock:ResourceGroup`
- `AzureBlock:NetworkSecurityGroupName`
