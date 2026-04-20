# Deployment Notes

## Local Docker-first workflow

1. Copy environment file:

```powershell
copy .env.example .env
```

2. Set a strong `SA_PASSWORD` in `.env`.

3. Start the stack:

```powershell
docker compose up --build
```

4. Verify:

- gateway: `http://localhost:5000/health`
- ai service: `http://localhost:8000/health`

5. Run smoke validation:

```powershell
.\tests\gateway-smoke.ps1
python .\src\ai-service\smoke_test.py
```

## SQL Server mode outside Docker

Use local SQL Server / SSMS when you want the policy store on SQL Server but still keep the decision cache local:

```json
"ConnectionStrings": {
  "PolicyDb": "Server=localhost;Database=ZtaPolicy;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False",
  "DecisionCacheDb": "Data Source=zta-cache.db"
},
"PolicyStore": {
  "Provider": "SqlServer"
}
```

## Azure NSG blocking

The gateway now supports real NSG rule creation if `AzureBlock.Enabled=true` and the following are set:

- `AzureBlock.SubscriptionId`
- `AzureBlock.ResourceGroup`
- `AzureBlock.NetworkSecurityGroupName`
- optional rule tuning fields in `appsettings.json`

Authentication order:

1. Azure CLI credential
2. Default Azure credential chain

This means local use is simplest after:

```powershell
az login
```

## Kubernetes starter path

Starter manifests live in `k8s/`.

Recommended order:

1. build and push images
2. create secrets/configmaps
3. apply namespace + workloads

Example:

```powershell
kubectl apply -f .\k8s\namespace.yaml
kubectl apply -f .\k8s\configmap.yaml
kubectl apply -f .\k8s\secret.example.yaml
kubectl apply -f .\k8s\mssql.yaml
kubectl apply -f .\k8s\ai-service.yaml
kubectl apply -f .\k8s\gateway.yaml
```

The manifests are starter-grade. They are enough to show service wiring, not final production security posture.
