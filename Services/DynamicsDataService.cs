// Fichier: Services/DynamicsDataService.cs
// Service pour récupérer les données depuis l'API Dynamics 365

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service pour récupérer les données depuis l'API Dynamics 365
    /// </summary>
    public class DynamicsDataService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DynamicsDataService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _baseUrl;

        public DynamicsDataService(HttpClient httpClient, ILogger<DynamicsDataService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _baseUrl = _configuration["Resource"] ?? "";
        }

        /// <summary>
        /// Récupère les données depuis un endpoint Dynamics 365
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="endpoint">Endpoint à interroger (ex: data/BRINT34ReleasedProducts)</param>
        /// <returns>Tableau des données JSON</returns>
        public async Task<JsonElement[]?> GetDataFromEndpointAsync(string token, string endpoint)
        {
            try
            {
                Console.WriteLine($"🌐 Récupération des données depuis {endpoint}...");

                // Configuration de la requête
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{endpoint}");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("OData-MaxVersion", "4.0");
                request.Headers.Add("OData-Version", "4.0");
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                // Timeout pour les grosses requêtes
                _httpClient.Timeout = TimeSpan.FromMinutes(10);

                // Exécution de la requête
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur API {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"❌ Erreur API {response.StatusCode}: {errorContent}");
                    return null;
                }

                // Lecture et parsing de la réponse
                var jsonContent = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(jsonContent))
                {
                    Console.WriteLine("⚠️ Réponse vide de l'API");
                    return Array.Empty<JsonElement>();
                }

                // Parse du JSON OData
                var jsonDocument = JsonDocument.Parse(jsonContent);

                // Récupérer le tableau "value" d'OData
                if (jsonDocument.RootElement.TryGetProperty("value", out var valueArray))
                {
                    var elements = new JsonElement[valueArray.GetArrayLength()];
                    var index = 0;

                    foreach (var element in valueArray.EnumerateArray())
                    {
                        elements[index++] = element.Clone();
                    }

                    Console.WriteLine($"✅ {elements.Length} enregistrements récupérés");
                    return elements;
                }
                else
                {
                    _logger.LogWarning($"Pas de propriété 'value' trouvée dans la réponse de {endpoint}");
                    Console.WriteLine("⚠️ Format de réponse inattendu (pas de propriété 'value')");
                    return Array.Empty<JsonElement>();
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"Erreur réseau lors de l'appel à {endpoint}");
                Console.WriteLine($"❌ Erreur réseau: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, $"Timeout lors de l'appel à {endpoint}");
                Console.WriteLine($"❌ Timeout de la requête vers {endpoint}");
                return null;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"Erreur de parsing JSON pour {endpoint}");
                Console.WriteLine($"❌ Erreur de parsing JSON: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur inattendue lors de l'appel à {endpoint}");
                Console.WriteLine($"❌ Erreur inattendue: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Récupère les données avec pagination automatique (pour les gros volumes)
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <param name="endpoint">Endpoint à interroger</param>
        /// <param name="pageSize">Taille de page (défaut: 1000)</param>
        /// <returns>Toutes les données paginées</returns>
        public async Task<JsonElement[]?> GetAllDataWithPaginationAsync(string token, string endpoint, int pageSize = 1000)
        {
            try
            {
                var allData = new List<JsonElement>();
                var skip = 0;
                var hasMoreData = true;

                Console.WriteLine($"🌐 Récupération paginée des données depuis {endpoint}...");

                while (hasMoreData)
                {
                    var paginatedEndpoint = $"{endpoint}?$top={pageSize}&$skip={skip}";
                    var pageData = await GetDataFromEndpointAsync(token, paginatedEndpoint);

                    if (pageData == null)
                    {
                        Console.WriteLine($"❌ Erreur lors de la récupération de la page (skip: {skip})");
                        return null;
                    }

                    if (pageData.Length == 0)
                    {
                        hasMoreData = false;
                    }
                    else
                    {
                        allData.AddRange(pageData);
                        skip += pageSize;

                        Console.WriteLine($"📄 Page récupérée: {pageData.Length} éléments (Total: {allData.Count})");

                        // Si on récupère moins que la taille de page, c'est la dernière page
                        if (pageData.Length < pageSize)
                        {
                            hasMoreData = false;
                        }
                    }
                }

                Console.WriteLine($"✅ Récupération paginée terminée: {allData.Count} éléments au total");
                return allData.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération paginée pour {endpoint}");
                Console.WriteLine($"❌ Erreur récupération paginée: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Teste la connectivité à l'API Dynamics
        /// </summary>
        /// <param name="token">Token d'authentification</param>
        /// <returns>True si la connexion fonctionne</returns>
        public async Task<bool> TestApiConnectivityAsync(string token)
        {
            try
            {
                Console.WriteLine("🔍 Test de connectivité à l'API Dynamics...");

                // Test simple avec un endpoint léger
                var testEndpoint = "data/Companies?$top=1";
                var testData = await GetDataFromEndpointAsync(token, testEndpoint);

                if (testData != null)
                {
                    Console.WriteLine("✅ Connectivité API Dynamics OK");
                    return true;
                }
                else
                {
                    Console.WriteLine("❌ Test de connectivité échoué");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du test de connectivité");
                Console.WriteLine($"❌ Erreur test connectivité: {ex.Message}");
                return false;
            }
        }
    }
}