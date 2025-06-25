// Fichier: Services/OrdersSyncService.cs
// Service de synchronisation des commandes (Achat, Retour, Transfert)

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service de synchronisation des commandes avec l'API Dynamics
    /// </summary>
    public class OrdersSyncService
    {
        private readonly ILogger<OrdersSyncService> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly DatabaseService _databaseService;

        public OrdersSyncService(
            ILogger<OrdersSyncService> logger,
            IConfiguration configuration,
            HttpClient httpClient,
            DatabaseService databaseService)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
            _databaseService = databaseService;
        }

        /// <summary>
        /// Synchronise toutes les commandes (Retour, Achat, Transfert)
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        public async Task SyncAllOrdersAsync(string token)
        {
            Console.WriteLine("\n🚚 === SYNCHRONISATION DES COMMANDES AVEC LIGNES MULTIPLES ===");

            var orderEndpoints = GetOrderEndpoints();
            var totalResults = new List<OrderSyncResult>();
            var globalStopwatch = Stopwatch.StartNew();

            foreach (var orderEndpoint in orderEndpoints)
            {
                Console.WriteLine($"\n📦 Synchronisation des {orderEndpoint.DisplayName}...");

                try
                {
                    var result = await SyncSingleOrderTypeAsync(token, orderEndpoint);
                    totalResults.Add(result);

                    Console.WriteLine($"✓ {orderEndpoint.DisplayName}: " +
                        $"{result.NewOrderLines} nouvelles lignes, " +
                        $"{result.UpdatedOrderLines} lignes modifiées, " +
                        $"{result.UnchangedOrderLines} lignes inchangées");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erreur lors de la synchronisation des {orderEndpoint.DisplayName}");
                    Console.WriteLine($"❌ Erreur {orderEndpoint.DisplayName}: {ex.Message}");
                }
            }

            globalStopwatch.Stop();
            DisplayGlobalSummary(totalResults, globalStopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Synchronise un type de commande spécifique
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="orderConfig">Configuration de l'endpoint</param>
        /// <returns>Résultat de la synchronisation</returns>
        public async Task<OrderSyncResult> SyncSingleOrderTypeAsync(string token, OrderEndpoint orderConfig)
        {
            var result = new OrderSyncResult { OrderType = orderConfig.DisplayName };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Récupération des données depuis l'API
                var orderLines = await FetchOrderDataFromApiAsync(token, orderConfig);
                if (orderLines == null || orderLines.Length == 0)
                {
                    Console.WriteLine($"⚠️ Aucune donnée trouvée pour {orderConfig.DisplayName}");
                    return result;
                }

                Console.WriteLine($"✓ {orderLines.Length} lignes trouvées pour {orderConfig.DisplayName}");

                // Synchronisation avec la base de données
                result = await _databaseService.SyncOrderLinesWithDatabaseAsync(orderLines, orderConfig);

                stopwatch.Stop();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la synchronisation des {orderConfig.DisplayName}");
                result.ErrorCount = 1;
                throw;
            }
        }

        /// <summary>
        /// Récupère les données d'un type de commande depuis l'API
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="orderConfig">Configuration de l'endpoint</param>
        /// <returns>Tableau des lignes de commandes</returns>
        private async Task<JsonElement[]> FetchOrderDataFromApiAsync(string token, OrderEndpoint orderConfig)
        {
            try
            {
                var url = $"{_configuration["Resource"]}{orderConfig.Endpoint}";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                _logger.LogInformation($"Appel API GET: {url}");
                Console.WriteLine($"📡 Appel API: {orderConfig.DisplayName}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API {response.StatusCode} pour {orderConfig.Name}: {errorContent}");
                    Console.WriteLine($"❌ Erreur API {response.StatusCode} pour {orderConfig.DisplayName}");
                    return null;
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✓ Données reçues: {jsonContent.Length} caractères");

                var jsonDocument = JsonDocument.Parse(jsonContent);

                if (!jsonDocument.RootElement.TryGetProperty("value", out var ordersArray))
                {
                    Console.WriteLine($"⚠️ Aucune commande trouvée pour {orderConfig.DisplayName}");
                    return new JsonElement[0];
                }

                var orderLines = ordersArray.EnumerateArray().ToArray();
                Console.WriteLine($"✅ {orderLines.Length} lignes récupérées pour {orderConfig.DisplayName}");

                return orderLines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des {orderConfig.DisplayName}");
                throw;
            }
        }

        /// <summary>
        /// Configuration des endpoints de commandes
        /// </summary>
        /// <returns>Liste des endpoints configurés</returns>
        private List<OrderEndpoint> GetOrderEndpoints()
        {
            return new List<OrderEndpoint>
            {
                new OrderEndpoint
                {
                    Name = "ReturnOrders",
                    Endpoint = "data/BRINT32ReturnOrderTables",
                    TableName = "return_orders_raw",
                    PrimaryKeyField = "ReturnItemNum",
                    LineNumberField = "LineNum",
                    DisplayName = "Commandes de Retour"
                },
                new OrderEndpoint
                {
                    Name = "PurchOrders",
                    Endpoint = "data/BRINT32PurchOrderTables",
                    TableName = "purch_orders_raw",
                    PrimaryKeyField = "PurchId",
                    LineNumberField = "LineNumber",
                    DisplayName = "Commandes d'Achat"
                },
                new OrderEndpoint
                {
                    Name = "TransferOrders",
                    Endpoint = "data/BRINT32TransferOrderTables",
                    TableName = "transfer_orders_raw",
                    PrimaryKeyField = "TransferId",
                    LineNumberField = "LineNum",
                    DisplayName = "Ordres de Transfert"
                }
            };
        }

        /// <summary>
        /// Affiche le résumé global des synchronisations
        /// </summary>
        /// <param name="results">Résultats de synchronisation</param>
        /// <param name="totalTimeMs">Temps total en millisecondes</param>
        private void DisplayGlobalSummary(List<OrderSyncResult> results, long totalTimeMs)
        {
            Console.WriteLine($"\n📋 === RÉSUMÉ GLOBAL DES COMMANDES (par lignes) ===");

            foreach (var result in results)
            {
                Console.WriteLine($"  {result.OrderType}:");
                Console.WriteLine($"    ➕ Nouvelles lignes: {result.NewOrderLines}");
                Console.WriteLine($"    🔄 Lignes modifiées: {result.UpdatedOrderLines}");
                Console.WriteLine($"    ✅ Lignes inchangées: {result.UnchangedOrderLines}");
                Console.WriteLine($"    ❌ Erreurs: {result.ErrorCount}");
            }

            var totalNew = results.Sum(r => r.NewOrderLines);
            var totalUpdated = results.Sum(r => r.UpdatedOrderLines);
            var totalErrors = results.Sum(r => r.ErrorCount);

            Console.WriteLine($"\n📊 TOTAUX GÉNÉRAUX:");
            Console.WriteLine($"    ➕ Total nouvelles lignes: {totalNew}");
            Console.WriteLine($"    🔄 Total lignes modifiées: {totalUpdated}");
            Console.WriteLine($"    ❌ Total erreurs: {totalErrors}");
            Console.WriteLine($"⏱️ Temps total commandes: {totalTimeMs}ms");
        }
    }
}