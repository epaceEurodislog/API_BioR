// Fichier: Models/DynamicsModels.cs
// Classes de données pour l'API Dynamics et la synchronisation SQL Server

using System;
using System.Text.Json.Serialization;

namespace DynamicsApiToDatabase.Models
{
    /// <summary>
    /// Configuration d'un endpoint à synchroniser
    /// </summary>
    public class EndpointConfig
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string PrimaryKeyField { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    /// <summary>
    /// Résultat d'une synchronisation d'endpoint
    /// </summary>
    public class SyncResult
    {
        public string EndpointName { get; set; } = "";
        public int NewRecords { get; set; }
        public int UpdatedRecords { get; set; }
        public int UnchangedRecords { get; set; }
        public int DeletedRecords { get; set; }
        public int ErrorRecords { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public int TotalProcessed => NewRecords + UpdatedRecords + UnchangedRecords;

        public double SuccessRate => TotalProcessed > 0 ?
            (double)(TotalProcessed - ErrorRecords) / TotalProcessed * 100 : 0;

        public string GetSummary()
        {
            var status = Success ? "✅" : "❌";
            return $"{status} {EndpointName}: {NewRecords} nouveaux, {UpdatedRecords} modifiés, " +
                   $"{UnchangedRecords} inchangés, {DeletedRecords} supprimés, {ErrorRecords} erreurs " +
                   $"({Duration.TotalSeconds:F1}s, {SuccessRate:F1}% succès)";
        }
    }

    /// <summary>
    /// Statistiques de la table JSON_IN
    /// </summary>
    public class JsonInStatistics
    {
        public int TotalRecords { get; set; }
        public int ActiveRecords { get; set; }
        public int DeletedRecords { get; set; }
        public int UpdatedLast24h { get; set; }

        public string GetSummary()
        {
            var activePercent = TotalRecords > 0 ? (double)ActiveRecords / TotalRecords * 100 : 0;
            return $"Total: {TotalRecords:N0}, Actifs: {ActiveRecords:N0} ({activePercent:F1}%), " +
                   $"Supprimés: {DeletedRecords:N0}, Maj 24h: {UpdatedLast24h:N0}";
        }
    }

    /// <summary>
    /// Statistiques spécifiques aux articles
    /// </summary>
    public class ArticleStatistics
    {
        public int TotalArticles { get; set; }
        public int ActiveArticles { get; set; }
        public int ConfirmedArticles { get; set; }
        public int ProcessedArticles { get; set; }
        public int DeletedArticles { get; set; }
        public int AddedLast24h { get; set; }
        public int AddedLast7Days { get; set; }

        public double ConfirmationRate => TotalArticles > 0 ?
            (double)ConfirmedArticles / TotalArticles * 100 : 0;

        public string GetSummary()
        {
            return $"Total: {TotalArticles:N0}, Actifs: {ActiveArticles:N0}, " +
                   $"Confirmés: {ConfirmedArticles:N0} ({ConfirmationRate:F1}%), " +
                   $"Ajoutés 24h: {AddedLast24h:N0}, 7j: {AddedLast7Days:N0}";
        }
    }

    /// <summary>
    /// Résultat d'une confirmation d'article
    /// </summary>
    public class ConfirmationResult
    {
        public string ItemId { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime AttemptTime { get; set; } = DateTime.Now;
        public int AttemptNumber { get; set; } = 1;
    }

    /// <summary>
    /// Résultat d'une confirmation en lot
    /// </summary>
    public class BatchConfirmationResult
    {
        public int TotalItems { get; set; }
        public int SuccessfulConfirmations { get; set; }
        public int FailedConfirmations { get; set; }
        public TimeSpan Duration { get; set; }
        public List<ConfirmationResult> Results { get; set; } = new();

        public double SuccessRate => TotalItems > 0 ?
            (double)SuccessfulConfirmations / TotalItems * 100 : 0;

        public string GetSummary()
        {
            return $"Confirmations: {SuccessfulConfirmations}/{TotalItems} réussies " +
                   $"({SuccessRate:F1}%) en {Duration.TotalSeconds:F1}s";
        }
    }


    /// <summary>
    /// Statistiques des confirmations optimisées
    /// </summary>
    public class ConfirmationStatistics
    {
        public int TotalArticles { get; set; }
        public int ConfirmedArticles { get; set; }
        public int PendingConfirmations { get; set; }
        public int ConfirmedLast24h { get; set; }

        public double ConfirmationRate => TotalArticles > 0 ?
            (double)ConfirmedArticles / TotalArticles * 100 : 0;

        public string GetSummary()
        {
            return $"Total: {TotalArticles:N0}, Confirmés: {ConfirmedArticles:N0} ({ConfirmationRate:F1}%), " +
                   $"En attente: {PendingConfirmations:N0}, Confirmés 24h: {ConfirmedLast24h:N0}";
        }
    }

    /// <summary>
    /// Statut d'un article dans le processus de synchronisation
    /// </summary>
    public enum ArticleStatus
    {
        Active,
        Confirmed,
        Processed,
        Deleted,
        Error
    }

    /// <summary>
    /// Information détaillée sur un article
    /// </summary>
    public class ArticleInfo
    {
        public string ItemId { get; set; } = "";
        public string BusinessKey { get; set; } = "";
        public ArticleStatus Status { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdated { get; set; }
        public string ContentHash { get; set; } = "";
        public bool IsConfirmed { get; set; }
        public string JsonData { get; set; } = "";
    }

    /// <summary>
    /// Modèle représentant une ligne de Sales Order (PackingSlip)
    /// </summary>
    public class SalesOrderLine
    {
        public string TransRefId { get; set; } = "";
        public string BRPortalOrderNumber { get; set; } = "";
        public long WMSTRansRecId { get; set; }
        public string ItemId { get; set; } = "";
        public decimal Qty { get; set; }
        public string INT3PLStatus { get; set; } = "";
        public string ExpeditionStatus { get; set; } = "";
        public string Customer { get; set; } = "";
        public string DeliveryName { get; set; } = "";
        public DateTime DlvDate { get; set; }
        public string DataAreaId { get; set; } = "";
        public string TransType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Contact { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
        public string ZipCode { get; set; } = "";
        public string CountryRegionId { get; set; } = "";
        public string CarrierCode { get; set; } = "";
        public string CarrierServiceCode { get; set; } = "";
        public string CommentPreparation { get; set; } = "";
        public string CommentExpedition { get; set; } = "";
        public string PickingRouteID { get; set; } = "";
        public string ItemBarCode { get; set; } = "";

        /// <summary>
        /// Indique si la ligne est en attente de traitement
        /// </summary>
        public bool IsPending => INT3PLStatus == "None";

        /// <summary>
        /// Indique si la ligne a été traitée
        /// </summary>
        public bool IsProcessed => INT3PLStatus == "Processed";

        /// <summary>
        /// Indique si l'expédition est activée
        /// </summary>
        public bool IsActivated => ExpeditionStatus == "Activated";

        /// <summary>
        /// Génère un résumé de la ligne
        /// </summary>
        public string GetSummary()
        {
            return $"Sales Order {TransRefId} - Item {ItemId} - Qty {Qty} - Status {INT3PLStatus}";
        }

        /// <summary>
        /// Génère l'adresse de livraison formatée
        /// </summary>
        public string GetDeliveryAddress()
        {
            var address = $"{DeliveryName}";
            if (!string.IsNullOrEmpty(Street)) address += $"\n{Street}";
            if (!string.IsNullOrEmpty(ZipCode) || !string.IsNullOrEmpty(City))
                address += $"\n{ZipCode} {City}";
            if (!string.IsNullOrEmpty(CountryRegionId)) address += $"\n{CountryRegionId}";
            return address;
        }
    }

    /// <summary>
    /// Résultat de synchronisation spécifique aux Sales Orders
    /// </summary>
    public class SalesOrderSyncResult : SyncResult
    {
        public int ProcessedOrdersCount { get; set; }
        public int ActivatedOrdersCount { get; set; }
        public int PendingOrdersCount { get; set; }
        public List<string> ProcessedOrderIds { get; set; } = new();
        public List<string> FailedOrderIds { get; set; } = new();

        public double ProcessingRate => TotalProcessed > 0 ?
            (double)ProcessedOrdersCount / TotalProcessed * 100 : 0;

        public new string GetSummary()
        {
            var baseStatus = Success ? "✅" : "❌";
            return $"{baseStatus} Sales Orders: {NewRecords} nouvelles, {UpdatedRecords} modifiées, " +
                   $"{ProcessedOrdersCount} traitées, {ActivatedOrdersCount} activées, " +
                   $"{PendingOrdersCount} en attente ({Duration.TotalSeconds:F1}s)";
        }
    }

    /// <summary>
    /// Configuration spécifique aux Sales Orders
    /// </summary>
    public class SalesOrderConfig
    {
        public string EndpointPath { get; set; } = "data/BRPackingSlipInterfaces";
        public string EndpointName { get; set; } = "SalesOrders";
        public string PrimaryKeyField { get; set; } = "WMSTRansRecId";
        public string OrderIdField { get; set; } = "transRefId";
        public string PortalNumberField { get; set; } = "BRPortalOrderNumber";
        public string StatusField { get; set; } = "INT3PLStatus";
        public string ExpeditionStatusField { get; set; } = "expeditionStatus";
        public string DefaultStatus { get; set; } = "None";
        public string ProcessedStatus { get; set; } = "Processed";
        public string ShippedStatus { get; set; } = "Shipped";
        public bool EnableAutoConfirmation { get; set; } = true;
        public int ConfirmationBatchSize { get; set; } = 10;
        public int ConfirmationDelayMs { get; set; } = 300;
    }

    /// <summary>
    /// Résultat de confirmation avec traçabilité pour Sales Orders
    /// </summary>
    public class SalesOrderConfirmationResult
    {
        public string SalesOrderId { get; set; } = "";
        public string PortalOrderNumber { get; set; } = "";
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public DateTime AttemptTime { get; set; } = DateTime.Now;
        public int AttemptNumber { get; set; } = 1;
        public int LinesProcessed { get; set; }
        public int LinesSuccessful { get; set; }
        public string PreviousStatus { get; set; } = "";
        public string NewStatus { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public string ResponseContent { get; set; } = "";
        public int HttpStatusCode { get; set; }

        public double LineSuccessRate => LinesProcessed > 0 ?
            (double)LinesSuccessful / LinesProcessed * 100 : 0;

        public string GetDetailedSummary()
        {
            var status = Success ? "✅ SUCCÈS" : "❌ ÉCHEC";
            var summary = $"{status} - Sales Order {SalesOrderId}";

            if (!string.IsNullOrEmpty(PortalOrderNumber))
                summary += $" (Portal: {PortalOrderNumber})";

            summary += $" - {LinesSuccessful}/{LinesProcessed} lignes";

            if (!string.IsNullOrEmpty(PreviousStatus) && !string.IsNullOrEmpty(NewStatus))
                summary += $" - {PreviousStatus} → {NewStatus}";

            summary += $" - {ProcessingTime.TotalSeconds:F1}s";

            if (!Success && !string.IsNullOrEmpty(ErrorMessage))
                summary += $" - Erreur: {ErrorMessage}";

            return summary;
        }
    }

    /// <summary>
    /// Classe pour représenter une ligne de commande avec informations détaillées
    /// </summary>
    public class OrderLineInfo
    {
        public string OrderId { get; set; } = "";
        public string ItemId { get; set; } = "";
        public int LineNumber { get; set; }
        public decimal Quantity { get; set; }
        public string OrderType { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdated { get; set; }

        public string GetDisplayName()
        {
            return $"{OrderType} {OrderId} - Line {LineNumber} - Item {ItemId} (Qty: {Quantity})";
        }
    }

    /// <summary>
    /// Statistiques des Sales Orders
    /// </summary>
    public class SalesOrderStatistics
    {
        public int TotalOrders { get; set; }
        public int TotalLines { get; set; }
        public int PendingLines { get; set; }
        public int ProcessedLines { get; set; }
        public int ActivatedLines { get; set; }
        public int CreatedLast24h { get; set; }
        public decimal AverageQuantity { get; set; }

        public double ProcessingRate => TotalLines > 0 ? (double)ProcessedLines / TotalLines * 100 : 0;
        public double ActivationRate => TotalLines > 0 ? (double)ActivatedLines / TotalLines * 100 : 0;

        public string GetSummary()
        {
            return $"Commandes: {TotalOrders:N0}, Lignes: {TotalLines:N0}, " +
                   $"Traitées: {ProcessedLines:N0} ({ProcessingRate:F1}%), " +
                   $"Activées: {ActivatedLines:N0} ({ActivationRate:F1}%), " +
                   $"Créées 24h: {CreatedLast24h:N0}";
        }
    }
}