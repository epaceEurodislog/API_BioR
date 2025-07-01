// Fichier: Models/DataModels.cs
// Modèles de données pour l'application

using System;
using System.Text.Json;

namespace DynamicsApiToDatabase.Models
{
    /// <summary>
    /// Résultat d'une synchronisation de données
    /// </summary>
    public class SyncResult
    {
        public string Endpoint { get; set; } = "";
        public int TotalRecords { get; set; }
        public int NewRecords { get; set; }
        public int UpdatedRecords { get; set; }
        public int UnchangedRecords { get; set; }
        public int DeletedRecords { get; set; }
        public int ErrorCount { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime SyncDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Calcule le pourcentage de réussite
        /// </summary>
        public double SuccessRate
        {
            get
            {
                if (TotalRecords == 0) return 100.0;
                return (double)(TotalRecords - this.ErrorCount) / TotalRecords * 100.0;
            }
        }

        /// <summary>
        /// Indique si la synchronisation a des changements
        /// </summary>
        public bool HasChanges => NewRecords > 0 || UpdatedRecords > 0 || DeletedRecords > 0;

        /// <summary>
        /// Résumé textuel de la synchronisation
        /// </summary>
        public string Summary
        {
            get
            {
                if (!Success)
                    return $"❌ Échec: {ErrorMessage}";

                if (!HasChanges)
                    return $"✅ Aucun changement ({TotalRecords} enregistrements vérifiés)";

                return $"✅ {NewRecords} nouveaux, {UpdatedRecords} MAJ, {DeletedRecords} supprimés sur {TotalRecords}";
            }
        }
    }

    /// <summary>
    /// Configuration d'un endpoint de synchronisation
    /// </summary>
    public class EndpointConfig
    {
        public string Name { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public string Description { get; set; } = "";
        public string TableName { get; set; } = "";
        public string PrimaryKeyField { get; set; } = "";
        public string? LineNumberField { get; set; }
        public bool SupportsPagination { get; set; } = true;
        public int DefaultPageSize { get; set; } = 1000;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Génère le nom de la table basé sur l'endpoint
        /// </summary>
        public string GetTableName()
        {
            if (!string.IsNullOrEmpty(TableName))
                return TableName;

            // Générer automatiquement depuis l'endpoint
            var endpointName = Endpoint.Replace("data/", "").Replace("BRINT", "").ToLower();
            return $"{endpointName}_raw";
        }

        /// <summary>
        /// Génère une clé unique pour un enregistrement
        /// </summary>
        public string GenerateKey(JsonElement data)
        {
            var endpointShort = Endpoint.Replace("data/", "");

            // Essayer d'extraire l'identifiant principal
            if (data.TryGetProperty(PrimaryKeyField, out var primaryValue))
            {
                var primaryKey = primaryValue.GetString() ?? "";

                // Si c'est une commande avec numéro de ligne
                if (!string.IsNullOrEmpty(LineNumberField) &&
                    data.TryGetProperty(LineNumberField, out var lineValue))
                {
                    return $"{endpointShort}_{primaryKey}_{lineValue.GetInt32()}";
                }

                return $"{endpointShort}_{primaryKey}";
            }

            // Fallback: utiliser un hash du contenu
            return $"{endpointShort}_{ComputeContentHash(data.GetRawText())}";
        }

        private static string ComputeContentHash(string content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash)[..16]; // Prendre les 16 premiers caractères
        }
    }

    /// <summary>
    /// Représente un enregistrement dans la table JSON_IN
    /// </summary>
    public class JsonInRecord
    {
        public string JsonKeyu { get; set; } = "";
        public DateTime JsonCrda { get; set; }
        public string JsonFrom { get; set; } = "";
        public string JsonCcli { get; set; } = "BR";
        public string JsonData { get; set; } = "";
        public string? JsonTrtp { get; set; }
        public DateTime? JsonTrda { get; set; }
        public string JsonTren { get; set; } = "SPEED";
        public DateTime? JsonUpda { get; set; }
        public string JsonStat { get; set; } = "ACTIVE";
        public string? JsonHash { get; set; }
        public int JsonVers { get; set; } = 1;

        /// <summary>
        /// Calcule le hash du contenu JSON
        /// </summary>
        public void UpdateHash()
        {
            if (!string.IsNullOrEmpty(JsonData))
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(JsonData));
                JsonHash = Convert.ToHexString(hash);
            }
        }

        /// <summary>
        /// Indique si l'enregistrement est actif
        /// </summary>
        public bool IsActive => JsonStat == "ACTIVE";

        /// <summary>
        /// Indique si l'enregistrement a été supprimé
        /// </summary>
        public bool IsDeleted => JsonStat == "DELETED";

        /// <summary>
        /// Indique si l'enregistrement a été exporté
        /// </summary>
        public bool IsExported => JsonStat == "EXPORTED";
    }

    /// <summary>
    /// Statuts possibles pour les enregistrements JSON_IN
    /// </summary>
    public static class JsonInStatus
    {
        public const string Active = "ACTIVE";
        public const string Deleted = "DELETED";
        public const string Exported = "EXPORTED";
        public const string Error = "ERROR";
        public const string Processing = "PROCESSING";

        public static readonly string[] AllStatuses = { Active, Deleted, Exported, Error, Processing };

        public static bool IsValid(string status)
        {
            return AllStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Résultat d'une opération sur la base de données
    /// </summary>
    public class DatabaseOperationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int AffectedRows { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public DateTime OperationDate { get; set; } = DateTime.UtcNow;

        public static DatabaseOperationResult CreateSuccess(int affectedRows, TimeSpan executionTime)
        {
            return new DatabaseOperationResult
            {
                Success = true,
                AffectedRows = affectedRows,
                ExecutionTime = executionTime
            };
        }

        public static DatabaseOperationResult CreateError(string errorMessage)
        {
            return new DatabaseOperationResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                AffectedRows = 0
            };
        }
    }

    /// <summary>
    /// Statistiques de synchronisation par endpoint
    /// </summary>
    public class EndpointStatistics
    {
        public string EndpointName { get; set; } = "";
        public int TotalRecords { get; set; }
        public int ActiveRecords { get; set; }
        public int DeletedRecords { get; set; }
        public int ExportedRecords { get; set; }
        public DateTime? LastSyncDate { get; set; }
        public DateTime? OldestRecord { get; set; }
        public DateTime? NewestRecord { get; set; }
        public double AverageVersions { get; set; }
        public long TotalSizeBytes { get; set; }

        /// <summary>
        /// Taille totale en format lisible
        /// </summary>
        public string TotalSizeFormatted
        {
            get
            {
                if (TotalSizeBytes < 1024) return $"{TotalSizeBytes} B";
                if (TotalSizeBytes < 1024 * 1024) return $"{TotalSizeBytes / 1024.0:F1} KB";
                if (TotalSizeBytes < 1024 * 1024 * 1024) return $"{TotalSizeBytes / (1024.0 * 1024.0):F1} MB";
                return $"{TotalSizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
            }
        }

        /// <summary>
        /// Pourcentage d'enregistrements actifs
        /// </summary>
        public double ActivePercentage
        {
            get
            {
                if (TotalRecords == 0) return 0;
                return (double)ActiveRecords / TotalRecords * 100;
            }
        }
    }

    /// <summary>
    /// Configuration de la base de données
    /// </summary>
    public class DatabaseConfig
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 1433;
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public string Name { get; set; } = "";
        public int CommandTimeout { get; set; } = 60;
        public int ConnectionTimeout { get; set; } = 30;
        public bool TrustServerCertificate { get; set; } = true;

        /// <summary>
        /// Génère la chaîne de connexion SQL Server
        /// </summary>
        public string GetConnectionString()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = $"{Host},{Port}",
                InitialCatalog = Name,
                UserID = User,
                Password = Password,
                TrustServerCertificate = TrustServerCertificate,
                ConnectTimeout = ConnectionTimeout,
                CommandTimeout = CommandTimeout
            };

            return builder.ConnectionString;
        }

        /// <summary>
        /// Valide la configuration
        /// </summary>
        public (bool IsValid, string ErrorMessage) Validate()
        {
            if (string.IsNullOrEmpty(Host))
                return (false, "Host de base de données manquant");

            if (string.IsNullOrEmpty(User))
                return (false, "Utilisateur de base de données manquant");

            if (string.IsNullOrEmpty(Password))
                return (false, "Mot de passe de base de données manquant");

            if (string.IsNullOrEmpty(Name))
                return (false, "Nom de base de données manquant");

            if (Port <= 0 || Port > 65535)
                return (false, "Port de base de données invalide");

            return (true, "");
        }
    }
}