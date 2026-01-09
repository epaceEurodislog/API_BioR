# 🔄 INT32 - Transfer Orders (Ordres de transfert)

## 🎯 Vue d'ensemble

**INT32 Transfer Orders** synchronise les **ordres de transfert** (mouvement de stock entre sites) depuis Dynamics 365 vers SQL Server et envoie des confirmations de traitement.

| Propriété | Valeur |
|-----------|--------|
| **Endpoint GET** | `data/BRINT32TransferOrderTables` |
| **Endpoint confirmation** | `api/services/BRINT32ServiceGroup/BRINT32Service/updateTransferOrderStatus` |
| **Direction GET** | Dynamics 365 → SQL Server |
| **Direction confirmation** | SQL Server → Dynamics 365 |
| **Commande** | `dotnet run transfer` |
| **Clé primaire** | `TransferId` (ex: TR24000088) |
| **Clé version** | `TransferOrderDocNum` (ex: TR24000088-1) |

---

## 🔄 Flux complet

```
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 1 : RÉCUPÉRATION DEPUIS DYNAMICS 365                      │
│ GET https://{dynamics}/data/BRINT32TransferOrderTables          │
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
│   JSON_FROM = 'data/BRINT32TransferOrderTables'                 │
│   JSON_IMPORT_ID = {TransferOrderDocNum}                        │
│   JSON_DATA = {JSON complet}                                    │
│ )                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 4 : RÉCUPÉRATION VERSIONS (TransferTableVersion)          │
│ SELECT TransferTableVersion                                      │
│ FROM BRINT32_TRANSFERTABLE                                       │
│ WHERE TransferId = 'TR24000088'                                  │
│ → Peut retourner plusieurs versions (ex: 890020, 890025)        │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 5 : CONFIRMATION POUR CHAQUE VERSION                      │
│ POST https://{dynamics}/api/services/                            │
│    BRINT32ServiceGroup/BRINT32Service/updateTransferOrderStatus  │
│ Pour chaque TransferTableVersion:                                │
│ {                                                                │
│   "_dataAreaId": "BR",                                           │
│   "_id": "890020",                                               │
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
│   JSON_FROM = 'TRANSFER_STATUS_{TransferId}_V{Version}'        │
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
  "TransferId": "TR24000088",
  "TransferOrderDocNum": "TR24000088-1",
  "INT3PLStatus": null,
  "InventLocationIdFrom": "PARIS",
  "InventLocationIdTo": "LYON",
  "DeliveryAddressName": "Entrepôt Lyon",
  "LineNumber": 1.0,
  "ItemNumber": "D14018",
  "TransferQuantity": 200.0,
  "TransferUnitSymbol": "pcs",
  "RequestedShippingDate": "2026-01-15T00:00:00Z"
}
```

### Payload confirmation (POST)

```json
{
  "_dataAreaId": "BR",
  "_id": "890020",
  "_status": 2
}
```

### Champs obligatoires

| Champ | Type | Nullable | Description |
|-------|------|----------|-------------|
| `dataAreaId` | string | ❌ Non | Code entreprise (fixe: "br") |
| `TransferId` | string | ❌ Non | Numéro transfert |
| `TransferOrderDocNum` | string | ❌ Non | Numéro transfert avec version |
| `InventLocationIdFrom` | string | ❌ Non | Site d'origine |
| `InventLocationIdTo` | string | ❌ Non | Site de destination |
| `LineNumber` | decimal | ❌ Non | Numéro de ligne |
| `ItemNumber` | string | ❌ Non | Code article |

---

## ⚠️ Variables bloquant l'insertion

### 1. **Hash identique (doublon)**

**Critère** :
```sql
SELECT COUNT(*)
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT32TransferOrderTables'
  AND JSON_IMPORT_ID = 'TR24000088-1'
  AND HASHBYTES('SHA2_256', JSON_DATA) = @NewHash
```

**Si COUNT > 0** → Transfer Order **IGNORÉ**

**Solution** : Normal, évite les doublons

---

### 2. **TransferId ou TransferOrderDocNum vide**

**Critère** :
```csharp
if (string.IsNullOrEmpty(transferId) || string.IsNullOrEmpty(transferOrderDocNum))
{
    _logger.LogWarning($"⚠️ Transfer Order avec ID vide ignoré");
    continue;
}
```

**Conséquence** : Transfer Order **IGNORÉ**

**Solution** : Vérifier les données dans Dynamics 365

---

### 3. **INT3PLStatus = 'ProcessedBy3PL'**

**Critère filtre OData** :
```odata
$filter=INT3PLStatus eq null or INT3PLStatus ne 'ProcessedBy3PL'
```

**Transfer Orders EXCLUS** :
- `INT3PLStatus = 'ProcessedBy3PL'` (déjà traités)

**Transfer Orders INCLUS** :
- `INT3PLStatus = null` (nouveaux)
- Tout autre statut

**Solution** : Si un transfert ne remonte pas, vérifier son statut INT3PLStatus

---

### 4. **Aucune version TransferTableVersion trouvée**

**Critère** :
```sql
SELECT TransferTableVersion
FROM BRINT32_TRANSFERTABLE
WHERE TransferId = 'TR24000088'
```

**Si résultat vide** → Confirmation **ÉCHOUÉE**

**Log** : `❌ Aucune version trouvée pour Transfer Order TR24000088`

**Solution** :
1. Vérifier table `BRINT32_TRANSFERTABLE` existe
2. Vérifier mapping Transfer Order → TransferTableVersion
3. Vérifier que le transfert a bien été synchronisé dans la table

---

### 5. **Erreur HTTP confirmation (POST)**

**Critères** :
- HTTP 400 : Données invalides (vérifier `_id`, `_status`)
- HTTP 401 : Token expiré
- HTTP 404 : Service introuvable
- HTTP 500 : Erreur serveur

**Conséquence** : Confirmation **ÉCHOUÉE** + JSON_OUT avec erreur

**Solution** :
1. Vérifier endpoint service : `/api/services/BRINT32ServiceGroup/BRINT32Service/updateTransferOrderStatus`
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

### 7. **Sites origine/destination invalides**

**Critère** : Sites non reconnus dans Dynamics 365

**Conséquence** : Transfer Order peut être récupéré mais générer erreur lors confirmation

**Solution** : Vérifier que `InventLocationIdFrom` et `InventLocationIdTo` existent dans le référentiel sites

---

## 🔧 Configuration appsettings.json

```json
{
  "TransferOrdersSync": {
    "EnableConfirmation": true,
    "ConfirmAllVersions": true,
    "UpdateLine1Only": true,
    "MaxRetries": 3,
    "ValidateSites": false
  }
}
```

### Variables critiques

| Variable | Impact |
|----------|--------|
| `EnableConfirmation` | Si false → Pas de confirmation envoyée |
| `ConfirmAllVersions` | Si false → Seule la 1ère version confirmée |
| `UpdateLine1Only` | Si false → Toutes les lignes mises à jour (non recommandé) |
| `ValidateSites` | Si true → Vérifie l'existence des sites avant traitement |

---

## 📊 Traçabilité JSON_IN

### Exemple de ligne

```sql
JSON_KEYU      : 456791
JSON_CRDA      : 2026-01-09 11:10:00
JSON_FROM      : data/BRINT32TransferOrderTables
JSON_CCLI      : BR
JSON_DATA      : {"dataAreaId":"br","TransferId":"TR24000088",...}
JSON_SENT      : NULL (devient 'Y' après confirmation)
JSON_IMPORT_ID : TR24000088-1
```

### Requête de vérification

```sql
-- Transferts synchronisés aujourd'hui
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_IMPORT_ID as TransferOrderDocNum,
    JSON_SENT as Confirmed,
    CASE 
        WHEN JSON_SENT = 'Y' THEN 'Confirmé'
        WHEN JSON_SENT IS NULL THEN 'En attente'
        ELSE 'Erreur'
    END as Status
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT32TransferOrderTables'
  AND CAST(JSON_CRDA AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY JSON_CRDA DESC
```

---

## 📊 Traçabilité JSON_OUT

### Exemple de confirmation réussie

```sql
JSON_KEYU      : 890125
JSON_CRDA      : 2026-01-09 11:10:05
JSON_FROM      : TRANSFER_STATUS_TR24000088_V890020
JSON_DEST      : RESPONSE
JSON_DATA      : {"status":"success"}
```

### Requête de vérification

```sql
-- Confirmations Transfer Orders
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_FROM,
    JSON_DEST,
    JSON_DATA
FROM JSON_OUT
WHERE JSON_FROM LIKE 'TRANSFER_STATUS_%'
ORDER BY JSON_CRDA DESC
```

---

## 🚀 Commandes d'exécution

### Synchronisation Transfer Orders uniquement

```bash
dotnet run transfer
```

---

## 🐛 Troubleshooting

### Problème : Transfer Order non confirmé

**Vérifications** :
1. Vérifier TransferTableVersion existe :
   ```sql
   SELECT * FROM BRINT32_TRANSFERTABLE WHERE TransferId = 'TR24000088'
   ```
2. Vérifier logs : `Logs/transfer_orders_YYYYMMDD.log`
3. Tester endpoint service manuellement

---

### Problème : Plusieurs versions confirmées

**Comportement normal** : Le code confirme **TOUTES** les versions

**Exemple** :
```
📋 2 version(s) trouvée(s) pour TR24000088: 890020, 890025
🔄 Traitement version 890020...
✅ Transfer Order TR24000088 version 890020 confirmé
🔄 Traitement version 890025...
✅ Transfer Order TR24000088 version 890025 confirmé
```

**Si non désiré** : Modifier config `ConfirmAllVersions: false`

---

### Problème : Sites invalides

**Cause** : `InventLocationIdFrom` ou `InventLocationIdTo` inconnus

**Solution** : Vérifier référentiel sites :
```sql
SELECT 
    TransferId,
    InventLocationIdFrom,
    InventLocationIdTo
FROM BRINT32_TRANSFERTABLE
WHERE TransferId = 'TR24000088'
```

---

## 📝 Notes importantes

1. ✅ **Gestion multi-versions** : Confirme toutes les versions d'un Transfer Order
2. ✅ **Mise à jour ligne 1 uniquement** : Évite la surcharge
3. ⚠️ **TransferTableVersion requis** : Table mapping nécessaire
4. ⚠️ **Endpoint service** : Différent des endpoints OData classiques
5. ⚠️ **Status = 2** : Code fixe pour "Processed"
6. 🏢 **Sites critiques** : Vérifier existence des sites origine et destination
7. 📦 **Stock inter-sites** : Impacte les deux emplacements

---

## 🔗 Relations avec autres flux

| Flux | Lien |
|------|------|
| **INT34 (Articles)** | Mise à jour INT3PLStatus des articles transférés |
| **INT36 (BL Export)** | BL peuvent confirmer réception transferts |
| **Inventory** | Impact sur stock multi-sites |
