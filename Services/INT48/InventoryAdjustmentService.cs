// Fichier: Services/INT48/InventoryAdjustmentService.cs
// Service principal pour le traitement des ajustements d'inventaire INT48
// Orchestration complète: récupération, transformation, envoi, logging

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using DynamicsApiToDatabase.Models.INT48;
using DynamicsApiToDatabase.DataAccess.INT48;
using DynamicsApiToDatabase.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services.INT48
{
    public class InventoryAdjustmentService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InventoryAdjustmentService> _logger;
        private readonly DynamicsAuthService _authService;
        private readonly InventoryTransformationService _transformationService;
        private readonly IJsonOutService _jsonOutService;
        private readonly SpeedWmsInventoryRepository _repository;
        private readonly HttpClient _httpClient;

        // URL de base pour Inventory Service
        private const string INVENTORY_SERVICE_BASE_URL = 
            "https://inventoryservice.weu-il107.gateway.prod.island.powerapps.com";

        public InventoryAdjustmentService(
            IConfiguration configuration,
            ILogger<InventoryAdjustmentService> logger,
            DynamicsAuthService authService,
            InventoryTransformationService transformationService,
            IJsonOutService jsonOutService,
            SpeedWmsInventoryRepository repository,
            HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _authService = authService;
            _transformationService = transformationService;
            _jsonOutService = jsonOutService;
            _repository = repository;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Traite un batch de mouvements d'inventaire
        /// </summary>
        public async Task<InventoryAdjustmentStatistics> ProcessInventoryAdjustmentsAsync(
            List<SpeedWmsInventoryMovement> movements)
        {
            var stats = new InventoryAdjustmentStatistics
            {
                TotalMovementsFound = movements.Count
            };

            var startTime = DateTime.Now;

            try
            {
                _logger.LogInformation($"🚀 Début traitement de {movements.Count} mouvements d'inventaire");

                if (movements.Count == 0)
                {
                    _logger.LogInformation("📭 Aucun mouvement à traiter");
                    return stats;
                }

                // Étape 1: Obtenir les tokens d'authentification
                var (azureToken, securityToken) = await _authService.GetAuthenticationTokensAsync();

                // Étape 2: Transformer les données SpeedWMS vers format Dynamics
                var adjustmentRequests = _transformationService.TransformMovementsToAdjustments(movements);

                if (adjustmentRequests.Count == 0)
                {
                    _logger.LogWarning("⚠️ Aucun ajustement valide après transformation");
                    return stats;
                }

                // Étape 3: Envoyer les données par batch avec workflow complet JSON_OUT
                int batchSize = _configuration.GetValue<int>("INT48:BatchSize", 100);
                var batches = adjustmentRequests
                    .Select((item, index) => new { item, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.item).ToList())
                    .ToList();

                _logger.LogInformation($"📦 Traitement de {batches.Count} batch(s) avec workflow complet");

                foreach (var (batch, batchIndex) in batches.Select((b, i) => (b, i + 1)))
                {
                    string importId = $"INT48_BATCH{batchIndex}_{DateTime.Now:yyyyMMddHHmmss}";
                    var mvtKeys = batch.Select(b => b.Id).ToList();

                    try
                    {
                        // ÉTAPE A: INSERT dans JSON_OUT (statut EN_ATTENTE) - 1 ligne par mouvement
                        await LogBatchPendingAsync(batch, batchIndex, importId);

                        // ÉTAPE B: UPDATE MVT_TOP3=1 dans MVT_DAT
                        await _repository.MarkMovementsAsProcessedAsync(mvtKeys, importId, success: true);

                        // ÉTAPE C: Envoi API Dynamics 365
                        var (statusCode, responseContent) = await SendInventoryAdjustmentBatchAsync(
                            batch, 
                            azureToken, 
                            securityToken, 
                            batchIndex);
                        
                        stats.MovementsProcessedSuccessfully += batch.Count;
                        stats.BatchesSent++;
                        stats.TotalPayloadsSent++;

                        // ÉTAPE D: UPDATE JSON_OUT avec succès (ENVOYE) - 1 ligne par mouvement
                        await UpdateJsonOutSuccessInt48Async(batch, batchIndex, importId, responseContent, statusCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Erreur batch {batchIndex}/{batches.Count}");
                        stats.MovementsWithErrors += batch.Count;

                        // ÉTAPE D: UPDATE JSON_OUT avec erreur (ERREUR) - 1 ligne par mouvement
                        await UpdateJsonOutErrorInt48Async(batch, batchIndex, importId, ex.Message);
                    }
                }

                stats.TotalProcessingTime = DateTime.Now - startTime;

                _logger.LogInformation(
                    $"✅ Traitement terminé: {stats.MovementsProcessedSuccessfully} succès, " +
                    $"{stats.MovementsWithErrors} erreurs en {stats.TotalProcessingTime.TotalSeconds:F1}s");

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur globale lors du traitement des ajustements");
                stats.TotalProcessingTime = DateTime.Now - startTime;
                return stats;
            }
        }

        /// <summary>
        /// Envoie un batch d'ajustements vers Dynamics 365
        /// Retourne (statusCode, responseContent)
        /// </summary>
        private async Task<(int statusCode, string responseContent)> SendInventoryAdjustmentBatchAsync(
            List<InventoryAdjustmentRequest> batch,
            string azureToken,
            string securityToken,
            int batchNumber)
        {
            // Obtenir l'Environment ID depuis la configuration
            var envId = _configuration["INT48:EnvironmentId"] 
                ?? throw new Exception("INT48:EnvironmentId manquant dans la configuration");

            var url = $"{INVENTORY_SERVICE_BASE_URL}/api/environment/{envId}/transaction/adjustment/bulk";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            // L'API Inventory Service attend le token Security Service en Bearer
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", securityToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            var jsonContent = JsonSerializer.Serialize(batch, jsonOptions);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogDebug($"📤 Envoi batch {batchNumber}: {batch.Count} ajustements vers {url}");
            _logger.LogTrace($"Payload: {jsonContent}");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    $"❌ Erreur API Dynamics batch {batchNumber}: {response.StatusCode}");
                _logger.LogError($"Réponse: {responseContent}");
                _logger.LogError($"Headers: {response.Headers}");
                throw new Exception(
                    $"Erreur API Dynamics: {response.StatusCode} - {responseContent}");
            }

            _logger.LogInformation(
                $"✅ Batch {batchNumber} envoyé avec succès ({batch.Count} ajustements)");
            _logger.LogDebug($"Réponse: {responseContent}");

            return ((int)response.StatusCode, responseContent);
        }

        /// <summary>
        /// ÉTAPE A: INSERT initial dans JSON_OUT avec statut EN_ATTENTE - 1 LIGNE PAR MOUVEMENT
        /// </summary>
        private async Task LogBatchPendingAsync(
            List<InventoryAdjustmentRequest> batch,
            int batchNumber,
            string importId)
        {
            try
            {
                // Enregistrer chaque mouvement individuellement dans JSON_OUT
                foreach (var movement in batch)
                {
                    var jsonPayload = JsonSerializer.Serialize(movement, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    await _jsonOutService.LogJsonSentAsync(
                        itemId: movement.Id,
                        jsonPayload: jsonPayload,
                        endpoint: "INT48_ADJUSTMENT",
                        responseContent: "EN_ATTENTE",
                        httpCode: 0,
                        importId: importId,
                        status: "EN_ATTENTE"
                    );
                }

                _logger.LogDebug($"📝 Batch {batchNumber}: {batch.Count} mouvements enregistrés individuellement dans JSON_OUT");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur INSERT JSON_OUT pour batch {batchNumber}");
                throw; // Bloquer si INSERT échoue
            }
        }

        /// <summary>
        /// 🆕 INT48 SPÉCIFIQUE: UPDATE JSON_OUT avec succès (1 ligne par mouvement - UPDATE au lieu d'INSERT)
        /// </summary>
        private async Task UpdateJsonOutSuccessInt48Async(
            List<InventoryAdjustmentRequest> batch,
            int batchNumber,
            string importId,
            string responseContent,
            int statusCode)
        {
            try
            {
                // Mettre à jour chaque mouvement individuellement
                foreach (var movement in batch)
                {
                    await _jsonOutService.UpdateJsonOutStatusAsync(
                        itemId: movement.Id,
                        importId: importId,
                        newStatus: "ENVOYE",
                        responseContent: responseContent,
                        httpCode: statusCode
                    );
                }

                _logger.LogDebug($"📝 Batch {batchNumber}: {batch.Count} mouvements mis à jour individuellement - ENVOYE");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur UPDATE JSON_OUT succès INT48 pour batch {batchNumber}");
            }
        }

        /// <summary>
        /// 🆕 INT48 SPÉCIFIQUE: UPDATE JSON_OUT avec erreur (1 ligne par mouvement - UPDATE au lieu d'INSERT)
        /// </summary>
        private async Task UpdateJsonOutErrorInt48Async(
            List<InventoryAdjustmentRequest> batch, 
            int batchNumber,
            string importId,
            string errorMessage)
        {
            try
            {
                // Mettre à jour chaque mouvement individuellement
                foreach (var movement in batch)
                {
                    await _jsonOutService.UpdateJsonOutStatusAsync(
                        itemId: movement.Id,
                        importId: importId,
                        newStatus: "ERREUR",
                        responseContent: errorMessage,
                        httpCode: 500
                    );
                }

                _logger.LogDebug($"📝 Batch {batchNumber}: {batch.Count} mouvements mis à jour individuellement - ERREUR");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur UPDATE JSON_OUT erreur INT48 pour batch {batchNumber}");
            }
        }

        /// <summary>
        /// ÉTAPE D: UPDATE JSON_OUT avec succès (statut ENVOYE) - ANCIENNE VERSION BATCH
        /// </summary>
        private async Task UpdateJsonOutSuccessAsync(
            List<InventoryAdjustmentRequest> batch,
            int batchNumber,
            string importId,
            string responseContent,
            int statusCode)
        {
            try
            {
                var jsonPayload = JsonSerializer.Serialize(batch, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await _jsonOutService.LogJsonSentAsync(
                    itemId: $"INT48_BATCH{batchNumber}",
                    jsonPayload: jsonPayload,
                    endpoint: "INT48_ADJUSTMENT",
                    responseContent: responseContent,
                    httpCode: statusCode,
                    importId: importId,
                    status: "ENVOYE"
                );

                _logger.LogDebug($"📝 Batch {batchNumber} UPDATE JSON_OUT: ENVOYE");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur UPDATE JSON_OUT succès pour batch {batchNumber}");
            }
        }

        /// <summary>
        /// ÉTAPE D: UPDATE JSON_OUT avec erreur (statut ERREUR) - ANCIENNE VERSION BATCH
        /// </summary>
        private async Task UpdateJsonOutErrorAsync(
            List<InventoryAdjustmentRequest> batch, 
            int batchNumber,
            string importId,
            string errorMessage)
        {
            try
            {
                var jsonPayload = JsonSerializer.Serialize(batch, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await _jsonOutService.LogJsonSentAsync(
                    itemId: $"INT48_BATCH{batchNumber}",
                    jsonPayload: jsonPayload,
                    endpoint: "INT48_ADJUSTMENT",
                    responseContent: errorMessage,
                    httpCode: 500,
                    importId: importId,
                    status: "ERREUR"
                );

                _logger.LogDebug($"📝 Batch {batchNumber} UPDATE JSON_OUT: ERREUR");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur UPDATE JSON_OUT erreur pour batch {batchNumber}");
            }
        }

        /// <summary>
        /// Teste la connectivité avec les endpoints Dynamics
        /// </summary>
        public async Task<bool> TestDynamicsConnectivityAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Test de connectivité Dynamics Inventory Service...");

                var (azureToken, securityToken) = await _authService.GetAuthenticationTokensAsync();

                _logger.LogInformation("✅ Connectivité Dynamics OK");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Échec du test de connectivité");
                return false;
            }
        }
    }
}