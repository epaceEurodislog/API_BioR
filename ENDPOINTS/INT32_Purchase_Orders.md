# 📦 INT32 - Purchase Orders (Commandes d'achat)

## 🎯 Vue d'ensemble

**INT32 Purchase Orders** synchronise les **commandes d'achat** depuis Dynamics 365 vers SQL Server et envoie des confirmations de traitement.

| Propriété | Valeur |
|-----------|--------|
| **Endpoint GET** | `data/BRINT32PurchOrderTables` |
| **Endpoint confirmation** | `api/services/BRINT32ServiceGroup/BRINT32Service/updatePurchOrderStatus` |
| **Direction GET** | Dynamics 365 → SQL Server |
| **Direction confirmation** | SQL Server → Dynamics 365 |
| **Commande** | `dotnet run purchase` |
| **Clé primaire** | `PurchId` (ex: OA24000761) |
| **Clé version** | `PurchOrderDocNum` (ex: OA24000761-2) |

---

## 🔄 Flux complet

```
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 1 : RÉCUPÉRATION DEPUIS DYNAMICS 365                      │
│ GET https://{dynamics}/data/BRINT32PurchOrderTables             │
│ $filter=INT3PLStatus eq null or INT3PLStatus ne 'ProcessedBy3PL'│
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 2 : VÉRIFICATION ANTI-DOUBLON (Hash SHA256)               │
│ - Hash sur JSON complet                                         │
│ - Vérification dans JSON_IN                                     │
│ - Si hash identique → IGNORÉ                                    │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 3 : INSERTION JSON_IN                                     │
│ INSERT INTO JSON_IN (                                            │
│   JSON_FROM = 'data/BRINT32PurchOrderTables'                    │
│   JSON_IMPORT_ID = {PurchOrderDocNum}                           │
│   JSON_DATA = {JSON complet}                                    │
│ )                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 4 : RÉCUPÉRATION VERSIONS (PurchTableVersion)             │
│ SELECT PurchTableVersion                                         │
│ FROM BRINT32_PURCHTABLE                                          │
│ WHERE PurchId = 'OA24000761'                                     │
│ → Peut retourner plusieurs versions (ex: 680035, 680042)        │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 5 : CONFIRMATION POUR CHAQUE VERSION                      │
│ POST https://{dynamics}/api/services/                            │
│      BRINT32ServiceGroup/BRINT32Service/updatePurchOrderStatus   │
│ Pour chaque PurchTableVersion:                                   │
│ {                                                                │
│   "_dataAreaId": "BR",                                           │
│   "_id": "680035",                                               │
│   "_status": 2                                                   │
│ }                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 6 : MISE À JOUR INT3PLStatus (LIGNE 1 UNIQUEMENT)         │
│ POST https://{dynamics}/data/BRINT34ReleasedProducts/           │
│      Microsoft.Dynamics.DataEntities.changeStatus                │
│ {                                                                │
│   "_itemId": "{ItemId de la ligne 1}",                          │
│   "_status": "Processed"                                         │
│ }                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 7 : TRAÇABILITÉ JSON_OUT                                  │
│ INSERT INTO JSON_OUT (                                           │
│   JSON_FROM = 'PURCH_STATUS_{PurchId}_V{Version}'              │
│   JSON_DEST = 'RESPONSE' ou 'ERROR'                             │
│   JSON_DATA = {Réponse Dynamics}                                 │
│ )                                                                │
└──────────────────────────────────────────────────────────────────┘
```

---

## 📋 Structure des données

### Données récupérées (GET)

```json
{
  "dataAreaId": "br",
  "PurchId": "OA24000761",
  "PurchOrderDocNum": "OA24000761-2",
  "OrderVendorAccountNumber": "V0001",
  "PurchOrderName": "Commande test",
  "INT3PLStatus": null,
  "DeliveryAddressName": "Entrepôt BioRécup",
  "InvoiceVendorAccountNumber": "V0001",
  "LineNumber": 1.0,
  "ItemNumber": "D14018",
  "OrderedPurchaseQuantity": 100.0,
  "PurchaseUnitSymbol": "pcs",
  "PurchasePrice": 25.50
}
```

### Payload confirmation (POST)

```json
{
  "_dataAreaId": "BR",
  "_id": "680035",
  "_status": 2
}
```

### Champs obligatoires

| Champ | Type | Nullable | Description |
|-------|------|----------|-------------|
| `dataAreaId` | string | ❌ Non | Code entreprise (fixe: "br") |
| `PurchId` | string | ❌ Non | Numéro commande |
| `PurchOrderDocNum` | string | ❌ Non | Numéro commande avec version |
| `OrderVendorAccountNumber` | string | ✅ Oui | Compte fournisseur |
| `LineNumber` | decimal | ❌ Non | Numéro de ligne |
| `ItemNumber` | string | ❌ Non | Code article |

---

## ⚠️ Variables bloquant l'insertion

### 1. **Hash identique (doublon)**

**Critère** :
```sql
SELECT COUNT(*)
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT32PurchOrderTables'
  AND JSON_IMPORT_ID = 'OA24000761-2'
  AND HASHBYTES('SHA2_256', JSON_DATA) = @NewHash
```

**Si COUNT > 0** → Purchase Order **IGNORÉE**

**Solution** : Normal, évite les doublons

---

### 2. **PurchId ou PurchOrderDocNum vide**

**Critère** :
```csharp
if (string.IsNullOrEmpty(purchId) || string.IsNullOrEmpty(purchOrderDocNum))
{
    _logger.LogWarning($"⚠️ Purchase Order avec ID vide ignorée");
    continue;
}
```

**Conséquence** : Purchase Order **IGNORÉE**

**Solution** : Vérifier les données dans Dynamics 365

---

### 3. **INT3PLStatus = 'ProcessedBy3PL'**

**Critère filtre OData** :
```odata
$filter=INT3PLStatus eq null or INT3PLStatus ne 'ProcessedBy3PL'
```

**Purchase Orders EXCLUES** :
- `INT3PLStatus = 'ProcessedBy3PL'` (déjà traitées)

**Purchase Orders INCLUSES** :
- `INT3PLStatus = null` (nouvelles)
- Tout autre statut

**Solution** : Si une commande ne remonte pas, vérifier son statut INT3PLStatus

---

### 4. **Aucune version PurchTableVersion trouvée**

**Critère** :
```sql
SELECT PurchTableVersion
FROM BRINT32_PURCHTABLE
WHERE PurchId = 'OA24000761'
```

**Si résultat vide** → Confirmation **ÉCHOUÉE**

**Log** : `❌ Aucune version trouvée pour Purchase Order OA24000761`

**Solution** :
1. Vérifier table `BRINT32_PURCHTABLE` existe
2. Vérifier mapping Purchase Order → PurchTableVersion
3. Vérifier que la commande a bien été synchronisée dans la table

---

### 5. **Erreur HTTP confirmation (POST)**

**Critères** :
- HTTP 400 : Données invalides (vérifier `_id`, `_status`)
- HTTP 401 : Token expiré
- HTTP 404 : Service introuvable
- HTTP 500 : Erreur serveur

**Conséquence** : Confirmation **ÉCHOUÉE** + JSON_OUT avec erreur

**Solution** :
1. Vérifier endpoint service : `/api/services/BRINT32ServiceGroup/BRINT32Service/updatePurchOrderStatus`
2. Vérifier payload (dataAreaId, id, status)
3. Vérifier logs détaillés

---

### 6. **Erreur mise à jour INT3PLStatus (ligne 1)**

**Critère** :
```csharp
var firstLine = orderLines.Where(l => l.LineNumber == 1).FirstOrDefault();
if (firstLine != null && !string.IsNullOrEmpty(firstLine.ItemId))
{
    await UpdateItemInt3PLStatusAsync(token, firstLine.ItemId, "Processed");
}
```

**Si ligne 1 sans ItemId** → Mise à jour **IGNORÉE** (warning)

**Solution** : Vérifier que la ligne 1 a un ItemId valide

---

## 🔧 Configuration appsettings.json

```json
{
  "PurchaseOrdersSync": {
    "EnableConfirmation": true,
    "ConfirmAllVersions": true,
    "UpdateLine1Only": true,
    "MaxRetries": 3
  }
}
```

### Variables critiques

| Variable | Impact |
|----------|--------|
| `EnableConfirmation` | Si false → Pas de confirmation envoyée |
| `ConfirmAllVersions` | Si false → Seule la 1ère version confirmée |
| `UpdateLine1Only` | Si false → Toutes les lignes mises à jour (non recommandé) |

---

## 📊 Traçabilité JSON_IN

### Exemple de ligne

```sql
JSON_KEYU      : 456789
JSON_CRDA      : 2026-01-09 11:00:00
JSON_FROM      : data/BRINT32PurchOrderTables
JSON_CCLI      : BR
JSON_DATA      : {"dataAreaId":"br","PurchId":"OA24000761",...}
JSON_SENT      : NULL (devient 'Y' après confirmation)
JSON_IMPORT_ID : OA24000761-2
```

### Requête de vérification

```sql
-- Commandes synchronisées aujourd'hui
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_IMPORT_ID as PurchOrderDocNum,
    JSON_SENT as Confirmed,
    CASE 
        WHEN JSON_SENT = 'Y' THEN 'Confirmée'
        WHEN JSON_SENT IS NULL THEN 'En attente'
        ELSE 'Erreur'
    END as Status
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT32PurchOrderTables'
  AND CAST(JSON_CRDA AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY JSON_CRDA DESC
```

---

## 📊 Traçabilité JSON_OUT

### Exemple de confirmation réussie

```sql
JSON_KEYU      : 890123
JSON_CRDA      : 2026-01-09 11:00:05
JSON_FROM      : PURCH_STATUS_OA24000761_V680035
JSON_DEST      : RESPONSE
JSON_DATA      : {"status":"success"}
```

### Requête de vérification

```sql
-- Confirmations Purchase Orders
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_FROM,
    JSON_DEST,
    JSON_DATA
FROM JSON_OUT
WHERE JSON_FROM LIKE 'PURCH_STATUS_%'
ORDER BY JSON_CRDA DESC
```

---

## 🚀 Commandes d'exécution

### Synchronisation Purchase Orders uniquement

```bash
dotnet run purchase
```

---

## 🐛 Troubleshooting

### Problème : Purchase Order non confirmée

**Vérifications** :
1. Vérifier PurchTableVersion existe :
   ```sql
   SELECT * FROM BRINT32_PURCHTABLE WHERE PurchId = 'OA24000761'
   ```
2. Vérifier logs : `Logs/purchase_orders_YYYYMMDD.log`
3. Tester endpoint service manuellement

---

### Problème : Plusieurs versions confirmées

**Comportement normal** : Le code confirme **TOUTES** les versions

**Exemple** :
```
📋 2 version(s) trouvée(s) pour OA24000761: 680035, 680042
🔄 Traitement version 680035...
✅ Purchase Order OA24000761 version 680035 confirmée
🔄 Traitement version 680042...
✅ Purchase Order OA24000761 version 680042 confirmée
```

**Si non désiré** : Modifier config `ConfirmAllVersions: false`

---

### Problème : Ligne 1 non mise à jour

**Cause** : Ligne 1 sans ItemId

**Solution** : Vérifier les données de la commande :
```sql
SELECT 
    PurchId,
    LineNumber,
    ItemNumber
FROM BRINT32_PURCHTABLE
WHERE PurchId = 'OA24000761'
ORDER BY LineNumber
```

---

## 📝 Notes importantes

1. ✅ **Gestion multi-versions** : Confirme toutes les versions d'une Purchase Order
2. ✅ **Mise à jour ligne 1 uniquement** : Évite la surcharge
3. ⚠️ **PurchTableVersion requis** : Table mapping nécessaire
4. ⚠️ **Endpoint service** : Différent des endpoints OData classiques
5. ⚠️ **Status = 2** : Code fixe pour "Processed"

---

## 🔗 Relations avec autres flux

| Flux | Lien |
|------|------|
| **INT34 (Articles)** | Mise à jour INT3PLStatus des articles référencés |
| **INT36 (BL Export)** | BL confirment les réceptions liées aux Purchase Orders |
