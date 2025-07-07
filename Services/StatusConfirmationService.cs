// Fichier: Services/StatusConfirmationService.cs
// Service pour envoyer la confirmation de réception vers l'API Dynamics 365

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
        private readonly string _baseUrl;
        private readonly string _dataAreaId;

        public StatusConfirmationService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<StatusConfirmationService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _baseUrl = configuration["ResourceUrl"]?.TrimEnd('/')
                ?? throw new ArgumentNullException("ResourceUrl manquante");
            _dataAreaId = configuration["DataAreaId"] ?? "br";
        }

        /// <summary>
        /// Confirme la réception d'un article avec le statut ProcessedBy3PL
        /// </summary>
        public async Task<bool> ConfirmItemReceivedAsync(string token, string itemId)
        {
            try
            {
                _logger.LogInformation($"📤 Confirmation de réception pour l'article {itemId}");

                var endpoint = $"{_baseUrl}/data/BRINT34ReleasedProducts/Microsoft.Dynamics.DataEntities.changeStatus";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var payload = new
                {
                    _itemId = itemId,
                    _status = "ProcessedBy3PL",
                    _dataAreaId = _dataAreaId
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _logger.LogDebug($"🔍 Envoi vers: {endpoint}");
                _logger.LogDebug($"📋 Payload: {jsonPayload}");

                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Confirmation réussie pour l'article {itemId}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ Erreur lors de la confirmation pour {itemId}: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception lors de la confirmation pour l'article {itemId}");
                return false;
            }
        }

        /// <summary>
        /// Confirme la réception de plusieurs articles en lot
        /// </summary>
        public async Task<int> ConfirmMultipleItemsReceivedAsync(string token, List<string> itemIds)
        {
            int successCount = 0;
            int totalCount = itemIds.Count;

            _logger.LogInformation($"📤 Début de confirmation pour {totalCount} articles...");

            for (int i = 0; i < itemIds.Count; i++)
            {
                var itemId = itemIds[i];
                bool success = await ConfirmItemReceivedAsync(token, itemId);

                if (success)
                {
                    successCount++;
                }

                // Affichage du progrès tous les 10 articles
                if ((i + 1) % 10 == 0 || (i + 1) == totalCount)
                {
                    _logger.LogInformation($"📊 Progrès confirmations: {i + 1}/{totalCount} articles traités ({successCount} réussies)");
                }

                // Pause pour éviter de surcharger l'API
                await Task.Delay(200);
            }

            _logger.LogInformation($"✅ Confirmations terminées: {successCount}/{totalCount} articles confirmés");
            return successCount;
        }
    }
}