// Fichier: Services/StatusUpdateService.cs
// Service pour mettre à jour le statut INT3PLStatus sur l'API Dynamics

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service pour mettre à jour le statut INT3PLStatus des articles
    /// </summary>
    public class StatusUpdateService
    {
        private readonly ILogger<StatusUpdateService> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public StatusUpdateService(
            ILogger<StatusUpdateService> logger,
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;

            // 🔧 Récupération de l'URL de base depuis la configuration (même clé que AuthenticationService)
            _baseUrl = _configuration["Resource"];

            // Debug pour vérifier l'URL
            Console.WriteLine($"🔧 StatusUpdateService - URL de base : {_baseUrl}");

            // Validation de l'URL
            if (string.IsNullOrEmpty(_baseUrl))
            {
                Console.WriteLine("❌ URL de base manquante dans la configuration (Resource)");
                _logger.LogError("URL de base manquante dans la configuration");
            }
        }

        /// <summary>
        /// Met à jour le statut INT3PLStatus d'un article
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="itemId">ID de l'article</param>
        /// <param name="newStatus">Nouveau statut (ex: "Récupéré", "Traité", etc.)</param>
        /// <param name="etag">ETag de l'article pour la concurrence optimiste</param>
        /// <returns>True si la mise à jour a réussi</returns>
        public async Task<bool> UpdateArticleStatusAsync(string token, string itemId, string newStatus, string etag = null)
        {
            try
            {
                _logger.LogInformation($"Mise à jour du statut INT3PLStatus pour l'article {itemId} -> {newStatus}");
                Console.WriteLine($"🔄 Mise à jour du statut pour l'article {itemId}...");

                // 🔧 Construction de l'URL correcte (comme les autres services)
                // Enlever le slash final de _baseUrl s'il existe pour éviter les doubles slashes
                string baseUrl = _baseUrl.TrimEnd('/');
                string endpoint = $"{baseUrl}/data/BRINT34ReleasedProducts(dataAreaId='br',ItemId='{itemId}')";

                // Debug de l'URL construite
                Console.WriteLine($"🔗 URL construite: {endpoint}");

                // Configuration des headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Si on a un ETag, l'ajouter pour la concurrence optimiste
                if (!string.IsNullOrEmpty(etag))
                {
                    _httpClient.DefaultRequestHeaders.Add("If-Match", etag);
                }

                // Création du payload JSON pour la mise à jour
                var updateData = new
                {
                    INT3PLStatus = newStatus
                };

                string jsonContent = JsonSerializer.Serialize(updateData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 🔧 ESSAI 1: Tentative avec PATCH
                var patchResponse = await _httpClient.PatchAsync(endpoint, content);

                if (patchResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Statut mis à jour avec succès (PATCH) pour l'article {itemId}");
                    Console.WriteLine($"✅ Statut mis à jour: {itemId} -> {newStatus}");
                    return true;
                }

                // 🔧 ESSAI 2: Si PATCH échoue, essayer avec PUT
                if (patchResponse.StatusCode == System.Net.HttpStatusCode.NotFound ||
                    patchResponse.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    Console.WriteLine($"⚠️ PATCH échoué, tentative avec PUT...");

                    // Reset content for PUT
                    content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    if (!string.IsNullOrEmpty(etag))
                    {
                        _httpClient.DefaultRequestHeaders.Add("If-Match", etag);
                    }

                    var putResponse = await _httpClient.PutAsync(endpoint, content);

                    if (putResponse.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"✅ Statut mis à jour avec succès (PUT) pour l'article {itemId}");
                        Console.WriteLine($"✅ Statut mis à jour: {itemId} -> {newStatus}");
                        return true;
                    }
                    else
                    {
                        string putErrorContent = await putResponse.Content.ReadAsStringAsync();
                        _logger.LogError($"❌ Erreur PUT pour {itemId}: {putResponse.StatusCode} - {putErrorContent}");
                        Console.WriteLine($"❌ Erreur PUT {putResponse.StatusCode} pour {itemId}: {putErrorContent}");
                    }
                }

                // 🔧 ESSAI 3: Vérifier si l'article existe en faisant d'abord un GET
                Console.WriteLine($"🔍 Vérification de l'existence de l'article {itemId}...");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var getResponse = await _httpClient.GetAsync(endpoint);

                if (getResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ Article {itemId} trouvé, mais mise à jour échoue");
                    string getContent = await getResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"📄 Données actuelles: {getContent.Substring(0, Math.Min(200, getContent.Length))}...");
                }
                else
                {
                    string getErrorContent = await getResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Article {itemId} non trouvé: {getResponse.StatusCode}");
                    Console.WriteLine($"📄 Erreur: {getErrorContent}");
                }

                // Retourner l'erreur PATCH originale
                string originalErrorContent = await patchResponse.Content.ReadAsStringAsync();
                _logger.LogError($"❌ Erreur lors de la mise à jour du statut pour {itemId}: {patchResponse.StatusCode} - {originalErrorContent}");
                Console.WriteLine($"❌ Erreur {patchResponse.StatusCode} pour {itemId}: {originalErrorContent}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception lors de la mise à jour du statut pour l'article {itemId}");
                Console.WriteLine($"❌ Exception pour {itemId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Met à jour le statut de plusieurs articles en lot
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="updates">Liste des mises à jour à effectuer</param>
        /// <returns>Nombre de mises à jour réussies</returns>
        public async Task<int> UpdateMultipleArticleStatusAsync(string token, List<ArticleStatusUpdate> updates)
        {
            int successCount = 0;
            int totalCount = updates.Count;

            Console.WriteLine($"🔄 Début de la mise à jour de {totalCount} articles...");

            for (int i = 0; i < updates.Count; i++)
            {
                var update = updates[i];

                bool success = await UpdateArticleStatusAsync(token, update.ItemId, update.NewStatus, update.ETag);

                if (success)
                {
                    successCount++;
                }

                // Affichage du progrès
                if ((i + 1) % 10 == 0 || (i + 1) == totalCount)
                {
                    Console.WriteLine($"📊 Progrès: {i + 1}/{totalCount} articles traités ({successCount} réussies)");
                }

                // Petite pause pour éviter de surcharger l'API
                await Task.Delay(100);
            }

            Console.WriteLine($"✅ Mise à jour terminée: {successCount}/{totalCount} articles mis à jour");
            _logger.LogInformation($"Mise à jour terminée: {successCount}/{totalCount} articles mis à jour");

            return successCount;
        }

        /// <summary>
        /// Récupère l'ETag actuel d'un article pour la concurrence optimiste
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="itemId">ID de l'article</param>
        /// <returns>L'ETag de l'article ou null si introuvable</returns>
        public async Task<string> GetArticleETagAsync(string token, string itemId)
        {
            try
            {
                string endpoint = $"{_baseUrl}/data/BRINT34ReleasedProducts(dataAreaId='br',ItemId='{itemId}')";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.GetAsync(endpoint);

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    var jsonDocument = JsonDocument.Parse(jsonContent);

                    if (jsonDocument.RootElement.TryGetProperty("@odata.etag", out var etagElement))
                    {
                        return etagElement.GetString();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération de l'ETag pour l'article {itemId}");
                return null;
            }
        }
    }

    /// <summary>
    /// Classe pour représenter une mise à jour de statut d'article
    /// </summary>
    public class ArticleStatusUpdate
    {
        public string ItemId { get; set; }
        public string NewStatus { get; set; }
        public string ETag { get; set; }
    }
}