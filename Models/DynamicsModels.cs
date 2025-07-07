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
}