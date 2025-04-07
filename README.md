
In the past, we wrote an app that read Secret from KeyValut inside of applciation, but
in this project we are going to read secret from KeyVault and Pass it as Enviroment Varialbe to our applicatin (instead of reading those values inside of app)

RepositoryName: ReadFromKeVaultInKubernet

****
## STEPS
In this project we try to followg the steps for creating AzureKeyVault in this article
https://medium.com/@nazrazpejman/deploying-asp-net-core-application-with-azure-key-vault-acr-and-azure-kubernetes-service-aks-852930ebfab2
But i will write here steps whic i followed:

#### Step1: Creae ServiceGroup and ACR and AKS
```
$serviceGroupName = "orderRG"
$resourceGroupLocation = "westus"
az group create --name $serviceGroupName --location $resourceGroupLocation

$azureContainerRegistryName = "ordercontainerregistry"
az acr create --name $azureContainerRegistryName --resource-group $serviceGroupName --sku Basic


$orderAzureKuberName = "orderazurekuber"
az aks create -n $orderAzureKuberName -g $serviceGroupName --node-vm-size Standard_B2s --node-count 2 --attach-acr $azureContainerRegistryName --enable-oidc-issuer --enable-workload-identity --generate-ssh-keys --location $resourceGroupLocation

az aks get-credentials --name $orderAzureKuberName --resource-group $serviceGroupName
```


#### Step2: Install Secrets Store CSI Driver & Azure Provider

```
$namespace = "dev"
kubectl create namespace $namespace


helm repo add secrets-store-csi-driver https://kubernetes-sigs.github.io/secrets-store-csi-driver/charts
helm install csi-secrets-store secrets-store-csi-driver/secrets-store-csi-driver --namespace $namespace

```


#### Step3: Configuring the Azure KeyVault
create
```
$azureKeyVaultName = "myOrderkeyvault"
az keyvault create -n $azureKeyVaultName -g $serviceGroupName
```

then assgin required role and then dfine ur `secrets` on keyvalut
```
$userId = az ad signed-in-user show --query id --output tsv
$subscriptionId = az account show --query id --output tsv

az role assignment create --assignee $userId --role "Key Vault Administrator" --scope "/subscriptions/$subscriptionId/resourceGroups/$serviceGroupName/providers/Microsoft.KeyVault/vaults/$azureKeyVaultName"

az keyvault secret set --vault-name $azureKeyVaultName --name "UserSetting--MySecret" --value "Secret From Azure KeyVault"


```

then create mangedientityt
```
$azureManageIdentityForIdentityMicroserviceName = "AzureKeyVaultServiceManageIdentity"
az identity create --name $azureManageIdentityForIdentityMicroserviceName --resource-group $serviceGroupName


$MANAGE_IDENTITY_CLIENT_ID = az identity show -g $serviceGroupName -n $azureManageIdentityForIdentityMicroserviceName --query clientId -o tsv
az role assignment create --assignee $MANAGE_IDENTITY_CLIENT_ID --role "Key Vault Secrets User" --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$serviceGroupName/providers/Microsoft.KeyVault/vaults/$azureKeyVaultName"

```

#### Step4: Define SecretProviderClass

the process is something like this:
```
keyvaultapp Deployment
└── Pod created with serviceAccount: keyvaultapp-serviceaccount
    └── Mounts /mnt/secrets-store (volume)
         └── CSI driver (secrets-store.csi.k8s.io) runs inside the pod
              └── Authenticates to Azure using the token from keyvaultapp-serviceaccount
                   └── Azure Workload Identity uses federated identity to map this token to the Managed Identity:
                        └── Managed Identity with clientId: 06791635-522f-4ef5-a476-c7b3c1bada03
                             └── Azure Key Vault allows access (Key Vault Secrets User role assigned)
                                  └── Secret `UserSetting--MySecret` is fetched from Key Vault
                                       └── CSI Driver creates a Kubernetes Secret:
                                            └── metadata.name: keyvault-secrets
                                                └── data:
                                                    └── UserSetting__MySecret: "Secret From Azure KeyVault"
                                                     
                                                   ⬇
                                       └── In the container:
                                            └── Environment variable is created:
                                                 └── UserSetting__MySecret = "Secret From Azure KeyVault"

```

we need fetch the secret from KeyVault and make it avaiable as kubernetsSecret or file so i need to create:
```
apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: azure-keyvault-secrets
  namespace: dev
spec:
  provider: azure
  parameters:
    usePodIdentity: "false"
    useVMManagedIdentity: "false"
    clientID: "06791635-522f-4ef5-a476-c7b3c1bada03" # your user-assigned managed identity client ID
    keyvaultName: "myOrderkeyvault"
    cloudName: "AzurePublicCloud"
    objects: |
      array:
        - |
          objectName: UserSetting--MySecret
          objectType: secret
    tenantId: 1046fb1f-882c-4640-881d-d2f993263e1a         #<YOUR_TENANT_ID>
  secretObjects:                     # <-- This creates a K8s secret from the Key Vault secret
    - secretName: keyvault-secrets  # Kubernetes Secret name
      type: Opaque
      data:
        - objectName: UserSetting--MySecret
          key: UserSetting__MySecret

```

In following part, `objectName` is telling the name of the object that should be fetch from KeyVault and 
`objectType` is the type of that object that can be secret → for plain secrets, key → for cryptographic keys and ..
```
objects: |
      array:
        - |
          objectName: UserSetting--MySecret
          objectType: secret
```
This is part of the SecretProviderClass, and it tells the CSI driver:
```
secretObjects:
  - secretName: keyvault-secrets
    type: Opaque
    data:
      - objectName: UserSetting--MySecret
        key: UserSetting__MySecret

```
>"Once you've fetched the secret from Azure Key Vault (using objectName), create a Kubernetes Secret in the pod's namespace named keyvault-secrets. Inside it, store the secret under the key UserSetting__MySecret."

✅ So yes:

secretName: keyvault-secrets ➜ the name of the Kubernetes Secret to create

objectName: UserSetting--MySecret ➜ the name of the secret in Azure Key Vault

key: UserSetting__MySecret ➜ the key used in the resulting Kubernetes Secret



NOTE => And above class should be avaiable for `keyvaultapp-deployment` through Volume.

#### Step7: Build application image and Apply deplpyment objects
Frist Build image and push it to ACR:
```
docker build -t keyvaultapp:1.0.4 .
$azureContainerRegistryAddress=$(az acr show --name $azureContainerRegistryName --query "loginServer" --output tsv)
docker tag keyvaultapp:1.0.4 "$azureContainerRegistryAddress/keyvaultapp:1.0.4"

az acr login --name $azureContainerRegistryName
docker push "$azureContainerRegistryAddress/keyvaultapp:1.0.4"
```

then we need apply following:
```
 kubectl apply -f .\Kubernetes\application.yaml -n $namespace
 kubectl apply -f .\Kubernetes\secretproviderclass.yaml -n $namespace
 kubectl apply -f .\Kubernetes\service.yaml -n $namespace
 kubectl apply -f .\Kubernetes\serviceaccount.yaml -n $namespace
```

#### Step6:  Link our AKS Service Account with Azure Managed Identity using Federated Identity Credentials.
```
$AKS_OIDC_ISSUER=az aks show -n $orderAzureKuberName -g  $serviceGroupName --query "oidcIssuerProfile.issuerUrl" -o tsv

$federateCredentialName="idenityservicefederatedidcredential"
az identity federated-credential create --name $federateCredentialName --identity-name $azureManageIdentityForIdentityMicroserviceName --resource-group $serviceGroupName --issuer $AKS_OIDC_ISSUER --subject "system:serviceaccount:${namespace}:keyvaultapp-serviceaccount"

```
