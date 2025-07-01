// Fichier: Program.cs (modifié pour SQL Server et table JSON_IN)
// Point d'entrée principal - Orchestration avec SQL Server

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Services;
using System.Data;

namespace DynamicsApiToDatabase
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== API_BIOR - Synchronisation Dynamics 365 vers SQL Server ===");
            Console.WriteLine("Version SQL Server - Table JSON_IN\n");

            try
            {
                // Configuration des services
                var services = ConfigureServices();
                var serviceProvider = services.BuildServiceProvider();

                // Initialisation
                var globalStopwatch = Stopwatch.StartNew();

                // Vérification de la base de données SQL Server
                var sqlServerService = serviceProvider.GetService<SqlServerDatabaseService>();
                if (!await sqlServerService.InitializeDatabaseAsync())
                {
                    Console.WriteLine("❌ Impossible d'initialiser la base de données SQL Server");
                    return;
                }

                // Service d'authentification
                var authService = serviceProvider.GetService<AuthenticationService>();
                if (!authService.ValidateConfiguration())
                {
                    Console.WriteLine("❌ Configuration invalide");
                    return;
                }

                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("❌ Impossible d'obtenir le token d'authentification");
                    return;
                }

                Console.WriteLine("✅ Authentification réussie\n");

                // Service de synchronisation des données
                var dataService = serviceProvider.GetService<DynamicsDataService>();

                // Liste des endpoints à synchroniser
                var endpoints = new[]
                {
                    new { Name = "Articles", Endpoint = "data/BRINT34ReleasedProducts" },
                    new { Name = "Commandes de Retour", Endpoint = "data/BRINT32ReturnOrderTables" },
                    new { Name = "Commandes d'Achat", Endpoint = "data/BRINT32PurchOrderTables" },
                    new { Name = "Ordres de Transfert", Endpoint = "data/BRINT32TransferOrderTables" }
                };

                // Synchronisation de chaque endpoint
                foreach (var endpoint in endpoints)
                {
                    Console.WriteLine($"\n🔄 === Synchronisation {endpoint.Name} ===");

                    try
                    {
                        // Récupérer les données de l'API
                        var data = await dataService.GetDataFromEndpointAsync(token, endpoint.Endpoint);

                        if (data?.Length > 0)
                        {
                            Console.WriteLine($"📥 {data.Length} enregistrements récupérés de l'API");

                            // Insérer/mettre à jour dans SQL Server
                            var result = await sqlServerService.InsertOrUpdateJsonDataAsync(endpoint.Endpoint, data);

                            // Marquer les enregistrements supprimés
                            var deletedCount = await sqlServerService.MarkDeletedRecordsAsync(endpoint.Endpoint, data);
                            result.DeletedRecords = deletedCount;

                            // Afficher le résumé
                            Console.WriteLine($"📊 Résumé {endpoint.Name}:");
                            Console.WriteLine($"   ✅ {result.NewRecords} nouveaux");
                            Console.WriteLine($"   🔄 {result.UpdatedRecords} mis à jour");
                            Console.WriteLine($"   ⚪ {result.UnchangedRecords} inchangés");
                            if (result.DeletedRecords > 0)
                                Console.WriteLine($"   🗑️ {result.DeletedRecords} supprimés");
                        }
                        else
                        {
                            Console.WriteLine("⚠️ Aucune donnée récupérée de l'API");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Erreur lors de la synchronisation {endpoint.Name}: {ex.Message}");
                    }
                }

                globalStopwatch.Stop();
                Console.WriteLine($"\n🎉 === Synchronisation terminée en {globalStopwatch.Elapsed.TotalSeconds:F1}s ===");

                // Affichage des statistiques finales
                await DisplayFinalStatsAsync(sqlServerService);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n💥 Erreur critique: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\nAppuyez sur une touche pour quitter...");
            Console.ReadKey();
        }

        /// <summary>
        /// Configure les services pour l'injection de dépendances
        /// </summary>
        private static ServiceCollection ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            // Logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // HTTP Client
            services.AddHttpClient();

            // Services personnalisés
            services.AddSingleton<AuthenticationService>();
            services.AddSingleton<SqlServerDatabaseService>();
            services.AddSingleton<DynamicsDataService>();

            return services;
        }

        /// <summary>
        /// Affiche les statistiques finales de la synchronisation
        /// </summary>
        private static async Task DisplayFinalStatsAsync(SqlServerDatabaseService sqlServerService)
        {
            try
            {
                Console.WriteLine("\n📈 === Statistiques de la base JSON_IN ===");

                using var connection = new Microsoft.Data.SqlClient.SqlConnection(GetConnectionString());
                await connection.OpenAsync();

                // Compter par endpoint
                var countByEndpointSql = @"
                    SELECT JSON_FROM, JSON_STAT, COUNT(*) as Count
                    FROM JSON_IN 
                    GROUP BY JSON_FROM, JSON_STAT
                    ORDER BY JSON_FROM, JSON_STAT";

                using var command = new Microsoft.Data.SqlClient.SqlCommand(countByEndpointSql, connection);
                using var reader = await command.ExecuteReaderAsync();

                var stats = new Dictionary<string, Dictionary<string, int>>();

                while (await reader.ReadAsync())
                {
                    var endpoint = reader.GetString("JSON_FROM");
                    var status = reader.GetString("JSON_STAT");
                    var count = reader.GetInt32("Count");

                    if (!stats.ContainsKey(endpoint))
                        stats[endpoint] = new Dictionary<string, int>();

                    stats[endpoint][status] = count;
                }

                foreach (var endpoint in stats)
                {
                    Console.WriteLine($"\n🔗 {endpoint.Key}:");
                    foreach (var status in endpoint.Value)
                    {
                        var icon = status.Key switch
                        {
                            "ACTIVE" => "✅",
                            "DELETED" => "🗑️",
                            "EXPORTED" => "📤",
                            _ => "⚪"
                        };
                        Console.WriteLine($"   {icon} {status.Key}: {status.Value}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Impossible d'afficher les statistiques: {ex.Message}");
            }
        }

        /// <summary>
        /// Récupère la chaîne de connexion (méthode utilitaire)
        /// </summary>
        private static string GetConnectionString()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = $"{configuration["Database:Host"]},{configuration.GetValue<int>("Database:Port", 1433)}",
                InitialCatalog = configuration["Database:Name"],
                UserID = configuration["Database:User"],
                Password = configuration["Database:Password"],
                TrustServerCertificate = true
            };

            return builder.ConnectionString;
        }
    }

    /// <summary>
    /// Extensions pour simplifier la gestion des services
    /// </summary>
    public static class ServiceExtensions
    {
        /// <summary>
        /// Ajoute tous les services personnalisés de l'application
        /// </summary>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Services d'authentification et de données
            services.AddScoped<AuthenticationService>();
            services.AddScoped<SqlServerDatabaseService>();
            services.AddScoped<DynamicsDataService>();

            // Configuration HTTP Client avec retry policy
            services.AddHttpClient<DynamicsDataService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.Add("User-Agent", "API_BioR/1.0");
            });

            return services;
        }

        /// <summary>
        /// Configure le logging pour l'application
        /// </summary>
        public static IServiceCollection AddApplicationLogging(this IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.AddConsole(options =>
                {
                    options.IncludeScopes = false;
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                });

                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.SetMinimumLevel(LogLevel.Information);
            });

            return services;
        }
    }

    /// <summary>
    /// Modèles pour les statistiques et résultats
    /// </summary>
    public class SyncStatistics
    {
        public string Endpoint { get; set; } = "";
        public int NewRecords { get; set; }
        public int UpdatedRecords { get; set; }
        public int UnchangedRecords { get; set; }
        public int DeletedRecords { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public int TotalProcessed => NewRecords + UpdatedRecords + UnchangedRecords;

        public override string ToString()
        {
            if (!Success)
                return $"❌ {Endpoint}: Erreur - {ErrorMessage}";

            return $"✅ {Endpoint}: {NewRecords} nouveaux, {UpdatedRecords} MAJ, {UnchangedRecords} inchangés, {DeletedRecords} supprimés ({Duration.TotalSeconds:F1}s)";
        }
    }

    /// <summary>
    /// Configuration des endpoints à synchroniser
    /// </summary>
    public static class EndpointConfiguration
    {
        public static readonly (string Name, string Endpoint, string Description)[] SyncEndpoints =
        {
            ("Articles", "data/BRINT34ReleasedProducts", "Référentiel des produits"),
            ("Commandes de Retour", "data/BRINT32ReturnOrderTables", "Lignes de commandes de retour"),
            ("Commandes d'Achat", "data/BRINT32PurchOrderTables", "Lignes de commandes d'achat"),
            ("Ordres de Transfert", "data/BRINT32TransferOrderTables", "Lignes d'ordres de transfert")
        };

        /// <summary>
        /// Retourne la configuration d'un endpoint spécifique
        /// </summary>
        public static (string Name, string Endpoint, string Description)? GetEndpointConfig(string endpointName)
        {
            return SyncEndpoints.FirstOrDefault(e => e.Name.Equals(endpointName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Retourne tous les noms d'endpoints disponibles
        /// </summary>
        public static string[] GetAvailableEndpointNames()
        {
            return SyncEndpoints.Select(e => e.Name).ToArray();
        }
    }

    /// <summary>
    /// Utilitaires pour l'affichage console
    /// </summary>
    public static class ConsoleHelper
    {
        public static void WriteHeader(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"🔷 === {title} ===");
        }

        public static void WriteSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ {message}");
            Console.ResetColor();
        }

        public static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ {message}");
            Console.ResetColor();
        }

        public static void WriteWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ {message}");
            Console.ResetColor();
        }

        public static void WriteInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"ℹ️ {message}");
            Console.ResetColor();
        }

        public static void WriteSeparator()
        {
            Console.WriteLine(new string('-', 80));
        }
    }
}