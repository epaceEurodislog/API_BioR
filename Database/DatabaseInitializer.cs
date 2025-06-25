// Fichier: Database/DatabaseInitializer.cs
// Service d'initialisation et création des tables de base de données

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace DynamicsApiToDatabase.Database
{
    /// <summary>
    /// Service d'initialisation de la base de données
    /// </summary>
    public class DatabaseInitializer
    {
        private readonly ILogger<DatabaseInitializer> _logger;
        private readonly IConfiguration _configuration;

        public DatabaseInitializer(ILogger<DatabaseInitializer> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Initialise la base de données et crée les tables si nécessaire
        /// </summary>
        /// <returns>True si l'initialisation est réussie</returns>
        public async Task<bool> InitializeDatabaseAsync()
        {
            try
            {
                Console.WriteLine("🗄️ Vérification de la base de données...");

                // Étape 1 : Créer la base de données si elle n'existe pas
                if (!await CreateDatabaseIfNotExistsAsync())
                {
                    return false;
                }

                // Étape 2 : Créer les tables si elles n'existent pas
                if (!await CreateTablesIfNotExistAsync())
                {
                    return false;
                }

                Console.WriteLine("✅ Base de données initialisée avec succès");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'initialisation de la base de données");
                Console.WriteLine($"❌ Erreur d'initialisation DB: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crée la base de données si elle n'existe pas
        /// </summary>
        private async Task<bool> CreateDatabaseIfNotExistsAsync()
        {
            try
            {
                var databaseName = _configuration["Database:Name"];
                Console.WriteLine($"🔍 Vérification de la base '{databaseName}'...");

                // Connexion sans spécifier la base de données
                var connectionStringWithoutDb = new MySqlConnectionStringBuilder
                {
                    Server = _configuration["Database:Host"],
                    Port = (uint)_configuration.GetValue<int>("Database:Port", 3306),
                    UserID = _configuration["Database:User"],
                    Password = _configuration["Database:Password"]
                }.ConnectionString;

                using var connection = new MySqlConnection(connectionStringWithoutDb);
                await connection.OpenAsync();

                // Créer la base de données si elle n'existe pas
                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    CREATE DATABASE IF NOT EXISTS `{databaseName}` 
                    CHARACTER SET utf8mb4 
                    COLLATE utf8mb4_unicode_ci";

                await command.ExecuteNonQueryAsync();
                Console.WriteLine($"✅ Base de données '{databaseName}' disponible");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création de la base de données");
                Console.WriteLine($"❌ Erreur création DB: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crée toutes les tables nécessaires si elles n'existent pas
        /// </summary>
        private async Task<bool> CreateTablesIfNotExistAsync()
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

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                Console.WriteLine("🔧 Création des tables...");

                // Créer les tables dans l'ordre
                await CreateArticlesTableAsync(connection);
                await CreateOrderTablesAsync(connection);
                await CreateSyncLogsTableAsync(connection);
                await CreateArticleTagsTableAsync(connection);

                Console.WriteLine("✅ Toutes les tables sont créées");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création des tables");
                Console.WriteLine($"❌ Erreur création tables: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crée la table des articles
        /// </summary>
        private async Task CreateArticlesTableAsync(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS `articles_raw` (
                    `id` int NOT NULL AUTO_INCREMENT COMMENT 'ID auto-incremente',
                    `item_id` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'ID de l article depuis l API',
                    `json_data` json NOT NULL COMMENT 'Donnees JSON completes de l article',
                    `content_hash` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'Hash du contenu pour detecter les modifications',
                    `api_endpoint` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'Endpoint API source',
                    `first_seen_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date de premiere synchronisation',
                    `last_updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date de derniere mise a jour',
                    `update_count` int DEFAULT '0' COMMENT 'Nombre de mises a jour',
                    `is_deleted` tinyint(1) DEFAULT '0' COMMENT 'Article supprime de l API',
                    `deleted_at` timestamp NULL DEFAULT NULL COMMENT 'Date de suppression',
                    PRIMARY KEY (`id`),
                    UNIQUE KEY `unique_item_id` (`item_id`),
                    KEY `idx_item_id` (`item_id`),
                    KEY `idx_content_hash` (`content_hash`(250)),
                    KEY `idx_api_endpoint` (`api_endpoint`(250)),
                    KEY `idx_last_updated` (`last_updated_at`),
                    KEY `idx_is_deleted` (`is_deleted`)
                ) ENGINE=MyISAM DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci 
                COMMENT='Table des articles synchronises depuis l API Dynamics'";

            await command.ExecuteNonQueryAsync();
            Console.WriteLine("  ✓ Table articles_raw créée");
        }

        /// <summary>
        /// Crée les tables de commandes (retour, achat, transfert)
        /// </summary>
        private async Task CreateOrderTablesAsync(MySqlConnection connection)
        {
            var orderTables = new[]
            {
                new { Name = "return_orders_raw", Comment = "Commandes de Retour avec gestion des lignes multiples" },
                new { Name = "purch_orders_raw", Comment = "Commandes d Achat avec gestion des lignes multiples" },
                new { Name = "transfer_orders_raw", Comment = "Ordres de Transfert avec gestion des lignes multiples" }
            };

            foreach (var table in orderTables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS `{table.Name}` (
                        `id` int NOT NULL AUTO_INCREMENT,
                        `composite_id` varchar(100) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'OrderId_LineNumber pour unicite',
                        `order_id` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'ID de la commande principale',
                        `line_number` varchar(20) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'Numero de ligne dans la commande',
                        `json_data` json NOT NULL,
                        `content_hash` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL,
                        `api_endpoint` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL,
                        `first_seen_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
                        `last_updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
                        `update_count` int DEFAULT '0',
                        `is_deleted` tinyint(1) DEFAULT '0',
                        `deleted_at` timestamp NULL DEFAULT NULL,
                        PRIMARY KEY (`id`),
                        UNIQUE KEY `unique_composite_id` (`composite_id`),
                        KEY `idx_composite_id` (`composite_id`),
                        KEY `idx_order_id` (`order_id`),
                        KEY `idx_line_number` (`line_number`),
                        KEY `idx_content_hash` (`content_hash`(250)),
                        KEY `idx_api_endpoint` (`api_endpoint`(250)),
                        KEY `idx_last_updated` (`last_updated_at`),
                        KEY `idx_is_deleted` (`is_deleted`)
                    ) ENGINE=MyISAM DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci 
                    COMMENT='{table.Comment}'";

                await command.ExecuteNonQueryAsync();
                Console.WriteLine($"  ✓ Table {table.Name} créée");
            }
        }

        /// <summary>
        /// Crée la table des logs de synchronisation
        /// </summary>
        private async Task CreateSyncLogsTableAsync(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS `sync_logs` (
                    `id` int NOT NULL AUTO_INCREMENT,
                    `endpoint` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'Endpoint API synchronise',
                    `status` enum('SUCCESS','WARNING','ERROR') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'SUCCESS',
                    `total_articles_processed` int DEFAULT '0' COMMENT 'Nombre total d elements traites',
                    `new_articles` int DEFAULT '0' COMMENT 'Nouveaux elements ajoutes',
                    `updated_articles` int DEFAULT '0' COMMENT 'Elements mis a jour',
                    `unchanged_articles` int DEFAULT '0' COMMENT 'Elements inchanges',
                    `error_count` int DEFAULT '0' COMMENT 'Nombre d erreurs',
                    `message` text COLLATE utf8mb4_unicode_ci COMMENT 'Message d erreur ou details',
                    `execution_time_ms` bigint DEFAULT '0' COMMENT 'Temps d execution en millisecondes',
                    `sync_date` timestamp NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date et heure de synchronisation',
                    PRIMARY KEY (`id`),
                    KEY `idx_endpoint` (`endpoint`(250)),
                    KEY `idx_sync_date` (`sync_date`),
                    KEY `idx_status` (`status`)
                ) ENGINE=MyISAM DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci 
                COMMENT='Journal des synchronisations pour suivi et debugging'";

            await command.ExecuteNonQueryAsync();
            Console.WriteLine("  ✓ Table sync_logs créée");
        }

        /// <summary>
        /// Crée la table d'analyse des balises d'articles
        /// </summary>
        private async Task CreateArticleTagsTableAsync(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS `article_tags` (
                    `id` int NOT NULL AUTO_INCREMENT,
                    `tag_name` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'Nom de la balise JSON',
                    `data_type` varchar(50) COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT 'Type de donnees detecte',
                    `first_seen` timestamp NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Premiere fois vu',
                    `last_seen` timestamp NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Derniere fois vu',
                    `occurrence_count` int DEFAULT '1' COMMENT 'Nombre d occurrences',
                    `sample_value` text COLLATE utf8mb4_unicode_ci COMMENT 'Exemple de valeur',
                    PRIMARY KEY (`id`),
                    UNIQUE KEY `unique_tag` (`tag_name`),
                    KEY `idx_tag_name` (`tag_name`(250)),
                    KEY `idx_data_type` (`data_type`),
                    KEY `idx_last_seen` (`last_seen`)
                ) ENGINE=MyISAM DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci 
                COMMENT='Analyse automatique des balises JSON des articles'";

            await command.ExecuteNonQueryAsync();
            Console.WriteLine("  ✓ Table article_tags créée");
        }

        /// <summary>
        /// Teste la connexion à la base de données
        /// </summary>
        /// <returns>True si la connexion fonctionne</returns>
        public async Task<bool> TestConnectionAsync()
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

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                Console.WriteLine("✅ Connexion à la base de données réussie");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur de connexion à la base de données");
                Console.WriteLine($"❌ Erreur de connexion DB: {ex.Message}");
                return false;
            }
        }
    }
}