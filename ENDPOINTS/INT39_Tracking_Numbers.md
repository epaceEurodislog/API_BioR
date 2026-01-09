# 📍 INT39 - Tracking Numbers (Numéros de suivi)

## 🎯 Vue d'ensemble

**INT39** synchronise les **numéros de suivi de transporteurs** depuis SpeedWMS vers Dynamics 365. C'est un flux **unidirectionnel** (SpeedWMS → Dynamics 365) qui met à jour les informations de tracking pour les commandes clients et ordres de transfert.

| Propriété | Valeur |
|-----------|--------|
| **Endpoint POST** | `data/BRTrackingNumbers` |
| **Endpoint validation** | `data/BRTrackingNumbers/Microsoft.Dynamics.DataEntities.PostTrackingNumber` |
| **Direction** | SpeedWMS → Dynamics 365 (UNIDIRECTIONNEL) |
| **Commande** | `dotnet run tracking` |
| **Clé primaire** | `BROrderId` (OPE_REDO) |
| **Types supportés** | Sales Orders + Transfer Orders |

---

## 🔄 Flux complet

```
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 1 : RÉCUPÉRATION DEPUIS SPEEDWMS (OPE_DAT + SEX_DAT)     │
│ SELECT FROM OPE_DAT + SEX_DAT                                    │
│ WHERE OPE_STAT='070' AND OPE_TOP15<>1                           │
│ 2 requêtes séparées : Sales Orders + Transfer Orders            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 2 : INSERT JSON_OUT (STATUT EN_ATTENTE)                  │
│ INSERT INTO JSON_OUT (                                           │
│   JSON_DEST = 'INT39_TRACKING'                                  │
│   JSON_DATA = {Tracking Number data}                            │
│   JSON_TREN = 'EN_ATTENTE'                                      │
│   JSON_IMPORT_ID = 'INT39_{OPE_REDO}_{timestamp}'              │
│ )                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 3 : UPDATE OPE_TOP15 = 1 (MARQUAGE TRAITEMENT)           │
│ UPDATE OPE_DAT SET OPE_TOP15 = 1                                │
│ WHERE OPE_REDO IN (SELECT OPE_REDO FROM JSON_OUT                │
│                    WHERE JSON_DEST='INT39_TRACKING')            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 4 : POST VERS DYNAMICS 365 (BRTrackingNumbers)           │
│ POST https://{dynamics}/data/BRTrackingNumbers                  │
│ {                                                                │
│   "dataAreaId": "br",                                            │
│   "BROrderId": "{OPE_REDO}",                                    │
│   "BRTrackingNumber": "{SEX_TRAK}",                             │
│   "BR3PLPackingSlipId": "{OPE_KEYU}",                           │
│   "BRDocuStatus": "Received|NotReceived",                       │
│   "BRDOcStatusDate": "{OPE_DATETIME}",                          │
│   "CarrierCode": "{OPE_CTRA}"                                   │
│ }                                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 5 : UPDATE JSON_OUT (STATUT FINAL)                       │
│ UPDATE JSON_OUT SET                                              │
│   JSON_TREN = 'ENVOYE' (succès) ou 'ERREUR' (échec)           │
│   JSON_DATA = {Réponse Dynamics}                                │
│ WHERE JSON_IMPORT_ID = 'INT39_{OPE_REDO}_{timestamp}'          │
└──────────────────────────────────────────────────────────────────┘
```

---

## 📋 Structure des données

### Données récupérées (SpeedWMS)

```json
{
  "ACT_CODE": "COSMETIQUE",
  "OPE_CCLI": "BR",
  "OPE_REDO": "CMD-2024-98765",
  "OPE_RTIE": "CLI001",
  "OPE_STAT": "070",
  "OPE_CTRA": "CHRONOPOST",
  "OPE_TOP28": "Oui",
  "OPE_TOP22": "Oui",
  "OPE_DATETIME": "2026-01-09 10:30:00",
  "SEX_TRAK": "XY123456789FR",
  "OPE_KEYU": "50682",
  "OrderType": "SALES"
}
```

### Payload POST vers Dynamics 365

```json
{
  "dataAreaId": "br",
  "BROrderId": "CMD-2024-98765",
  "BRTrackingNumber": "XY123456789FR",
  "BR3PLPackingSlipId": "50682",
  "BRDocuStatus": "Received",
  "BRDOcStatusDate": "2026-01-09 10:30:00",
  "CarrierCode": "CHRONOPOST"
}
```

### Champs obligatoires

| Champ | Type | Nullable | Description |
|-------|------|----------|-------------|
| `dataAreaId` | string | ❌ Non | Code entreprise (fixe: "br") |
| `BROrderId` | string | ❌ Non | Numéro commande (OPE_REDO) |
| `BRTrackingNumber` | string | ✅ Oui | Numéro tracking (SEX_TRAK) |
| `BR3PLPackingSlipId` | string | ✅ Oui | N° expédition STACI (OPE_KEYU) |
| `BRDocuStatus` | string | ✅ Oui | Statut document ("Received" / "NotReceived") |
| `BRDOcStatusDate` | string | ✅ Oui | Date/heure statut document |
| `CarrierCode` | string | ✅ Oui | Code transporteur |

---

## ⚠️ Variables bloquant l'insertion

### 1. **OPE_TOP15 = 1 (déjà traité)**

**Critère** :
```sql
WHERE OPE_STAT = '070'
  AND COALESCE(OPE_TOP15, 0) <> 1
  AND OPE_CCLI = 'BR'
```

**Tracking Numbers EXCLUS** :
- `OPE_TOP15 = 1` (déjà envoyés vers Dynamics)

**Tracking Numbers INCLUS** :
- `OPE_TOP15 = 0` ou `NULL` (nouveaux)

**Solution** : Vérifier dans SpeedWMS :
```sql
SELECT OPE_REDO, OPE_TOP15, OPE_STAT
FROM OPE_DAT
WHERE OPE_CCLI = 'BR'
  AND OPE_STAT = '070'
ORDER BY OPE_MODA DESC
```

---

### 2. **OPE_STAT <> '070' (statut invalide)**

**Critère** :
```sql
WHERE OPE_STAT = '070'  -- EN PREPARATION COMPLETE
```

**Statuts EXCLUS** :
- Tout statut différent de `'070'`
- Exemple: `'010'` (En saisie), `'050'` (En cours)

**Solution** : Vérifier le statut dans OPE_DAT

---

### 3. **OPE_CCLI <> 'BR' (client incorrect)**

**Critère** :
```sql
WHERE OPE_CCLI = 'BR'
```

**Tracking Numbers EXCLUS** :
- Toutes les commandes d'autres clients

**Solution** : Normal, seul le client 'BR' est traité

---

### 4. **Aucun tracking number (SEX_TRAK vide)**

**Critère** : Vérification côté applicatif

**Comportement** :
- Si `SEX_TRAK` est NULL → `BRTrackingNumber = ""` (chaîne vide)
- La donnée est quand même envoyée à Dynamics 365

**Solution** : Vérifier dans SpeedWMS si SEX_TRAK est renseigné :
```sql
SELECT OPE_REDO, SEX_TRAK
FROM OPE_DAT o
LEFT JOIN SEX_DAT s ON o.ACT_CODE = s.ACT_CODE AND o.OPE_NOOE = s.OPE_NOOE
WHERE o.OPE_CCLI = 'BR'
  AND o.OPE_STAT = '070'
```

---

### 5. **Erreur HTTP POST (401, 404, 500)**

**Critères** :
- HTTP 400 : Données invalides (vérifier payload)
- HTTP 401 : Token expiré
- HTTP 404 : Endpoint introuvable
- HTTP 500 : Erreur serveur Dynamics

**Conséquence** : 
- JSON_OUT mis à jour avec `JSON_TREN = 'ERREUR'`
- OPE_TOP15 reste à 1 (pas de retry automatique)

**Solution** :
1. Vérifier logs : `Logs/tracking_numbers_YYYYMMDD.log`
2. Tester endpoint manuellement
3. Vérifier token OAuth2

---

### 6. **Type de mouvement invalide (TMV_CODE)**

**Critère** :
```sql
WHERE OPE_DAT.OPE_NoOE IN (
    SELECT ope_nooe 
    FROM MVT_DAT 
    WHERE act_code = 'COSMETIQUE' 
      AND TMV_CODE = '40110'
)
```

**TMV_CODE requis** : `'40110'` (Expédition)

**Solution** : Vérifier que la commande a bien un mouvement d'expédition

---

## 🔧 Configuration appsettings.json

```json
{
  "TrackingNumberSync": {
    "EnableSync": true,
    "EnableSalesOrders": true,
    "EnableTransferOrders": true,
    "DelayBetweenCalls": 200,
    "MaxRetries": 3
  }
}
```

### Variables critiques

| Variable | Impact |
|----------|--------|
| `EnableSync` | Si false → Pas de synchronisation |
| `EnableSalesOrders` | Si false → Sales Orders ignorés |
| `EnableTransferOrders` | Si false → Transfer Orders ignorés |
| `DelayBetweenCalls` | Délai (ms) entre chaque POST (éviter throttling) |

---

## 📊 Traçabilité JSON_OUT

### Exemple de trace EN_ATTENTE (Étape 2)

```sql
JSON_KEYU      : 890127
JSON_CRDA      : 2026-01-09 11:20:00
JSON_DEST      : INT39_TRACKING
JSON_TREN      : EN_ATTENTE
JSON_DATA      : {"dataAreaId":"br","BROrderId":"CMD-2024-98765",...}
JSON_IMPORT_ID : INT39_CMD-2024-98765_20260109112000
```

### Exemple de trace ENVOYE (Étape 5 - Succès)

```sql
JSON_KEYU      : 890127
JSON_CRDA      : 2026-01-09 11:20:05
JSON_DEST      : INT39_TRACKING
JSON_TREN      : ENVOYE
JSON_DATA      : {"status":"success","timestamp":"2026-01-09T11:20:05"}
JSON_IMPORT_ID : INT39_CMD-2024-98765_20260109112000
```

### Exemple de trace ERREUR (Étape 5 - Échec)

```sql
JSON_KEYU      : 890128
JSON_CRDA      : 2026-01-09 11:25:00
JSON_DEST      : INT39_TRACKING
JSON_TREN      : ERREUR
JSON_DATA      : {"error":"HTTP 400 - Invalid BROrderId","timestamp":"..."}
JSON_IMPORT_ID : INT39_CMD-2024-99999_20260109112500
```

### Requête de vérification

```sql
-- Tracking numbers par statut
SELECT 
    JSON_TREN as Status,
    COUNT(*) as Count,
    MIN(JSON_CRDA) as FirstSeen,
    MAX(JSON_CRDA) as LastSeen
FROM JSON_OUT
WHERE JSON_DEST = 'INT39_TRACKING'
  AND CAST(JSON_CRDA AS DATE) = CAST(GETDATE() AS DATE)
GROUP BY JSON_TREN
```

---

## 🗃️ Traçabilité SpeedWMS (OPE_TOP15)

### Vérification OPE_TOP15

```sql
-- Tracking numbers traités aujourd'hui
SELECT 
    o.OPE_REDO,
    o.OPE_KEYU,
    s.SEX_TRAK,
    o.OPE_TOP15,
    o.OPE_STAT,
    o.OPE_MODA as LastModified,
    CASE 
        WHEN o.OPE_TOP15 = 1 THEN 'Envoyé vers D365'
        WHEN o.OPE_TOP15 = 0 THEN 'En attente'
        ELSE 'Non marqué'
    END as Status
FROM OPE_DAT o
LEFT JOIN SEX_DAT s ON o.ACT_CODE = s.ACT_CODE AND o.OPE_NOOE = s.OPE_NOOE
WHERE o.ACT_CODE = 'COSMETIQUE'
  AND o.OPE_CCLI = 'BR'
  AND o.OPE_STAT = '070'
  AND CAST(o.OPE_MODA AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY o.OPE_MODA DESC
```

---

## 🚀 Commandes d'exécution

### Synchronisation Tracking Numbers

```bash
dotnet run tracking
```

### Output typique

```
🔍 Vérification connectivité Dynamics Tracking Service...
✅ Connectivité Tracking Service OK

📥 Récupération des tracking numbers depuis SpeedWMS...
✅ 15 tracking numbers SALES ORDERS récupérés
✅ 3 tracking numbers TRANSFER ORDERS récupérés
📦 Total: 18 tracking numbers à traiter

🔄 Traitement complet Tracking Number : CMD-2024-98765
📝 ÉTAPE 2 : INSERT JSON_OUT
🔄 ÉTAPE 3 : Mise à jour OPE_TOP15 pour CMD-2024-98765
✅ 1 ligne(s) mise(s) à jour - OPE_TOP15 = 1
📤 POST Tracking Number - OrderId: CMD-2024-98765
✅ Tracking Number créé/mis à jour : CMD-2024-98765
✅ Tracking Number CMD-2024-98765 traité avec succès

📊 === RÉSULTATS INT39 === 📊
🔍 Tracking numbers trouvés: 18
✅ Tracking traités avec succès: 17
❌ Erreurs: 1
⏱️ Durée totale: 5.2 secondes
```

---

## 🐛 Troubleshooting

### Problème : Tracking number non récupéré

**Vérifications** :
1. Vérifier OPE_STAT = '070' :
   ```sql
   SELECT * FROM OPE_DAT WHERE OPE_REDO = 'CMD-2024-98765'
   ```
2. Vérifier OPE_TOP15 <> 1
3. Vérifier OPE_CCLI = 'BR'
4. Vérifier mouvement TMV_CODE = '40110' existe

---

### Problème : OPE_TOP15 marqué mais pas dans Dynamics

**Cause** : Erreur lors du POST mais OPE_TOP15 déjà mis à jour

**Solution** :
1. Vérifier JSON_OUT pour erreur :
   ```sql
   SELECT * FROM JSON_OUT
   WHERE JSON_DEST = 'INT39_TRACKING'
     AND JSON_TREN = 'ERREUR'
   ORDER BY JSON_CRDA DESC
   ```
2. Réinitialiser OPE_TOP15 si nécessaire :
   ```sql
   UPDATE OPE_DAT SET OPE_TOP15 = 0
   WHERE OPE_REDO = 'CMD-2024-98765'
   ```
3. Relancer le traitement

---

### Problème : BRDocuStatus incorrect

**Mapping OPE_TOP22 → BRDocuStatus** :
- `OPE_TOP22 = 'Oui'` → `BRDocuStatus = "Received"`
- `OPE_TOP22 = 'Non'` → `BRDocuStatus = "NotReceived"`
- `OPE_TOP22 = NULL` → `BRDocuStatus = "NotReceived"` (par défaut)

**Vérification** :
```sql
SELECT OPE_REDO, OPE_TOP22
FROM OPE_DAT
WHERE OPE_REDO = 'CMD-2024-98765'
```

---

## 📝 Notes importantes

1. ✅ **Double type** : Supporte Sales Orders ET Transfer Orders
2. ⚠️ **OPE_TOP15 critique** : Une fois marqué, pas de retry automatique
3. ⚠️ **JSON_OUT 2 étapes** : EN_ATTENTE → ENVOYE/ERREUR
4. 📦 **SEX_TRAK optionnel** : Peut être vide (string vide envoyée)
5. 🔗 **Lien INT35** : Tracking liés aux Packing Slips (INT35)

---

## 🔗 Relations avec autres flux

| Flux | Lien |
|------|------|
| **INT35 (Packing Slips)** | Tracking numbers associés aux Packing Slips |
| **INT36 (BL Export)** | BL peuvent inclure les tracking numbers |
| **SpeedWMS OPE_DAT** | Source des données tracking |
| **SpeedWMS SEX_DAT** | Numéros de tracking transporteurs |

---

## 📊 Statistiques typiques

```sql
-- Vue d'ensemble journalière
SELECT 
    CAST(JSON_CRDA AS DATE) as Date,
    COUNT(*) as TotalTracking,
    SUM(CASE WHEN JSON_TREN = 'ENVOYE' THEN 1 ELSE 0 END) as Success,
    SUM(CASE WHEN JSON_TREN = 'ERREUR' THEN 1 ELSE 0 END) as Errors,
    SUM(CASE WHEN JSON_TREN = 'EN_ATTENTE' THEN 1 ELSE 0 END) as Pending
FROM JSON_OUT
WHERE JSON_DEST = 'INT39_TRACKING'
  AND JSON_CRDA >= DATEADD(DAY, -7, GETDATE())
GROUP BY CAST(JSON_CRDA AS DATE)
ORDER BY Date DESC
```

---

## 🔐 Sécurité

| Aspect | Configuration |
|--------|---------------|
| **Authentication** | OAuth2 Bearer Token |
| **Endpoint** | `/data/BRTrackingNumbers` (POST) |
| **Validation** | `/data/BRTrackingNumbers/Microsoft.Dynamics.DataEntities.PostTrackingNumber` |
| **Token refresh** | Automatique via AuthenticationService |
