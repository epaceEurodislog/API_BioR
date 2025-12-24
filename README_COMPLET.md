# 🧬 API_BioR - Synchronisation Dynamics 365 vers SQL Server

## 📋 Table des matières

1. [Vue d'ensemble](#vue-densemble)
2. [Architecture technique](#architecture-technique)
3. [Installation & Configuration](#installation--configuration)
4. [Fonctionnalités](#fonctionnalités)
5. [Guide d'utilisation](#guide-dutilisation)
6. [Architecture base de données](#architecture-base-de-données)
7. [Déploiement](#déploiement)
8. [Troubleshooting](#troubleshooting)
9. [FAQ](#faq)

---

## 🎯 Vue d'ensemble

**API_BioR** est un **orchestrateur intelligent de synchronisation** entre **Dynamics 365** et **SQL Server**, développé pour **BioRécup**. Il synchronise en temps réel les **articles**, **commandes** (Achat, Retour, Transfert, Vente) et gère les **confirmations automatiques** avec traçabilité complète.

### 📊 Informations générales

| Propriété | Valeur |
|-----------|--------|
| **Nom du projet** | API_BioR (DynamicsApiToDatabase) |
| **Version** | 2.0.0 |
| **Framework** | .NET 8.0 (Console Application) |
| **Langage** | C# |
| **Client** | BiologiqueRecherche (code: **BR**) |
| **Environnement** | UAT Sandbox (Dynamics 365) |
| **Base de données** | SQL Server (7.2.160.173 - Middleware) |
| **Type de synchronisation** | Bidirectionnelle avec confirmations |

### 🔄 Flux principal

```
┌─────────────────────────────────────────────────────────────────┐
│                      DYNAMICS 365 (UAT)                         │
│  Articles | Purchase Orders | Return Orders | Transfer Orders   │
│                      Sales Orders                               │
└────────────────────────────┬────────────────────────────────────┘
                             │
                    HTTP REST API (Bearer Token)
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                    API_BIOR (Orchestrateur)                     │
│  • Synchronisation intelligente par hash/date                   │
│  • Confirmations automatiques (Purchase/Return/Transfer/Sales)  │
│  • Export BL SpeedWMS vers Dynamics 365                         │
│  • Ajustements d'inventaire (INT48)                             │
│  • Lancement DynamicsToXmlTranslator                            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                    SQL Server (7.2.160.173)
                             │
         ┌───────────────────┼───────────────────┐
         ▼                   ▼                   ▼
    ┌────────────┐     ┌────────────┐     ┌────────────┐
    │  JSON_IN   │     │  JSON_OUT  │     │ SpeedWMS   │
    │ (Réception)│     │ (Traçabilité)    │(Source BL) │
    └────────────┘     └────────────┘     └────────────┘
```

---

## 🏗️ Architecture technique

### 📦 Dépendances principales

```xml
<!-- Framework .NET 8.0 -->
Microsoft.Extensions.Configuration       v8.0.0
Microsoft.Extensions.DependencyInjection v8.0.0
Microsoft.Extensions.Logging             v8.0.0

<!-- Accès données -->
Microsoft.Data.SqlClient                 v5.1.5
System.Text.Json                         v8.0.5

<!-- Authentification Azure -->
Microsoft.Identity.Client                v4.61.3

<!-- Utilitaires -->
System.Net.Http                          v4.3.4
System.Security.Cryptography.Algorithms  v4.3.1
```

### 📂 Structure du projet

```
API_BioR/
│
├── 📄 Program.cs                                    # ⭐ Point d'entrée principal
│                                                      # • ExecuteFullSync() : Synchronisation complète
│                                                      # • ExecuteSpecializedSync() : Sync spécialisée par type
│                                                      # • ConfigureServices() : Injection de dépendances
│
├── Services/
│   ├── AuthenticationService.cs                    # 🔐 Authentification OAuth2 Azure AD
│   │                                                 # Obtient les tokens Bearer pour Dynamics 365
│   │
│   ├── DynamicsDataService.cs                      # 🔄 Synchronisation intelligente
│   │                                                 # • SyncAllEndpointsWithOrderConfirmationsAsync()
│   │                                                 # • Synchronisation par hash pour la plupart
│   │                                                 # • Synchronisation par date pour les articles
│   │                                                 # • Détection des modifications
│   │
│   ├── SqlServerDatabaseService.cs                 # 🗄️ Gestion SQL Server
│   │                                                 # • Insertion/Mise à jour JSON_IN
│   │                                                 # • Gestion des confirmations
│   │                                                 # • Statistiques
│   │
│   ├── StatusConfirmationService.cs                # ✅ Confirmations des commandes
│   │                                                 # • ConfirmPurchaseOrderWithStatusUpdateAsync()
│   │                                                 # • ConfirmReturnOrderWithStatusUpdateAsync()
│   │                                                 # • ConfirmTransferOrderWithStatusUpdateAsync()
│   │                                                 # • ConfirmSalesOrderWithStatusUpdateAsync()
│   │
│   ├── BLExportService.cs                          # 📦 Export Bons de Livraison
│   │                                                 # • Récupère les BL de SpeedWMS
│   │                                                 # • Valide auprès de Dynamics 365
│   │                                                 # • POST les confirmations
│   │
│   ├── JsonOutService.cs                           # 📝 Traçabilité JSON_OUT
│   │                                                 # • Enregistre tous les mouvements
│   │                                                 # • Suivi des articles confirmés
│   │
│   ├── ItemArrivalJournalService.cs                # 📥 Journaux de réception
│   │                                                 # • Crée les headers ItemArrivalJournal
│   │                                                 # • Crée les lignes détaillées
│   │                                                 # • Confirme les journaux
│   │
│   ├── REEDataService.cs & SpeedWmsDataService.cs # 🏭 Accès aux données externes
│   │                                                 # • Récupération données SpeedWMS
│   │                                                 # • Récupération données REE
│   │
│   ├── ExternalProgramLauncher.cs                  # 🚀 Lancement translator externe
│   │                                                 # • Lance DynamicsToXmlTranslator.exe
│   │
│   ├── StatusUpdateService.cs                      # 📊 Mise à jour statuts
│   │
│   ├── SimplePurchaseLogger.cs                     # 📋 Logging simplifié
│   │
│   └── INT48/
│       ├── InventoryAdjustmentService.cs           # 🏭 Ajustements d'inventaire (Dynamics 365)
│       ├── InventoryTransformationService.cs       # 🔄 Transformation des données INT48
│       └── DynamicsAuthService.cs                  # 🔐 Auth spécifique INT48
│
├── Models/
│   ├── DynamicsModels.cs                           # 📦 Classes de données principales
│   │                                                 # SyncResult, DatabaseStatistics, etc.
│   │
│   ├── JsonOutModels.cs                            # 📝 Modèles traçabilité JSON_OUT
│   │
│   ├── ItemArrivalJournalModels.cs                 # 📥 Modèles journaux réception
│   │
│   └── INT48/
│       └── InventoryAdjustmentModels.cs            # 🏭 Modèles ajustements stock
│
├── DataAccess/
│   └── INT48/
│       └── SpeedWmsInventoryRepository.cs          # 🏭 Accès données SpeedWMS (INT48)
│
├── Utilities/
│   ├── ConfirmationHelper.cs                       # ✅ Helpers confirmations
│   ├── OrderConfirmationHelper.cs                  # 🛒 Helpers confirmations commandes
│   ├── StatusHelper.cs                             # 📊 Helpers statuts
│   └── JsonHelper.cs                               # 📄 Helpers JSON
│
├── 📄 DynamicsApiToDatabase.csproj                 # Configuration projet .NET 8
├── 📄 appsettings.json                             # ⚙️ Configuration (à créer)
├── 📄 appsettings.Development.json                 # Configuration développement
└── 📄 appsettings.Production.json                  # Configuration production
```

### 🔌 Services principaux et leurs rôles

#### 1️⃣ **AuthenticationService**
```csharp
// Obtient un token Bearer auprès d'Azure AD
var token = await authService.GetAccessTokenAsync();
// Token à injecter dans les headers HTTP : Authorization: Bearer {token}
```

**Configuration requise :**
- `TenantId` : Identifiant du tenant Azure
- `ClientId` : ID de l'application enregistrée
- `ClientSecret` : Secret de l'application
- `ResourceUrl` : URL de l'instance Dynamics 365

#### 2️⃣ **DynamicsDataService**
```csharp
// Synchronise tous les endpoints avec confirmations automatiques
var syncResults = await dynamicsService.SyncAllEndpointsWithOrderConfirmationsAsync();

// Endpoints synchronisés :
// • Articles          (BRINT34ReleasedProducts)
// • Purchase Orders   (BRINT32PurchOrderTables)
// • Return Orders     (BRINT32ReturnOrderTables)
// • Transfer Orders   (BRINT32TransferOrderTables)
// • Sales Orders      (BRPackingSlipInterfaces)
```

**Logique de synchronisation :**
- **Pour les Articles** : Synchronisation par **date de modification** (ModifiedDate)
- **Pour les Commandes** : Synchronisation par **hash SHA256** (détecte tout changement)
- **Détection intelligente** : N'insère/met à jour que si changement réel
- **Suppressions** : Marque les records supprimés en Dynamics comme supprimés

#### 3️⃣ **StatusConfirmationService**
```csharp
// Confirme automatiquement les commandes
await statusConfirmationService.ConfirmPurchaseOrderWithStatusUpdateAsync(token, purchaseOrderId);
await statusConfirmationService.ConfirmReturnOrderWithStatusUpdateAsync(token, returnOrderId);
await statusConfirmationService.ConfirmTransferOrderWithStatusUpdateAsync(token, transferOrderId);
await statusConfirmationService.ConfirmSalesOrderWithStatusUpdateAsync(token, salesOrderId);

// Met à jour le statut en SQL Server : INT3PLStatus = 'Processed'
```

#### 4️⃣ **BLExportService**
```csharp
// Exporte les Bons de Livraison depuis SpeedWMS vers Dynamics 365
var statistics = await blExportService.ProcessBLExportAsync(token);

// Processus :
// 1. Récupère tous les BL de SpeedWMS_MSY_SF_RCT
// 2. Valide chaque BL auprès de Dynamics 365
// 3. POST les confirmations de livraison
// 4. Traçabilité dans JSON_OUT
```

#### 5️⃣ **ItemArrivalJournalService**
```csharp
// Traite les journaux de réception des articles
var report = await itemArrivalService.ProcessAllJournalsAsync(token);

// Étapes :
// 1. Crée les en-têtes ItemArrivalJournal (ItemArrivalJournalHeadersV2)
// 2. Crée les lignes détaillées (ItemArrivalJournalLinesV2)
// 3. Confirme les journaux via API personnalisée
// 4. Retrace dans JSON_OUT
```

#### 6️⃣ **InventoryAdjustmentService (INT48)**
```csharp
// Synchronise les ajustements de stock depuis SpeedWMS vers Dynamics 365
var stats = await adjustmentService.ProcessInventoryAdjustmentsAsync(movements);

// Processus :
// 1. Récupère les mouvements d'inventaire de SpeedWMS
// 2. Transforme les données au format Dynamics 365
// 3. POST les ajustements via l'API
// 4. Marque les mouvements comme traités
```

---

## ⚙️ Installation & Configuration

### 1️⃣ Prérequis

- **.NET 8.0 SDK** ou Runtime ([Télécharger](https://dotnet.microsoft.com/download/dotnet/8.0))
- **SQL Server 2019+** (ou accès à 7.2.160.173)
- **Accès Dynamics 365** (instance UAT Sandbox)
- **Enregistrement Azure AD** (application + secret)

### 2️⃣ Configuration Azure AD

**Dans le portail Azure (https://portal.azure.com) :**

1. Accédez à **Azure Active Directory > App registrations**
2. Créez une nouvelle application (ou utilisez l'existante)
3. Notez :
   - **Application (client) ID** → `ClientId`
   - **Directory (tenant) ID** → `TenantId`
4. Créez un **Client Secret** → `ClientSecret`
5. Ajoutez les **permissions API** :
   - Dynamics 365 : `user_impersonation`
   - Accès délégué sur votre instance

### 3️⃣ Fichier `appsettings.json`

Créez ou éditez `appsettings.json` à la racine du projet :

```json
{
  "TenantId": "00000000-0000-0000-0000-000000000000",
  "ClientId": "00000000-0000-0000-0000-000000000000",
  "ClientSecret": "***REPLACE_WITH_YOUR_CLIENT_SECRET***",
  "ResourceUrl": "https://br-uat.sandbox.operations.eu.dynamics.com/",
  "DataAreaId": "br",
  
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=Middleware;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=true;Connection Timeout=30;",
    "SpeedWmsConnection": "Server=YOUR_SERVER;Database=SpeedWMS_MSY_SF_RCT;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=true;Connection Timeout=30;"
  },

  "BLExport": {
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 30,
    "BatchSize": 10,
    "EnableConfirmationPost": true,
    "DefaultInventLocationId": "RECNOLP"
  },

  "ItemArrivalJournal": {
    "HeadersEndpoint": "data/ItemArrivalJournalHeadersV2",
    "LinesEndpoint": "data/ItemArrivalJournalLinesV2",
    "ConfirmationEndpoint": "api/services/BRINT41PostJournalServiceGroup/BRINT41PostJournalService/post",
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 30,
    "BatchSize": 5,
    "EnableConfirmationPost": true,
    "DefaultJournalNameId": "ARR",
    "DefaultReceivingSiteId": "S01",
    "DefaultReceivingWarehouseId": "12",
    "DefaultReceivingWarehouseLocationId": "RECNOLP"
  },

  "ExternalPrograms": {
    "TranslatorEnabled": true,
    "TranslatorPath": "C:\\Applications\\DynamicsToXmlTranslator\\DynamicsToXmlTranslator.exe",
    "TimeoutMinutes": 5
  },

  "Paths": {
    "BaseDirectory": "",
    "ExportsDirectory": "exports",
    "LogsDirectory": "Logs"
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

### 4️⃣ Compilation et lancement

```powershell
# Restaurer les dépendances
dotnet restore

# Compiler le projet
dotnet build --configuration Release

# Exécuter la synchronisation complète
dotnet run

# Exécuter une synchronisation spécialisée
dotnet run articles       # Synchronise les articles uniquement
dotnet run purchase       # Synchronise les commandes d'achat
dotnet run return         # Synchronise les retours
dotnet run transfer       # Synchronise les transferts
dotnet run sales          # Synchronise les commandes de vente
dotnet run blexport       # Export BL SpeedWMS uniquement
dotnet run int48          # Ajustements d'inventaire uniquement
```

---

## 🎯 Fonctionnalités

### ✨ Synchronisation complète

La synchronisation complète (`dotnet run`) exécute dans cet ordre :

#### 1. **Authentification Azure**
- Obtient un token Bearer auprès d'Azure AD
- Valide la configuration (TenantId, ClientId, ClientSecret)

#### 2. **Synchronisation des données principales**
```
Articles          → Sync par date de modification
Purchase Orders   → Sync par hash (détection changement)
Return Orders     → Sync par hash
Transfer Orders   → Sync par hash
Sales Orders      → Sync par hash
```

**Résultats retournés pour chaque endpoint :**
- 📥 Nouveaux records insérés
- 🔄 Records modifiés
- ➖ Records inchangés
- 🗑️ Records supprimés (marqués comme supprimés)
- ⚠️ Records en erreur

#### 3. **Confirmations automatiques**
- Confirme toutes les commandes d'achat → `ProcessedBy3PL`
- Confirme toutes les retours → `ProcessedBy3PL`
- Confirme tous les transferts → `ProcessedBy3PL`
- Confirme tous les bons de livraison → `ProcessedBy3PL`

#### 4. **Export BL SpeedWMS → Dynamics 365**
- Récupère les bons de livraison de SpeedWMS
- Valide chaque BL auprès de Dynamics 365
- POST les confirmations de livraison
- Traçabilité complète dans JSON_OUT

#### 5. **Traitement journaux de réception**
- Crée les en-têtes de réception
- Crée les lignes avec quantités et articles
- Confirme les journaux
- Statistiques détaillées

#### 6. **Ajustements d'inventaire (INT48)**
- Récupère les mouvements de stock de SpeedWMS
- Transforme les données (codes articles, quantités, localisations)
- POST les ajustements vers Dynamics 365
- Marque les mouvements comme traités

#### 7. **Lancement DynamicsToXmlTranslator**
- Lance le programme externe `DynamicsToXmlTranslator.exe`
- Génère les fichiers XML à partir des données synchronisées
- Timeout configurable (par défaut 5 minutes)

### 🎛️ Modes de synchronisation spécialisée

```powershell
# Mode Articles uniquement
dotnet run articles

# Mode Purchase uniquement
dotnet run purchase

# Mode Return uniquement
dotnet run return

# Mode Transfer uniquement
dotnet run transfer

# Mode Sales Orders uniquement
dotnet run sales

# Mode BLExport uniquement
dotnet run blexport

# Mode Confirmation Préparation uniquement
dotnet run cr_prep

# Mode Confirmation Réception uniquement
dotnet run cr_recep

# Mode INT48 (ajustements inventaire)
dotnet run int48
```

Chaque mode :
- Synchronise **uniquement** les données du type spécifié
- Lance le **DynamicsToXmlTranslator** si configuré (sauf pour BLExport et INT48)
- Affiche les statistiques détaillées

---

## 🗄️ Architecture base de données

### Tables principales

#### 1. **JSON_IN** (Middleware - Reception des données de Dynamics)

```sql
CREATE TABLE JSON_IN (
    JSON_KEYU    INT IDENTITY(1,1) PRIMARY KEY,
    JSON_CRDA    DATETIME DEFAULT GETDATE(),
    JSON_FROM    NVARCHAR(255),           -- Endpoint source (ex: data/BRINT34ReleasedProducts)
    JSON_CCLI    NVARCHAR(10) DEFAULT 'BR',
    JSON_DATA    NTEXT,                  -- Données JSON brutes
    JSON_SENT    NVARCHAR(1),            -- 'Y' = confirmé
    JSON_IMPORT_ID NVARCHAR(100)         -- ID import pour traçabilité
)
```

**Endpoints synchronisés dans JSON_IN :**

| Endpoint | Table | Description |
|----------|-------|-------------|
| `data/BRINT34ReleasedProducts` | Articles | Articles/produits |
| `data/BRINT32PurchOrderTables` | Commandes d'achat | Purchase Orders |
| `data/BRINT32ReturnOrderTables` | Retours | Return Orders |
| `data/BRINT32TransferOrderTables` | Transferts | Transfer Orders |
| `data/BRPackingSlipInterfaces` | Commandes de vente | Sales Orders / Packing Slips |

#### 2. **JSON_OUT** (Middleware - Traçabilité des traitements)

```sql
CREATE TABLE JSON_OUT (
    JSON_KEYU     INT IDENTITY(1,1) PRIMARY KEY,
    JSON_CRDA     DATETIME DEFAULT GETDATE(),
    JSON_FROM     NVARCHAR(255),          -- Source du traitement
    JSON_DEST     NVARCHAR(255),          -- Destination (INT48_ADJUSTMENT, BL_EXPORT, etc.)
    JSON_CCLI     NVARCHAR(10) DEFAULT 'BR',
    JSON_DATA     NTEXT,                  -- Données traitées
    JSON_STATUS   NVARCHAR(50),           -- SUCCESS, FAILED, PENDING
    JSON_IMPORT_ID NVARCHAR(100)          -- Traçabilité
)
```

**Destinations tracées dans JSON_OUT :**

| Destination | Traitement | Source |
|-------------|-----------|--------|
| `INT48_ADJUSTMENT` | Ajustements d'inventaire | SpeedWMS → Dynamics 365 |
| `BL_EXPORT` | Export de bons de livraison | SpeedWMS → Dynamics 365 |
| `ITEM_ARRIVAL` | Journaux de réception | JSON_IN → Dynamics 365 |
| `ORDER_CONFIRMATION` | Confirmations de commandes | JSON_IN → Dynamics 365 |

#### 3. **SpeedWMS_MSY_SF_RCT.MVT_DAT** (Mouvements d'inventaire)

```sql
-- Mouvements suivis :
SELECT 
    MVT_KEYU,              -- Clé unique mouvement
    MVT_DATE1,             -- Date du mouvement
    MVT_DATE3,             -- Date de traitement INT48
    MVT_TOP3,              -- Flag traitement (0=À traiter, 1=Traité)
    ART_CODE,              -- Code article
    MVT_QMEU,              -- Quantité
    ACT_CODE               -- Code activité (ex: COSMETIQUE)
FROM SpeedWMS_MSY_SF_RCT.dbo.MVT_DAT
WHERE MVT_TOP3 = 0        -- Mouvements non traités
```

### 📊 Flows de données

#### Flow 1 : Synchronisation simple (Articles)
```
Dynamics 365 API
    ↓
DynamicsDataService.SyncEndpointAsync()
    ↓
Récupération par date ModifiedDate
    ↓
Comparison avec JSON_IN
    ↓
INSERT/UPDATE JSON_IN
    ↓
JSON_OUT (traçabilité)
```

#### Flow 2 : Synchronisation avec confirmation (Orders)
```
Dynamics 365 API
    ↓
DynamicsDataService.SyncEndpointAsync()
    ↓
Récupération par hash SHA256
    ↓
Comparison avec JSON_IN
    ↓
INSERT/UPDATE JSON_IN
    ↓
StatusConfirmationService.ConfirmOrderAsync()
    ↓
UPDATE JSON_IN (JSON_SENT = 'Y')
    ↓
JSON_OUT (traçabilité)
```

#### Flow 3 : Export BL
```
SpeedWMS.RECF
    ↓
BLExportService.ProcessBLExportAsync()
    ↓
Récupération des BL
    ↓
Validation auprès de Dynamics 365
    ↓
POST confirmations /PostPackingSlip
    ↓
UPDATE MVT_DAT (MVT_TOP3 = 1)
    ↓
JSON_OUT (traçabilité BL_EXPORT)
```

#### Flow 4 : Ajustements d'inventaire (INT48)
```
SpeedWMS.MVT_DAT (MVT_TOP3=0)
    ↓
SpeedWmsInventoryRepository.GetPendingMovementsAsync()
    ↓
InventoryTransformationService.TransformMovements()
    ↓
InventoryAdjustmentService.ProcessAsync()
    ↓
POST Dynamics 365 API
    ↓
UPDATE MVT_DAT (MVT_TOP3=1, MVT_DATE3=NOW)
    ↓
JSON_OUT (traçabilité INT48_ADJUSTMENT)
```

---

## 🚀 Guide d'utilisation

### Scénario 1 : Synchronisation complète quotidienne

**Objectif** : Synchroniser tous les données matin et soir

```powershell
# Dans une tâche planifiée Windows (Planificateur de tâches)
cd C:\Users\BDEQUEKER\OneDrive\Bureau\Eurodislog 2024-2025\API_BR - exe\API_BioR
dotnet run
```

**Résultat attendu** :
```
=== API_BIOR - Synchronisation Dynamics 365 vers SQL Server ===
✅ Authentification Azure réussie

📈 === STATISTIQUES ACTUELLES === 📈
📦 Total enregistrements JSON_IN: 15,432
✅ Enregistrements actifs: 15,200
🗑️ Enregistrements supprimés: 232

🚀 === DÉBUT SYNCHRONISATION AVEC CONFIRMATIONS === 🚀

✅ Articles synchronisé: 45 nouveaux, 120 modifiés, 14,900 inchangés
✅ PurchaseOrders synchronisé: 12 nouveaux, 8 modifiés, 1,250 inchangés
✅ ReturnOrders synchronisé: 3 nouveaux, 2 modifiés, 450 inchangés
...

📊 === RÉSULTATS DE SYNCHRONISATION === 📊
✅ Export BL : EXCELLENT (92.5% succès)
🎉 Journaux de réception : EXCELLENT (98% succès)
✅ INT48 : BON (85% succès)

✅ DynamicsToXmlTranslator exécuté avec succès

✅ === SYNCHRONISATION AVEC CONFIRMATIONS ET BLEXPORT TERMINÉE === ✅
⏱️ Durée totale: 12.5 minutes
```

### Scénario 2 : Correction d'erreurs articles

**Objectif** : Resynchroniser les articles après correction en Dynamics

```powershell
dotnet run articles

# Attend quelques minutes...
# Les articles modifiés sont resynchronisés
# Les confirmations articles envoyées à Dynamics
```

### Scénario 3 : Debug d'une commande spécifique

**Objectif** : Vérifier le statut d'une commande d'achat

**Données en SQL :**
```sql
-- Vérifier si la commande est dans JSON_IN
SELECT * FROM JSON_IN 
WHERE JSON_DATA LIKE '%POVendorId%12345%'
  AND JSON_FROM = 'data/BRINT32PurchOrderTables'

-- Vérifier si elle a été confirmée
SELECT * FROM JSON_IN 
WHERE JSON_SENT = 'Y'
  AND JSON_FROM = 'data/BRINT32PurchOrderTables'

-- Vérifier la traçabilité
SELECT * FROM JSON_OUT
WHERE JSON_DEST = 'ORDER_CONFIRMATION'
  AND JSON_DATA LIKE '%12345%'
ORDER BY JSON_CRDA DESC
```

### Scénario 4 : Test de connectivité Dynamics 365

```csharp
// Dans Program.cs - Ajouter une méthode test
private static async Task TestDynamicsConnectivityAsync()
{
    var authService = new AuthenticationService(configuration);
    var token = await authService.GetAccessTokenAsync();
    
    if (string.IsNullOrEmpty(token))
    {
        Console.WriteLine("❌ Authentification échouée");
        return;
    }
    
    Console.WriteLine("✅ Authentification réussie");
    Console.WriteLine($"Token: {token.Substring(0, 50)}...");
    
    // Test appel API
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
    
    var response = await httpClient.GetAsync(
        "https://br-uat.sandbox.operations.eu.dynamics.com/" +
        "data/BRINT34ReleasedProducts?$top=1"
    );
    
    if (response.IsSuccessStatusCode)
        Console.WriteLine("✅ API Dynamics 365 accessible");
    else
        Console.WriteLine($"❌ Erreur API: {response.StatusCode}");
}
```

---

## 📦 Déploiement

### 🖥️ Sur serveur Windows

#### Option 1 : Publication en tant qu'application autonome

```powershell
# Compiler pour Windows 64-bit
dotnet publish -c Release -r win-x64 --self-contained

# Résultat dans : bin/Release/net8.0/win-x64/publish/
# Copier le dossier entier sur le serveur
```

#### Option 2 : Tâche planifiée Windows

**Créer une tâche planifiée pour exécution quotidienne :**

1. Ouvrir **Planificateur de tâches**
2. **Créer une tâche** :
   - **Nom** : API_BioR_DailySynchronization
   - **Déclencheur** : Quotidien à 8h00 et 20h00
   - **Action** : 
     ```
     Program: C:\Program Files\dotnet\dotnet.exe
     Arguments: C:\chemin\vers\API_BioR\API_BioR.dll
     ```
   - **Options** :
     - ☑ Exécuter avec les privilèges les plus élevés
     - ☑ Exécuter si l'utilisateur est connecté ou non

#### Option 3 : Service Windows (.NET)

```xml
<!-- Dans DynamicsApiToDatabase.csproj -->
<PropertyGroup>
  <UseWindowsFormRuntime>true</UseWindowsFormRuntime>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
</ItemGroup>
```

### 🐧 Sur serveur Linux

```bash
# Compiler pour Linux
dotnet publish -c Release -r linux-x64 --self-contained

# Créer un service systemd
sudo nano /etc/systemd/system/api-bior.service

[Unit]
Description=API BioR Synchronization Service
After=network.target

[Service]
Type=oneshot
ExecStart=/usr/bin/dotnet /opt/api-bior/API_BioR.dll
User=api-bior
WorkingDirectory=/opt/api-bior

[Install]
WantedBy=multi-user.target

# Activer et démarrer
sudo systemctl daemon-reload
sudo systemctl enable api-bior.service
```

### ☁️ Sur Azure Container Instances

**Créer une image Docker :**

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY bin/Release/net8.0/publish/ .
ENTRYPOINT ["dotnet", "API_BioR.dll"]
```

```bash
# Compiler
dotnet publish -c Release

# Construire l'image
docker build -t api-bior:2.0 .

# Pousser vers Azure Container Registry
az acr build --registry <registryName> --image api-bior:2.0 .
```

---

## 🔧 Troubleshooting

### ❌ Erreur : "Authentification échouée"

**Cause possible** : Credentials invalides

**Solution** :
```powershell
# Vérifier dans appsettings.json :
# - TenantId correct
# - ClientId correct
# - ClientSecret correct (regarder dans Azure Portal)
# - ResourceUrl au format exact : https://br-uat.sandbox.operations.eu.dynamics.com/

# Test manuel
$tenantId = "00000000-0000-0000-0000-000000000000"
$clientId = "00000000-0000-0000-0000-000000000000"
$clientSecret = "***REPLACE_WITH_YOUR_CLIENT_SECRET***"

$tokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" `
  -Method POST `
  -Body @{
    client_id = $clientId
    client_secret = $clientSecret
    scope = "https://br-uat.sandbox.operations.eu.dynamics.com/.default"
    grant_type = "client_credentials"
  }

if ($tokenResponse.access_token) {
    Write-Host "✅ Token obtenu avec succès"
} else {
    Write-Host "❌ Erreur : $($tokenResponse.error_description)"
}
```

### ❌ Erreur : "Base de données non accessible"

**Cause possible** : Connexion SQL Server invalide

**Solution** :
```powershell
# Tester la connexion
$ConnectionString = "Server=7.2.160.173;Database=Middleware;User Id=eurodislog;Password=euro;TrustServerCertificate=true;"
$Connection = New-Object System.Data.SqlClient.SqlConnection
$Connection.ConnectionString = $ConnectionString

try {
    $Connection.Open()
    Write-Host "✅ Connexion SQL Server réussie"
    $Connection.Close()
} catch {
    Write-Host "❌ Erreur connexion : $_"
}

# Depuis SQL Server Management Studio
sqlcmd -S 7.2.160.173 -U eurodislog -P euro -d Middleware -q "SELECT COUNT(*) FROM JSON_IN"
```

### ❌ Erreur : "Table JSON_IN n'existe pas"

**Cause possible** : Table non créée

**Solution** :
```sql
-- Créer la table JSON_IN si elle n'existe pas
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'JSON_IN')
BEGIN
    CREATE TABLE JSON_IN (
        JSON_KEYU INT IDENTITY(1,1) PRIMARY KEY,
        JSON_CRDA DATETIME DEFAULT GETDATE(),
        JSON_FROM NVARCHAR(255) NOT NULL,
        JSON_CCLI NVARCHAR(10) DEFAULT 'BR',
        JSON_DATA NTEXT NOT NULL,
        JSON_SENT NVARCHAR(1),
        JSON_IMPORT_ID NVARCHAR(100)
    )
    
    CREATE INDEX IX_JSON_FROM ON JSON_IN(JSON_FROM)
    CREATE INDEX IX_JSON_CCLI ON JSON_IN(JSON_CCLI)
    CREATE INDEX IX_JSON_CRDA ON JSON_IN(JSON_CRDA DESC)
    
    PRINT '✅ Table JSON_IN créée avec succès'
END
ELSE
BEGIN
    PRINT '✅ Table JSON_IN existe déjà'
END
```

### ⚠️ Avertissement : "DynamicsToXmlTranslator non disponible"

**Cause possible** : Chemin incorrect ou exe manquant

**Solution** :
```json
// Dans appsettings.json
"ExternalPrograms": {
    "TranslatorEnabled": false,  // Désactiver si pas utilisé
    "TranslatorPath": "C:\\Applications\\DynamicsToXmlTranslator.exe",
    "TimeoutMinutes": 5
}

// Ou vérifier le chemin
if (File.Exists(@"C:\Applications\DynamicsToXmlTranslator.exe")) {
    Console.WriteLine("✅ Translator trouvé");
} else {
    Console.WriteLine("❌ Translator non trouvé");
}
```

### 🐌 Synchronisation très lente

**Cause possible** : Trop de données ou réseau lent

**Solution** :
```json
// Réduire les timeouts dans appsettings.json
"BLExport": {
    "MaxRetryAttempts": 2,      // Réduire de 3 à 2
    "RetryDelaySeconds": 15,    // Réduire de 30 à 15
    "BatchSize": 5              // Réduire de 10 à 5
}

// Ou exécuter en mode spécialisé (plus rapide)
dotnet run articles  // Seulement les articles
```

---

## ❓ FAQ

### Q: Combien de temps prend une synchronisation complète ?

**A:** Entre 10 et 20 minutes selon :
- Nombre d'enregistrements (15k+ articles par défaut)
- Vitesse du réseau
- Charge des serveurs Dynamics et SQL Server
- Nombre de modifications

Référence : Avec 15k articles, 1k commandes, export BL + INT48 = ~12 minutes

---

### Q: Comment savoir si une commande a été confirmée ?

**A:** 
```sql
-- Vérifier le statut de confirmation
SELECT JSON_KEYU, JSON_SENT, JSON_CRDA FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT32PurchOrderTables'
  AND JSON_SENT = 'Y'
ORDER BY JSON_CRDA DESC

-- Ou consulter la traçabilité
SELECT * FROM JSON_OUT
WHERE JSON_DEST = 'ORDER_CONFIRMATION'
ORDER BY JSON_CRDA DESC LIMIT 20
```

---

### Q: Puis-je resynchroniser une commande manuellement ?

**A:** Oui, supprimer l'entrée JSON_IN et relancer la sync :
```sql
-- Supprimer une commande spécifique
DELETE FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT32PurchOrderTables'
  AND JSON_DATA LIKE '%PurchaseId%12345%'

-- Relancer la synchronisation


```

Puis exécuter : `dotnet run purchase`

---

### Q: Qu'est-ce qu'INT48 ?

**A:** INT48 = **Intégration 48 = Ajustements de stock**

Synchronisation des mouvements d'inventaire depuis SpeedWMS vers Dynamics 365 :
- Transferts de stock
- Ajustements quantités
- Changements de localisations
- Mouvements de qualité

Chaque mouvement est tracé via le flag `MVT_TOP3` dans SpeedWMS.

---

### Q: Comment ajouter un nouvel endpoint de synchronisation ?

**A:** 
```csharp
// Dans Program.cs - ExecuteFullSync()

// Ajouter l'appel de synchronisation
var newEndpointResult = await dynamicsService.SyncEndpointWithOrderConfirmationAsync(
    "MyEndpoint",
    "data/MyEndpointPath",
    "MyPrimaryKeyField"
);

Console.WriteLine($"✅ {newEndpointResult.EndpointName}: {newEndpointResult.NewRecords} nouveaux");
```

---

### Q: Puis-je configurer plusieurs clients ?

**A:** Oui, via la colonne `JSON_CCLI` dans JSON_IN et JSON_OUT :

```json
// appsettings.json
"DataAreaId": "br",  // Code client

// Pour multi-clients, créer plusieurs fichiers config
// appsettings.BR.json    → Client BR
// appsettings.AUTRE.json → Autre client
```

---

### Q: Quel est le format des données JSON dans JSON_IN ?

**A:** Format Dynamics 365 OData natif :

```json
{
  "ItemId": "COSM001",
  "modifiedDate": "2025-12-09T14:30:00Z",
  "ItemName": "Crème hydratante BioRécup",
  "ItemType": "Product",
  "QuantityOnHand": 1250,
  "@odata.etag": "W/\"123456789\"",
  // ... autres champs
}
```

---

### Q: Comment monitorer les logs ?

**A:**
```powershell
# Lire les logs en temps réel
Get-Content .\Logs\*.log -Wait

# Chercher les erreurs
Select-String "ERROR|❌" .\Logs\*.log

# Compter les mouvements par heure
Get-Content .\Logs\*.log | 
  Select-String "✅" | 
  Measure-Object
```

---

### Q: Y a-t-il une documentation API complète ?

**A:** La documentation Dynamics 365 se trouve :
- **API OData** : https://docs.microsoft.com/dynamics365/business-central/api-reference/
- **Votre instance** : https://br-uat.sandbox.operations.eu.dynamics.com/api/resources

À adapter selon votre instance et version.

---

## 📝 Changelog

### Version 2.0.0 (Décembre 2025)
- ✨ **Synchronisation vers table JSON_IN**
- ✨ **Confirmations automatiques** (Purchase/Return/Transfer/Sales)
- ✨ **Export BL SpeedWMS → Dynamics 365**
- ✨ **Journaux de réception ItemArrival**
- ✨ **Ajustements d'inventaire INT48**
- ✨ **Lancement DynamicsToXmlTranslator automatique**
- ✅ **Traçabilité complète JSON_OUT**
- ✅ **.NET 8.0** (mis à jour de .NET 6)

### Version 1.0.0 (Initial)
- Synchronisation basique Dynamics 365
- Insertion données SQL Server

---

**🎉 API_BioR est prêt à synchroniser vos données !**
