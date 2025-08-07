# 🧬 API_BioR - Synchronisation Dynamics 365 vers SQL Server

## 📄 Description

**API_BioR** est un outil de synchronisation intelligent entre l'API Dynamics 365 et une base de données **SQL Server** (table JSON_IN). Il récupère et synchronise automatiquement les **articles**, **commandes** (Achat, Retour, Transfert, Vente) avec détection intelligente des modifications et **confirmations automatiques** des commandes.

**🆕 Version SQL Server avec :**

- Synchronisation vers table **JSON_IN** (Middleware)
- Confirmations automatiques Purchase/Return/Transfer/Sales Orders
- Mise à jour **INT3PLStatus**
- Traçabilité complète avec **JSON_OUT**
- Lancement automatique du **DynamicsToXmlTranslator**

## 🏗️ Architecture Technique

### **Technologies utilisées :**

- **.NET 8.0** (Console Application)
- **C#** avec architecture modulaire
- **SQL Server** (base Middleware - 7.2.160.173)
- **HTTP Client** pour l'API Dynamics 365
- **Microsoft.Extensions** pour l'injection de dépendances
- **JSON** pour la sérialisation des données

### **Fichiers de code principaux :**

**📁 Structure du projet :**

```
API_BioR/
├── Program.cs                           # ← Orchestrateur principal
├── Services/
│   ├── AuthenticationService.cs         # ← Authentification OAuth2
│   ├── DynamicsDataService.cs          # ← Synchronisation intelligente
│   ├── StatusConfirmationService.cs    # ← Confirmations commandes/articles
│   ├── SqlServerDatabaseService.cs     # ← Gestion base SQL Server
│   ├── JsonOutService.cs              # ← Traçabilité JSON_OUT
│   └── ExternalProgramLauncher.cs     # ← Lancement DynamicsToXmlTranslator
├── Models/
│   ├── DynamicsModels.cs               # ← Classes de données principales
│   └── JsonOutModels.cs               # ← Modèles traçabilité
├── Utilities/
│   ├── ConfirmationHelper.cs          # ← Helpers confirmation simplifiés
│   ├── StatusHelper.cs                # ← Helpers statut articles
│   └── OrderConfirmationHelper.cs     # ← Helpers confirmation commandes
├── DynamicsApiToDatabase.csproj       # ← Configuration projet .NET 8
├── appsettings.json.example           # ← Template configuration
└── appsettings.json                   # ← Configuration (à créer)
```

## ⚙️ Configuration

### **1. Fichier de configuration**

**📄 Fichier à créer :** `appsettings.json`

```json
{
  "TenantId": "votre-tenant-id-azure",
  "ClientId": "votre-client-id-app",
  "ClientSecret": "votre-client-secret",
  "ResourceUrl": "https://votre-instance.operations.dynamics.com/",
  "ConnectionStrings": {
    "DefaultConnection": "Server=7.2.160.173;Database=Middleware;Uid=votre_user;Pwd=votre_mdp;TrustServerCertificate=True;"
  },
  "ExternalPrograms": {
    "TranslatorEnabled": true,
    "TranslatorPath": "C:\\chemin\\vers\\DynamicsToXmlTranslator.exe",
    "TimeoutMinutes": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### **2. Configuration Azure AD**

**Prérequis Azure :**

- **Application enregistrée** dans Azure AD
- **Permissions** sur l'API Dynamics 365
- **Client Secret** généré et valide

## 🗄️ Architecture de la Base de Données SQL Server

### **Table principale : JSON_IN**

La table **JSON_IN** stocke tous les données synchronisées depuis Dynamics :

```sql
-- Structure de la table JSON_IN (existante dans Middleware)
JSON_KEYU       INT IDENTITY(1,1) PRIMARY KEY    -- Clé auto-incrémentée
JSON_CRDA       DATETIME DEFAULT GETDATE()       -- Date de création
JSON_FROM       NVARCHAR(255)                    -- Endpoint source (ex: data/BRINT34ReleasedProducts)
JSON_CCLI       NVARCHAR(10) DEFAULT 'BR'        -- Code client
JSON_DATA       NTEXT                            -- Données JSON brutes
JSON_TRTP       INT DEFAULT 0                    -- Type transaction
JSON_TRDA       DATETIME DEFAULT GETDATE()       -- Date transaction
JSON_TREN       NVARCHAR(50) DEFAULT 'SPEED'     -- Environnement
JSON_BKEY       NVARCHAR(255)                    -- Clé métier unique
JSON_HASH       NVARCHAR(255)                    -- Hash MD5 du contenu
JSON_STAT       NVARCHAR(20) DEFAULT 'ACTIVE'    -- Statut (ACTIVE/DELETED)
JSON_SENT       BIT DEFAULT 0                    -- 🆕 Colonne confirmation
```

### **Table de traçabilité : JSON_OUT**

La table **JSON_OUT** trace tous les envois vers l'API :

```sql
-- Structure de la table JSON_OUT (existante dans Middleware)
JSON_KEYU       INT IDENTITY(1,1) PRIMARY KEY    -- Clé auto-incrémentée
JSON_CRDA       DATETIME DEFAULT GETDATE()       -- Date de création
JSON_DEST       NVARCHAR(50)                     -- Destination (endpoint raccourci)
JSON_CCLI       NVARCHAR(10) DEFAULT 'BR'        -- Code client
JSON_DATA       NTEXT                            -- Payload JSON envoyé
JSON_TRTP       INT DEFAULT 1                    -- Type transaction (1=envoi)
JSON_TRDA       DATETIME DEFAULT GETDATE()       -- Date transaction
JSON_TREN       NVARCHAR(50)                     -- Environnement/tracking
```

## 🚀 Installation et Lancement

### **Prérequis**

- **.NET 8.0 SDK** installé
- **Accès à SQL Server** (7.2.160.173 - Base Middleware)
- **Accès à l'API Dynamics 365** configuré
- **Permissions Azure AD** accordées
- **DynamicsToXmlTranslator.exe** (optionnel)

### **1. Installation des dépendances**

```bash
cd API_BioR
dotnet restore
```

### **2. Configuration**

```bash
# Copier le template de configuration
cp appsettings.json.example appsettings.json

# Éditer avec vos paramètres Azure et SQL Server
notepad appsettings.json  # Windows
nano appsettings.json     # Linux
```

### **3. Première exécution**

```bash
dotnet run
```

**L'outil va automatiquement :**

- ✅ Vérifier la connexion SQL Server (Middleware)
- ✅ Ajouter la colonne **JSON_SENT** si nécessaire
- ✅ Tester l'authentification Azure
- ✅ Lancer la synchronisation avec confirmations
- ✅ Exécuter **DynamicsToXmlTranslator** (si configuré)

## 🔧 Fonctionnalités Principales

### **1. Synchronisation Intelligente**

**📄 Fichier principal :** `Services/DynamicsDataService.cs`

- **Détection des modifications** par hash MD5
- **Évitement des doublons** avec clés métier uniques
- **Gestion des suppressions** (statut DELETED)
- **Optimisation des confirmations** (évite les doublons)

**Endpoints synchronisés :**

- `data/BRINT34ReleasedProducts` → Articles
- `data/BRINT32PurchOrderTables` → Commandes d'achat
- `data/BRINT32ReturnOrderTables` → Commandes de retour
- `data/BRINT32TransferOrderTables` → Ordres de transfert
- `data/BRPackingSlipInterfaces` → Commandes de vente

### **2. Confirmations Automatiques**

**📄 Fichier principal :** `Services/StatusConfirmationService.cs`

**Articles :**

- Confirmation de réception avec statut **"ProcessedBy3PL"**
- Évite les confirmations en double (optimisation)
- Traçabilité complète dans **JSON_OUT**

**Commandes :**

- **Purchase Orders** : Service `updatePurchOrderStatus` (statut = 2)
- **Return Orders** : Service `updateReturnOrderStatus` (statut = 2)
- **Transfer Orders** : Service `updateTransferOrderStatus` (statut = 2)
- **Sales Orders** : PATCH sur `BRPackingSlipInterfaces` avec **"ProcessedBy3PL"**
- **Mise à jour INT3PLStatus** pour la ligne 1 de chaque commande

### **3. Traçabilité Complète**

**📄 Fichier principal :** `Services/JsonOutService.cs`

- **Enregistrement automatique** de tous les appels API
- **Troncature intelligente** pour éviter les erreurs de taille
- **Suivi des succès/échecs** avec codes HTTP
- **Statistiques** et **nettoyage automatique**

### **4. Utilitaires Simplifiés**

**📄 Fichiers utilitaires :**

**`Utilities/ConfirmationHelper.cs` :**

```csharp
// Confirmer un article
await ConfirmationHelper.ConfirmSingleItemAsync(serviceProvider, "ARTICLE001");

// Confirmer une commande
await ConfirmationHelper.ConfirmPurchaseOrderAsync(serviceProvider, "PO-2024-001");

// Confirmer toutes les commandes actives
var results = await ConfirmationHelper.ConfirmAllActiveOrdersWithReportAsync(serviceProvider);
```

**`Utilities/StatusHelper.cs` :**

```csharp
// Marquer un article comme récupéré
await StatusHelper.MarkArticleAsRetrievedAsync(serviceProvider, "ARTICLE001");

// Marquer plusieurs articles
await StatusHelper.MarkMultipleArticlesAsRetrievedAsync(serviceProvider, listArticles);
```

## 📊 Résultats de Synchronisation

### **Exemple de sortie console :**

```
=== API_BIOR - Synchronisation Dynamics 365 vers SQL Server ===
Version SQL Server avec confirmations commandes - Table JSON_IN
Base de données: 7.2.160.173 - Middleware
🔄 NOUVEAU: Confirmations automatiques Purchase/Return/Transfer/Sales Orders

✅ Authentification Azure réussie

📈 === STATISTIQUES ACTUELLES === 📈
📦 Total enregistrements: 15,234
✅ Enregistrements actifs: 14,891
🗑️ Enregistrements supprimés: 343
📤 Articles confirmés: 12,456 (83.6%)

🚀 === DÉBUT SYNCHRONISATION AVEC CONFIRMATIONS === 🚀

✅ Articles: 234 nouveaux, 45 modifiés, 1,156 inchangés (12.3s)
✅ PurchaseOrders: 12 nouveaux, 3 modifiés, 89 inchangés (8.7s)
✅ ReturnOrders: 5 nouveaux, 1 modifiés, 23 inchangés (4.2s)
✅ TransferOrders: 8 nouveaux, 2 modifiés, 45 inchangés (6.1s)
✅ SalesOrders: 67 nouveaux, 12 modifiés, 234 inchangés (15.4s)

🔄 === CONFIRMATION COMMANDES EN ATTENTE === 🔄
✅ Purchase Orders: 12 confirmées
✅ Return Orders: 5 confirmées
✅ Transfer Orders: 8 confirmées
✅ Sales Orders: 67 confirmées
🎯 Total confirmations: 92 commandes

🔄 === LANCEMENT DU TRANSLATOR === 🔄
🚀 Lancement de DynamicsToXmlTranslator...
✅ DynamicsToXmlTranslator exécuté avec succès

⏱️ Durée totale: 3.2 minutes
✅ === SYNCHRONISATION AVEC CONFIRMATIONS TERMINÉE === ✅
```

### **Requêtes de monitoring utiles :**

```sql
-- Dernière synchronisation
SELECT TOP 10
    JSON_FROM,
    COUNT(*) as nb_enregistrements,
    MAX(JSON_CRDA) as derniere_sync,
    SUM(CASE WHEN JSON_SENT = 1 THEN 1 ELSE 0 END) as confirmes
FROM JSON_IN
WHERE JSON_STAT = 'ACTIVE'
GROUP BY JSON_FROM
ORDER BY derniere_sync DESC;

-- Articles confirmés aujourd'hui
SELECT COUNT(*) as articles_confirmes_aujourdhui
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
AND JSON_SENT = 1
AND CAST(JSON_CRDA AS DATE) = CAST(GETDATE() AS DATE);

-- Traçabilité JSON_OUT dernières 24h
SELECT
    JSON_DEST,
    COUNT(*) as nb_envois,
    AVG(CASE WHEN JSON_TREN LIKE '%SUCCESS%' THEN 100.0 ELSE 0.0 END) as taux_succes
FROM JSON_OUT
WHERE JSON_CRDA >= DATEADD(hour, -24, GETDATE())
GROUP BY JSON_DEST
ORDER BY nb_envois DESC;

-- Commandes en attente de confirmation
SELECT DISTINCT
    JSON_FROM,
    COUNT(*) as commandes_actives
FROM JSON_IN
WHERE JSON_STAT = 'ACTIVE'
AND JSON_FROM LIKE '%Order%'
GROUP BY JSON_FROM;
```

## 🐛 Résolution de Problèmes

### **Erreur de connexion SQL Server**

```bash
# Vérifier la connexion
sqlcmd -S 7.2.160.173 -d Middleware -U votre_user -P votre_mdp

# Test de connectivité réseau
telnet 7.2.160.173 1433
```

**Solutions courantes :**

- Vérifier les **identifiants** SQL Server
- Contrôler les **règles de firewall**
- Valider que **TrustServerCertificate=True** est présent

### **Erreur "Column JSON_SENT doesn't exist"**

L'outil ajoute automatiquement cette colonne. Si problème :

```sql
-- Ajouter manuellement la colonne
ALTER TABLE JSON_IN ADD JSON_SENT BIT DEFAULT 0 NOT NULL;

-- Créer l'index pour les performances
CREATE NONCLUSTERED INDEX IX_JSON_IN_JSON_SENT
ON JSON_IN (JSON_SENT, JSON_FROM, JSON_STAT);
```

### **Confirmations qui échouent**

```sql
-- Vérifier les envois récents
SELECT TOP 20
    JSON_DEST,
    JSON_DATA,
    JSON_TREN,
    JSON_CRDA
FROM JSON_OUT
ORDER BY JSON_CRDA DESC;

-- Statistiques des erreurs
SELECT
    JSON_DEST,
    COUNT(*) as total_envois,
    SUM(CASE WHEN JSON_TREN LIKE '%ERROR%' THEN 1 ELSE 0 END) as erreurs
FROM JSON_OUT
WHERE JSON_CRDA >= DATEADD(day, -1, GETDATE())
GROUP BY JSON_DEST;
```

### **DynamicsToXmlTranslator ne se lance pas**

Vérifier la configuration dans `appsettings.json` :

```json
{
  "ExternalPrograms": {
    "TranslatorEnabled": true,
    "TranslatorPath": "C:\\chemin\\correct\\vers\\DynamicsToXmlTranslator.exe",
    "TimeoutMinutes": 10
  }
}
```

## 🔄 Automatisation et Planification

### **Script de lancement automatique**

**📄 Fichier à créer :** `sync_auto.bat` (Windows)

```batch
@echo off
cd /d "C:\chemin\vers\API_BioR"
echo [%date% %time%] Début synchronisation SQL Server >> sync_auto.log
dotnet run >> sync_auto.log 2>&1
echo [%date% %time%] Fin synchronisation >> sync_auto.log

REM Archiver les logs de plus de 30 jours
forfiles /p "." /m "sync_auto_*.log" /d -30 /c "cmd /c del @path"
```

### **Planification Windows (Tâches planifiées)**

```batch
# Ouvrir le planificateur de tâches
taskschd.msc

# Créer une nouvelle tâche :
# - Nom : "API_BioR Sync SQL Server"
# - Déclencheur : Quotidien à 06:00
# - Action : Démarrer un programme
# - Programme : C:\chemin\vers\API_BioR\sync_auto.bat
# - Conditions : Démarrer uniquement si l'ordinateur est connecté au réseau
```

### **Monitoring automatique avec alertes**

**📄 Fichier à créer :** `check_sync_health.sql`

```sql
-- Script de monitoring à exécuter périodiquement
DECLARE @LastSyncMinutes INT
DECLARE @ErrorCount INT
DECLARE @AlertMessage NVARCHAR(500)

-- Vérifier la dernière synchronisation
SELECT @LastSyncMinutes = DATEDIFF(MINUTE, MAX(JSON_CRDA), GETDATE())
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT34ReleasedProducts';

-- Compter les erreurs récentes
SELECT @ErrorCount = COUNT(*)
FROM JSON_OUT
WHERE JSON_CRDA >= DATEADD(hour, -1, GETDATE())
AND JSON_TREN LIKE '%ERROR%';

-- Générer des alertes
IF @LastSyncMinutes > 90  -- Plus de 1h30 sans sync
    SET @AlertMessage = 'ALERTE: Dernière sync il y a ' + CAST(@LastSyncMinutes AS NVARCHAR(10)) + ' minutes';

IF @ErrorCount > 10       -- Plus de 10 erreurs/heure
    SET @AlertMessage = COALESCE(@AlertMessage + ' | ', '') + 'ALERTE: ' + CAST(@ErrorCount AS NVARCHAR(10)) + ' erreurs dernière heure';

-- Afficher ou envoyer l'alerte
IF @AlertMessage IS NOT NULL
    PRINT @AlertMessage;
ELSE
    PRINT 'Système API_BioR fonctionnel';
```

## 🛠️ Développement et Extension

### **Ajouter un nouvel endpoint**

**📄 Fichier à modifier :** `Services/DynamicsDataService.cs`

Dans la méthode `GetConfiguredEndpoints()` :

```csharp
new EndpointConfig
{
    Name = "InventoryJournals",
    Path = "data/BRInventoryJournalTables",
    PrimaryKeyField = "JournalId",
    DisplayName = "Journaux d'inventaire"
}
```

L'outil synchronisera automatiquement ce nouvel endpoint vers **JSON_IN**.

### **Ajouter une nouvelle confirmation**

**📄 Fichier à modifier :** `Services/StatusConfirmationService.cs`

```csharp
public async Task<bool> ConfirmInventoryJournalAsync(string token, string journalId)
{
    // Logique de confirmation spécifique
    var endpoint = $"{_baseUrl}/api/services/MonService/confirmInventory";
    // ... reste de l'implémentation
}
```

### **Personnaliser les helpers**

**📄 Fichier à créer :** `Utilities/CustomHelper.cs`

```csharp
public static class CustomHelper
{
    public static async Task<bool> ProcessSpecificWorkflowAsync(IServiceProvider serviceProvider, string workflowId)
    {
        // Logique métier spécifique
        var confirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
        // ... implémentation personnalisée
    }
}
```

## 📈 Données et Statistiques

### **Exemples de données JSON stockées**

**Articles (JSON_FROM = 'data/BRINT34ReleasedProducts') :**

```json
{
  "ItemId": "SHSEBO500",
  "Name": "SHAMPOING SEBORREGULATRICE 500ML",
  "INT3PLStatus": "ProcessedBy3PL",
  "itemBarCode": "3401360016484",
  "dataAreaId": "BR"
}
```

**Sales Orders (JSON_FROM = 'data/BRPackingSlipInterfaces') :**

```json
{
  "transRefId": "SO001824",
  "BRPortalOrderNumber": "WEB-001234",
  "WMSTRansRecId": 5637160326,
  "itemId": "STILL",
  "qty": 1.0,
  "INT3PLStatus": "ProcessedBy3PL",
  "expeditionStatus": "Activated"
}
```

### **Statistiques en temps réel**

```sql
-- Dashboard complet
SELECT 'Stat' as Métrique, 'Valeur' as Donnée
UNION ALL
SELECT 'Total JSON_IN', CAST(COUNT(*) AS NVARCHAR(20))
FROM JSON_IN
UNION ALL
SELECT 'Articles actifs', CAST(COUNT(*) AS NVARCHAR(20))
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT34ReleasedProducts' AND JSON_STAT = 'ACTIVE'
UNION ALL
SELECT 'Articles confirmés', CAST(COUNT(*) AS NVARCHAR(20))
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT34ReleasedProducts' AND JSON_SENT = 1
UNION ALL
SELECT 'Envois JSON_OUT 24h', CAST(COUNT(*) AS NVARCHAR(20))
FROM JSON_OUT
WHERE JSON_CRDA >= DATEADD(hour, -24, GETDATE())
UNION ALL
SELECT 'Commandes sync 24h', CAST(COUNT(DISTINCT JSON_FROM) AS NVARCHAR(20))
FROM JSON_IN
WHERE JSON_CRDA >= DATEADD(hour, -24, GETDATE())
AND JSON_FROM LIKE '%Order%';
```

## 📞 Support et Maintenance

### **Logs à consulter :**

1. **Console** : Messages temps réel
2. **JSON_OUT** : Historique des appels API
3. **sync_auto.log** : Logs des exécutions automatiques
4. **Logs Windows** : Erreurs système

### **Nettoyage automatique recommandé :**

```sql
-- Script de maintenance mensuel
-- Nettoyer les anciens logs JSON_OUT (garder 3 mois)
DELETE FROM JSON_OUT
WHERE JSON_CRDA < DATEADD(month, -3, GETDATE());

-- Nettoyer les anciens DELETED de JSON_IN (garder 1 mois)
DELETE FROM JSON_IN
WHERE JSON_STAT = 'DELETED'
AND JSON_CRDA < DATEADD(month, -1, GETDATE());

-- Reconstruire les index pour optimiser les performances
ALTER INDEX ALL ON JSON_IN REBUILD;
ALTER INDEX ALL ON JSON_OUT REBUILD;
```

### **Vérification de santé quotidienne :**

```sql
-- Health check complet
WITH HealthStats AS (
    SELECT
        'Dernière sync articles (minutes)' as Check_Name,
        CAST(DATEDIFF(MINUTE, MAX(JSON_CRDA), GETDATE()) AS NVARCHAR(20)) as Check_Value,
        CASE WHEN DATEDIFF(MINUTE, MAX(JSON_CRDA), GETDATE()) > 90 THEN 'KO' ELSE 'OK' END as Status
    FROM JSON_IN
    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'

    UNION ALL

    SELECT
        'Erreurs dernières 24h',
        CAST(COUNT(*) AS NVARCHAR(20)),
        CASE WHEN COUNT(*) > 50 THEN 'KO' ELSE 'OK' END
    FROM JSON_OUT
    WHERE JSON_CRDA >= DATEADD(hour, -24, GETDATE())
    AND JSON_TREN LIKE '%ERROR%'

    UNION ALL

    SELECT
        'Taux confirmation articles (%)',
        CAST(ROUND(100.0 * SUM(CASE WHEN JSON_SENT = 1 THEN 1.0 ELSE 0.0 END) / COUNT(*), 1) AS NVARCHAR(20)),
        CASE WHEN 100.0 * SUM(CASE WHEN JSON_SENT = 1 THEN 1.0 ELSE 0.0 END) / COUNT(*) < 80 THEN 'KO' ELSE 'OK' END
    FROM JSON_IN
    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts' AND JSON_STAT = 'ACTIVE'
)
SELECT
    Check_Name,
    Check_Value,
    CASE Status
        WHEN 'OK' THEN '✅ ' + Status
        ELSE '❌ ' + Status
    END as Status
FROM HealthStats;
```

---

## 🎯 Points Clés

- **Base de données** : SQL Server Middleware (7.2.160.173)
- **Table principale** : **JSON_IN** avec colonne **JSON_SENT**
- **Traçabilité** : **JSON_OUT** pour tous les appels API
- **Confirmations automatiques** : Articles + 4 types de commandes
- **Performance** : Optimisation anti-doublons et synchronisation intelligente
- **Maintenance** : Scripts de nettoyage et monitoring intégrés

**🚀 Version opérationnelle pour environnement de production BioRécup/Eurodislog !**
