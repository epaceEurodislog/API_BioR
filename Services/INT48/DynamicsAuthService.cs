// Fichier: Services/INT48/DynamicsAuthService.cs
// Service d'authentification pour Dynamics 365 Inventory Service
// Gestion des tokens Azure AD et Security Token

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services.INT48
{
    public class DynamicsAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DynamicsAuthService> _logger;
        private readonly HttpClient _httpClient;

        // Cache des tokens
        private string? _cachedAzureToken;
        private string? _cachedSecurityToken;
        private DateTime _tokenExpiration;

        public DynamicsAuthService(
            IConfiguration configuration, 
            ILogger<DynamicsAuthService> logger,
            HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Obtient les tokens d'authentification (Azure + Security)
        /// Utilise le cache si les tokens sont encore valides
        /// </summary>
        public async Task<(string azureToken, string securityToken)> GetAuthenticationTokensAsync()
        {
            // Vérifier si les tokens en cache sont encore valides (avec marge de 5 minutes)
            if (_tokenExpiration > DateTime.UtcNow.AddMinutes(5) && 
                !string.IsNullOrEmpty(_cachedAzureToken) && 
                !string.IsNullOrEmpty(_cachedSecurityToken))
            {
                _logger.LogDebug("🔑 Utilisation des tokens en cache");
                return (_cachedAzureToken, _cachedSecurityToken);
            }

            _logger.LogInformation("🔑 Obtention de nouveaux tokens d'authentification pour INT48");

            try
            {
                // Étape 1: Obtenir le token Azure AD
                var azureToken = await GetAzureAdTokenAsync();

                // Étape 2: Obtenir le token de sécurité Dynamics
                var securityToken = await GetDynamicsSecurityTokenAsync(azureToken);

                // Mettre en cache les tokens
                _cachedAzureToken = azureToken;
                _cachedSecurityToken = securityToken;
                _tokenExpiration = DateTime.UtcNow.AddHours(1); // Durée de validité du token

                _logger.LogInformation("✅ Tokens d'authentification obtenus avec succès");
                return (azureToken, securityToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de l'obtention des tokens d'authentification");
                throw;
            }
        }

        /// <summary>
        /// Obtient le token Azure AD via OAuth2
        /// </summary>
        private async Task<string> GetAzureAdTokenAsync()
        {
            var tenantId = _configuration["TenantId"] 
                ?? throw new Exception("TenantId manquant dans la configuration");
            var clientId = _configuration["INT48:ClientId"] 
                ?? throw new Exception("INT48:ClientId manquant dans la configuration");
            var clientSecret = _configuration["INT48:ClientSecret"] 
                ?? throw new Exception("INT48:ClientSecret manquant dans la configuration");
            var resource = _configuration["INT48:Resource"] 
                ?? throw new Exception("INT48:Resource manquant dans la configuration");

            var url = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("resource", resource)
            });

            _logger.LogDebug($"🔐 Demande de token Azure AD pour resource: {resource}");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Erreur lors de l'obtention du token Azure AD: {response.StatusCode}");
                _logger.LogError($"Réponse: {responseContent}");
                throw new Exception($"Erreur d'authentification Azure AD: {response.StatusCode}");
            }

            var jsonResponse = JsonDocument.Parse(responseContent);
            var accessToken = jsonResponse.RootElement.GetProperty("access_token").GetString()
                ?? throw new Exception("Access token manquant dans la réponse");

            _logger.LogInformation("✅ Token Azure AD obtenu avec succès");
            return accessToken;
        }

        /// <summary>
        /// Obtient le token de sécurité Dynamics 365
        /// </summary>
        private async Task<string> GetDynamicsSecurityTokenAsync(string azureToken)
        {
            var url = "https://securityservice.operations365.dynamics.com/token";

            var environmentId = _configuration["INT48:EnvironmentId"]
                ?? throw new Exception("INT48:EnvironmentId manquant dans la configuration");

            var payload = new
            {
                grant_type = "client_credentials",
                client_assertion_type = "aad_app",
                client_assertion = azureToken,
                scope = "https://inventoryservice.operations365.dynamics.com/.default",
                context = environmentId,
                context_type = "finops-env"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {azureToken}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), 
                System.Text.Encoding.UTF8, 
                "application/json"
            );

            _logger.LogDebug($"🔐 Demande de token de sécurité Dynamics (context: {environmentId})");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Erreur lors de l'obtention du token Dynamics: {response.StatusCode}");
                _logger.LogError($"Réponse: {responseContent}");
                throw new Exception($"Erreur d'authentification Dynamics: {response.StatusCode}");
            }

            _logger.LogInformation("✅ Token de sécurité Dynamics obtenu avec succès");

            // La réponse est un JSON { "access_token": "..." }
            var json = JsonDocument.Parse(responseContent);
            var token = json.RootElement.GetProperty("access_token").GetString()
                ?? throw new Exception("Access token manquant dans la réponse Security Service");

            _logger.LogDebug($"🔑 Security token reçu (len={token.Length})");
            return token;
        }

        /// <summary>
        /// Invalide le cache des tokens (forcer un rafraîchissement)
        /// </summary>
        public void InvalidateTokenCache()
        {
            _cachedAzureToken = null;
            _cachedSecurityToken = null;
            _tokenExpiration = DateTime.MinValue;
            _logger.LogInformation("🔄 Cache des tokens invalidé");
        }
    }
}