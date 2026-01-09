# 🔙 INT32 - Return Orders (Retours fournisseurs)

## 🎯 Vue d'ensemble

**INT32 Return Orders** synchronise les **ordres de retour fournisseur** depuis Dynamics 365 vers SQL Server et envoie des confirmations de traitement.

| Propriété | Valeur |
|-----------|--------|
| **Endpoint GET** | `data/BRINT32ReturnOrderTables` |
| **Endpoint confirmation** | `api/services/BRINT32ServiceGroup/BRINT32Service/updateReturnOrderStatus` |
| **Direction GET** | Dynamics 365 → SQL Server |
| **Direction confirmation** | SQL Server → Dynamics 365 |
| **Commande** | `dotnet run return` |
| **Clé primaire** | `RMANum` (ex: RE24000145) |
| **Clé version** | `ReturnOrderDocNum` (ex: RE24000145-1) |

---

## 🔄 Flux complet

```
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 1 : RÉCUPÉRATION DEPUIS DYNAMICS 365                      │
│ GET https://{dynamics}/data/BRINT32ReturnOrderTables            │
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
│   JSON_FROM = 'data/BRINT32ReturnOrderTables'                   │
│   JSON_IMPORT_ID = {ReturnOrderDocNum}                          │
│   JSON_DATA = {JSON complet}                                    │
│ )                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 4 : RÉCUPÉRATION VERSIONS (ReturnTableVersion)            │
│ SELECT ReturnTableVersion                                        │
│ FROM BRINT32_RETURNTABLE                                         │
│ WHERE RMANum = 'RE24000145'                                      │
│ → Peut retourner plusieurs versions (ex: 780012, 780015)        │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 5 : CONFIRMATION POUR CHAQUE VERSION                      │
│ POST https://{dynamics}/api/services/                            │
│      BRINT32ServiceGroup/BRINT32Service/updateReturnOrderStatus  │
│ Pour chaque ReturnTableVersion:                                  │
│ {                                                                │
│   "_dataAreaId": "BR",                                           │
│   "_id": "780012",                                               │
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
│   JSON_FROM = 'RETURN_STATUS_{RMANum}_V{Version}'              │
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
  "RMANum": "RE24000145",
  "ReturnOrderDocNum": "RE24000145-1",
  "OrderVendorAccountNumber": "V0001",
  "INT3PLStatus": null,
  "DeliveryAddressName": "Entrepôt BioRécup",
  "LineNumber": 1.0,
  "ItemNumber": "D14018",
  "ReturnQuantity": 50.0,
  "ReturnUnitSymbol": "pcs",
  "ReturnReasonCodeId": "Défectueux"
}
```

### Payload confirmation (POST)

```json
{
  "_dataAreaId": "BR",
  "_id": "780012",
  "_status": 2
}
```

### Champs obligatoires

| Champ | Type | Nullable | Description |
|-------|------|----------|-------------|
| `dataAreaId` | string | ❌ Non | Code entreprise (fixe: "br") |
| `RMANum` | string | ❌ Non | Numéro retour |
| `ReturnOrderDocNum` | string | ❌ Non | Numéro retour avec version |
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
WHERE JSON_FROM = 'data/BRINT32ReturnOrderTables'
  AND JSON_IMPORT_ID = 'RE24000145-1'
  AND HASHBYTES('SHA2_256', JSON_DATA) = @NewHash
```

**Si COUNT > 0** → Return Order **IGNORÉE**

**Solution** : Normal, évite les doublons

---

### 2. **RMANum ou ReturnOrderDocNum vide**

**Critère** :
```csharp
if (string.IsNullOrEmpty(rmaNum) || string.IsNullOrEmpty(returnOrderDocNum))
{
    _logger.LogWarning($"⚠️ Return Order avec ID vide ignorée");
    continue;
}
```

**Conséquence** : Return Order **IGNORÉE**

**Solution** : Vérifier les données dans Dynamics 365

---

### 3. **INT3PLStatus = 'ProcessedBy3PL'**

**Critère filtre OData** :
```odata
$filter=INT3PLStatus eq null or INT3PLStatus ne 'ProcessedBy3PL'
```

**Return Orders EXCLUES** :
- `INT3PLStatus = 'ProcessedBy3PL'` (déjà traitées)

**Return Orders INCLUSES** :
- `INT3PLStatus = null` (nouvelles)
- Tout autre statut

**Solution** : Si un retour ne remonte pas, vérifier son statut INT3PLStatus

---

### 4. **Aucune version ReturnTableVersion trouvée**

**Critère** :
```sql
SELECT ReturnTableVersion
FROM BRINT32_RETURNTABLE
WHERE RMANum = 'RE24000145'
```

**Si résultat vide** → Confirmation **ÉCHOUÉE**

**Log** : `❌ Aucune version trouvée pour Return Order RE24000145`

**Solution** :
1. Vérifier table `BRINT32_RETURNTABLE` existe
2. Vérifier mapping Return Order → ReturnTableVersion
3. Vérifier que le retour a bien été synchronisé dans la table

---

### 5. **Erreur HTTP confirmation (POST)**

**Critères** :
- HTTP 400 : Données invalides (vérifier `_id`, `_status`)
- HTTP 401 : Token expiré
- HTTP 404 : Service introuvable
- HTTP 500 : Erreur serveur

**Conséquence** : Confirmation **ÉCHOUÉE** + JSON_OUT avec erreur

**Solution** :
1. Vérifier endpoint service : `/api/services/BRINT32ServiceGroup/BRINT32Service/updateReturnOrderStatus`
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
  "ReturnOrdersSync": {
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
JSON_KEYU      : 456790
JSON_CRDA      : 2026-01-09 11:05:00
JSON_FROM      : data/BRINT32ReturnOrderTables
JSON_CCLI      : BR
JSON_DATA      : {"dataAreaId":"br","RMANum":"RE24000145",...}
JSON_SENT      : NULL (devient 'Y' après confirmation)
JSON_IMPORT_ID : RE24000145-1
```

### Requête de vérification

```sql
-- Retours synchronisés aujourd'hui
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_IMPORT_ID as ReturnOrderDocNum,
    JSON_SENT as Confirmed,
    CASE 
        WHEN JSON_SENT = 'Y' THEN 'Confirmé'
        WHEN JSON_SENT IS NULL THEN 'En attente'
        ELSE 'Erreur'
    END as Status
FROM JSON_IN
WHERE JSON_FROM = 'data/BRINT32ReturnOrderTables'
  AND CAST(JSON_CRDA AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY JSON_CRDA DESC
```

---

## 📊 Traçabilité JSON_OUT

### Exemple de confirmation réussie

```sql
JSON_KEYU      : 890124
JSON_CRDA      : 2026-01-09 11:05:05
JSON_FROM      : RETURN_STATUS_RE24000145_V780012
JSON_DEST      : RESPONSE
JSON_DATA      : {"status":"success"}
```

### Requête de vérification

```sql
-- Confirmations Return Orders
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_FROM,
    JSON_DEST,
    JSON_DATA
FROM JSON_OUT
WHERE JSON_FROM LIKE 'RETURN_STATUS_%'
ORDER BY JSON_CRDA DESC
```

---

## 🚀 Commandes d'exécution

### Synchronisation Return Orders uniquement

```bash
dotnet run return
```

---

## 🐛 Troubleshooting

### Problème : Return Order non confirmé

**Vérifications** :
1. Vérifier ReturnTableVersion existe :
   ```sql
   SELECT * FROM BRINT32_RETURNTABLE WHERE RMANum = 'RE24000145'
   ```
2. Vérifier logs : `Logs/return_orders_YYYYMMDD.log`
3. Tester endpoint service manuellement

---

### Problème : Plusieurs versions confirmées

**Comportement normal** : Le code confirme **TOUTES** les versions

**Exemple** :
```
📋 2 version(s) trouvée(s) pour RE24000145: 780012, 780015
🔄 Traitement version 780012...
✅ Return Order RE24000145 version 780012 confirmée
🔄 Traitement version 780015...
✅ Return Order RE24000145 version 780015 confirmée
```

**Si non désiré** : Modifier config `ConfirmAllVersions: false`

---

### Problème : Ligne 1 non mise à jour

**Cause** : Ligne 1 sans ItemId

**Solution** : Vérifier les données du retour :
```sql
SELECT 
    RMANum,
    LineNumber,
    ItemNumber
FROM BRINT32_RETURNTABLE
WHERE RMANum = 'RE24000145'
ORDER BY LineNumber
```

---

## 📝 Notes importantes

1. ✅ **Gestion multi-versions** : Confirme toutes les versions d'un Return Order
2. ✅ **Mise à jour ligne 1 uniquement** : Évite la surcharge
3. ⚠️ **ReturnTableVersion requis** : Table mapping nécessaire
4. ⚠️ **Endpoint service** : Différent des endpoints OData classiques
5. ⚠️ **Status = 2** : Code fixe pour "Processed"
6. 📦 **ReturnReasonCodeId** : Important pour traçabilité des retours

---

## 🔗 Relations avec autres flux

| Flux | Lien |
|------|------|
| **INT34 (Articles)** | Mise à jour INT3PLStatus des articles retournés |
| **INT36 (BL Export)** | BL peuvent inclure retours fournisseurs |
