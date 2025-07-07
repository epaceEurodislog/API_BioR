// Fichier: Services/StatusConfirmationService.cs
// Service pour envoyer la confirmation de réception vers l'API Dynamics 365
// VERSION SIMPLIFIÉE - Juste enregistrer les envois

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
        /// ✅ SIMPLE: Confirme la réception d'un article et enregistre l'envoi
        /// </summary>
        public async Task<bool> ConfirmItemReceivedAsync(string token, string itemId)
        {
            try
            {
                _logger.LogInformation($"📤 Confirmation de réception pour l'article {itemId}");

                var endpoint = $"{_baseUrl}/data/BRINT34ReleasedProducts/Microsoft.Dynamics.DataEntities.changeStatus";

                // ✅ 1. Préparer le payload
                var payload = new
                {
                    _itemId = itemId,
                    _status = "ProcessedBy3PL"
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null // Garder les underscores
                });

                // ✅ 2. Enregistrer AVANT l'envoi dans JSON_OUT
                await _jsonOutService.LogJsonSentAsync(itemId, jsonPayload, endpoint);

                // ✅ 3. Envoyer à l'API
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _logger.LogDebug($"🔍 Envoi vers: {endpoint}");
                _logger.LogDebug($"📋 Payload: {jsonPayload}");

                var response = await _httpClient.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Confirmation réussie pour l'article {itemId}");

                    // ✅ 4. Optionnel: Enregistrer aussi la réponse
                    await _jsonOutService.LogJsonSentAsync($"{itemId}_RESPONSE", responseContent, "RESPONSE", null, (int)response.StatusCode);

                    return true;
                }
                else
                {
                    var errorMessage = $"HTTP {response.StatusCode}: {responseContent}";
                    _logger.LogError($"❌ Erreur confirmation {itemId}: {errorMessage}");

                    // ✅ 5. Enregistrer l'erreur aussi
                    await _jsonOutService.LogJsonSentAsync($"{itemId}_ERROR", responseContent, "ERROR", null, (int)response.StatusCode);

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception confirmation {itemId}");

                // ✅ 6. Enregistrer l'exception
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

                // Affichage du progrès tous les 5 articles
                if ((i + 1) % 5 == 0 || (i + 1) == totalCount)
                {
                    _logger.LogInformation($"📊 Progrès: {i + 1}/{totalCount} articles traités ({successfullyConfirmed.Count} confirmés)");
                }

                // Pause pour éviter de surcharger l'API
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

                // Si ce n'est pas la dernière tentative, attendre
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

        /// <summary>
        /// Confirme plusieurs articles avec retry
        /// </summary>
        public async Task<int> ConfirmMultipleItemsWithRetryAsync(string token, List<string> itemIds, int maxRetries = 3)
        {
            int successCount = 0;
            int totalCount = itemIds.Count;

            _logger.LogInformation($"📤 Début de confirmation avec retry pour {totalCount} articles (max {maxRetries} tentatives)...");

            for (int i = 0; i < itemIds.Count; i++)
            {
                var itemId = itemIds[i];
                bool success = await ConfirmItemReceivedWithRetryAsync(token, itemId, maxRetries);

                if (success)
                {
                    successCount++;
                }

                if ((i + 1) % 3 == 0 || (i + 1) == totalCount)
                {
                    _logger.LogInformation($"📊 Progrès confirmations: {i + 1}/{totalCount} articles traités ({successCount} réussies)");
                }

                await Task.Delay(500);
            }

            var successRate = totalCount > 0 ? (double)successCount / totalCount * 100 : 0;
            _logger.LogInformation($"✅ Confirmations avec retry terminées: {successCount}/{totalCount} articles confirmés ({successRate:F1}% succès)");

            return successCount;
        }
    }
}