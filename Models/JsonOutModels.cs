// Fichier: Models/JsonOutModels.cs
// Modèles de données pour la traçabilité JSON_OUT

using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamicsApiToDatabase.Models
{
    /// <summary>
    /// Enregistrement JSON_OUT pour traçabilité des envois
    /// </summary>
    public class JsonOutRecord
    {
        public int JsonKeyU { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Destination { get; set; } = "";
        public string RequestData { get; set; } = "";
        public string ResponseData { get; set; } = "";
        public string Status { get; set; } = "";
        public int HttpCode { get; set; }
        public int RetryCount { get; set; }

        public bool IsSuccessful => Status == "SUCCESS" && HttpCode >= 200 && HttpCode < 300;
        public bool IsPending => Status == "PENDING";
        public bool IsFailed => Status == "ERROR" || HttpCode >= 400;

        public string GetStatusIcon()
        {
            return Status switch
            {
                "SUCCESS" => "✅",
                "ERROR" => "❌",
                "PENDING" => "⏳",
                _ => "❓"
            };
        }

        public string GetSummary()
        {
            var icon = GetStatusIcon();
            var retryInfo = RetryCount > 0 ? $" (retry {RetryCount})" : "";
            return $"{icon} {Status} - HTTP {HttpCode}{retryInfo} - {CreatedDate:dd/MM HH:mm}";
        }
    }

    /// <summary>
    /// Statistiques des envois JSON_OUT
    /// </summary>
    public class JsonOutStatistics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public int PendingRequests { get; set; }
        public int RequestsLast24h { get; set; }
        public int AverageHttpCode { get; set; }

        public double SuccessRate => TotalRequests > 0 ?
            (double)SuccessfulRequests / TotalRequests * 100 : 0;

        public double FailureRate => TotalRequests > 0 ?
            (double)FailedRequests / TotalRequests * 100 : 0;

        public string GetSummary()
        {
            return $"Total: {TotalRequests:N0}, Succès: {SuccessfulRequests:N0} ({SuccessRate:F1}%), " +
                   $"Échecs: {FailedRequests:N0} ({FailureRate:F1}%), En attente: {PendingRequests:N0}, " +
                   $"Dernières 24h: {RequestsLast24h:N0}";
        }

        public string GetHealthStatus()
        {
            if (SuccessRate >= 95) return "🟢 EXCELLENT";
            if (SuccessRate >= 85) return "🟡 BON";
            if (SuccessRate >= 70) return "🟠 MOYEN";
            return "🔴 CRITIQUE";
        }
    }

    /// <summary>
    /// Traçabilité complète d'un article (JSON_IN + JSON_OUT)
    /// </summary>
    public class ArticleTraceability
    {
        public int JsonInId { get; set; }
        public string ItemId { get; set; } = "";
        public bool IsConfirmedLocally { get; set; }
        public DateTime ReceivedDate { get; set; }

        public int? JsonOutId { get; set; }
        public string ConfirmationStatus { get; set; } = "";
        public int? HttpCode { get; set; }
        public DateTime? SentDate { get; set; }
        public int RetryCount { get; set; }

        public bool HasBeenSent => JsonOutId.HasValue;
        public bool IsFullyProcessed => IsConfirmedLocally && HasBeenSent && ConfirmationStatus == "SUCCESS";
        public bool HasIssues => HasBeenSent && ConfirmationStatus == "ERROR";
        public bool IsPendingConfirmation => !HasBeenSent || ConfirmationStatus == "PENDING";

        public TimeSpan? ProcessingTime => SentDate.HasValue ?
            SentDate.Value - ReceivedDate : null;

        public string GetStatusSummary()
        {
            if (IsFullyProcessed)
                return $"✅ COMPLET - Confirmé en {ProcessingTime?.TotalMinutes:F1}min";

            if (HasIssues)
                return $"❌ ÉCHEC - HTTP {HttpCode} ({RetryCount} tentatives)";

            if (IsPendingConfirmation)
                return "⏳ EN ATTENTE - Pas encore confirmé";

            return "❓ STATUT INCONNU";
        }

        public string GetTraceabilityReport()
        {
            var report = $"📋 ARTICLE: {ItemId}\n";
            report += $"   📥 Reçu: {ReceivedDate:dd/MM/yyyy HH:mm}\n";
            report += $"   💾 Marqué local: {(IsConfirmedLocally ? "✅ Oui" : "❌ Non")}\n";

            if (HasBeenSent)
            {
                report += $"   📤 Envoyé: {SentDate:dd/MM/yyyy HH:mm}\n";
                report += $"   🌐 Statut API: {ConfirmationStatus} (HTTP {HttpCode})\n";

                if (RetryCount > 0)
                    report += $"   🔄 Tentatives: {RetryCount}\n";
            }
            else
            {
                report += $"   📤 Envoyé: ❌ Jamais envoyé\n";
            }

            report += $"   📊 État: {GetStatusSummary()}";

            return report;
        }
    }

    /// <summary>
    /// Paramètres pour la recherche de traçabilité
    /// </summary>
    public class TraceabilitySearchParams
    {
        public string? ItemId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? Status { get; set; }
        public bool? OnlyWithIssues { get; set; }
        public int Limit { get; set; } = 100;

        public string GetSearchSummary()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(ItemId))
                parts.Add($"Article: {ItemId}");

            if (FromDate.HasValue)
                parts.Add($"Depuis: {FromDate:dd/MM/yyyy}");

            if (ToDate.HasValue)
                parts.Add($"Jusqu'à: {ToDate:dd/MM/yyyy}");

            if (!string.IsNullOrEmpty(Status))
                parts.Add($"Statut: {Status}");

            if (OnlyWithIssues == true)
                parts.Add("Problèmes uniquement");

            parts.Add($"Limite: {Limit}");

            return string.Join(" | ", parts);
        }
    }

    /// <summary>
    /// Résultat de traitement avec traçabilité
    /// </summary>
    public class ProcessResultWithTraceability
    {
        public bool Success { get; set; }
        public bool IsNewOrModified { get; set; }
        public int? JsonInId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Résultat de confirmation avec traçabilité complète
    /// </summary>
    public class ConfirmationResultWithTraceability
    {
        public string ItemId { get; set; } = "";
        public bool Success { get; set; }
        public int? JsonInId { get; set; }
        public int? JsonOutId { get; set; }
        public string? ErrorMessage { get; set; }
        public int HttpCode { get; set; }
        public string? ResponseContent { get; set; }
        public DateTime AttemptTime { get; set; } = DateTime.Now;
        public int AttemptNumber { get; set; } = 1;

        public string GetResultSummary()
        {
            var status = Success ? "✅ SUCCÈS" : "❌ ÉCHEC";
            var tracking = JsonOutId.HasValue ? $" (Track: {JsonOutId})" : "";
            return $"{status} - {ItemId} - HTTP {HttpCode}{tracking}";
        }
    }
}