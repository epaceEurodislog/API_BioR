// Fichier: Services/JsonOutService.cs
// Service SIMPLE pour stocker les envois JSON dans JSON_OUT
// VERSION AVEC TRONCATURE pour éviter les erreurs de taille

using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service SIMPLE pour enregistrer les envois JSON dans JSON_OUT
    /// Structure réelle: JSON_KEYU, JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN
    /// AVEC GESTION DE LA TRONCATURE pour éviter les erreurs de taille
    /// </summary>
    public class JsonOutService
    {
        private readonly string _connectionString;
        private readonly ILogger<JsonOutService> _logger;

        // 🔧 Tailles max probables des colonnes (à ajuster selon votre schéma)
        private const int MAX_JSON_DEST_LENGTH = 50;  // Souvent nvarchar(50)
        private const int MAX_JSON_CCLI_LENGTH = 10;  // Souvent nvarchar(10)
        private const int MAX_JSON_TREN_LENGTH = 50;  // Souvent nvarchar(50)

        public JsonOutService(IConfiguration configuration, ILogger<JsonOutService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException("ConnectionString manquante");
            _logger = logger;
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

        /// <summary>
        /// Enregistre un envoi JSON dans la table JSON_OUT
        /// AVEC TRONCATURE pour éviter les erreurs de taille
        /// </summary>
        /// <param name="itemId">ID de l'article</param>
        /// <param name="jsonPayload">JSON envoyé à l'API</param>
        /// <param name="endpoint">URL de destination</param>
        /// <param name="responseContent">Réponse de l'API (optionnel)</param>
        /// <param name="httpCode">Code HTTP de retour (optionnel)</param>
        public async Task LogJsonSentAsync(string itemId, string jsonPayload, string endpoint, string responseContent = null, int httpCode = 0)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // ✅ REQUÊTE AVEC TRONCATURE
                const string sql = @"
                    INSERT INTO JSON_OUT 
                    (JSON_CRDA, JSON_DEST, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN)
                    VALUES 
                    (GETDATE(), @Destination, @Client, @JsonPayload, @TransactionType, GETDATE(), @Environment)";

                using var command = new SqlCommand(sql, connection);

                // ✅ Paramètres avec troncature automatique
                command.Parameters.AddWithValue("@Destination", ShortenEndpoint(endpoint));
                command.Parameters.AddWithValue("@Client", TruncateString("BR", MAX_JSON_CCLI_LENGTH));
                command.Parameters.AddWithValue("@JsonPayload", jsonPayload ?? "");
                command.Parameters.AddWithValue("@TransactionType", 1); // 1 = envoi sortant
                command.Parameters.AddWithValue("@Environment", TruncateString($"SPEED_{itemId}", MAX_JSON_TREN_LENGTH));

                await command.ExecuteNonQueryAsync();

                _logger.LogDebug($"📤 JSON enregistré dans JSON_OUT pour l'article {itemId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de l'enregistrement JSON_OUT pour {itemId}");

                // 🔍 Log détaillé pour diagnostiquer les problèmes de taille
                _logger.LogDebug($"🔍 Détails tentative: ItemId={itemId}, Endpoint={ShortenEndpoint(endpoint)}, PayloadLength={jsonPayload?.Length ?? 0}");

                // Ne pas lancer l'exception pour ne pas bloquer le processus principal
            }
        }

        /// <summary>
        /// Version simplifiée pour confirmation réussie
        /// </summary>
        public async Task LogSuccessAsync(string itemId, string jsonPayload)
        {
            await LogJsonSentAsync(itemId, jsonPayload, "CONFIRM_OK", "SUCCESS", 200);
        }

        /// <summary>
        /// Version simplifiée pour confirmation échouée
        /// </summary>
        public async Task LogErrorAsync(string itemId, string jsonPayload, string errorMessage, int httpCode = 500)
        {
            // Tronquer le message d'erreur aussi pour éviter les problèmes dans JSON_DATA
            var truncatedPayload = jsonPayload?.Length > 4000 ? jsonPayload.Substring(0, 4000) : jsonPayload;
            var truncatedError = errorMessage?.Length > 1000 ? errorMessage.Substring(0, 1000) : errorMessage;

            await LogJsonSentAsync(itemId, truncatedPayload, "CONFIRM_ERR", truncatedError, httpCode);
        }

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
                    AND COLUMN_NAME IN ('JSON_DEST', 'JSON_CCLI', 'JSON_DATA', 'JSON_TREN')
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
        /// Nettoie les anciens enregistrements (maintenance)
        /// </summary>
        public async Task<int> CleanupOldRecordsAsync(int daysToKeep = 30)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    DELETE FROM JSON_OUT 
                    WHERE JSON_CRDA < DATEADD(day, -@DaysToKeep, GETDATE())";

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
    }
}