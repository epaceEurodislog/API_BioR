// Fichier: Services/SqlServerDatabaseService.cs
// Service de gestion SQL Server pour la table JSON_IN

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service pour gérer les opérations SQL Server avec la table JSON_IN
    /// </summary>
    public class SqlServerDatabaseService
    {
        private readonly ILogger<SqlServerDatabaseService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public SqlServerDatabaseService(ILogger<SqlServerDatabaseService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = BuildConnectionString();
        }

        /// <summary>
        /// Initialise la base de données et crée la table JSON_IN si nécessaire
        /// </summary>
        public async Task<bool> InitializeDatabaseAsync()
        {
            try
            {
                Console.WriteLine("🔗 Test de connexion à SQL Server...");

                if (!await TestConnectionAsync())
                {
                    return false;
                }

                Console.WriteLine("🔧 Vérification/création de la table JSON_IN...");
                await CreateJsonTableIfNotExistsAsync();

                Console.WriteLine("✅ Base de données SQL Server initialisée");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'initialisation de la base SQL Server");
                Console.WriteLine($"❌ Erreur initialisation: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Insère ou met à jour des données JSON dans la table JSON_IN
        /// </summary>
        public async Task<JsonInsertResult> InsertOrUpdateJsonDataAsync(string endpoint, JsonElement[] data, string clientCode = "BR")
        {
            var result = new JsonInsertResult { Endpoint = endpoint };

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                Console.WriteLine($"🔄 Insertion des données pour {endpoint}...");

                foreach (var item in data)
                {
                    // Générer une clé unique pour chaque enregistrement
                    var jsonKey = GenerateUniqueKey(endpoint, item);
                    var jsonData = item.GetRawText();

                    // Vérifier si l'enregistrement existe déjà
                    var existingRecord = await GetExistingRecordAsync(connection, jsonKey);

                    if (existingRecord == null)
                    {
                        // Nouvel enregistrement
                        await InsertNewRecordAsync(connection, jsonKey, endpoint, clientCode, jsonData);
                        result.NewRecords++;
                    }
                    else
                    {
                        // Enregistrement existant - mettre à jour si nécessaire
                        if (existingRecord.JsonData != jsonData)
                        {
                            await UpdateExistingRecordAsync(connection, jsonKey, jsonData);
                            result.UpdatedRecords++;
                        }
                        else
                        {
                            result.UnchangedRecords++;
                        }
                    }
                }

                Console.WriteLine($"✅ {result.NewRecords} nouveaux, {result.UpdatedRecords} mis à jour, {result.UnchangedRecords} inchangés");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de l'insertion des données pour {endpoint}");
                throw;
            }
        }

        /// <summary>
        /// Marque les enregistrements comme supprimés s'ils ne sont plus dans l'API
        /// </summary>
        public async Task<int> MarkDeletedRecordsAsync(string endpoint, JsonElement[] currentData)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Récupérer toutes les clés existantes pour cet endpoint
                var existingKeys = await GetExistingKeysForEndpointAsync(connection, endpoint);

                // Récupérer les clés actuelles de l'API
                var currentKeys = new HashSet<string>();
                foreach (var item in currentData)
                {
                    currentKeys.Add(GenerateUniqueKey(endpoint, item));
                }

                // Identifier les clés supprimées
                var deletedKeys = existingKeys.Except(currentKeys).ToList();

                // Marquer comme supprimées (vous pouvez adapter selon vos besoins)
                int deletedCount = 0;
                foreach (var deletedKey in deletedKeys)
                {
                    await MarkRecordAsDeletedAsync(connection, deletedKey);
                    deletedCount++;
                }

                if (deletedCount > 0)
                {
                    Console.WriteLine($"🗑️ {deletedCount} enregistrements marqués comme supprimés");
                }

                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du marquage des suppressions pour {endpoint}");
                throw;
            }
        }

        #region Méthodes privées

        /// <summary>
        /// Construit la chaîne de connexion SQL Server
        /// </summary>
        private string BuildConnectionString()
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = $"{_configuration["Database:Host"]},{_configuration.GetValue<int>("Database:Port", 1433)}",
                InitialCatalog = _configuration["Database:Name"],
                UserID = _configuration["Database:User"],
                Password = _configuration["Database:Password"],
                TrustServerCertificate = true, // Pour éviter les problèmes de certificat
                ConnectTimeout = 30,
                CommandTimeout = 60
            };

            return builder.ConnectionString;
        }

        /// <summary>
        /// Teste la connexion à SQL Server
        /// </summary>
        private async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                Console.WriteLine("✅ Connexion à SQL Server réussie");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur de connexion à SQL Server");
                Console.WriteLine($"❌ Erreur de connexion: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crée la table JSON_IN si elle n'existe pas avec les améliorations suggérées
        /// </summary>
        private async Task CreateJsonTableIfNotExistsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var createTableSql = @"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='JSON_IN' AND xtype='U')
                BEGIN
                    CREATE TABLE [dbo].[JSON_IN] (
                        [JSON_KEYU] NVARCHAR(255) NOT NULL PRIMARY KEY,  -- Clé unique
                        [JSON_CRDA] DATETIME2 NOT NULL DEFAULT GETDATE(),  -- Date de création
                        [JSON_FROM] NVARCHAR(255) NOT NULL,  -- Endpoint de provenance
                        [JSON_CCLI] NVARCHAR(10) NOT NULL DEFAULT 'BR',  -- Client
                        [JSON_DATA] NVARCHAR(MAX) NOT NULL,  -- Contenu JSON
                        [JSON_TRTP] NVARCHAR(50) NULL,  -- Type de transaction (usage futur)
                        [JSON_TRDA] DATETIME2 NULL,  -- Date d'export XML
                        [JSON_TREN] NVARCHAR(50) NOT NULL DEFAULT 'SPEED',  -- Environnement de destination
                        
                        -- Améliorations suggérées
                        [JSON_UPDA] DATETIME2 NULL,  -- Date de dernière mise à jour
                        [JSON_STAT] NVARCHAR(20) NOT NULL DEFAULT 'ACTIVE',  -- Statut: ACTIVE, DELETED, EXPORTED
                        [JSON_HASH] NVARCHAR(64) NULL,  -- Hash du contenu pour détecter les changements
                        [JSON_VERS] INT NOT NULL DEFAULT 1,  -- Version/nombre de mises à jour
                        
                        -- Index pour optimiser les performances
                        INDEX IX_JSON_IN_FROM (JSON_FROM),
                        INDEX IX_JSON_IN_CCLI (JSON_CCLI),
                        INDEX IX_JSON_IN_STAT (JSON_STAT),
                        INDEX IX_JSON_IN_CRDA (JSON_CRDA),
                        INDEX IX_JSON_IN_UPDA (JSON_UPDA)
                    );
                    
                    PRINT 'Table JSON_IN créée avec succès';
                END
                ELSE
                BEGIN
                    -- Vérifier et ajouter les nouvelles colonnes si elles n'existent pas
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('JSON_IN') AND name = 'JSON_UPDA')
                        ALTER TABLE [dbo].[JSON_IN] ADD [JSON_UPDA] DATETIME2 NULL;
                    
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('JSON_IN') AND name = 'JSON_STAT')
                        ALTER TABLE [dbo].[JSON_IN] ADD [JSON_STAT] NVARCHAR(20) NOT NULL DEFAULT 'ACTIVE';
                    
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('JSON_IN') AND name = 'JSON_HASH')
                        ALTER TABLE [dbo].[JSON_IN] ADD [JSON_HASH] NVARCHAR(64) NULL;
                    
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('JSON_IN') AND name = 'JSON_VERS')
                        ALTER TABLE [dbo].[JSON_IN] ADD [JSON_VERS] INT NOT NULL DEFAULT 1;
                    
                    PRINT 'Table JSON_IN mise à jour';
                END";

            using var command = new SqlCommand(createTableSql, connection);
            await command.ExecuteNonQueryAsync();
            Console.WriteLine("  ✓ Table JSON_IN vérifiée/créée");
        }

        /// <summary>
        /// Génère une clé unique pour un enregistrement
        /// </summary>
        private string GenerateUniqueKey(string endpoint, JsonElement item)
        {
            // Essayer de trouver un identifiant naturel dans le JSON
            string identifier = "";

            // Pour les articles
            if (item.TryGetProperty("ItemId", out var itemId))
            {
                identifier = itemId.GetString() ?? "";
            }
            // Pour les commandes avec numéro de ligne
            else if (item.TryGetProperty("OrderId", out var orderId) && item.TryGetProperty("LineNum", out var lineNum))
            {
                identifier = $"{orderId.GetString()}_{lineNum.GetInt32()}";
            }
            // Pour d'autres types d'objets
            else if (item.TryGetProperty("Id", out var id))
            {
                identifier = id.GetString() ?? "";
            }

            // Si pas d'identifiant naturel, utiliser un hash du contenu
            if (string.IsNullOrEmpty(identifier))
            {
                identifier = ComputeHash(item.GetRawText());
            }

            // Créer la clé finale
            return $"{endpoint.Replace("data/", "")}_{identifier}";
        }

        /// <summary>
        /// Calcule un hash SHA256 du contenu
        /// </summary>
        private string ComputeHash(string content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// Récupère un enregistrement existant par sa clé
        /// </summary>
        private async Task<JsonRecord?> GetExistingRecordAsync(SqlConnection connection, string jsonKey)
        {
            using var command = new SqlCommand(
                "SELECT JSON_KEYU, JSON_DATA, JSON_STAT FROM JSON_IN WHERE JSON_KEYU = @key",
                connection);
            command.Parameters.AddWithValue("@key", jsonKey);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new JsonRecord
                {
                    Key = reader.GetString("JSON_KEYU"),
                    JsonData = reader.GetString("JSON_DATA"),
                    Status = reader.GetString("JSON_STAT")
                };
            }
            return null;
        }

        /// <summary>
        /// Insère un nouvel enregistrement
        /// </summary>
        private async Task InsertNewRecordAsync(SqlConnection connection, string jsonKey, string endpoint, string clientCode, string jsonData)
        {
            var insertSql = @"
                INSERT INTO JSON_IN 
                (JSON_KEYU, JSON_FROM, JSON_CCLI, JSON_DATA, JSON_HASH, JSON_CRDA, JSON_UPDA, JSON_STAT, JSON_VERS)
                VALUES 
                (@key, @from, @client, @data, @hash, GETDATE(), GETDATE(), 'ACTIVE', 1)";

            using var command = new SqlCommand(insertSql, connection);
            command.Parameters.AddWithValue("@key", jsonKey);
            command.Parameters.AddWithValue("@from", endpoint);
            command.Parameters.AddWithValue("@client", clientCode);
            command.Parameters.AddWithValue("@data", jsonData);
            command.Parameters.AddWithValue("@hash", ComputeHash(jsonData));

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Met à jour un enregistrement existant
        /// </summary>
        private async Task UpdateExistingRecordAsync(SqlConnection connection, string jsonKey, string jsonData)
        {
            var updateSql = @"
                UPDATE JSON_IN 
                SET JSON_DATA = @data, 
                    JSON_HASH = @hash, 
                    JSON_UPDA = GETDATE(),
                    JSON_VERS = JSON_VERS + 1,
                    JSON_STAT = 'ACTIVE'
                WHERE JSON_KEYU = @key";

            using var command = new SqlCommand(updateSql, connection);
            command.Parameters.AddWithValue("@key", jsonKey);
            command.Parameters.AddWithValue("@data", jsonData);
            command.Parameters.AddWithValue("@hash", ComputeHash(jsonData));

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Récupère toutes les clés existantes pour un endpoint
        /// </summary>
        private async Task<HashSet<string>> GetExistingKeysForEndpointAsync(SqlConnection connection, string endpoint)
        {
            var keys = new HashSet<string>();

            using var command = new SqlCommand(
                "SELECT JSON_KEYU FROM JSON_IN WHERE JSON_FROM = @endpoint AND JSON_STAT != 'DELETED'",
                connection);
            command.Parameters.AddWithValue("@endpoint", endpoint);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                keys.Add(reader.GetString("JSON_KEYU"));
            }

            return keys;
        }

        /// <summary>
        /// Marque un enregistrement comme supprimé
        /// </summary>
        private async Task MarkRecordAsDeletedAsync(SqlConnection connection, string jsonKey)
        {
            var updateSql = @"
                UPDATE JSON_IN 
                SET JSON_STAT = 'DELETED', 
                    JSON_UPDA = GETDATE()
                WHERE JSON_KEYU = @key";

            using var command = new SqlCommand(updateSql, connection);
            command.Parameters.AddWithValue("@key", jsonKey);

            await command.ExecuteNonQueryAsync();
        }

        #endregion
    }

    /// <summary>
    /// Modèle pour un enregistrement JSON
    /// </summary>
    public class JsonRecord
    {
        public string Key { get; set; } = "";
        public string JsonData { get; set; } = "";
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// Résultat d'une opération d'insertion
    /// </summary>
    public class JsonInsertResult
    {
        public string Endpoint { get; set; } = "";
        public int NewRecords { get; set; }
        public int UpdatedRecords { get; set; }
        public int UnchangedRecords { get; set; }
        public int DeletedRecords { get; set; }
    }
}