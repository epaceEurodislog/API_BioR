# 🧬 API_BioR - Synchronisation Dynamics 365

## 📄 Description

**API_BioR** est un outil de synchronisation intelligent entre l'API Dynamics 365 et une base de données MySQL locale. Il récupère et synchronise automatiquement les **articles** et **commandes** (Achat, Retour, Transfert) avec détection intelligente des modifications.

## 🏗️ Architecture Technique

### **Technologies utilisées :**

- **.NET 6.0** (Console Application)
- **C#** avec architecture modulaire
- **MySQL** pour le stockage local
- **HTTP Client** pour l'API Dynamics 365
- **Microsoft.Extensions** pour l'injection de dépendances
- **JSON** pour la sérialisation des données

### **Fichiers de code principaux :**

**📁 Emplacement des fichiers :**

```
API_BioR/
├── Program.cs                      # ← Orchestrateur principal
├── Services/
│   ├── AuthenticationService.cs    # ← Authentification OAuth2
│   ├── ArticlesSyncService.cs      # ← Synchronisation articles
│   ├── OrdersSyncService.cs        # ← Synchronisation commandes
│   └── DatabaseService.cs          # ← Gestion base de données
├── Models/
│   └── DynamicsModels.cs           # ← Classes de données
├── Database/
│   └── DatabaseInitializer.cs     # ← Initialisation des tables
├── DynamicsApiToDatabase.csproj    # ← Configuration projet
├── appsettings.json.example        # ← Template configuration
└── appsettings.json                # ← Configuration (à créer)
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
    "DefaultConnection": "Server=localhost;Port=3306;Database=dynamics_sync;Uid=root;Pwd=VOTRE_MDP;"
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

## 🚀 Installation et Lancement

### **Prérequis**

- **.NET 6.0 SDK** installé
- **MySQL/WAMP/XAMPP** en fonctionnement
- **Accès à l'API Dynamics 365** configuré
- **Permissions Azure AD** accordées

### **1. Installation des dépendances**

```bash
cd API_BioR
dotnet restore
```

### **2. Configuration**

```bash
# Copier le template de configuration
cp appsettings.json.example appsettings.json

# Éditer avec vos paramètres Azure et MySQL
nano appsettings.json
```

### **3. Première exécution**

```bash
dotnet run
```

**L'outil va automatiquement :**

- ✅ Créer la base de données `dynamics_sync`
- ✅ Créer toutes les tables nécessaires
- ✅ Tester l'authentification Azure
- ✅ Lancer la synchronisation complète

## 📊 Architecture de la Base de Données

### **Tables créées automatiquement :**

```sql
-- Table des articles
CREATE TABLE articles_raw (
    id INT PRIMARY KEY AUTO_INCREMENT,
    json_data JSON NOT NULL,
    content_hash VARCHAR(255) NOT NULL,
    api_endpoint VARCHAR(255) DEFAULT 'BRINT34ReleasedProducts',
    item_id VARCHAR(50) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(json_data, '$.ItemId'))) STORED,
    first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    update_count INT DEFAULT 0
);

-- Table des commandes de retour
CREATE TABLE return_orders_raw (
    id INT PRIMARY KEY AUTO_INCREMENT,
    json_data JSON NOT NULL,
    content_hash VARCHAR(255) NOT NULL,
    composite_id VARCHAR(255) NOT NULL,  -- ReturnItemNum + LineNum
    primary_key_value VARCHAR(255),      -- ReturnItemNum
    line_number_value VARCHAR(255),      -- LineNum
    first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    update_count INT DEFAULT 0
);

-- Table des commandes d'achat
CREATE TABLE purch_orders_raw (
    id INT PRIMARY KEY AUTO_INCREMENT,
    json_data JSON NOT NULL,
    content_hash VARCHAR(255) NOT NULL,
    composite_id VARCHAR(255) NOT NULL,  -- PurchId + LineNumber
    primary_key_value VARCHAR(255),      -- PurchId
    line_number_value VARCHAR(255),      -- LineNumber
    first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    update_count INT DEFAULT 0
);

-- Table des logs de synchronisation
CREATE TABLE sync_logs (
    id INT PRIMARY KEY AUTO_INCREMENT,
    sync_type VARCHAR(50) NOT NULL,
    endpoint VARCHAR(255),
    status ENUM('SUCCESS', 'ERROR', 'WARNING') NOT NULL,
    total_articles_processed INT DEFAULT 0,
    new_articles INT DEFAULT 0,
    updated_articles INT DEFAULT 0,
    unchanged_articles INT DEFAULT 0,
    error_count INT DEFAULT 0,
    sync_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    execution_time_ms BIGINT DEFAULT 0,
    error_message TEXT,
    additional_info JSON
);
```

## 🔧 Architecture du Code

### **Program.cs - Orchestrateur principal**

**📄 Fonctions principales :**

```csharp
static async Task Main(string[] args)
{
    // Configuration des services avec injection de dépendances
    var services = ConfigureServices();
    var serviceProvider = services.BuildServiceProvider();

    // Initialisation de la base de données
    var dbInitializer = serviceProvider.GetService<DatabaseInitializer>();
    await dbInitializer.InitializeDatabaseAsync();

    // Authentification
    var authService = serviceProvider.GetService<AuthenticationService>();
    var token = await authService.GetAccessTokenAsync();

    // Synchronisation des articles
    var articlesService = serviceProvider.GetService<ArticlesSyncService>();
    await articlesService.SyncArticlesAsync(token);

    // Synchronisation des commandes
    var ordersService = serviceProvider.GetService<OrdersSyncService>();
    await ordersService.SyncAllOrdersAsync(token);
}
```

### **Services/AuthenticationService.cs - Authentification OAuth2**

**📄 Méthodes d'authentification :**

```csharp
public class AuthenticationService
{
    public async Task<string> GetAccessTokenAsync()
    {
        // Authentification OAuth2 avec Azure AD
        // Gestion du refresh token
        // Validation des paramètres de configuration
    }

    public bool ValidateConfiguration()
    {
        // Validation des paramètres TenantId, ClientId, ClientSecret
        // Vérification de la configuration Azure
    }
}
```

### **Services/ArticlesSyncService.cs - Synchronisation articles**

**📄 Logique de synchronisation intelligente :**

```csharp
public class ArticlesSyncService
{
    public async Task<SyncResult> SyncArticlesAsync(string token)
    {
        // 1. Récupération depuis l'API Dynamics
        var apiArticles = await FetchArticlesFromApiAsync(token);

        // 2. Calcul des hash de contenu
        var articlesWithHashes = ComputeContentHashes(apiArticles);

        // 3. Synchronisation intelligente avec la base
        return await SyncWithDatabaseAsync(articlesWithHashes);
    }

    private async Task<SyncResult> SyncWithDatabaseAsync(List<ArticleWithHash> articles)
    {
        // Récupération des hash existants
        // Détection des nouveaux articles
        // Détection des modifications
        // Mise à jour en base avec statistiques
    }
}
```

### **Services/OrdersSyncService.cs - Synchronisation commandes**

**📄 Gestion des commandes multi-lignes :**

```csharp
public class OrdersSyncService
{
    public async Task SyncAllOrdersAsync(string token)
    {
        var orderEndpoints = GetOrderEndpoints();

        foreach (var endpoint in orderEndpoints)
        {
            await SyncSingleOrderTypeAsync(token, endpoint);
        }
    }

    private List<OrderEndpoint> GetOrderEndpoints()
    {
        return new List<OrderEndpoint>
        {
            new OrderEndpoint
            {
                Name = "ReturnOrders",
                Endpoint = "data/BRINT32ReturnOrderTables",
                TableName = "return_orders_raw",
                PrimaryKeyField = "ReturnItemNum",
                LineNumberField = "LineNum",
                DisplayName = "Commandes de Retour"
            },
            new OrderEndpoint
            {
                Name = "PurchOrders",
                Endpoint = "data/BRINT32PurchOrderTables",
                TableName = "purch_orders_raw",
                PrimaryKeyField = "PurchId",
                LineNumberField = "LineNumber",
                DisplayName = "Commandes d'Achat"
            }
        };
    }
}
```

### **Services/DatabaseService.cs - Gestion base de données**

**📄 Méthodes de synchronisation :**

```csharp
public class DatabaseService
{
    public async Task<SyncResult> SyncArticlesWithDatabaseAsync(List<ArticleWithHash> articles)
    {
        // Synchronisation intelligente avec détection des modifications
        // Gestion des nouveaux articles
        // Mise à jour des articles existants
        // Statistiques de synchronisation
    }

    public async Task<OrderSyncResult> SyncOrderLinesWithDatabaseAsync(JsonElement[] orderLines, OrderEndpoint config)
    {
        // Gestion des ID composites (PrimaryKey + LineNumber)
        // Détection des lignes supprimées
        // Synchronisation des lignes multiples
        // Suivi des modifications par ligne
    }
}
```

### **Models/DynamicsModels.cs - Classes de données**

**📄 Modèles de données :**

```csharp
public class TokenResponse
{
    public string access_token { get; set; }
    public string token_type { get; set; }
    public string expires_in { get; set; }
}

public class SyncResult
{
    public int TotalProcessed { get; set; }
    public int NewArticles { get; set; }
    public int UpdatedArticles { get; set; }
    public int UnchangedArticles { get; set; }
    public int ErrorCount { get; set; }
}

public class OrderEndpoint
{
    public string Name { get; set; }
    public string Endpoint { get; set; }
    public string TableName { get; set; }
    public string PrimaryKeyField { get; set; }
    public string LineNumberField { get; set; }
    public string DisplayName { get; set; }
}
```

## 📋 Fonctionnalités Avancées

### **1. Synchronisation Intelligente**

- **Détection des modifications** par hash SHA256
- **Évitement des doublons** avec identifiants uniques
- **Gestion des suppressions** pour les commandes
- **Statistiques détaillées** de chaque synchronisation

### **2. Gestion des Erreurs**

- **Logs détaillés** dans la table `sync_logs`
- **Retry automatique** en cas d'erreur réseau
- **Validation des données** avant insertion
- **Rapports d'erreur** avec contexte

### **3. Performance**

- **Traitement par batch** pour les gros volumes
- **Index optimisés** sur les champs de recherche
- **Requêtes préparées** pour la sécurité
- **Gestion mémoire** optimisée

### **4. Monitoring**

- **Suivi en temps réel** des synchronisations
- **Historique complet** des opérations
- **Métriques de performance** (temps d'exécution)
- **Alertes** en cas d'anomalie

## 📊 Utilisation et Résultats

### **Résultats de synchronisation affichés :**

```
=== API_BIOR - Synchronisation Dynamics 365 ===
🔐 Authentification en cours...
✅ Token obtenu avec succès

📦 SYNCHRONISATION DES ARTICLES
🔄 Récupération depuis l'API Dynamics...
✅ 1,245 articles récupérés
🔄 Synchronisation intelligente...
✅ Synchronisation terminée

📋 RÉSULTAT DE LA SYNCHRONISATION DES ARTICLES:
✓ Articles traités: 1,245
  - Nouveaux articles ajoutés: 15
  - Articles mis à jour: 23
  - Articles inchangés: 1,207
  - Erreurs: 0

🚚 SYNCHRONISATION DES COMMANDES
📦 Synchronisation des Commandes de Retour...
✅ 156 lignes traitées
📦 Synchronisation des Commandes d'Achat...
✅ 892 lignes traitées
```

### **Requêtes de monitoring utiles :**

```sql
-- Dernière synchronisation
SELECT
    sync_type,
    status,
    total_articles_processed,
    sync_date,
    execution_time_ms / 1000 as duree_secondes
FROM sync_logs
ORDER BY sync_date DESC
LIMIT 10;

-- Articles modifiés aujourd'hui
SELECT COUNT(*) as articles_modifies_aujourd_hui
FROM articles_raw
WHERE DATE(last_updated_at) = CURDATE();

-- Commandes par type
SELECT
    'Commandes Retour' as type,
    COUNT(*) as total_lignes
FROM return_orders_raw
UNION ALL
SELECT
    'Commandes Achat' as type,
    COUNT(*) as total_lignes
FROM purch_orders_raw;

-- Évolution par jour (7 derniers jours)
SELECT
    DATE(sync_date) as date_sync,
    sync_type,
    SUM(new_articles) as nouveaux,
    SUM(updated_articles) as modifies,
    AVG(execution_time_ms / 1000) as duree_moyenne_sec
FROM sync_logs
WHERE sync_date >= DATE_SUB(NOW(), INTERVAL 7 DAY)
GROUP BY DATE(sync_date), sync_type
ORDER BY date_sync DESC;
```

## 🐛 Résolution de Problèmes

### **Erreur d'authentification Azure**

```bash
# Vérifier la configuration
cat appsettings.json

# Tester avec Postman
POST https://login.microsoftonline.com/{TenantId}/oauth2/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id={ClientId}&client_secret={ClientSecret}&resource={ResourceUrl}
```

**Solutions courantes :**

- Vérifier que le **Client Secret** n'a pas expiré
- Contrôler les **permissions** de l'application Azure
- Valider le **TenantId** et **ClientId**

### **Erreur de connexion MySQL**

```bash
# Vérifier que MySQL fonctionne
services.msc  # Windows
sudo systemctl status mysql  # Linux

# Tester la connexion
mysql -u root -p -h localhost -P 3306
```

### **"Table doesn't exist"**

L'outil recrée automatiquement toutes les tables. Si problème :

```sql
-- Supprimer complètement la base
DROP DATABASE IF EXISTS dynamics_sync;

-- Relancer l'outil qui recrée tout
dotnet run
```

### **Données manquantes ou incorrectes**

```sql
-- Consulter les logs d'erreur
SELECT * FROM sync_logs
WHERE status = 'ERROR'
ORDER BY sync_date DESC
LIMIT 10;

-- Voir les détails d'erreur
SELECT
    sync_type,
    error_message,
    additional_info,
    sync_date
FROM sync_logs
WHERE error_message IS NOT NULL;
```

### **Performance lente**

```sql
-- Vérifier la taille des tables
SELECT
    table_name,
    ROUND(((data_length + index_length) / 1024 / 1024), 2) AS "Table Size (MB)"
FROM information_schema.tables
WHERE table_schema = 'dynamics_sync';

-- Optimiser les index
ANALYZE TABLE articles_raw;
ANALYZE TABLE return_orders_raw;
ANALYZE TABLE purch_orders_raw;
```

## 🔄 Automatisation et Planification

### **Script de lancement automatique**

**📄 Fichier à créer :** `sync_auto.bat` (Windows)

```batch
@echo off
cd /d "C:\chemin\vers\API_BioR"
echo [%date% %time%] Début synchronisation >> sync_auto.log
dotnet run >> sync_auto.log 2>&1
echo [%date% %time%] Fin synchronisation >> sync_auto.log
```

**📄 Fichier à créer :** `sync_auto.sh` (Linux)

```bash
#!/bin/bash
cd /chemin/vers/API_BioR
echo "[$(date)] Début synchronisation" >> sync_auto.log
dotnet run >> sync_auto.log 2>&1
echo "[$(date)] Fin synchronisation" >> sync_auto.log
```

### **Planification Windows (Tâches planifiées)**

```batch
# Ouvrir le planificateur de tâches
taskschd.msc

# Créer une nouvelle tâche :
# - Nom : "Sync Dynamics API_BioR"
# - Déclencheur : Quotidien à 06:00
# - Action : Démarrer un programme
# - Programme : C:\chemin\vers\API_BioR\sync_auto.bat
```

### **Planification Linux (Cron)**

```bash
# Éditer le crontab
crontab -e

# Ajouter la ligne pour exécution quotidienne à 6h
0 6 * * * /chemin/vers/API_BioR/sync_auto.sh

# Vérifier la planification
crontab -l
```

## 📈 Monitoring et Maintenance

### **Surveillance quotidienne**

```sql
-- Dashboard de monitoring quotidien
SELECT
    'Dernière sync Articles' as indicateur,
    CONCAT(
        TIMESTAMPDIFF(HOUR, MAX(sync_date), NOW()), 'h ',
        TIMESTAMPDIFF(MINUTE, MAX(sync_date), NOW()) % 60, 'm'
    ) as valeur
FROM sync_logs
WHERE sync_type = 'Articles'
UNION ALL
SELECT
    'Articles traités hier' as indicateur,
    COALESCE(SUM(total_articles_processed), 0) as valeur
FROM sync_logs
WHERE sync_type = 'Articles'
AND DATE(sync_date) = DATE_SUB(CURDATE(), INTERVAL 1 DAY)
UNION ALL
SELECT
    'Erreurs dernières 24h' as indicateur,
    COUNT(*) as valeur
FROM sync_logs
WHERE status = 'ERROR'
AND sync_date >= DATE_SUB(NOW(), INTERVAL 24 HOUR);
```

### **Nettoyage automatique**

```sql
-- Script de maintenance hebdomadaire
-- Supprimer les logs de plus de 3 mois
DELETE FROM sync_logs
WHERE sync_date < DATE_SUB(NOW(), INTERVAL 3 MONTH);

-- Optimiser les tables
OPTIMIZE TABLE articles_raw;
OPTIMIZE TABLE return_orders_raw;
OPTIMIZE TABLE purch_orders_raw;
OPTIMIZE TABLE sync_logs;
```

### **Alertes par email (optionnelles)**

**📄 Fichier à créer :** `check_sync.py`

```python
import mysql.connector
import smtplib
from datetime import datetime, timedelta

def check_last_sync():
    conn = mysql.connector.connect(
        host='localhost',
        database='dynamics_sync',
        user='root',
        password='VOTRE_MDP'
    )

    cursor = conn.cursor()
    cursor.execute("""
        SELECT MAX(sync_date)
        FROM sync_logs
        WHERE status = 'SUCCESS'
    """)

    last_sync = cursor.fetchone()[0]
    if last_sync < datetime.now() - timedelta(hours=25):
        send_alert("Sync API_BioR en retard !")

    conn.close()

def send_alert(message):
    # Configuration email
    smtp_server = "smtp.office365.com"
    sender_email = "votre-email@eurodislog.com"
    # ... code d'envoi email
```

## 🛠️ Développement et Extension

### **Ajouter un nouvel endpoint de commandes**

**1. Dans OrdersSyncService.cs, méthode GetOrderEndpoints() :**

```csharp
new OrderEndpoint
{
    Name = "TransferOrders",
    Endpoint = "data/BRINT32TransferOrderTables",
    TableName = "transfer_orders_raw",
    PrimaryKeyField = "TransferId",
    LineNumberField = "LineNumber",
    DisplayName = "Ordres de Transfert"
}
```

**2. L'outil créera automatiquement :**

- La table `transfer_orders_raw`
- Les index optimisés
- La logique de synchronisation

### **Modifier la structure des données**

**Pour ajouter des champs calculés :**

```sql
-- Exemple : ajouter un champ calculé pour les articles
ALTER TABLE articles_raw
ADD COLUMN category VARCHAR(100)
GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(json_data, '$.Category'))) STORED;

-- Créer un index pour les performances
CREATE INDEX idx_category ON articles_raw(category);
```

### **Personnaliser la logique de synchronisation**

**Fichiers à modifier :**

- `Services/ArticlesSyncService.cs` : pour les articles
- `Services/OrdersSyncService.cs` : pour les commandes
- `Services/DatabaseService.cs` : pour la logique base de données

### **Ajouter des notifications**

```csharp
// Dans Program.cs, après chaque synchronisation
public static async Task SendSlackNotification(SyncResult result)
{
    var webhook = "https://hooks.slack.com/services/...";
    var message = $"✅ Sync terminée: {result.NewArticles} nouveaux, {result.UpdatedArticles} modifiés";

    using var client = new HttpClient();
    await client.PostAsync(webhook, new StringContent(
        JsonSerializer.Serialize(new { text = message })
    ));
}
```

## 📊 Données Stockées

### **Format des données JSON stockées**

**Articles (table articles_raw) :**

```json
{
  "ItemId": "SHSEBO500",
  "Name": "SHAMPOING SEBORREGULATRICE 500ML",
  "Category": "CAPILLAIRE",
  "ExternalItemId": "",
  "GrossWeight": 0.6,
  "Weight": 0.5,
  "Height": 20.5,
  "Width": 6.8,
  "Depth": 6.8,
  "itemBarCode": "3401360016484",
  "ItemGroupId": "SHAMPOOING",
  "UnitId": "ML",
  "INT3PLStatus": "Active",
  "TrackingLot1": 0,
  "TrackingLot2": 0,
  "PdsShelfLife": 1095,
  "OrigCountryRegionId": "FR",
  "dataAreaId": "BRE"
}
```

**Commandes (tables \*\_orders_raw) :**

```json
{
  "ReturnItemNum": "RET-2024-001",
  "LineNum": 1.0,
  "ItemId": "SHSEBO500",
  "OrderedReturnQuantity": 12.0,
  "ReturnUnitPrice": 8.5,
  "CurrencyCode": "EUR",
  "DeliveryDate": "2024-12-15T00:00:00Z",
  "dataAreaId": "BRE"
}
```

## 📞 Support et Contact

### **Logs à consulter en cas de problème :**

1. **Console** : Messages temps réel
2. **Table sync_logs** : Historique complet des synchronisations
3. **Logs système** : Erreurs .NET dans Windows Event Viewer

### **Informations à fournir pour le support :**

- Version de .NET utilisée
- Configuration MySQL (version, paramètres)
- Contenu de `appsettings.json` (sans les secrets)
- Derniers logs d'erreur de la table `sync_logs`
- Taille actuelle des tables de données

### **Vérification de santé rapide :**

```sql
-- Script de diagnostic complet
SELECT 'Articles total' as metric, COUNT(*) as value FROM articles_raw
UNION ALL
SELECT 'Dernière sync articles', TIMESTAMPDIFF(MINUTE, MAX(last_updated_at), NOW()) FROM articles_raw
UNION ALL
SELECT 'Commandes retour', COUNT(*) FROM return_orders_raw
UNION ALL
SELECT 'Commandes achat', COUNT(*) FROM purch_orders_raw
UNION ALL
SELECT 'Erreurs 24h', COUNT(*) FROM sync_logs WHERE status='ERROR' AND sync_date >= DATE_SUB(NOW(), INTERVAL 24 HOUR);
```

---