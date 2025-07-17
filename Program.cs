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
            Console.WriteLine("Version SQL Server avec confirmations commandes - Table JSON_IN");
            Console.WriteLine("Base de données: 7.2.160.173 - Middleware");
            Console.WriteLine("Client: BR | Environnement: SPEED");
            Console.WriteLine("🔄 NOUVEAU: Confirmations automatiques Purchase/Return/Transfer Orders avec INT3PLStatus\n");

            try
            {
                var services = ConfigureServices();
                var serviceProvider = services.BuildServiceProvider();

                var globalStopwatch = Stopwatch.StartNew();

                await DisplayConfigurationAsync(serviceProvider);

                var sqlServerService = serviceProvider.GetService<SqlServerDatabaseService>();
                if (!await sqlServerService.InitializeDatabaseAsync())
                {
                    Console.WriteLine("❌ Impossible d'initialiser la base de données SQL Server");
                    Console.WriteLine("Vérifiez la connexion vers 7.2.160.173");
                    return;
                }

                // ✅ Vérifier/créer la colonne JSON_SENT
                if (!await sqlServerService.EnsureConfirmationColumnExistsAsync())
                {
                    Console.WriteLine("⚠️ Problème avec la colonne JSON_SENT, mais on continue...");
                }

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

                var dataService = serviceProvider.GetService<DynamicsDataService>();
                var statusConfirmationService = serviceProvider.GetService<StatusConfirmationService>();

                await DisplayPreSyncStatisticsAsync(sqlServerService);

                Console.WriteLine("🚀 === DÉBUT SYNCHRONISATION AVEC CONFIRMATIONS === 🚀\n");

                // ✅ NOUVELLE MÉTHODE: Synchronisation avec confirmations automatiques
                var syncResults = await dataService.SyncAllEndpointsWithOrderConfirmationsAsync();

                Console.WriteLine("\n📊 === RÉSULTATS DE SYNCHRONISATION === 📊");
                await DisplaySyncResultsAsync(syncResults, sqlServerService);

                // ✅ NOUVELLE SECTION: Confirmation additionnelle des commandes en attente
                Console.WriteLine("\n🔄 === CONFIRMATION COMMANDES EN ATTENTE === 🔄");
                await ConfirmPendingOrdersAsync(dataService, statusConfirmationService, token);

                globalStopwatch.Stop();
                Console.WriteLine($"\n⏱️ Durée totale: {globalStopwatch.Elapsed.TotalMinutes:F1} minutes");
                Console.WriteLine("✅ === SYNCHRONISATION AVEC CONFIRMATIONS TERMINÉE === ✅");

                // 🚀 LANCEMENT DU PROGRAMME EXTERNE
                Console.WriteLine("\n🔄 === LANCEMENT DU TRANSLATOR === 🔄");
                var externalLauncher = serviceProvider.GetService<ExternalProgramLauncher>();

                if (externalLauncher.IsTranslatorAvailable())
                {
                    var translatorSuccess = await externalLauncher.LaunchTranslatorAsync();

                    if (translatorSuccess)
                    {
                        Console.WriteLine("✅ DynamicsToXmlTranslator exécuté avec succès");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ DynamicsToXmlTranslator terminé avec des erreurs (mais la sync continue)");
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ DynamicsToXmlTranslator non disponible, synchronisation terminée sans traduction");
                }

                Console.WriteLine("\n🎯 === PROCESSUS COMPLET TERMINÉ === 🎯");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERREUR CRITIQUE: {ex.Message}");
                Console.WriteLine($"Détails: {ex}");
                Environment.Exit(1);
            }

            if (Debugger.IsAttached)
            {
                Console.WriteLine("\nAppuyez sur une touche pour fermer...");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Nouvelle méthode pour confirmer les commandes en attente
        /// </summary>
        private static async Task ConfirmPendingOrdersAsync(DynamicsDataService dataService, StatusConfirmationService statusConfirmationService, string token)
        {
            try
            {
                Console.WriteLine("🔍 Vérification des commandes en attente de confirmation...");

                // Confirmer toutes les commandes actives par type
                var confirmationResults = await dataService.ConfirmAllActiveOrdersAsync();

                Console.WriteLine("\n📈 === RÉSULTATS CONFIRMATIONS === 📈");

                var totalConfirmed = 0;
                foreach (var result in confirmationResults)
                {
                    var orderType = result.Key;
                    var confirmedCount = result.Value;
                    totalConfirmed += confirmedCount;

                    var icon = confirmedCount > 0 ? "✅" : "➖";
                    Console.WriteLine($"{icon} {orderType} Orders: {confirmedCount} confirmées");
                }

                if (totalConfirmed > 0)
                {
                    Console.WriteLine($"🎯 Total confirmations: {totalConfirmed} commandes");
                }
                else
                {
                    Console.WriteLine("ℹ️ Aucune commande en attente de confirmation");
                }

                // Optionnel: Confirmer spécifiquement les commandes par type si nécessaire
                if (totalConfirmed == 0)
                {
                    Console.WriteLine("🔍 Recherche de commandes spécifiques...");

                    // Essayer de confirmer les commandes une par une si la méthode globale n'a rien trouvé
                    await TrySpecificOrderConfirmationsAsync(statusConfirmationService, token);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de la confirmation des commandes: {ex.Message}");
            }
        }

        /// <summary>
        /// Essaye de confirmer des commandes spécifiques pour diagnostic
        /// </summary>
        private static async Task TrySpecificOrderConfirmationsAsync(StatusConfirmationService statusConfirmationService, string token)
        {
            try
            {
                Console.WriteLine("🧪 Test de confirmation avec commandes d'exemple...");

                // Test avec des IDs d'exemple (à adapter selon vos données)
                var testResults = new List<(string Type, string Id, bool Success)>();

                // Test Purchase Order
                var purchaseResult = await statusConfirmationService.ConfirmPurchaseOrderWithStatusUpdateAsync(token, "TEST_PURCH_001");
                testResults.Add(("Purchase", "TEST_PURCH_001", purchaseResult));

                // Test Return Order
                var returnResult = await statusConfirmationService.ConfirmReturnOrderWithStatusUpdateAsync(token, "TEST_RET_001");
                testResults.Add(("Return", "TEST_RET_001", returnResult));

                // Test Transfer Order
                var transferResult = await statusConfirmationService.ConfirmTransferOrderWithStatusUpdateAsync(token, "TEST_TRANS_001");
                testResults.Add(("Transfer", "TEST_TRANS_001", transferResult));

                Console.WriteLine("📊 Résultats des tests:");
                foreach (var (type, id, success) in testResults)
                {
                    var icon = success ? "✅" : "❌";
                    Console.WriteLine($"   {icon} {type} {id}: {(success ? "Confirmé" : "Échec")}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors des tests spécifiques: {ex.Message}");
            }
        }

        

        private static IServiceCollection ConfigureServices()
        {
            var services = new ServiceCollection();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddApplicationLogging();
            services.AddApplicationServices(configuration);

            return services;
        }

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

        private static async Task DisplayPreSyncStatisticsAsync(SqlServerDatabaseService sqlServerService)
        {
            try
            {
                var stats = await sqlServerService.GetStatisticsAsync();
                var confirmStats = await sqlServerService.GetConfirmationStatisticsAsync();

                Console.WriteLine("📈 === STATISTIQUES ACTUELLES === 📈");
                Console.WriteLine($"📦 Total enregistrements: {stats.TotalRecords:N0}");
                Console.WriteLine($"✅ Enregistrements actifs: {stats.ActiveRecords:N0}");
                Console.WriteLine($"🗑️ Enregistrements supprimés: {stats.DeletedRecords:N0}");
                Console.WriteLine($"🔄 Mis à jour dernières 24h: {stats.UpdatedLast24h:N0}");
                Console.WriteLine($"📤 Articles confirmés: {confirmStats.ConfirmedArticles:N0} ({confirmStats.ConfirmationRate:F1}%)");
                Console.WriteLine($"⏳ Confirmations en attente: {confirmStats.PendingConfirmations:N0}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Impossible de récupérer les statistiques: {ex.Message}\n");
            }
        }

        private static async Task DisplaySyncResultsAsync(List<SyncResult> syncResults, SqlServerDatabaseService sqlServerService)
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

            try
            {
                var finalStats = await sqlServerService.GetStatisticsAsync();
                var finalConfirmStats = await sqlServerService.GetConfirmationStatisticsAsync();

                Console.WriteLine("📊 === STATISTIQUES FINALES === 📊");
                Console.WriteLine($"📦 Total enregistrements: {finalStats.TotalRecords:N0}");
                Console.WriteLine($"✅ Enregistrements actifs: {finalStats.ActiveRecords:N0}");
                Console.WriteLine($"🗑️ Enregistrements supprimés: {finalStats.DeletedRecords:N0}");
                Console.WriteLine($"📤 Articles confirmés: {finalConfirmStats.ConfirmedArticles:N0} ({finalConfirmStats.ConfirmationRate:F1}%)");

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

        private static string MaskSensitiveData(string? data)
        {
            if (string.IsNullOrEmpty(data) || data.Length <= 8)
                return "***";

            return data[..4] + "***" + data[^4..];
        }

        // ✅ UNE SEULE VERSION de la méthode ExtractServerFromConnectionString
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
            services.AddScoped<StatusConfirmationService>();
            services.AddScoped<ExternalProgramLauncher>();
            services.AddScoped<JsonOutService>();

            // Configuration HTTP Client
            services.AddHttpClient<DynamicsDataService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(15);
                client.DefaultRequestHeaders.Add("User-Agent", "API_BioR/2.0-SQLServer");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            services.AddHttpClient<StatusConfirmationService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.Add("User-Agent", "API_BioR/2.0-StatusConfirmation");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

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

                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);

                builder.SetMinimumLevel(LogLevel.Information);
            });

            return services;
        }
    }
}