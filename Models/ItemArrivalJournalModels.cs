// Fichier: Models/ItemArrivalJournalModels.cs
// Modèles pour les journaux de réception ItemArrival basés sur REE_DAT
// 3 endpoints : Headers, Lines, et Confirmation Post

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DynamicsApiToDatabase.Models
{
    /// <summary>
    /// Données brutes provenant de REE_DAT pour les journaux de réception
    /// </summary>
    public class REEData
    {
        // Données en-tête depuis REE_DAT
        public string REE_NOREIN { get; set; } = "";           // Bon de Livraison (PackingSlipId)
        public DateTime REE_DARE { get; set; }                 // Date de réception
        public string REE_ETRE { get; set; } = "200";          // Statut réception (fixe)
        public string ACT_CODE { get; set; } = "COSMETIQUE";   // Activité (fixe)
        public string REE_CCLI { get; set; } = "BR";           // Compte (fixe)
        public string QUA_CODE { get; set; } = "";             // Code Qualité

        // Navigation vers les données MVT_DAT et REL_DAT
        public List<REEMovementData> Movements { get; set; } = new();
    }

    /// <summary>
    /// Données de mouvement depuis MVT_DAT
    /// </summary>
    public class REEMovementData
    {
        public string REA_RFCE { get; set; } = "";             // Numéro Attendu Réception
        public string REA_RFTI { get; set; } = "";             // N° Commande Achat
        public int NoLR { get; set; }                          // Numéro Ligne

        // Navigation vers les lignes d'articles
        public List<REELineData> Lines { get; set; } = new();
    }

    /// <summary>
    /// Données de ligne depuis REL_DAT
    /// </summary>
    public class REELineData
    {
        public string ART_CODE { get; set; } = "";             // Référence article
        public long ART_QTEUB { get; set; }                    // Quantité réceptionnée
        public string REL_LOT1 { get; set; } = "";             // Lot
        public DateTime? REL_DLUO { get; set; }                // DLUO/DLC
        public string REL_LOT2 { get; set; } = "";             // Lot 2 (N° série Machine)
        public int NoLR { get; set; }                          // Référence ligne
    }

    /// <summary>
    /// Données consolidées pour traitement ItemArrivalJournal
    /// </summary>
    public class ItemArrivalJournalData
    {
        // Identifiants

        //MODIFICATION RD 08/08/2025
        //public string JournalNumber { get; set; } = "";        // Généré automatiquement
        public string JournalLog { get; set; } = "";        // Généré automatiquement
        public string PackingSlipId { get; set; } = "";        // REE_NOREIN
        public string TransactionReferenceNumber { get; set; } = ""; // REA_RFTI (N° Commande)
        public DateTime TransactionDate { get; set; }          // REE_DARE

        // Configuration fixe
        public string DataAreaId { get; set; } = "BR";
        public string JournalNameId { get; set; } = "ARR";
        public string DefaultReceivingSiteId { get; set; } = "S01";
        public string DefaultReceivingWarehouseId { get; set; } = "12";
        public string DefaultReceivingWarehouseLocationId { get; set; } = "RECNOLP";

        // Lignes regroupées
        public List<ItemArrivalJournalLine> Lines { get; set; } = new();

        // Métadonnées
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public bool HasErrors { get; set; }
        public List<string> ErrorMessages { get; set; } = new();
    }

    /// <summary>
    /// Ligne d'article pour le journal de réception
    /// </summary>
    public class ItemArrivalJournalLine
    {
        public int LineNumber { get; set; }                    // NoLR
        public string ReferenceInventoryLotId { get; set; } = ""; // REA_ALPHA2
        public string ItemNumber { get; set; } = "";          // ART_CODE
        public long ItemQuantity { get; set; }                // ART_QTEUB (cumulée par NoLR)
        public string ItemBatchNumber { get; set; } = "";     // REL_LOT1
        public string ItemSerialNumber { get; set; } = "";    // REL_LOT2
        public DateTime? ExpDate { get; set; }                 // REL_DLUO
        public string ReceivingInventoryStatusId { get; set; } = ""; // Basé sur QUA_CODE
    }

    /// <summary>
    /// Payload pour l'endpoint ItemArrivalJournalHeadersV2
    /// </summary>
    public class ItemArrivalJournalHeaderPayload
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = "BR";

        [JsonPropertyName("JournalNameId")]
        public string JournalNameId { get; set; } = "ARR";

        [JsonPropertyName("DefaultReceivingSiteId")]
        public string DefaultReceivingSiteId { get; set; } = "S01";

        [JsonPropertyName("DefaultReceivingWarehouseId")]
        public string DefaultReceivingWarehouseId { get; set; } = "12";

        [JsonPropertyName("DefaultReceivingWarehouseLocationId")]
        public string DefaultReceivingWarehouseLocationId { get; set; } = "RECNOLP";

        [JsonPropertyName("DefaultTransactionReferenceNumber")]
        public string DefaultTransactionReferenceNumber { get; set; } = "";

        [JsonPropertyName("PackingSlipId")]
        public string PackingSlipId { get; set; } = "";
    }

    /// <summary>
    /// Payload pour l'endpoint ItemArrivalJournalLinesV2
    /// </summary>
    public class ItemArrivalJournalLinePayload
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = "BR";

        [JsonPropertyName("JournalNumber")]
        public string JournalNumber { get; set; } = "";

        [JsonPropertyName("LineNumber")]
        public int LineNumber { get; set; }

        [JsonPropertyName("ReferenceInventoryLotId")]
        public string ReferenceInventoryLotId { get; set; }

        [JsonPropertyName("ItemNumber")]
        public string ItemNumber { get; set; } = "";

        [JsonPropertyName("TransactionDate")]
        public string TransactionDate { get; set; } = "";

        [JsonPropertyName("ItemSerialNumber")]
        public string ItemSerialNumber { get; set; } = "";

        [JsonPropertyName("ItemBatchNumber")]
        public string ItemBatchNumber { get; set; } = "";

        [JsonPropertyName("ItemQuantity")]
        public long ItemQuantity { get; set; }

        [JsonPropertyName("expDate")]
        public string ExpDate { get; set; } = "";

        [JsonPropertyName("ReceivingInventoryStatusId")]
        public string ReceivingInventoryStatusId { get; set; } = "";
    }

    /// <summary>
    /// Payload pour l'endpoint de confirmation BRINT41PostJournalService
    /// </summary>
    public class ItemArrivalJournalConfirmationPayload
    {
        [JsonPropertyName("_request")]
        public ItemArrivalJournalConfirmationRequest Request { get; set; } = new();
    }

    public class ItemArrivalJournalConfirmationRequest
    {
        [JsonPropertyName("DataAreaId")]
        public string DataAreaId { get; set; } = "BR";

        [JsonPropertyName("JournalNumber")]
        public string JournalNumber { get; set; } = "";
    }

    /// <summary>
    /// Résultat du traitement d'un journal de réception
    /// </summary>
    public class ItemArrivalJournalResult
    {
        public bool Success { get; set; }

        //MODIFICATION RD 08/08/2025
        //public string JournalNumber { get; set; } = "";
        public string JournalLog { get; set; } = "";
        public string PackingSlipId { get; set; } = "";
        public string Status { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public int HeadersSent { get; set; }
        public int LinesSent { get; set; }
        public bool IsConfirmed { get; set; }
    }

    /// <summary>
    /// Rapport de traitement pour les journaux de réception
    /// </summary>
    public class ItemArrivalJournalReport
    {
        public int TotalJournals { get; set; }
        public int SuccessfulJournals { get; set; }
        public int FailedJournals { get; set; }
        public int TotalHeaders { get; set; }
        public int TotalLines { get; set; }
        public int ConfirmedJournals { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public List<string> ErrorMessages { get; set; } = new();

        public double SuccessRate => TotalJournals > 0 ? (double)SuccessfulJournals / TotalJournals * 100 : 0;

        public string GetSummary()
        {
            var report = "📊 === RAPPORT JOURNAUX DE RÉCEPTION === 📊\n";
            report += $"   📝 Journaux traités: {TotalJournals}\n";
            report += $"   ✅ Succès: {SuccessfulJournals} ({SuccessRate:F1}%)\n";
            report += $"   ❌ Échecs: {FailedJournals}\n";
            report += $"   📤 En-têtes envoyés: {TotalHeaders}\n";
            report += $"   📄 Lignes envoyées: {TotalLines}\n";
            report += $"   ✅ Journaux confirmés: {ConfirmedJournals}\n";
            report += $"   ⏱️ Durée totale: {TotalProcessingTime.TotalMinutes:F1} minutes\n";
            return report;
        }
    }

    /// <summary>
    /// Configuration pour le processus ItemArrivalJournal
    /// </summary>
    public class ItemArrivalJournalConfig
    {
        public string SpeedWmsConnectionString { get; set; } = "";
        public string DynamicsBaseUrl { get; set; } = "";
        public string HeadersEndpoint { get; set; } = "data/ItemArrivalJournalHeadersV2";
        public string LinesEndpoint { get; set; } = "data/ItemArrivalJournalLinesV2";
        public string ConfirmationEndpoint { get; set; } = "api/services/BRINT41PostJournalServiceGroup/BRINT41PostJournalService/post";
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelaySeconds { get; set; } = 30;
        public int BatchSize { get; set; } = 10;
        public bool EnableConfirmationPost { get; set; } = true;
        public string DefaultJournalNameId { get; set; } = "ARR";
        public string DefaultReceivingSiteId { get; set; } = "S01";
        public string DefaultReceivingWarehouseId { get; set; } = "12";
        public string DefaultReceivingWarehouseLocationId { get; set; } = "RECNOLP";
    }

    /// <summary>
    /// Énumération des statuts possibles pour un journal de réception
    /// </summary>
    public enum ItemArrivalJournalStatus
    {
        Pending,                // En attente de traitement
        Processing,             // En cours de traitement
        HeadersSent,            // En-têtes envoyés
        LinesSent,             // Lignes envoyées
        Confirmed,             // Journal confirmé
        Error,                 // Erreur lors du traitement
        PendingRetry           // En attente de retry
    }

    /// <summary>
    /// Classe pour mapper les constantes de statuts JSON_OUT pour ItemArrivalJournal
    /// </summary>
    public static class ItemArrivalJournalStatusConstants
    {
        public const string HeadersSent = "JOURNAL_HEADERS_SENT";           // En-têtes envoyés
        public const string LinesSent = "JOURNAL_LINES_SENT";               // Lignes envoyées
        public const string Confirmed = "JOURNAL_CONFIRMED";                // Journal confirmé
        public const string Error = "JOURNAL_ERROR";                        // Erreur de traitement
        public const string PendingRetry = "JOURNAL_PENDING_RETRY";         // En attente de retry
        public const string Processing = "JOURNAL_PROCESSING";              // En cours de traitement
    }

    /// <summary>
    /// Helper pour mapper les codes qualité vers les statuts d'inventaire
    /// </summary>
    public static class QualityCodeMapper
    {
        public static string MapToInventoryStatus(string quaCode)
        {
            return quaCode?.ToUpper() switch
            {
                "DISP" => "Libéré",
                _ => "Bloqué"
            };
        }
    }
}