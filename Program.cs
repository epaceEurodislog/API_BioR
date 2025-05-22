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

namespace DynamicsApiToDatabase
{
    class Program
    {
        private static ILogger<Program> _logger;
        private static IConfiguration _configuration;
        private static HttpClient _httpClient;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Récupération des articles Dynamics (JSON brut) ===");

            // Configuration
            SetupConfiguration();
            SetupLogging();
            SetupHttpClient();

            _logger.LogInformation("Démarrage de la récupération des articles");

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

            _logger.LogInformation($"Début de la récupération depuis: {articlesEndpoint}");
            Console.WriteLine($"Récupération des articles depuis l'API...");

            // Récupération et stockage du JSON brut
            bool success = await FetchAndStoreArticlesAsync(token, articlesEndpoint);

            stopwatch.Stop();

            if (success)
            {
                _logger.LogInformation($"Récupération terminée en {stopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine($"✓ Récupération terminée en {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                Console.WriteLine("❌ Erreur lors de la récupération");
            }

            Console.WriteLine("Appuyez sur une touche pour fermer...");
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
                // URL exacte comme dans Postman
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

        private static async Task<bool> FetchAndStoreArticlesAsync(string token, string endpoint)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Construction de l'URL exacte comme dans Postman
                var url = $"{_configuration["Resource"]}{endpoint}";

                // Configuration de l'autorisation
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                _logger.LogInformation($"Appel API GET: {url}");
                Console.WriteLine($"Appel API: {url}");

                // Appel à l'API
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"Erreur API: {response.StatusCode}");
                    LogSyncResult(endpoint, "ERROR", 0, $"Erreur API: {response.StatusCode}", stopwatch.ElapsedMilliseconds);
                    return false;
                }

                // Récupération du JSON brut
                var jsonContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"JSON reçu: {jsonContent.Length} caractères");
                Console.WriteLine($"✓ Données reçues: {jsonContent.Length} caractères");

                // Validation que c'est du JSON valide
                JsonDocument.Parse(jsonContent);

                // Stockage en base de données
                int articlesCount = StoreRawJsonInDatabase(jsonContent, endpoint);

                stopwatch.Stop();

                LogSyncResult(endpoint, "SUCCESS", articlesCount, $"Récupération réussie", stopwatch.ElapsedMilliseconds);

                Console.WriteLine($"✓ {articlesCount} articles stockés en base");
                return true;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Erreur: JSON invalide reçu de l'API");
                Console.WriteLine("Erreur: JSON invalide reçu");
                LogSyncResult(endpoint, "ERROR", 0, $"JSON invalide: {ex.Message}", stopwatch.ElapsedMilliseconds);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération depuis {endpoint}");
                Console.WriteLine($"Erreur: {ex.Message}");
                LogSyncResult(endpoint, "ERROR", 0, ex.Message, stopwatch.ElapsedMilliseconds);
                return false;
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

                    // Création de la table pour le JSON brut
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS articles_raw (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                json_data JSON NOT NULL,
                                retrieved_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                api_endpoint VARCHAR(255) DEFAULT 'BRINT34ReleasedProducts',
                                INDEX idx_retrieved_at (retrieved_at),
                                INDEX idx_api_endpoint (api_endpoint)
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
                                status ENUM('SUCCESS', 'ERROR') DEFAULT 'SUCCESS',
                                articles_count INT DEFAULT 0,
                                message TEXT,
                                execution_time_ms INT,
                                sync_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP
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

        private static int StoreRawJsonInDatabase(string jsonContent, string endpoint)
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

                // Parse du JSON pour compter les articles
                var jsonDoc = JsonDocument.Parse(jsonContent);
                int articlesCount = 0;

                if (jsonDoc.RootElement.TryGetProperty("value", out var valueElement))
                {
                    articlesCount = valueElement.GetArrayLength();
                }

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Insertion du JSON brut complet
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            INSERT INTO articles_raw (json_data, api_endpoint, retrieved_at) 
                            VALUES (@json_data, @endpoint, NOW())";

                        command.Parameters.AddWithValue("@json_data", jsonContent);
                        command.Parameters.AddWithValue("@endpoint", endpoint);
                        command.ExecuteNonQuery();
                    }
                }

                _logger.LogInformation($"JSON brut stocké en base. {articlesCount} articles dans la réponse");
                return articlesCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du stockage en base de données");
                throw;
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
                        command.CommandText = @"
                            INSERT INTO sync_logs (endpoint, status, articles_count, message, execution_time_ms) 
                            VALUES (@endpoint, @status, @articles_count, @message, @execution_time)";

                        command.Parameters.AddWithValue("@endpoint", endpoint);
                        command.Parameters.AddWithValue("@status", status);
                        command.Parameters.AddWithValue("@articles_count", articlesCount);
                        command.Parameters.AddWithValue("@message", message);
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

    // Classe pour désérialiser la réponse du token
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