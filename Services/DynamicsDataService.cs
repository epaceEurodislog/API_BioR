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

                var currentKeys = new List<string>();
                var confirmedItems = new List<string>();

                foreach (var item in apiData)
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

                // ⚡ OPTIMISATION: Confirmer SEULEMENT les articles NON confirmés
                if (confirmedItems.Count > 0)
                {
                    _logger.LogInformation($"📤 Vérification des confirmations pour {confirmedItems.Count} articles...");

                    var nonConfirmedItems = await FilterAlreadyConfirmedItemsAsync(confirmedItems);

                    if (nonConfirmedItems.Count > 0)
                    {
                        var confirmationCount = await _statusConfirmationService.ConfirmMultipleItemsReceivedAsync(token, nonConfirmedItems);

                        if (confirmationCount > 0)
                        {
                            // Marquer les articles confirmés avec succès
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

                // Marquer les enregistrements supprimés
                var deletedCount = await _databaseService.MarkMissingRecordsAsDeletedAsync(endpointPath, currentKeys);
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
        /// Génère une clé unique pour chaque enregistrement
        /// </summary>
        private string GenerateUniqueKey(JsonElement item, string primaryKeyField, string endpointName)
        {
            try
            {
                switch (endpointName.ToUpper())
                {
                    case "ARTICLES":
                    case "BRINT34RELEASEDPRODUCTS":
                        if (item.TryGetProperty("ItemId", out var itemId))
                        {
                            return $"ART_{itemId.GetString()}";
                        }
                        break;

                    case "RETURNORDERS":
                    case "BRINT31RETURNORDERLINES":
                        if (item.TryGetProperty("ReturnItemNum", out var returnNum) &&
                            item.TryGetProperty("LineNum", out var lineNum))
                        {
                            return $"RET_{returnNum.GetString()}_{lineNum.GetDecimal()}";
                        }
                        break;

                    case "PURCHASEORDERS":
                    case "BRINT32PURCHASEORDERLINES":
                        if (item.TryGetProperty("PurchaseOrderNumber", out var purchaseNum) &&
                            item.TryGetProperty("LineNumber", out var purchaseLineNum))
                        {
                            return $"PUR_{purchaseNum.GetString()}_{purchaseLineNum.GetDecimal()}";
                        }
                        break;

                    case "TRANSFERORDERS":
                    case "BRINT32TRANSFERORDERTABLES":
                        if (item.TryGetProperty("TransferId", out var transferId) &&
                            item.TryGetProperty("LineNumber", out var transferLineNum))
                        {
                            return $"TRA_{transferId.GetString()}_{transferLineNum.GetDecimal()}";
                        }
                        break;
                }

                if (item.TryGetProperty(primaryKeyField, out var primaryValue))
                {
                    var prefix = endpointName.Substring(0, Math.Min(3, endpointName.Length)).ToUpper();
                    return $"{prefix}_{primaryValue.GetString()}";
                }

                var contentHash = ComputeContentHash(item.GetRawText());
                return $"HASH_{contentHash}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Impossible de générer une clé unique pour {endpointName}, utilisation du hash");
                var contentHash = ComputeContentHash(item.GetRawText());
                return $"HASH_{contentHash}";
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
        private List<EndpointConfig> GetConfiguredEndpoints()
        {
            return new List<EndpointConfig>
            {
                new()
                {
                    Name = "Articles",
                    Path = "data/BRINT34ReleasedProducts",
                    PrimaryKeyField = "ItemId"
                },
                new()
                {
                    Name = "ReturnOrders",
                    Path = "data/BRINT32ReturnOrderTables",
                    PrimaryKeyField = "ReturnOrderNumber"
                },
                new()
                {
                    Name = "PurchaseOrders",
                    Path = "data/BRINT32PurchOrderTables",
                    PrimaryKeyField = "PurchaseOrderNumber"
                },
                new()
                {
                    Name = "TransferOrders",
                    Path = "data/BRINT32TransferOrderTables",
                    PrimaryKeyField = "TransferId"
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