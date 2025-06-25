// Fichier: Services/DatabaseService.cs
// Service de gestion des opérations base de données

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using DynamicsApiToDatabase.Models;
using static DynamicsApiToDatabase.Services.ArticlesSyncService;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service de gestion des opérations base de données
    /// </summary>
    public class DatabaseService
    {
        private readonly ILogger<DatabaseService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public DatabaseService(ILogger<DatabaseService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = BuildConnectionString();
        }

        /// <summary>
        /// Synchronise les lignes de commandes avec la base de données
        /// </summary>
        /// <param name="orderLines">Lignes de commandes depuis l'API</param>
        /// <param name="orderConfig">Configuration de l'endpoint</param>
        /// <returns>Résultat de la synchronisation</returns>
        public async Task<OrderSyncResult> SyncOrderLinesWithDatabaseAsync(JsonElement[] orderLines, OrderEndpoint orderConfig)
        {
            var result = new OrderSyncResult { OrderType = orderConfig.DisplayName };

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                Console.WriteLine($"🔄 Synchronisation intelligente des lignes pour {orderConfig.DisplayName}...");

                // ÉTAPE 1 : Récupérer les hash existants
                var existingHashes = await GetExistingOrderLineHashesAsync(connection, orderConfig.TableName);
                Console.WriteLine($"📋 {existingHashes.Count} lignes existantes trouvées");

                // ÉTAPE 2 : Récupérer les IDs composites existants pour détecter les suppressions
                var existingCompositeIds = await GetExistingOrderLineCompositeIdsAsync(connection, orderConfig.TableName);

                // ÉTAPE 3 : Traquer les IDs composites de l'API
                var apiCompositeIds = new HashSet<string>();

                // ÉTAPE 4 : Synchronisation ligne par ligne
                Console.WriteLine("🔍 Analyse et synchronisation des lignes de commandes...");

                foreach (var orderLine in orderLines)
                {
                    try
                    {
                        result.TotalProcessed++;

                        // *** CORRECTION DU BUG DE TYPES JSON ***
                        // Extraction de l'ID principal (commande) - Version corrigée
                        string orderId = JsonHelper.GetFlexibleStringValue(orderLine, orderConfig.PrimaryKeyField);

                        // Extraction du numéro de ligne - Version corrigée  
                        string lineNumber = JsonHelper.GetFlexibleStringValue(orderLine, orderConfig.LineNumberField);

                        // Si lineNumber est vide, utiliser "0"
                        if (string.IsNullOrEmpty(lineNumber) || lineNumber == "UNKNOWN")
                        {
                            lineNumber = "0";
                        }

                        // Création de l'ID composite unique (OrderId + LineNumber)
                        string compositeId = $"{orderId}_{lineNumber}";
                        apiCompositeIds.Add(compositeId);

                        string orderLineJson = orderLine.GetRawText();
                        string currentHash = CalculateHash(orderLineJson);

                        // Vérifier si la ligne de commande existe déjà
                        if (existingHashes.ContainsKey(compositeId))
                        {
                            if (existingHashes[compositeId] != currentHash)
                            {
                                // Ligne modifiée
                                await UpdateExistingOrderLineAsync(connection, orderConfig.TableName, compositeId, orderId, lineNumber, orderLineJson, currentHash, orderConfig.Endpoint);
                                result.UpdatedOrderLines++;

                                if (result.UpdatedOrderLines % 10 == 0)
                                {
                                    Console.WriteLine($"🔄 {result.UpdatedOrderLines} lignes mises à jour");
                                }
                            }
                            else
                            {
                                // Ligne inchangée
                                await TouchOrderLineAsync(connection, orderConfig.TableName, compositeId);
                                result.UnchangedOrderLines++;
                            }
                        }
                        else
                        {
                            // Nouvelle ligne de commande
                            await InsertNewOrderLineAsync(connection, orderConfig.TableName, compositeId, orderId, lineNumber, orderLineJson, currentHash, orderConfig.Endpoint);
                            result.NewOrderLines++;

                            if (result.NewOrderLines % 10 == 0)
                            {
                                Console.WriteLine($"➕ {result.NewOrderLines} nouvelles lignes ajoutées");
                            }
                        }

                        // Affichage du progrès
                        if (result.TotalProcessed % 100 == 0)
                        {
                            string progressMessage = $"📊 {orderConfig.DisplayName}: {result.TotalProcessed}/{orderLines.Length} | " +
                                $"Nouvelles: {result.NewOrderLines} | Modifiées: {result.UpdatedOrderLines} | Inchangées: {result.UnchangedOrderLines}";
                            Console.Write($"\r{progressMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ErrorCount++;
                        _logger.LogError(ex, $"Erreur lors du traitement de la ligne {result.TotalProcessed} pour {orderConfig.Name}");

                        // Debug amélioré
                        Console.WriteLine($"❌ Erreur ligne {result.TotalProcessed}: {ex.Message}");

                        // Log du JSON problématique (seulement les 3 premières erreurs)
                        if (result.ErrorCount <= 3)
                        {
                            _logger.LogWarning($"JSON problématique: {orderLine.GetRawText()}");
                        }
                    }
                }

                Console.WriteLine(); // Nouvelle ligne

                // ÉTAPE 5 : Marquer les lignes supprimées
                var deletedCompositeIds = existingCompositeIds.Except(apiCompositeIds).ToList();
                if (deletedCompositeIds.Any())
                {
                    Console.WriteLine($"🗑️ {deletedCompositeIds.Count} lignes de {orderConfig.DisplayName.ToLower()} supprimées de l'API");
                    foreach (var deletedCompositeId in deletedCompositeIds)
                    {
                        await MarkOrderLineAsDeletedAsync(connection, orderConfig.TableName, deletedCompositeId);
                    }
                }

                Console.WriteLine($"✅ Synchronisation terminée pour {orderConfig.DisplayName}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la synchronisation des {orderConfig.DisplayName}");
                throw;
            }
        }

        #region Méthodes privées de base de données

        /// <summary>
        /// Construit la chaîne de connexion MySQL
        /// </summary>
        private string BuildConnectionString()
        {
            return new MySqlConnectionStringBuilder
            {
                Server = _configuration["Database:Host"],
                Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                UserID = _configuration["Database:User"],
                Password = _configuration["Database:Password"],
                Database = _configuration["Database:Name"]
            }.ConnectionString;
        }

        /// <summary>
        /// Récupère les hash existants des lignes de commandes
        /// </summary>
        private async Task<Dictionary<string, string>> GetExistingOrderLineHashesAsync(MySqlConnection connection, string tableName)
        {
            var hashes = new Dictionary<string, string>();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT composite_id, content_hash FROM {tableName} WHERE composite_id IS NOT NULL AND (is_deleted = FALSE OR is_deleted IS NULL)";

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var compositeId = reader.GetString(0);
                    var hash = reader.GetString(1);
                    hashes[compositeId] = hash;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des hash pour {tableName}");
            }

            return hashes;
        }

        /// <summary>
        /// Récupère les IDs composites existants
        /// </summary>
        private async Task<HashSet<string>> GetExistingOrderLineCompositeIdsAsync(MySqlConnection connection, string tableName)
        {
            var compositeIds = new HashSet<string>();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT composite_id FROM {tableName} WHERE composite_id IS NOT NULL AND (is_deleted = FALSE OR is_deleted IS NULL)";

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var compositeId = reader.GetString(0);
                    compositeIds.Add(compositeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des IDs composites pour {tableName}");
            }

            return compositeIds;
        }

        /// <summary>
        /// Insère une nouvelle ligne de commande
        /// </summary>
        private async Task InsertNewOrderLineAsync(MySqlConnection connection, string tableName, string compositeId, string orderId, string lineNumber, string jsonData, string hash, string endpoint)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    INSERT IGNORE INTO {tableName} (
                        composite_id, order_id, line_number, json_data, 
                        api_endpoint, content_hash, first_seen_at, last_updated_at
                    ) VALUES (
                        @composite_id, @order_id, @line_number, @json_data, 
                        @endpoint, @hash, NOW(), NOW()
                    )";

                command.Parameters.AddWithValue("@composite_id", compositeId);
                command.Parameters.AddWithValue("@order_id", orderId);
                command.Parameters.AddWithValue("@line_number", lineNumber);
                command.Parameters.AddWithValue("@json_data", jsonData);
                command.Parameters.AddWithValue("@endpoint", endpoint);
                command.Parameters.AddWithValue("@hash", hash);
                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex) when (ex.Number == 1062) // Erreur de doublon
            {
                Console.WriteLine($"⚠️ Doublon détecté et ignoré: {compositeId}");
                _logger.LogWarning($"Doublon ignoré pour composite_id: {compositeId}");
            }
        }

        /// <summary>
        /// Met à jour une ligne de commande existante
        /// </summary>
        private async Task UpdateExistingOrderLineAsync(MySqlConnection connection, string tableName, string compositeId, string orderId, string lineNumber, string jsonData, string hash, string endpoint)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                UPDATE {tableName} 
                SET json_data = @json_data, 
                    content_hash = @hash, 
                    last_updated_at = NOW(),
                    update_count = update_count + 1
                WHERE composite_id = @composite_id";

            command.Parameters.AddWithValue("@json_data", jsonData);
            command.Parameters.AddWithValue("@hash", hash);
            command.Parameters.AddWithValue("@composite_id", compositeId);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Met à jour la date de dernière vérification d'une ligne
        /// </summary>
        private async Task TouchOrderLineAsync(MySqlConnection connection, string tableName, string compositeId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE {tableName} SET last_updated_at = NOW() WHERE composite_id = @composite_id";
            command.Parameters.AddWithValue("@composite_id", compositeId);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Marque une ligne comme supprimée
        /// </summary>
        private async Task MarkOrderLineAsDeletedAsync(MySqlConnection connection, string tableName, string compositeId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                UPDATE {tableName} 
                SET is_deleted = TRUE, deleted_at = NOW() 
                WHERE composite_id = @composite_id";
            command.Parameters.AddWithValue("@composite_id", compositeId);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Calcule le hash SHA256 d'une chaîne
        /// </summary>
        private string CalculateHash(string input)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hashBytes);
        }

        #endregion
    }
}