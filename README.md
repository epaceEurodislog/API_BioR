# 📚 Documentation Complète - Projet DynamicsApiToDatabase

## 🎯 Vue d'ensemble du projet

### Objectif

Synchroniser intelligemment les données depuis l'API Microsoft Dynamics 365 Finance & Operations vers une base de données MySQL locale, avec gestion avancée des lignes multiples pour les commandes et analyse automatique des balises JSON.

### Architecture

- **Langage :** C# .NET 6.0
- **Base de données :** MySQL (via WAMP)
- **API :** Microsoft Dynamics 365 F&O (authentification OAuth2)
- **Pattern :** Synchronisation intelligente avec détection de changements

---

## 📁 Structure du projet

```
DynamicsApiToDatabase/
├── Program.cs                           # Fichier principal (4 parties)
├── DynamicsApiToDatabase.csproj         # Configuration du projet
├── appsettings.json                     # Configuration (non committé)
├── .gitignore                          # Fichiers à ignorer
├── logs/                               # Dossier des logs (auto-créé)
│   └── dynamics_sync.log               # Logs détaillés
└── README.md                           # Documentation utilisateur
```

---

## 🔧 Configuration du projet

### 1. Fichier `appsettings.json`

```json
{
  "TenantId": "",
  "ClientId": "",
  "ClientSecret": "",
  "Resource": "https://br-uat.sandbox.operations.eu.dynamics.com/",
  "Database": {
    "Host": "localhost",
    "Port": 3306,
    "User": "root",
    "Password": "",
    "Name": "dynamics_sync"
  }
}
```

### 2. Dépendances NuGet

- `Microsoft.Extensions.Configuration` v6.0.1
- `Microsoft.Extensions.Configuration.Json` v6.0.0
- `Microsoft.Extensions.Logging` v6.0.0
- `Microsoft.Extensions.Logging.Console` v6.0.0
- `MySql.Data` v8.0.33
- `Serilog.Extensions.Logging.File` v3.0.0

---

## 🗄️ Structure de la base de données

### Base de données : `dynamics_sync`

#### Table `articles_raw`

Stockage des articles du référentiel produits.

```sql
CREATE TABLE articles_raw (
    id INT AUTO_INCREMENT PRIMARY KEY,
    json_data JSON NOT NULL,
    content_hash VARCHAR(255) NOT NULL,
    api_endpoint VARCHAR(255) DEFAULT 'BRINT34ReleasedProducts',
    item_id VARCHAR(50) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(json_data, '$.ItemId'))) STORED,
    first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    update_count INT DEFAULT 0,
    is_deleted BOOLEAN DEFAULT FALSE,
    deleted_at TIMESTAMP NULL,
    INDEX idx_item_id (item_id),
    INDEX idx_content_hash (content_hash),
    UNIQUE KEY unique_item_id (item_id)
);
```

#### Tables de commandes (avec lignes multiples)

**`return_orders_raw`** - Commandes de retour
**`purch_orders_raw`** - Commandes d'achat  
**`transfer_orders_raw`** - Ordres de transfert

```sql
CREATE TABLE purch_orders_raw (
    id INT AUTO_INCREMENT PRIMARY KEY,
    composite_id VARCHAR(100) NOT NULL COMMENT 'OrderId_LineNumber pour unicité',
    order_id VARCHAR(50) NOT NULL COMMENT 'ID de la commande principale',
    line_number VARCHAR(20) NOT NULL COMMENT 'Numéro de ligne dans la commande',
    json_data JSON NOT NULL,
    content_hash VARCHAR(255) NOT NULL,
    api_endpoint VARCHAR(255) NOT NULL,
    first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    update_count INT DEFAULT 0,
    is_deleted BOOLEAN DEFAULT FALSE,
    deleted_at TIMESTAMP NULL,
    INDEX idx_composite_id (composite_id),
    INDEX idx_order_id (order_id),
    INDEX idx_line_number (line_number),
    UNIQUE KEY unique_composite_id (composite_id)
);
```

#### Table `article_tags`

Analyse automatique des balises JSON des articles.

```sql
CREATE TABLE article_tags (
    id INT AUTO_INCREMENT PRIMARY KEY,
    tag_name VARCHAR(191) NOT NULL,
    data_type VARCHAR(50) NOT NULL,
    first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    occurrence_count INT DEFAULT 0,
    sample_value TEXT,
    is_active BOOLEAN DEFAULT TRUE,
    UNIQUE KEY unique_tag_name (tag_name)
);
```

#### Table `sync_logs`

Historique des synchronisations.

```sql
CREATE TABLE sync_logs (
    id INT AUTO_INCREMENT PRIMARY KEY,
    endpoint VARCHAR(255),
    status ENUM('SUCCESS', 'ERROR', 'WARNING') DEFAULT 'SUCCESS',
    total_articles_processed INT DEFAULT 0,
    new_articles INT DEFAULT 0,
    updated_articles INT DEFAULT 0,
    unchanged_articles INT DEFAULT 0,
    error_count INT DEFAULT 0,
    message TEXT,
    execution_time_ms INT,
    sync_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

---

## 🚀 Endpoints API synchronisés

### Articles

- **Endpoint :** `data/BRINT34ReleasedProducts`
- **Fonction :** Référentiel des produits
- **Identifiant unique :** `ItemId`
- **Gestion :** Synchronisation intelligente avec détection de modifications

### Commandes de Retour

- **Endpoint :** `data/BRINT32ReturnOrderTables`
- **Identifiant commande :** `ReturnItemNum`
- **Numéro de ligne :** `LineNum`
- **Clé composite :** `ReturnItemNum_LineNum`

### Commandes d'Achat

- **Endpoint :** `data/BRINT32PurchOrderTables`
- **Identifiant commande :** `PurchId`
- **Numéro de ligne :** `LineNumber`
- **Clé composite :** `PurchId_LineNumber`

### Ordres de Transfert

- **Endpoint :** `data/BRINT32TransferOrderTables`
- **Identifiant commande :** `TransferId`
- **Numéro de ligne :** `LineNum`
- **Clé composite :** `TransferId_LineNum`

---

## 🧠 Logique de synchronisation intelligente

### Pour les Articles

1. **Récupération** des données depuis l'API
2. **Calcul du hash SHA256** pour chaque article
3. **Comparaison** avec les hash existants en base
4. **Actions selon le cas :**
   - **Nouveau :** Insertion
   - **Modifié :** Mise à jour + incrémentation compteur
   - **Inchangé :** Touch de la date de dernière vérification
   - **Supprimé de l'API :** Marquage `is_deleted = TRUE`

### Pour les Commandes (Lignes multiples)

1. **Formation de l'ID composite** : `{OrderId}_{LineNumber}`
2. **Gestion des types JSON** flexibles (String/Number)
3. **Synchronisation ligne par ligne** individuelle
4. **Détection des doublons** via contrainte unique
5. **Gestion des suppressions** par comparaison des ensembles

### Analyse des Balises JSON

1. **Parcours récursif** de tous les objets JSON
2. **Détection automatique** des nouvelles propriétés
3. **Classification des types** (String, Number, Boolean, Array, Object)
4. **Stockage des métadonnées** : première/dernière occurrence, échantillon de valeur

---

## 🔍 Fonctionnalités avancées

### Gestion des Erreurs

- **Try/Catch** sur chaque opération critique
- **Logging détaillé** avec Serilog
- **Continuation** du processus malgré les erreurs ponctuelles
- **Statistiques complètes** en fin d'exécution

### Détection de Doublons

- **Gestion** des doublons API via `INSERT IGNORE`
- **Contraintes uniques** en base de données
- **Logging** des doublons détectés

### Performance et Monitoring

- **Affichage de progression** en temps réel
- **Chronométrage** précis des opérations
- **Logging structuré** pour audit et debug
- **Optimisation** des requêtes avec index appropriés

---

## 📊 Classes et Structures de données

### Classes principales

#### `SyncResult`

```csharp
public class SyncResult
{
    public int TotalProcessed { get; set; } = 0;
    public int NewArticles { get; set; } = 0;
    public int UpdatedArticles { get; set; } = 0;
    public int UnchangedArticles { get; set; } = 0;
    public int ErrorCount { get; set; } = 0;
}
```

#### `OrderSyncResult`

```csharp
public class OrderSyncResult
{
    public int TotalProcessed { get; set; } = 0;
    public int NewOrderLines { get; set; } = 0;
    public int UpdatedOrderLines { get; set; } = 0;
    public int UnchangedOrderLines { get; set; } = 0;
    public int ErrorCount { get; set; } = 0;
    public string OrderType { get; set; } = "";
}
```

#### `OrderEndpoint`

```csharp
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

#### `ArticleTagInfo`

```csharp
public class ArticleTagInfo
{
    public string TagName { get; set; }
    public string DataType { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int OccurrenceCount { get; set; }
    public string SampleValue { get; set; }
}
```

---

## 🏃‍♂️ Utilisation

### Prérequis

1. **WAMP/XAMPP** installé et démarré
2. **MySQL** accessible sur localhost:3306
3. **Accès à l'API** Dynamics configuré
4. **.NET 6.0** SDK installé

### Lancement

```bash
cd DynamicsApiToDatabase
dotnet run
```

### Sortie type

```
=== Synchronisation intelligente des articles Dynamics avec gestion des lignes multiples ===
✓ Authentification réussie
✓ Base de données initialisée

📦 === SYNCHRONISATION DES ARTICLES ===
✓ 1247 articles trouvés dans l'API
🔄 Début de la synchronisation intelligente...
✓ 1247 articles existants trouvés
➕ 12 nouveaux articles ajoutés
🔄 23 articles mis à jour
✓ Analyse des balises terminée: 156 balises gérées

🚚 === SYNCHRONISATION DES COMMANDES AVEC LIGNES MULTIPLES ===
📦 Synchronisation des Commandes de Retour...
✓ 5 lignes de commandes de retour trouvées dans l'API
➕ 5 nouvelles lignes ajoutées

📦 Synchronisation des Commandes d'Achat...
✓ 17 lignes de commandes d'achat trouvées dans l'API
➕ 15 nouvelles lignes ajoutées
⚠️ 2 doublons détectés et ignorés

📦 Synchronisation des Ordres de Transfert...
✓ 8 lignes de ordres de transfert trouvées dans l'API
➕ 8 nouvelles lignes ajoutées

🎉 === SYNCHRONISATION GLOBALE TERMINÉE ===
⏱️ Temps total d'exécution: 45281ms
```

---

## 🛠️ Maintenance et Evolution

### Ajout d'un nouvel endpoint

1. **Ajouter** la configuration dans `orderEndpoints`
2. **Créer** la table correspondante dans `CreateOrderTables`
3. **Tester** la synchronisation

### Modification de la structure JSON

- Les balises sont **automatiquement détectées**
- Nouvelles propriétés **loggées** automatiquement
- **Pas de modification** de code nécessaire

### Optimisation des performances

- **Index** sur les colonnes de recherche fréquente
- **Pagination** possible pour gros volumes
- **Parallélisation** envisageable pour les endpoints

---

## 🔒 Sécurité

### Données sensibles

- **appsettings.json** exclu du versioning
- **Secrets** stockés en configuration locale
- **Logging** sans exposition des tokens

### Accès base de données

- **Paramètres SQL** sécurisés contre l'injection
- **Connexions** fermées automatiquement
- **Transactions** pour la cohérence

---

## 📝 Historique des versions

### Version 1.0 - Synchronisation de base

- Synchronisation simple des articles
- Base de données MySQL
- Logging basique

### Version 2.0 - Synchronisation intelligente

- Détection des modifications par hash
- Gestion des suppressions
- Optimisations de performance

### Version 3.0 - Gestion des lignes multiples

- Support des commandes multi-lignes
- Clés composites
- Gestion flexible des types JSON

### Version 4.0 - Analyse avancée

- Détection automatique des balises
- Gestion des doublons API
- Logging enrichi et monitoring

---

## 🤝 Support et Contact

### Problèmes courants

**Erreur de connexion MySQL :**

- Vérifier que WAMP est démarré
- Contrôler les paramètres de connexion
- Tester l'accès MySQL depuis phpMyAdmin

**Erreur d'authentification API :**

- Vérifier les credentials dans appsettings.json
- Contrôler la validité du token
- Vérifier les permissions de l'application Azure

**Doublons détectés :**

- Normal pour certains endpoints
- Gérés automatiquement par `INSERT IGNORE`
- Vérifier les logs pour le détail

### Logs et Debug

- **Logs console** : Affichage temps réel
- **Logs fichier** : `logs/dynamics_sync.log`
- **Logs base** : Table `sync_logs`

---

## 📈 Métriques et KPIs

### Métriques collectées

- **Temps d'exécution** par endpoint
- **Nombre d'enregistrements** traités
- **Taux de succès/échec**
- **Détection des modifications**
- **Nouvelles balises découvertes**

### Tableaux de bord possibles

```sql
-- Évolution du nombre d'articles
SELECT DATE(sync_date), new_articles, updated_articles
FROM sync_logs
WHERE endpoint = 'data/BRINT34ReleasedProducts'
ORDER BY sync_date DESC;

-- Performance des synchronisations
SELECT endpoint, AVG(execution_time_ms), COUNT(*)
FROM sync_logs
GROUP BY endpoint;

-- Nouvelles balises détectées
SELECT tag_name, data_type, first_seen_at
FROM article_tags
ORDER BY first_seen_at DESC
LIMIT 20;
```

---

_Cette documentation est maintenue à jour et évolue avec le projet. Dernière mise à jour : Mai 2025_
