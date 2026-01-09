# 📦 INT35 - Sales Orders / Packing Slips (Commandes de vente / Bordereaux d'expédition)

## 🎯 Vue d'ensemble

**INT35** synchronise les **commandes de vente et bordereaux d'expédition** depuis Dynamics 365 vers SQL Server. Contrairement aux autres INT, ce flux **ne renvoie PAS de confirmation** vers Dynamics 365.

| Propriété | Valeur |
|-----------|--------|
| **Endpoint GET** | `data/BRPackingSlipInterfaces` |
| **Direction** | Dynamics 365 → SQL Server (UNIDIRECTIONNEL) |
| **Commande** | `dotnet run cr_recep` |
| **Clé primaire** | `PackingSlipId` (ex: BL-2024-12345) |
| **Trace JSON_OUT** | Pas de confirmation, uniquement traçabilité |

---

## 🔄 Flux complet

```
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 1 : RÉCUPÉRATION DEPUIS DYNAMICS 365                      │
│ GET https://{dynamics}/data/BRPackingSlipInterfaces             │
│ (Aucun filtre particulier - toutes les nouvelles données)       │
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
│   JSON_FROM = 'data/BRPackingSlipInterfaces'                    │
│   JSON_IMPORT_ID = {PackingSlipId}                              │
│   JSON_DATA = {JSON complet}                                    │
│ )                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 4 : TRAÇABILITÉ JSON_OUT (RÉCEPTION UNIQUEMENT)           │
│ INSERT INTO JSON_OUT (                                           │
│   JSON_FROM = 'PACKING_RECEIVED_{PackingSlipId}'               │
│   JSON_DEST = 'RECEIVED'                                        │
│   JSON_DATA = {Métadonnées réception}                           │
│ )                                                                │
│                                                                  │
│ ⚠️ PAS DE CONFIRMATION RENVOYÉE VERS DYNAMICS 365               │
└──────────────────────────────────────────────────────────────────┘
```

---

## 📋 Structure des données

### Données récupérées (GET)

```json
{
  "dataAreaId": "br",
  "PackingSlipId": "BL-2024-12345",
  "SalesId": "CMD-2024-98765",
  "DeliveryName": "Client ABC",
  "DeliveryAddress": "123 Rue Principale, Paris",
  "LineNumber": 1.0,
  "ItemId": "D14018",
  "Quantity": 50.0,
  "SalesUnit": "pcs",
  "DeliveryDate": "2026-01-15T00:00:00Z",
  "TrackingNumber": null
}
```

### Champs obligatoires

| Champ | Type | Nullable | Description |
|-------|------|----------|-------------|
| `dataAreaId` | string | ❌ Non | Code entreprise (fixe: "br") |
| `PackingSlipId` | string | ❌ Non | Numéro bordereau |
| `SalesId` | string | ❌ Non | Numéro commande de vente |
| `LineNumber` | decimal | ❌ Non | Numéro de ligne |
| `ItemId` | string | ❌ Non | Code article |
| `Quantity` | decimal | ❌ Non | Quantité expédiée |

---

## ⚠️ Variables bloquant l'insertion

### 1. **Hash identique (doublon)**

**Critère** :
```sql
SELECT COUNT(*)
FROM JSON_IN
WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
  AND JSON_IMPORT_ID = 'BL-2024-12345'
  AND HASHBYTES('SHA2_256', JSON_DATA) = @NewHash
```

**Si COUNT > 0** → Packing Slip **IGNORÉ**

**Solution** : Normal, évite les doublons

---

### 2. **PackingSlipId vide**

**Critère** :
```csharp
if (string.IsNullOrEmpty(packingSlipId))
{
    _logger.LogWarning($"⚠️ Packing Slip avec ID vide ignoré");
    continue;
}
```

**Conséquence** : Packing Slip **IGNORÉ**

**Solution** : Vérifier les données dans Dynamics 365

---

### 3. **SalesId vide**

**Critère** :
```csharp
if (string.IsNullOrEmpty(salesId))
{
    _logger.LogWarning($"⚠️ Packing Slip {packingSlipId} sans SalesId ignoré");
    continue;
}
```

**Conséquence** : Packing Slip **IGNORÉ**

**Solution** : Vérifier que la commande de vente existe et est liée

---

### 4. **Erreur HTTP GET**

**Critères** :
- HTTP 401 : Token expiré
- HTTP 404 : Endpoint introuvable
- HTTP 500 : Erreur serveur

**Conséquence** : Récupération **ÉCHOUÉE**, aucune donnée traitée

**Solution** :
1. Vérifier endpoint : `/data/BRPackingSlipInterfaces`
2. Vérifier token OAuth2
3. Consulter logs Dynamics 365

---

## 🔧 Configuration appsettings.json

```json
{
  "PackingSlipSync": {
    "EnableSync": true,
    "MaxBatchSize": 100,
    "IncludeTrackingNumbers": true
  }
}
```

### Variables critiques

| Variable | Impact |
|----------|--------|
| `EnableSync` | Si false → Pas de synchronisation |
| `MaxBatchSize` | Limite le nombre de packing slips par requête |
| `IncludeTrackingNumbers` | Si true → Récupère aussi les tracking numbers (voir INT39) |

---

## 📊 Traçabilité JSON_IN

### Exemple de ligne

```sql
JSON_KEYU      : 456792
JSON_CRDA      : 2026-01-09 11:15:00
JSON_FROM      : data/BRPackingSlipInterfaces
JSON_CCLI      : BR
JSON_DATA      : {"dataAreaId":"br","PackingSlipId":"BL-2024-12345",...}
JSON_SENT      : NULL (pas de confirmation pour INT35)
JSON_IMPORT_ID : BL-2024-12345
```

### Requête de vérification

```sql
-- Packing Slips synchronisés aujourd'hui
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_IMPORT_ID as PackingSlipId,
    LEN(JSON_DATA) as DataSize,
    CASE 
        WHEN JSON_SENT = 'Y' THEN 'Traité'
        WHEN JSON_SENT IS NULL THEN 'Reçu'
        ELSE 'Erreur'
    END as Status
FROM JSON_IN
WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
  AND CAST(JSON_CRDA AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY JSON_CRDA DESC
```

---

## 📊 Traçabilité JSON_OUT

### Exemple de traçabilité réception

```sql
JSON_KEYU      : 890126
JSON_CRDA      : 2026-01-09 11:15:05
JSON_FROM      : PACKING_RECEIVED_BL-2024-12345
JSON_DEST      : RECEIVED
JSON_DATA      : {"timestamp":"2026-01-09T11:15:05","recordCount":3}
```

### Requête de vérification

```sql
-- Traçabilité réceptions Packing Slips
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_FROM,
    JSON_DEST,
    JSON_DATA
FROM JSON_OUT
WHERE JSON_FROM LIKE 'PACKING_RECEIVED_%'
ORDER BY JSON_CRDA DESC
```

### ⚠️ Différences avec les autres INT

| Critère | INT35 (Packing Slips) | Autres INT (32, 34, 36) |
|---------|----------------------|------------------------|
| **Confirmation POST** | ❌ NON | ✅ OUI |
| **JSON_OUT/DEST** | `RECEIVED` | `RESPONSE` / `ERROR` |
| **JSON_OUT/FROM** | `PACKING_RECEIVED_*` | `*_STATUS_*` / `CONFIRM_OK` |
| **JSON_IN/SENT** | Reste `NULL` | Devient `'Y'` |

---

## 🚀 Commandes d'exécution

### Synchronisation Packing Slips uniquement

```bash
dotnet run cr_recep
```

---

## 🐛 Troubleshooting

### Problème : Packing Slip non synchronisé

**Vérifications** :
1. Vérifier dans Dynamics 365 que le Packing Slip existe
2. Vérifier logs : `Logs/packing_slips_YYYYMMDD.log`
3. Tester endpoint manuellement :
   ```http
   GET https://{dynamics}/data/BRPackingSlipInterfaces
   Authorization: Bearer {token}
   ```

---

### Problème : Doublons malgré anti-doublon

**Cause probable** : Données modifiées dans Dynamics 365

**Vérification** :
```sql
SELECT 
    JSON_IMPORT_ID,
    COUNT(*) as Count,
    MIN(JSON_CRDA) as FirstReceived,
    MAX(JSON_CRDA) as LastReceived
FROM JSON_IN
WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
GROUP BY JSON_IMPORT_ID
HAVING COUNT(*) > 1
```

**Solution** : Vérifier si le contenu a changé (hash différent)

---

### Problème : Aucune trace JSON_OUT

**Cause** : JSON_OUT pour INT35 est optionnel (traçabilité uniquement)

**Comportement normal** : Seul JSON_IN est obligatoire

**Solution** : Vérifier JSON_IN pour confirmer la réception

---

## 📝 Notes importantes

1. ⚠️ **PAS DE CONFIRMATION** : INT35 est unidirectionnel (Dynamics → SQL uniquement)
2. ✅ **Anti-doublon actif** : Hash SHA256 sur JSON complet
3. ⚠️ **JSON_SENT reste NULL** : Normal car pas de confirmation renvoyée
4. 📦 **Lien avec INT39** : Les Tracking Numbers peuvent être synchronisés en parallèle
5. 🔗 **Lien avec commandes** : SalesId obligatoire pour traçabilité

---

## 🔗 Relations avec autres flux

| Flux | Lien |
|------|------|
| **INT39 (Tracking Numbers)** | Tracking numbers liés aux Packing Slips |
| **INT34 (Articles)** | Articles expédiés doivent exister dans le référentiel |
| **INT36 (BL Export)** | BL peuvent correspondre à des Packing Slips |

---

## 📊 Statistiques typiques

```sql
-- Statistiques journalières
SELECT 
    CAST(JSON_CRDA AS DATE) as Date,
    COUNT(*) as TotalPackingSlips,
    COUNT(DISTINCT JSON_IMPORT_ID) as UniquePackingSlips,
    SUM(CASE WHEN JSON_SENT = 'Y' THEN 1 ELSE 0 END) as Processed
FROM JSON_IN
WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
  AND JSON_CRDA >= DATEADD(DAY, -7, GETDATE())
GROUP BY CAST(JSON_CRDA AS DATE)
ORDER BY Date DESC
```
