// Fichier: Program.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;

namespace DynamicsApiToDatabase
{
    class Program
    {
        private static ILogger<Program> _logger;
        private static IConfiguration _configuration;
        private static HttpClient _httpClient;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Synchronisation intelligente des articles Dynamics ===");

            // Configuration
            SetupConfiguration();
            SetupLogging();
            SetupHttpClient();

            _logger.LogInformation("Démarrage de la synchronisation des articles");

            // Création de la base de données et des tables si nécessaire
            if (!CreateDatabaseIfNotExists())
            {
                _logger.LogError("Impossible de créer ou d'accéder à la base de données. Arrêt du programme.");
                Console.WriteLine("Erreur: Problème de base de données. Vérifiez votre configuration WAMP.");
                return;
            }

            // Obtention du token d'accès
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Impossible d'obtenir un token d'accès. Arrêt du programme.");
                Console.WriteLine("Erreur: Impossible de s'authentifier auprès de l'API Dynamics.");
                return;
            }

            Console.WriteLine("✓ Authentification réussie");

            // Endpoint pour les articles
            string articlesEndpoint = "data/BRINT34ReleasedProducts";

            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation($"Début de la synchronisation depuis: {articlesEndpoint}");
            Console.WriteLine($"Récupération des articles depuis l'API...");

            // Récupération et synchronisation intelligente des articles
            var syncResult = await FetchAndSyncArticlesAsync(token, articlesEndpoint);

            stopwatch.Stop();

            Console.WriteLine($"\n=== RÉSULTAT DE LA SYNCHRONISATION ===");
            Console.WriteLine($"✓ Articles traités: {syncResult.TotalProcessed}");
            Console.WriteLine($"  - Nouveaux articles ajoutés: {syncResult.NewArticles}");
            Console.WriteLine($"  - Articles mis à jour: {syncResult.UpdatedArticles}");
            Console.WriteLine($"  - Articles inchangés: {syncResult.UnchangedArticles}");
            Console.WriteLine($"  - Erreurs: {syncResult.ErrorCount}");
            Console.WriteLine($"⏱️ Temps d'exécution: {stopwatch.ElapsedMilliseconds}ms");

            _logger.LogInformation($"Synchronisation terminée: {syncResult.TotalProcessed} articles traités en {stopwatch.ElapsedMilliseconds}ms");

            Console.WriteLine("\nAppuyez sur une touche pour fermer...");
            Console.ReadKey();
        }

        private static void SetupConfiguration()
        {
            _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
        }

        private static void SetupLogging()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .AddFile("logs/dynamics_sync.log");
            });

            _logger = loggerFactory.CreateLogger<Program>();
        }

        private static void SetupHttpClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        private static async Task<string> GetAccessTokenAsync()
        {
            try
            {
                var authUrl = $"https://login.microsoftonline.com/{_configuration["TenantId"]}/oauth2/token";

                _logger.LogInformation($"Demande de token à: {authUrl}");

                var formParams = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _configuration["ClientId"]),
                    new KeyValuePair<string, string>("client_secret", _configuration["ClientSecret"]),
                    new KeyValuePair<string, string>("resource", _configuration["Resource"])
                };

                var content = new FormUrlEncodedContent(formParams);
                var response = await _httpClient.PostAsync(authUrl, content);
                var responseText = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Réponse d'authentification: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Erreur d'authentification: {responseText}");
                    return null;
                }

                var tokenData = JsonSerializer.Deserialize<TokenResponse>(responseText);

                if (string.IsNullOrEmpty(tokenData?.access_token))
                {
                    _logger.LogError("Token d'accès vide dans la réponse");
                    return null;
                }

                _logger.LogInformation("Token d'accès obtenu avec succès");
                return tokenData.access_token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'obtention du token d'accès");
                return null;
            }
        }

        private static async Task<SyncResult> FetchAndSyncArticlesAsync(string token, string endpoint)
        {
            var result = new SyncResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var url = $"{_configuration["Resource"]}{endpoint}";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                _logger.LogInformation($"Appel API GET: {url}");
                Console.WriteLine($"Appel API: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"Erreur API: {response.StatusCode}");
                    LogSyncError(endpoint, $"Erreur API: {response.StatusCode}", stopwatch.ElapsedMilliseconds);
                    return result;
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"JSON reçu: {jsonContent.Length} caractères");
                Console.WriteLine($"✓ Données reçues: {jsonContent.Length} caractères");

                var jsonDocument = JsonDocument.Parse(jsonContent);

                if (!jsonDocument.RootElement.TryGetProperty("value", out var articlesArray))
                {
                    _logger.LogWarning("Propriété 'value' non trouvée dans la réponse JSON");
                    Console.WriteLine("Avertissement: Aucun article trouvé dans la réponse");
                    return result;
                }

                var articles = articlesArray.EnumerateArray().ToArray();
                Console.WriteLine($"✓ {articles.Length} articles trouvés dans l'API");

                // Synchronisation intelligente des articles
                result = await SyncArticlesWithDatabaseAsync(articles, endpoint);

                stopwatch.Stop();

                string status = result.ErrorCount == 0 ? "SUCCESS" : (result.ErrorCount < result.TotalProcessed ? "WARNING" : "ERROR");
                LogSyncResult(endpoint, status, result, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération depuis {endpoint}");
                Console.WriteLine($"Erreur: {ex.Message}");
                LogSyncError(endpoint, ex.Message, stopwatch.ElapsedMilliseconds);
                return result;
            }
        }

        private static async Task<SyncResult> SyncArticlesWithDatabaseAsync(JsonElement[] articles, string endpoint)
        {
            var result = new SyncResult();

            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // ÉTAPE 1 : Vider complètement la table articles_raw
                    Console.WriteLine("Suppression de tous les articles existants...");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "TRUNCATE TABLE articles_raw";
                        await command.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine("✓ Table articles_raw vidée");

                    // ÉTAPE 2 : Insérer tous les articles de l'API
                    Console.WriteLine("Insertion des nouveaux articles...");

                    foreach (var article in articles)
                    {
                        try
                        {
                            result.TotalProcessed++;

                            // Extraction de l'ItemId
                            string itemId = article.TryGetProperty("ItemId", out var itemIdProp)
                                ? itemIdProp.GetString() ?? "UNKNOWN"
                                : "UNKNOWN";

                            string articleJson = article.GetRawText();
                            string currentHash = CalculateHash(articleJson);

                            // Insertion de tous les articles comme "nouveaux"
                            await InsertNewArticleAsync(connection, itemId, articleJson, currentHash, endpoint);
                            result.NewArticles++;

                            // Affichage du progrès
                            if (result.TotalProcessed % 50 == 0)
                            {
                                Console.Write($"\rInsertion: {result.TotalProcessed}/{articles.Length} articles");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.ErrorCount++;
                            _logger.LogError(ex, $"Erreur lors de l'insertion de l'article {result.TotalProcessed}");
                        }
                    }

                    Console.WriteLine(); // Nouvelle ligne après le compteur
                    Console.WriteLine($"✓ {result.NewArticles} articles insérés");
                }

                _logger.LogInformation($"Synchronisation complète terminée: {result.NewArticles} articles rechargés, {result.ErrorCount} erreurs");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation avec la base de données");
                throw;
            }
        }

        private static async Task<HashSet<string>> GetExistingArticleIdsAsync(MySqlConnection connection)
        {
            var itemIds = new HashSet<string>();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT item_id FROM articles_raw WHERE item_id IS NOT NULL";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var itemId = reader.GetString(0);
                            itemIds.Add(itemId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des ItemIds existants");
            }

            return itemIds;
        }

        private static async Task<string> GetArticleHashAsync(MySqlConnection connection, string itemId)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT content_hash FROM articles_raw WHERE item_id = @item_id";
                    command.Parameters.AddWithValue("@item_id", itemId);

                    var result = await command.ExecuteScalarAsync();
                    return result?.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération du hash pour {itemId}");
                return null;
            }
        }

        private static async Task ForceUpdateArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    UPDATE articles_raw 
                    SET json_data = @json_data, 
                        content_hash = @hash, 
                        last_updated_at = NOW()
                    WHERE item_id = @item_id";

                command.Parameters.AddWithValue("@json_data", jsonData);
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@item_id", itemId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task TouchArticleAsync(MySqlConnection connection, string itemId)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE articles_raw 
                        SET last_updated_at = NOW()
                        WHERE item_id = @item_id";

                    command.Parameters.AddWithValue("@item_id", itemId);
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du touch de l'article {itemId}");
            }
        }

        private static async Task<Dictionary<string, string>> GetExistingArticleHashesAsync(MySqlConnection connection)
        {
            var hashes = new Dictionary<string, string>();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT item_id, content_hash FROM articles_raw WHERE item_id IS NOT NULL";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var itemId = reader.GetString(0); // index 0 pour item_id
                            var hash = reader.GetString(1);   // index 1 pour content_hash
                            hashes[itemId] = hash;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des hash existants");
            }

            return hashes;
        }

        private static async Task InsertNewArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO articles_raw (json_data, api_endpoint, content_hash, first_seen_at, last_updated_at) 
                    VALUES (@json_data, @endpoint, @hash, NOW(), NOW())";

                command.Parameters.AddWithValue("@json_data", jsonData);
                command.Parameters.AddWithValue("@endpoint", endpoint);
                command.Parameters.AddWithValue("@hash", hash);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task UpdateExistingArticleAsync(MySqlConnection connection, string itemId, string jsonData, string hash, string endpoint)
        {
            using (var command = connection.CreateCommand())
            {
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
        }

        private static string CalculateHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(hashBytes);
            }
        }

        private static bool CreateDatabaseIfNotExists()
        {
            try
            {
                var connectionStringBuilder = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"]
                };

                using (var connection = new MySqlConnection(connectionStringBuilder.ConnectionString))
                {
                    connection.Open();
                    _logger.LogInformation("✓ Connexion MySQL établie");

                    // Création de la base de données
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_configuration["Database:Name"]}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
                        command.ExecuteNonQuery();
                    }

                    // Utilisation de la base de données
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"USE `{_configuration["Database:Name"]}`";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table pour les articles avec colonnes de suivi
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS articles_raw (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                json_data JSON NOT NULL,
                                content_hash VARCHAR(255) NOT NULL,
                                api_endpoint VARCHAR(255) DEFAULT 'BRINT34ReleasedProducts',
                                item_id VARCHAR(50) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(json_data, '$.ItemId'))) STORED,
                                first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                update_count INT DEFAULT 0,
                                INDEX idx_item_id (item_id),
                                INDEX idx_content_hash (content_hash),
                                INDEX idx_api_endpoint (api_endpoint),
                                INDEX idx_last_updated (last_updated_at),
                                UNIQUE KEY unique_item_id (item_id)
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des logs
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS sync_logs (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                endpoint VARCHAR(255),
                                status ENUM('SUCCESS', 'ERROR', 'WARNING') DEFAULT 'SUCCESS',
                                articles_count INT DEFAULT 0,
                                message TEXT,
                                execution_time_ms INT,
                                sync_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table d'historique des modifications
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS articles_history (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                item_id VARCHAR(50),
                                old_json_data JSON,
                                new_json_data JSON,
                                change_type ENUM('INSERT', 'UPDATE') DEFAULT 'UPDATE',
                                changed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                INDEX idx_item_id (item_id),
                                INDEX idx_changed_at (changed_at)
                            )";
                        command.ExecuteNonQuery();
                    }
                }

                _logger.LogInformation("✓ Base de données et tables créées/vérifiées");
                Console.WriteLine("✓ Base de données initialisée");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création de la base de données");
                Console.WriteLine($"Erreur DB: {ex.Message}");
                return false;
            }
        }

        private static void LogSyncResult(string endpoint, string status, SyncResult result, long executionTimeMs)
        {
            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            INSERT INTO sync_logs (
                                endpoint, 
                                status, 
                                total_articles_processed, 
                                new_articles, 
                                updated_articles, 
                                unchanged_articles, 
                                error_count, 
                                message, 
                                execution_time_ms
                            ) VALUES (
                                @endpoint, 
                                @status, 
                                @total_articles_processed, 
                                @new_articles, 
                                @updated_articles, 
                                @unchanged_articles, 
                                @error_count, 
                                @message, 
                                @execution_time
                            )";

                        command.Parameters.AddWithValue("@endpoint", endpoint);
                        command.Parameters.AddWithValue("@status", status);
                        command.Parameters.AddWithValue("@total_articles_processed", result.TotalProcessed);
                        command.Parameters.AddWithValue("@new_articles", result.NewArticles);
                        command.Parameters.AddWithValue("@updated_articles", result.UpdatedArticles);
                        command.Parameters.AddWithValue("@unchanged_articles", result.UnchangedArticles);
                        command.Parameters.AddWithValue("@error_count", result.ErrorCount);
                        command.Parameters.AddWithValue("@message", $"Synchronisation - Nouveaux: {result.NewArticles}, MàJ: {result.UpdatedArticles}, Inchangés: {result.UnchangedArticles}");
                        command.Parameters.AddWithValue("@execution_time", executionTimeMs);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log détaillé");
            }
        }

        // Méthode de logging simple pour les erreurs
        private static void LogSyncError(string endpoint, string message, long executionTimeMs)
        {
            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                       INSERT INTO sync_logs (
                           endpoint, 
                           status, 
                           total_articles_processed, 
                           new_articles, 
                           updated_articles, 
                           unchanged_articles, 
                           error_count, 
                           message, 
                           execution_time_ms
                       ) VALUES (
                                @endpoint, 
                                'ERROR', 
                                0, 
                                0, 
                                0, 
                                0, 
                                1, 
                                @message, 
                                @execution_time
                            )";

                        command.Parameters.AddWithValue("@endpoint", endpoint);
                        command.Parameters.AddWithValue("@message", message);
                        command.Parameters.AddWithValue("@execution_time", executionTimeMs);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log d'erreur");
            }
        }

        private static void LogSyncResult(string endpoint, string status, int articlesCount, string message, long executionTimeMs)
        {
            try
            {
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        // Adaptation pour la structure existante de sync_logs
                        command.CommandText = @"
                            INSERT INTO sync_logs (endpoint, status, message, execution_time_ms) 
                            VALUES (@endpoint, @status, @message, @execution_time)";

                        command.Parameters.AddWithValue("@endpoint", endpoint);
                        command.Parameters.AddWithValue("@status", status);
                        command.Parameters.AddWithValue("@message", $"Articles: {articlesCount} - {message}");
                        command.Parameters.AddWithValue("@execution_time", executionTimeMs);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log");
            }
        }
    }

    // Classes de support
    public class SyncResult
    {
        public int TotalProcessed { get; set; } = 0;
        public int NewArticles { get; set; } = 0;
        public int UpdatedArticles { get; set; } = 0;
        public int UnchangedArticles { get; set; } = 0;
        public int ErrorCount { get; set; } = 0;
    }

    public class TokenResponse
    {
        public string token_type { get; set; }
        public string scope { get; set; }
        public string expires_in { get; set; }
        public string ext_expires_in { get; set; }
        public string expires_on { get; set; }
        public string not_before { get; set; }
        public string resource { get; set; }
        public string access_token { get; set; }
    }
}