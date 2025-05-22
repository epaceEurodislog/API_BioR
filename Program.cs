// Fichier: Program.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
            // Configuration
            SetupConfiguration();
            SetupLogging();
            SetupHttpClient();

            _logger.LogInformation("Démarrage du transfert de données");

            // Création de la base de données et des tables si nécessaire
            if (!CreateDatabaseIfNotExists())
            {
                _logger.LogError("Impossible de créer ou d'accéder à la base de données. Arrêt du programme.");
                return;
            }

            // Obtention du token d'accès
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Impossible d'obtenir un token d'accès. Arrêt du programme.");
                return;
            }

            // Liste des endpoints disponibles
            var endpoints = new List<string>
            {
                "data/BRINT32TransferOrderTables",
                "data/BRINT32PurchOrderTables",
                "data/BRINT32ReturnOrderTables",
                "data/BRINT34ReleasedProducts",
                "data/BRPackingSlipInterfaces",
                "data/BRPackingSlipValidationInterfaces",
                "data/ItemArrivalJournalHeadersV2",
                "data/ItemArrivalJournalLinesV2"
            };

            int totalItems = 0;

            // Traitement de chaque endpoint
            foreach (var endpoint in endpoints)
            {
                _logger.LogInformation($"Traitement de l'endpoint: {endpoint}");

                // Récupération des données
                var data = await FetchDataFromApiAsync(token, endpoint);
                if (data == null)
                {
                    _logger.LogWarning($"Aucune donnée récupérée depuis {endpoint}. Passage à l'endpoint suivant.");
                    continue;
                }

                // Insertion des données dans la base de données
                int inserted = InsertDataIntoDb(data, endpoint);
                totalItems += inserted;

                _logger.LogInformation($"Traitement terminé pour {endpoint}: {inserted} articles traités");
            }

            _logger.LogInformation($"Transfert de données terminé. Total: {totalItems} articles traités.");
        }

        private static void SetupConfiguration()
        {
            // Chargement de la configuration depuis appsettings.json et variables d'environnement
            _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
        }

        private static void SetupLogging()
        {
            // Configuration du système de logging
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .AddFile("api_transfer.log");
            });

            _logger = loggerFactory.CreateLogger<Program>();
        }

        private static void SetupHttpClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private static async Task<string> GetAccessTokenAsync()
        {
            try
            {
                var authUrl = $"https://login.microsoftonline.com/{_configuration["TenantId"]}/oauth2/token";
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _configuration["ClientId"]),
                    new KeyValuePair<string, string>("client_secret", _configuration["ClientSecret"]),
                    new KeyValuePair<string, string>("resource", _configuration["Resource"])
                });

                var response = await _httpClient.PostAsync(authUrl, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var tokenData = JsonSerializer.Deserialize<TokenResponse>(responseJson);

                return tokenData?.access_token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'obtention du token d'accès");
                return null;
            }
        }

        private static async Task<JsonDocument> FetchDataFromApiAsync(string token, string endpoint)
        {
            try
            {
                var url = $"{_configuration["Resource"]}{endpoint}";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des données depuis {endpoint}");
                return null;
            }
        }

        private static bool CreateDatabaseIfNotExists()
        {
            try
            {
                // Connexion sans spécifier la base de données
                var connectionStringBuilder = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"]
                };

                using (var connection = new MySqlConnection(connectionStringBuilder.ConnectionString))
                {
                    connection.Open();

                    // Création de la base de données si elle n'existe pas
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_configuration["Database:Name"]}`";
                        command.ExecuteNonQuery();
                    }

                    // Utilisation de la base de données
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"USE `{_configuration["Database:Name"]}`";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des articles
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS articles (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                item_number VARCHAR(100),
                                api_endpoint VARCHAR(255),
                                json_data JSON,
                                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des logs d'importation
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS import_logs (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                api_endpoint VARCHAR(255),
                                items_count INT,
                                status VARCHAR(50),
                                message TEXT,
                                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                            )";
                        command.ExecuteNonQuery();
                    }
                }

                _logger.LogInformation("Base de données et tables créées ou vérifiées avec succès");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création de la base de données");
                return false;
            }
        }

        private static int InsertDataIntoDb(JsonDocument data, string endpoint)
        {
            try
            {
                // Vérification de la structure des données
                if (!data.RootElement.TryGetProperty("value", out var valueElement))
                {
                    _logger.LogWarning($"Aucune donnée à insérer depuis {endpoint}");
                    return 0;
                }

                var items = valueElement.EnumerateArray();
                if (!items.Any())
                {
                    _logger.LogWarning($"Aucun article trouvé dans les données de {endpoint}");
                    return 0;
                }

                // Construction de la chaîne de connexion complète
                var connectionString = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"],
                    Database = _configuration["Database:Name"]
                }.ConnectionString;

                int insertedCount = 0;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Pour chaque article dans les données
                    foreach (var item in valueElement.EnumerateArray())
                    {
                        // Extraction de l'identifiant de l'article (si disponible)
                        string itemNumber = null;
                        if (item.TryGetProperty("ItemNumber", out var itemNumberProp))
                        {
                            itemNumber = itemNumberProp.GetString();
                        }
                        else if (item.TryGetProperty("itemId", out var itemIdProp))
                        {
                            itemNumber = itemIdProp.GetString();
                        }
                        else if (item.TryGetProperty("InventLocationId", out var locationIdProp))
                        {
                            itemNumber = locationIdProp.GetString();
                        }

                        // Conversion de l'article en JSON
                        string jsonData = item.GetRawText();

                        // Vérification si l'article existe déjà
                        using (var checkCommand = connection.CreateCommand())
                        {
                            checkCommand.CommandText = "SELECT id FROM articles WHERE item_number = @itemNumber AND api_endpoint = @endpoint";
                            checkCommand.Parameters.AddWithValue("@itemNumber", itemNumber ?? (object)DBNull.Value);
                            checkCommand.Parameters.AddWithValue("@endpoint", endpoint);

                            var existingItemId = checkCommand.ExecuteScalar();

                            if (existingItemId != null)
                            {
                                // Mise à jour de l'article existant
                                using (var updateCommand = connection.CreateCommand())
                                {
                                    updateCommand.CommandText = "UPDATE articles SET json_data = @jsonData WHERE id = @id";
                                    updateCommand.Parameters.AddWithValue("@jsonData", jsonData);
                                    updateCommand.Parameters.AddWithValue("@id", existingItemId);
                                    updateCommand.ExecuteNonQuery();
                                }
                            }
                            else
                            {
                                // Insertion d'un nouvel article
                                using (var insertCommand = connection.CreateCommand())
                                {
                                    insertCommand.CommandText = "INSERT INTO articles (item_number, api_endpoint, json_data) VALUES (@itemNumber, @endpoint, @jsonData)";
                                    insertCommand.Parameters.AddWithValue("@itemNumber", itemNumber ?? (object)DBNull.Value);
                                    insertCommand.Parameters.AddWithValue("@endpoint", endpoint);
                                    insertCommand.Parameters.AddWithValue("@jsonData", jsonData);
                                    insertCommand.ExecuteNonQuery();
                                }
                            }
                        }

                        insertedCount++;
                    }

                    // Enregistrement du log d'importation
                    using (var logCommand = connection.CreateCommand())
                    {
                        logCommand.CommandText = "INSERT INTO import_logs (api_endpoint, items_count, status, message) VALUES (@endpoint, @count, @status, @message)";
                        logCommand.Parameters.AddWithValue("@endpoint", endpoint);
                        logCommand.Parameters.AddWithValue("@count", insertedCount);
                        logCommand.Parameters.AddWithValue("@status", "SUCCESS");
                        logCommand.Parameters.AddWithValue("@message", $"Importation réussie de {insertedCount} articles");
                        logCommand.ExecuteNonQuery();
                    }
                }

                _logger.LogInformation($"Insertion réussie de {insertedCount} articles depuis {endpoint}");
                return insertedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de l'insertion des données depuis {endpoint}");

                // Tentative d'enregistrement du log d'erreur
                try
                {
                    var connectionString = new MySqlConnectionStringBuilder
                    {
                        Server = _configuration["Database:Host"],
                        UserID = _configuration["Database:User"],
                        Password = _configuration["Database:Password"],
                        Database = _configuration["Database:Name"]
                    }.ConnectionString;

                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "INSERT INTO import_logs (api_endpoint, items_count, status, message) VALUES (@endpoint, @count, @status, @message)";
                            command.Parameters.AddWithValue("@endpoint", endpoint);
                            command.Parameters.AddWithValue("@count", 0);
                            command.Parameters.AddWithValue("@status", "ERROR");
                            command.Parameters.AddWithValue("@message", ex.Message);
                            command.ExecuteNonQuery();
                        }
                    }
                }
                catch
                {
                    // En cas d'erreur lors de l'enregistrement du log d'erreur
                }

                return 0;
            }
        }
    }

    // Classe pour désérialiser la réponse du token
    class TokenResponse
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