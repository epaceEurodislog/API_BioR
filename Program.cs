// Fichier: Program.cs - CODE COMPLET FINAL avec gestion des lignes multiples et fonctionnalités de suppression
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Data;

namespace DynamicsApiToDatabase
{
    class Program
    {
        private static ILogger<Program> _logger;
        private static IConfiguration _configuration;
        private static HttpClient _httpClient;

        /// <summary>
        /// Extrait une valeur depuis un JsonElement en gérant tous les types
        /// </summary>
        private static string GetFlexibleStringValue(JsonElement element, string fieldName)
        {
            if (!element.TryGetProperty(fieldName, out var property))
            {
                return "UNKNOWN";
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? "UNKNOWN",
                JsonValueKind.Number => property.GetDecimal().ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "NULL",
                _ => property.ToString() ?? "UNKNOWN"
            };
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Synchronisation intelligente des articles Dynamics avec gestion des lignes multiples ===");

            // Configuration
            SetupConfiguration();
            SetupLogging();
            SetupHttpClient();

            // NOUVEAU : Vérification des arguments pour le nettoyage
            if (args.Length > 0 && args[0].ToLower() == "--cleanup")
            {
                await ShowCleanupMenuAsync();
                return; // Sortir après le nettoyage
            }

            _logger.LogInformation("Démarrage de la synchronisation des articles avec gestion des lignes multiples");

            // Création de la base de données et des tables si nécessaire
            if (!await CreateDatabaseIfNotExistsAsync())
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

            Console.WriteLine("✅ Authentification réussie");

            try
            {
                // ========== SYNCHRONISATION DES ARTICLES ==========
                Console.WriteLine("\n🔄 SYNCHRONISATION DES ARTICLES");
                Console.WriteLine("================================");

                var articlesResult = await SyncArticlesWithDatabaseAsync(token);
                await LogSyncResultAsync("data/BRINT34ReleasedProducts", "SUCCESS", articlesResult.TotalProcessed, "", articlesResult);

                // ========== SYNCHRONISATION DES COMMANDES ==========
                Console.WriteLine("\n🔄 SYNCHRONISATION DES COMMANDES");
                Console.WriteLine("=================================");

                var orderEndpoints = new[]
                {
                    new OrderEndpoint
                    {
                        Name = "ReturnOrders",
                        Endpoint = "data/BRINT32ReturnOrderTables",
                        TableName = "return_orders_raw",
                        PrimaryKeyField = "ReturnOrderId",
                        LineNumberField = "LineNumber",
                        DisplayName = "Commandes de Retour"
                    },
                    new OrderEndpoint
                    {
                        Name = "PurchOrders",
                        Endpoint = "data/BRINT32PurchOrderTables",
                        TableName = "purch_orders_raw",
                        PrimaryKeyField = "PurchaseOrderId",
                        LineNumberField = "LineNumber",
                        DisplayName = "Commandes d'Achat"
                    },
                    new OrderEndpoint
                    {
                        Name = "TransferOrders",
                        Endpoint = "data/BRINT32TransferOrderTables",
                        TableName = "transfer_orders_raw",
                        PrimaryKeyField = "TransferId",
                        LineNumberField = "LineNumber",
                        DisplayName = "Ordres de Transfert"
                    }
                };

                foreach (var orderEndpoint in orderEndpoints)
                {
                    Console.WriteLine($"\n--- {orderEndpoint.DisplayName} ---");
                    var orderResult = await SyncOrderDataAsync(token, orderEndpoint);
                    await LogSyncResultAsync(orderEndpoint.Endpoint, "SUCCESS", orderResult.TotalProcessed, "", null, orderResult);
                }

                Console.WriteLine("\n🎉 SYNCHRONISATION COMPLÈTE TERMINÉE !");
                _logger.LogInformation("Synchronisation complète terminée avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation complète");
                Console.WriteLine($"❌ Erreur : {ex.Message}");
                await LogSyncResultAsync("GLOBAL", "ERROR", 0, ex.Message, null);
            }
        }

        // ========================================
        // NOUVELLES MÉTHODES DE SUPPRESSION SÉCURISÉE
        // ========================================

        /// <summary>
        /// Supprime TOUTES les données de test de la base de données avec confirmation multiple
        /// ⚠️ ATTENTION : Cette méthode est DESTRUCTIVE et irréversible !
        /// </summary>
        /// <param name="forceConfirmation">Texte de confirmation exact requis</param>
        /// <returns>True si la suppression a été effectuée, False sinon</returns>
        private static async Task<bool> ClearAllTestDataAsync(string forceConfirmation = null)
        {
            const string REQUIRED_CONFIRMATION = "SUPPRIMER_TOUTES_DONNEES_TEST";

            try
            {
                // SÉCURITÉ 1 : Vérification de l'environnement
                var resource = _configuration["Resource"];
                if (!resource.Contains("sandbox") && !resource.Contains("uat") && !resource.Contains("test"))
                {
                    Console.WriteLine("❌ ERREUR : Cette fonction ne peut être utilisée qu'en environnement de test !");
                    Console.WriteLine($"   Environnement détecté : {resource}");
                    return false;
                }

                // SÉCURITÉ 2 : Confirmation obligatoire
                if (forceConfirmation != REQUIRED_CONFIRMATION)
                {
                    Console.WriteLine("⚠️  ATTENTION : Vous allez supprimer TOUTES les données de test !");
                    Console.WriteLine("   Cette action est IRRÉVERSIBLE !");
                    Console.WriteLine($"   Pour confirmer, tapez exactement : {REQUIRED_CONFIRMATION}");
                    Console.Write("   Confirmation : ");

                    var userInput = Console.ReadLine()?.Trim();
                    if (userInput != REQUIRED_CONFIRMATION)
                    {
                        Console.WriteLine("❌ Suppression annulée - confirmation incorrecte");
                        return false;
                    }
                }

                // SÉCURITÉ 3 : Double confirmation
                Console.WriteLine("⚠️  DERNIÈRE CHANCE ! Êtes-vous absolument sûr ? (oui/non)");
                var finalConfirm = Console.ReadLine()?.Trim().ToLower();
                if (finalConfirm != "oui")
                {
                    Console.WriteLine("❌ Suppression annulée par l'utilisateur");
                    return false;
                }

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

                    // Compter les données avant suppression
                    var dataCounts = await GetDataCountsAsync(connection);

                    Console.WriteLine("\n📊 Données à supprimer :");
                    foreach (var count in dataCounts)
                    {
                        Console.WriteLine($"   • {count.Key}: {count.Value:N0} enregistrements");
                    }

                    Console.WriteLine("\n🔄 Suppression en cours...");

                    // Transaction pour assurer la cohérence
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Désactiver les contraintes de clés étrangères temporairement
                            await ExecuteCommandAsync(connection, transaction, "SET FOREIGN_KEY_CHECKS = 0");

                            // 1. Supprimer les données des tables principales (ordre important)
                            var tablesToClear = new[]
                            {
                                "articles_raw",
                                "return_orders_raw",
                                "purch_orders_raw",
                                "transfer_orders_raw",
                                "article_tags",
                                "article_changes",
                                "sync_logs"
                            };

                            foreach (var table in tablesToClear)
                            {
                                var deletedCount = await ClearTableAsync(connection, transaction, table);
                                Console.WriteLine($"   ✓ {table}: {deletedCount:N0} enregistrements supprimés");
                            }

                            // 2. Réinitialiser les AUTO_INCREMENT
                            foreach (var table in tablesToClear)
                            {
                                await ExecuteCommandAsync(connection, transaction, $"ALTER TABLE {table} AUTO_INCREMENT = 1");
                            }

                            // Réactiver les contraintes de clés étrangères
                            await ExecuteCommandAsync(connection, transaction, "SET FOREIGN_KEY_CHECKS = 1");

                            // Valider la transaction
                            transaction.Commit();

                            Console.WriteLine("\n✅ SUPPRESSION TERMINÉE !");
                            Console.WriteLine("   Toutes les données de test ont été supprimées");
                            Console.WriteLine("   Les compteurs AUTO_INCREMENT ont été réinitialisés");

                            // Log de sécurité
                            _logger.LogWarning("SUPPRESSION COMPLÈTE DES DONNÉES DE TEST effectuée par l'utilisateur");

                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Console.WriteLine($"❌ Erreur lors de la suppression : {ex.Message}");
                            _logger.LogError(ex, "Erreur lors de la suppression des données de test");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur critique : {ex.Message}");
                _logger.LogError(ex, "Erreur critique lors de la suppression des données");
                return false;
            }
        }

        /// <summary>
        /// Supprime uniquement les données des articles (plus rapide pour les tests fréquents)
        /// </summary>
        private static async Task<bool> ClearArticlesOnlyAsync()
        {
            try
            {
                Console.WriteLine("🔄 Suppression des articles uniquement...");

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

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            var articlesDeleted = await ClearTableAsync(connection, transaction, "articles_raw");
                            var tagsDeleted = await ClearTableAsync(connection, transaction, "article_tags");
                            var changesDeleted = await ClearTableAsync(connection, transaction, "article_changes");

                            // Réinitialiser les AUTO_INCREMENT
                            await ExecuteCommandAsync(connection, transaction, "ALTER TABLE articles_raw AUTO_INCREMENT = 1");
                            await ExecuteCommandAsync(connection, transaction, "ALTER TABLE article_tags AUTO_INCREMENT = 1");
                            await ExecuteCommandAsync(connection, transaction, "ALTER TABLE article_changes AUTO_INCREMENT = 1");

                            transaction.Commit();

                            Console.WriteLine($"✅ Articles supprimés : {articlesDeleted:N0}");
                            Console.WriteLine($"✅ Tags supprimés : {tagsDeleted:N0}");
                            Console.WriteLine($"✅ Changements supprimés : {changesDeleted:N0}");

                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur : {ex.Message}");
                _logger.LogError(ex, "Erreur lors de la suppression des articles");
                return false;
            }
        }

        /// <summary>
        /// Supprime uniquement les données de commandes
        /// </summary>
        private static async Task<bool> ClearOrdersOnlyAsync()
        {
            try
            {
                Console.WriteLine("🔄 Suppression des commandes uniquement...");

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

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            var returnOrdersDeleted = await ClearTableAsync(connection, transaction, "return_orders_raw");
                            var purchOrdersDeleted = await ClearTableAsync(connection, transaction, "purch_orders_raw");
                            var transferOrdersDeleted = await ClearTableAsync(connection, transaction, "transfer_orders_raw");

                            // Réinitialiser les AUTO_INCREMENT
                            await ExecuteCommandAsync(connection, transaction, "ALTER TABLE return_orders_raw AUTO_INCREMENT = 1");
                            await ExecuteCommandAsync(connection, transaction, "ALTER TABLE purch_orders_raw AUTO_INCREMENT = 1");
                            await ExecuteCommandAsync(connection, transaction, "ALTER TABLE transfer_orders_raw AUTO_INCREMENT = 1");

                            transaction.Commit();

                            Console.WriteLine($"✅ Commandes de retour supprimées : {returnOrdersDeleted:N0}");
                            Console.WriteLine($"✅ Commandes d'achat supprimées : {purchOrdersDeleted:N0}");
                            Console.WriteLine($"✅ Ordres de transfert supprimés : {transferOrdersDeleted:N0}");

                            return true;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur : {ex.Message}");
                _logger.LogError(ex, "Erreur lors de la suppression des commandes");
                return false;
            }
        }

        /// <summary>
        /// Affiche un menu interactif pour choisir le type de suppression
        /// </summary>
        private static async Task ShowCleanupMenuAsync()
        {
            Console.WriteLine("\n🧹 MENU DE NETTOYAGE DES DONNÉES DE TEST");
            Console.WriteLine("=========================================");
            Console.WriteLine("1. Supprimer TOUTES les données (⚠️ DESTRUCTIF)");
            Console.WriteLine("2. Supprimer uniquement les articles");
            Console.WriteLine("3. Supprimer uniquement les commandes");
            Console.WriteLine("4. Afficher le nombre d'enregistrements");
            Console.WriteLine("0. Annuler");
            Console.WriteLine();
            Console.Write("Votre choix (0-4) : ");

            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    await ClearAllTestDataAsync();
                    break;

                case "2":
                    Console.WriteLine("⚠️ Confirmer la suppression des articles ? (oui/non)");
                    if (Console.ReadLine()?.Trim().ToLower() == "oui")
                    {
                        await ClearArticlesOnlyAsync();
                    }
                    break;

                case "3":
                    Console.WriteLine("⚠️ Confirmer la suppression des commandes ? (oui/non)");
                    if (Console.ReadLine()?.Trim().ToLower() == "oui")
                    {
                        await ClearOrdersOnlyAsync();
                    }
                    break;

                case "4":
                    await ShowDataCountsAsync();
                    break;

                case "0":
                    Console.WriteLine("Opération annulée");
                    break;

                default:
                    Console.WriteLine("Choix invalide");
                    break;
            }
        }

        // ========================================
        // MÉTHODES UTILITAIRES POUR LA SUPPRESSION
        // ========================================

        private static async Task<Dictionary<string, int>> GetDataCountsAsync(MySqlConnection connection)
        {
            var counts = new Dictionary<string, int>();

            var tables = new[]
            {
                "articles_raw",
                "return_orders_raw",
                "purch_orders_raw",
                "transfer_orders_raw",
                "article_tags",
                "article_changes",
                "sync_logs"
            };

            foreach (var table in tables)
            {
                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"SELECT COUNT(*) FROM {table}";
                        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        counts[table] = count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Impossible de compter les enregistrements de {table}: {ex.Message}");
                    counts[table] = 0;
                }
            }

            return counts;
        }

        private static async Task<int> ClearTableAsync(MySqlConnection connection, MySqlTransaction transaction, string tableName)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = $"SELECT COUNT(*) FROM {tableName}";
                    var countBefore = Convert.ToInt32(await command.ExecuteScalarAsync());

                    command.CommandText = $"DELETE FROM {tableName}";
                    await command.ExecuteNonQueryAsync();

                    return countBefore;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la suppression de {tableName}: {ex.Message}");
                return 0;
            }
        }

        private static async Task ExecuteCommandAsync(MySqlConnection connection, MySqlTransaction transaction, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task ShowDataCountsAsync()
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
                    var counts = await GetDataCountsAsync(connection);

                    Console.WriteLine("\n📊 ÉTAT ACTUEL DE LA BASE DE DONNÉES");
                    Console.WriteLine("=====================================");

                    var totalRecords = 0;
                    foreach (var count in counts)
                    {
                        Console.WriteLine($"• {count.Key.PadRight(20)}: {count.Value:N0} enregistrements");
                        totalRecords += count.Value;
                    }

                    Console.WriteLine($"\nTotal: {totalRecords:N0} enregistrements");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de l'affichage des compteurs : {ex.Message}");
            }
        }

        // ========================================
        // CONFIGURATION ET AUTHENTIFICATION (CODE EXISTANT)
        // ========================================

        private static void SetupConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        private static void SetupLogging()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Information);
            });

            _logger = loggerFactory.CreateLogger<Program>();
        }

        private static void SetupHttpClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        private static async Task<string> GetAccessTokenAsync()
        {
            try
            {
                var tokenUrl = $"https://login.microsoftonline.com/{_configuration["TenantId"]}/oauth2/token";

                var tokenRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _configuration["ClientId"]),
                    new KeyValuePair<string, string>("client_secret", _configuration["ClientSecret"]),
                    new KeyValuePair<string, string>("resource", _configuration["Resource"])
                });

                var response = await _httpClient.PostAsync(tokenUrl, tokenRequest);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var tokenResponse = JsonSerializer.Deserialize<JsonElement>(content);

                    return tokenResponse.GetProperty("access_token").GetString();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur d'authentification: {response.StatusCode} - {errorContent}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception lors de l'obtention du token d'accès");
                return null;
            }
        }

        // ========================================
        // GESTION DE LA BASE DE DONNÉES (CODE EXISTANT)
        // ========================================

        private static async Task<bool> CreateDatabaseIfNotExistsAsync()
        {
            try
            {
                // Connexion pour créer la base si elle n'existe pas
                var connectionStringWithoutDb = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"]
                }.ConnectionString;

                using (var connection = new MySqlConnection(connectionStringWithoutDb))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_configuration["Database:Name"]}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
                        command.ExecuteNonQuery();
                    }
                }

                // Connexion à la base spécifique
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

                    // Vérifier et mettre à jour la structure des tables existantes
                    await UpdateDatabaseStructureAsync(connection);

                    // Création de la table des articles (mise à jour si nécessaire)
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS articles_raw (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                json_data JSON NOT NULL,
                                content_hash VARCHAR(255) NOT NULL COMMENT 'Hash SHA256 du contenu pour détecter les modifications',
                                api_endpoint VARCHAR(255) DEFAULT 'BRINT34ReleasedProducts',
                                item_id VARCHAR(50) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(json_data, '$.ItemId'))) STORED,
                                first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP COMMENT 'Date de première apparition',
                                last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP COMMENT 'Date de dernière modification',
                                update_count INT DEFAULT 0 COMMENT 'Nombre de fois que l''article a été modifié',
                                is_deleted BOOLEAN DEFAULT FALSE COMMENT 'Marquage de suppression logique',
                                deleted_at TIMESTAMP NULL COMMENT 'Date de suppression',
                                UNIQUE KEY unique_item_id (item_id),
                                INDEX idx_item_id (item_id),
                                INDEX idx_content_hash (content_hash),
                                INDEX idx_api_endpoint (api_endpoint),
                                INDEX idx_last_updated (last_updated_at),
                                INDEX idx_first_seen (first_seen_at),
                                INDEX idx_is_deleted (is_deleted)
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des logs de synchronisation
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS sync_logs (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                endpoint VARCHAR(255) NOT NULL,
                                sync_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                status VARCHAR(50) NOT NULL,
                                total_articles_processed INT DEFAULT 0,
                                new_articles INT DEFAULT 0,
                                updated_articles INT DEFAULT 0,
                                unchanged_articles INT DEFAULT 0,
                                error_count INT DEFAULT 0,
                                execution_time_ms BIGINT DEFAULT 0,
                                error_message TEXT,
                                INDEX idx_endpoint (endpoint),
                                INDEX idx_sync_date (sync_date),
                                INDEX idx_status (status)
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des changements d'articles
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS article_changes (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                item_id VARCHAR(50) NOT NULL,
                                change_type ENUM('NEW', 'UPDATED', 'DELETED') NOT NULL,
                                old_hash VARCHAR(255),
                                new_hash VARCHAR(255),
                                changed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                changes_summary TEXT COMMENT 'Résumé des modifications détectées',
                                INDEX idx_item_id (item_id),
                                INDEX idx_changed_at (changed_at),
                                INDEX idx_change_type (change_type)
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création de la table des balises d'articles
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            CREATE TABLE IF NOT EXISTS article_tags (
                                id INT AUTO_INCREMENT PRIMARY KEY,
                                tag_name VARCHAR(191) NOT NULL,
                                data_type VARCHAR(50) NOT NULL,
                                first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                last_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                occurrence_count INT DEFAULT 0,
                                sample_value TEXT,
                                is_active BOOLEAN DEFAULT TRUE,
                                UNIQUE KEY unique_tag_name (tag_name),
                                INDEX idx_data_type (data_type),
                                INDEX idx_last_seen (last_seen_at)
                            )";
                        command.ExecuteNonQuery();
                    }

                    // Création des tables de commandes avec support des lignes multiples
                    CreateOrderTables(connection);
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

        private static async Task UpdateDatabaseStructureAsync(MySqlConnection connection)
        {
            try
            {
                Console.WriteLine("🔄 Vérification et mise à jour de la structure de la base...");

                // Mise à jour de la table articles_raw
                var hasIsDeleted = await CheckIfColumnExistsAsync(connection, "articles_raw", "is_deleted");
                if (!hasIsDeleted)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'is_deleted' à la table articles_raw");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE articles_raw ADD COLUMN is_deleted BOOLEAN DEFAULT FALSE COMMENT 'Marquage de suppression logique'";
                        command.ExecuteNonQuery();
                    }
                }

                var hasDeletedAt = await CheckIfColumnExistsAsync(connection, "articles_raw", "deleted_at");
                if (!hasDeletedAt)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'deleted_at' à la table articles_raw");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE articles_raw ADD COLUMN deleted_at TIMESTAMP NULL COMMENT 'Date de suppression'";
                        command.ExecuteNonQuery();
                    }
                }

                // Ajouter l'index si nécessaire
                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE articles_raw ADD INDEX idx_is_deleted (is_deleted)";
                        command.ExecuteNonQuery();
                    }
                }
                catch
                {
                    // L'index existe déjà, c'est normal
                }

                // Mise à jour de la table sync_logs
                var hasErrorMessage = await CheckIfColumnExistsAsync(connection, "sync_logs", "error_message");
                if (!hasErrorMessage)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'error_message' à la table sync_logs");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE sync_logs ADD COLUMN error_message TEXT";
                        command.ExecuteNonQuery();
                    }
                }

                var hasExecutionTime = await CheckIfColumnExistsAsync(connection, "sync_logs", "execution_time_ms");
                if (!hasExecutionTime)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'execution_time_ms' à la table sync_logs");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE sync_logs ADD COLUMN execution_time_ms BIGINT DEFAULT 0";
                        command.ExecuteNonQuery();
                    }
                }

                var hasNewArticles = await CheckIfColumnExistsAsync(connection, "sync_logs", "new_articles");
                if (!hasNewArticles)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'new_articles' à la table sync_logs");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE sync_logs ADD COLUMN new_articles INT DEFAULT 0";
                        command.ExecuteNonQuery();
                    }
                }

                var hasUpdatedArticles = await CheckIfColumnExistsAsync(connection, "sync_logs", "updated_articles");
                if (!hasUpdatedArticles)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'updated_articles' à la table sync_logs");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE sync_logs ADD COLUMN updated_articles INT DEFAULT 0";
                        command.ExecuteNonQuery();
                    }
                }

                var hasUnchangedArticles = await CheckIfColumnExistsAsync(connection, "sync_logs", "unchanged_articles");
                if (!hasUnchangedArticles)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'unchanged_articles' à la table sync_logs");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE sync_logs ADD COLUMN unchanged_articles INT DEFAULT 0";
                        command.ExecuteNonQuery();
                    }
                }

                var hasErrorCount = await CheckIfColumnExistsAsync(connection, "sync_logs", "error_count");
                if (!hasErrorCount)
                {
                    Console.WriteLine("  ➕ Ajout de la colonne 'error_count' à la table sync_logs");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "ALTER TABLE sync_logs ADD COLUMN error_count INT DEFAULT 0";
                        command.ExecuteNonQuery();
                    }
                }

                // Mise à jour des tables de commandes
                var orderTables = new[] { "return_orders_raw", "purch_orders_raw", "transfer_orders_raw" };
                foreach (var table in orderTables)
                {
                    var tableExists = await CheckIfTableExistsAsync(connection, table);
                    if (tableExists)
                    {
                        Console.WriteLine($"  🔍 Vérification de la structure de la table {table}...");

                        // Vérifier toutes les colonnes nécessaires pour les tables de commandes
                        var hasCompositeId = await CheckIfColumnExistsAsync(connection, table, "composite_id");
                        var hasPrimaryKeyValue = await CheckIfColumnExistsAsync(connection, table, "primary_key_value");
                        var hasLineNumber = await CheckIfColumnExistsAsync(connection, table, "line_number");
                        var hasJsonData = await CheckIfColumnExistsAsync(connection, table, "json_data");
                        var hasContentHash = await CheckIfColumnExistsAsync(connection, table, "content_hash");
                        var hasApiEndpoint = await CheckIfColumnExistsAsync(connection, table, "api_endpoint");
                        var hasFirstSeenAt = await CheckIfColumnExistsAsync(connection, table, "first_seen_at");
                        var hasLastUpdatedAt = await CheckIfColumnExistsAsync(connection, table, "last_updated_at");
                        var hasUpdateCount = await CheckIfColumnExistsAsync(connection, table, "update_count");

                        // Si les colonnes principales manquent, recréer la table
                        if (!hasCompositeId || !hasPrimaryKeyValue || !hasLineNumber || !hasJsonData || !hasContentHash)
                        {
                            Console.WriteLine($"  🔄 Recréation de la table {table} avec la nouvelle structure...");

                            // Sauvegarder les données existantes si possible
                            var backupTableName = $"{table}_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                            try
                            {
                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = $"CREATE TABLE {backupTableName} AS SELECT * FROM {table}";
                                    command.ExecuteNonQuery();
                                }
                                Console.WriteLine($"    💾 Sauvegarde créée : {backupTableName}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"    ⚠️ Impossible de créer la sauvegarde : {ex.Message}");
                            }

                            // Supprimer l'ancienne table
                            using (var command = connection.CreateCommand())
                            {
                                command.CommandText = $"DROP TABLE {table}";
                                command.ExecuteNonQuery();
                            }

                            // Recréer la table avec la bonne structure
                            using (var command = connection.CreateCommand())
                            {
                                command.CommandText = $@"
                                    CREATE TABLE {table} (
                                        id INT AUTO_INCREMENT PRIMARY KEY,
                                        composite_id VARCHAR(100) NOT NULL,
                                        primary_key_value VARCHAR(50) NOT NULL,
                                        line_number VARCHAR(20) NOT NULL,
                                        json_data JSON NOT NULL,
                                        content_hash VARCHAR(255) NOT NULL,
                                        api_endpoint VARCHAR(255) NOT NULL,
                                        first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                        last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                        update_count INT DEFAULT 0,
                                        is_deleted BOOLEAN DEFAULT FALSE,
                                        deleted_at TIMESTAMP NULL,
                                        UNIQUE KEY unique_composite_id (composite_id),
                                        INDEX idx_primary_key (primary_key_value),
                                        INDEX idx_line_number (line_number),
                                        INDEX idx_content_hash (content_hash),
                                        INDEX idx_api_endpoint (api_endpoint),
                                        INDEX idx_last_updated (last_updated_at),
                                        INDEX idx_is_deleted (is_deleted)
                                    )";
                                command.ExecuteNonQuery();
                            }
                            Console.WriteLine($"    ✅ Table {table} recréée avec succès");
                        }
                        else
                        {
                            // Ajouter les colonnes manquantes une par une
                            var hasOrderIsDeleted = await CheckIfColumnExistsAsync(connection, table, "is_deleted");
                            if (!hasOrderIsDeleted)
                            {
                                Console.WriteLine($"    ➕ Ajout de la colonne 'is_deleted' à la table {table}");
                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = $"ALTER TABLE {table} ADD COLUMN is_deleted BOOLEAN DEFAULT FALSE";
                                    command.ExecuteNonQuery();
                                }
                            }

                            var hasOrderDeletedAt = await CheckIfColumnExistsAsync(connection, table, "deleted_at");
                            if (!hasOrderDeletedAt)
                            {
                                Console.WriteLine($"    ➕ Ajout de la colonne 'deleted_at' à la table {table}");
                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = $"ALTER TABLE {table} ADD COLUMN deleted_at TIMESTAMP NULL";
                                    command.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }

                Console.WriteLine("✓ Structure de la base de données mise à jour");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la mise à jour de la structure de la base");
                Console.WriteLine($"⚠️ Avertissement lors de la mise à jour de la structure : {ex.Message}");
            }
        }

        private static async Task<bool> CheckIfTableExistsAsync(MySqlConnection connection, string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_SCHEMA = @database 
                    AND TABLE_NAME = @tableName";

                command.Parameters.AddWithValue("@database", _configuration["Database:Name"]);
                command.Parameters.AddWithValue("@tableName", tableName);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
        }

        private static async Task<bool> CheckIfColumnExistsAsync(MySqlConnection connection, string tableName, string columnName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = @database 
                    AND TABLE_NAME = @tableName 
                    AND COLUMN_NAME = @columnName";

                command.Parameters.AddWithValue("@database", _configuration["Database:Name"]);
                command.Parameters.AddWithValue("@tableName", tableName);
                command.Parameters.AddWithValue("@columnName", columnName);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
        }

        private static void CreateOrderTables(MySqlConnection connection)
        {
            var orderTables = new[]
            {
                new { TableName = "return_orders_raw", DisplayName = "Commandes de Retour" },
                new { TableName = "purch_orders_raw", DisplayName = "Commandes d Achat" },
                new { TableName = "transfer_orders_raw", DisplayName = "Ordres de Transfert" }
            };

            foreach (var table in orderTables)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"
                        CREATE TABLE IF NOT EXISTS {table.TableName} (
                            id INT AUTO_INCREMENT PRIMARY KEY,
                            composite_id VARCHAR(100) NOT NULL,
                            primary_key_value VARCHAR(50) NOT NULL,
                            line_number VARCHAR(20) NOT NULL,
                            json_data JSON NOT NULL,
                            content_hash VARCHAR(255) NOT NULL,
                            api_endpoint VARCHAR(255) NOT NULL,
                            first_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            last_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            update_count INT DEFAULT 0,
                            is_deleted BOOLEAN DEFAULT FALSE,
                            deleted_at TIMESTAMP NULL,
                            UNIQUE KEY unique_composite_id (composite_id),
                            INDEX idx_primary_key (primary_key_value),
                            INDEX idx_line_number (line_number),
                            INDEX idx_content_hash (content_hash),
                            INDEX idx_api_endpoint (api_endpoint),
                            INDEX idx_last_updated (last_updated_at),
                            INDEX idx_is_deleted (is_deleted)
                        )";
                    command.ExecuteNonQuery();
                }
            }
        }

        // ========================================
        // SYNCHRONISATION DES ARTICLES (CODE EXISTANT)
        // ========================================

        private static async Task<SyncResult> SyncArticlesWithDatabaseAsync(string token)
        {
            var result = new SyncResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                Console.WriteLine("🔍 Récupération des articles depuis l'API Dynamics...");

                // ÉTAPE 1 : Récupérer les données depuis l'API
                var articles = await GetArticlesFromApiAsync(token);
                if (articles == null || articles.Length == 0)
                {
                    Console.WriteLine("⚠️ Aucun article récupéré depuis l'API");
                    return result;
                }

                Console.WriteLine($"✅ {articles.Length} articles récupérés depuis l'API");

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

                    // ÉTAPE 2 : Récupérer les articles existants et leurs hashes
                    Console.WriteLine("🔍 Vérification des articles existants en base...");
                    var existingArticles = await GetExistingArticlesAsync(connection);
                    var existingItemIds = existingArticles.Keys.ToHashSet();
                    var apiItemIds = new HashSet<string>();

                    Console.WriteLine($"📊 {existingArticles.Count} articles existants en base");

                    // ÉTAPE 3 : Traitement intelligent de chaque article
                    Console.WriteLine($"🔄 Traitement intelligent de {articles.Length} articles...");

                    foreach (var article in articles)
                    {
                        try
                        {
                            result.TotalProcessed++;

                            // Récupérer l'ItemId
                            var itemId = article.TryGetProperty("ItemId", out var itemIdProp) ? itemIdProp.GetString() : null;
                            if (string.IsNullOrEmpty(itemId))
                            {
                                result.ErrorCount++;
                                continue;
                            }

                            apiItemIds.Add(itemId);

                            // Calculer le hash du contenu
                            var jsonString = JsonSerializer.Serialize(article, new JsonSerializerOptions { WriteIndented = false });
                            var contentHash = ComputeHash(jsonString);

                            // ÉTAPE 4 : Logique intelligente de synchronisation
                            if (existingArticles.ContainsKey(itemId))
                            {
                                // Article existant - vérifier s'il a changé
                                var existingHash = existingArticles[itemId];
                                if (existingHash != contentHash)
                                {
                                    // Article modifié
                                    await UpdateExistingArticleAsync(connection, itemId, jsonString, contentHash, existingHash);
                                    result.UpdatedArticles++;
                                }
                                else
                                {
                                    // Article inchangé
                                    result.UnchangedArticles++;
                                }
                            }
                            else
                            {
                                // Nouvel article
                                await InsertNewArticleAsync(connection, itemId, jsonString, contentHash);
                                result.NewArticles++;
                            }

                            // Affichage du progrès toutes les 50 itérations
                            if (result.TotalProcessed % 50 == 0)
                            {
                                Console.Write($"\r📊 Traités: {result.TotalProcessed}/{articles.Length} | Nouveaux: {result.NewArticles} | Modifiés: {result.UpdatedArticles} | Inchangés: {result.UnchangedArticles}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.ErrorCount++;
                            var errorItemId = "UNKNOWN";
                            try
                            {
                                errorItemId = article.TryGetProperty("ItemId", out var itemIdProp) ? itemIdProp.GetString() : "UNKNOWN";
                            }
                            catch
                            {
                                errorItemId = "UNKNOWN";
                            }
                            _logger.LogError(ex, $"Erreur lors du traitement de l'article {result.TotalProcessed} (ItemId: {errorItemId})");
                        }
                    }

                    Console.WriteLine(); // Nouvelle ligne après le compteur

                    // ÉTAPE 5 : Détecter et marquer les articles supprimés de l'API
                    var deletedItemIds = existingItemIds.Except(apiItemIds).ToList();
                    if (deletedItemIds.Any())
                    {
                        Console.WriteLine($"🗑️ Détection de {deletedItemIds.Count} articles supprimés de l'API");

                        foreach (var deletedItemId in deletedItemIds)
                        {
                            await MarkArticleAsDeletedAsync(connection, deletedItemId);
                        }

                        Console.WriteLine($"✓ {deletedItemIds.Count} articles marqués comme supprimés");
                    }

                    // ÉTAPE 6 : Analyser et mettre à jour les balises
                    Console.WriteLine("\n🔍 Analyse des balises des articles...");
                    var detectedTags = await AnalyzeAndUpdateArticleTagsAsync(articles);
                    Console.WriteLine($"✓ Analyse des balises terminée: {detectedTags.Count} balises gérées");

                    // ÉTAPE 7 : Résumé de la synchronisation
                    Console.WriteLine($"\n📋 RÉSUMÉ DE LA SYNCHRONISATION:");
                    Console.WriteLine($"  ➕ Nouveaux articles: {result.NewArticles}");
                    Console.WriteLine($"  🔄 Articles mis à jour: {result.UpdatedArticles}");
                    Console.WriteLine($"  ✅ Articles inchangés: {result.UnchangedArticles}");
                    Console.WriteLine($"  🗑️ Articles supprimés: {deletedItemIds.Count}");
                    Console.WriteLine($"  ❌ Erreurs: {result.ErrorCount}");
                }

                stopwatch.Stop();
                result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

                _logger.LogInformation($"Synchronisation intelligente terminée: {result.NewArticles} nouveaux, {result.UpdatedArticles} modifiés, {result.UnchangedArticles} inchangés, {result.ErrorCount} erreurs");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation avec la base de données");
                throw;
            }
        }

        private static async Task<JsonElement[]> GetArticlesFromApiAsync(string token)
        {
            try
            {
                var apiUrl = $"{_configuration["Resource"]}data/BRINT34ReleasedProducts";

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(content);

                    if (jsonDoc.RootElement.TryGetProperty("value", out var valueElement))
                    {
                        var articles = new List<JsonElement>();
                        foreach (var item in valueElement.EnumerateArray())
                        {
                            articles.Add(item);
                        }
                        return articles.ToArray();
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API: {response.StatusCode} - {errorContent}");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception lors de la récupération des articles depuis l'API");
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> GetExistingArticlesAsync(MySqlConnection connection)
        {
            var existingArticles = new Dictionary<string, string>();

            using (var command = connection.CreateCommand())
            {
                // Vérifier d'abord si la colonne is_deleted existe
                bool hasIsDeletedColumn = await CheckIfColumnExistsAsync(connection, "articles_raw", "is_deleted");

                if (hasIsDeletedColumn)
                {
                    command.CommandText = "SELECT item_id, content_hash FROM articles_raw WHERE is_deleted = FALSE";
                }
                else
                {
                    // Fallback pour les anciennes structures de base
                    command.CommandText = "SELECT item_id, content_hash FROM articles_raw";
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var itemId = reader.GetString("item_id");
                        var contentHash = reader.GetString("content_hash");
                        existingArticles[itemId] = contentHash;
                    }
                }
            }

            return existingArticles;
        }

        private static string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(hashBytes);
            }
        }

        private static async Task InsertNewArticleAsync(MySqlConnection connection, string itemId, string jsonData, string contentHash)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO articles_raw (json_data, content_hash, api_endpoint, first_seen_at, last_updated_at, update_count)
                    VALUES (@jsonData, @contentHash, 'data/BRINT34ReleasedProducts', NOW(), NOW(), 0)";

                command.Parameters.AddWithValue("@jsonData", jsonData);
                command.Parameters.AddWithValue("@contentHash", contentHash);

                await command.ExecuteNonQueryAsync();
            }

            // Log du changement
            await LogArticleChangeAsync(connection, itemId, "NEW", null, contentHash, "Nouvel article ajouté");
        }

        private static async Task UpdateExistingArticleAsync(MySqlConnection connection, string itemId, string jsonData, string newContentHash, string oldContentHash)
        {
            // Vérifier si les colonnes existent
            bool hasIsDeletedColumn = await CheckIfColumnExistsAsync(connection, "articles_raw", "is_deleted");

            using (var command = connection.CreateCommand())
            {
                if (hasIsDeletedColumn)
                {
                    command.CommandText = @"
                        UPDATE articles_raw 
                        SET json_data = @jsonData, 
                            content_hash = @contentHash, 
                            last_updated_at = NOW(), 
                            update_count = update_count + 1,
                            is_deleted = FALSE,
                            deleted_at = NULL
                        WHERE item_id = @itemId";
                }
                else
                {
                    command.CommandText = @"
                        UPDATE articles_raw 
                        SET json_data = @jsonData, 
                            content_hash = @contentHash, 
                            last_updated_at = NOW(), 
                            update_count = update_count + 1
                        WHERE item_id = @itemId";
                }

                command.Parameters.AddWithValue("@jsonData", jsonData);
                command.Parameters.AddWithValue("@contentHash", newContentHash);
                command.Parameters.AddWithValue("@itemId", itemId);

                await command.ExecuteNonQueryAsync();
            }

            // Log du changement
            await LogArticleChangeAsync(connection, itemId, "UPDATED", oldContentHash, newContentHash, "Article mis à jour");
        }

        private static async Task MarkArticleAsDeletedAsync(MySqlConnection connection, string itemId)
        {
            // Vérifier si la colonne is_deleted existe
            bool hasIsDeletedColumn = await CheckIfColumnExistsAsync(connection, "articles_raw", "is_deleted");

            if (hasIsDeletedColumn)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE articles_raw 
                        SET is_deleted = TRUE, 
                            deleted_at = NOW() 
                        WHERE item_id = @itemId AND is_deleted = FALSE";

                    command.Parameters.AddWithValue("@itemId", itemId);

                    var rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        await LogArticleChangeAsync(connection, itemId, "DELETED", null, null, "Article marqué comme supprimé (absent de l'API)");
                    }
                }
            }
            else
            {
                // Fallback : supprimer physiquement si la colonne n'existe pas
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM articles_raw WHERE item_id = @itemId";
                    command.Parameters.AddWithValue("@itemId", itemId);
                    var rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        await LogArticleChangeAsync(connection, itemId, "DELETED", null, null, "Article supprimé physiquement (absent de l'API)");
                    }
                }
            }
        }

        private static async Task LogArticleChangeAsync(MySqlConnection connection, string itemId, string changeType, string oldHash, string newHash, string summary)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO article_changes (item_id, change_type, old_hash, new_hash, changed_at, changes_summary)
                    VALUES (@itemId, @changeType, @oldHash, @newHash, NOW(), @summary)";

                command.Parameters.AddWithValue("@itemId", itemId);
                command.Parameters.AddWithValue("@changeType", changeType);
                command.Parameters.AddWithValue("@oldHash", oldHash);
                command.Parameters.AddWithValue("@newHash", newHash);
                command.Parameters.AddWithValue("@summary", summary);

                await command.ExecuteNonQueryAsync();
            }
        }

        // ========================================
        // NOUVELLES MÉTHODES POUR LA GESTION DES BALISES
        // ========================================

        private static async Task<Dictionary<string, ArticleTagInfo>> AnalyzeAndUpdateArticleTagsAsync(JsonElement[] articles)
        {
            var detectedTags = new Dictionary<string, ArticleTagInfo>();

            Console.WriteLine("🔍 Analyse des balises des articles...");

            // Analyser tous les articles pour détecter les balises
            foreach (var article in articles)
            {
                AnalyzeJsonElement(article, "", detectedTags);
            }

            Console.WriteLine($"✓ {detectedTags.Count} balises détectées au total");

            // Mettre à jour la base de données avec les balises
            await UpdateArticleTagsInDatabaseAsync(detectedTags);

            return detectedTags;
        }

        private static void AnalyzeJsonElement(JsonElement element, string prefix, Dictionary<string, ArticleTagInfo> tags)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        string fullPath = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

                        // Tronquer le nom de balise si trop long pour MySQL
                        if (fullPath.Length > 190)
                        {
                            fullPath = fullPath.Substring(0, 187) + "...";
                        }

                        // Ajouter ou mettre à jour la balise
                        if (!tags.ContainsKey(fullPath))
                        {
                            tags[fullPath] = new ArticleTagInfo
                            {
                                TagName = fullPath,
                                DataType = GetJsonValueType(property.Value),
                                FirstSeen = DateTime.Now,
                                LastSeen = DateTime.Now,
                                OccurrenceCount = 1,
                                SampleValue = GetSampleValue(property.Value)
                            };
                        }
                        else
                        {
                            tags[fullPath].LastSeen = DateTime.Now;
                            tags[fullPath].OccurrenceCount++;
                        }

                        // Récursion pour les objets imbriqués
                        if (property.Value.ValueKind == JsonValueKind.Object)
                        {
                            AnalyzeJsonElement(property.Value, fullPath, tags);
                        }
                    }
                    break;
            }
        }

        private static string GetJsonValueType(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => "String",
                JsonValueKind.Number => "Number",
                JsonValueKind.True or JsonValueKind.False => "Boolean",
                JsonValueKind.Array => "Array",
                JsonValueKind.Object => "Object",
                JsonValueKind.Null => "Null",
                _ => "Unknown"
            };
        }

        private static string GetSampleValue(JsonElement element)
        {
            try
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString()?.Substring(0, Math.Min(50, element.GetString()?.Length ?? 0)) ?? "",
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True or JsonValueKind.False => element.GetBoolean().ToString(),
                    JsonValueKind.Array => $"[{element.GetArrayLength()} éléments]",
                    JsonValueKind.Object => "[Objet]",
                    JsonValueKind.Null => "null",
                    _ => element.GetRawText()?.Substring(0, Math.Min(50, element.GetRawText()?.Length ?? 0)) ?? ""
                };
            }
            catch
            {
                return "N/A";
            }
        }

        private static async Task UpdateArticleTagsInDatabaseAsync(Dictionary<string, ArticleTagInfo> detectedTags)
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

                    // Récupérer les balises existantes
                    var existingTags = await GetExistingTagsAsync(connection);

                    int newTagsCount = 0;
                    int updatedTagsCount = 0;

                    foreach (var tag in detectedTags.Values)
                    {
                        if (existingTags.ContainsKey(tag.TagName))
                        {
                            // Mettre à jour une balise existante
                            await UpdateExistingTagAsync(connection, tag, existingTags[tag.TagName]);
                            updatedTagsCount++;
                        }
                        else
                        {
                            // Nouvelle balise détectée !
                            await InsertNewTagAsync(connection, tag);
                            newTagsCount++;
                        }
                    }

                    Console.WriteLine($"✓ Balises: {newTagsCount} nouvelles, {updatedTagsCount} mises à jour");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la mise à jour des balises d'articles");
            }
        }

        private static async Task<Dictionary<string, ArticleTagInfo>> GetExistingTagsAsync(MySqlConnection connection)
        {
            var existingTags = new Dictionary<string, ArticleTagInfo>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT tag_name, data_type, first_seen_at, last_seen_at, occurrence_count, sample_value FROM article_tags";

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var tagInfo = new ArticleTagInfo
                        {
                            TagName = reader.GetString("tag_name"),
                            DataType = reader.GetString("data_type"),
                            FirstSeen = reader.GetDateTime("first_seen_at"),
                            LastSeen = reader.GetDateTime("last_seen_at"),
                            OccurrenceCount = reader.GetInt32("occurrence_count"),
                            SampleValue = reader.IsDBNull("sample_value") ? "" : reader.GetString("sample_value")
                        };
                        existingTags[tagInfo.TagName] = tagInfo;
                    }
                }
            }

            return existingTags;
        }

        private static async Task UpdateExistingTagAsync(MySqlConnection connection, ArticleTagInfo newTag, ArticleTagInfo existingTag)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    UPDATE article_tags 
                    SET last_seen_at = @lastSeen, 
                        occurrence_count = @occurrenceCount,
                        sample_value = @sampleValue
                    WHERE tag_name = @tagName";

                command.Parameters.AddWithValue("@lastSeen", newTag.LastSeen);
                command.Parameters.AddWithValue("@occurrenceCount", existingTag.OccurrenceCount + newTag.OccurrenceCount);
                command.Parameters.AddWithValue("@sampleValue", newTag.SampleValue);
                command.Parameters.AddWithValue("@tagName", newTag.TagName);

                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task InsertNewTagAsync(MySqlConnection connection, ArticleTagInfo tag)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO article_tags (tag_name, data_type, first_seen_at, last_seen_at, occurrence_count, sample_value)
                    VALUES (@tagName, @dataType, @firstSeen, @lastSeen, @occurrenceCount, @sampleValue)";

                command.Parameters.AddWithValue("@tagName", tag.TagName);
                command.Parameters.AddWithValue("@dataType", tag.DataType);
                command.Parameters.AddWithValue("@firstSeen", tag.FirstSeen);
                command.Parameters.AddWithValue("@lastSeen", tag.LastSeen);
                command.Parameters.AddWithValue("@occurrenceCount", tag.OccurrenceCount);
                command.Parameters.AddWithValue("@sampleValue", tag.SampleValue);

                await command.ExecuteNonQueryAsync();
            }
        }

        // ========================================
        // SYNCHRONISATION DES COMMANDES (CODE EXISTANT)
        // ========================================

        private static async Task<OrderSyncResult> SyncOrderDataAsync(string token, OrderEndpoint orderConfig)
        {
            var result = new OrderSyncResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                Console.WriteLine($"🔍 Récupération des {orderConfig.DisplayName.ToLower()} depuis l'API...");

                // ÉTAPE 1 : Récupérer les données depuis l'API
                var orderLines = await GetOrderDataFromApiAsync(token, orderConfig.Endpoint);
                if (orderLines == null || orderLines.Length == 0)
                {
                    Console.WriteLine($"⚠️ Aucune ligne récupérée pour {orderConfig.DisplayName}");
                    return result;
                }

                Console.WriteLine($"✅ {orderLines.Length} lignes récupérées pour {orderConfig.DisplayName}");

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

                    // ÉTAPE 2 : Récupérer les lignes existantes
                    Console.WriteLine($"🔍 Vérification des lignes existantes pour {orderConfig.DisplayName}...");
                    var existingOrderLines = await GetExistingOrderLinesAsync(connection, orderConfig.TableName);
                    var existingCompositeIds = existingOrderLines.Keys.ToHashSet();
                    var apiCompositeIds = new HashSet<string>();

                    Console.WriteLine($"📊 {existingOrderLines.Count} lignes existantes en base pour {orderConfig.DisplayName}");

                    // ÉTAPE 3 : Traitement de chaque ligne
                    Console.WriteLine($"🔄 Traitement de {orderLines.Length} lignes pour {orderConfig.DisplayName}...");

                    foreach (var orderLine in orderLines)
                    {
                        try
                        {
                            result.TotalProcessed++;

                            // Récupérer les clés primaires
                            var primaryKeyValue = orderLine.TryGetProperty(orderConfig.PrimaryKeyField, out var primaryProp) ? primaryProp.GetString() : null;
                            var lineNumber = orderLine.TryGetProperty(orderConfig.LineNumberField, out var lineProp) ? lineProp.GetString() : "0";

                            if (string.IsNullOrEmpty(primaryKeyValue))
                            {
                                result.ErrorCount++;
                                continue;
                            }

                            // Créer l'ID composite
                            var compositeId = $"{primaryKeyValue}_{lineNumber}";
                            apiCompositeIds.Add(compositeId);

                            // Calculer le hash du contenu
                            var jsonString = JsonSerializer.Serialize(orderLine, new JsonSerializerOptions { WriteIndented = false });
                            var contentHash = ComputeHash(jsonString);

                            // ÉTAPE 4 : Logique de synchronisation
                            if (existingOrderLines.ContainsKey(compositeId))
                            {
                                // Ligne existante - vérifier si elle a changé
                                var existingHash = existingOrderLines[compositeId];
                                if (existingHash != contentHash)
                                {
                                    // Ligne modifiée
                                    await UpdateExistingOrderLineAsync(connection, orderConfig, compositeId, jsonString, contentHash);
                                    result.UpdatedOrderLines++;
                                }
                                else
                                {
                                    // Ligne inchangée
                                    result.UnchangedOrderLines++;
                                }
                            }
                            else
                            {
                                // Nouvelle ligne
                                await InsertNewOrderLineAsync(connection, orderConfig, compositeId, primaryKeyValue, lineNumber, jsonString, contentHash);
                                result.NewOrderLines++;

                                if (result.NewOrderLines % 1000 == 0)
                                {
                                    Console.WriteLine($"   💾 {result.NewOrderLines} nouvelles lignes ajoutées");
                                }
                            }

                            // Affichage du progrès
                            if (result.TotalProcessed % 100 == 0)
                            {
                                string progressMessage = $"📊 {orderConfig.DisplayName}: {result.TotalProcessed}/{orderLines.Length} | Nouvelles: {result.NewOrderLines} | Modifiées: {result.UpdatedOrderLines} | Inchangées: {result.UnchangedOrderLines}";
                                Console.Write($"\r{progressMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.ErrorCount++;
                            _logger.LogError(ex, $"Erreur lors du traitement de la ligne {result.TotalProcessed} pour {orderConfig.Name}");
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
                }

                stopwatch.Stop();
                result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la synchronisation des {orderConfig.DisplayName}");
                throw;
            }
        }

        private static async Task<JsonElement[]> GetOrderDataFromApiAsync(string token, string endpoint)
        {
            try
            {
                var apiUrl = $"{_configuration["Resource"]}{endpoint}";

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(content);

                    if (jsonDoc.RootElement.TryGetProperty("value", out var valueElement))
                    {
                        var orderLines = new List<JsonElement>();
                        foreach (var item in valueElement.EnumerateArray())
                        {
                            orderLines.Add(item);
                        }
                        return orderLines.ToArray();
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API {endpoint}: {response.StatusCode} - {errorContent}");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception lors de la récupération des données depuis {endpoint}");
                return null;
            }
        }

        private static async Task<Dictionary<string, string>> GetExistingOrderLinesAsync(MySqlConnection connection, string tableName)
        {
            var existingLines = new Dictionary<string, string>();

            try
            {
                // Vérifier si les colonnes nécessaires existent
                var hasCompositeId = await CheckIfColumnExistsAsync(connection, tableName, "composite_id");
                var hasContentHash = await CheckIfColumnExistsAsync(connection, tableName, "content_hash");
                var hasIsDeleted = await CheckIfColumnExistsAsync(connection, tableName, "is_deleted");

                if (!hasCompositeId || !hasContentHash)
                {
                    // La table n'a pas la bonne structure, retourner un dictionnaire vide
                    // Les données seront traitées comme nouvelles
                    Console.WriteLine($"⚠️ La table {tableName} n'a pas la structure attendue - toutes les lignes seront traitées comme nouvelles");
                    return existingLines;
                }

                using (var command = connection.CreateCommand())
                {
                    if (hasIsDeleted)
                    {
                        command.CommandText = $"SELECT composite_id, content_hash FROM {tableName} WHERE is_deleted = FALSE";
                    }
                    else
                    {
                        command.CommandText = $"SELECT composite_id, content_hash FROM {tableName}";
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var compositeId = reader.GetString("composite_id");
                            var contentHash = reader.GetString("content_hash");
                            existingLines[compositeId] = contentHash;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erreur lors de la récupération des lignes existantes pour {tableName}: {ex.Message}");
                // Retourner un dictionnaire vide en cas d'erreur
                return new Dictionary<string, string>();
            }

            return existingLines;
        }

        private static async Task InsertNewOrderLineAsync(MySqlConnection connection, OrderEndpoint config, string compositeId, string primaryKeyValue, string lineNumber, string jsonData, string contentHash)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"
                    INSERT INTO {config.TableName} (composite_id, primary_key_value, line_number, json_data, content_hash, api_endpoint, first_seen_at, last_updated_at, update_count)
                    VALUES (@compositeId, @primaryKeyValue, @lineNumber, @jsonData, @contentHash, @apiEndpoint, NOW(), NOW(), 0)";

                command.Parameters.AddWithValue("@compositeId", compositeId);
                command.Parameters.AddWithValue("@primaryKeyValue", primaryKeyValue);
                command.Parameters.AddWithValue("@lineNumber", lineNumber);
                command.Parameters.AddWithValue("@jsonData", jsonData);
                command.Parameters.AddWithValue("@contentHash", contentHash);
                command.Parameters.AddWithValue("@apiEndpoint", config.Endpoint);

                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task UpdateExistingOrderLineAsync(MySqlConnection connection, OrderEndpoint config, string compositeId, string jsonData, string contentHash)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"
                    UPDATE {config.TableName} 
                    SET json_data = @jsonData, 
                        content_hash = @contentHash, 
                        last_updated_at = NOW(), 
                        update_count = update_count + 1,
                        is_deleted = FALSE,
                        deleted_at = NULL
                    WHERE composite_id = @compositeId";

                command.Parameters.AddWithValue("@jsonData", jsonData);
                command.Parameters.AddWithValue("@contentHash", contentHash);
                command.Parameters.AddWithValue("@compositeId", compositeId);

                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task MarkOrderLineAsDeletedAsync(MySqlConnection connection, string tableName, string compositeId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"
                    UPDATE {tableName} 
                    SET is_deleted = TRUE, 
                        deleted_at = NOW() 
                    WHERE composite_id = @compositeId AND is_deleted = FALSE";

                command.Parameters.AddWithValue("@compositeId", compositeId);

                await command.ExecuteNonQueryAsync();
            }
        }

        // ========================================
        // LOGGING ET GESTION DES RÉSULTATS
        // ========================================

        private static async Task LogSyncResultAsync(string endpoint, string status, int totalProcessed, string errorMessage, SyncResult syncResult = null, OrderSyncResult orderSyncResult = null)
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

                    // Vérifier quelles colonnes existent dans sync_logs
                    var hasErrorMessage = await CheckIfColumnExistsAsync(connection, "sync_logs", "error_message");
                    var hasExecutionTime = await CheckIfColumnExistsAsync(connection, "sync_logs", "execution_time_ms");
                    var hasNewArticles = await CheckIfColumnExistsAsync(connection, "sync_logs", "new_articles");
                    var hasUpdatedArticles = await CheckIfColumnExistsAsync(connection, "sync_logs", "updated_articles");
                    var hasUnchangedArticles = await CheckIfColumnExistsAsync(connection, "sync_logs", "unchanged_articles");
                    var hasErrorCount = await CheckIfColumnExistsAsync(connection, "sync_logs", "error_count");

                    using (var command = connection.CreateCommand())
                    {
                        if (syncResult != null && hasNewArticles && hasUpdatedArticles && hasUnchangedArticles && hasErrorCount && hasExecutionTime && hasErrorMessage)
                        {
                            // Version complète avec toutes les colonnes
                            command.CommandText = @"
                                INSERT INTO sync_logs (endpoint, sync_date, status, total_articles_processed, new_articles, updated_articles, unchanged_articles, error_count, execution_time_ms, error_message)
                                VALUES (@endpoint, NOW(), @status, @totalProcessed, @newArticles, @updatedArticles, @unchangedArticles, @errorCount, @executionTime, @errorMessage)";

                            command.Parameters.AddWithValue("@endpoint", endpoint);
                            command.Parameters.AddWithValue("@status", status);
                            command.Parameters.AddWithValue("@totalProcessed", totalProcessed);
                            command.Parameters.AddWithValue("@newArticles", syncResult.NewArticles);
                            command.Parameters.AddWithValue("@updatedArticles", syncResult.UpdatedArticles);
                            command.Parameters.AddWithValue("@unchangedArticles", syncResult.UnchangedArticles);
                            command.Parameters.AddWithValue("@errorCount", syncResult.ErrorCount);
                            command.Parameters.AddWithValue("@executionTime", syncResult.ExecutionTimeMs);
                            command.Parameters.AddWithValue("@errorMessage", errorMessage ?? "");
                        }
                        else if (orderSyncResult != null && hasNewArticles && hasUpdatedArticles && hasUnchangedArticles && hasErrorCount && hasExecutionTime && hasErrorMessage)
                        {
                            // Version complète avec toutes les colonnes pour les commandes
                            command.CommandText = @"
                                INSERT INTO sync_logs (endpoint, sync_date, status, total_articles_processed, new_articles, updated_articles, unchanged_articles, error_count, execution_time_ms, error_message)
                                VALUES (@endpoint, NOW(), @status, @totalProcessed, @newOrderLines, @updatedOrderLines, @unchangedOrderLines, @errorCount, @executionTime, @errorMessage)";

                            command.Parameters.AddWithValue("@endpoint", endpoint);
                            command.Parameters.AddWithValue("@status", status);
                            command.Parameters.AddWithValue("@totalProcessed", totalProcessed);
                            command.Parameters.AddWithValue("@newOrderLines", orderSyncResult.NewOrderLines);
                            command.Parameters.AddWithValue("@updatedOrderLines", orderSyncResult.UpdatedOrderLines);
                            command.Parameters.AddWithValue("@unchangedOrderLines", orderSyncResult.UnchangedOrderLines);
                            command.Parameters.AddWithValue("@errorCount", orderSyncResult.ErrorCount);
                            command.Parameters.AddWithValue("@executionTime", orderSyncResult.ExecutionTimeMs);
                            command.Parameters.AddWithValue("@errorMessage", errorMessage ?? "");
                        }
                        else
                        {
                            // Version minimale pour compatibilité avec anciennes structures
                            command.CommandText = @"
                                INSERT INTO sync_logs (endpoint, sync_date, status, total_articles_processed)
                                VALUES (@endpoint, NOW(), @status, @totalProcessed)";

                            command.Parameters.AddWithValue("@endpoint", endpoint);
                            command.Parameters.AddWithValue("@status", status);
                            command.Parameters.AddWithValue("@totalProcessed", totalProcessed);
                        }

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du log de synchronisation");
                // Ne pas faire planter le programme pour un problème de log
                Console.WriteLine($"⚠️ Avertissement : Impossible d'enregistrer le log de sync : {ex.Message}");
            }
        }

        // ========================================
        // CLASSES DE DONNÉES
        // ========================================

        public class SyncResult
        {
            public int TotalProcessed { get; set; } = 0;
            public int NewArticles { get; set; } = 0;
            public int UpdatedArticles { get; set; } = 0;
            public int UnchangedArticles { get; set; } = 0;
            public int ErrorCount { get; set; } = 0;
            public long ExecutionTimeMs { get; set; } = 0;
        }

        public class OrderSyncResult
        {
            public int TotalProcessed { get; set; } = 0;
            public int NewOrderLines { get; set; } = 0;
            public int UpdatedOrderLines { get; set; } = 0;
            public int UnchangedOrderLines { get; set; } = 0;
            public int ErrorCount { get; set; } = 0;
            public long ExecutionTimeMs { get; set; } = 0;
        }

        public class OrderEndpoint
        {
            public string Name { get; set; }
            public string Endpoint { get; set; }
            public string TableName { get; set; }
            public string PrimaryKeyField { get; set; }
            public string LineNumberField { get; set; }
            public string DisplayName { get; set; }
        }

        public class ArticleTagInfo
        {
            public string TagName { get; set; }
            public string DataType { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public int OccurrenceCount { get; set; }
            public string SampleValue { get; set; }
        }
    }
}