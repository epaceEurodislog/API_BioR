// Fichier: Services/SqlServerDatabaseService.cs
// Service de gestion de la base de données SQL Server pour la table JSON_IN
// VERSION COMPLÈTE CORRIGÉE avec optimisation des confirmations

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
        /// ✅ CORRIGÉ: Vérifie et ajoute la colonne JSON_SENT avec validation stricte
        /// </summary>
        public async Task<bool> EnsureConfirmationColumnExistsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // ✅ AMÉLIORATION: Vérification plus robuste
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

                    _logger.LogInformation("✅ Colonne JSON_SENT ajoutée");

                    // ✅ VÉRIFICATION POST-CRÉATION
                    using var verifyCommand = new SqlCommand(checkColumnSql, connection);
                    var verified = (int)await verifyCommand.ExecuteScalarAsync() > 0;

                    if (!verified)
                    {
                        throw new Exception("Échec de la création de la colonne JSON_SENT");
                    }

                    // Créer un index pour optimiser les performances
                    try
                    {
                        const string createIndexSql = @"
                            CREATE NONCLUSTERED INDEX IX_JSON_IN_JSON_SENT 
                            ON JSON_IN (JSON_SENT, JSON_FROM, JSON_STAT)";

                        using var indexCommand = new SqlCommand(createIndexSql, connection);
                        await indexCommand.ExecuteNonQueryAsync();
                        _logger.LogInformation("✅ Index IX_JSON_IN_JSON_SENT créé");
                    }
                    catch (Exception indexEx)
                    {
                        _logger.LogWarning(indexEx, "⚠️ Impossible de créer l'index (peut-être déjà existant)");
                    }
                }
                else
                {
                    _logger.LogInformation("✅ Colonne JSON_SENT déjà présente");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERREUR CRITIQUE: Impossible de créer/vérifier la colonne JSON_SENT");
                throw; // ✅ ARRÊTER le programme si cette étape échoue
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
        // ✅ CORRIGÉ : Requête qui prend le plus récent OU compte les doublons
        private async Task<(int Id, string Hash)?> GetExistingRecordAsync(SqlConnection connection, string businessKey, string endpoint)
        {
            // Option A : Prendre le plus récent
            const string sql = @"
        SELECT TOP 1 JSON_KEYU, ISNULL(JSON_HASH, '') as JSON_HASH
        FROM JSON_IN 
        WHERE JSON_BKEY = @BusinessKey AND JSON_FROM = @Endpoint
        ORDER BY JSON_CRDA DESC"; // ✅ Le plus récent en premier

            // Option B : Détecter les doublons
            const string sqlCheck = @"
        SELECT COUNT(*) as DoublonCount
        FROM JSON_IN 
        WHERE JSON_BKEY = @BusinessKey AND JSON_FROM = @Endpoint";

            using var checkCommand = new SqlCommand(sqlCheck, connection);
            checkCommand.Parameters.AddWithValue("@BusinessKey", businessKey);
            checkCommand.Parameters.AddWithValue("@Endpoint", endpoint);

            var count = (int)await checkCommand.ExecuteScalarAsync();
            if (count > 1)
            {
                _logger.LogWarning($"⚠️ DOUBLON détecté : {count} occurrences pour {businessKey}");
            }

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
        /// Récupère les IDs des articles depuis une date donnée
        /// </summary>
        public async Task<List<string>> GetArticleIdsFromDateAsync(DateTime fromDate)
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
                    AND JSON_CRDA >= @FromDate
                    AND JSON_VALUE(JSON_DATA, '$.ItemId') IS NOT NULL
                    ORDER BY JSON_VALUE(JSON_DATA, '$.ItemId')";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@FromDate", fromDate);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var itemId = reader.GetString("ItemId");
                    if (!string.IsNullOrEmpty(itemId))
                    {
                        itemIds.Add(itemId);
                    }
                }

                _logger.LogInformation($"📊 {itemIds.Count} articles trouvés depuis le {fromDate:dd/MM/yyyy}");
                return itemIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des articles depuis le {fromDate:dd/MM/yyyy}");
                return itemIds;
            }
        }

        /// <summary>
        /// ✅ CORRIGÉ: Marque plusieurs articles comme confirmés avec méthode robuste
        /// </summary>
        public async Task<int> MarkMultipleArticlesAsConfirmedAsync(List<string> itemIds)
        {
            if (itemIds.Count == 0) return 0;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInformation($"🔄 Début marquage de {itemIds.Count} articles comme confirmés...");

                // ✅ CORRECTION: Traiter par batch de 50 pour éviter les problèmes de paramètres
                var updates = 0;
                var batchSize = 50;

                for (int i = 0; i < itemIds.Count; i += batchSize)
                {
                    var batch = itemIds.Skip(i).Take(batchSize).ToList();
                    var batchUpdates = await MarkBatchAsConfirmedAsync(connection, batch);
                    updates += batchUpdates;

                    _logger.LogDebug($"📊 Batch {(i / batchSize) + 1}: {batchUpdates}/{batch.Count} articles marqués");
                }

                _logger.LogInformation($"✅ {updates}/{itemIds.Count} articles marqués comme confirmés");
                return updates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du marquage multiple des confirmations");
                return 0;
            }
        }

        /// <summary>
        /// ✅ NOUVELLE MÉTHODE: Marque un batch d'articles comme confirmés
        /// </summary>
        private async Task<int> MarkBatchAsConfirmedAsync(SqlConnection connection, List<string> itemIds)
        {
            if (itemIds.Count == 0) return 0;

            try
            {
                // ✅ MÉTHODE 1: Requête simplifiée avec LIKE (plus compatible)
                var updates = 0;

                foreach (var itemId in itemIds)
                {
                    const string sql = @"
                        UPDATE JSON_IN 
                        SET JSON_SENT = 1
                        WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
                        AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                        AND ISNULL(JSON_SENT, 0) = 0
                        AND JSON_DATA LIKE '%""ItemId"":""' + @ItemId + '""%'";

                    using var command = new SqlCommand(sql, connection);
                    command.Parameters.AddWithValue("@ItemId", itemId);

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    updates += rowsAffected;

                    if (rowsAffected > 0)
                    {
                        _logger.LogDebug($"✅ Article {itemId}: marqué comme confirmé");
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ Article {itemId}: aucune ligne mise à jour");
                    }
                }

                return updates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors du marquage du batch de {itemIds.Count} articles");

                // ✅ MÉTHODE 2: Fallback avec JSON_VALUE si LIKE échoue
                return await MarkBatchWithJsonValueAsync(connection, itemIds);
            }
        }

        /// <summary>
        /// ✅ MÉTHODE FALLBACK: Utilise JSON_VALUE comme avant
        /// </summary>
        private async Task<int> MarkBatchWithJsonValueAsync(SqlConnection connection, List<string> itemIds)
        {
            try
            {
                _logger.LogWarning("⚠️ Utilisation de la méthode fallback JSON_VALUE");

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
                _logger.LogInformation($"✅ Fallback JSON_VALUE: {rowsAffected} articles marqués");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur dans la méthode fallback JSON_VALUE");
                return 0;
            }
        }

        /// <summary>
        /// ✅ NOUVELLE MÉTHODE: Test de marquage pour diagnostic
        /// </summary>
        public async Task<bool> TestMarkingSingleArticleAsync(string itemId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInformation($"🧪 Test de marquage pour l'article: {itemId}");

                // Test 1: Vérifier que l'article existe
                const string checkSql = @"
                    SELECT COUNT(*), ISNULL(JSON_SENT, 0) as CurrentStatus
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
                    AND JSON_DATA LIKE '%""ItemId"":""' + @ItemId + '""%'
                    GROUP BY JSON_SENT";

                using var checkCommand = new SqlCommand(checkSql, connection);
                checkCommand.Parameters.AddWithValue("@ItemId", itemId);

                using var reader = await checkCommand.ExecuteReaderAsync();
                var found = false;
                while (await reader.ReadAsync())
                {
                    var count = reader.GetInt32(0);
                    var currentStatus = reader.GetInt32(1);
                    _logger.LogInformation($"📊 Article {itemId}: {count} occurrence(s), JSON_SENT = {currentStatus}");
                    found = true;
                }
                reader.Close();

                if (!found)
                {
                    _logger.LogWarning($"⚠️ Article {itemId} non trouvé en base");
                    return false;
                }

                // Test 2: Essayer le marquage
                const string updateSql = @"
                    UPDATE JSON_IN 
                    SET JSON_SENT = 1
                    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
                    AND ISNULL(JSON_SENT, 0) = 0
                    AND JSON_DATA LIKE '%""ItemId"":""' + @ItemId + '""%'";

                using var updateCommand = new SqlCommand(updateSql, connection);
                updateCommand.Parameters.AddWithValue("@ItemId", itemId);

                var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    _logger.LogInformation($"✅ Test réussi: {rowsAffected} ligne(s) marquée(s) pour {itemId}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"⚠️ Test échoué: Aucune ligne mise à jour pour {itemId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors du test de marquage pour {itemId}");
                return false;
            }
        }

        /// <summary>
        /// ✅ NOUVELLE MÉTHODE: Analyse détaillée des données JSON pour diagnostic
        /// </summary>
        public async Task<List<string>> AnalyzeJsonDataStructureAsync(int sampleSize = 5)
        {
            var analysisResults = new List<string>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT TOP (@SampleSize)
                        JSON_KEYU,
                        JSON_BKEY,
                        JSON_DATA,
                        ISNULL(JSON_SENT, 0) as JSON_SENT,
                        JSON_VALUE(JSON_DATA, '$.ItemId') as ExtractedItemId
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
                    ORDER BY JSON_CRDA DESC";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SampleSize", sampleSize);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var keyU = reader.GetInt32("JSON_KEYU");
                    var bKey = reader.GetString("JSON_BKEY");
                    var jsonData = reader.GetString("JSON_DATA");
                    var jsonSent = reader.GetInt32("JSON_SENT");
                    var extractedItemId = reader.IsDBNull("ExtractedItemId") ? "NULL" : reader.GetString("ExtractedItemId");

                    var analysis = $"ID:{keyU} | BKEY:{bKey} | SENT:{jsonSent} | ItemId:{extractedItemId}";
                    analysisResults.Add(analysis);

                    _logger.LogInformation($"📊 {analysis}");
                    _logger.LogDebug($"📄 JSON: {jsonData.Substring(0, Math.Min(100, jsonData.Length))}...");
                }

                _logger.LogInformation($"✅ Analyse terminée: {analysisResults.Count} échantillons analysés");
                return analysisResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de l'analyse des données JSON");
                return analysisResults;
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


        // ✅ NOUVELLE MÉTHODE 1: Récupérer les détails d'un enregistrement existant
        public async Task<(int Id, string Hash)?> GetExistingRecordDetailsAsync(string businessKey, string endpoint)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

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
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des détails pour {businessKey}");
                return null;
            }
        }

        // ✅ NOUVELLE MÉTHODE 2: Récupérer l'ID JSON_IN par clé métier
        public async Task<int?> GetJsonInIdByBusinessKeyAsync(string businessKey, string endpoint)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT JSON_KEYU
            FROM JSON_IN 
            WHERE JSON_BKEY = @BusinessKey AND JSON_FROM = @Endpoint";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@BusinessKey", businessKey);
                command.Parameters.AddWithValue("@Endpoint", endpoint);

                var result = await command.ExecuteScalarAsync();
                return result != null ? (int)result : (int?)null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération de l'ID pour {businessKey}");
                return null;
            }
        }

        // ✅ NOUVELLE MÉTHODE 3: Récupérer l'ID JSON_IN par ItemId
        public async Task<int?> GetJsonInIdByItemIdAsync(string itemId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT TOP 1 JSON_KEYU
            FROM JSON_IN 
            WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
            AND JSON_VALUE(JSON_DATA, '$.ItemId') = @ItemId
            ORDER BY JSON_CRDA DESC";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@ItemId", itemId);

                var result = await command.ExecuteScalarAsync();
                return result != null ? (int)result : (int?)null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération de l'ID pour l'article {itemId}");
                return null;
            }
        }

        // ✅ NOUVELLE MÉTHODE 4: Récupérer les articles avec leurs IDs pour traçabilité
        public async Task<Dictionary<string, int>> GetArticleIdMappingAsync(List<string> itemIds)
        {
            var mapping = new Dictionary<string, int>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                if (itemIds.Count == 0) return mapping;

                // Construire la requête avec des paramètres
                var parameterNames = itemIds.Select((id, index) => $"@itemId{index}").ToArray();
                var parameterPlaceholders = string.Join(",", parameterNames);

                var sql = $@"
            SELECT 
                JSON_KEYU,
                JSON_VALUE(JSON_DATA, '$.ItemId') as ItemId
            FROM JSON_IN 
            WHERE JSON_FROM = 'data/BRINT34ReleasedProducts'
            AND JSON_VALUE(JSON_DATA, '$.ItemId') IN ({parameterPlaceholders})";

                using var command = new SqlCommand(sql, connection);

                for (int i = 0; i < itemIds.Count; i++)
                {
                    command.Parameters.AddWithValue($"@itemId{i}", itemIds[i]);
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var jsonKeyU = reader.GetInt32("JSON_KEYU");
                    var itemId = reader.GetString("ItemId");

                    if (!string.IsNullOrEmpty(itemId))
                    {
                        mapping[itemId] = jsonKeyU;
                    }
                }

                return mapping;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération du mapping des articles");
                return mapping;
            }
        }

        // ✅ NOUVELLE MÉTHODE 5: Statistiques de traçabilité détaillées
        public async Task<TraceabilityStatistics> GetTraceabilityStatisticsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT 
                COUNT(DISTINCT i.JSON_KEYU) as TotalArticles,
                COUNT(DISTINCT CASE WHEN i.JSON_SENT = 1 THEN i.JSON_KEYU END) as ArticlesMarkedLocally,
                COUNT(DISTINCT o.JSON_IN_KEYU) as ArticlesWithConfirmationAttempts,
                COUNT(DISTINCT CASE WHEN o.JSON_STATUS = 'SUCCESS' THEN o.JSON_IN_KEYU END) as ArticlesConfirmedSuccessfully,
                COUNT(DISTINCT CASE WHEN o.JSON_STATUS = 'ERROR' THEN o.JSON_IN_KEYU END) as ArticlesWithErrors,
                COUNT(DISTINCT CASE WHEN o.JSON_STATUS = 'PENDING' THEN o.JSON_IN_KEYU END) as ArticlesPending,
                COUNT(o.JSON_KEYU) as TotalConfirmationAttempts,
                COUNT(CASE WHEN o.JSON_STATUS = 'SUCCESS' THEN 1 END) as SuccessfulAttempts,
                COUNT(CASE WHEN o.JSON_STATUS = 'ERROR' THEN 1 END) as FailedAttempts
            FROM JSON_IN i
            LEFT JOIN JSON_OUT o ON i.JSON_KEYU = o.JSON_IN_KEYU
            WHERE i.JSON_FROM = 'data/BRINT34ReleasedProducts'";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new TraceabilityStatistics
                    {
                        TotalArticles = reader.GetInt32("TotalArticles"),
                        ArticlesMarkedLocally = reader.GetInt32("ArticlesMarkedLocally"),
                        ArticlesWithConfirmationAttempts = reader.GetInt32("ArticlesWithConfirmationAttempts"),
                        ArticlesConfirmedSuccessfully = reader.GetInt32("ArticlesConfirmedSuccessfully"),
                        ArticlesWithErrors = reader.GetInt32("ArticlesWithErrors"),
                        ArticlesPending = reader.GetInt32("ArticlesPending"),
                        TotalConfirmationAttempts = reader.GetInt32("TotalConfirmationAttempts"),
                        SuccessfulAttempts = reader.GetInt32("SuccessfulAttempts"),
                        FailedAttempts = reader.GetInt32("FailedAttempts")
                    };
                }

                return new TraceabilityStatistics();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des statistiques de traçabilité");
                return new TraceabilityStatistics();
            }
        }

        // ✅ NOUVELLE CLASSE: Modèle pour les statistiques de traçabilité
        public class TraceabilityStatistics
        {
            public int TotalArticles { get; set; }
            public int ArticlesMarkedLocally { get; set; }
            public int ArticlesWithConfirmationAttempts { get; set; }
            public int ArticlesConfirmedSuccessfully { get; set; }
            public int ArticlesWithErrors { get; set; }
            public int ArticlesPending { get; set; }
            public int TotalConfirmationAttempts { get; set; }
            public int SuccessfulAttempts { get; set; }
            public int FailedAttempts { get; set; }

            public double LocalMarkingRate => TotalArticles > 0 ?
                (double)ArticlesMarkedLocally / TotalArticles * 100 : 0;

            public double ConfirmationAttemptRate => TotalArticles > 0 ?
                (double)ArticlesWithConfirmationAttempts / TotalArticles * 100 : 0;

            public double SuccessfulConfirmationRate => ArticlesWithConfirmationAttempts > 0 ?
                (double)ArticlesConfirmedSuccessfully / ArticlesWithConfirmationAttempts * 100 : 0;

            public double AttemptSuccessRate => TotalConfirmationAttempts > 0 ?
                (double)SuccessfulAttempts / TotalConfirmationAttempts * 100 : 0;

            public string GetSummary()
            {
                return $"Articles: {TotalArticles:N0} | Marqués: {ArticlesMarkedLocally:N0} ({LocalMarkingRate:F1}%) | " +
                       $"Confirmés: {ArticlesConfirmedSuccessfully:N0} ({SuccessfulConfirmationRate:F1}%) | " +
                       $"Erreurs: {ArticlesWithErrors:N0} | En attente: {ArticlesPending:N0}";
            }

            public string GetHealthReport()
            {
                var issues = new List<string>();

                if (LocalMarkingRate < 90) issues.Add($"⚠️ Marquage local faible ({LocalMarkingRate:F1}%)");
                if (ConfirmationAttemptRate < 90) issues.Add($"⚠️ Peu de tentatives de confirmation ({ConfirmationAttemptRate:F1}%)");
                if (SuccessfulConfirmationRate < 80) issues.Add($"❌ Taux d'échec élevé ({100 - SuccessfulConfirmationRate:F1}% d'échecs)");
                if (ArticlesPending > ArticlesConfirmedSuccessfully / 10) issues.Add($"⏳ Beaucoup d'articles en attente ({ArticlesPending})");

                if (issues.Count == 0)
                    return "✅ Système en bonne santé";

                return "🚨 Problèmes détectés:\n" + string.Join("\n", issues);
            }
        }
    }
}