// Fichier: Services/JsonOutService.cs
// Service complet pour stocker les envois JSON dans JSON_OUT avec support BLExport
// VERSION FINALE COMPLÈTE - TESTÉ ET VALIDÉ

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;
using System.Data;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Interface pour le service JsonOut
    /// </summary>
    public interface IJsonOutService
    {
        Task LogItemArrivalJournalAsync(string journalNumber, string packingSlipId, string jsonData, string destination, string errorMessage = "");
        Task<bool> EnsureImportIdColumnExistsAsync();
        Task LogBLExportAsync(string blNumber, string jsonData, string destination, string clientCode = "BR", string errorMessage = "", string importId = "");
        Task<string> GenerateJsonOutReportAsync();
    }

    /// <summary>
    /// Service pour enregistrer les envois JSON dans JSON_OUT
    /// Structure étendue: JSON_KEYU, JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_IMPORT_ID
    /// AVEC SUPPORT COMPLET BLEXPORT et gestion ImportId
    /// </summary>
    public class JsonOutService : IJsonOutService
    {
        private readonly string _connectionString;
        private readonly ILogger<JsonOutService> _logger;

        // 🔧 Tailles max des colonnes pour éviter les erreurs de troncature
        private const int MAX_JSON_DEST_LENGTH = 50;
        private const int MAX_JSON_CCLI_LENGTH = 10;
        private const int MAX_JSON_TREN_LENGTH = 50;
        private const int MAX_JSON_IMPORT_ID_LENGTH = 100;

        public JsonOutService(IConfiguration configuration, ILogger<JsonOutService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException("ConnectionString manquante");
            _logger = logger;
        }

        /// <summary>
        /// ✅ Initialise la table JSON_OUT avec la colonne ImportId si nécessaire
        /// </summary>
        public async Task<bool> EnsureImportIdColumnExistsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string checkColumnSql = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'JSON_OUT' 
                    AND COLUMN_NAME = 'JSON_IMPORT_ID'";

                using var checkCommand = new SqlCommand(checkColumnSql, connection);
                var columnExists = (int)await checkCommand.ExecuteScalarAsync() > 0;

                if (!columnExists)
                {
                    _logger.LogInformation("🔧 Ajout de la colonne JSON_IMPORT_ID à JSON_OUT...");

                    const string addColumnSql = @"
                        ALTER TABLE JSON_OUT 
                        ADD JSON_IMPORT_ID NVARCHAR(100) NULL";

                    using var addCommand = new SqlCommand(addColumnSql, connection);
                    await addCommand.ExecuteNonQueryAsync();

                    _logger.LogInformation("✅ Colonne JSON_IMPORT_ID ajoutée à JSON_OUT");
                }
                else
                {
                    _logger.LogInformation("✅ Colonne JSON_IMPORT_ID déjà présente");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la création/vérification de la colonne JSON_IMPORT_ID");
                return false;
            }
        }

        /// <summary>
        /// ✅ MÉTHODE UNIFIÉE : Enregistre un envoi BLExport
        /// </summary>
        public async Task LogBLExportAsync(string blNumber, string jsonData, string destination, string clientCode = "BR", string errorMessage = "", string importId = "")
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                INSERT INTO JSON_OUT (
                    JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_IMPORT_ID
                ) VALUES (
                    GETDATE(), @Destination, @ClientCode, @JsonData, @TransactionType, GETDATE(), @Environment, @ImportId
                )";

                using var command = new SqlCommand(sql, connection);

                // Déterminer le statut selon la présence d'erreur
                var actualDestination = string.IsNullOrEmpty(errorMessage) ? destination : "BL_ERROR";
                var status = string.IsNullOrEmpty(errorMessage) ? "BL_SENT" : "BL_ERROR";
                var dataToStore = string.IsNullOrEmpty(errorMessage) ? jsonData : errorMessage;

                // Tronquer les valeurs selon les limites de colonne
                var destination_safe = TruncateString(actualDestination, MAX_JSON_DEST_LENGTH);
                var clientCode_safe = TruncateString(clientCode, MAX_JSON_CCLI_LENGTH);
                var environment_safe = TruncateString($"BL_{blNumber}_{status}", MAX_JSON_TREN_LENGTH);
                var importId_safe = TruncateString(string.IsNullOrEmpty(importId) ? blNumber : importId, MAX_JSON_IMPORT_ID_LENGTH);

                command.Parameters.AddWithValue("@Destination", destination_safe);
                command.Parameters.AddWithValue("@ClientCode", clientCode_safe);
                command.Parameters.AddWithValue("@JsonData", dataToStore ?? "");
                command.Parameters.AddWithValue("@TransactionType", 1); // 1 = envoi
                command.Parameters.AddWithValue("@Environment", environment_safe);
                command.Parameters.AddWithValue("@ImportId", importId_safe);

                await command.ExecuteNonQueryAsync();

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogWarning($"⚠️ BL Export avec erreur pour {blNumber}: {errorMessage}");
                }
                else
                {
                    _logger.LogDebug($"📤 BL Export enregistré pour {blNumber}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur enregistrement BL Export pour {blNumber}");
                throw;
            }
        }

        /// <summary>
        /// ✅ Enregistre un envoi pour ItemArrivalJournal
        /// </summary>
        public async Task LogItemArrivalJournalAsync(string JournalLog, string packingSlipId,
            string jsonData, string destination, string errorMessage = "")
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                INSERT INTO JSON_OUT (
                    JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_IMPORT_ID
                ) VALUES (
                    GETDATE(), @Destination, @ClientCode, @JsonData, @TransactionType, GETDATE(), @Environment, @ImportId
                )";

                using var command = new SqlCommand(sql, connection);

                // Tronquer les valeurs selon les limites de colonne
                var destination_safe = TruncateString(destination, MAX_JSON_DEST_LENGTH);
                var environment_safe = TruncateString($"JOURNAL_{JournalLog}", MAX_JSON_TREN_LENGTH);
                var importId_safe = TruncateString($"{JournalLog}_{packingSlipId}", MAX_JSON_IMPORT_ID_LENGTH);

                command.Parameters.AddWithValue("@Destination", destination_safe);
                command.Parameters.AddWithValue("@ClientCode", "BR");
                command.Parameters.AddWithValue("@JsonData", jsonData ?? "");
                command.Parameters.AddWithValue("@TransactionType", 1); // 1 = envoi
                command.Parameters.AddWithValue("@Environment", environment_safe);
                command.Parameters.AddWithValue("@ImportId", importId_safe);

                await command.ExecuteNonQueryAsync();

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogWarning($"⚠️ Journal {JournalLog} - {destination}: {errorMessage}");
                }
                else
                {
                    _logger.LogDebug($"📝 Journal {JournalLog} - {destination} enregistré dans JSON_OUT");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur enregistrement JSON_OUT pour journal {JournalLog}");
                throw;
            }
        }

        /// <summary>
        /// ✅ Génère un rapport des activités JSON_OUT
        /// </summary>
        public async Task<string> GenerateJsonOutReportAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                SELECT 
                    JSON_DEST,
                    COUNT(*) as Total,
                    COUNT(CASE WHEN JSON_CRDA >= DATEADD(hour, -24, GETDATE()) THEN 1 END) as Last24h,
                    MAX(JSON_CRDA) as LastActivity
                FROM JSON_OUT 
                GROUP BY JSON_DEST
                ORDER BY Total DESC";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                var report = "📊 === RAPPORT JSON_OUT === 📊\n";
                report += $"Généré le : {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n\n";

                var totalRecords = 0;
                var total24h = 0;

                while (await reader.ReadAsync())
                {
                    var dest = reader.GetString("JSON_DEST");
                    var total = reader.GetInt32("Total");
                    var last24h = reader.GetInt32("Last24h");
                    var lastActivity = reader.GetDateTime("LastActivity");

                    totalRecords += total;
                    total24h += last24h;

                    report += $"📋 {dest}:\n";
                    report += $"   Total: {total:N0} | Dernières 24h: {last24h:N0}\n";
                    report += $"   Dernière activité: {lastActivity:dd/MM/yyyy HH:mm:ss}\n\n";
                }

                report += $"🎯 RÉSUMÉ GLOBAL:\n";
                report += $"   Total enregistrements: {totalRecords:N0}\n";
                report += $"   Activité 24h: {total24h:N0}\n";

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur génération rapport JSON_OUT");
                return "❌ Erreur lors de la génération du rapport";
            }
        }

        // ==========================================
        // MÉTHODES UTILITAIRES
        // ==========================================

        /// <summary>
        /// Tronque une chaîne à la longueur maximale
        /// </summary>
        private string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            if (input.Length <= maxLength)
                return input;

            return input.Substring(0, maxLength);
        }

        /// <summary>
        /// ✅ Vérifie si un BL a déjà été traité (éviter les doublons)
        /// </summary>
        public async Task<bool> IsBLAlreadyProcessedAsync(string blNumber)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT COUNT(*)
                    FROM JSON_OUT 
                    WHERE JSON_DEST IN ('BL_EXPORT', 'BL_ERROR')
                    AND (JSON_TREN LIKE '%' + @BLNumber + '%' 
                         OR JSON_IMPORT_ID LIKE @BLNumber + '_%')";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@BLNumber", blNumber);

                var count = (int)await command.ExecuteScalarAsync();
                var isProcessed = count > 0;

                _logger.LogDebug($"🔍 BL {blNumber}: {(isProcessed ? "déjà traité" : "nouveau")} ({count} occurrences)");
                return isProcessed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur vérification BL {blNumber}");
                return false; // En cas d'erreur, considérer comme non traité
            }
        }

        /// <summary>
        /// ✅ Obtient des statistiques BLExport
        /// </summary>
        public async Task<BLExportStatistics> GetBLExportStatisticsAsync()
        {
            var stats = new BLExportStatistics();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        COUNT(DISTINCT JSON_IMPORT_ID) as TotalBLs,
                        COUNT(CASE WHEN JSON_TREN LIKE '%CONFIRMED%' THEN 1 END) as BLsConfirmed,
                        COUNT(CASE WHEN JSON_TREN LIKE '%SENT%' THEN 1 END) as BLsSent,
                        COUNT(CASE WHEN JSON_TREN LIKE '%ERROR%' THEN 1 END) as BLsWithErrors,
                        COUNT(*) as TotalPayloads
                    FROM JSON_OUT 
                    WHERE JSON_DEST IN ('BL_EXPORT', 'BL_ERROR')
                    AND JSON_IMPORT_ID IS NOT NULL
                    AND JSON_IMPORT_ID != ''";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    stats.TotalBLsFound = reader.GetInt32("TotalBLs");
                    stats.BLsProcessedSuccessfully = reader.GetInt32("BLsConfirmed") + reader.GetInt32("BLsSent");
                    stats.BLsWithErrors = reader.GetInt32("BLsWithErrors");
                    stats.TotalPayloadsSent = reader.GetInt32("TotalPayloads");
                    stats.ConfirmationsSent = reader.GetInt32("BLsConfirmed");
                    stats.BLsToProcess = stats.TotalBLsFound;
                    stats.BLsAlreadyProcessed = 0;
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération statistiques BLExport");
                return stats;
            }
        }

        /// <summary>
        /// ✅ Compte les envois JSON_OUT des dernières 24h
        /// </summary>
        public async Task<int> GetLast24HoursCountAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT COUNT(*) 
                    FROM JSON_OUT 
                    WHERE JSON_CRDA >= DATEADD(hour, -24, GETDATE())";

                using var command = new SqlCommand(sql, connection);
                var count = (int)await command.ExecuteScalarAsync();

                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du comptage JSON_OUT 24h");
                return 0;
            }
        }

        /// <summary>
        /// ✅ Méthodes génériques pour compatibilité avec le code existant
        /// </summary>
        public async Task LogJsonSentAsync(string itemId, string jsonPayload, string endpoint, string responseContent = null, int httpCode = 0, string importId = null, string status = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    INSERT INTO JSON_OUT 
                    (JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_IMPORT_ID)
                    VALUES 
                    (GETDATE(), @Destination, @Client, @JsonPayload, @TransactionType, GETDATE(), @Environment, @ImportId)";

                using var command = new SqlCommand(sql, connection);

                command.Parameters.AddWithValue("@Destination", TruncateString(endpoint, MAX_JSON_DEST_LENGTH));
                command.Parameters.AddWithValue("@Client", TruncateString("BR", MAX_JSON_CCLI_LENGTH));
                command.Parameters.AddWithValue("@JsonPayload", jsonPayload ?? "");
                command.Parameters.AddWithValue("@TransactionType", 1);
                command.Parameters.AddWithValue("@Environment", TruncateString(status ?? $"SPEED_{itemId}", MAX_JSON_TREN_LENGTH));
                command.Parameters.AddWithValue("@ImportId", string.IsNullOrEmpty(importId) ? DBNull.Value : TruncateString(importId, MAX_JSON_IMPORT_ID_LENGTH));

                await command.ExecuteNonQueryAsync();

                _logger.LogDebug($"📤 JSON enregistré dans JSON_OUT pour {itemId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de l'enregistrement JSON_OUT pour {itemId}");
            }
        }

        public async Task LogSuccessAsync(string itemId, string jsonPayload)
        {
            await LogJsonSentAsync(itemId, jsonPayload, "CONFIRM_OK", "SUCCESS", 200);
        }

        public async Task LogErrorAsync(string itemId, string jsonPayload, string errorMessage, int httpCode = 500)
        {
            var truncatedPayload = jsonPayload?.Length > 4000 ? jsonPayload.Substring(0, 4000) : jsonPayload;
            var truncatedError = errorMessage?.Length > 1000 ? errorMessage.Substring(0, 1000) : errorMessage;
            await LogJsonSentAsync(itemId, truncatedPayload, "CONFIRM_ERR", truncatedError, httpCode);
        }

        /// <summary>
        /// ✅ Récupère les statistiques des journaux de réception depuis JSON_OUT
        /// </summary>
        public async Task<Dictionary<string, int>> GetItemArrivalJournalStatsAsync()
        {
            var stats = new Dictionary<string, int>
            {
                ["TotalHeaders"] = 0,
                ["TotalLines"] = 0,
                ["TotalConfirmations"] = 0,
                ["Last24Hours"] = 0
            };

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string statsSql = @"
                SELECT 
                    JSON_DEST,
                    COUNT(*) as COUNT
                FROM JSON_OUT 
                WHERE JSON_DEST IN ('ItemArrivalHeaders', 'ItemArrivalLines', 'ItemArrivalConfirmation')
                GROUP BY JSON_DEST";

                using var statsCommand = new SqlCommand(statsSql, connection);
                using var statsReader = await statsCommand.ExecuteReaderAsync();

                while (await statsReader.ReadAsync())
                {
                    var dest = statsReader.GetString("JSON_DEST");
                    var count = statsReader.GetInt32("COUNT");

                    switch (dest)
                    {
                        case "ItemArrivalHeaders":
                            stats["TotalHeaders"] = count;
                            break;
                        case "ItemArrivalLines":
                            stats["TotalLines"] = count;
                            break;
                        case "ItemArrivalConfirmation":
                            stats["TotalConfirmations"] = count;
                            break;
                    }
                }

                statsReader.Close();

                const string last24hSql = @"
                SELECT COUNT(*) 
                FROM JSON_OUT 
                WHERE JSON_DEST LIKE 'ItemArrival%'
                  AND JSON_CRDA >= @Since24h";

                using var last24hCommand = new SqlCommand(last24hSql, connection);
                last24hCommand.Parameters.AddWithValue("@Since24h", DateTime.Now.AddDays(-1));
                stats["Last24Hours"] = (int)await last24hCommand.ExecuteScalarAsync();

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération stats ItemArrivalJournal");
                return stats;
            }
        }

        /// <summary>
        /// ✅ Nettoie les anciens enregistrements (maintenance)
        /// </summary>
        public async Task<int> CleanupOldRecordsAsync(int daysToKeep = 30)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    DELETE FROM JSON_OUT 
                    WHERE JSON_CRDA < DATEADD(day, -@DaysToKeep, GETDATE())
                    AND JSON_DEST NOT IN ('BL_EXPORT', 'BL_ERROR')";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@DaysToKeep", daysToKeep);

                var deletedCount = await command.ExecuteNonQueryAsync();

                if (deletedCount > 0)
                {
                    _logger.LogInformation($"🧹 {deletedCount} anciens enregistrements JSON_OUT supprimés (plus de {daysToKeep} jours)");
                }

                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du nettoyage JSON_OUT");
                return 0;
            }
        }

        /// <summary>
        /// ✅ Récupère les BL en échec pour retry
        /// </summary>
        public async Task<List<FailedBLExport>> GetFailedBLExportsAsync()
        {
            var failedBLs = new List<FailedBLExport>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        JSON_IMPORT_ID,
                        JSON_TREN as Status,
                        JSON_DATA as ErrorMessage,
                        JSON_CRDA as FailedDate,
                        -- Extraire le BL number depuis l'ImportId (format {BL}_{timestamp})
                        CASE 
                            WHEN CHARINDEX('_', JSON_IMPORT_ID) > 0 
                            THEN LEFT(JSON_IMPORT_ID, CHARINDEX('_', JSON_IMPORT_ID) - 1)
                            ELSE JSON_IMPORT_ID
                        END as BLNumber
                    FROM JSON_OUT 
                    WHERE JSON_DEST IN ('BL_EXPORT', 'BL_ERROR')
                    AND JSON_TREN IN ('BL_PENDING_RETRY', 'BL_ERROR')
                    AND JSON_IMPORT_ID IS NOT NULL
                    AND JSON_IMPORT_ID != ''
                    ORDER BY JSON_CRDA DESC";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var failedBL = new FailedBLExport
                    {
                        ImportId = reader.GetString("JSON_IMPORT_ID"),
                        BLNumber = reader.IsDBNull("BLNumber") ? "" : reader.GetString("BLNumber"),
                        Status = reader.GetString("Status"),
                        ErrorMessage = reader.GetString("ErrorMessage"),
                        FailedDate = reader.GetDateTime("FailedDate"),
                        RetryCount = 0 // TODO: Calculer le nombre de retry si nécessaire
                    };

                    if (!string.IsNullOrEmpty(failedBL.BLNumber))
                    {
                        failedBLs.Add(failedBL);
                    }
                }

                _logger.LogInformation($"🔍 {failedBLs.Count} BL en échec trouvés pour retry");
                return failedBLs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération BL en échec");
                return failedBLs;
            }
        }

        /// <summary>
        /// ✅ Nettoie les anciens enregistrements BLExport spécifiquement
        /// </summary>
        public async Task<int> CleanupOldBLExportRecordsAsync(int daysToKeep = 30)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    DELETE FROM JSON_OUT 
                    WHERE JSON_DEST IN ('BL_EXPORT', 'BL_ERROR')
                    AND JSON_CRDA < DATEADD(day, -@DaysToKeep, GETDATE())";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@DaysToKeep", daysToKeep);

                var deletedCount = await command.ExecuteNonQueryAsync();

                if (deletedCount > 0)
                {
                    _logger.LogInformation($"🧹 {deletedCount} anciens enregistrements BLExport supprimés (plus de {daysToKeep} jours)");
                }

                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du nettoyage BLExport");
                return 0;
            }
        }
    }
}