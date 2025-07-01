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
}