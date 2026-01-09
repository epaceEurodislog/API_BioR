# 📦 INT34 - Articles (BRINT34ReleasedProducts)

## 🎯 Vue d'ensemble

**INT34** synchronise les **articles/produits** depuis Dynamics 365 vers SQL Server (JSON_IN) et envoie des confirmations de réception.

| Propriété | Valeur |
|-----------|--------|
| **Endpoint Dynamics** | `data/BRINT34ReleasedProducts` |
| **Direction principale** | Dynamics 365 → SQL Server |
| **Direction confirmation** | SQL Server → Dynamics 365 |
| **Commande** | `dotnet run articles` |
| **Clé primaire** | `ItemId` |
| **Table destination** | `JSON_IN` |

---

## 🔄 Flux complet

```
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 1 : RÉCUPÉRATION DEPUIS DYNAMICS 365                      │
│ GET https://{dynamics}/data/BRINT34ReleasedProducts             │
│ Filtres: $filter=INT3PLStatus eq null or INT3PLStatus ne 'ProcessedBy3PL' │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 2 : VÉRIFICATION ANTI-DOUBLON                             │
│ - Hash SHA256 sur JSON complet                                  │
│ - Vérification dans JSON_IN (JSON_FROM + JSON_IMPORT_ID)        │
│ - Si hash identique → IGNORÉ                                    │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 3 : INSERTION JSON_IN                                     │
│ INSERT INTO JSON_IN (                                            │
│   JSON_FROM = 'data/BRINT34ReleasedProducts'                    │
│   JSON_CCLI = 'BR'                                               │
│   JSON_DATA = {JSON complet}                                    │
│   JSON_SENT = NULL                                               │
│   JSON_IMPORT_ID = {ItemId}                                     │
│ )                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 4 : CONFIRMATION VERS DYNAMICS 365 (optionnel)            │
│ POST https://{dynamics}/data/BRINT34ReleasedProducts/           │
│      Microsoft.Dynamics.DataEntities.changeStatus                │
│ Payload: {                                                       │
│   "_itemId": "{ItemId}",                                         │
│   "_status": "ProcessedBy3PL"                                    │
│ }                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 5 : TRAÇABILITÉ JSON_OUT                                  │
│ INSERT INTO JSON_OUT (                                           │
│   JSON_FROM = {ItemId}                                           │
│   JSON_DEST = 'RESPONSE'                                         │
│   JSON_DATA = {Réponse Dynamics}                                 │
│   JSON_TREN = {ItemId}                                           │
│ )                                                                │
└──────────────────────────────────────────────────────────────────┘
```

---

## 📋 Structure des données

### Champs principaux récupérés

```json
{
  "dataAreaId": "br",
  "ItemId": "D14018",
  "ProductName": "Crème Corporelle",
  "ProductDescription": "Description produit",
  "INT3PLStatus": null,
  "ProductType": "Item",
  "ProductColorId": "",
  "ProductConfigurationId": "",
  "ProductSizeId": "",
  "ProductStyleId": "",
  "ProductVersionId": "",
  "SearchName": "D14018",
  "PrimaryVendorAccountNumber": "",
  "ProductCategoryHierarchyName": "",
  "ProductCategoryName": ""
}
```

### Champs obligatoires

| Champ | Type | Description | Nullable |
|-------|------|-------------|----------|
| `dataAreaId` | string | Code entreprise (fixe: "br") | ❌ Non |
| `ItemId` | string | Code article (clé primaire) | ❌ Non |
| `ProductName` | string | Nom du produit | ✅ Oui |
| `INT3PLStatus` | string | Statut traitement 3PL | ✅ Oui |

---

## ⚠️ Variables bloquant l'insertion

### 1. **Hash identique (doublon détecté)**

**Critère** :
```sql
SELECT COUNT(*)
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
  AND JSON_IMPORT_ID = @ItemId
  AND HASHBYTES('SHA2_256', JSON_DATA) = @NewHash
```

**Si COUNT > 0** → Article **IGNORÉ** (déjà synchronisé avec les mêmes données)

**Log** : `ℹ️ Article {ItemId} ignoré (hash identique)`

**Solution** : Normal, aucune action requise (évite les doublons)

---

### 2. **ItemId vide ou null**

**Critère** :
```csharp
if (string.IsNullOrEmpty(itemId))
{
    _logger.LogWarning("⚠️ Article avec ItemId vide/null ignoré");
    continue;
}
```

**Conséquence** : Article **IGNORÉ**

**Solution** : Vérifier les données dans Dynamics 365

---

### 3. **Erreur réseau/API Dynamics 365**

**Critères** :
- HTTP 401 (Authentification)
- HTTP 403 (Permissions)
- HTTP 404 (Endpoint introuvable)
- HTTP 500 (Erreur serveur)
- Timeout réseau

**Conséquence** : **ÉCHEC COMPLET** de la synchronisation

**Log** : `❌ Erreur API Dynamics: HTTP {StatusCode}`

**Solution** :
1. Vérifier token OAuth2
2. Vérifier URL Dynamics dans `appsettings.json`
3. Vérifier connexion réseau

---

### 4. **Erreur insertion SQL Server**

**Critères** :
- Connexion SQL échouée
- Timeout SQL
- Contrainte unique violée
- JSON_DATA trop volumineux (>2GB NTEXT)

**Conséquence** : Article **NON INSÉRÉ** + Erreur loggée

**Log** : `❌ Erreur insertion SQL pour article {ItemId}: {Exception}`

**Solution** :
1. Vérifier `ConnectionStrings:DefaultConnection` dans `appsettings.json`
2. Vérifier droits SQL (INSERT sur JSON_IN)
3. Vérifier taille des données JSON

---

### 5. **Filtrage par statut INT3PLStatus**

**Critère par défaut** :
```odata
$filter=INT3PLStatus eq null or INT3PLStatus ne 'ProcessedBy3PL'
```

**Articles EXCLUS** :
- `INT3PLStatus = 'ProcessedBy3PL'` (déjà traités)

**Articles INCLUS** :
- `INT3PLStatus = null` (nouveaux)
- `INT3PLStatus = ''` (vides)
- `INT3PLStatus = 'Pending'`
- Tout autre statut

**Solution** : Si un article ne remonte pas, vérifier son statut dans Dynamics

---

## 🔧 Configuration appsettings.json

```json
{
  "TenantId": "votre-tenant-id",
  "ClientId": "votre-client-id",
  "ClientSecret": "votre-secret",
  "ResourceUrl": "https://votre-org.operations.dynamics.com/",
  
  "ConnectionStrings": {
    "DefaultConnection": "Server=7.2.160.173;Database=MSY_SF_RCT;User Id=sa;Password=***;"
  },
  
  "ArticlesSync": {
    "EnableConfirmation": true,
    "BatchSize": 50,
    "MaxRetries": 3
  }
}
```

### Variables critiques

| Variable | Impact si manquante/incorrecte |
|----------|-------------------------------|
| `ResourceUrl` | ❌ Échec total - API inaccessible |
| `TenantId` | ❌ Échec authentification |
| `ClientId` | ❌ Échec authentification |
| `ClientSecret` | ❌ Échec authentification |
| `DefaultConnection` | ❌ Échec insertion JSON_IN |

---

## 📊 Traçabilité JSON_IN

### Exemple de ligne insérée

```sql
JSON_KEYU      : 123456
JSON_CRDA      : 2026-01-09 10:30:00.000
JSON_FROM      : data/BRINT34ReleasedProducts
JSON_CCLI      : BR
JSON_DATA      : {"dataAreaId":"br","ItemId":"D14018",...}
JSON_SENT      : NULL (devient 'Y' après confirmation)
JSON_IMPORT_ID : D14018
```

### Requête de vérification

```sql
-- Voir les articles synchronisés aujourd'hui
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_IMPORT_ID as ItemId,
    JSON_SENT as Confirmed,
    CASE 
        WHEN JSON_SENT = 'Y' THEN 'Confirmé'
        WHEN JSON_SENT IS NULL THEN 'En attente'
        ELSE 'Erreur'
    END as Status
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
  AND CAST(JSON_CRDA AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY JSON_CRDA DESC
```

---

## 📊 Traçabilité JSON_OUT (Confirmations)

### Exemple de ligne de confirmation

```sql
JSON_KEYU      : 789012
JSON_CRDA      : 2026-01-09 10:30:05.000
JSON_FROM      : D14018
JSON_DEST      : RESPONSE
JSON_DATA      : {"status":"ProcessedBy3PL"}
JSON_TREN      : D14018_RESPONSE
```

### Requête de vérification

```sql
-- Voir les confirmations envoyées
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_FROM as ItemId,
    JSON_DEST,
    CASE 
        WHEN JSON_DEST = 'RESPONSE' THEN 'Succès'
        WHEN JSON_DEST = 'ERROR' THEN 'Erreur'
        ELSE 'Autre'
    END as Status
FROM JSON_OUT
WHERE JSON_FROM LIKE 'D%'
  AND JSON_DEST IN ('RESPONSE', 'ERROR')
ORDER BY JSON_CRDA DESC
```

---

## 🚀 Commandes d'exécution

### Synchronisation complète avec confirmation

```bash
dotnet run articles
```

### Synchronisation sans confirmation

Modifier `appsettings.json` :
```json
{
  "ArticlesSync": {
    "EnableConfirmation": false
  }
}
```

---

## 🐛 Troubleshooting

### Problème : Aucun article synchronisé

**Vérifications** :
1. Vérifier le filtre OData dans Dynamics 365
2. Vérifier que des articles ont `INT3PLStatus = null`
3. Vérifier les logs : `Logs/log_YYYYMMDD.txt`
4. Tester la connectivité API :
   ```bash
   curl -H "Authorization: Bearer {token}" https://{dynamics}/data/BRINT34ReleasedProducts?$top=1
   ```

---

### Problème : Articles dupliqués dans JSON_IN

**Cause** : Hash différent (données modifiées dans Dynamics)

**Vérification** :
```sql
SELECT 
    JSON_IMPORT_ID,
    COUNT(*) as Occurrences
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
GROUP BY JSON_IMPORT_ID
HAVING COUNT(*) > 1
```

**Solution** : Normal si les données changent. Garder l'historique.

---

### Problème : Erreur "Unable to load the service index"

**Cause** : URL Dynamics incorrecte ou authentification échouée

**Solution** :
1. Vérifier `ResourceUrl` dans `appsettings.json`
2. Régénérer le token OAuth2
3. Vérifier les permissions Azure AD

---

## 📈 Statistiques

### Exemple de sortie console

```
🚀 === DÉBUT SYNCHRONISATION ARTICLES === 🚀
🔍 Récupération des articles depuis Dynamics 365...
📊 125 articles trouvés dans Dynamics 365
🔄 Vérification des articles déjà synchronisés...
📤 45 nouveaux articles à synchroniser
✅ 45/45 articles insérés avec succès
📤 Envoi des confirmations...
✅ 45/45 confirmations envoyées (100.0% succès)
⏱️ Durée totale: 12.5 secondes
✅ === SYNCHRONISATION ARTICLES TERMINÉE === ✅
```

---

## 🔐 Sécurité

### Authentification OAuth2

```csharp
// Authentification Azure AD
var token = await authService.GetAccessTokenAsync();
// Token valide 1 heure, renouvelé automatiquement
```

### Permissions requises

- **Dynamics 365** : `Dynamics 365 API` + Role `System Administrator`
- **SQL Server** : `INSERT`, `SELECT` sur `JSON_IN`, `JSON_OUT`

---

## 📝 Notes importantes

1. ✅ **Anti-doublon automatique** via hash SHA256
2. ✅ **Retry automatique** en cas d'erreur réseau (max 3 tentatives)
3. ✅ **Logs détaillés** dans `Logs/log_YYYYMMDD.txt`
4. ⚠️ **Pas de suppression** : Les articles ne sont jamais supprimés de JSON_IN
5. ⚠️ **Confirmation optionnelle** : Peut être désactivée via config
