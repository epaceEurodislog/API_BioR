// Fichier: Services/DynamicsDataService.cs
// Service de synchronisation avec confirmation de réception optimisée

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text;
using System.Diagnostics;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    public class DynamicsDataService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthenticationService _authService;
        private readonly SqlServerDatabaseService _databaseService;
        private readonly StatusConfirmationService _statusConfirmationService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DynamicsDataService> _logger;
        private readonly string _baseUrl;

        public DynamicsDataService(
            HttpClient httpClient,
            AuthenticationService authService,
            SqlServerDatabaseService databaseService,
            StatusConfirmationService statusConfirmationService,
            IConfiguration configuration,
            ILogger<DynamicsDataService> logger)
        {
            _httpClient = httpClient;
            _authService = authService;
            _databaseService = databaseService;
            _statusConfirmationService = statusConfirmationService;
            _configuration = configuration;
            _logger = logger;
            _baseUrl = configuration["ResourceUrl"] ?? throw new ArgumentNullException("ResourceUrl manquante");
        }

        /// <summary>
        /// Synchronise un endpoint spécifique vers la table JSON_IN avec confirmation optimisée
        /// </summary>
        public async Task<SyncResult> SyncEndpointAsync(string endpointName, string endpointPath, string primaryKeyField = "ItemId")
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new SyncResult { EndpointName = endpointName };

            try
            {
                _logger.LogInformation($"🔄 Début synchronisation {endpointName}...");

                var token = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Impossible d'obtenir le token d'authentification");
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                var apiData = await FetchAllDataFromEndpointAsync(endpointPath);
                _logger.LogInformation($"📊 {apiData.Count} enregistrements récupérés de l'API {endpointName}");

                if (apiData.Count == 0)
                {
                    _logger.LogWarning($"⚠️ Aucune donnée récupérée pour {endpointName}");
                    return result;
                }

                // ✅ NOUVEAU : Filtrage des éléments déjà traités
                var filteredData = await FilterAlreadyProcessedItemsAsync(apiData, endpointName, endpointPath);

                if (filteredData.Count == 0)
                {
                    _logger.LogInformation($"✅ {endpointName}: Tous les éléments sont déjà traités, rien à synchroniser");
                    result.Success = true;
                    result.UnchangedRecords = apiData.Count;
                    stopwatch.Stop();
                    result.Duration = stopwatch.Elapsed;
                    return result;
                }

                _logger.LogInformation($"🔄 {endpointName}: {filteredData.Count} éléments à traiter sur {apiData.Count} récupérés");

                var currentKeys = new List<string>();
                var confirmedItems = new List<string>();

                // ✅ Traiter seulement les données filtrées
                foreach (var item in filteredData)
                {
                    var uniqueKey = GenerateUniqueKey(item, primaryKeyField, endpointName);
                    currentKeys.Add(uniqueKey);

                    var processResult = await ProcessSingleRecordAsync(uniqueKey, item, endpointPath, result);

                    if (processResult.Success && endpointName.ToUpper() == "ARTICLES")
                    {
                        var itemId = ExtractItemId(item);
                        if (!string.IsNullOrEmpty(itemId))
                        {
                            confirmedItems.Add(itemId);
                        }
                    }
                }

                // ✅ Optimisation confirmations (uniquement pour les articles)
                if (confirmedItems.Count > 0)
                {
                    _logger.LogInformation($"📤 Vérification des confirmations pour {confirmedItems.Count} articles...");

                    var nonConfirmedItems = await FilterAlreadyConfirmedItemsAsync(confirmedItems);

                    if (nonConfirmedItems.Count > 0)
                    {
                        var confirmationCount = await _statusConfirmationService.ConfirmMultipleItemsReceivedAsync(token, nonConfirmedItems);

                        if (confirmationCount > 0)
                        {
                            var confirmedSuccessfully = nonConfirmedItems.Take(confirmationCount).ToList();
                            await _databaseService.MarkMultipleArticlesAsConfirmedAsync(confirmedSuccessfully);
                        }

                        _logger.LogInformation($"✅ {confirmationCount}/{nonConfirmedItems.Count} nouveaux articles confirmés");
                    }
                    else
                    {
                        _logger.LogInformation("ℹ️ Tous les articles étaient déjà confirmés - aucune confirmation envoyée");
                    }
                }

                // Marquer les enregistrements supprimés (utiliser TOUTES les données API, pas seulement filtrées)
                var allCurrentKeys = new List<string>();
                foreach (var item in apiData)
                {
                    var uniqueKey = GenerateUniqueKey(item, primaryKeyField, endpointName);
                    allCurrentKeys.Add(uniqueKey);
                }

                var deletedCount = await _databaseService.MarkMissingRecordsAsDeletedAsync(endpointPath, allCurrentKeys);
                result.DeletedRecords = deletedCount;

                result.Success = true;
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;

                _logger.LogInformation($"✅ {endpointName} synchronisé: {result.NewRecords} nouveaux, {result.UpdatedRecords} modifiés, {result.UnchangedRecords} inchangés, {result.DeletedRecords} supprimés en {result.Duration.TotalSeconds:F1}s");

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;

                _logger.LogError(ex, $"❌ Erreur lors de la synchronisation de {endpointName}");
                return result;
            }
        }

        /// <summary>
        /// Filtre les articles déjà confirmés pour éviter les confirmations en double (OPTIMISATION)
        /// </summary>
        private async Task<List<string>> FilterAlreadyConfirmedItemsAsync(List<string> itemIds)
        {
            try
            {
                var unconfirmedItems = await _databaseService.GetUnconfirmedArticleIdsOptimizedAsync();
                var itemsToConfirm = itemIds.Intersect(unconfirmedItems).ToList();

                var alreadyConfirmed = itemIds.Count - itemsToConfirm.Count;
                if (alreadyConfirmed > 0)
                {
                    _logger.LogInformation($"⚡ Optimisation: {alreadyConfirmed} articles déjà confirmés ignorés");
                }

                return itemsToConfirm;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du filtrage des articles confirmés");
                return itemIds;
            }
        }

        /// <summary>
        /// Traite un seul enregistrement
        /// </summary>
        private async Task<ProcessResult> ProcessSingleRecordAsync(string uniqueKey, JsonElement item, string endpointPath, SyncResult result)
        {
            try
            {
                var jsonData = item.GetRawText();
                var success = await _databaseService.InsertOrUpdateJsonDataAsync(uniqueKey, jsonData, endpointPath);

                if (success)
                {
                    lock (result)
                    {
                        result.NewRecords++;
                    }
                    return new ProcessResult { Success = true };
                }
                else
                {
                    lock (result)
                    {
                        result.ErrorRecords++;
                    }
                    return new ProcessResult { Success = false };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du traitement de l'enregistrement {uniqueKey}");
                lock (result)
                {
                    result.ErrorRecords++;
                }
                return new ProcessResult { Success = false };
            }
        }

        /// <summary>
        /// Extrait l'ItemId d'un élément JSON
        /// </summary>
        private string ExtractItemId(JsonElement item)
        {
            try
            {
                if (item.TryGetProperty("ItemId", out var itemIdProperty))
                {
                    return itemIdProperty.GetString() ?? "";
                }
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible d'extraire l'ItemId");
                return "";
            }
        }

        /// <summary>
        /// Récupère toutes les données d'un endpoint avec pagination
        /// </summary>
        private async Task<List<JsonElement>> FetchAllDataFromEndpointAsync(string endpointPath)
        {
            var allData = new List<JsonElement>();
            var url = $"{_baseUrl}{endpointPath}";
            var pageSize = 1000;
            var skip = 0;

            while (true)
            {
                var pageUrl = $"{url}?$top={pageSize}&$skip={skip}";

                try
                {
                    _logger.LogDebug($"🔍 Récupération page: skip={skip}, top={pageSize}");

                    var response = await _httpClient.GetAsync(pageUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"Erreur API: {response.StatusCode} - {errorContent}");
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(content);

                    if (jsonDoc.RootElement.TryGetProperty("value", out var valueProperty))
                    {
                        var pageData = valueProperty.EnumerateArray().ToList();

                        if (pageData.Count == 0)
                        {
                            break;
                        }

                        allData.AddRange(pageData);
                        skip += pageData.Count;

                        _logger.LogDebug($"📄 Page récupérée: {pageData.Count} enregistrements (total: {allData.Count})");

                        if (pageData.Count < pageSize)
                        {
                            break;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Structure de réponse API inattendue - propriété 'value' manquante");
                        break;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, $"Erreur HTTP lors de la récupération de la page skip={skip}");
                    throw;
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogError(ex, "Timeout lors de la récupération des données");
                    throw;
                }
            }

            return allData;
        }

        /// <summary>
        /// Génère une clé unique pour chaque enregistrement - MODIFIÉ avec PackingSlip
        /// </summary>
        private string GenerateUniqueKey(JsonElement item, string primaryKeyField, string endpointName)
        {
            try
            {
                switch (endpointName.ToUpper())
                {
                    case "ARTICLES":
                    case "BRINT34RELEASEDPRODUCTS":
                        if (item.TryGetProperty("ItemId", out var itemId) &&
                            item.TryGetProperty("dataAreaId", out var dataArea))
                        {
                            return $"ART_{dataArea.GetString()}_{itemId.GetString()}";
                        }
                        else if (item.TryGetProperty("ItemId", out var itemIdOnly))
                        {
                            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                            return $"ART_BR_{itemIdOnly.GetString()}_{timestamp}";
                        }
                        break;

                    // ✅ NOUVEAU : PackingSlip avec clé unique basée sur WMSTRansRecId
                    case "SALESORDERS":
                    case "BRPACKINGSLIPINTERFACES":
                        if (item.TryGetProperty("WMSTRansRecId", out var wmsRecId) &&
                            item.TryGetProperty("dataAreaId", out var salesDataArea))
                        {
                            return $"SALES_{salesDataArea.GetString()}_{wmsRecId.GetInt64()}";
                        }
                        else if (item.TryGetProperty("WMSTRansRecId", out var wmsRecIdOnly))
                        {
                            return $"SALES_BR_{wmsRecIdOnly.GetInt64()}";
                        }
                        break;

                    // Autres cas existants...
                    case "RETURNORDERS":
                    case "BRINT32RETURNORDERTABLES":
                        if (item.TryGetProperty("ReturnItemNum", out var returnNum) &&
                            item.TryGetProperty("LineNum", out var returnLine))
                        {
                            return $"RET_{returnNum.GetString()}_{returnLine}";
                        }
                        break;

                    case "PURCHASEORDERS":
                    case "BRINT32PURCHORDERTABLES":
                        if (item.TryGetProperty("PurchId", out var purchId) &&
                            item.TryGetProperty("LineNumber", out var purchLine))
                        {
                            return $"PURCH_{purchId.GetString()}_{purchLine}";
                        }
                        break;

                    case "TRANSFERORDERS":
                    case "BRINT32TRANSFERORDERTABLES":
                        if (item.TryGetProperty("TransferId", out var transferId) &&
                            item.TryGetProperty("LineNumber", out var transferLine))
                        {
                            return $"TRANS_{transferId.GetString()}_{transferLine}";
                        }
                        break;
                }

                // Fallback avec hash + timestamp pour garantir l'unicité
                var contentHash = ComputeContentHash(item.GetRawText());
                var ts = DateTimeOffset.Now.ToUnixTimeSeconds();
                return $"HASH_{contentHash}_{ts}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Erreur génération clé pour {endpointName}");
                var contentHash = ComputeContentHash(item.GetRawText());
                var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                return $"HASH_{contentHash}_{timestamp}";
            }
        }

        /// <summary>
        /// Filtre les éléments déjà traités selon le type d'endpoint
        /// </summary>
        private async Task<List<JsonElement>> FilterAlreadyProcessedItemsAsync(List<JsonElement> apiData, string endpointName, string endpointPath)
        {
            try
            {
                switch (endpointName.ToUpper())
                {
                    case "ARTICLES":
                    case "BRINT34RELEASEDPRODUCTS":
                        // ✅ EXISTANT : Filtrage articles via JSON_SENT
                        return await FilterUnconfirmedArticlesAsync(apiData);

                    case "PURCHASEORDERS":
                    case "BRINT32PURCHORDERTABLES":
                        return await FilterUnprocessedOrdersAsync(apiData, "PurchId", endpointPath);

                    case "RETURNORDERS":
                    case "BRINT32RETURNORDERTABLES":
                        return await FilterUnprocessedOrdersAsync(apiData, "ReturnItemNum", endpointPath);

                    case "TRANSFERORDERS":
                    case "BRINT32TRANSFERORDERTABLES":
                        return await FilterUnprocessedOrdersAsync(apiData, "TransferId", endpointPath);

                    case "SALESORDERS":
                    case "BRPACKINGSLIPINTERFACES":
                        return await FilterUnprocessedSalesOrdersAsync(apiData, endpointPath);

                    default:
                        // Pas de filtrage pour les autres endpoints
                        return apiData;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du filtrage des éléments pour {endpointName}");
                return apiData; // Retourner toutes les données en cas d'erreur
            }
        }

        /// <summary>
        /// Filtre les articles non confirmés (méthode existante renommée)
        /// </summary>
        private async Task<List<JsonElement>> FilterUnconfirmedArticlesAsync(List<JsonElement> apiData)
        {
            var itemsToProcess = new List<JsonElement>();
            var unconfirmedItems = await _databaseService.GetUnconfirmedArticleIdsOptimizedAsync();
            var unconfirmedSet = new HashSet<string>(unconfirmedItems);

            foreach (var item in apiData)
            {
                var itemId = ExtractItemId(item);
                if (!string.IsNullOrEmpty(itemId) && unconfirmedSet.Contains(itemId))
                {
                    itemsToProcess.Add(item);
                }
            }

            var filteredCount = apiData.Count - itemsToProcess.Count;
            if (filteredCount > 0)
            {
                _logger.LogInformation($"⚡ Articles: {filteredCount} déjà confirmés ignorés, {itemsToProcess.Count} à traiter");
            }

            return itemsToProcess;
        }

        /// <summary>
        /// Filtre les commandes non traitées (Purchase, Return, Transfer)
        /// </summary>
        private async Task<List<JsonElement>> FilterUnprocessedOrdersAsync(List<JsonElement> apiData, string orderIdField, string endpointPath)
        {
            var ordersToProcess = new List<JsonElement>();
            var processedOrders = await _databaseService.GetProcessedOrderIdsAsync(endpointPath);
            var processedSet = new HashSet<string>(processedOrders);

            foreach (var item in apiData)
            {
                var orderId = ExtractOrderIdFromItem(item, orderIdField);
                if (!string.IsNullOrEmpty(orderId) && !processedSet.Contains(orderId))
                {
                    ordersToProcess.Add(item);
                }
            }

            var filteredCount = apiData.Count - ordersToProcess.Count;
            if (filteredCount > 0)
            {
                _logger.LogInformation($"⚡ Commandes: {filteredCount} déjà traitées ignorées, {ordersToProcess.Count} à traiter");
            }

            return ordersToProcess;
        }

        /// <summary>
        /// Filtre les Sales Orders non traitées
        /// </summary>
        private async Task<List<JsonElement>> FilterUnprocessedSalesOrdersAsync(List<JsonElement> apiData, string endpointPath)
        {
            var ordersToProcess = new List<JsonElement>();
            var processedSalesOrders = await _databaseService.GetProcessedSalesOrderIdsAsync();
            var processedSet = new HashSet<string>(processedSalesOrders);

            foreach (var item in apiData)
            {
                var salesOrderId = ExtractOrderIdFromItem(item, "transRefId");
                if (!string.IsNullOrEmpty(salesOrderId) && !processedSet.Contains(salesOrderId))
                {
                    ordersToProcess.Add(item);
                }
            }

            var filteredCount = apiData.Count - ordersToProcess.Count;
            if (filteredCount > 0)
            {
                _logger.LogInformation($"⚡ Sales Orders: {filteredCount} déjà traitées ignorées, {ordersToProcess.Count} à traiter");
            }

            return ordersToProcess;
        }

        /// <summary>
        /// Extrait l'ID de commande depuis un élément JSON
        /// </summary>
        private string ExtractOrderIdFromItem(JsonElement item, string fieldName)
        {
            try
            {
                if (item.TryGetProperty(fieldName, out var orderIdProperty))
                {
                    return orderIdProperty.GetString() ?? "";
                }
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Impossible d'extraire {fieldName}");
                return "";
            }
        }

        /// <summary>
        /// Calcule un hash du contenu
        /// </summary>
        private string ComputeContentHash(string content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hashBytes)[..16];
        }

        /// <summary>
        /// Synchronise tous les endpoints configurés
        /// </summary>
        public async Task<List<SyncResult>> SyncAllEndpointsAsync()
        {
            var endpoints = GetConfiguredEndpoints();
            var results = new List<SyncResult>();

            _logger.LogInformation($"🚀 Début synchronisation de {endpoints.Count} endpoints");

            foreach (var endpoint in endpoints)
            {
                var result = await SyncEndpointAsync(endpoint.Name, endpoint.Path, endpoint.PrimaryKeyField);
                results.Add(result);

                await Task.Delay(1000);
            }

            return results;
        }

        /// <summary>
        /// Retourne la liste des endpoints à synchroniser
        /// </summary>
        // <summary>
        /// Retourne la liste des endpoints à synchroniser - MODIFIÉ avec PackingSlip
        /// </summary>
        private List<EndpointConfig> GetConfiguredEndpoints()
        {
            return new List<EndpointConfig>
    {
        new()
        {
            Name = "Articles",
            Path = "data/BRINT34ReleasedProducts",
            PrimaryKeyField = "ItemId",
            DisplayName = "Articles"
        },
        new()
        {
            Name = "ReturnOrders",
            Path = "data/BRINT32ReturnOrderTables",
            PrimaryKeyField = "ReturnItemNum",
            DisplayName = "Commandes de Retour"
        },
        new()
        {
            Name = "PurchaseOrders",
            Path = "data/BRINT32PurchOrderTables",
            PrimaryKeyField = "PurchId",
            DisplayName = "Commandes d'Achat"
        },
        new()
        {
            Name = "TransferOrders",
            Path = "data/BRINT32TransferOrderTables",
            PrimaryKeyField = "TransferId",
            DisplayName = "Ordres de Transfert"
        },
        new()
        {
            Name = "SalesOrders",
            Path = "data/BRPackingSlipInterfaces",
            PrimaryKeyField = "WMSTRansRecId",
            DisplayName = "Commandes de Ventes"
        }
    };
        }

        /// <summary>
        /// Obtient les statistiques de synchronisation
        /// </summary>
        public async Task<JsonInStatistics> GetSyncStatisticsAsync()
        {
            return await _databaseService.GetStatisticsAsync();
        }

        /// <summary>
        /// Synchronise un endpoint avec confirmations automatiques des commandes
        /// </summary>
        public async Task<SyncResult> SyncEndpointWithOrderConfirmationAsync(string endpointName, string endpointPath, string primaryKeyField = "ItemId")
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new SyncResult { EndpointName = endpointName };

            try
            {
                _logger.LogInformation($"🔄 Début synchronisation {endpointName} avec confirmations...");

                var token = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Impossible d'obtenir le token d'authentification");
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                var apiData = await FetchAllDataFromEndpointAsync(endpointPath);
                _logger.LogInformation($"📊 {apiData.Count} enregistrements récupérés de l'API {endpointName}");

                if (apiData.Count == 0)
                {
                    _logger.LogWarning($"⚠️ Aucune donnée récupérée pour {endpointName}");
                    return result;
                }

                var currentKeys = new List<string>();
                var orderIds = new HashSet<string>();

                // Traiter chaque enregistrement
                foreach (var item in apiData)
                {
                    var uniqueKey = GenerateUniqueKey(item, primaryKeyField, endpointName);
                    currentKeys.Add(uniqueKey);

                    var processResult = await ProcessSingleRecordAsync(uniqueKey, item, endpointPath, result);

                    // Extraire les IDs de commandes pour confirmation
                    if (processResult.Success)
                    {
                        var orderId = ExtractOrderId(item, endpointName);
                        if (!string.IsNullOrEmpty(orderId))
                        {
                            orderIds.Add(orderId);
                        }
                    }
                }

                // Confirmer les commandes selon le type d'endpoint
                if (orderIds.Count > 0)
                {
                    await ConfirmOrdersByTypeAsync(token, endpointName, orderIds.ToList());
                }

                // Marquer les enregistrements supprimés
                var deletedCount = await _databaseService.MarkMissingRecordsAsDeletedAsync(endpointPath, currentKeys);
                result.DeletedRecords = deletedCount;

                result.Success = true;
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;

                _logger.LogInformation($"✅ {endpointName} synchronisé avec confirmations: {result.NewRecords} nouveaux, {result.UpdatedRecords} modifiés, {result.UnchangedRecords} inchangés, {result.DeletedRecords} supprimés, {orderIds.Count} commandes confirmées en {result.Duration.TotalSeconds:F1}s");

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;

                _logger.LogError(ex, $"❌ Erreur lors de la synchronisation de {endpointName}");
                return result;
            }
        }

        /// <summary>
        /// Extrait l'ID de commande selon le type d'endpoint - MODIFIÉ avec PackingSlip
        /// </summary>
        private string ExtractOrderId(JsonElement item, string endpointName)
        {
            try
            {
                return endpointName.ToUpper() switch
                {
                    "PURCHASEORDERS" or "BRINT32PURCHORDERTABLES" =>
                        item.TryGetProperty("PurchId", out var purchId) ? purchId.GetString() ?? "" : "",

                    "RETURNORDERS" or "BRINT32RETURNORDERTABLES" =>
                        item.TryGetProperty("ReturnItemNum", out var returnId) ? returnId.GetString() ?? "" : "",

                    "TRANSFERORDERS" or "BRINT32TRANSFERORDERTABLES" =>
                        item.TryGetProperty("TransferId", out var transferId) ? transferId.GetString() ?? "" : "",

                    // ✅ NOUVEAU : Commandes de ventes PackingSlip
                    "SALESORDERS" or "BRPACKINGSLIPINTERFACES" =>
                        item.TryGetProperty("transRefId", out var salesId) ? salesId.GetString() ?? "" : "",

                    _ => ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Impossible d'extraire l'ID de commande pour {endpointName}");
                return "";
            }
        }

        /// <summary>
        /// Confirme les commandes selon leur type - MODIFIÉ avec PackingSlip
        /// </summary>
        private async Task ConfirmOrdersByTypeAsync(string token, string endpointName, List<string> orderIds)
        {
            try
            {
                var confirmedCount = 0;

                switch (endpointName.ToUpper())
                {
                    case "PURCHASEORDERS":
                    case "BRINT32PURCHORDERTABLES":
                        _logger.LogInformation($"📤 Confirmation de {orderIds.Count} Purchase Orders...");
                        confirmedCount = await _statusConfirmationService.ConfirmMultiplePurchaseOrdersWithStatusAsync(token, orderIds);
                        break;

                    case "RETURNORDERS":
                    case "BRINT32RETURNORDERTABLES":
                        _logger.LogInformation($"📤 Confirmation de {orderIds.Count} Return Orders...");
                        confirmedCount = await _statusConfirmationService.ConfirmMultipleReturnOrdersWithStatusAsync(token, orderIds);
                        break;

                    case "TRANSFERORDERS":
                    case "BRINT32TRANSFERORDERTABLES":
                        _logger.LogInformation($"📤 Confirmation de {orderIds.Count} Transfer Orders...");
                        confirmedCount = await _statusConfirmationService.ConfirmMultipleTransferOrdersWithStatusAsync(token, orderIds);
                        break;

                    // ✅ NOUVEAU : Commandes de ventes PackingSlip
                    case "SALESORDERS":
                    case "BRPACKINGSLIPINTERFACES":
                        _logger.LogInformation($"📤 Confirmation de {orderIds.Count} Sales Orders...");
                        // NOTE: La méthode sera ajoutée une fois la méthode de confirmation clarifiée
                        confirmedCount = await _statusConfirmationService.ConfirmMultipleSalesOrdersWithStatusAsync(token, orderIds);
                        break;

                    default:
                        _logger.LogWarning($"⚠️ Type de commande non reconnu pour confirmation: {endpointName}");
                        break;
                }

                if (confirmedCount > 0)
                {
                    _logger.LogInformation($"✅ {confirmedCount}/{orderIds.Count} commandes {endpointName} confirmées avec succès");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la confirmation des commandes {endpointName}");
            }
        }

        /// <summary>
        /// Confirme toutes les commandes actives d'un type spécifique - MODIFIÉ avec PackingSlip
        /// </summary>
        public async Task<int> ConfirmAllActiveOrdersOfTypeAsync(string orderType)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Impossible d'obtenir le token d'authentification");
                }

                var confirmedCount = 0;

                switch (orderType.ToUpper())
                {
                    case "PURCHASE":
                        var purchaseOrders = await _databaseService.GetActivePurchaseOrderIdsAsync();
                        if (purchaseOrders.Count > 0)
                        {
                            _logger.LogInformation($"📤 Confirmation de {purchaseOrders.Count} Purchase Orders actives...");
                            confirmedCount = await _statusConfirmationService.ConfirmMultiplePurchaseOrdersWithStatusAsync(token, purchaseOrders);
                        }
                        break;

                    case "RETURN":
                        var returnOrders = await _databaseService.GetActiveReturnOrderIdsAsync();
                        if (returnOrders.Count > 0)
                        {
                            _logger.LogInformation($"📤 Confirmation de {returnOrders.Count} Return Orders actives...");
                            confirmedCount = await _statusConfirmationService.ConfirmMultipleReturnOrdersWithStatusAsync(token, returnOrders);
                        }
                        break;

                    case "TRANSFER":
                        var transferOrders = await _databaseService.GetActiveTransferOrderIdsAsync();
                        if (transferOrders.Count > 0)
                        {
                            _logger.LogInformation($"📤 Confirmation de {transferOrders.Count} Transfer Orders actives...");
                            confirmedCount = await _statusConfirmationService.ConfirmMultipleTransferOrdersWithStatusAsync(token, transferOrders);
                        }
                        break;

                    // ✅ NOUVEAU : Commandes de ventes
                    case "SALES":
                        var salesOrders = await _databaseService.GetActiveSalesOrderIdsAsync();
                        if (salesOrders.Count > 0)
                        {
                            _logger.LogInformation($"📤 Confirmation de {salesOrders.Count} Sales Orders actives...");
                            confirmedCount = await _statusConfirmationService.ConfirmMultipleSalesOrdersWithStatusAsync(token, salesOrders);
                        }
                        break;

                    default:
                        _logger.LogWarning($"⚠️ Type de commande non reconnu: {orderType}");
                        break;
                }

                _logger.LogInformation($"✅ {confirmedCount} commandes {orderType} confirmées au total");
                return confirmedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la confirmation des commandes {orderType}");
                return 0;
            }
        }

        /// <summary>
        /// Synchronise tous les endpoints avec confirmations automatiques
        /// </summary>
        public async Task<List<SyncResult>> SyncAllEndpointsWithOrderConfirmationsAsync()
        {
            var endpoints = GetConfiguredEndpoints();
            var results = new List<SyncResult>();

            _logger.LogInformation($"🚀 Début synchronisation de {endpoints.Count} endpoints avec confirmations");

            foreach (var endpoint in endpoints)
            {
                SyncResult result;

                // Utiliser la méthode avec confirmations pour les commandes
                if (endpoint.Name.ToUpper().Contains("ORDER"))
                {
                    result = await SyncEndpointWithOrderConfirmationAsync(endpoint.Name, endpoint.Path, endpoint.PrimaryKeyField);
                }
                else
                {
                    // Utiliser la méthode standard pour les articles
                    result = await SyncEndpointAsync(endpoint.Name, endpoint.Path, endpoint.PrimaryKeyField);
                }

                results.Add(result);
                await Task.Delay(1000); // Pause entre les endpoints
            }

            return results;
        }

        // <summary>
        /// Confirme toutes les commandes actives (tous types) - MODIFIÉ avec PackingSlip
        /// </summary>
        public async Task<Dictionary<string, int>> ConfirmAllActiveOrdersAsync()
        {
            var results = new Dictionary<string, int>();

            try
            {
                _logger.LogInformation("🚀 Début confirmation de toutes les commandes actives...");

                // Confirmer les Purchase Orders
                var purchaseCount = await ConfirmAllActiveOrdersOfTypeAsync("PURCHASE");
                results["Purchase"] = purchaseCount;

                await Task.Delay(2000); // Pause entre les types

                // Confirmer les Return Orders
                var returnCount = await ConfirmAllActiveOrdersOfTypeAsync("RETURN");
                results["Return"] = returnCount;

                await Task.Delay(2000); // Pause entre les types

                // Confirmer les Transfer Orders
                var transferCount = await ConfirmAllActiveOrdersOfTypeAsync("TRANSFER");
                results["Transfer"] = transferCount;

                await Task.Delay(2000); // Pause entre les types

                // ✅ NOUVEAU : Confirmer les Sales Orders
                var salesCount = await ConfirmAllActiveOrdersOfTypeAsync("SALES");
                results["Sales"] = salesCount;

                var totalConfirmed = purchaseCount + returnCount + transferCount + salesCount;
                _logger.LogInformation($"🎯 Confirmation terminée: {totalConfirmed} commandes au total " +
                    $"(Purchase: {purchaseCount}, Return: {returnCount}, Transfer: {transferCount}, Sales: {salesCount})");

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la confirmation globale des commandes");
                return results;
            }
        }

        /// <summary>
        /// Résultat du traitement d'un enregistrement
        /// </summary>
        public class ProcessResult
        {
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }

}