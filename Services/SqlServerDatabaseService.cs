// Fichier: Services/SqlServerDatabaseService.cs
// Service de gestion de la base de données SQL Server pour la table JSON_IN
// VERSION COMPLÈTE avec optimisation des confirmations

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    public class SqlServerDatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlServerDatabaseService> _logger;

        public SqlServerDatabaseService(IConfiguration configuration, ILogger<SqlServerDatabaseService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException("ConnectionString manquante");
            _logger = logger;
        }

        /// <summary>
        /// Initialise la base de données et vérifie seulement la connexion
        /// </summary>
        public async Task<bool> InitializeDatabaseAsync()
        {
            try
            {
                _logger.LogInformation("🔄 Vérification de la connexion SQL Server...");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("✅ Connexion SQL Server établie");

                // Juste vérifier que la table existe
                if (await TableExistsAsync(connection, "JSON_IN"))
                {
                    _logger.LogInformation("✅ Table JSON_IN trouvée");
                }
                else
                {
                    _logger.LogError("❌ Table JSON_IN non trouvée dans la base Middleware");
                    return false;
                }

                _logger.LogInformation("✅ Base de données vérifiée avec succès");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la vérification de la base de données");
                return false;
            }
        }

        /// <summary>
        /// Vérifie et ajoute la colonne JSON_SENT si elle n'existe pas
        /// </summary>
        public async Task<bool> EnsureConfirmationColumnExistsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Vérifier si la colonne existe déjà
                const string checkColumnSql = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'JSON_IN' 
                    AND COLUMN_NAME = 'JSON_SENT'";

                using var checkCommand = new SqlCommand(checkColumnSql, connection);
                var columnExists = (int)await checkCommand.ExecuteScalarAsync() > 0;

                if (!columnExists)
                {
                    _logger.LogInformation("🔧 Ajout de la colonne JSON_SENT...");

                    const string addColumnSql = @"
                        ALTER TABLE JSON_IN 
                        ADD JSON_SENT BIT DEFAULT 0 NOT NULL";

                    using var addCommand = new SqlCommand(addColumnSql, connection);
                    await addCommand.ExecuteNonQueryAsync();

                    // Créer un index pour optimiser les performances
                    const string createIndexSql = @"
                        CREATE NONCLUSTERED INDEX IX_JSON_IN_JSON_SENT 
                        ON JSON_IN (JSON_SENT, JSON_FROM, JSON_STAT)";

                    using var indexCommand = new SqlCommand(createIndexSql, connection);
                    await indexCommand.ExecuteNonQueryAsync();

                    _logger.LogInformation("✅ Colonne JSON_SENT ajoutée avec index");
                }
                else
                {
                    _logger.LogInformation("✅ Colonne JSON_SENT déjà présente");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la création de la colonne JSON_SENT");
                return false;
            }
        }

        /// <summary>
        /// Vérifie si une table existe
        /// </summary>
        private async Task<bool> TableExistsAsync(SqlConnection connection, string tableName)
        {
            const string sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName";
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TableName", tableName);
            var count = (int)await command.ExecuteScalarAsync();
            return count > 0;
        }

        /// <summary>
        /// Insère ou met à jour un enregistrement dans JSON_IN
        /// </summary>
        public async Task<bool> InsertOrUpdateJsonDataAsync(string businessKey, string jsonData, string endpoint, string status = "ACTIVE")
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var contentHash = ComputeHash(jsonData);

                // Vérifier si cet enregistrement existe déjà
                var existingRecord = await GetExistingRecordAsync(connection, businessKey, endpoint);

                if (existingRecord.HasValue)
                {
                    // Si le hash a changé, mettre à jour
                    if (existingRecord.Value.Hash != contentHash)
                    {
                        _logger.LogDebug($"🔄 Mise à jour de {businessKey} (contenu modifié)");
                        return await UpdateExistingRecordAsync(connection, existingRecord.Value.Id, jsonData, contentHash, status);
                    }
                    else
                    {
                        _logger.LogDebug($"➖ {businessKey} inchangé");
                        return true; // Données identiques, rien à faire
                    }
                }
                else
                {
                    // Nouvel enregistrement
                    _logger.LogDebug($"📥 Nouveau: {businessKey}");
                    return await InsertNewRecordAsync(connection, businessKey, jsonData, endpoint, contentHash, status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de l'insertion/mise à jour de {businessKey}");
                return false;
            }
        }

        /// <summary>
        /// Recherche un enregistrement existant par clé métier + endpoint
        /// </summary>
        private async Task<(int Id, string Hash)?> GetExistingRecordAsync(SqlConnection connection, string businessKey, string endpoint)
        {
            const string sql = @"
                SELECT JSON_KEYU, ISNULL(JSON_HASH, '') as JSON_HASH
                FROM JSON_IN 
                WHERE JSON_BKEY = @BusinessKey AND JSON_FROM = @Endpoint";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@BusinessKey", businessKey);
            command.Parameters.AddWithValue("@Endpoint", endpoint);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return (reader.GetInt32("JSON_KEYU"), reader.GetString("JSON_HASH"));
            }
            return null;
        }

        /// <summary>
        /// Met à jour un enregistrement existant
        /// </summary>
        private async Task<bool> UpdateExistingRecordAsync(SqlConnection connection, int id, string jsonData, string contentHash, string status)
        {
            const string sql = @"
                UPDATE JSON_IN 
                SET 
                    JSON_DATA = @JsonData, 
                    JSON_HASH = @Hash,
                    JSON_STAT = @Status
                WHERE JSON_KEYU = @Id";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@JsonData", jsonData);
            command.Parameters.AddWithValue("@Hash", contentHash);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@Id", id);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }

        /// <summary>
        /// Insère un nouvel enregistrement (JSON_KEYU auto-incrémenté)
        /// </summary>
        private async Task<bool> InsertNewRecordAsync(SqlConnection connection, string businessKey, string jsonData, string endpoint, string contentHash, string status)
        {
            const string sql = @"
                INSERT INTO JSON_IN 
                (JSON_CRDA, JSON_FROM, JSON_CCLI, JSON_DATA, JSON_TRTP, JSON_TRDA, JSON_TREN, JSON_BKEY, JSON_HASH, JSON_STAT)
                VALUES 
                (GETDATE(), @Endpoint, 'BR', @JsonData, 0, GETDATE(), 'SPEED', @BusinessKey, @Hash, @Status)";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@BusinessKey", businessKey);
            command.Parameters.AddWithValue("@Endpoint", endpoint);
            command.Parameters.AddWithValue("@JsonData", jsonData);
            command.Parameters.AddWithValue("@Hash", contentHash);
            command.Parameters.AddWithValue("@Status", status);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }

        /// <summary>
        /// Marque les enregistrements comme supprimés s'ils ne sont plus dans l'API
        /// </summary>
        public async Task<int> MarkMissingRecordsAsDeletedAsync(string endpoint, List<string> currentBusinessKeys)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                if (currentBusinessKeys.Count == 0)
                {
                    _logger.LogWarning($"Aucune clé fournie pour {endpoint}, pas de suppression effectuée");
                    return 0;
                }

                // Construire la requête avec des paramètres pour éviter l'injection SQL
                var parameterNames = currentBusinessKeys.Select((key, index) => $"@key{index}").ToArray();
                var parameterPlaceholders = string.Join(",", parameterNames);

                var sql = $@"
                    UPDATE JSON_IN 
                    SET JSON_STAT = 'DELETED'
                    WHERE JSON_FROM = @Endpoint 
                    AND JSON_STAT = 'ACTIVE'
                    AND JSON_BKEY NOT IN ({parameterPlaceholders})";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Endpoint", endpoint);

                // Ajouter chaque clé comme paramètre
                for (int i = 0; i < currentBusinessKeys.Count; i++)
                {
                    command.Parameters.AddWithValue($"@key{i}", currentBusinessKeys[i]);
                }

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    _logger.LogInformation($"🗑️ {rowsAffected} enregistrements marqués comme supprimés pour {endpoint}");
                }

                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du marquage des suppressions pour {endpoint}");
                return 0;
            }
        }

        /// <summary>
        /// Récupère UNIQUEMENT les articles NON confirmés (optimisation performance)
        /// </summary>
        public async Task<List<string>> GetUnconfirmedArticleIdsOptimizedAsync()
        {
            var itemIds = new List<string>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT DISTINCT JSON_VALUE(JSON_DATA, '$.ItemId') as ItemId
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    AND ISNULL(JSON_SENT, 0) = 0
                    AND JSON_VALUE(JSON_DATA, '$.ItemId') IS NOT NULL
                    ORDER BY JSON_VALUE(JSON_DATA, '$.ItemId')";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var itemId = reader.GetString("ItemId");
                    if (!string.IsNullOrEmpty(itemId))
                    {
                        itemIds.Add(itemId);
                    }
                }

                _logger.LogInformation($"📊 {itemIds.Count} articles NON confirmés trouvés (optimisé)");
                return itemIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des articles non confirmés");
                return itemIds;
            }
        }

        /// <summary>
        /// Marque plusieurs articles comme confirmés en une seule requête (ULTRA OPTIMISÉ)
        /// </summary>
        public async Task<int> MarkMultipleArticlesAsConfirmedAsync(List<string> itemIds)
        {
            if (itemIds.Count == 0) return 0;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Construire la requête avec des paramètres pour éviter l'injection SQL
                var parameterNames = itemIds.Select((id, index) => $"@itemId{index}").ToArray();
                var parameterPlaceholders = string.Join(",", parameterNames);

                var sql = $@"
                    UPDATE JSON_IN 
                    SET JSON_SENT = 1
                    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
                    AND JSON_VALUE(JSON_DATA, '$.ItemId') IN ({parameterPlaceholders})
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    AND ISNULL(JSON_SENT, 0) = 0";

                using var command = new SqlCommand(sql, connection);

                // Ajouter chaque ItemId comme paramètre
                for (int i = 0; i < itemIds.Count; i++)
                {
                    command.Parameters.AddWithValue($"@itemId{i}", itemIds[i]);
                }

                var rowsAffected = await command.ExecuteNonQueryAsync();

                _logger.LogInformation($"✅ {rowsAffected}/{itemIds.Count} articles marqués comme confirmés");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du marquage multiple des confirmations");
                return 0;
            }
        }

        /// <summary>
        /// Statistiques des confirmations pour monitoring
        /// </summary>
        public async Task<ConfirmationStatistics> GetConfirmationStatisticsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        COUNT(*) as TotalArticles,
                        COUNT(CASE WHEN ISNULL(JSON_SENT, 0) = 1 THEN 1 END) as ConfirmedArticles,
                        COUNT(CASE WHEN ISNULL(JSON_SENT, 0) = 0 AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE' THEN 1 END) as PendingConfirmations,
                        COUNT(CASE WHEN ISNULL(JSON_SENT, 0) = 1 AND JSON_CRDA >= DATEADD(day, -1, GETDATE()) THEN 1 END) as ConfirmedLast24h
                    FROM JSON_IN
                    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new ConfirmationStatistics
                    {
                        TotalArticles = reader.GetInt32("TotalArticles"),
                        ConfirmedArticles = reader.GetInt32("ConfirmedArticles"),
                        PendingConfirmations = reader.GetInt32("PendingConfirmations"),
                        ConfirmedLast24h = reader.GetInt32("ConfirmedLast24h")
                    };
                }

                return new ConfirmationStatistics();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des statistiques de confirmation");
                return new ConfirmationStatistics();
            }
        }

        /// <summary>
        /// Calcule un hash MD5 du contenu JSON
        /// </summary>
        private static string ComputeHash(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Obtient des statistiques sur la table JSON_IN
        /// </summary>
        public async Task<JsonInStatistics> GetStatisticsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        COUNT(*) as Total,
                        COUNT(CASE WHEN ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE' THEN 1 END) as Active,
                        COUNT(CASE WHEN JSON_STAT = 'DELETED' THEN 1 END) as Deleted,
                        COUNT(CASE WHEN JSON_CRDA >= DATEADD(day, -1, GETDATE()) THEN 1 END) as UpdatedLast24h
                    FROM JSON_IN";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new JsonInStatistics
                    {
                        TotalRecords = reader.GetInt32("Total"),
                        ActiveRecords = reader.GetInt32("Active"),
                        DeletedRecords = reader.GetInt32("Deleted"),
                        UpdatedLast24h = reader.GetInt32("UpdatedLast24h")
                    };
                }

                return new JsonInStatistics();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des statistiques");
                return new JsonInStatistics();
            }
        }
    }
}