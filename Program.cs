using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Services;
using DynamicsApiToDatabase.Models;
using System.Linq;

namespace DynamicsApiToDatabase
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== API_BIOR - Synchronisation Dynamics 365 vers SQL Server ===");
            Console.WriteLine("Version SQL Server - Table JSON_IN");
            Console.WriteLine("Base de données: 7.2.160.173 - Middleware");
            Console.WriteLine("Client: BR | Environnement: SPEED\n");

            try
            {
                // Configuration des services
                var services = ConfigureServices();
                var serviceProvider = services.BuildServiceProvider();

                // Initialisation
                var globalStopwatch = Stopwatch.StartNew();

                // Affichage de la configuration
                await DisplayConfigurationAsync(serviceProvider);

                // Vérification de la base de données SQL Server
                var sqlServerService = serviceProvider.GetService<SqlServerDatabaseService>();
                if (!await sqlServerService.InitializeDatabaseAsync())
                {
                    Console.WriteLine("❌ Impossible d'initialiser la base de données SQL Server");
                    Console.WriteLine("Vérifiez la connexion vers 7.2.160.173");
                    return;
                }

                // Service d'authentification
                var authService = serviceProvider.GetService<AuthenticationService>();
                if (!authService.ValidateConfiguration())
                {
                    Console.WriteLine("❌ Configuration Azure AD invalide");
                    Console.WriteLine("Vérifiez TenantId, ClientId et ClientSecret dans appsettings.json");
                    return;
                }

                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("❌ Impossible d'obtenir le token d'authentification Azure");
                    return;
                }

                Console.WriteLine("✅ Authentification Azure réussie\n");

                // Service de synchronisation des données
                var dataService = serviceProvider.GetService<DynamicsDataService>();

                // Affichage des statistiques avant synchronisation
                await DisplayPreSyncStatisticsAsync(sqlServerService);

                // Synchronisation de tous les endpoints
                Console.WriteLine("🚀 === DÉBUT SYNCHRONISATION === 🚀\n");

                var syncResults = await dataService.SyncAllEndpointsAsync();

                // Affichage des résultats
                Console.WriteLine("\n📊 === RÉSULTATS DE SYNCHRONISATION === 📊");
                await DisplaySyncResultsAsync(syncResults, sqlServerService);

                globalStopwatch.Stop();
                Console.WriteLine($"\n⏱️ Durée totale: {globalStopwatch.Elapsed.TotalMinutes:F1} minutes");
                Console.WriteLine("✅ === SYNCHRONISATION TERMINÉE === ✅");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERREUR CRITIQUE: {ex.Message}");
                Console.WriteLine($"Détails: {ex}");
                Environment.Exit(1);
            }

            // Attendre une entrée utilisateur avant de fermer (utile en développement)
            if (Debugger.IsAttached)
            {
                Console.WriteLine("\nAppuyez sur une touche pour fermer...");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Configure tous les services de l'application
        /// </summary>
        private static IServiceCollection ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            // Logging
            services.AddApplicationLogging();

            // Services de l'application
            services.AddApplicationServices(configuration);

            return services;
        }

        /// <summary>
        /// Affiche la configuration actuelle
        /// </summary>
        private static async Task DisplayConfigurationAsync(ServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();

            Console.WriteLine("📋 === CONFIGURATION === 📋");
            Console.WriteLine($"🏢 Tenant ID: {MaskSensitiveData(configuration["TenantId"])}");
            Console.WriteLine($"🔑 Client ID: {MaskSensitiveData(configuration["ClientId"])}");
            Console.WriteLine($"🌐 Resource URL: {configuration["ResourceUrl"]}");
            Console.WriteLine($"🗄️ Base de données: {ExtractServerFromConnectionString(configuration.GetConnectionString("DefaultConnection"))}");
            Console.WriteLine($"📅 Date/Heure: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            Console.WriteLine();
        }

        /// <summary>
        /// Affiche les statistiques avant synchronisation
        /// </summary>
        private static async Task DisplayPreSyncStatisticsAsync(SqlServerDatabaseService sqlServerService)
        {
            try
            {
                var stats = await sqlServerService.GetStatisticsAsync();

                Console.WriteLine("📈 === STATISTIQUES ACTUELLES JSON_IN === 📈");
                Console.WriteLine($"📦 Total enregistrements: {stats.TotalRecords:N0}");
                Console.WriteLine($"✅ Enregistrements actifs: {stats.ActiveRecords:N0}");
                Console.WriteLine($"🗑️ Enregistrements supprimés: {stats.DeletedRecords:N0}");
                Console.WriteLine($"🔄 Mis à jour dernières 24h: {stats.UpdatedLast24h:N0}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Impossible de récupérer les statistiques: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Affiche les résultats de synchronisation
        /// </summary>
        private static async Task DisplaySyncResultsAsync(List<DynamicsApiToDatabase.Models.SyncResult> syncResults, SqlServerDatabaseService sqlServerService)
        {
            foreach (var result in syncResults)
            {
                var status = result.Success ? "✅" : "❌";
                Console.WriteLine($"{status} {result.EndpointName}:");
                Console.WriteLine($"   📥 Nouveaux: {result.NewRecords}");
                Console.WriteLine($"   🔄 Modifiés: {result.UpdatedRecords}");
                Console.WriteLine($"   ➖ Inchangés: {result.UnchangedRecords}");
                Console.WriteLine($"   🗑️ Supprimés: {result.DeletedRecords}");
                Console.WriteLine($"   ⚠️ Erreurs: {result.ErrorRecords}");
                Console.WriteLine($"   ⏱️ Durée: {result.Duration.TotalSeconds:F1}s");

                if (!result.Success)
                {
                    Console.WriteLine($"   💥 Erreur: {result.ErrorMessage}");
                }
                Console.WriteLine();
            }

            // Statistiques globales après synchronisation
            try
            {
                var finalStats = await sqlServerService.GetStatisticsAsync();

                Console.WriteLine("📊 === STATISTIQUES FINALES === 📊");
                Console.WriteLine($"📦 Total enregistrements: {finalStats.TotalRecords:N0}");
                Console.WriteLine($"✅ Enregistrements actifs: {finalStats.ActiveRecords:N0}");
                Console.WriteLine($"🗑️ Enregistrements supprimés: {finalStats.DeletedRecords:N0}");

                var totalProcessed = syncResults.Sum(r => r.NewRecords + r.UpdatedRecords + r.UnchangedRecords);
                var totalErrors = syncResults.Sum(r => r.ErrorRecords);
                var successRate = totalProcessed > 0 ? (double)(totalProcessed - totalErrors) / totalProcessed * 100 : 0;

                Console.WriteLine($"📈 Taux de succès: {successRate:F1}%");
                Console.WriteLine($"🎯 Enregistrements traités: {totalProcessed:N0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Impossible de récupérer les statistiques finales: {ex.Message}");
            }
        }

        /// <summary>
        /// Masque les données sensibles pour l'affichage
        /// </summary>
        private static string MaskSensitiveData(string? data)
        {
            if (string.IsNullOrEmpty(data) || data.Length <= 8)
                return "***";

            return data[..4] + "***" + data[^4..];
        }

        /// <summary>
        /// Extrait le serveur de la chaîne de connexion pour l'affichage
        /// </summary>
        private static string ExtractServerFromConnectionString(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return "Non configuré";

            try
            {
                var parts = connectionString.Split(';');
                var serverPart = parts.FirstOrDefault(p => p.StartsWith("Server=", StringComparison.OrdinalIgnoreCase));
                return serverPart?.Split('=')[1] ?? "Inconnu";
            }
            catch
            {
                return "Format invalide";
            }
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
            services.AddScoped<StatusConfirmationService>(); // ✅ SERVICE AJOUTÉ

            // Configuration HTTP Client avec retry policy et timeout étendu
            services.AddHttpClient<DynamicsDataService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(15); // Timeout plus long pour les grandes synchronisations
                client.DefaultRequestHeaders.Add("User-Agent", "API_BioR/2.0-SQLServer");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // ✅ Configuration HTTP Client pour le service de confirmation
            services.AddHttpClient<StatusConfirmationService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.Add("User-Agent", "API_BioR/2.0-StatusConfirmation");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // ✅ Configuration HTTP Client pour AuthenticationService
            services.AddHttpClient<AuthenticationService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Add("User-Agent", "API_BioR/2.0-Auth");
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

                // Filtrer les logs de Microsoft et System pour réduire le bruit
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);

                // Niveau de log pour l'application
                builder.SetMinimumLevel(LogLevel.Information);
            });

            return services;
        }
    }
}