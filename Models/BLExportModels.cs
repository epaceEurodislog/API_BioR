// Fichier: Models/BLExportModels.cs
// Modèles de données pour l'export des BL depuis SpeedWMS vers Dynamics 365

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DynamicsApiToDatabase.Models
{
    /// <summary>
    /// Données brutes d'un BL récupérées depuis SpeedWMS
    /// </summary>
    public class SpeedWmsBLData
    {
        public string OpeKeyu { get; set; } = "";           // Numéro BL (clé principale)
        public string OpeRedo { get; set; } = "";           // Référence commande D365
        public string OpeAlpha17 { get; set; } = "";        // Picking Route ID
        public string OpeCtra { get; set; } = "";           // Code Transport
        public string OpeAlpha40 { get; set; } = "";        // Code service transport (partie 1)
        public string OpeAlpha41 { get; set; } = "";        // Code service transport (partie 2)
        public DateTime? OpeModa { get; set; }              // Date expédition (si OPE_STAT=070)
        public string OpeTop22 { get; set; } = "";          // Statut Documentation
        public string OpeStat { get; set; } = "";           // Statut opération
        public DateTime? Fnc008Date { get; set; }           // Code Chargement
        public string DataHeurreIc { get; set; } = "";      // Date/Heure Statut Documentation

        // Données des lignes d'articles
        public List<SpeedWmsBLLine> Lines { get; set; } = new();

        // Données de support/emballage
        public List<SpeedWmsSupportData> Supports { get; set; } = new();
    }

    /// <summary>
    /// Ligne d'article d'un BL dans SpeedWMS
    /// </summary>
    public class SpeedWmsBLLine
    {
        public string OpeKeyu { get; set; } = "";           // Référence BL
        public string ArtCode { get; set; } = "";           // Code article
        public decimal QttePreparee { get; set; }           // Quantité préparée (MIL_QTTP)
        public decimal QttePrevue { get; set; }             // Quantité prévue (MIL_QTTA)
        public decimal QtteManquante { get; set; }          // Quantité manquante (MIL_QTMA)
        public string Lot1 { get; set; } = "";             // Lot (MIL_LOT1P)
        public string Lot2 { get; set; } = "";             // Lot 2/Série (MIL_LOT2P)
        public string Support { get; set; } = "";          // N° Support (MIL_SUPP)
        public DateTime? MaxMieModa { get; set; }           // Max MIE_MODA pour date fin préparation
        public DateTime? DluoMin { get; set; }              // DLUO minimum (OPL_DLOM)
    }

    /// <summary>
    /// Données de support/emballage depuis SpeedWMS
    /// </summary>
    public class SpeedWmsSupportData
    {
        public string SupportId { get; set; } = "";        // ID du support
        public string SupportType { get; set; } = "";      // Type support (Palette/Colis)
        public decimal Poids { get; set; }                 // Poids (SEX_POISR)
        public decimal Longueur { get; set; }              // Longueur (SEX_PROF)
        public decimal Largeur { get; set; }               // Largeur (SEX_LARG)
        public decimal Hauteur { get; set; }               // Hauteur (SEX_HAUT)
        public string SupportRegroupement { get; set; } = "";  // Support regroupement (SEX_SUPR)
        public string TypeRegroupement { get; set; } = "";     // Type regroupement
        public decimal PoidsRegroupement { get; set; }         // Poids regroupement calculé
        public decimal LongueurRegroupement { get; set; }      // Dimensions regroupement (EMB_PROF)
        public decimal LargeurRegroupement { get; set; }       // EMB_LARG
        public decimal HauteurRegroupement { get; set; }       // EMB_HAUT
    }

    /// <summary>
    /// BL complet avec toutes ses données transformées, prêt pour l'export
    /// </summary>
    public class BLExportData
    {
        public string BLNumber { get; set; } = "";                          // Numéro BL (OPE_KEYU)
        public string ImportId { get; set; } = "";                          // ImportId généré ({BL}_{timestamp})
        public string TransRefId { get; set; } = "";                        // Référence commande D365
        public string PickingRouteId { get; set; } = "";                    // Picking Route ID
        public string CarrierCode { get; set; } = "";                       // Code transporteur
        public string CarrierServiceCode { get; set; } = "";                // Code service transporteur
        public DateTime? ShippingDate { get; set; }                         // Date expédition
        public DateTime? EndDatePrep { get; set; }                          // Date fin préparation
        public string InventLocationId { get; set; } = "RECNOLP";           // Emplacement (fixe)
        public string DocStatus { get; set; } = "";                         // Statut documentation
        public DateTime? DocStatusDate { get; set; }                        // Date statut documentation

        // Lignes d'articles regroupées
        public List<BLExportLine> Lines { get; set; } = new();

        // Données de support
        public List<BLSupportInfo> Supports { get; set; } = new();

        // Métadonnées
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public bool HasErrors { get; set; }
        public List<string> ErrorMessages { get; set; } = new();
    }

    /// <summary>
    /// Ligne d'article regroupée pour l'export (un article peut avoir plusieurs lots)
    /// </summary>
    public class BLExportLine
    {
        public string ItemId { get; set; } = "";                   // Code article
        public decimal TotalQuantity { get; set; }                 // Quantité totale préparée
        public decimal PlannedQuantity { get; set; }               // Quantité prévue totale
        public decimal MissingQuantity { get; set; }               // Quantité manquante totale
        public List<string> BatchIds { get; set; } = new();        // Tous les lots (regroupés)
        public List<string> SerialIds { get; set; } = new();       // Tous les numéros de série
        public List<string> SupportIds { get; set; } = new();      // Supports utilisés
        public DateTime? MinDluo { get; set; }                     // DLUO minimum
    }

    /// <summary>
    /// Informations sur un support pour l'export
    /// </summary>
    public class BLSupportInfo
    {
        public string SupportId { get; set; } = "";
        public string SupportType { get; set; } = "";              // "Palette" ou "Colis"
        public decimal Weight { get; set; }
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public string GroupingSupportId { get; set; } = "";
        public string GroupingType { get; set; } = "";
        public decimal GroupingWeight { get; set; }
        public decimal GroupingLength { get; set; }
        public decimal GroupingWidth { get; set; }
        public decimal GroupingHeight { get; set; }
    }

    public class BRPackingSlipValidationPayload
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = "br";

        [JsonPropertyName("ImportId")]
        public string ImportId { get; set; } = "";

        [JsonPropertyName("transRefId")]
        public string TransRefId { get; set; } = "";

        [JsonPropertyName("BR3PLShippingDate")]
        public string BR3PLShippingDate { get; set; } = "";

        [JsonPropertyName("pickingRouteID")]
        public string PickingRouteID { get; set; } = "";

        [JsonPropertyName("CarrierServiceCode")]
        public string CarrierServiceCode { get; set; } = "";

        [JsonPropertyName("qty")]
        public decimal Qty { get; set; }

        [JsonPropertyName("BR3PLPackingSlipId")]
        public string BR3PLPackingSlipId { get; set; } = "";

        [JsonPropertyName("itemId")]
        public string ItemId { get; set; } = "";

        [JsonPropertyName("InventLocationId")]
        public string InventLocationId { get; set; } = "RECNOLP";

        [JsonPropertyName("BR3PLEndDatePrep")]
        public string BR3PLEndDatePrep { get; set; } = "";

        [JsonPropertyName("CarrierCode")]
        public string CarrierCode { get; set; } = "";

        [JsonPropertyName("inventSerialId")]
        public string InventSerialId { get; set; } = "";

        [JsonPropertyName("inventBatchId")]
        public string InventBatchId { get; set; } = "";
    }

    /// <summary>
    /// Résultat du traitement d'un BL
    /// </summary>
    public class BLExportResult
    {
        public string BLNumber { get; set; } = "";
        public string ImportId { get; set; } = "";
        public bool Success { get; set; }
        public bool FirstPostSuccess { get; set; }                  // POST données réussi
        public bool ConfirmationPostSuccess { get; set; }           // POST confirmation réussi
        public string ErrorMessage { get; set; } = "";
        public List<string> DetailedErrors { get; set; } = new();
        public DateTime ProcessedDate { get; set; } = DateTime.Now;
        public int RetryCount { get; set; }
        public string Status { get; set; } = "";                    // "BL_SENT", "BL_CONFIRMED", "BL_ERROR", etc.
        public TimeSpan ProcessingTime { get; set; }
        public int LinesProcessed { get; set; }
        public int PayloadsGenerated { get; set; }                  // Nombre de POST pour ce BL
    }

    /// <summary>
    /// Statistiques globales du processus BLExport
    /// </summary>
    public class BLExportStatistics
    {
        public int TotalBLsFound { get; set; }                      // BLs trouvés dans SpeedWMS
        public int BLsAlreadyProcessed { get; set; }                // BLs déjà dans JSON_OUT
        public int BLsToProcess { get; set; }                       // BLs à traiter
        public int BLsProcessedSuccessfully { get; set; }           // BLs traités avec succès
        public int BLsWithErrors { get; set; }                      // BLs en erreur
        public int TotalPayloadsSent { get; set; }                  // Nombre total de POST envoyés
        public int ConfirmationsSent { get; set; }                  // Nombre de confirmations envoyées
        public TimeSpan TotalProcessingTime { get; set; }
        public DateTime ProcessingStartTime { get; set; }
        public DateTime ProcessingEndTime { get; set; }

        public double SuccessRate => TotalBLsFound > 0 ?
            (double)BLsProcessedSuccessfully / BLsToProcess * 100 : 0;

        public string GetSummary()
        {
            return $"BLExport: {BLsProcessedSuccessfully}/{BLsToProcess} BLs traités " +
                   $"({SuccessRate:F1}% succès), {TotalPayloadsSent} POST envoyés, " +
                   $"{ConfirmationsSent} confirmations en {TotalProcessingTime.TotalSeconds:F1}s";
        }

        public string GetDetailedReport()
        {
            var report = "📊 === RAPPORT BLEXPORT === 📊\n";
            report += $"   🔍 BLs trouvés SpeedWMS: {TotalBLsFound}\n";
            report += $"   ✅ BLs déjà traités: {BLsAlreadyProcessed}\n";
            report += $"   🔄 BLs à traiter: {BLsToProcess}\n";
            report += $"   ✅ BLs traités avec succès: {BLsProcessedSuccessfully}\n";
            report += $"   ❌ BLs en erreur: {BLsWithErrors}\n";
            report += $"   📤 Total POST envoyés: {TotalPayloadsSent}\n";
            report += $"   📋 Confirmations envoyées: {ConfirmationsSent}\n";
            report += $"   📈 Taux de succès: {SuccessRate:F1}%\n";
            report += $"   ⏱️ Durée totale: {TotalProcessingTime.TotalMinutes:F1} minutes\n";
            return report;
        }
    }

    /// <summary>
    /// Configuration pour le processus BLExport
    /// </summary>
    public class BLExportConfig
    {
        public string SpeedWmsConnectionString { get; set; } = "";
        public string DynamicsBaseUrl { get; set; } = "";
        public string ValidationEndpoint { get; set; } = "data/BRPackingSlipValidationInterfaces";
        public string ConfirmationEndpoint { get; set; } = "data/BRPackingSlipValidationInterfaces/Microsoft.Dynamics.DataEntities.PostPackingSlip";
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelaySeconds { get; set; } = 30;
        public int BatchSize { get; set; } = 10;                    // Nombre de BLs à traiter en parallèle
        public bool EnableConfirmationPost { get; set; } = true;
        public string DefaultInventLocationId { get; set; } = "RECNOLP";
    }

    /// <summary>
    /// Énumération des statuts possibles pour un BL
    /// </summary>
    public enum BLExportStatus
    {
        Pending,                // En attente de traitement
        Processing,             // En cours de traitement
        DataSent,              // Données envoyées (1er POST réussi)
        Confirmed,             // Confirmation envoyée (2ème POST réussi)
        Error,                 // Erreur lors du traitement
        PendingRetry           // En attente de retry
    }

    /// <summary>
    /// Classe pour mapper les constantes de statuts JSON_OUT
    /// </summary>
    public static class BLExportStatusConstants
    {
        public const string Sent = "BL_SENT";                       // 1er POST réussi
        public const string Confirmed = "BL_CONFIRMED";             // 2ème POST réussi
        public const string Error = "BL_ERROR";                     // Erreur de traitement
        public const string PendingRetry = "BL_PENDING_RETRY";      // En attente de retry
        public const string Processing = "BL_PROCESSING";           // En cours de traitement
    }
}