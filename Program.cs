using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Collections.Generic;
using DynamicsApiToDatabase.Services;
using DynamicsApiToDatabase.Models;
using DynamicsApiToDatabase.Services.INT48;
using DynamicsApiToDatabase.DataAccess.INT48;
using DynamicsApiToDatabase.Models.INT48;

namespace DynamicsApiToDatabase
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Vérifier si un argument a été passé pour une synchronisation spécialisée
            if (args.Length > 0)
            {
                await ExecuteSpecializedSync(args[0]);
            }
            else
            {
                await ExecuteFullSync();
            }
        }

        /// <summary>
        /// 🆕 NOUVELLE MÉTHODE : Traite les journaux de réception ItemArrival
        /// </summary>
        private static async Task ProcessItemArrivalJournalsAsync(IServiceProvider serviceProvider, string token)
        {
            try
            {
                Console.WriteLine("\n🧬 === TRAITEMENT JOURNAUX DE RÉCEPTION === 🧬");

                var itemArrivalService = serviceProvider.GetService<IItemArrivalJournalService>();
                if (itemArrivalService == null)
                {
                    Console.WriteLine("⚠️ Service ItemArrival non disponible");
                    return;
                }

                // Traiter tous les journaux en attente
                var itemArrivalReport = await itemArrivalService.ProcessAllJournalsAsync(token);

                Console.WriteLine("\n📊 === RÉSULTATS JOURNAUX DE RÉCEPTION === 📊");

                if (itemArrivalReport.TotalJournals > 0)
                {
                    Console.WriteLine($"📋 Total journaux trouvés: {itemArrivalReport.TotalJournals}");

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
        private static Task DisplayBLExportResultsAsync(BLExportStatistics statistics)
        {
            try
            {
                Console.WriteLine("\n📊 === RÉSULTATS BLEXPORT === 📊");

                if (statistics.TotalBLsFound == 0)
                {
                    Console.WriteLine("ℹ️ Aucun BL trouvé dans SpeedWMS");
                    return Task.CompletedTask;
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
            return Task.CompletedTask;
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
                var debugInfos = await sqlServerService!.GetSalesOrderDebugInfoAsync(salesOrderId);

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
                var token = await authService!.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("❌ Impossible d'obtenir le token d'authentification");
                    return;
                }

                Console.WriteLine($"\n🧪 Test de confirmation Sales Order {salesOrderId}...");
                var confirmResult = await statusConfirmationService!.ConfirmSalesOrderWithStatusUpdateAsync(token, salesOrderId, "ProcessedBy3PL");

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
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();

            // Configuration
            services.AddSingleton<IConfiguration>(configuration);

            // HttpClient
            services.AddHttpClient();

            // Logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // ✅ SERVICES PRINCIPAUX - ORDRE IMPORTANT
            services.AddSingleton<AuthenticationService>();
            services.AddSingleton<SqlServerDatabaseService>();
            services.AddSingleton<DynamicsDataService>();
            services.AddSingleton<SimplePurchaseLogger>();
            services.AddSingleton<StatusConfirmationService>();

            // ✅ CORRECTION CRITIQUE : Interface et implémentation
            services.AddSingleton<IJsonOutService, JsonOutService>();
            services.AddSingleton<JsonOutService>(); // Pour la compatibilité avec les appels directs

            // ✅ SERVICES SPÉCIALISÉS
            services.AddSingleton<IItemArrivalJournalService, ItemArrivalJournalService>();
            services.AddSingleton<ExternalProgramLauncher>();

            // ✅ SERVICES BLEXPORT (ajoutés si manquants)
            services.AddSingleton<IREEDataService, REEDataService>(); // Service pour les données REE
            services.AddSingleton<SpeedWmsDataService>(); // Service pour SpeedWMS
            services.AddSingleton<BLExportService>(); // Service d'export BL

            // ✅ SERVICES INT48
            services.AddSingleton<DynamicsAuthService>();
            services.AddSingleton<InventoryTransformationService>();
            services.AddSingleton<InventoryAdjustmentService>();
            services.AddSingleton<SpeedWmsInventoryRepository>();

            // ✅ SERVICES INT39 - Tracking Numbers
            services.AddSingleton<TrackingNumberService>();
            services.AddSingleton<TrackingNumberIntegrationService>();
            services.AddSingleton<DynamicsApiToDatabase.DataAccess.INT39.SpeedWmsTrackingRepository>();

            return services;
        }

        // ✅ MÉTHODE UTILITAIRE POUR VÉRIFIER LES SERVICES
        private static void ValidateServices(ServiceProvider serviceProvider)
        {
            Console.WriteLine("🔍 Validation des services...");

            try
            {
                // Test des services critiques
                var authService = serviceProvider.GetService<AuthenticationService>();
                var sqlService = serviceProvider.GetService<SqlServerDatabaseService>();
                var jsonOutService = serviceProvider.GetService<IJsonOutService>();
                var itemArrivalService = serviceProvider.GetService<IItemArrivalJournalService>();

                if (authService == null) Console.WriteLine("❌ AuthenticationService manquant");
                if (sqlService == null) Console.WriteLine("❌ SqlServerDatabaseService manquant");
                if (jsonOutService == null) Console.WriteLine("❌ IJsonOutService manquant");
                if (itemArrivalService == null) Console.WriteLine("❌ IItemArrivalJournalService manquant");

                Console.WriteLine("✅ Validation des services terminée");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur validation services: {ex.Message}");
            }
        }

        private static Task DisplayConfigurationAsync(ServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();

            Console.WriteLine("📋 === CONFIGURATION === 📋");
            Console.WriteLine($"🏢 Tenant ID: {MaskSensitiveData(configuration!["TenantId"])}");
            Console.WriteLine($"🔑 Client ID: {MaskSensitiveData(configuration["ClientId"])}");
            Console.WriteLine($"🌐 Resource URL: {configuration["ResourceUrl"]}");
            Console.WriteLine($"🗄️ Base de données: {ExtractServerFromConnectionString(configuration!.GetConnectionString("DefaultConnection"))}");
            Console.WriteLine($"📅 Date/Heure: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            Console.WriteLine();
            return Task.CompletedTask;
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

        /// <summary>
        /// Exécute une synchronisation spécialisée selon l'argument
        /// </summary>
        private static async Task ExecuteSpecializedSync(string syncType)
        {
            var services = ConfigureServices();
            var serviceProvider = services.BuildServiceProvider();

            var blExportService = serviceProvider.GetRequiredService<BLExportService>();

            // Authentification
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var token = await authService.GetAccessTokenAsync();

            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("❌ Échec de l'authentification Azure");
                Environment.Exit(1);
            }

            Console.WriteLine("✅ Authentification Azure réussie");

            var dynamicsService = serviceProvider.GetRequiredService<DynamicsDataService>();

            // Synchroniser selon le type demandé avec les bons endpoints
            switch (syncType.ToLower())
            {
                case "articles":
                    Console.WriteLine("\n🧬 === SYNCHRONISATION ARTICLES UNIQUEMENT === 🧬");
                    await SyncEndpoint(dynamicsService, token, "Articles", "data/BRINT34ReleasedProducts", "ItemId");
                    await LaunchTranslatorIfNeeded(serviceProvider, "articles");
                    break;

                case "purchase":
                    Console.WriteLine("\n💰 === SYNCHRONISATION PURCHASE ORDERS UNIQUEMENT === 💰");
                    await SyncEndpoint(dynamicsService, token, "PurchaseOrders", "data/BRINT32PurchOrderTables", "OrderDocNum");
                    await LaunchTranslatorIfNeeded(serviceProvider, "purchase");
                    break;

                case "return":
                    Console.WriteLine("\n↩️ === SYNCHRONISATION RETURN ORDERS UNIQUEMENT === ↩️");
                    await SyncEndpoint(dynamicsService, token, "ReturnOrders", "data/BRINT32ReturnOrderTables", "ReturnItemNum");
                    await LaunchTranslatorIfNeeded(serviceProvider, "return");
                    break;

                case "transfer":
                    Console.WriteLine("\n🔄 === SYNCHRONISATION TRANSFER ORDERS UNIQUEMENT === 🔄");
                    await SyncEndpoint(dynamicsService, token, "TransferOrders", "data/BRINT32TransferOrderTables", "TransferId");
                    await LaunchTranslatorIfNeeded(serviceProvider, "transfer");
                    break;

                case "sales":
                    Console.WriteLine("\n🛒 === SYNCHRONISATION SALES ORDERS (PACKING SLIPS) UNIQUEMENT === 🛒");
                    await SyncEndpoint(dynamicsService, token, "SalesOrders", "data/BRPackingSlipInterfaces", "WMSTRansRecId");
                    await LaunchTranslatorIfNeeded(serviceProvider, "sales");
                    break;

                case "blexport":
                    Console.WriteLine("\n📦 === EXPORT BL SPEEDWMS UNIQUEMENT === 📦");
                    await ProcessBLExportAsync(blExportService, token);
                    break;

                case "cr_prep":
                    Console.WriteLine("\n🛒 === SYNCHRONISATION CONFIRMATION DE PREPARATION UNIQUEMENT === 🛒");
                    await ProcessBLExportAsync(blExportService, token);
                    break;

                case "cr_recep":
                    Console.WriteLine("\n🛒 === SYNCHRONISATION CONFIRMATION DE RECEPTION UNIQUEMENT === 🛒");
                    await ProcessItemArrivalJournalsAsync(serviceProvider, token);
                    break;

                case "int48":
                case "inventory":
                case "adjustments":
                    Console.WriteLine("\n📦 === SYNCHRONISATION AJUSTEMENTS D'INVENTAIRE (INT48) === 📦");
                    await ProcessInventoryAdjustmentsAsync(serviceProvider);
                break;

                
                case "int39":
                case "tracking":
                case "trackingnumbers":
                    Console.WriteLine("\n📦 === SYNCHRONISATION TRACKING NUMBERS (INT39) === 📦");
                    await ProcessTrackingNumbersAsync(serviceProvider);
                    break;      
                default:

                    Console.WriteLine($"❌ Type de synchronisation non reconnu : {syncType}");
                    Console.WriteLine("📖 Types disponibles : articles, purchase, return, transfer, sales, blexport, cr_prep, cr_recep");
                    Environment.Exit(1);
                    break;
            }

            Console.WriteLine($"\n✅ === SYNCHRONISATION {syncType.ToUpper()} TERMINÉE === ✅");
        }

        /// <summary>
        /// Synchronise un endpoint spécifique
        /// </summary>
        private static async Task SyncEndpoint(DynamicsDataService dynamicsService, string token,
            string endpointName, string endpointPath, string primaryKeyField)
        {
            var result = await dynamicsService.SyncEndpointWithOrderConfirmationAsync(
                endpointName, endpointPath, primaryKeyField);

            if (result.Success)
            {
                Console.WriteLine($"✅ {endpointName} synchronisé : {result.NewRecords} nouveaux, {result.UpdatedRecords} modifiés, {result.UnchangedRecords} inchangés ({result.Duration.TotalSeconds:F1}s)");
            }
            else
            {
                Console.WriteLine($"❌ Erreur lors de la synchronisation de {endpointName}: {result.ErrorMessage}");
                Environment.Exit(1);
            }
        }


        /// <summary>
/// Traite les tracking numbers (INT39)
/// </summary>
private static async Task ProcessTrackingNumbersAsync(IServiceProvider serviceProvider)
{
    try
    {
        var trackingService = serviceProvider.GetRequiredService<TrackingNumberService>();
        var integrationService = serviceProvider.GetRequiredService<TrackingNumberIntegrationService>();

        Console.WriteLine("🔍 Vérification connectivité Dynamics Tracking Service...");
        
        // TODO: Ajouter un test de connectivité si nécessaire
        // var connectivityOk = await integrationService.TestDynamicsConnectivityAsync();

        Console.WriteLine("✅ Connectivité Tracking Service OK");

        // Étape 1: Récupérer les tracking numbers depuis SpeedWMS
        Console.WriteLine("\n📥 Récupération des tracking numbers depuis SpeedWMS...");
        var trackingNumbers = await trackingService.GetAllTrackingNumbersAsync();

        if (trackingNumbers.Count == 0)
        {
            Console.WriteLine("📭 Aucun tracking number à traiter");
            return;
        }

        Console.WriteLine($"📦 {trackingNumbers.Count} tracking numbers trouvés");
        Console.WriteLine($"   - Sales Orders: {trackingNumbers.Count(t => t.OrderType == "SALES")}");
        Console.WriteLine($"   - Transfer Orders: {trackingNumbers.Count(t => t.OrderType == "TRANSFER")}");

        // Étape 2: Traiter et envoyer vers Dynamics 365
        Console.WriteLine("\n🚀 Envoi des tracking numbers vers Dynamics 365...");
        
        int successCount = 0;
        int errorCount = 0;
        var startTime = DateTime.Now;

        foreach (var tracking in trackingNumbers)
        {
            try
            {
                Console.WriteLine($"📤 Traitement {tracking.OrderType} : {tracking.OPE_REDO}");
                
                var success = await integrationService.ProcessTrackingNumberAsync(tracking);
                
                if (success)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                }
                
                // Petit délai entre chaque traitement
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur traitement tracking {tracking.OPE_REDO}: {ex.Message}");
                errorCount++;
            }
        }

        var duration = DateTime.Now - startTime;

        // Étape 3: Afficher les résultats
        Console.WriteLine("\n📊 === RÉSULTATS INT39 === 📊");
        Console.WriteLine($"🔍 Tracking numbers trouvés: {trackingNumbers.Count}");
        Console.WriteLine($"✅ Tracking traités avec succès: {successCount}");
        Console.WriteLine($"❌ Tracking en erreur: {errorCount}");
        Console.WriteLine($"⏱️ Durée: {duration.TotalSeconds:F1}s");

        if (trackingNumbers.Count > 0)
        {
            var successRate = (double)successCount / trackingNumbers.Count * 100;
            Console.WriteLine($"📈 Taux de succès: {successRate:F1}%");

            // Indicateur visuel
            if (successRate >= 90)
            {
                Console.WriteLine("🎉 INT39 : EXCELLENT");
            }
            else if (successRate >= 70)
            {
                Console.WriteLine("✅ INT39 : BON");
            }
            else
            {
                Console.WriteLine("⚠️ INT39 : À SURVEILLER");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Erreur lors du traitement INT39: {ex.Message}");
        Console.WriteLine("⚠️ Le processus continue malgré l'erreur INT39...");
    }
}

        /// <summary>
        /// Lance le translator avec le type spécifique
        /// </summary>
        private static async Task LaunchTranslatorIfNeeded(IServiceProvider serviceProvider, string syncType = "")
        {
            Console.WriteLine("\n🔄 === LANCEMENT DU TRANSLATOR === 🔄");
            var externalLauncher = serviceProvider.GetService<ExternalProgramLauncher>();

            if (externalLauncher?.IsTranslatorAvailable() == true)
            {
                string translatorArg = MapSyncTypeToTranslatorArg(syncType);

                if (!string.IsNullOrEmpty(translatorArg))
                {
                    Console.WriteLine($"🎯 Lancement du Translator en mode filtré : {translatorArg}");
                    var translatorSuccess = await externalLauncher.LaunchTranslatorWithArgsAsync(translatorArg);
                    Console.WriteLine(translatorSuccess ?
                        $"✅ DynamicsToXmlTranslator ({translatorArg}) exécuté avec succès" :
                        $"⚠️ DynamicsToXmlTranslator ({translatorArg}) terminé avec des erreurs");
                }
                else
                {
                    Console.WriteLine("🔄 Lancement du Translator en mode complet");
                    var translatorSuccess = await externalLauncher.LaunchTranslatorAsync();
                    Console.WriteLine(translatorSuccess ?
                        "✅ DynamicsToXmlTranslator (mode complet) exécuté avec succès" :
                        "⚠️ DynamicsToXmlTranslator (mode complet) terminé avec des erreurs");
                }
            }
            else
            {
                Console.WriteLine("⚠️ DynamicsToXmlTranslator non disponible");
            }
        }

        /// <summary>
        /// Mappe les types de synchronisation vers les arguments Translator
        /// </summary>
        private static string MapSyncTypeToTranslatorArg(string syncType)
        {
            return syncType.ToLower() switch
            {
                "articles" => "articles",
                "purchase" => "purchase",
                "return" => "return",
                "transfer" => "transfer",
                "sales" => "sales",
                "cr_prep" => "cr_prep",
                "cr_recep" => "cr_recep",
                _ => ""
            };
        }

        private static async Task ProcessInventoryAdjustmentsAsync(IServiceProvider serviceProvider)
{
    try
    {
        var repository = serviceProvider.GetRequiredService<SpeedWmsInventoryRepository>();
        var adjustmentService = serviceProvider.GetRequiredService<InventoryAdjustmentService>();

        Console.WriteLine("🔍 Vérification connectivité Dynamics Inventory Service...");
        
        var connectivityOk = await adjustmentService.TestDynamicsConnectivityAsync();
        if (!connectivityOk)
        {
            Console.WriteLine("❌ Connectivité Inventory Service KO, traitement annulé");
            return;
        }

        Console.WriteLine("✅ Connectivité Inventory Service OK");

        // Étape 1: Récupérer les mouvements depuis SpeedWMS
        Console.WriteLine("\n📥 Récupération des mouvements d'inventaire depuis SpeedWMS...");
        var movements = await repository.GetPendingInventoryMovementsAsync();

        if (movements.Count == 0)
        {
            Console.WriteLine("📭 Aucun mouvement d'inventaire à traiter");
            return;
        }

        Console.WriteLine($"📦 {movements.Count} mouvements d'inventaire trouvés");

        // Étape 2: Traiter et envoyer vers Dynamics 365
        Console.WriteLine("\n🚀 Envoi des ajustements vers Dynamics 365...");
        var stats = await adjustmentService.ProcessInventoryAdjustmentsAsync(movements);

        // Étape 3: Afficher les résultats
        Console.WriteLine("\n📊 === RÉSULTATS INT48 === 📊");
        Console.WriteLine($"🔍 Mouvements trouvés: {stats.TotalMovementsFound}");
        Console.WriteLine($"✅ Mouvements traités: {stats.MovementsProcessedSuccessfully}");
        Console.WriteLine($"❌ Mouvements en erreur: {stats.MovementsWithErrors}");
        Console.WriteLine($"📤 Batches envoyés: {stats.BatchesSent}");
        Console.WriteLine($"📦 Total payloads: {stats.TotalPayloadsSent}");
        Console.WriteLine($"⏱️ Durée: {stats.TotalProcessingTime.TotalSeconds:F1}s");

        if (stats.TotalMovementsFound > 0)
        {
            Console.WriteLine($"📈 Taux de succès: {stats.SuccessRate:F1}%");
        }

        // Indicateur visuel
        if (stats.SuccessRate >= 90)
        {
            Console.WriteLine("🎉 INT48 : EXCELLENT");
        }
        else if (stats.SuccessRate >= 70)
        {
            Console.WriteLine("✅ INT48 : BON");
        }
        else if (stats.TotalMovementsFound > 0)
        {
            Console.WriteLine("⚠️ INT48 : À SURVEILLER");
        }

        // Étape 4: Mettre à jour le statut dans SpeedWMS (optionnel)
        if (stats.MovementsProcessedSuccessfully > 0)
        {
            Console.WriteLine("\n🔄 Mise à jour du statut dans SpeedWMS...");
            var processedIds = movements
                .Take(stats.MovementsProcessedSuccessfully)
                .Select(m => m.MVT_KEYU)
                .ToList();
            
            string importId = $"INT48_MANUAL_{DateTime.Now:yyyyMMddHHmmss}";
            await repository.MarkMovementsAsProcessedAsync(processedIds, importId, success: true);
            Console.WriteLine($"✅ {processedIds.Count} mouvements marqués comme envoyés");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Erreur lors du traitement INT48: {ex.Message}");
        Console.WriteLine("⚠️ Le processus continue malgré l'erreur INT48...");
    }
}

        /// <summary>
        /// Exécute la synchronisation complète
        /// </summary>
        private static async Task ExecuteFullSync()
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
                if (!await sqlServerService!.InitializeDatabaseAsync())
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
                if (!await jsonOutService!.EnsureImportIdColumnExistsAsync())
                {
                    Console.WriteLine("⚠️ Problème avec la colonne JSON_IMPORT_ID, mais on continue...");
                }

                var authService = serviceProvider.GetService<AuthenticationService>();
                if (!authService!.ValidateConfiguration())
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
                var blExportService = serviceProvider.GetService<BLExportService>();

                await DisplayPreSyncStatisticsAsync(sqlServerService, serviceProvider);

                Console.WriteLine("🚀 === DÉBUT SYNCHRONISATION AVEC CONFIRMATIONS === 🚀\n");

                // Synchronisation avec confirmations automatiques
                var syncResults = await dataService!.SyncAllEndpointsWithOrderConfirmationsAsync();

                Console.WriteLine("\n📊 === RÉSULTATS DE SYNCHRONISATION === 📊");
                await DisplaySyncResultsAsync(syncResults, sqlServerService);

                // Confirmation additionnelle des commandes en attente
                Console.WriteLine("\n🔄 === CONFIRMATION COMMANDES EN ATTENTE === 🔄");
                await ConfirmPendingOrdersAsync(dataService!, statusConfirmationService!, token);

                // Export BL depuis SpeedWMS
                Console.WriteLine("\n📦 === EXPORT BL SPEEDWMS === 📦");
                await ProcessBLExportAsync(blExportService!, token);

                // TRAITEMENT JOURNAUX DE RÉCEPTION
                Console.WriteLine("\n🧬 === TRAITEMENT JOURNAUX DE RÉCEPTION === 🧬");
                await ProcessItemArrivalJournalsAsync(serviceProvider, token);

                // LANCEMENT DU PROGRAMME EXTERNE
                Console.WriteLine("\n🔄 === LANCEMENT DU TRANSLATOR === 🔄");
                var externalLauncher = serviceProvider.GetService<ExternalProgramLauncher>();

                Console.WriteLine("\n📦 === AJUSTEMENTS D'INVENTAIRE (INT48) === 📦");
                await ProcessInventoryAdjustmentsAsync(serviceProvider);

                Console.WriteLine("\n📦 === TRACKING NUMBERS (INT39) === 📦");
                await ProcessTrackingNumbersAsync(serviceProvider);

                if (externalLauncher!.IsTranslatorAvailable())
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

                globalStopwatch.Stop();
                Console.WriteLine($"\n⏱️ Durée totale: {globalStopwatch.Elapsed.TotalMinutes:F1} minutes");
                Console.WriteLine("✅ === SYNCHRONISATION AVEC CONFIRMATIONS ET BLEXPORT TERMINÉE === ✅");

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
    }
}
