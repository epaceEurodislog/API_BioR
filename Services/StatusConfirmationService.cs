// Fichier: Services/StatusConfirmationService.cs
// Service pour envoyer la confirmation de réception vers l'API Dynamics 365
// VERSION COMPLÈTE avec confirmations Purchase/Return/Transfer et INT3PLStatus

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service pour confirmer la réception d'articles vers l'API Dynamics
    /// </summary>
    public class StatusConfirmationService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StatusConfirmationService> _logger;
        private readonly JsonOutService _jsonOutService;
        private readonly string _baseUrl;

        public StatusConfirmationService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<StatusConfirmationService> logger,
            JsonOutService jsonOutService)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _jsonOutService = jsonOutService;
            _baseUrl = configuration["ResourceUrl"]?.TrimEnd('/')
                ?? throw new ArgumentNullException("ResourceUrl manquante");
        }

        /// <summary>
        /// Confirme la réception d'un article et enregistre l'envoi
        /// </summary>
        public async Task<bool> ConfirmItemReceivedAsync(string token, string itemId)
        {
            try
            {
                _logger.LogInformation($"📤 Confirmation de réception pour l'article {itemId}");

                var endpoint = $"{_baseUrl}/data/BRINT34ReleasedProducts/Microsoft.Dynamics.DataEntities.changeStatus";

                var payload = new
                {
                    _itemId = itemId,
                    _status = "ProcessedBy3PL"
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                });

                await _jsonOutService.LogJsonSentAsync(itemId, jsonPayload, endpoint);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Confirmation réussie pour l'article {itemId}");
                    await _jsonOutService.LogJsonSentAsync($"{itemId}_RESPONSE", responseContent, "RESPONSE", null, (int)response.StatusCode);
                    return true;
                }
                else
                {
                    var errorMessage = $"HTTP {response.StatusCode}: {responseContent}";
                    _logger.LogError($"❌ Erreur confirmation {itemId}: {errorMessage}");
                    await _jsonOutService.LogJsonSentAsync($"{itemId}_ERROR", responseContent, "ERROR", null, (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception confirmation {itemId}");
                await _jsonOutService.LogJsonSentAsync($"{itemId}_EXCEPTION", ex.Message, "EXCEPTION");
                return false;
            }
        }

        /// <summary>
        /// Confirme plusieurs articles avec tracking
        /// </summary>
        public async Task<List<string>> ConfirmMultipleItemsReceivedWithTrackingAsync(string token, List<string> itemIds)
        {
            var successfullyConfirmed = new List<string>();
            int totalCount = itemIds.Count;

            _logger.LogInformation($"📤 Début confirmation pour {totalCount} articles...");

            for (int i = 0; i < itemIds.Count; i++)
            {
                var itemId = itemIds[i];
                bool success = await ConfirmItemReceivedAsync(token, itemId);

                if (success)
                {
                    successfullyConfirmed.Add(itemId);
                }

                if ((i + 1) % 5 == 0 || (i + 1) == totalCount)
                {
                    _logger.LogInformation($"📊 Progrès: {i + 1}/{totalCount} articles traités ({successfullyConfirmed.Count} confirmés)");
                }

                await Task.Delay(200);
            }

            var successRate = totalCount > 0 ? (double)successfullyConfirmed.Count / totalCount * 100 : 0;
            _logger.LogInformation($"✅ Confirmations terminées: {successfullyConfirmed.Count}/{totalCount} articles confirmés ({successRate:F1}% succès)");

            return successfullyConfirmed;
        }

        /// <summary>
        /// Méthode legacy pour compatibilité
        /// </summary>
        public async Task<int> ConfirmMultipleItemsReceivedAsync(string token, List<string> itemIds)
        {
            var confirmedItems = await ConfirmMultipleItemsReceivedWithTrackingAsync(token, itemIds);
            return confirmedItems.Count;
        }

        /// <summary>
        /// Méthode avec retry
        /// </summary>
        public async Task<bool> ConfirmItemReceivedWithRetryAsync(string token, string itemId, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                _logger.LogInformation($"🔄 Tentative {attempt}/{maxRetries} pour l'article {itemId}");

                bool success = await ConfirmItemReceivedAsync(token, itemId);

                if (success)
                {
                    if (attempt > 1)
                    {
                        _logger.LogInformation($"✅ Confirmation réussie pour {itemId} après {attempt} tentative(s)");
                    }
                    return true;
                }

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogWarning($"⚠️ Tentative {attempt} échouée pour {itemId}, nouvelle tentative dans {delay.TotalSeconds}s...");
                    await Task.Delay(delay);
                }
            }

            _logger.LogError($"❌ Échec définitif pour l'article {itemId} après {maxRetries} tentatives");
            return false;
        }

        /// <summary>
        /// Test de connectivité
        /// </summary>
        public async Task<bool> TestApiConnectivityAsync(string token)
        {
            try
            {
                _logger.LogInformation("🔍 Test de connectivité API...");

                var endpoint = $"{_baseUrl}/data/BRINT34ReleasedProducts";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await _httpClient.GetAsync($"{endpoint}?$top=1");

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ API accessible");
                    return true;
                }
                else
                {
                    _logger.LogError($"❌ API non accessible: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception lors du test de connectivité API");
                return false;
            }
        }

        // ==========================================
        // NOUVELLES MÉTHODES POUR COMMANDES
        // ==========================================

        /// <summary>
        /// Confirme une Purchase Order et met à jour INT3PLStatus
        /// </summary>
        public async Task<bool> ConfirmPurchaseOrderWithStatusUpdateAsync(string token, string purchId, string int3plStatus = "Processed")
        {
            try
            {
                _logger.LogInformation($"📤 Confirmation Purchase Order avec INT3PLStatus: {purchId}");

                var endpoint = $"{_baseUrl}/api/services/BRINT32ServiceGroup/BRINT32Service/updatePurchOrderStatus";

                var payload = new
                {
                    _dataAreaId = "BR",
                    _id = purchId,
                    _status = 2  // Processed
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                });

                await _jsonOutService.LogJsonSentAsync($"PURCH_STATUS_{purchId}", jsonPayload, endpoint);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Purchase Order {purchId} confirmée avec succès");
                    await _jsonOutService.LogSuccessAsync($"PURCH_STATUS_{purchId}", responseContent);

                    await UpdatePurchaseOrderLinesInt3PLStatusAsync(token, purchId, int3plStatus);

                    return true;
                }
                else
                {
                    var errorMessage = $"HTTP {response.StatusCode}: {responseContent}";
                    _logger.LogError($"❌ Erreur Purchase Order {purchId}: {errorMessage}");
                    await _jsonOutService.LogErrorAsync($"PURCH_STATUS_{purchId}", jsonPayload, errorMessage, (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception Purchase Order {purchId}");
                await _jsonOutService.LogErrorAsync($"PURCH_STATUS_{purchId}", "", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Confirme une Return Order et met à jour INT3PLStatus
        /// </summary>
        public async Task<bool> ConfirmReturnOrderWithStatusUpdateAsync(string token, string returnId, string int3plStatus = "Processed")
        {
            try
            {
                _logger.LogInformation($"📤 Confirmation Return Order avec INT3PLStatus: {returnId}");

                var endpoint = $"{_baseUrl}/api/services/BRINT32ServiceGroup/BRINT32Service/updateReturnOrderStatus";

                var payload = new
                {
                    _dataAreaId = "BR",
                    _id = returnId,
                    _status = 2  // Processed
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                });

                await _jsonOutService.LogJsonSentAsync($"RETURN_STATUS_{returnId}", jsonPayload, endpoint);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Return Order {returnId} confirmée avec succès");
                    await _jsonOutService.LogSuccessAsync($"RETURN_STATUS_{returnId}", responseContent);

                    await UpdateReturnOrderLinesInt3PLStatusAsync(token, returnId, int3plStatus);

                    return true;
                }
                else
                {
                    var errorMessage = $"HTTP {response.StatusCode}: {responseContent}";
                    _logger.LogError($"❌ Erreur Return Order {returnId}: {errorMessage}");
                    await _jsonOutService.LogErrorAsync($"RETURN_STATUS_{returnId}", jsonPayload, errorMessage, (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception Return Order {returnId}");
                await _jsonOutService.LogErrorAsync($"RETURN_STATUS_{returnId}", "", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Confirme une Transfer Order et met à jour INT3PLStatus
        /// </summary>
        public async Task<bool> ConfirmTransferOrderWithStatusUpdateAsync(string token, string transferId, string int3plStatus = "Processed")
        {
            try
            {
                _logger.LogInformation($"📤 Confirmation Transfer Order avec INT3PLStatus: {transferId}");

                var endpoint = $"{_baseUrl}/api/services/BRINT32ServiceGroup/BRINT32Service/updateTransferOrderStatus";

                var payload = new
                {
                    _dataAreaId = "BR",
                    _id = transferId,
                    _status = 2  // Processed
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                });

                await _jsonOutService.LogJsonSentAsync($"TRANSFER_STATUS_{transferId}", jsonPayload, endpoint);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Transfer Order {transferId} confirmée avec succès");
                    await _jsonOutService.LogSuccessAsync($"TRANSFER_STATUS_{transferId}", responseContent);

                    await UpdateTransferOrderLinesInt3PLStatusAsync(token, transferId, int3plStatus);

                    return true;
                }
                else
                {
                    var errorMessage = $"HTTP {response.StatusCode}: {responseContent}";
                    _logger.LogError($"❌ Erreur Transfer Order {transferId}: {errorMessage}");
                    await _jsonOutService.LogErrorAsync($"TRANSFER_STATUS_{transferId}", jsonPayload, errorMessage, (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception Transfer Order {transferId}");
                await _jsonOutService.LogErrorAsync($"TRANSFER_STATUS_{transferId}", "", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Confirme plusieurs Purchase Orders avec mise à jour INT3PLStatus
        /// </summary>
        public async Task<int> ConfirmMultiplePurchaseOrdersWithStatusAsync(string token, List<string> purchaseOrderIds, string int3plStatus = "Processed")
        {
            var successCount = 0;
            var totalCount = purchaseOrderIds.Count;

            _logger.LogInformation($"📤 Début confirmation de {totalCount} Purchase Orders avec INT3PLStatus...");

            for (int i = 0; i < purchaseOrderIds.Count; i++)
            {
                var purchId = purchaseOrderIds[i];

                try
                {
                    var success = await ConfirmPurchaseOrderWithStatusUpdateAsync(token, purchId, int3plStatus);

                    if (success)
                    {
                        successCount++;
                    }

                    if ((i + 1) % 5 == 0 || (i + 1) == totalCount)
                    {
                        _logger.LogInformation($"📊 Progrès Purchase Orders: {i + 1}/{totalCount} traitées ({successCount} confirmées)");
                    }

                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Erreur lors de la confirmation Purchase Order {purchId}");
                }
            }

            var successRate = totalCount > 0 ? (double)successCount / totalCount * 100 : 0;
            _logger.LogInformation($"✅ Purchase Orders confirmées: {successCount}/{totalCount} ({successRate:F1}% succès)");

            return successCount;
        }

        /// <summary>
        /// Confirme plusieurs Return Orders avec mise à jour INT3PLStatus
        /// </summary>
        public async Task<int> ConfirmMultipleReturnOrdersWithStatusAsync(string token, List<string> returnOrderIds, string int3plStatus = "Processed")
        {
            var successCount = 0;
            var totalCount = returnOrderIds.Count;

            _logger.LogInformation($"📤 Début confirmation de {totalCount} Return Orders avec INT3PLStatus...");

            for (int i = 0; i < returnOrderIds.Count; i++)
            {
                var returnId = returnOrderIds[i];

                try
                {
                    var success = await ConfirmReturnOrderWithStatusUpdateAsync(token, returnId, int3plStatus);

                    if (success)
                    {
                        successCount++;
                    }

                    if ((i + 1) % 5 == 0 || (i + 1) == totalCount)
                    {
                        _logger.LogInformation($"📊 Progrès Return Orders: {i + 1}/{totalCount} traitées ({successCount} confirmées)");
                    }

                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Erreur lors de la confirmation Return Order {returnId}");
                }
            }

            var successRate = totalCount > 0 ? (double)successCount / totalCount * 100 : 0;
            _logger.LogInformation($"✅ Return Orders confirmées: {successCount}/{totalCount} ({successRate:F1}% succès)");

            return successCount;
        }

        /// <summary>
        /// Confirme plusieurs Transfer Orders avec mise à jour INT3PLStatus
        /// </summary>
        public async Task<int> ConfirmMultipleTransferOrdersWithStatusAsync(string token, List<string> transferOrderIds, string int3plStatus = "Processed")
        {
            var successCount = 0;
            var totalCount = transferOrderIds.Count;

            _logger.LogInformation($"📤 Début confirmation de {totalCount} Transfer Orders avec INT3PLStatus...");

            for (int i = 0; i < transferOrderIds.Count; i++)
            {
                var transferId = transferOrderIds[i];

                try
                {
                    var success = await ConfirmTransferOrderWithStatusUpdateAsync(token, transferId, int3plStatus);

                    if (success)
                    {
                        successCount++;
                    }

                    if ((i + 1) % 5 == 0 || (i + 1) == totalCount)
                    {
                        _logger.LogInformation($"📊 Progrès Transfer Orders: {i + 1}/{totalCount} traitées ({successCount} confirmées)");
                    }

                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Erreur lors de la confirmation Transfer Order {transferId}");
                }
            }

            var successRate = totalCount > 0 ? (double)successCount / totalCount * 100 : 0;
            _logger.LogInformation($"✅ Transfer Orders confirmées: {successCount}/{totalCount} ({successRate:F1}% succès)");

            return successCount;
        }

        // ==========================================
        // MÉTHODES PRIVÉES
        // ==========================================

        /// <summary>
        /// Met à jour INT3PLStatus pour les lignes d'une Purchase Order
        /// </summary>
        private async Task UpdatePurchaseOrderLinesInt3PLStatusAsync(string token, string purchId, string int3plStatus)
        {
            try
            {
                _logger.LogInformation($"🔄 Mise à jour INT3PLStatus pour Purchase Order {purchId}");

                var orderLines = await GetPurchaseOrderLinesAsync(purchId);

                if (orderLines.Count == 0)
                {
                    _logger.LogWarning($"⚠️ Aucune ligne trouvée pour Purchase Order {purchId}");
                    return;
                }

                foreach (var line in orderLines)
                {
                    if (!string.IsNullOrEmpty(line.ItemId))
                    {
                        await UpdateItemInt3PLStatusAsync(token, line.ItemId, int3plStatus);
                    }
                }

                _logger.LogInformation($"✅ INT3PLStatus mis à jour pour {orderLines.Count} lignes de Purchase Order {purchId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur mise à jour INT3PLStatus Purchase Order {purchId}");
            }
        }

        /// <summary>
        /// Met à jour INT3PLStatus pour les lignes d'une Return Order
        /// </summary>
        private async Task UpdateReturnOrderLinesInt3PLStatusAsync(string token, string returnId, string int3plStatus)
        {
            try
            {
                _logger.LogInformation($"🔄 Mise à jour INT3PLStatus pour Return Order {returnId}");

                var orderLines = await GetReturnOrderLinesAsync(returnId);

                if (orderLines.Count == 0)
                {
                    _logger.LogWarning($"⚠️ Aucune ligne trouvée pour Return Order {returnId}");
                    return;
                }

                foreach (var line in orderLines)
                {
                    if (!string.IsNullOrEmpty(line.ItemId))
                    {
                        await UpdateItemInt3PLStatusAsync(token, line.ItemId, int3plStatus);
                    }
                }

                _logger.LogInformation($"✅ INT3PLStatus mis à jour pour {orderLines.Count} lignes de Return Order {returnId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur mise à jour INT3PLStatus Return Order {returnId}");
            }
        }

        /// <summary>
        /// Met à jour INT3PLStatus pour les lignes d'une Transfer Order
        /// </summary>
        private async Task UpdateTransferOrderLinesInt3PLStatusAsync(string token, string transferId, string int3plStatus)
        {
            try
            {
                _logger.LogInformation($"🔄 Mise à jour INT3PLStatus pour Transfer Order {transferId}");

                var orderLines = await GetTransferOrderLinesAsync(transferId);

                if (orderLines.Count == 0)
                {
                    _logger.LogWarning($"⚠️ Aucune ligne trouvée pour Transfer Order {transferId}");
                    return;
                }

                foreach (var line in orderLines)
                {
                    if (!string.IsNullOrEmpty(line.ItemId))
                    {
                        await UpdateItemInt3PLStatusAsync(token, line.ItemId, int3plStatus);
                    }
                }

                _logger.LogInformation($"✅ INT3PLStatus mis à jour pour {orderLines.Count} lignes de Transfer Order {transferId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur mise à jour INT3PLStatus Transfer Order {transferId}");
            }
        }

        /// <summary>
        /// Met à jour INT3PLStatus d'un article spécifique
        /// </summary>
        private async Task UpdateItemInt3PLStatusAsync(string token, string itemId, string int3plStatus)
        {
            try
            {
                var endpoint = $"{_baseUrl}/data/BRINT34ReleasedProducts(dataAreaId='BR',ItemId='{itemId}')";

                var payload = new
                {
                    INT3PLStatus = int3plStatus
                };

                var jsonPayload = JsonSerializer.Serialize(payload);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PatchAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"✅ INT3PLStatus mis à jour pour l'article {itemId}: {int3plStatus}");
                    await _jsonOutService.LogSuccessAsync($"INT3PL_{itemId}", $"INT3PLStatus updated to {int3plStatus}");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"⚠️ Erreur mise à jour INT3PLStatus pour {itemId}: {response.StatusCode} - {errorContent}");
                    await _jsonOutService.LogErrorAsync($"INT3PL_{itemId}", jsonPayload, errorContent, (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception mise à jour INT3PLStatus pour {itemId}");
                await _jsonOutService.LogErrorAsync($"INT3PL_{itemId}", "", ex.Message);
            }
        }

        /// <summary>
        /// Récupère les lignes d'une Purchase Order depuis la base de données
        /// </summary>
        private async Task<List<OrderLine>> GetPurchaseOrderLinesAsync(string purchId)
        {
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var sqlLogger = loggerFactory.CreateLogger<SqlServerDatabaseService>();
                var sqlServerService = new SqlServerDatabaseService(_configuration, sqlLogger);
                var orderLines = await sqlServerService.GetPurchaseOrderLinesAsync(purchId);

                return orderLines.Select(line => new OrderLine
                {
                    ItemId = line.ItemId,
                    OrderId = line.OrderId,
                    LineNumber = line.LineNumber,
                    Quantity = line.Quantity,
                    Status = line.Status
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération lignes Purchase Order {purchId}");
                return new List<OrderLine>();
            }
        }

        /// <summary>
        private async Task<List<OrderLine>> GetReturnOrderLinesAsync(string returnId)
        {
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var sqlLogger = loggerFactory.CreateLogger<SqlServerDatabaseService>();
                var sqlServerService = new SqlServerDatabaseService(_configuration, sqlLogger);
                var orderLines = await sqlServerService.GetReturnOrderLinesAsync(returnId);

                return orderLines.Select(line => new OrderLine
                {
                    ItemId = line.ItemId,
                    OrderId = line.OrderId,
                    LineNumber = line.LineNumber,
                    Quantity = line.Quantity,
                    Status = line.Status
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération lignes Return Order {returnId}");
                return new List<OrderLine>();
            }
        }

        private async Task<List<OrderLine>> GetTransferOrderLinesAsync(string transferId)
        {
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var sqlLogger = loggerFactory.CreateLogger<SqlServerDatabaseService>();
                var sqlServerService = new SqlServerDatabaseService(_configuration, sqlLogger);
                var orderLines = await sqlServerService.GetTransferOrderLinesAsync(transferId);

                return orderLines.Select(line => new OrderLine
                {
                    ItemId = line.ItemId,
                    OrderId = line.OrderId,
                    LineNumber = line.LineNumber,
                    Quantity = line.Quantity,
                    Status = line.Status
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur récupération lignes Transfer Order {transferId}");
                return new List<OrderLine>();
            }
        }
    }

    /// <summary>
    /// Classe pour représenter une ligne de commande
    /// </summary>
    public class OrderLine
    {
        public string ItemId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public int LineNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// Résultat de confirmation de commandes avec statistiques
    /// </summary>
    public class OrderConfirmationResult
    {
        public string OrderType { get; set; } = "";
        public int TotalOrders { get; set; }
        public int ConfirmedOrders { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";

        public int FailedOrders => TotalOrders - ConfirmedOrders;
        public double SuccessRate => TotalOrders > 0 ? (double)ConfirmedOrders / TotalOrders * 100 : 0;
        public TimeSpan Duration => EndTime - StartTime;

        public string GetSummary()
        {
            var status = Success ? "✅" : "❌";
            return $"{status} {OrderType}: {ConfirmedOrders}/{TotalOrders} confirmées ({SuccessRate:F1}%) en {Duration.TotalSeconds:F1}s";
        }

        public string GetDetailedReport()
        {
            var report = $"📊 RAPPORT CONFIRMATION {OrderType.ToUpper()}\n";
            report += $"   📅 Période: {StartTime:dd/MM/yyyy HH:mm:ss} - {EndTime:dd/MM/yyyy HH:mm:ss}\n";
            report += $"   ⏱️ Durée: {Duration.TotalMinutes:F1} minutes\n";
            report += $"   📦 Total commandes: {TotalOrders}\n";
            report += $"   ✅ Confirmées: {ConfirmedOrders}\n";
            report += $"   ❌ Échecs: {FailedOrders}\n";
            report += $"   📈 Taux de succès: {SuccessRate:F1}%\n";

            if (!Success)
            {
                report += $"   💥 Erreur: {ErrorMessage}\n";
            }

            return report;
        }
    }
}