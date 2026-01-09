# 🚚 INT36 - BL Export / Confirmation Préparation (cr_prep)

## 🎯 Vue d'ensemble

**INT36** exporte les **Bons de Livraison (BL)** depuis SpeedWMS vers Dynamics 365 pour confirmer la préparation des commandes.

| Propriété | Valeur |
|-----------|--------|
| **Endpoint validation** | `data/BRPackingSlipValidationInterfaces` |
| **Endpoint confirmation** | `data/BRPackingSlipValidationInterfaces/Microsoft.Dynamics.DataEntities.PostPackingSlip` |
| **Direction** | SpeedWMS → Dynamics 365 |
| **Commande** | `dotnet run cr_prep` |
| **Clé primaire** | `OPE_KEYU` (Numéro BL) |
| **Source** | Base SpeedWMS `MSY_SF_RCT` |

---

## 🔄 Flux complet

```
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 1 : RÉCUPÉRATION BL DEPUIS SPEEDWMS                       │
│ Tables:                                                          │
│ - OPE_DAT (En-têtes BL) WHERE OPE_STAT='070'                   │
│ - MIL_DAT (Lignes articles)                                     │
│ - SEX_DAT (Supports/emballages)                                 │
│ Filtre: ACT_CODE='COSMETIQUE' AND OPE_CRQI='INTERFACE'         │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 2 : TRANSFORMATION DES DONNÉES                            │
│ - Regroupement articles par ItemId                              │
│ - Consolidation lots/séries                                     │
│ - Calcul dates (ShippingDate, EndDatePrep)                      │
│ - Génération ImportId = {OPE_KEYU}                             │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 3 : VÉRIFICATION ANTI-DOUBLON JSON_OUT                    │
│ SELECT COUNT(*) FROM JSON_OUT                                    │
│ WHERE JSON_DEST IN ('BL_EXPORT')                                  │
│   AND JSON_TREN LIKE '%{BL}%'                                   │
│ Si COUNT > 0 → BL IGNORÉ (déjà traité)                         │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 4 : GÉNÉRATION PAYLOADS (1 par ligne article)             │
│ {                                                                │
│   "dataAreaId": "br",                                            │
│   "ImportId": "50682",                                           │
│   "transRefId": "UATSO-000218",                                  │
│   "BR3PLShippingDate": "2026-01-09T10:43:00Z",                  │
│   "pickingRouteID": "UATSO-000218",                              │
│   "qty": 5.0,                                                    │
│   "itemId": "D14018",                                            │
│   "InventLocationId": "RECNOLP"                                  │
│ }                                                                │
│ Sauvegarde dans: BL_Payloads/{BL}_payload_{idx}.json           │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 5 : POST DONNÉES (1er POST pour chaque payload)           │
│ POST https://{dynamics}/data/BRPackingSlipValidationInterfaces  │
│ Headers:                                                         │
│   Authorization: Bearer {token}                                  │
│   Content-Type: application/json                                │
│ Body: {payload JSON}                                             │
│ → Traçabilité JSON_OUT: JSON_TREN='BL_DATA_{BL}_{ItemId}'      │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 6 : POST CONFIRMATION VIDE (2ème POST)                    │
│ POST https://{dynamics}/data/BRPackingSlipValidationInterfaces/ │
│      Microsoft.Dynamics.DataEntities.PostPackingSlip             │
│ Headers:                                                         │
│   Authorization: Bearer {token}                                  │
│ Body: null (POST VIDE)                                          │
│ → Traçabilité JSON_OUT: JSON_TREN='BL_CONFIRM_{BL}'            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ ÉTAPE 7 : MISE À JOUR JSON_OUT                                  │
│ INSERT INTO JSON_OUT (                                           │
│   JSON_FROM = 'BL_DATA_{BL}_{ItemId}'                          │
│   JSON_DEST = 'BL_EXPORT'                                        │
│   JSON_DATA = '{payload ou message succès}'                     │
│   JSON_TREN = 'BL_CONFIRM_{BL}'                                 │
│   JSON_IMPORT_ID = '{BL}_timestamp'                             │
│ )                                                                │
└──────────────────────────────────────────────────────────────────┘
```

---

## 📋 Structure des données

### Requête SpeedWMS (OPE_DAT)

```sql
SELECT 
    OPE_KEYU,                    -- Numéro BL (ex: 50682)
    OPE_REDO,                    -- Référence commande D365
    OPE_ALPHA17,                 -- Picking Route ID
    OPE_CTRA,                    -- Code Transport
    OPE_ALPHA40,                 -- Service transport (partie 1)
    OPE_ALPHA41,                 -- Service transport (partie 2)
    OPE_MODA,                    -- Date expédition
    OPE_TOP22,                   -- Statut Documentation
    OPE_STAT                     -- Statut opération
FROM OPE_DAT
WHERE OPE_KEYU IS NOT NULL
  AND OPE_CRQI = 'INTERFACE'
  AND ACT_CODE = 'COSMETIQUE'
  AND OPE_STAT = '070'           -- ✅ CRITÈRE PRINCIPAL: Préparation terminée
ORDER BY OPE_KEYU
```

### Payload POST Dynamics 365

```json
{
  "dataAreaId": "br",
  "ImportId": "50682",
  "transRefId": "UATSO-000218",
  "BR3PLShippingDate": "2026-01-09T10:43:00Z",
  "pickingRouteID": "UATSO-000218",
  "CarrierServiceCode": "STANDARD",
  "qty": 5.0,
  "BR3PLPackingSlipId": "50682",
  "itemId": "D14018",
  "InventLocationId": "RECNOLP",
  "BR3PLEndDatePrep": "2026-01-09T10:43:05Z",
  "CarrierCode": "GEODIS",
  "inventSerialId": "",
  "inventBatchId": "A00009876"
}
```

### Champs obligatoires

| Champ | Source SpeedWMS | Obligatoire | Description |
|-------|----------------|-------------|-------------|
| `ImportId` | `OPE_KEYU` | ❌ **OUI** | Numéro BL |
| `transRefId` | `OPE_REDO` | ❌ **OUI** | Référence commande D365 |
| `itemId` | `ART_CODE` | ❌ **OUI** | Code article |
| `qty` | `MIL_QTTP` | ❌ **OUI** | Quantité préparée |
| `InventLocationId` | Fixe: `RECNOLP` | ❌ **OUI** | Emplacement |
| `BR3PLShippingDate` | `OPE_MODA` | ✅ Oui (si null → exclu) | Date expédition |
| `BR3PLEndDatePrep` | `MAX(MIE_MODA)` | ✅ Oui | Date fin préparation |

---

## ⚠️ Variables bloquant l'insertion/traitement

### 1. **BL déjà traité (JSON_OUT)**

**Critère** :
```sql
SELECT COUNT(*)
FROM JSON_OUT 
WHERE JSON_DEST IN ('BL_EXPORT')
  AND (JSON_TREN LIKE '%50682%' 
       OR JSON_IMPORT_ID LIKE '50682_%')
```

**Si COUNT > 0** → BL **IGNORÉ**

**Log** : `✅ BL 50682: déjà traité, ignoré`

**Solution** :
- Normal si déjà exporté avec succès

---

### 2. **OPE_STAT différent de '070'**

**Critère** :
```sql
WHERE OPE_STAT = '070'  -- Préparation terminée
```

**Statuts SpeedWMS** :
- `010` : En cours de préparation → ❌ **IGNORÉ**
- `050` : Partiellement préparé → ❌ **IGNORÉ**
- `070` : Préparation terminée → ✅ **TRAITÉ**
- `080` : Expédié → ❌ **IGNORÉ** (normalement déjà traité)

**Solution** : Vérifier le statut du BL dans SpeedWMS

---

### 3. **OPE_REDO vide (TransRefId manquant)**

**Critère** :
```csharp
if (string.IsNullOrEmpty(bl.OpeRedo))
{
    _logger.LogError($"❌ BL {bl.OpeKeyu}: OPE_REDO vide (TransRefId requis)");
    result.Success = false;
    result.ErrorMessage = "TransRefId manquant";
    return result;
}
```

**Conséquence** : BL **NON EXPORTÉ** + Erreur dans JSON_OUT

**Solution** : Corriger la donnée dans SpeedWMS (OPE_REDO doit contenir la référence commande D365)

---

### 4. **Aucune ligne article (MIL_DAT)**

**Critère** :
```csharp
if (bl.Lines.Count == 0)
{
    _logger.LogWarning($"⚠️ BL {bl.OpeKeyu}: aucune ligne article");
    result.ErrorMessage = "Aucune ligne";
    return result;
}
```

**Conséquence** : BL **IGNORÉ**

**Solution** : Vérifier les lignes dans MIL_DAT pour ce BL

---

### 5. **Quantité préparée = 0**

**Critère** :
```sql
SELECT SUM(MIL_QTTP) as TotalQty
FROM MIL_DAT
WHERE OPE_KEYU = 50682
```

**Si TotalQty = 0** → Ligne **IGNORÉE** (payload non généré)

**Solution** : Vérifier MIL_QTTP dans SpeedWMS

---

### 6. **Erreur HTTP Dynamics 365**

**Critères** :
- HTTP 400 : Données invalides (vérifier payload)
- HTTP 401 : Token expiré
- HTTP 403 : Permissions insuffisantes
- HTTP 404 : Endpoint incorrect
- HTTP 500 : Erreur serveur Dynamics

**Conséquence** : POST **ÉCHOUÉ** + JSON_OUT avec `JSON_DEST='BL_ERROR'`

**Solution** :
1. Vérifier les logs détaillés
2. Vérifier le payload sauvegardé dans `BL_Payloads/`
3. Tester manuellement avec Postman

---

### 7. **Dates invalides (ShippingDate, EndDatePrep)**

**Critère** :
```csharp
// Si date nulle, le champ est exclu du JSON
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
public string? BR3PLShippingDate { get; set; }
```

**Conséquence** :
- Si `OPE_MODA = NULL` → `BR3PLShippingDate` non envoyé
- ⚠️ Dynamics peut rejeter si champ requis

**Solution** : Vérifier les dates dans SpeedWMS OPE_DAT

---

### 8. **Succès partiel (payloads partiellement envoyés)**

**Critère** :
```csharp
var message = $"{successCount}/{payloads.Count} payloads envoyés";
if (!allSuccess)
{
    await _jsonOutService.LogBLExportAsync(
        bl.BLNumber, 
        bl.ImportId, 
        message, 
        BLExportStatusConstants.Error,  // ← BL_ERROR
        "Succès partiel"
    );
}
```

**Conséquence** : BL marqué **BL_ERROR** dans JSON_OUT

**Solution** :
1. Vérifier les logs pour identifier le payload en erreur
2. Corriger le problème (données, réseau)
3. Supprimer la ligne JSON_OUT
4. Relancer `dotnet run cr_prep`

---

## 🔧 Configuration appsettings.json

```json
{
  "BLExport": {
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 30,
    "BatchSize": 10,
    "EnableConfirmationPost": true,
    "DefaultInventLocationId": "RECNOLP"
  },
  
  "SpeedWmsConnection": "Server=serveur;Database=MSY_SF_RCT;User Id=sa;Password=***;"
}
```

### Variables critiques

| Variable | Impact si manquante/incorrecte |
|----------|-------------------------------|
| `SpeedWmsConnection` | ❌ Échec total - Impossible de récupérer les BL |
| `ResourceUrl` | ❌ Échec total - API Dynamics inaccessible |
| `EnableConfirmationPost` | ⚠️ Pas de 2ème POST (confirmation) |
| `DefaultInventLocationId` | ⚠️ Valeur incorrecte dans payloads |

---

## 📊 Traçabilité JSON_OUT

### États possibles

| JSON_DEST | JSON_TREN | Description | Retraitable ? |
|-----------|-----------|-------------|---------------|
| `BL_EXPORT` | `BL_DATA_{BL}_{Item}` | Données envoyées (1er POST) | ❌ Non |
| `BL_EXPORT` | `BL_CONFIRM_{BL}` | Confirmation envoyée (2ème POST) | ❌ Non |
| `BL_ERROR` | `BL_{BL}_BL_ERROR` | Erreur complète | ⚠️ **Actuellement bloqué** |
| `BL_ERROR` | - | Succès partiel | ⚠️ **Actuellement bloqué** |

### Requête de vérification

```sql
-- Voir tous les BL exportés
SELECT 
    JSON_KEYU,
    JSON_CRDA,
    JSON_DEST,
    JSON_TREN,
    JSON_IMPORT_ID,
    JSON_DATA
FROM JSON_OUT
WHERE JSON_DEST IN ('BL_EXPORT', 'BL_ERROR')
  AND (JSON_TREN LIKE 'BL_%' OR JSON_IMPORT_ID LIKE '%_20%')
ORDER BY JSON_CRDA DESC
```

### Requête : BL en erreur à retraiter

```sql
-- BL qui devraient être retraités mais sont bloqués
SELECT 
    JSON_IMPORT_ID,
    JSON_DEST,
    JSON_DATA as ErrorMessage,
    JSON_CRDA
FROM JSON_OUT
WHERE JSON_DEST = 'BL_ERROR'
  AND JSON_TREN LIKE 'BL_%'
ORDER BY JSON_CRDA DESC
```

**⚠️ Solution temporaire** : Supprimer ces lignes pour permettre le retraitement

---

## 📁 Fichiers de débogage

### Payloads sauvegardés

**Emplacement** : `BL_Payloads/{BL}_payload_{index}_{timestamp}.json`

**Exemple** : `BL_Payloads/50682_payload_000_20260109_104305.json`

**Utilité** :
- Debugging : Voir exactement ce qui a été envoyé
- Rejeu manuel : Tester avec Postman
- Audit : Traçabilité complète

**Structure** :
```json
{
  "dataAreaId": "br",
  "ImportId": "50682",
  "transRefId": "UATSO-000218",
  ...
}
```

---

## 🚀 Commandes d'exécution

### Export BL uniquement

```bash
dotnet run cr_prep
```

### Export dans un mode synchronisation complète

```bash
dotnet run
# Exécute tous les endpoints dont BL Export
```

---

## 🐛 Troubleshooting

### Problème : BL non exporté alors qu'en statut 070

**Vérifications** :
1. Vérifier anti-doublon JSON_OUT :
   ```sql
   SELECT * FROM JSON_OUT WHERE JSON_TREN LIKE '%{BL}%'
   ```
2. Vérifier OPE_REDO non vide
3. Vérifier lignes articles dans MIL_DAT
4. Consulter les logs : `Logs/log_YYYYMMDD.txt`

---

### Problème : Erreur "Succès partiel"

**Cause** : Certains payloads échoués

**Solution** :
1. Consulter les logs pour identifier le payload en erreur
2. Vérifier le fichier payload dans `BL_Payloads/`
3. Tester manuellement le payload avec Postman
4. Corriger les données dans SpeedWMS
5. Supprimer la ligne JSON_OUT
6. Relancer

---

### Problème : POST confirmation échoue (2ème POST)

**Symptôme** :
```
✅ BL 50682: 3/3 payloads envoyés
❌ Erreur confirmation BL 50682: HTTP 400
```

**Solution** :
1. Vérifier que le 1er POST a réussi
2. Vérifier l'endpoint de confirmation
3. Tester POST vide avec Postman :
   ```
   POST https://{dynamics}/data/BRPackingSlipValidationInterfaces/Microsoft.Dynamics.DataEntities.PostPackingSlip
   Body: (vide)
   ```

---

## 📈 Statistiques

### Exemple de sortie console

```
🚀 === DÉBUT BLEXPORT === 🚀
🔍 Récupération des BL depuis SpeedWMS...
📊 25 BL trouvés dans SpeedWMS
🔄 Transformation de 25 BL...
🔍 Vérification des BL déjà traités...
✅ BLs déjà traités: 18
📤 7 nouveaux BL à traiter...
📦 Batch 1/1: 7 BL
✅ Batch 1 - BL 50682: BL_SENT
✅ Batch 1 - BL 50683: BL_CONFIRMED
...
📊 Batch 1 terminé: 7/7 BL traités avec succès
✅ BLExport terminé: 7/7 BLs traités (100.0% succès), 21 POST envoyés, 7 confirmations en 45.2s
✅ === SYNCHRONISATION CR_PREP TERMINÉE === ✅
```

---

## 📝 Notes importantes

1. ✅ **Double POST obligatoire** : Données + Confirmation vide
2. ✅ **Payloads sauvegardés** dans `BL_Payloads/` pour audit
3. ✅ **Anti-doublon automatique** via JSON_OUT
4. ⚠️ **BL en erreur bloqués** : Problème logique à corriger (exclure BL_ERROR du filtre anti-doublon)
5. ⚠️ **Dates nullable** : Gérées via `JsonIgnore`
6. ⚠️ **1 payload par ligne article** : Si 3 articles → 3 POST de données + 1 POST confirmation

---

## 🔐 Sécurité

### Authentification

Même token OAuth2 que les autres endpoints.

### Permissions Dynamics 365

- **Endpoint validation** : Lecture/Écriture sur `BRPackingSlipValidationInterfaces`
- **Endpoint confirmation** : Exécution de l'action `PostPackingSlip`

---

## 🔄 Relation avec autres flux

| Flux | Lien avec INT36 |
|------|----------------|
| **INT35 (Sales Orders)** | BL confirme les commandes de vente récupérées par INT35 |
| **SpeedWMS** | Source unique des données BL |
| **JSON_OUT** | Traçabilité partagée avec tous les flux |
