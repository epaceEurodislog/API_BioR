// Fichier: Services/JsonOutService.cs
// Service complet pour stocker les envois JSON dans JSON_OUT avec support BLExport
// VERSION COMPLÈTE avec ImportId et fonctionnalités BLExport

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
    public class JsonOutService
    {
        private readonly string _connectionString;
        private readonly ILogger<JsonOutService> _logger;

        // 🔧 Tailles max des colonnes pour éviter les erreurs de troncature
        private const int MAX_JSON_DEST_LENGTH = 50;
        private const int MAX_JSON_CCLI_LENGTH = 10;
        private const int MAX_JSON_TREN_LENGTH = 50;
        private const int MAX_JSON_IMPORT_ID_LENGTH = 100;  // 🆕 Nouveau champ ImportId

        public JsonOutService(IConfiguration configuration, ILogger<JsonOutService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException("ConnectionString manquante");
            _logger = logger;
        }

        /// <summary>
        /// 🆕 Initialise la table JSON_OUT avec la colonne ImportId si nécessaire
        /// </summary>
        public async Task<bool> EnsureImportIdColumnExistsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // ✅ Vérification si la colonne JSON_IMPORT_ID existe
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

                    // ✅ Créer un index pour optimiser les recherches BLExport
                    try
                    {
                        const string createIndexSql = @"
                            CREATE NONCLUSTERED INDEX IX_JSON_OUT_IMPORT_ID 
                            ON JSON_OUT (JSON_IMPORT_ID, JSON_DEST, JSON_TREN)";

                        using var indexCommand = new SqlCommand(createIndexSql, connection);
                        await indexCommand.ExecuteNonQueryAsync();
                        _logger.LogInformation("✅ Index IX_JSON_OUT_IMPORT_ID créé");
                    }
                    catch (Exception indexEx)
                    {
                        _logger.LogWarning(indexEx, "⚠️ Impossible de créer l'index ImportId (peut-être déjà existant)");
                    }
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
        /// Raccourcit une URL pour la stocker dans JSON_DEST
        /// </summary>
        private string ShortenEndpoint(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
                return "API_DYNAMICS";

            // Si c'est une URL complète, extraire juste la partie utile
            if (endpoint.StartsWith("http"))
            {
                try
                {
                    var uri = new Uri(endpoint);
                    // Garder juste le host et le premier segment
                    var shortName = uri.Host.Replace("operations.eu.dynamics.com", "DYN")
                                          .Replace("sandbox.", "")
                                          .Replace("-uat", "")
                                          .Replace("br-", "BR_");

                    return TruncateString(shortName, MAX_JSON_DEST_LENGTH);
                }
                catch
                {
                    // Si parsing échoue, utiliser un nom générique
                    return "DYN_API";
                }
            }

            // Pour les autres cas, utiliser tel quel mais tronqué
            return TruncateString(endpoint, MAX_JSON_DEST_LENGTH);
        }

        // ==========================================
        // MÉTHODES GÉNÉRIQUES JSON_OUT
        // ==========================================

        /// <summary>
        /// Enregistre un envoi JSON dans la table JSON_OUT avec support ImportId
        /// VERSION ÉTENDUE pour BLExport et fonctionnalités existantes
        /// </summary>
        /// <param name="itemId">ID de l'article ou BL</param>
        /// <param name="jsonPayload">JSON envoyé à l'API</param>
        /// <param name="endpoint">URL de destination</param>
        /// <param name="responseContent">Réponse de l'API (optionnel)</param>
        /// <param name="httpCode">Code HTTP de retour (optionnel)</param>
        /// <param name="importId">ImportId pour BLExport (optionnel)</param>
        /// <param name="status">Statut personnalisé (optionnel)</param>
        public async Task LogJsonSentAsync(string itemId, string jsonPayload, string endpoint, string responseContent = null, int httpCode = 0, string importId = null, string status = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // ✅ REQUÊTE AVEC SUPPORT ImportId
                const string sql = @"
                    INSERT INTO JSON_OUT 
                    (JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_IMPORT_ID)
                    VALUES 
                    (GETDATE(), @Destination, @Client, @JsonPayload, @TransactionType, GETDATE(), @Environment, @ImportId)";

                using var command = new SqlCommand(sql, connection);

                // ✅ Paramètres avec troncature automatique
                command.Parameters.AddWithValue("@Destination", ShortenEndpoint(endpoint));
                command.Parameters.AddWithValue("@Client", TruncateString("BR", MAX_JSON_CCLI_LENGTH));
                command.Parameters.AddWithValue("@JsonPayload", jsonPayload ?? "");
                command.Parameters.AddWithValue("@TransactionType", 1); // 1 = envoi sortant
                command.Parameters.AddWithValue("@Environment", TruncateString(status ?? $"SPEED_{itemId}", MAX_JSON_TREN_LENGTH));
                command.Parameters.AddWithValue("@ImportId", string.IsNullOrEmpty(importId) ? DBNull.Value : TruncateString(importId, MAX_JSON_IMPORT_ID_LENGTH));

                await command.ExecuteNonQueryAsync();

                _logger.LogDebug($"📤 JSON enregistré dans JSON_OUT pour {itemId}" +
                    (string.IsNullOrEmpty(importId) ? "" : $" (ImportId: {importId})"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de l'enregistrement JSON_OUT pour {itemId}");

                // 🔍 Log détaillé pour diagnostiquer les problèmes de taille
                _logger.LogDebug($"🔍 Détails tentative: ItemId={itemId}, Endpoint={ShortenEndpoint(endpoint)}, PayloadLength={jsonPayload?.Length ?? 0}, ImportId={importId}");

                // Ne pas lancer l'exception pour ne pas bloquer le processus principal
            }
        }

        /// <summary>
        /// Version simplifiée pour confirmation réussie (compatibilité existante)
        /// </summary>
        public async Task LogSuccessAsync(string itemId, string jsonPayload)
        {
            await LogJsonSentAsync(itemId, jsonPayload, "CONFIRM_OK", "SUCCESS", 200);
        }

        /// <summary>
        /// Version simplifiée pour confirmation échouée (compatibilité existante)
        /// </summary>
        public async Task LogErrorAsync(string itemId, string jsonPayload, string errorMessage, int httpCode = 500)
        {
            // Tronquer le message d'erreur aussi pour éviter les problèmes dans JSON_DATA
            var truncatedPayload = jsonPayload?.Length > 4000 ? jsonPayload.Substring(0, 4000) : jsonPayload;
            var truncatedError = errorMessage?.Length > 1000 ? errorMessage.Substring(0, 1000) : errorMessage;

            await LogJsonSentAsync(itemId, truncatedPayload, "CONFIRM_ERR", truncatedError, httpCode);
        }

        // ==========================================
        // 🆕 NOUVELLES MÉTHODES BLEXPORT
        // ==========================================

        /// <summary>
        /// 🆕 Enregistre un envoi BLExport avec ImportId et statut spécifique
        /// </summary>
        public async Task LogBLExportAsync(string blNumber, string importId, string jsonPayload, string status, string errorMessage = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    INSERT INTO JSON_OUT 
                    (JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_IMPORT_ID)
                    VALUES 
                    (GETDATE(), @Destination, @Client, @JsonPayload, @TransactionType, GETDATE(), @Status, @ImportId)";

                using var command = new SqlCommand(sql, connection);

                var destinationName = string.IsNullOrEmpty(errorMessage) ? "BL_EXPORT" : "BL_ERROR";
                var payloadToStore = string.IsNullOrEmpty(errorMessage) ? jsonPayload : errorMessage;

                command.Parameters.AddWithValue("@Destination", TruncateString(destinationName, MAX_JSON_DEST_LENGTH));
                command.Parameters.AddWithValue("@Client", TruncateString("BR", MAX_JSON_CCLI_LENGTH));
                command.Parameters.AddWithValue("@JsonPayload", payloadToStore ?? "");
                command.Parameters.AddWithValue("@TransactionType", 1);
                command.Parameters.AddWithValue("@Status", TruncateString(status, MAX_JSON_TREN_LENGTH));
                command.Parameters.AddWithValue("@ImportId", TruncateString(importId, MAX_JSON_IMPORT_ID_LENGTH));

                await command.ExecuteNonQueryAsync();

                _logger.LogDebug($"📤 BLExport enregistré: BL {blNumber}, ImportId {importId}, Status {status}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur enregistrement BLExport: BL {blNumber}, ImportId {importId}");
            }
        }

        /// <summary>
        /// 🆕 Vérifie si un BL a déjà été traité (éviter les doublons)
        /// Recherche intelligente par numéro BL dans plusieurs champs
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
                         OR JSON_DATA LIKE '%' + @BLNumber + '%'
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
                return false; // En cas d'erreur, considérer comme non traité pour éviter de bloquer
            }
        }

        /// <summary>
        /// 🆕 Récupère les BL en échec pour retry
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
        /// 🆕 Obtient des statistiques BLExport
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
                        COUNT(CASE WHEN JSON_TREN = 'BL_CONFIRMED' THEN 1 END) as BLsConfirmed,
                        COUNT(CASE WHEN JSON_TREN = 'BL_SENT' THEN 1 END) as BLsSent,
                        COUNT(CASE WHEN JSON_TREN IN ('BL_ERROR', 'BL_PENDING_RETRY') THEN 1 END) as BLsWithErrors,
                        COUNT(CASE WHEN JSON_CRDA >= DATEADD(hour, -24, GETDATE()) THEN 1 END) as ProcessedLast24h,
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
                    stats.BLsProcessedSuccessfully = reader.GetInt32("BLsConfirmed");
                    stats.BLsWithErrors = reader.GetInt32("BLsWithErrors");
                    stats.TotalPayloadsSent = reader.GetInt32("TotalPayloads");
                    stats.ConfirmationsSent = reader.GetInt32("BLsConfirmed");

                    // Calculer les BL à traiter (estimation)
                    stats.BLsToProcess = stats.TotalBLsFound;
                    stats.BLsAlreadyProcessed = 0; // Difficile à calculer précisément avec les données actuelles
                }

                _logger.LogDebug($"📊 Statistiques BLExport: {stats.GetSummary()}");
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération statistiques BLExport");
                return stats;
            }
        }

        /// <summary>
        /// 🆕 Nettoie les anciens enregistrements BLExport
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

        // ==========================================
        // MÉTHODES STATISTIQUES ET UTILITAIRES
        // ==========================================

        /// <summary>
        /// Test de la taille des colonnes (pour diagnostic)
        /// </summary>
        public async Task<string> GetColumnSizesAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        COLUMN_NAME, 
                        DATA_TYPE, 
                        ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) as MAX_LENGTH
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'JSON_OUT' 
                    AND COLUMN_NAME IN ('JSON_DEST', 'JSON_CCLI', 'JSON_DATA', 'JSON_TREN', 'JSON_IMPORT_ID')
                    ORDER BY COLUMN_NAME";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                var result = "Structure JSON_OUT:\n";
                while (await reader.ReadAsync())
                {
                    var columnName = reader["COLUMN_NAME"].ToString();
                    var dataType = reader["DATA_TYPE"].ToString();
                    var maxLengthObj = reader["MAX_LENGTH"];
                    var maxLength = maxLengthObj == DBNull.Value ? 0 : Convert.ToInt32(maxLengthObj);

                    result += $"  {columnName}: {dataType}({maxLength})\n";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la lecture de la structure");
                return "Erreur structure";
            }
        }

        /// <summary>
        /// Statistiques simples de JSON_OUT
        /// </summary>
        public async Task<int> GetTotalJsonOutRecordsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = "SELECT COUNT(*) FROM JSON_OUT";
                using var command = new SqlCommand(sql, connection);

                var count = (int)await command.ExecuteScalarAsync();
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du comptage des enregistrements JSON_OUT");
                return 0;
            }
        }

        /// <summary>
        /// Compte les envois des dernières 24h
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
                _logger.LogError(ex, "Erreur lors du comptage JSON_OUT 24h");
                return 0;
            }
        }

        /// <summary>
        /// Nettoie les anciens enregistrements (maintenance générale)
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
                    AND JSON_DEST NOT IN ('BL_EXPORT', 'BL_ERROR')"; // Exclure BLExport du nettoyage général

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
                _logger.LogError(ex, "Erreur lors du nettoyage JSON_OUT");
                return 0;
            }
        }

        /// <summary>
        /// Obtient un rapport complet de JSON_OUT
        /// </summary>
        public async Task<string> GetCompleteReportAsync()
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
                        COUNT(CASE WHEN JSON_IMPORT_ID IS NOT NULL THEN 1 END) as WithImportId,
                        MIN(JSON_CRDA) as FirstRecord,
                        MAX(JSON_CRDA) as LastRecord
                    FROM JSON_OUT 
                    GROUP BY JSON_DEST
                    ORDER BY Total DESC";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                var report = "📊 === RAPPORT COMPLET JSON_OUT === 📊\n";
                var totalRecords = 0;
                var total24h = 0;

                while (await reader.ReadAsync())
                {
                    var dest = reader.GetString("JSON_DEST");
                    var total = reader.GetInt32("Total");
                    var last24h = reader.GetInt32("Last24h");
                    var withImportId = reader.GetInt32("WithImportId");
                    var firstRecord = reader.GetDateTime("FirstRecord");
                    var lastRecord = reader.GetDateTime("LastRecord");

                    totalRecords += total;
                    total24h += last24h;

                    report += $"\n📋 {dest}:\n";
                    report += $"   Total: {total:N0} enregistrements\n";
                    report += $"   Dernières 24h: {last24h:N0}\n";
                    report += $"   Avec ImportId: {withImportId:N0}\n";
                    report += $"   Période: {firstRecord:dd/MM/yyyy} → {lastRecord:dd/MM/yyyy}\n";
                }

                report += $"\n🎯 TOTAL GLOBAL: {totalRecords:N0} enregistrements ({total24h:N0} dernières 24h)";

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur génération rapport JSON_OUT");
                return "❌ Erreur génération rapport";
            }
        }

        // Extension de JsonOutService.cs pour supporter ItemArrivalJournal
        // À AJOUTER dans le fichier Services/JsonOutService.cs existant

        /// <summary>
        /// 🆕 Enregistre un envoi pour ItemArrivalJournal avec JournalNumber et PackingSlip
        /// </summary>
        public async Task LogItemArrivalJournalAsync(string journalNumber, string packingSlipId,
            string jsonData, string destination, string errorMessage = "")
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            INSERT INTO JSON_OUT (
                JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_IMPORT_ID
            ) VALUES (
                @Destination, @ClientCode, @JsonData, @TransactionType, GETDATE(), @Environment, @ImportId
            )";

                using var command = new SqlCommand(sql, connection);

                // Tronquer les valeurs selon les limites de colonne
                var destination_safe = TruncateString(destination, 50);
                var environment_safe = TruncateString($"JOURNAL_{journalNumber}", 50);
                var importId_safe = TruncateString($"{journalNumber}_{packingSlipId}", 100);

                command.Parameters.AddWithValue("@Destination", destination_safe);
                command.Parameters.AddWithValue("@ClientCode", "BR");
                command.Parameters.AddWithValue("@JsonData", jsonData ?? "");
                command.Parameters.AddWithValue("@TransactionType", 1); // 1 = envoi
                command.Parameters.AddWithValue("@Environment", environment_safe);
                command.Parameters.AddWithValue("@ImportId", importId_safe);

                await command.ExecuteNonQueryAsync();

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogWarning($"⚠️ Journal {journalNumber} - {destination}: {errorMessage}");
                }
                else
                {
                    _logger.LogDebug($"📝 Journal {journalNumber} - {destination} enregistré dans JSON_OUT");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur enregistrement JSON_OUT pour journal {journalNumber}");
            }
        }

        /// <summary>
        /// 🆕 Récupère les statistiques des journaux de réception depuis JSON_OUT
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

                // Statistiques par type de destination
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

                // Statistiques des dernières 24h
                const string last24hSql = @"
            SELECT COUNT(*) 
            FROM JSON_OUT 
            WHERE JSON_DEST LIKE 'ItemArrival%'
              AND JSON_CRDA >= @Since24h";

                using var last24hCommand = new SqlCommand(last24hSql, connection);
                last24hCommand.Parameters.AddWithValue("@Since24h", DateTime.Now.AddDays(-1));
                stats["Last24Hours"] = (int)await last24hCommand.ExecuteScalarAsync();

                _logger.LogDebug($"📊 Stats ItemArrival: Headers={stats["TotalHeaders"]}, Lines={stats["TotalLines"]}, Confirmations={stats["TotalConfirmations"]}, 24h={stats["Last24Hours"]}");
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération stats ItemArrivalJournal");
                return stats;
            }
        }
    }
}