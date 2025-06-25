// Fichier: Services/ArticlesSyncService.cs
// Service de synchronisation des articles depuis l'API Dynamics

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service de synchronisation des articles depuis l'API Dynamics
    /// </summary>
    public class ArticlesSyncService
    {
        private readonly ILogger<ArticlesSyncService> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _connectionString;

        public ArticlesSyncService(
            ILogger<ArticlesSyncService> logger,
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
            _connectionString = BuildConnectionString();
        }
        
        // Helper for flexible JSON value extraction
        internal static class JsonHelper
        {
            public static string GetFlexibleStringValue(JsonElement element, string propertyName)
            {
                if (element.TryGetProperty(propertyName, out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        return value.GetString();
                    if (value.ValueKind == JsonValueKind.Number)
                        return value.GetRawText();
                    return value.ToString();
                }
                return "UNKNOWN";
            }
        }

        /// <summary>
        /// Synchronise les articles depuis l'API Dynamics
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <returns>Résultat de la synchronisation</returns>
        public async Task<SyncResult> SyncArticlesAsync(string token)
        {
            var result = new SyncResult();
            var stopwatch = Stopwatch.StartNew();
            const string endpoint = "data/BRINT34ReleasedProducts";

            try
            {
                Console.WriteLine("📦 Récupération des articles depuis l'API...");

                // Récupération des données depuis l'API
                var articles = await FetchArticlesFromApiAsync(token, endpoint);
                if (articles == null || articles.Length == 0)
                {
                    Console.WriteLine("⚠️ Aucun article trouvé dans l'API");
                    return result;
                }

                Console.WriteLine($"✅ {articles.Length} articles trouvés dans l'API");

                // Synchronisation avec la base de données
                result = await SyncArticlesWithDatabaseAsync(articles, endpoint);

                stopwatch.Stop();

                // Log du résultat
                string status = result.ErrorCount == 0 ? "SUCCESS" :
                               (result.ErrorCount < result.TotalProcessed ? "WARNING" : "ERROR");

                await LogSyncResultAsync(endpoint, status, result, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la synchronisation des articles depuis {endpoint}");
                Console.WriteLine($"❌ Erreur synchronisation articles: {ex.Message}");

                await LogSyncErrorAsync(endpoint, ex.Message, stopwatch.ElapsedMilliseconds);

                result.ErrorCount = 1;
                return result;
            }
        }

        /// <summary>
        /// Récupère les articles depuis l'API Dynamics
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="endpoint">Endpoint de l'API</param>
        /// <returns>Tableau des articles JSON</returns>
        private async Task<JsonElement[]> FetchArticlesFromApiAsync(string token, string endpoint)
        {
            try
            {
                var url = $"{_configuration["Resource"]}{endpoint}";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                _logger.LogInformation($"Appel API GET: {url}");
                Console.WriteLine($"📡 Appel API: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"❌ Erreur API: {response.StatusCode}");
                    return null;
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"JSON reçu: {jsonContent.Length} caractères");
                Console.WriteLine($"✓ Données reçues: {jsonContent.Length} caractères");

                var jsonDocument = JsonDocument.Parse(jsonContent);

                if (!jsonDocument.RootElement.TryGetProperty("value", out var articlesArray))
                {
                    _logger.LogWarning("Propriété 'value' non trouvée dans la réponse JSON");
                    Console.WriteLine("⚠️ Aucun article trouvé dans la réponse");
                    return new JsonElement[0];
                }

                var articles = articlesArray.EnumerateArray().ToArray();
                Console.WriteLine($"✅ {articles.Length} articles trouvés dans l'API");

                return articles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération depuis {endpoint}");
                throw;
            }
        }

        /// <summary>
        /// Synchronise les articles avec la base de données
        /// </summary>
        /// <param name="articles">Articles depuis l'API</param>
        /// <param name="endpoint">Endpoint source</param>
        /// <returns>Résultat de la synchronisation</returns>
        private async Task<SyncResult> SyncArticlesWithDatabaseAsync(JsonElement[] articles, string endpoint)
        {
            var result = new SyncResult();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                Console.WriteLine("🔄 Début de la synchronisation intelligente...");

                // ÉTAPE 1 : Récupérer les hash existants pour comparaison
                Console.WriteLine("📋 Récupération des articles existants...");
                var existingHashes = await GetExistingArticleHashesAsync(connection);
                Console.WriteLine($"✓ {existingHashes.Count} articles existants trouvés");
                // ÉTAPE 2 : Récupérer tous les ItemIds existants pour détecter les suppressions
                var existingItemIds = await GetExistingArticleIdsAsync(connection);

                // ÉTAPE 3 : Traquer les ItemIds de l'API pour détecter les articles supprimés
                var apiItemIds = new HashSet<string>();

                // ÉTAPE 4 : Synchronisation article par article
                Console.WriteLine("🔍 Analyse et synchronisation des articles...");

                foreach (var article in articles)
                {
                    try
                    {
                        result.TotalProcessed++;

                        // Extraction de l'ItemId avec gestion flexible du type
                        string itemId = JsonHelper.GetFlexibleStringValue(article, "ItemId");
                        if (string.IsNullOrEmpty(itemId) || itemId == "UNKNOWN")
                        {
                            _logger.LogWarning($"ItemId manquant pour l'article {result.TotalProcessed}");
                            result.ErrorCount++;
                            continue;
                        }

                        apiItemIds.Add(itemId);

                        string articleJson = article.GetRawText();
                        string currentHash = CalculateHash(articleJson);

                        // Vérifier si l'article existe déjà
                        if (existingHashes.ContainsKey(itemId))
                        {
                            if (existingHashes[itemId] != currentHash)
                            {
                                // Article modifié
                                await UpdateExistingArticleAsync(connection, itemId, articleJson, currentHash, endpoint);
                                result.UpdatedArticles++;

                                if (result.UpdatedArticles % 50 == 0)
                                {
                                    Console.WriteLine($"🔄 {result.UpdatedArticles} articles mis à jour");
                                }
                            }
                            else
                            {
                                // Article inchangé
                                await TouchArticleAsync(connection, itemId);
                                result.UnchangedArticles++;
                            }
                        }
                        else
                        {
                            // Nouvel article
                            await InsertNewArticleAsync(connection, itemId, articleJson, currentHash, endpoint);
                            result.NewArticles++;

                            if (result.NewArticles % 50 == 0)
                            {
                                Console.WriteLine($"➕ {result.NewArticles} nouveaux articles ajoutés");
                            }
                        }

                        // Affichage du progrès
                        if (result.TotalProcessed % 100 == 0)
                        {
                            string progressMessage = $"📊 Articles: {result.TotalProcessed}/{articles.Length} | " +
                                $"Nouveaux: {result.NewArticles} | Modifiés: {result.UpdatedArticles} | Inchangés: {result.UnchangedArticles}";
                            Console.Write($"\r{progressMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ErrorCount++;
                        _logger.LogError(ex, $"Erreur lors du traitement de l'article {result.TotalProcessed}");

                        if (result.ErrorCount <= 3)
                        {
                            _logger.LogWarning($"JSON problématique: {article.GetRawText()}");
                        }
                    }
                }

                Console.WriteLine(); // Nouvelle ligne

                // ÉTAPE 5 : Marquer les articles supprimés
                var deletedItemIds = existingItemIds.Except(apiItemIds).ToList();
                if (deletedItemIds.Any())
                {
                    Console.WriteLine($"🗑️ {deletedItemIds.Count} articles supprimés de l'API");
                    foreach (var deletedItemId in deletedItemIds)
                    {
                        await MarkArticleAsDeletedAsync(connection, deletedItemId);
                    }
                }

                Console.WriteLine($"✅ Synchronisation des articles terminée");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation des articles");
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
        /// Récupère les hash existants des articles
        /// </summary>
        private async Task<Dictionary<string, string>> GetExistingArticleHashesAsync(MySqlConnection connection)
        {
            var hashes = new Dictionary<string, string>();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT item_id, content_hash FROM articles_raw WHERE item_id IS NOT NULL AND (is_deleted = FALSE OR is_deleted IS NULL)";

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var itemId = reader.GetString(0);
                    var hash = reader.GetString(1);
                    hashes[itemId] = hash;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des hash d'articles");
            }

            return hashes;
        }

        /// <summary>
        /// Récupère les IDs d'articles existants
        /// </summary>
        private async Task<HashSet<string>> GetExistingArticleIdsAsync(MySqlConnection connection)
        {
            var itemIds = new HashSet<string>();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT item_id FROM articles_raw WHERE item_id IS NOT NULL AND (is_deleted = FALSE OR is_deleted IS NULL)";

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    itemIds.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des IDs d'articles");
            }

            return itemIds;
        }

        /// <summary>
        /// Insère un nouvel article
        /// </summary>
        private async Task InsertNewArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT IGNORE INTO articles_raw (
                        item_id, json_data, content_hash, api_endpoint, first_seen_at, last_updated_at
                    ) VALUES (
                        @item_id, @json_data, @hash, @endpoint, NOW(), NOW()
                    )";

                command.Parameters.AddWithValue("@item_id", itemId);
                command.Parameters.AddWithValue("@json_data", jsonData);
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@endpoint", endpoint);
                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                Console.WriteLine($"⚠️ Doublon détecté et ignoré: {itemId}");
                _logger.LogWarning($"Doublon ignoré pour item_id: {itemId}");
            }
        }

        /// <summary>
        /// Met à jour un article existant
        /// </summary>
        private async Task UpdateExistingArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE articles_raw 
                SET json_data = @json_data, 
                    content_hash = @hash, 
                    last_updated_at = NOW(),
                    update_count = update_count + 1
                WHERE item_id = @item_id";

            command.Parameters.AddWithValue("@json_data", jsonData);
            command.Parameters.AddWithValue("@hash", hash);
            command.Parameters.AddWithValue("@item_id", itemId);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Met à jour la date de dernière vérification
        /// </summary>
        private async Task TouchArticleAsync(MySqlConnection connection, string itemId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE articles_raw SET last_updated_at = NOW() WHERE item_id = @item_id";
            command.Parameters.AddWithValue("@item_id", itemId);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Marque un article comme supprimé
        /// </summary>
        private async Task MarkArticleAsDeletedAsync(MySqlConnection connection, string itemId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE articles_raw SET is_deleted = TRUE, deleted_at = NOW() WHERE item_id = @item_id";
            command.Parameters.AddWithValue("@item_id", itemId);
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

        /// <summary>
        /// Enregistre le résultat de synchronisation dans les logs
        /// </summary>
        private async Task LogSyncResultAsync(string endpoint, string status, SyncResult result, long executionTimeMs)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sync_logs (
                        endpoint, status, total_articles_processed, new_articles, 
                        updated_articles, unchanged_articles, error_count, 
                        execution_time_ms, sync_date
                    ) VALUES (
                        @endpoint, @status, @total, @new, @updated, @unchanged, 
                        @errors, @execution_time, NOW()
                    )";

                command.Parameters.AddWithValue("@endpoint", endpoint);
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@total", result.TotalProcessed);
                command.Parameters.AddWithValue("@new", result.NewArticles);
                command.Parameters.AddWithValue("@updated", result.UpdatedArticles);
                command.Parameters.AddWithValue("@unchanged", result.UnchangedArticles);
                command.Parameters.AddWithValue("@errors", result.ErrorCount);
                command.Parameters.AddWithValue("@execution_time", executionTimeMs);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log de synchronisation");
            }
        }

        /// <summary>
        /// Enregistre une erreur de synchronisation dans les logs
        /// </summary>
        private async Task LogSyncErrorAsync(string endpoint, string message, long executionTimeMs)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO sync_logs (
                        endpoint, status, total_articles_processed, new_articles, 
                        updated_articles, unchanged_articles, error_count, 
                        message, execution_time_ms, sync_date
                    ) VALUES (
                        @endpoint, 'ERROR', 0, 0, 0, 0, 1, @message, @execution_time, NOW()
                    )";

                command.Parameters.AddWithValue("@endpoint", endpoint);
                command.Parameters.AddWithValue("@message", message);
                command.Parameters.AddWithValue("@execution_time", executionTimeMs);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log d'erreur");
            }
        }

        #endregion
    }
}