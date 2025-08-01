// Fichier: Services/BLExportService.cs
// Service principal pour l'export des BL vers Dynamics 365
// Gestion des POST vers les 2 endpoints + traçabilité JSON_OUT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service pour l'export des BL vers Dynamics 365
    /// </summary>
    public class BLExportService
    {
        private readonly HttpClient _httpClient;
        private readonly SpeedWmsDataService _speedWmsService;
        private readonly JsonOutService _jsonOutService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BLExportService> _logger;
        private readonly string _baseUrl;
        private readonly BLExportConfig _config;

        public BLExportService(
            HttpClient httpClient,
            SpeedWmsDataService speedWmsService,
            JsonOutService jsonOutService,
            IConfiguration configuration,
            ILogger<BLExportService> logger)
        {
            _httpClient = httpClient;
            _speedWmsService = speedWmsService;
            _jsonOutService = jsonOutService;
            _configuration = configuration;
            _logger = logger;
            _baseUrl = configuration["ResourceUrl"]?.TrimEnd('/')
                ?? throw new ArgumentNullException("ResourceUrl manquante");

            // Configuration BLExport
            _config = new BLExportConfig
            {
                DynamicsBaseUrl = _baseUrl,
                ValidationEndpoint = "data/BRPackingSlipValidationInterfaces",
                ConfirmationEndpoint = "data/BRPackingSlipValidationInterfaces/Microsoft.Dynamics.DataEntities.PostPackingSlip",
                MaxRetryAttempts = configuration.GetValue<int>("BLExport:MaxRetryAttempts", 3),
                RetryDelaySeconds = configuration.GetValue<int>("BLExport:RetryDelaySeconds", 30),
                BatchSize = configuration.GetValue<int>("BLExport:BatchSize", 10),
                EnableConfirmationPost = configuration.GetValue<bool>("BLExport:EnableConfirmationPost", true),
                DefaultInventLocationId = configuration.GetValue<string>("BLExport:DefaultInventLocationId", "RECNOLP")
            };
        }

        /// <summary>
        /// Processus principal d'export des BL
        /// </summary>
        public async Task<BLExportStatistics> ProcessBLExportAsync(string authToken)
        {
            var statistics = new BLExportStatistics
            {
                ProcessingStartTime = DateTime.Now
            };

            try
            {
                _logger.LogInformation("🚀 === DÉBUT BLEXPORT === 🚀");

                // Étape 1: Récupérer tous les BL depuis SpeedWMS
                _logger.LogInformation("🔍 Récupération des BL depuis SpeedWMS...");
                var speedWmsBLs = await _speedWmsService.GetAllAvailableBLsAsync();
                statistics.TotalBLsFound = speedWmsBLs.Count;

                if (speedWmsBLs.Count == 0)
                {
                    _logger.LogInformation("ℹ️ Aucun BL trouvé dans SpeedWMS");
                    return statistics;
                }

                // Étape 2: Transformer les données SpeedWMS
                _logger.LogInformation($"🔄 Transformation de {speedWmsBLs.Count} BL...");
                var exportBLs = await _speedWmsService.TransformToBLExportDataAsync(speedWmsBLs);

                // Étape 3: Filtrer les BL déjà traités (vérification JSON_OUT)
                _logger.LogInformation("🔍 Vérification des BL déjà traités...");
                var newBLs = await FilterAlreadyProcessedBLsAsync(exportBLs);
                statistics.BLsAlreadyProcessed = exportBLs.Count - newBLs.Count;
                statistics.BLsToProcess = newBLs.Count;

                if (newBLs.Count == 0)
                {
                    _logger.LogInformation("ℹ️ Tous les BL ont déjà été traités");
                    return statistics;
                }

                _logger.LogInformation($"📤 {newBLs.Count} nouveaux BL à traiter...");

                // Étape 4: Traiter les BL par batch
                var results = await ProcessBLBatchesAsync(authToken, newBLs);

                // Étape 5: Calculer les statistiques finales
                statistics.BLsProcessedSuccessfully = results.Count(r => r.Success);
                statistics.BLsWithErrors = results.Count(r => !r.Success);
                statistics.TotalPayloadsSent = results.Sum(r => r.PayloadsGenerated);
                statistics.ConfirmationsSent = results.Count(r => r.ConfirmationPostSuccess);
                statistics.ProcessingEndTime = DateTime.Now;
                statistics.TotalProcessingTime = statistics.ProcessingEndTime - statistics.ProcessingStartTime;

                _logger.LogInformation($"✅ BLExport terminé: {statistics.GetSummary()}");

                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur critique lors du processus BLExport");
                statistics.ProcessingEndTime = DateTime.Now;
                statistics.TotalProcessingTime = statistics.ProcessingEndTime - statistics.ProcessingStartTime;
                return statistics;
            }
        }

        /// <summary>
        /// Filtre les BL déjà traités en vérifiant JSON_OUT
        /// </summary>
        private async Task<List<BLExportData>> FilterAlreadyProcessedBLsAsync(List<BLExportData> exportBLs)
        {
            var newBLs = new List<BLExportData>();

            try
            {
                foreach (var bl in exportBLs)
                {
                    // Vérifier si ce BL a déjà été traité dans JSON_OUT
                    var alreadyProcessed = await _jsonOutService.IsBLAlreadyProcessedAsync(bl.BLNumber);

                    if (!alreadyProcessed)
                    {
                        newBLs.Add(bl);
                        _logger.LogDebug($"📋 BL {bl.BLNumber}: nouveau, à traiter");
                    }
                    else
                    {
                        _logger.LogDebug($"✅ BL {bl.BLNumber}: déjà traité, ignoré");
                    }
                }

                _logger.LogInformation($"🔍 Filtrage terminé: {newBLs.Count}/{exportBLs.Count} BL à traiter");
                return newBLs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du filtrage des BL");
                return exportBLs; // En cas d'erreur, traiter tous les BL
            }
        }

        /// <summary>
        /// Traite les BL par batch pour optimiser les performances
        /// </summary>
        private async Task<List<BLExportResult>> ProcessBLBatchesAsync(string authToken, List<BLExportData> blsToProcess)
        {
            var allResults = new List<BLExportResult>();
            var batchSize = _config.BatchSize;

            _logger.LogInformation($"🔄 Traitement par batch de {batchSize} BL...");

            for (int i = 0; i < blsToProcess.Count; i += batchSize)
            {
                var batch = blsToProcess.Skip(i).Take(batchSize).ToList();
                var batchNumber = (i / batchSize) + 1;
                var totalBatches = (int)Math.Ceiling((double)blsToProcess.Count / batchSize);

                _logger.LogInformation($"📦 Batch {batchNumber}/{totalBatches}: {batch.Count} BL");

                var batchResults = await ProcessSingleBatchAsync(authToken, batch, batchNumber);
                allResults.AddRange(batchResults);

                // Pause entre les batch pour ne pas surcharger l'API
                if (i + batchSize < blsToProcess.Count)
                {
                    await Task.Delay(2000);
                }
            }

            return allResults;
        }

        /// <summary>
        /// Traite un batch de BL
        /// </summary>
        private async Task<List<BLExportResult>> ProcessSingleBatchAsync(string authToken, List<BLExportData> batch, int batchNumber)
        {
            var batchResults = new List<BLExportResult>();

            foreach (var bl in batch)
            {
                var result = await ProcessSingleBLAsync(authToken, bl);
                batchResults.Add(result);

                var status = result.Success ? "✅" : "❌";
                _logger.LogInformation($"{status} Batch {batchNumber} - BL {bl.BLNumber}: {result.Status}");

                // Pause entre les BL
                await Task.Delay(500);
            }

            var successCount = batchResults.Count(r => r.Success);
            _logger.LogInformation($"📊 Batch {batchNumber} terminé: {successCount}/{batch.Count} BL traités avec succès");

            return batchResults;
        }

        /// <summary>
        /// Traite un BL complet (POST données + POST confirmation)
        /// </summary>
        private async Task<BLExportResult> ProcessSingleBLAsync(string authToken, BLExportData bl)
        {
            var startTime = DateTime.Now;
            var result = new BLExportResult
            {
                BLNumber = bl.BLNumber,
                ImportId = bl.ImportId,
                ProcessedDate = DateTime.Now
            };

            try
            {
                _logger.LogDebug($"🔄 Traitement BL {bl.BLNumber} (ImportId: {bl.ImportId})...");

                // Étape 1: Générer les payloads pour ce BL
                var payloads = GeneratePayloadsForBL(bl);
                result.PayloadsGenerated = payloads.Count;

                if (payloads.Count == 0)
                {
                    result.Success = false;
                    result.Status = BLExportStatusConstants.Error;
                    result.ErrorMessage = "Aucun payload généré";
                    await _jsonOutService.LogBLExportAsync(bl.BLNumber, bl.ImportId, "", result.Status, result.ErrorMessage);
                    return result;
                }

                _logger.LogDebug($"📤 BL {bl.BLNumber}: {payloads.Count} payloads à envoyer");

                // Étape 2: Envoyer les données (1er POST)
                var dataPostSuccess = await SendBLDataAsync(authToken, bl, payloads);
                result.FirstPostSuccess = dataPostSuccess;

                if (!dataPostSuccess)
                {
                    result.Success = false;
                    result.Status = BLExportStatusConstants.Error;
                    result.ErrorMessage = "Échec envoi données";
                    return result;
                }

                // Étape 3: Envoyer la confirmation (2ème POST)
                if (_config.EnableConfirmationPost)
                {
                    var confirmationSuccess = await SendBLConfirmationAsync(authToken, bl);
                    result.ConfirmationPostSuccess = confirmationSuccess;

                    if (confirmationSuccess)
                    {
                        result.Success = true;
                        result.Status = BLExportStatusConstants.Confirmed;
                        await _jsonOutService.LogBLExportAsync(bl.BLNumber, "Données et confirmation envoyées", "BL_EXPORT", "BR", "", bl.ImportId);
                    }
                    else
                    {
                        result.Success = false;
                        result.Status = BLExportStatusConstants.PendingRetry;
                        result.ErrorMessage = "Confirmation échouée, en attente retry";
                        await _jsonOutService.LogBLExportAsync(bl.BLNumber, "", "BL_ERROR", "BR", result.ErrorMessage, bl.ImportId);
                    }
                }
                else
                {
                    // Si confirmation désactivée, considérer comme réussi après le 1er POST
                    result.Success = true;
                    result.Status = BLExportStatusConstants.Sent;
                    await _jsonOutService.LogBLExportAsync(bl.BLNumber, "Données envoyées (confirmation désactivée)", "BL_EXPORT", "BR", "", bl.ImportId);
                }

                result.ProcessingTime = DateTime.Now - startTime;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception lors du traitement BL {bl.BLNumber}");
                result.Success = false;
                result.Status = BLExportStatusConstants.Error;
                result.ErrorMessage = ex.Message;
                result.ProcessingTime = DateTime.Now - startTime;

                await _jsonOutService.LogBLExportAsync(bl.BLNumber, bl.ImportId, "", result.Status, ex.Message);
                return result;
            }
        }

        /// <summary>
        /// ✅ SOLUTION: Génère les payloads avec ImportId = OPE_KEYU et gestion correcte des dates
        /// </summary>
        private List<BRPackingSlipValidationPayload> GeneratePayloadsForBL(BLExportData bl)
        {
            var payloads = new List<BRPackingSlipValidationPayload>();

            foreach (var line in bl.Lines)
            {
                var payload = new BRPackingSlipValidationPayload
                {
                    ImportId = bl.BLNumber,
                    TransRefId = bl.TransRefId,
                    PickingRouteID = string.IsNullOrEmpty(bl.PickingRouteId) ? bl.TransRefId : bl.PickingRouteId,
                    CarrierCode = bl.CarrierCode,
                    CarrierServiceCode = bl.CarrierServiceCode,
                    ItemId = line.ItemId,
                    Qty = line.TotalQuantity,
                    InventLocationId = bl.InventLocationId,
                    BR3PLPackingSlipId = bl.BLNumber,
                    InventSerialId = line.SerialIds.FirstOrDefault() ?? "",
                    InventBatchId = line.BatchIds.FirstOrDefault() ?? ""
                };

                // ✅ CORRECTION: Ne définir les dates QUE si elles ont une valeur
                if (bl.ShippingDate.HasValue)
                {
                    payload.BR3PLShippingDate = bl.ShippingDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                // ✅ IMPORTANT: Si pas de date, ne pas définir la propriété (laisser null/vide)

                if (bl.EndDatePrep.HasValue)
                {
                    payload.BR3PLEndDatePrep = bl.EndDatePrep.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                // ✅ IMPORTANT: Si pas de date, ne pas définir la propriété (laisser null/vide)

                payloads.Add(payload);

                _logger.LogDebug($"🔧 Payload généré: ImportId={payload.ImportId}, TransRefId={payload.TransRefId}, ItemId={payload.ItemId}, ShippingDate={payload.BR3PLShippingDate ?? "NULL"}, EndDatePrep={payload.BR3PLEndDatePrep ?? "NULL"}");
            }

            return payloads;
        }

        /// <summary>
        /// POST de confirmation avec ImportId = OPE_KEYU
        /// </summary>
        private async Task<bool> PostConfirmationAsync(BLExportData bl, string authToken)
        {
            try
            {
                var url = $"{_config.DynamicsBaseUrl}/{_config.ConfirmationEndpoint}";

                // ✅ Payload de confirmation avec ImportId = OPE_KEYU
                var confirmationPayload = new
                {
                    ImportId = bl.BLNumber, // ✅ ImportId = OPE_KEYU
                    transRefId = bl.TransRefId
                };

                var json = JsonSerializer.Serialize(confirmationPayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                _logger.LogDebug($"📤 POST confirmation vers {url}: ImportId={bl.BLNumber}, TransRefId={bl.TransRefId}");

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ Erreur confirmation BL {bl.BLNumber}: HTTP {response.StatusCode}: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception confirmation BL {bl.BLNumber}");
                return false;
            }
        }

        /// <summary>
        /// Formate une date pour Dynamics 365 (format ISO 8601) ou retourne null si la date est null
        /// </summary>
        private static string? FormatDateForDynamics(DateTime? date)
        {
            if (!date.HasValue)
                return null;

            // Format ISO 8601 requis par Dynamics 365
            return date.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        /// <summary>
        /// Crée un payload pour une ligne d'article - VERSION FINALE CORRIGÉE
        /// </summary>
        private BRPackingSlipValidationPayload CreatePayloadForLine(BLExportData bl, BLExportLine line, int index)
        {
            return new BRPackingSlipValidationPayload
            {
                DataAreaId = "br",
                ImportId = bl.ImportId,
                TransRefId = bl.TransRefId,
                BR3PLShippingDate = (bl.ShippingDate ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                PickingRouteID = bl.PickingRouteId,
                CarrierServiceCode = bl.CarrierServiceCode,
                Qty = line.TotalQuantity,
                BR3PLPackingSlipId = bl.BLNumber,
                ItemId = line.ItemId,
                InventLocationId = bl.InventLocationId,
                BR3PLEndDatePrep = (bl.EndDatePrep ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                CarrierCode = bl.CarrierCode,
                InventSerialId = index < line.SerialIds.Count ? line.SerialIds[index] : "",
                InventBatchId = index < line.BatchIds.Count ? line.BatchIds[index] : ""
            };
        }



        /// <summary>
        /// Envoie les données du BL (1er POST)
        /// </summary>
        private async Task<bool> SendBLDataAsync(string authToken, BLExportData bl, List<BRPackingSlipValidationPayload> payloads)
        {
            var successCount = 0;

            try
            {
                var endpoint = $"{_baseUrl}/{_config.ValidationEndpoint}";
                _logger.LogDebug($"📤 Envoi données BL {bl.BLNumber} vers {endpoint}");

                foreach (var payload in payloads)
                {
                    var success = await SendSinglePayloadAsync(authToken, endpoint, payload, bl.BLNumber);
                    if (success)
                    {
                        successCount++;
                    }

                    // Petite pause entre les payloads
                    await Task.Delay(200);
                }

                var allSuccess = successCount == payloads.Count;
                var message = $"{successCount}/{payloads.Count} payloads envoyés";

                if (allSuccess)
                {
                    _logger.LogInformation($"✅ BL {bl.BLNumber}: {message}");
                    await _jsonOutService.LogBLExportAsync(bl.BLNumber, message, "BL_EXPORT", "BR", "", bl.ImportId);
                }
                else
                {
                    _logger.LogWarning($"⚠️ BL {bl.BLNumber}: {message} - succès partiel");
                    await _jsonOutService.LogBLExportAsync(bl.BLNumber, bl.ImportId, message, BLExportStatusConstants.Error, "Succès partiel");
                }

                return allSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur envoi données BL {bl.BLNumber}");
                await _jsonOutService.LogBLExportAsync(bl.BLNumber, bl.ImportId, "", BLExportStatusConstants.Error, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Envoie un payload individuel
        /// </summary>
        private async Task<bool> SendSinglePayloadAsync(string authToken, string endpoint, BRPackingSlipValidationPayload payload, string blNumber)
        {
            try
            {
                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    WriteIndented = false
                });

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Log du payload pour traçabilité
                await _jsonOutService.LogJsonSentAsync($"BL_DATA_{blNumber}_{payload.ItemId}", jsonPayload, endpoint);

                var response = await _httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"✅ Payload envoyé: BL {blNumber}, Item {payload.ItemId}");
                    await _jsonOutService.LogJsonSentAsync($"BL_DATA_RESP_{blNumber}_{payload.ItemId}", responseContent, "RESPONSE", null, (int)response.StatusCode);
                    return true;
                }
                else
                {
                    var errorMessage = $"HTTP {response.StatusCode}: {responseContent}";
                    _logger.LogError($"❌ Erreur payload BL {blNumber}, Item {payload.ItemId}: {errorMessage}");
                    await _jsonOutService.LogErrorAsync($"BL_DATA_ERR_{blNumber}_{payload.ItemId}", jsonPayload, errorMessage, (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception payload BL {blNumber}, Item {payload.ItemId}");
                await _jsonOutService.LogErrorAsync($"BL_DATA_EXC_{blNumber}_{payload.ItemId}", "", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// ✅ CORRECT: Confirmation avec POST vide - aucun contenu JSON requis
        /// </summary>
        private async Task<bool> SendBLConfirmationAsync(string authToken, BLExportData bl)
        {
            try
            {
                var endpoint = $"{_baseUrl}/{_config.ConfirmationEndpoint}";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                // ✅ CORRECTION: POST vide sans contenu JSON
                _logger.LogDebug($"📤 POST confirmation vide vers {endpoint} pour BL {bl.BLNumber}");

                // Log pour traçabilité (même si vide)
                await _jsonOutService.LogJsonSentAsync($"BL_CONFIRM_{bl.BLNumber}", "POST_VIDE", endpoint);

                var response = await _httpClient.PostAsync(endpoint, null); // ✅ Contenu null
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Confirmation BL {bl.BLNumber} envoyée avec succès (POST vide)");
                    await _jsonOutService.LogSuccessAsync($"BL_CONFIRM_{bl.BLNumber}", responseContent);
                    return true;
                }
                else
                {
                    var errorMessage = $"HTTP {response.StatusCode}: {responseContent}";
                    _logger.LogError($"❌ Erreur confirmation BL {bl.BLNumber}: {errorMessage}");
                    await _jsonOutService.LogErrorAsync($"BL_CONFIRM_{bl.BLNumber}", "POST_VIDE", errorMessage, (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception confirmation BL {bl.BLNumber}");
                await _jsonOutService.LogErrorAsync($"BL_CONFIRM_{bl.BLNumber}", "POST_VIDE", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// ✅ NOUVELLE MÉTHODE: Détermine un PickingRouteID valide avec logs de test
        /// </summary>
        private string GetValidPickingRouteID(BLExportData bl)
        {
            // 🧪 LOG DÉTAILLÉ POUR TEST PP000283
            if (bl.PickingRouteId == "PP000283")
            {
                _logger.LogWarning($"🧪 TEST PP000283 - BL: {bl.BLNumber}");
                _logger.LogWarning($"🧪 TEST PP000283 - PickingRouteId: '{bl.PickingRouteId}'");
                _logger.LogWarning($"🧪 TEST PP000283 - TransRefId: '{bl.TransRefId}'");
                _logger.LogWarning($"🧪 TEST PP000283 - Decision: UTILISER PP000283 DIRECTEMENT");

                return bl.PickingRouteId; // 🧪 FORCER l'utilisation pour tester l'erreur
            }

            // Logique normale pour les autres
            if (string.IsNullOrEmpty(bl.PickingRouteId) || bl.PickingRouteId.Trim() == "")
            {
                _logger.LogDebug($"📍 OPE_ALPHA17 vide, utilisation TransRefId: {bl.TransRefId}");
                return bl.TransRefId;
            }

            // Pour tous les autres PP*, utiliser TransRefId par sécurité
            if (bl.PickingRouteId.StartsWith("PP"))
            {
                _logger.LogWarning($"⚠️ PickingRouteID PP* '{bl.PickingRouteId}', utilisation TransRefId: {bl.TransRefId}");
                return bl.TransRefId;
            }

            return bl.PickingRouteId;
        }

        /// <summary>
        /// Retry des confirmations en échec
        /// </summary>
        public async Task<int> RetryFailedConfirmationsAsync(string authToken)
        {
            try
            {
                _logger.LogInformation("🔄 Recherche des confirmations BL en échec...");

                var failedBLs = await _jsonOutService.GetFailedBLExportsAsync();

                if (failedBLs.Count == 0)
                {
                    _logger.LogInformation("ℹ️ Aucune confirmation BL en échec trouvée");
                    return 0;
                }

                _logger.LogInformation($"🔄 Retry de {failedBLs.Count} confirmations BL...");

                var retryCount = 0;
                foreach (var failedBL in failedBLs)
                {
                    try
                    {
                        // Créer un BLExportData minimal pour le retry
                        var blData = new BLExportData
                        {
                            BLNumber = failedBL.BLNumber,
                            ImportId = failedBL.ImportId
                        };

                        var success = await SendBLConfirmationAsync(authToken, blData);
                        if (success)
                        {
                            retryCount++;
                            await _jsonOutService.LogBLExportAsync(failedBL.BLNumber, failedBL.ImportId, "Retry confirmation réussi", BLExportStatusConstants.Confirmed);
                        }

                        await Task.Delay(1000); // Pause entre les retry
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Erreur retry confirmation BL {failedBL.BLNumber}");
                    }
                }

                _logger.LogInformation($"✅ Retry terminé: {retryCount}/{failedBLs.Count} confirmations récupérées");
                return retryCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du retry des confirmations");
                return 0;
            }
        }

        /// <summary>
        /// Test de connectivité vers les endpoints Dynamics
        /// </summary>
        public async Task<bool> TestDynamicsConnectivityAsync(string authToken)
        {
            try
            {
                _logger.LogInformation("🔍 Test connectivité endpoints BLExport...");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                // Test endpoint validation
                var validationEndpoint = $"{_baseUrl}/{_config.ValidationEndpoint}?$top=1";
                var validationResponse = await _httpClient.GetAsync(validationEndpoint);

                if (!validationResponse.IsSuccessStatusCode)
                {
                    _logger.LogError($"❌ Endpoint validation non accessible: {validationResponse.StatusCode}");
                    return false;
                }

                _logger.LogInformation("✅ Endpoints BLExport accessibles");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur test connectivité BLExport");
                return false;
            }
        }

        /// <summary>
        /// Obtient des statistiques sur les BL traités
        /// </summary>
        public async Task<BLExportStatistics> GetBLExportStatisticsAsync()
        {
            try
            {
                var stats = await _jsonOutService.GetBLExportStatisticsAsync();
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération statistiques BLExport");
                return new BLExportStatistics();
            }
        }
    }

    /// <summary>
    /// Classe pour représenter un BL en échec pour retry
    /// </summary>
    public class FailedBLExport
    {
        public string BLNumber { get; set; } = "";
        public string ImportId { get; set; } = "";
        public string Status { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public DateTime FailedDate { get; set; }
        public int RetryCount { get; set; }
    }
}