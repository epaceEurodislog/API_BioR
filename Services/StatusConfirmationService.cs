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
        }

        /// <summary>
        /// Confirme la réception d'un article avec le statut ProcessedBy3PL
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="itemId">ID de l'article</param>
        /// <returns>True si la confirmation a réussi</returns>
        public async Task<bool> ConfirmItemReceivedAsync(string token, string itemId)
        {
            try
            {
                _logger.LogInformation($"📤 Confirmation de réception pour l'article {itemId}");

                // Construction de l'URL de l'endpoint
                var endpoint = $"{_baseUrl}/data/BRINT34ReleasedProducts/Microsoft.Dynamics.DataEntities.changeStatus";

                // Préparation des headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                // Création du payload JSON
                var payload = new
                {
                    _itemId = itemId,
                    _status = "ProcessedBy3PL"
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _logger.LogDebug($"🔍 Envoi vers: {endpoint}");
                _logger.LogDebug($"📋 Payload: {jsonPayload}");

                // Envoi de la requête POST
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
        /// <param name="token">Token d'authentification</param>
        /// <param name="itemIds">Liste des IDs d'articles</param>
        /// <returns>Nombre de confirmations réussies</returns>
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

        /// <summary>
        /// Confirme la réception d'un article avec retry en cas d'échec
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="itemId">ID de l'article</param>
        /// <param name="maxRetries">Nombre maximum de tentatives</param>
        /// <returns>True si la confirmation a réussi</returns>
        public async Task<bool> ConfirmItemReceivedWithRetryAsync(string token, string itemId, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    bool success = await ConfirmItemReceivedAsync(token, itemId);
                    if (success)
                    {
                        return true;
                    }

                    if (attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Délai exponentiel
                        _logger.LogWarning($"⚠️ Tentative {attempt} échouée pour {itemId}, retry dans {delay.TotalSeconds}s");
                        await Task.Delay(delay);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Erreur tentative {attempt} pour {itemId}");
                    if (attempt == maxRetries)
                    {
                        return false;
                    }
                }
            }

            _logger.LogError($"❌ Échec définitif de la confirmation pour {itemId} après {maxRetries} tentatives");
            return false;
        }
    }
}