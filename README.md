# 🔄 Synchronisation Dynamics 365 → MySQL

## 📋 Qu'est-ce que c'est ?

Un outil C# qui récupère automatiquement les données depuis l'API Dynamics 365 et les stocke dans une base MySQL locale.

**Utile pour :** Analyses, rapports, intégrations avec d'autres outils sans surcharger Dynamics.

---

## 🚀 Installation rapide

### Prérequis

- WAMP ou XAMPP installé et démarré
- Visual Studio ou VS Code
- .NET 6.0 SDK

### 1. Clone et setup

```bash
git clone [url-du-repo]
cd DynamicsApiToDatabase
```

### 2. Créer le fichier de configuration

**Créer le fichier `appsettings.json`** dans le dossier racine :

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

### 3. Lancer

```bash
dotnet run
```

> ⚠️ **Important :** Le fichier `appsettings.json` ne doit JAMAIS être committé (il contient nos secrets).

---

## 📊 Qu'est-ce qui est synchronisé ?

### Articles (Référentiel Produits)

- **API :** `BRINT34ReleasedProducts`
- **Table :** `articles_raw`
- **Fréquence recommandée :** 1x par jour

### Commandes de Retour

- **API :** `BRINT32ReturnOrderTables`
- **Table :** `return_orders_raw`
- **Particularité :** Gère les lignes multiples par commande

### Commandes d'Achat

- **API :** `BRINT32PurchOrderTables`
- **Table :** `purch_orders_raw`
- **Particularité :** Gère les lignes multiples par commande

### Ordres de Transfert

- **API :** `BRINT32TransferOrderTables`
- **Table :** `transfer_orders_raw`
- **Particularité :** Gère les lignes multiples par commande

---

## 🗄️ Structure de la base de données

### Base créée automatiquement : `dynamics_sync`

#### Tables principales

- **`articles_raw`** - Tous les articles du référentiel
- **`return_orders_raw`** - Lignes des commandes de retour
- **`purch_orders_raw`** - Lignes des commandes d'achat
- **`transfer_orders_raw`** - Lignes des ordres de transfert

#### Tables utilitaires

- **`sync_logs`** - Historique des synchronisations (pour debugging)
- **`article_tags`** - Analyse automatique des champs JSON

### Exemples de requêtes utiles

```sql
-- Voir tous les articles
SELECT item_id, JSON_EXTRACT(json_data, '$.ProductName') as nom_produit
FROM articles_raw
LIMIT 10;

-- Compter les commandes par type
SELECT 'Retour' as type, COUNT(*) FROM return_orders_raw
UNION
SELECT 'Achat' as type, COUNT(*) FROM purch_orders_raw
UNION
SELECT 'Transfert' as type, COUNT(*) FROM transfer_orders_raw;

-- Voir les dernières synchronisations
SELECT endpoint, status, total_articles_processed, sync_date
FROM sync_logs
ORDER BY sync_date DESC
LIMIT 10;
```

---

## 🔧 Configuration avancée

### Changer les paramètres MySQL

Modifier dans `appsettings.json` :

```json
"Database": {
  "Host": "localhost",     // Adresse du serveur MySQL
  "Port": 3306,           // Port MySQL (3306 par défaut)
  "User": "root",         // Utilisateur MySQL
  "Password": "monmdp",   // Mot de passe MySQL
  "Name": "dynamics_sync" // Nom de la base
}
```

### Changer l'environnement Dynamics

```json
"Resource": "https://br-prod.operations.eu.dynamics.com/" // Pour la prod
```

---

## 🐛 Résolution des problèmes courants

### Erreur "Could not connect to MySQL"

1. Vérifier que WAMP/XAMPP est démarré
2. Tester la connexion dans phpMyAdmin
3. Vérifier les paramètres dans `appsettings.json`

### Erreur d'authentification API

1. Vérifier que les credentials sont corrects
2. Tester avec Postman en utilisant la collection fournie
3. Vérifier que l'application Azure a les bonnes permissions

### "Table doesn't exist"

- Supprimer la base `dynamics_sync` dans phpMyAdmin et relancer
- L'outil recrée automatiquement toutes les tables

### Données manquantes ou incorrectes

- Voir les logs dans le dossier `logs/`
- Consulter la table `sync_logs` pour les détails

---

## 📈 Monitoring et maintenance

### Vérifier que tout fonctionne

```sql
-- Dernière synchronisation par endpoint
SELECT
    endpoint,
    status,
    total_articles_processed,
    sync_date,
    execution_time_ms / 1000 as duree_secondes
FROM sync_logs
WHERE sync_date > DATE_SUB(NOW(), INTERVAL 7 DAY)
ORDER BY sync_date DESC;
```

### Nettoyer les vieilles données

```sql
-- Supprimer les logs de plus de 3 mois
DELETE FROM sync_logs
WHERE sync_date < DATE_SUB(NOW(), INTERVAL 3 MONTH);
```

---

## 🛠️ Développement et modification

### Structure du code (dans `Program.cs`)

1. **Configuration et authentification** (lignes 1-100)
2. **Synchronisation des articles** (lignes 101-300)
3. **Synchronisation des commandes** (lignes 301-600)
4. **Gestion de la base de données** (lignes 601-fin)

### Ajouter un nouvel endpoint

**1. Dans la méthode `Main()`, ajouter :**

```csharp
// Après la synchronisation des articles existants
var nouveauEndpoint = new OrderEndpoint
{
    Name = "MonNouvelEndpoint",
    Endpoint = "data/MonNouvelleAPI",
    TableName = "ma_nouvelle_table_raw",
    PrimaryKeyField = "MonId",
    LineNumberField = "MaLigne",
    DisplayName = "Ma Nouvelle API"
};

var resultatNouveau = await SyncOrderDataAsync(token, nouveauEndpoint);
```

**2. La table sera créée automatiquement** avec la structure standard.

### Modifier la logique de synchronisation

- **Articles :** Modifier `SyncArticlesWithDatabaseAsync()`
- **Commandes :** Modifier `SyncOrderDataAsync()`
- **Base de données :** Modifier `CreateOrderTables()`

---

## 📝 Notes importantes

- ⚠️ **Ne jamais committer** `appsettings.json`
- 🔄 **La synchronisation est intelligente** : seules les données modifiées sont mises à jour
- 📊 **Toutes les données sont en JSON** dans les colonnes `json_data`
- 🗂️ **Les ID composites** gèrent les lignes multiples des commandes
- 📈 **L'outil peut tourner plusieurs fois par jour** sans problème

---

**Version :** 4.0 - Mai 2025  
**Dernière mise à jour :** Compatible avec tous les endpoints Dynamics BR UAT
