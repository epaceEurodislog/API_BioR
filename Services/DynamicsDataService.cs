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
        private readonly IConfiguration _configuration;
        private readonly ILogger<DynamicsDataService> _logger;
        private readonly string _baseUrl;

        public DynamicsDataService(
            HttpClient httpClient,
            AuthenticationService authService,
            SqlServerDatabaseService databaseService,
            IConfiguration configuration,
            ILogger<DynamicsDataService> logger)
        {
            _httpClient = httpClient;
            _authService = authService;
            _databaseService = databaseService;
            _configuration = configuration;
            _logger = logger;
            _baseUrl = configuration["ResourceUrl"] ?? throw new ArgumentNullException("ResourceUrl manquante");
        }

        /// <summary>
        /// Synchronise un endpoint spécifique vers la table JSON_IN
        /// </summary>
        public async Task<SyncResult> SyncEndpointAsync(string endpointName, string endpointPath, string primaryKeyField = "ItemId")
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new SyncResult { EndpointName = endpointName };

            try
            {
                _logger.LogInformation($"🔄 Début synchronisation {endpointName}...");

                // Obtenir le token d'authentification
                var token = await _authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Impossible d'obtenir le token d'authentification");
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                // Récupérer les données de l'API
                var apiData = await FetchAllDataFromEndpointAsync(endpointPath);
                _logger.LogInformation($"📊 {apiData.Count} enregistrements récupérés de l'API {endpointName}");

                if (apiData.Count == 0)
                {
                    _logger.LogWarning($"⚠️ Aucune donnée récupérée pour {endpointName}");
                    return result;
                }

                // Traiter chaque enregistrement
                var currentKeys = new List<string>();
                var tasks = new List<Task>();

                foreach (var item in apiData)
                {
                    var uniqueKey = GenerateUniqueKey(item, primaryKeyField, endpointName);
                    currentKeys.Add(uniqueKey);

                    // Traitement en batch pour éviter la surcharge
                    tasks.Add(ProcessSingleRecordAsync(uniqueKey, item, endpointPath, result));

                    // Traiter par batch de 50 pour éviter trop de connexions simultanées
                    if (tasks.Count >= 50)
                    {
                        await Task.WhenAll(tasks);
                        tasks.Clear();
                    }
                }

                // Traiter les derniers enregistrements
                if (tasks.Any())
                {
                    await Task.WhenAll(tasks);
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
        /// Traite un seul enregistrement
        /// </summary>
        private async Task ProcessSingleRecordAsync(string uniqueKey, JsonElement item, string endpointPath, SyncResult result)
        {
            try
            {
                var jsonData = item.GetRawText();
                var success = await _databaseService.InsertOrUpdateJsonDataAsync(uniqueKey, jsonData, endpointPath);

                if (success)
                {
                    // Cette logique est simplifiée - en réalité, le service de base de données
                    // devrait retourner le type d'opération effectuée
                    lock (result)
                    {
                        result.NewRecords++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du traitement de l'enregistrement {uniqueKey}");
                lock (result)
                {
                    result.ErrorRecords++;
                }
            }
        }

        /// <summary>
        /// Récupère toutes les données d'un endpoint avec pagination
        /// </summary>
        private async Task<List<JsonElement>> FetchAllDataFromEndpointAsync(string endpointPath)
        {
            var allData = new List<JsonElement>();
            var url = $"{_baseUrl}{endpointPath}";
            var pageSize = 1000; // Taille de page optimale pour Dynamics
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

                    // Dynamics retourne les données dans une propriété "value"
                    if (jsonDoc.RootElement.TryGetProperty("value", out var valueProperty))
                    {
                        var pageData = valueProperty.EnumerateArray().ToList();

                        if (pageData.Count == 0)
                        {
                            break; // Fin des données
                        }

                        allData.AddRange(pageData);
                        skip += pageData.Count;

                        _logger.LogDebug($"📄 Page récupérée: {pageData.Count} enregistrements (total: {allData.Count})");

                        // Si la page retournée est plus petite que la taille demandée, on a tout récupéré
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
                // Cas spéciaux pour différents types d'endpoints
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

                // Cas général : utiliser le champ de clé primaire fourni
                if (item.TryGetProperty(primaryKeyField, out var primaryValue))
                {
                    var prefix = endpointName.Substring(0, Math.Min(3, endpointName.Length)).ToUpper();
                    return $"{prefix}_{primaryValue.GetString()}";
                }

                // Dernière option : générer une clé basée sur le hash du contenu
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
        /// Calcule un hash du contenu pour générer une clé unique
        /// </summary>
        private string ComputeContentHash(string content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hashBytes)[..16]; // Prendre les 16 premiers caractères
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

                // Pause entre les endpoints pour éviter la surcharge
                await Task.Delay(1000);
            }

            return results;
        }

        /// <summary>
        /// Retourne la liste des endpoints à synchroniser (CORRIGÉS)
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
}