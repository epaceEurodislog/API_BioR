// Fichier: Services/SqlServerDatabaseService.cs
// Service de gestion de la base de données SQL Server pour la table JSON_IN
// VERSION CORRIGÉE - Constructeurs unifiés

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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicsApiToDatabase.Services
{
    public class SqlServerDatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlServerDatabaseService> _logger;

        /// <summary>
        /// ✅ CORRIGÉ: UN SEUL CONSTRUCTEUR avec le bon type de logger
        /// </summary>
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
        private async Task<(int Id, string Hash)?> GetExistingRecordAsync(SqlConnection connection, string businessKey, string endpoint)
        {
            const string sql = @"
                SELECT TOP 1 JSON_KEYU, ISNULL(JSON_HASH, '') as JSON_HASH
                FROM JSON_IN 
                WHERE JSON_BKEY = @BusinessKey AND JSON_FROM = @Endpoint
                ORDER BY JSON_CRDA DESC";

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
        /// Marque plusieurs articles comme confirmés avec méthode robuste
        /// </summary>
        public async Task<int> MarkMultipleArticlesAsConfirmedAsync(List<string> itemIds)
        {
            if (itemIds.Count == 0) return 0;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInformation($"🔄 Début marquage de {itemIds.Count} articles comme confirmés...");

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
        /// Marque un batch d'articles comme confirmés
        /// </summary>
        private async Task<int> MarkBatchAsConfirmedAsync(SqlConnection connection, List<string> itemIds)
        {
            if (itemIds.Count == 0) return 0;

            try
            {
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
                return await MarkBatchWithJsonValueAsync(connection, itemIds);
            }
        }

        /// <summary>
        /// Méthode fallback avec JSON_VALUE
        /// </summary>
        private async Task<int> MarkBatchWithJsonValueAsync(SqlConnection connection, List<string> itemIds)
        {
            try
            {
                _logger.LogWarning("⚠️ Utilisation de la méthode fallback JSON_VALUE");

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
        /// Récupère les lignes d'une Sales Order avec debug complet - VERSION DEBUG
        /// </summary>
        public async Task<List<OrderLineInfo>> GetSalesOrderLinesAsync(string salesOrderId)
        {
            var orderLines = new List<OrderLineInfo>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInformation($"🔍 Recherche lignes pour Sales Order: {salesOrderId}");

                const string sql = @"
            SELECT 
                JSON_KEYU,
                JSON_VALUE(JSON_DATA, '$.transRefId') as SalesOrderId,
                JSON_VALUE(JSON_DATA, '$.itemId') as ItemId,
                JSON_VALUE(JSON_DATA, '$.WMSTRansRecId') as WMSTRansRecId,
                JSON_VALUE(JSON_DATA, '$.qty') as Quantity,
                JSON_VALUE(JSON_DATA, '$.INT3PLStatus') as Status,
                JSON_VALUE(JSON_DATA, '$.dataAreaId') as DataAreaId,
                JSON_CRDA as CreatedDate,
                -- ✅ AJOUT : Récupérer aussi le JSON brut pour diagnostic
                JSON_DATA as RawJson
            FROM JSON_IN 
            WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
            AND JSON_VALUE(JSON_DATA, '$.transRefId') = @SalesOrderId
            AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
            ORDER BY JSON_KEYU";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SalesOrderId", salesOrderId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var jsonKeyU = reader.GetInt32("JSON_KEYU");
                    var itemId = reader.IsDBNull("ItemId") ? "" : reader.GetString("ItemId");
                    var wmsTransRecIdStr = reader.IsDBNull("WMSTRansRecId") ? "" : reader.GetString("WMSTRansRecId");
                    var quantityStr = reader.IsDBNull("Quantity") ? "0" : reader.GetString("Quantity");
                    var status = reader.IsDBNull("Status") ? "" : reader.GetString("Status");
                    var dataAreaId = reader.IsDBNull("DataAreaId") ? "" : reader.GetString("DataAreaId");
                    var rawJson = reader.GetString("RawJson");

                    // ✅ DEBUG COMPLET
                    _logger.LogInformation($"📊 Ligne trouvée:");
                    _logger.LogInformation($"   JSON_KEYU: {jsonKeyU}");
                    _logger.LogInformation($"   ItemId: {itemId}");
                    _logger.LogInformation($"   WMSTRansRecId (string): '{wmsTransRecIdStr}'");
                    _logger.LogInformation($"   DataAreaId: {dataAreaId}");
                    _logger.LogInformation($"   Status: {status}");

                    // ✅ EXTRACTION MANUELLE depuis le JSON brut pour vérification
                    var manualWmsRecId = ExtractWMSTRansRecIdManually(rawJson);
                    _logger.LogInformation($"   WMSTRansRecId (manuel): {manualWmsRecId}");

                    // ✅ CONVERSION avec vérification
                    if (long.TryParse(wmsTransRecIdStr, out var wmsTransRecId))
                    {
                        _logger.LogInformation($"   ✅ Conversion réussie: {wmsTransRecIdStr} -> {wmsTransRecId}");

                        // Vérifier la cohérence avec extraction manuelle
                        if (manualWmsRecId != wmsTransRecId)
                        {
                            _logger.LogWarning($"   ⚠️ INCOHÉRENCE: JSON_VALUE={wmsTransRecId} vs Manuel={manualWmsRecId}");
                        }
                    }
                    else
                    {
                        _logger.LogError($"   ❌ Conversion échouée: '{wmsTransRecIdStr}' n'est pas un long valide");
                        wmsTransRecId = manualWmsRecId; // Utiliser la valeur manuelle
                    }

                    if (wmsTransRecId > 0 && !string.IsNullOrEmpty(itemId))
                    {
                        _logger.LogInformation($"🔍 STOCKAGE: wmsTransRecId={wmsTransRecId}, va être stocké dans LineNumber={wmsTransRecId}");
                        orderLines.Add(new OrderLineInfo
                        {
                            OrderId = salesOrderId,
                            ItemId = itemId,
                            LineNumber = (int)wmsTransRecId, // ✅ ATTENTION: Stockage dans LineNumber
                            Quantity = decimal.TryParse(quantityStr, out var qty) ? qty : 0,
                            OrderType = "Sales",
                            Status = status,
                            CreatedDate = reader.GetDateTime("CreatedDate"),
                            LastUpdated = DateTime.Now
                        });
                        _logger.LogInformation($"🔍 VÉRIFICATION: LineNumber stocké={(int)wmsTransRecId}");
                        _logger.LogInformation($"   ✅ Ligne ajoutée: Item={itemId}, WMSTRansRecId={wmsTransRecId}");
                    }
                    else
                    {
                        _logger.LogWarning($"   ⚠️ Ligne ignorée: WMSTRansRecId={wmsTransRecId}, Item='{itemId}'");
                    }
                }

                _logger.LogInformation($"📊 {orderLines.Count} lignes valides récupérées pour Sales Order {salesOrderId}");
                return orderLines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération lignes Sales Order {salesOrderId}");
                return orderLines;
            }
        }

        /// <summary>
        /// Extraction manuelle du WMSTRansRecId depuis le JSON brut
        /// </summary>
        private long ExtractWMSTRansRecIdManually(string jsonData)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonData);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("WMSTRansRecId", out var property))
                {
                    if (property.ValueKind == JsonValueKind.Number)
                    {
                        return property.GetInt64();
                    }
                    else if (property.ValueKind == JsonValueKind.String)
                    {
                        var stringValue = property.GetString();
                        if (long.TryParse(stringValue, out var result))
                        {
                            return result;
                        }
                    }
                }

                return 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Méthode de debug pour analyser les données Sales Orders dans JSON_IN
        /// </summary>
        public async Task<List<SalesOrderDebugInfo>> GetSalesOrderDebugInfoAsync(string salesOrderId)
        {
            var debugInfo = new List<SalesOrderDebugInfo>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT 
                JSON_KEYU,
                JSON_DATA,
                JSON_VALUE(JSON_DATA, '$.transRefId') as SalesOrderId,
                JSON_VALUE(JSON_DATA, '$.BRPortalOrderNumber') as PortalOrderNumber,
                JSON_VALUE(JSON_DATA, '$.itemId') as ItemId,
                JSON_VALUE(JSON_DATA, '$.WMSTRansRecId') as WMSTransRecId,
                JSON_VALUE(JSON_DATA, '$.qty') as Quantity,
                JSON_VALUE(JSON_DATA, '$.INT3PLStatus') as CurrentStatus,
                JSON_VALUE(JSON_DATA, '$.expeditionStatus') as ExpeditionStatus,
                JSON_VALUE(JSON_DATA, '$.dataAreaId') as DataAreaId,
                JSON_CRDA as CreatedDate,
                JSON_STAT as Status
            FROM JSON_IN 
            WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
            AND (JSON_VALUE(JSON_DATA, '$.transRefId') = @SalesOrderId 
                 OR JSON_VALUE(JSON_DATA, '$.BRPortalOrderNumber') = @SalesOrderId)
            ORDER BY CAST(JSON_VALUE(JSON_DATA, '$.WMSTRansRecId') AS BIGINT)";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SalesOrderId", salesOrderId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    debugInfo.Add(new SalesOrderDebugInfo
                    {
                        JsonKeyU = reader.GetInt32("JSON_KEYU"),
                        SalesOrderId = reader.IsDBNull("SalesOrderId") ? "" : reader.GetString("SalesOrderId"),
                        PortalOrderNumber = reader.IsDBNull("PortalOrderNumber") ? "" : reader.GetString("PortalOrderNumber"),
                        ItemId = reader.IsDBNull("ItemId") ? "" : reader.GetString("ItemId"),
                        WMSTransRecIdStr = reader.IsDBNull("WMSTransRecId") ? "" : reader.GetString("WMSTransRecId"),
                        Quantity = reader.IsDBNull("Quantity") ? "" : reader.GetString("Quantity"),
                        CurrentStatus = reader.IsDBNull("CurrentStatus") ? "" : reader.GetString("CurrentStatus"),
                        ExpeditionStatus = reader.IsDBNull("ExpeditionStatus") ? "" : reader.GetString("ExpeditionStatus"),
                        DataAreaId = reader.IsDBNull("DataAreaId") ? "" : reader.GetString("DataAreaId"),
                        CreatedDate = reader.GetDateTime("CreatedDate"),
                        RecordStatus = reader.IsDBNull("Status") ? "" : reader.GetString("Status"),
                        RawJsonData = reader.GetString("JSON_DATA")
                    });
                }

                _logger.LogInformation($"🔍 Debug info récupérée: {debugInfo.Count} lignes pour Sales Order {salesOrderId}");

                // Log détaillé pour diagnostic
                foreach (var info in debugInfo)
                {
                    _logger.LogInformation($"📊 Ligne {info.JsonKeyU}: WMSTransRecId={info.WMSTransRecIdStr}, Item={info.ItemId}, Status={info.CurrentStatus}");
                }

                return debugInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération debug info Sales Order {salesOrderId}");
                return debugInfo;
            }
        }

        /// <summary>
        /// Classe pour les informations de debug des Sales Orders
        /// </summary>
        public class SalesOrderDebugInfo
        {
            public int JsonKeyU { get; set; }
            public string SalesOrderId { get; set; } = "";
            public string PortalOrderNumber { get; set; } = "";
            public string ItemId { get; set; } = "";
            public string WMSTransRecIdStr { get; set; } = "";
            public string Quantity { get; set; } = "";
            public string CurrentStatus { get; set; } = "";
            public string ExpeditionStatus { get; set; } = "";
            public string DataAreaId { get; set; } = "";
            public DateTime CreatedDate { get; set; }
            public string RecordStatus { get; set; } = "";
            public string RawJsonData { get; set; } = "";

            public long WMSTransRecIdLong => long.TryParse(WMSTransRecIdStr, out var result) ? result : 0;

            public string GetSummary()
            {
                return $"ID:{JsonKeyU} | WMSRecId:{WMSTransRecIdStr} | Item:{ItemId} | Status:{CurrentStatus} | Area:{DataAreaId}";
            }
        }

        /// <summary>
        /// Récupère tous les IDs des Sales Orders actives
        /// </summary>
        public async Task<List<string>> GetActiveSalesOrderIdsAsync()
        {
            var orderIds = new List<string>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT DISTINCT JSON_VALUE(JSON_DATA, '$.transRefId') as SalesOrderId
            FROM JSON_IN 
            WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
            AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
            AND JSON_VALUE(JSON_DATA, '$.transRefId') IS NOT NULL
            AND JSON_VALUE(JSON_DATA, '$.transType') = 'Sales'
            ORDER BY JSON_VALUE(JSON_DATA, '$.transRefId')";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var salesOrderId = reader.GetString("SalesOrderId");
                    if (!string.IsNullOrEmpty(salesOrderId))
                    {
                        orderIds.Add(salesOrderId);
                    }
                }

                _logger.LogInformation($"📊 {orderIds.Count} Sales Orders actives trouvées");
                return orderIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération Sales Orders actives");
                return orderIds;
            }
        }

        /// <summary>
        /// Récupère les Sales Orders par statut INT3PL
        /// </summary>
        public async Task<List<string>> GetSalesOrderIdsByStatusAsync(string int3plStatus)
        {
            var orderIds = new List<string>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT DISTINCT JSON_VALUE(JSON_DATA, '$.transRefId') as SalesOrderId
            FROM JSON_IN 
            WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
            AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
            AND JSON_VALUE(JSON_DATA, '$.transRefId') IS NOT NULL
            AND JSON_VALUE(JSON_DATA, '$.transType') = 'Sales'
            AND JSON_VALUE(JSON_DATA, '$.INT3PLStatus') = @Status
            ORDER BY JSON_VALUE(JSON_DATA, '$.transRefId')";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Status", int3plStatus);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var salesOrderId = reader.GetString("SalesOrderId");
                    if (!string.IsNullOrEmpty(salesOrderId))
                    {
                        orderIds.Add(salesOrderId);
                    }
                }

                _logger.LogInformation($"📊 {orderIds.Count} Sales Orders trouvées avec statut '{int3plStatus}'");
                return orderIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération Sales Orders par statut '{int3plStatus}'");
                return orderIds;
            }
        }

        /// <summary>
        /// Récupère des statistiques sur les Sales Orders
        /// </summary>
        public async Task<SalesOrderStatistics> GetSalesOrderStatisticsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT 
                COUNT(DISTINCT JSON_VALUE(JSON_DATA, '$.transRefId')) as TotalOrders,
                COUNT(*) as TotalLines,
                COUNT(CASE WHEN JSON_VALUE(JSON_DATA, '$.INT3PLStatus') = 'None' THEN 1 END) as PendingLines,
                COUNT(CASE WHEN JSON_VALUE(JSON_DATA, '$.INT3PLStatus') = 'Processed' THEN 1 END) as ProcessedLines,
                COUNT(CASE WHEN JSON_VALUE(JSON_DATA, '$.expeditionStatus') = 'Activated' THEN 1 END) as ActivatedLines,
                COUNT(CASE WHEN JSON_CRDA >= DATEADD(day, -1, GETDATE()) THEN 1 END) as CreatedLast24h,
                AVG(CAST(JSON_VALUE(JSON_DATA, '$.qty') AS DECIMAL)) as AverageQuantity
            FROM JSON_IN
            WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
            AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new SalesOrderStatistics
                    {
                        TotalOrders = reader.GetInt32("TotalOrders"),
                        TotalLines = reader.GetInt32("TotalLines"),
                        PendingLines = reader.GetInt32("PendingLines"),
                        ProcessedLines = reader.GetInt32("ProcessedLines"),
                        ActivatedLines = reader.GetInt32("ActivatedLines"),
                        CreatedLast24h = reader.GetInt32("CreatedLast24h"),
                        AverageQuantity = reader.IsDBNull("AverageQuantity") ? 0 : reader.GetDecimal("AverageQuantity")
                    };
                }

                return new SalesOrderStatistics();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des statistiques Sales Orders");
                return new SalesOrderStatistics();
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

        /// <summary>
        /// Récupère les lignes d'une Purchase Order depuis JSON_IN
        /// </summary>
        public async Task<List<OrderLineInfo>> GetPurchaseOrderLinesAsync(string purchId)
        {
            var orderLines = new List<OrderLineInfo>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        JSON_DATA,
                        JSON_VALUE(JSON_DATA, '$.PurchId') as PurchId,
                        JSON_VALUE(JSON_DATA, '$.ItemId') as ItemId,
                        JSON_VALUE(JSON_DATA, '$.LineNumber') as LineNumber,
                        JSON_VALUE(JSON_DATA, '$.PurchQty') as Quantity
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT32PurchOrderTables'
                    AND JSON_VALUE(JSON_DATA, '$.PurchId') = @PurchId
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    ORDER BY CAST(JSON_VALUE(JSON_DATA, '$.LineNumber') AS INT)";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@PurchId", purchId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var itemId = reader.IsDBNull("ItemId") ? "" : reader.GetString("ItemId");
                    var lineNumberStr = reader.IsDBNull("LineNumber") ? "0" : reader.GetString("LineNumber");
                    var quantityStr = reader.IsDBNull("Quantity") ? "0" : reader.GetString("Quantity");

                    if (!string.IsNullOrEmpty(itemId))
                    {
                        orderLines.Add(new OrderLineInfo
                        {
                            OrderId = purchId,
                            ItemId = itemId,
                            LineNumber = int.TryParse(lineNumberStr, out var lineNum) ? lineNum : 0,
                            Quantity = decimal.TryParse(quantityStr, out var qty) ? qty : 0,
                            OrderType = "Purchase"
                        });
                    }
                }

                _logger.LogInformation($"📊 {orderLines.Count} lignes récupérées pour Purchase Order {purchId}");
                return orderLines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération lignes Purchase Order {purchId}");
                return orderLines;
            }
        }

        /// <summary>
        /// Récupère les lignes d'une Return Order depuis JSON_IN
        /// </summary>
        public async Task<List<OrderLineInfo>> GetReturnOrderLinesAsync(string returnId)
        {
            var orderLines = new List<OrderLineInfo>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        JSON_DATA,
                        JSON_VALUE(JSON_DATA, '$.ReturnItemNum') as ReturnItemNum,
                        JSON_VALUE(JSON_DATA, '$.ItemId') as ItemId,
                        JSON_VALUE(JSON_DATA, '$.LineNum') as LineNumber,
                        JSON_VALUE(JSON_DATA, '$.OrderedReturnQuantity') as Quantity
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT32ReturnOrderTables'
                    AND JSON_VALUE(JSON_DATA, '$.ReturnItemNum') = @ReturnId
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    ORDER BY CAST(JSON_VALUE(JSON_DATA, '$.LineNum') AS DECIMAL)";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@ReturnId", returnId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var itemId = reader.IsDBNull("ItemId") ? "" : reader.GetString("ItemId");
                    var lineNumberStr = reader.IsDBNull("LineNumber") ? "0" : reader.GetString("LineNumber");
                    var quantityStr = reader.IsDBNull("Quantity") ? "0" : reader.GetString("Quantity");

                    if (!string.IsNullOrEmpty(itemId))
                    {
                        orderLines.Add(new OrderLineInfo
                        {
                            OrderId = returnId,
                            ItemId = itemId,
                            LineNumber = (int)(decimal.TryParse(lineNumberStr, out var lineNum) ? lineNum : 0),
                            Quantity = decimal.TryParse(quantityStr, out var qty) ? qty : 0,
                            OrderType = "Return"
                        });
                    }
                }

                _logger.LogInformation($"📊 {orderLines.Count} lignes récupérées pour Return Order {returnId}");
                return orderLines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération lignes Return Order {returnId}");
                return orderLines;
            }
        }

        /// <summary>
        /// Récupère les lignes d'une Transfer Order depuis JSON_IN
        /// </summary>
        public async Task<List<OrderLineInfo>> GetTransferOrderLinesAsync(string transferId)
        {
            var orderLines = new List<OrderLineInfo>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT 
                        JSON_DATA,
                        JSON_VALUE(JSON_DATA, '$.TransferId') as TransferId,
                        JSON_VALUE(JSON_DATA, '$.ItemId') as ItemId,
                        JSON_VALUE(JSON_DATA, '$.LineNumber') as LineNumber,
                        JSON_VALUE(JSON_DATA, '$.QtyTransfer') as Quantity
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT32TransferOrderTables'
                    AND JSON_VALUE(JSON_DATA, '$.TransferId') = @TransferId
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    ORDER BY CAST(JSON_VALUE(JSON_DATA, '$.LineNumber') AS INT)";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TransferId", transferId);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var itemId = reader.IsDBNull("ItemId") ? "" : reader.GetString("ItemId");
                    var lineNumberStr = reader.IsDBNull("LineNumber") ? "0" : reader.GetString("LineNumber");
                    var quantityStr = reader.IsDBNull("Quantity") ? "0" : reader.GetString("Quantity");

                    if (!string.IsNullOrEmpty(itemId))
                    {
                        orderLines.Add(new OrderLineInfo
                        {
                            OrderId = transferId,
                            ItemId = itemId,
                            LineNumber = int.TryParse(lineNumberStr, out var lineNum) ? lineNum : 0,
                            Quantity = decimal.TryParse(quantityStr, out var qty) ? qty : 0,
                            OrderType = "Transfer"
                        });
                    }
                }

                _logger.LogInformation($"📊 {orderLines.Count} lignes récupérées pour Transfer Order {transferId}");
                return orderLines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération lignes Transfer Order {transferId}");
                return orderLines;
            }
        }

        /// <summary>
        /// Récupère tous les IDs des Purchase Orders actives
        /// </summary>
        public async Task<List<string>> GetActivePurchaseOrderIdsAsync()
        {
            var orderIds = new List<string>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT DISTINCT JSON_VALUE(JSON_DATA, '$.PurchId') as PurchId
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT32PurchOrderTables'
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    AND JSON_VALUE(JSON_DATA, '$.PurchId') IS NOT NULL
                    ORDER BY JSON_VALUE(JSON_DATA, '$.PurchId')";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var purchId = reader.GetString("PurchId");
                    if (!string.IsNullOrEmpty(purchId))
                    {
                        orderIds.Add(purchId);
                    }
                }

                _logger.LogInformation($"📊 {orderIds.Count} Purchase Orders actives trouvées");
                return orderIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération Purchase Orders actives");
                return orderIds;
            }
        }

        /// <summary>
        /// Récupère tous les IDs des Return Orders actives
        /// </summary>
        public async Task<List<string>> GetActiveReturnOrderIdsAsync()
        {
            var orderIds = new List<string>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT DISTINCT JSON_VALUE(JSON_DATA, '$.ReturnItemNum') as ReturnItemNum
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT32ReturnOrderTables'
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    AND JSON_VALUE(JSON_DATA, '$.ReturnItemNum') IS NOT NULL
                    ORDER BY JSON_VALUE(JSON_DATA, '$.ReturnItemNum')";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var returnId = reader.GetString("ReturnItemNum");
                    if (!string.IsNullOrEmpty(returnId))
                    {
                        orderIds.Add(returnId);
                    }
                }

                _logger.LogInformation($"📊 {orderIds.Count} Return Orders actives trouvées");
                return orderIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération Return Orders actives");
                return orderIds;
            }
        }

        /// <summary>
        /// Récupère tous les IDs des Transfer Orders actives
        /// </summary>
        public async Task<List<string>> GetActiveTransferOrderIdsAsync()
        {
            var orderIds = new List<string>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT DISTINCT JSON_VALUE(JSON_DATA, '$.TransferId') as TransferId
                    FROM JSON_IN 
                    WHERE JSON_FROM = 'data/BRINT32TransferOrderTables'
                    AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
                    AND JSON_VALUE(JSON_DATA, '$.TransferId') IS NOT NULL
                    ORDER BY JSON_VALUE(JSON_DATA, '$.TransferId')";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var transferId = reader.GetString("TransferId");
                    if (!string.IsNullOrEmpty(transferId))
                    {
                        orderIds.Add(transferId);
                    }
                }

                _logger.LogInformation($"📊 {orderIds.Count} Transfer Orders actives trouvées");
                return orderIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération Transfer Orders actives");
                return orderIds;
            }
        }

        /// <summary>
        /// Récupère le transRefId d'une Sales Order à partir du BRPortalOrderNumber
        /// </summary>
        public async Task<string> GetSalesOrderIdByPortalNumberAsync(string portalOrderNumber)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
            SELECT DISTINCT JSON_VALUE(JSON_DATA, '$.transRefId') as SalesOrderId
            FROM JSON_IN 
            WHERE JSON_FROM = 'data/BRPackingSlipInterfaces'
            AND ISNULL(JSON_STAT, 'ACTIVE') = 'ACTIVE'
            AND JSON_VALUE(JSON_DATA, '$.BRPortalOrderNumber') = @PortalOrderNumber
            AND JSON_VALUE(JSON_DATA, '$.transRefId') IS NOT NULL";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@PortalOrderNumber", portalOrderNumber);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var salesOrderId = reader.GetString("SalesOrderId");
                    _logger.LogInformation($"📊 Sales Order {salesOrderId} trouvée pour portail {portalOrderNumber}");
                    return salesOrderId;
                }

                _logger.LogWarning($"⚠️ Aucune Sales Order trouvée pour le numéro portail {portalOrderNumber}");
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération Sales Order pour portail {portalOrderNumber}");
                return "";
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
        }
    }
}