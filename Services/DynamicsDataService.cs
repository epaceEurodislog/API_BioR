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
        private readonly StatusConfirmationService _confirmationService;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

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
            _confirmationService = statusConfirmationService; // ✅ AJOUT pour compatibilité
            _configuration = configuration;
            _logger = logger;
            _baseUrl = configuration["ResourceUrl"] ?? throw new ArgumentNullException("ResourceUrl manquante");
        }

        /// <summary>
        /// Synchronise un endpoint spécifique - avec logique par date UNIQUEMENT pour les articles
        /// CONSERVE la logique hash existante pour tous les autres endpoints
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

                // 🎯 DÉCISION : Utiliser la logique par date UNIQUEMENT pour les articles
                bool isArticlesEndpoint = endpointName.ToUpper().Contains("ARTICLE") ||
                                         endpointPath.Contains("BRINT34ReleasedProducts");

                if (isArticlesEndpoint)
                {
                    // 🆕 NOUVELLE LOGIQUE PAR DATE pour les articles
                    _logger.LogInformation("📅 Synchronisation articles par date de modification");
                    await SyncArticlesByModificationDateAsync(apiData, endpointPath, primaryKeyField, result);
                }
                else
                {
                    // 🔄 *** LOGIQUE HASH EXISTANTE CONSERVÉE *** pour les autres endpoints
                    _logger.LogInformation($"🔗 Synchronisation {endpointName} par hash (logique existante conservée)");
                    await SyncByExistingHashLogicAsync(apiData, endpointPath, primaryKeyField, result, endpointName);
                }

                // Marquer les enregistrements supprimés (commun à tous)
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
        /// 🆕 CORRECTION pour la confirmation des articles - Utilise la VRAIE méthode
        /// </summary>
        private async Task SyncArticlesByModificationDateAsync(List<JsonElement> apiData, string endpointPath, string primaryKeyField, SyncResult result)
        {
            foreach (var item in apiData)
            {
                var uniqueKey = GenerateUniqueKey(item, primaryKeyField, "Articles");
                var currentContent = JsonSerializer.Serialize(item, _jsonOptions);

                // 📅 Extraction de la date de modification de l'article
                var itemModificationDate = ExtractArticleModificationDate(item);

                // Récupérer la date de modification stockée en BDD
                var dbModificationDate = await _databaseService.GetStoredModificationDateAsync(endpointPath, uniqueKey);

                bool needsUpdate = false;
                string operation = "";

                if (dbModificationDate == null)
                {
                    // Nouvel article
                    needsUpdate = true;
                    operation = "NOUVEAU";
                    result.NewRecords++;
                }
                else if (itemModificationDate > dbModificationDate)
                {
                    // Article modifié (date API plus récente que BDD)
                    needsUpdate = true;
                    operation = "MODIFIÉ";
                    result.UpdatedRecords++;

                    // 🔍 DEBUG pour MPL42PP
                    if (uniqueKey.Contains("MPL42PP"))
                    {
                        Console.WriteLine($"🔍 Article MPL42PP MODIFIÉ DÉTECTÉ:");
                        Console.WriteLine($"   Date API: {itemModificationDate:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"   Date BDD: {dbModificationDate:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"   Différence: {(itemModificationDate - dbModificationDate.Value).TotalMinutes:F1} minutes");

                        // Afficher les champs qui ont changé
                        var productLifecycleStateId = ExtractFieldValue(item, "ProductLifecycleStateId");
                        Console.WriteLine($"   ProductLifecycleStateId: {productLifecycleStateId}");
                    }
                }
                else
                {
                    // Aucune modification
                    operation = "INCHANGÉ";
                    result.UnchangedRecords++;
                }

                if (needsUpdate)
                {
                    // Stocker avec la date de modification
                    var inserted = await _databaseService.InsertOrUpdateJsonDataWithDateAsync(
                        uniqueKey, currentContent, endpointPath, itemModificationDate);

                    if (inserted)
                    {
                        _logger.LogDebug($"✅ {operation}: {uniqueKey}");
                    }
                    else
                    {
                        result.ErrorRecords++;
                        _logger.LogWarning($"❌ Erreur insertion {uniqueKey}");
                    }
                }
            }

            // ✅ CORRECTION confirmation des articles - Utilise la VRAIE méthode
            if (result.NewRecords > 0 || result.UpdatedRecords > 0)
            {
                var itemIds = apiData.Select(item => ExtractFieldValue(item, primaryKeyField)).ToList();
                var itemsToConfirm = await FilterAlreadyConfirmedItemsAsync(itemIds);

                if (itemsToConfirm.Count > 0)
                {
                    // ✅ UTILISE la VRAIE méthode qui existe dans StatusConfirmationService
                    var token = await _authService.GetAccessTokenAsync();
                    if (!string.IsNullOrEmpty(token))
                    {
                        var confirmationCount = await _statusConfirmationService.ConfirmMultipleItemsReceivedAsync(token, itemsToConfirm);
                        _logger.LogInformation($"✅ {confirmationCount} articles confirmés");
                    }
                }
                else
                {
                    _logger.LogInformation("ℹ️ Tous les articles étaient déjà confirmés - aucune confirmation envoyée");
                }
            }
        }

        /// <summary>
        /// Extrait la valeur d'un champ spécifique depuis un JsonElement
        /// </summary>
        private string ExtractFieldValue(JsonElement item, string fieldName)
        {
            try
            {
                if (item.TryGetProperty(fieldName, out var property))
                {
                    return property.GetString() ?? "";
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
        /// 📅 Extrait la date de modification spécifiquement pour les articles
        /// </summary>
        private DateTime ExtractArticleModificationDate(JsonElement item)
        {
            try
            {
                // Pour les articles, utiliser BRModifiedDateTime en priorité, puis InventTableModifiedDateTime
                string[] dateFields = { "BRModifiedDateTime", "InventTableModifiedDateTime", "UnitConversionModifiedDateTime" };

                foreach (var field in dateFields)
                {
                    if (item.TryGetProperty(field, out var dateProperty) &&
                        dateProperty.ValueKind == JsonValueKind.String)
                    {
                        var dateString = dateProperty.GetString();
                        if (!string.IsNullOrEmpty(dateString) &&
                            DateTime.TryParse(dateString, out var parsedDate))
                        {
                            return parsedDate.ToUniversalTime();
                        }
                    }
                }

                // Si aucune date trouvée, utiliser la date actuelle
                _logger.LogWarning("⚠️ Aucune date de modification trouvée pour l'article, utilisation de la date actuelle");
                return DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur extraction date de modification pour l'article");
                return DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 🔄 *** CONSERVATION TOTALE DE LA LOGIQUE HASH EXISTANTE *** pour les autres endpoints
        /// Cette méthode reproduit EXACTEMENT le comportement existant avant modification
        /// </summary>
        private async Task SyncByExistingHashLogicAsync(List<JsonElement> apiData, string endpointPath, string primaryKeyField, SyncResult result, string endpointName)
        {
            // ✅ REPRODUCTION EXACTE DE LA LOGIQUE EXISTANTE

            // ✅ NOUVEAU : Filtrage des éléments déjà traités (logique existante)
            var filteredData = await FilterAlreadyProcessedItemsAsync(apiData, endpointName, endpointPath);

            if (filteredData.Count == 0)
            {
                _logger.LogInformation($"✅ {endpointName}: Tous les éléments sont déjà traités, rien à synchroniser");
                result.Success = true;
                result.UnchangedRecords = apiData.Count;
                return;
            }

            _logger.LogInformation($"🔄 {endpointName}: {filteredData.Count}/{apiData.Count} éléments à traiter");

            // ✅ TRAITEMENT EXISTANT : Insérer chaque élément filtré
            foreach (var item in filteredData)
            {
                var uniqueKey = GenerateUniqueKey(item, primaryKeyField, endpointName);
                var currentContent = JsonSerializer.Serialize(item, _jsonOptions);

                // ✅ LOGIQUE HASH EXISTANTE CONSERVÉE
                var inserted = await _databaseService.InsertOrUpdateJsonDataAsync(uniqueKey, currentContent, endpointPath);

                if (inserted)
                {
                    result.NewRecords++; // Dans la logique existante, les éléments filtrés sont considérés comme nouveaux
                    _logger.LogDebug($"✅ Traité: {uniqueKey}");
                }
                else
                {
                    result.ErrorRecords++;
                    _logger.LogWarning($"❌ Erreur insertion {uniqueKey}");
                }
            }

            result.UnchangedRecords = apiData.Count - filteredData.Count;

            // ✅ CONFIRMATIONS EXISTANTES pour les commandes - CORRIGÉ
            if (endpointName.ToUpper().Contains("ORDER"))
            {
                var confirmationCount = await ConfirmOrdersByTypeCorrectAsync(endpointName, filteredData);
                _logger.LogInformation($"✅ {confirmationCount} commandes {endpointName} confirmées");
            }
        }

        /// <summary>
        /// ✅ MÉTHODE CORRIGÉE - Utilise les vraies méthodes existantes de StatusConfirmationService
        /// </summary>
        private async Task<int> ConfirmOrdersByTypeCorrectAsync(string endpointName, List<JsonElement> orders)
        {
            try
            {
                // Obtenir le token pour les confirmations
                var token = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("Token d'authentification manquant pour les confirmations");
                    return 0;
                }

                switch (endpointName.ToUpper())
                {
                    case "PURCHASEORDERS":
                    case "BRINT32PURCHORDERTABLES":
                        // Extraire les IDs de Purchase Orders et utiliser la VRAIE méthode
                        var purchaseIds = orders.Select(o => ExtractFieldValue(o, "PurchId"))
                                               .Where(id => !string.IsNullOrEmpty(id))
                                               .ToList();
                        if (purchaseIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultiplePurchaseOrdersWithStatusAsync(token, purchaseIds);
                        }
                        break;

                    case "RETURNORDERS":
                    case "BRINT32RETURNORDERTABLES":
                        // Extraire les IDs de Return Orders et utiliser la VRAIE méthode
                        var returnIds = orders.Select(o => ExtractFieldValue(o, "ReturnItemNum"))
                                             .Where(id => !string.IsNullOrEmpty(id))
                                             .ToList();
                        if (returnIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultipleReturnOrdersWithStatusAsync(token, returnIds);
                        }
                        break;

                    case "TRANSFERORDERS":
                    case "BRINT32TRANSFERORDERTABLES":
                        // Extraire les IDs de Transfer Orders et utiliser la VRAIE méthode
                        var transferIds = orders.Select(o => ExtractFieldValue(o, "TransferId"))
                                               .Where(id => !string.IsNullOrEmpty(id))
                                               .ToList();
                        if (transferIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultipleTransferOrdersWithStatusAsync(token, transferIds);
                        }
                        break;

                    case "SALESORDERS":
                    case "BRPACKINGSLIPINTERFACES":
                        // Extraire les IDs de Sales Orders et utiliser la VRAIE méthode
                        var salesIds = orders.Select(o => ExtractFieldValue(o, "transRefId"))
                                            .Where(id => !string.IsNullOrEmpty(id))
                                            .ToList();
                        if (salesIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultipleSalesOrdersWithStatusAsync(token, salesIds);
                        }
                        break;

                    default:
                        _logger.LogWarning($"⚠️ Type de commande non reconnu: {endpointName}");
                        return 0;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la confirmation des commandes {endpointName}");
                return 0;
            }
        }

        /// <summary>
        /// ✅ MÉTHODE CORRIGÉE - Confirme les commandes par type en utilisant les bonnes méthodes existantes
        /// </summary>
        private async Task<int> ConfirmOrdersByTypeAsync(string endpointName, List<JsonElement> orders)
        {
            try
            {
                // Obtenir le token pour les confirmations
                var token = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("Token d'authentification manquant pour les confirmations");
                    return 0;
                }

                switch (endpointName.ToUpper())
                {
                    case "PURCHASEORDERS":
                    case "BRINT32PURCHORDERTABLES":
                        // Extraire les IDs de Purchase Orders et utiliser la bonne méthode
                        var purchaseIds = orders.Select(o => ExtractFieldValue(o, "PurchId"))
                                               .Where(id => !string.IsNullOrEmpty(id))
                                               .ToList();
                        if (purchaseIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultiplePurchaseOrdersWithStatusAsync(token, purchaseIds);
                        }
                        break;

                    case "RETURNORDERS":
                    case "BRINT32RETURNORDERTABLES":
                        // Extraire les IDs de Return Orders et utiliser la bonne méthode
                        var returnIds = orders.Select(o => ExtractFieldValue(o, "ReturnItemNum"))
                                             .Where(id => !string.IsNullOrEmpty(id))
                                             .ToList();
                        if (returnIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultipleReturnOrdersWithStatusAsync(token, returnIds);
                        }
                        break;

                    case "TRANSFERORDERS":
                    case "BRINT32TRANSFERORDERTABLES":
                        // Extraire les IDs de Transfer Orders et utiliser la bonne méthode
                        var transferIds = orders.Select(o => ExtractFieldValue(o, "TransferId"))
                                               .Where(id => !string.IsNullOrEmpty(id))
                                               .ToList();
                        if (transferIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultipleTransferOrdersWithStatusAsync(token, transferIds);
                        }
                        break;

                    case "SALESORDERS":
                    case "BRPACKINGSLIPINTERFACES":
                        // Extraire les IDs de Sales Orders et utiliser la bonne méthode
                        var salesIds = orders.Select(o => ExtractFieldValue(o, "transRefId"))
                                            .Where(id => !string.IsNullOrEmpty(id))
                                            .ToList();
                        if (salesIds.Count > 0)
                        {
                            return await _statusConfirmationService.ConfirmMultipleSalesOrdersWithStatusAsync(token, salesIds);
                        }
                        break;

                    default:
                        _logger.LogWarning($"⚠️ Type de commande non reconnu: {endpointName}");
                        return 0;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la confirmation des commandes {endpointName}");
                return 0;
            }
        }

        /// <summary>
        /// ✅ NOUVELLE MÉTHODE - Version corrigée avec la bonne signature
        /// </summary>
        private async Task<int> ConfirmOrdersByTypeAsync(string token, string endpointName, List<string> orderIds)
        {
            try
            {
                if (string.IsNullOrEmpty(token) || orderIds.Count == 0)
                {
                    return 0;
                }

                switch (endpointName.ToUpper())
                {
                    case "PURCHASEORDERS":
                    case "BRINT32PURCHORDERTABLES":
                        return await _statusConfirmationService.ConfirmMultiplePurchaseOrdersWithStatusAsync(token, orderIds);

                    case "RETURNORDERS":
                    case "BRINT32RETURNORDERTABLES":
                        return await _statusConfirmationService.ConfirmMultipleReturnOrdersWithStatusAsync(token, orderIds);

                    case "TRANSFERORDERS":
                    case "BRINT32TRANSFERORDERTABLES":
                        return await _statusConfirmationService.ConfirmMultipleTransferOrdersWithStatusAsync(token, orderIds);

                    case "SALESORDERS":
                    case "BRPACKINGSLIPINTERFACES":
                        return await _statusConfirmationService.ConfirmMultipleSalesOrdersWithStatusAsync(token, orderIds);

                    default:
                        _logger.LogWarning($"⚠️ Type de commande non reconnu: {endpointName}");
                        return 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la confirmation des commandes {endpointName}");
                return 0;
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