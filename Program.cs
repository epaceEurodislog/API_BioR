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
            Console.WriteLine("🔄 NOUVEAU: Confirmations automatiques Purchase/Return/Transfer/Sales Orders avec INT3PLStatus");
            Console.WriteLine("🆕 NOUVEAU: Export BL SpeedWMS vers Dynamics 365 avec ImportId\n");

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

                // 🆕 NOUVEAU: Vérifier/créer la colonne JSON_IMPORT_ID pour BLExport
                var jsonOutService = serviceProvider.GetService<JsonOutService>();
                if (!await jsonOutService.EnsureImportIdColumnExistsAsync())
                {
                    Console.WriteLine("⚠️ Problème avec la colonne JSON_IMPORT_ID, mais on continue...");
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
                var blExportService = serviceProvider.GetService<BLExportService>(); // 🆕 NOUVEAU

                await DisplayPreSyncStatisticsAsync(sqlServerService, serviceProvider);

                Console.WriteLine("🚀 === DÉBUT SYNCHRONISATION AVEC CONFIRMATIONS === 🚀\n");

                // ✅ EXISTANT: Synchronisation avec confirmations automatiques
                var syncResults = await dataService.SyncAllEndpointsWithOrderConfirmationsAsync();

                Console.WriteLine("\n📊 === RÉSULTATS DE SYNCHRONISATION === 📊");
                await DisplaySyncResultsAsync(syncResults, sqlServerService);

                // ✅ EXISTANT: Confirmation additionnelle des commandes en attente
                Console.WriteLine("\n🔄 === CONFIRMATION COMMANDES EN ATTENTE === 🔄");
                await ConfirmPendingOrdersAsync(dataService, statusConfirmationService, token);


                globalStopwatch.Stop();
                Console.WriteLine($"\n⏱️ Durée totale: {globalStopwatch.Elapsed.TotalMinutes:F1} minutes");
                Console.WriteLine("✅ === SYNCHRONISATION AVEC CONFIRMATIONS ET BLEXPORT TERMINÉE === ✅");

                // 🆕 NOUVEAU: Export BL depuis SpeedWMS
                Console.WriteLine("\n📦 === EXPORT BL SPEEDWMS === 📦");
                await ProcessBLExportAsync(blExportService, token);

                // 🆕 NOUVEAU: TRAITEMENT JOURNAUX DE RÉCEPTION
                Console.WriteLine("\n🧬 === TRAITEMENT JOURNAUX DE RÉCEPTION === 🧬");
                await ProcessItemArrivalJournalsAsync(serviceProvider, token);

                // 🚀 EXISTANT: LANCEMENT DU PROGRAMME EXTERNE
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
        /// 🆕 NOUVELLE MÉTHODE : Traite tous les journaux de réception ItemArrival
        /// </summary>
        private static async Task ProcessItemArrivalJournalsAsync(IServiceProvider serviceProvider, string token)
        {
            try
            {
                Console.WriteLine("🔍 Vérification des journaux de réception en attente...");

                var reeDataService = serviceProvider.GetRequiredService<IREEDataService>();
                var itemArrivalService = serviceProvider.GetRequiredService<IItemArrivalJournalService>();
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();

                // Diagnostic des tables REE (optionnel, pour debug initial)
                if (configuration.GetValue<bool>("ItemArrivalJournal:EnableDiagnostic", false))
                {
                    Console.WriteLine("🔍 Diagnostic des tables REE...");
                    var reeStructureReport = await reeDataService.AnalyzeREEStructureAsync();
                    Console.WriteLine(reeStructureReport);

                    var reeDiagnosticReport = await reeDataService.DiagnoseREETablesAsync();
                    Console.WriteLine(reeDiagnosticReport);
                }

                // Traitement des journaux de réception
                var itemArrivalReport = await itemArrivalService.ProcessAllJournalsAsync(token);

                if (itemArrivalReport.TotalJournals > 0)
                {
                    Console.WriteLine("\n📊 === RAPPORT JOURNAUX DE RÉCEPTION === 📊");
                    Console.WriteLine(itemArrivalReport.GetSummary());

                    // Afficher les erreurs s'il y en a
                    if (itemArrivalReport.ErrorMessages.Any())
                    {
                        Console.WriteLine("⚠️ Erreurs détectées lors du traitement:");
                        foreach (var error in itemArrivalReport.ErrorMessages.Take(5)) // Limiter à 5 erreurs pour la console
                        {
                            Console.WriteLine($"   - {error}");
                        }

                        if (itemArrivalReport.ErrorMessages.Count > 5)
                        {
                            Console.WriteLine($"   ... et {itemArrivalReport.ErrorMessages.Count - 5} autres erreurs");
                        }
                    }

                    // Statistiques détaillées
                    if (itemArrivalReport.SuccessfulJournals > 0)
                    {
                        Console.WriteLine($"✅ Journaux traités avec succès: {itemArrivalReport.SuccessfulJournals}");
                        Console.WriteLine($"📤 Total en-têtes envoyés: {itemArrivalReport.TotalHeaders}");
                        Console.WriteLine($"📄 Total lignes envoyées: {itemArrivalReport.TotalLines}");
                        Console.WriteLine($"✅ Journaux confirmés: {itemArrivalReport.ConfirmedJournals}");

                        // Indicateur visuel du résultat
                        var successRate = itemArrivalReport.SuccessRate;
                        if (successRate >= 90)
                        {
                            Console.WriteLine("🎉 Journaux de réception : EXCELLENT");
                        }
                        else if (successRate >= 70)
                        {
                            Console.WriteLine("✅ Journaux de réception : BON");
                        }
                        else if (itemArrivalReport.TotalJournals > 0)
                        {
                            Console.WriteLine("⚠️ Journaux de réception : À SURVEILLER");
                        }
                    }

                    if (itemArrivalReport.FailedJournals > 0)
                    {
                        Console.WriteLine($"❌ Journaux en échec: {itemArrivalReport.FailedJournals}");
                    }
                }
                else
                {
                    Console.WriteLine("📭 Aucun journal de réception en attente de traitement");
                }

                Console.WriteLine($"⏱️ Durée traitement journaux: {itemArrivalReport.TotalProcessingTime.TotalMinutes:F1} minutes");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors du traitement des journaux de réception: {ex.Message}");
                Console.WriteLine("⚠️ Le processus continue malgré l'erreur ItemArrival...");
            }
        }

        /// <summary>
        /// Version finale de ProcessBLExportAsync avec diagnostic ET traitement réel
        /// À REMPLACER dans Program.cs
        /// </summary>
        private static async Task ProcessBLExportAsync(BLExportService blExportService, string token)
        {
            try
            {
                Console.WriteLine("🔍 Vérification connectivité SpeedWMS et endpoints BLExport...");

                // Test de connectivité endpoints Dynamics
                var connectivityOk = await blExportService.TestDynamicsConnectivityAsync(token);
                if (!connectivityOk)
                {
                    Console.WriteLine("❌ Endpoints BLExport non accessibles, export annulé");
                    return;
                }

                Console.WriteLine("✅ Connectivité BLExport OK");

                // ✅ ACTIVATION : Traitement principal BLExport
                var statistics = await blExportService.ProcessBLExportAsync(token);

                // Affichage des résultats
                await DisplayBLExportResultsAsync(statistics);

                // Retry des confirmations échouées si nécessaire
                if (statistics.BLsWithErrors > 0)
                {
                    Console.WriteLine("\n🔄 Retry des confirmations BL en échec...");
                    var retryCount = await blExportService.RetryFailedConfirmationsAsync(token);

                    if (retryCount > 0)
                    {
                        Console.WriteLine($"✅ {retryCount} confirmations BL récupérées lors du retry");
                    }
                    else
                    {
                        Console.WriteLine("ℹ️ Aucune confirmation BL récupérée lors du retry");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de l'export BL: {ex.Message}");
                Console.WriteLine("⚠️ Le processus continue malgré l'erreur BLExport...");
            }
        }

        /// <summary>
        /// 🆕 NOUVELLE MÉTHODE : Affiche les résultats de l'export BL
        /// </summary>
        private static async Task DisplayBLExportResultsAsync(BLExportStatistics statistics)
        {
            try
            {
                Console.WriteLine("\n📊 === RÉSULTATS BLEXPORT === 📊");

                if (statistics.TotalBLsFound == 0)
                {
                    Console.WriteLine("ℹ️ Aucun BL trouvé dans SpeedWMS");
                    return;
                }

                Console.WriteLine($"🔍 BL trouvés SpeedWMS: {statistics.TotalBLsFound}");
                Console.WriteLine($"✅ BL déjà traités: {statistics.BLsAlreadyProcessed}");
                Console.WriteLine($"🔄 BL à traiter: {statistics.BLsToProcess}");
                Console.WriteLine($"✅ BL traités avec succès: {statistics.BLsProcessedSuccessfully}");
                Console.WriteLine($"❌ BL en erreur: {statistics.BLsWithErrors}");
                Console.WriteLine($"📤 Total POST envoyés: {statistics.TotalPayloadsSent}");
                Console.WriteLine($"📋 Confirmations envoyées: {statistics.ConfirmationsSent}");
                Console.WriteLine($"⏱️ Durée: {statistics.TotalProcessingTime.TotalSeconds:F1}s");

                if (statistics.BLsToProcess > 0)
                {
                    Console.WriteLine($"📈 Taux de succès: {statistics.SuccessRate:F1}%");
                }

                // Indicateur visuel du résultat
                if (statistics.SuccessRate >= 90)
                {
                    Console.WriteLine("🎉 Export BL : EXCELLENT");
                }
                else if (statistics.SuccessRate >= 70)
                {
                    Console.WriteLine("✅ Export BL : BON");
                }
                else if (statistics.BLsToProcess > 0)
                {
                    Console.WriteLine("⚠️ Export BL : À SURVEILLER");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erreur affichage résultats BLExport: {ex.Message}");
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
        /// Teste et diagnostique une Sales Order spécifique
        /// </summary>
        private static async Task DebugSpecificSalesOrderAsync(ServiceProvider serviceProvider, string salesOrderId)
        {
            try
            {
                Console.WriteLine($"\n🔍 === DEBUG SALES ORDER {salesOrderId} === 🔍");

                var sqlServerService = serviceProvider.GetService<SqlServerDatabaseService>();
                var statusConfirmationService = serviceProvider.GetService<StatusConfirmationService>();
                var authService = serviceProvider.GetService<AuthenticationService>();

                // 1. Récupérer les infos de debug
                var debugInfos = await sqlServerService.GetSalesOrderDebugInfoAsync(salesOrderId);

                Console.WriteLine($"📊 {debugInfos.Count} lignes trouvées dans JSON_IN:");
                foreach (var info in debugInfos)
                {
                    Console.WriteLine($"   {info.GetSummary()}");
                }

                if (debugInfos.Count == 0)
                {
                    Console.WriteLine("❌ Aucune donnée trouvée pour cette Sales Order");
                    return;
                }

                // 2. Tester la confirmation
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("❌ Impossible d'obtenir le token d'authentification");
                    return;
                }

                Console.WriteLine($"\n🧪 Test de confirmation Sales Order {salesOrderId}...");
                var confirmResult = await statusConfirmationService.ConfirmSalesOrderWithStatusUpdateAsync(token, salesOrderId, "ProcessedBy3PL");

                var resultIcon = confirmResult ? "✅" : "❌";
                Console.WriteLine($"{resultIcon} Résultat confirmation: {(confirmResult ? "SUCCÈS" : "ÉCHEC")}");

                // 3. Afficher les détails après tentative
                Console.WriteLine("\n📋 Détails des lignes:");
                foreach (var info in debugInfos)
                {
                    Console.WriteLine($"   📄 Ligne {info.JsonKeyU}:");
                    Console.WriteLine($"      WMSTransRecId: {info.WMSTransRecIdStr}");
                    Console.WriteLine($"      ItemId: {info.ItemId}");
                    Console.WriteLine($"      Status actuel: {info.CurrentStatus}");
                    Console.WriteLine($"      DataAreaId: {info.DataAreaId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur debug Sales Order {salesOrderId}: {ex.Message}");
            }
        }

        // USAGE: Ajoutez cet appel dans la méthode Main pour tester
        // await DebugSpecificSalesOrderAsync(serviceProvider, "SO000992");

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
            services.AddScoped<IREEDataService, REEDataService>();
            services.AddScoped<IItemArrivalJournalService, ItemArrivalJournalService>();

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

        private static async Task DisplayPreSyncStatisticsAsync(SqlServerDatabaseService sqlServerService, ServiceProvider serviceProvider)
        {
            try
            {
                var stats = await sqlServerService.GetStatisticsAsync();
                var confirmStats = await sqlServerService.GetConfirmationStatisticsAsync();

                Console.WriteLine("📈 === STATISTIQUES ACTUELLES === 📈");
                Console.WriteLine($"📦 Total enregistrements JSON_IN: {stats.TotalRecords:N0}");
                Console.WriteLine($"✅ Enregistrements actifs: {stats.ActiveRecords:N0}");
                Console.WriteLine($"🗑️ Enregistrements supprimés: {stats.DeletedRecords:N0}");
                Console.WriteLine($"🔄 Mis à jour dernières 24h: {stats.UpdatedLast24h:N0}");
                Console.WriteLine($"📤 Articles confirmés: {confirmStats.ConfirmedArticles:N0} ({confirmStats.ConfirmationRate:F1}%)");
                Console.WriteLine($"⏳ Confirmations en attente: {confirmStats.PendingConfirmations:N0}");

                // 🆕 NOUVEAU: Statistiques BLExport via JsonOutService
                try
                {
                    var jsonOutService = serviceProvider.GetService<JsonOutService>();
                    if (jsonOutService != null)
                    {
                        var blStats = await jsonOutService.GetBLExportStatisticsAsync();
                        if (blStats.TotalBLsFound > 0)
                        {
                            Console.WriteLine($"📦 BL traités (historique): {blStats.TotalBLsFound:N0}");
                            Console.WriteLine($"✅ BL confirmés: {blStats.BLsProcessedSuccessfully:N0}");
                            Console.WriteLine($"❌ BL en erreur: {blStats.BLsWithErrors:N0}");
                        }
                    }
                }
                catch (Exception blEx)
                {
                    Console.WriteLine($"⚠️ Impossible de récupérer les statistiques BLExport: {blEx.Message}");
                }

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
        /// <summary>
        /// Ajoute tous les services personnalisés de l'application
        /// </summary>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Services d'authentification et de données EXISTANTS
            services.AddScoped<AuthenticationService>();
            services.AddScoped<SqlServerDatabaseService>();
            services.AddScoped<DynamicsDataService>();
            services.AddScoped<StatusConfirmationService>();
            services.AddScoped<ExternalProgramLauncher>();
            services.AddScoped<JsonOutService>();

            // 🆕 NOUVEAUX SERVICES BLEXPORT
            services.AddScoped<SpeedWmsDataService>();
            services.AddScoped<BLExportService>();

            // Configuration HTTP Client EXISTANTS
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

            // 🆕 NOUVEAU: HTTP Client pour BLExport
            services.AddHttpClient<BLExportService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(20);
                client.DefaultRequestHeaders.Add("User-Agent", "API_BioR/2.0-BLExport");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
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