// Fichier: Services/AuthenticationService.cs
// Service d'authentification Azure AD pour Dynamics 365

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
    /// Service d'authentification Azure AD pour accéder à Dynamics 365
    /// </summary>
    public class AuthenticationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AuthenticationService> _logger;
        private readonly IConfiguration _configuration;

        // Cache du token pour éviter les appels répétés
        private string? _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public AuthenticationService(HttpClient httpClient, ILogger<AuthenticationService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Obtient un token d'accès Azure AD pour Dynamics 365
        /// </summary>
        /// <returns>Token d'accès Bearer</returns>
        public async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                // Vérifier si on a un token en cache encore valide
                if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
                {
                    _logger.LogDebug("Utilisation du token en cache");
                    return _cachedToken;
                }

                Console.WriteLine("🔐 Authentification auprès d'Azure AD...");

                var tenantId = _configuration["TenantId"];
                var clientId = _configuration["ClientId"];
                var clientSecret = _configuration["ClientSecret"];
                var resource = _configuration["Resource"];

                // URL du service d'authentification Azure AD
                var authUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";

                // Paramètres de la requête
                var parameters = new List<KeyValuePair<string, string>>
                {
                    new("grant_type", "client_credentials"),
                    new("client_id", clientId ?? ""),
                    new("client_secret", clientSecret ?? ""),
                    new("resource", resource ?? "")
                };

                // Création de la requête
                using var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
                request.Content = new FormUrlEncodedContent(parameters);
                request.Headers.Add("Accept", "application/json");

                // Exécution de la requête
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Erreur d'authentification {response.StatusCode}: {errorContent}");
                    Console.WriteLine($"❌ Erreur d'authentification: {response.StatusCode}");
                    return null;
                }

                // Parse de la réponse
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<AuthTokenResponse>(jsonResponse);

                if (tokenResponse?.AccessToken == null)
                {
                    _logger.LogError("Token d'accès non trouvé dans la réponse");
                    Console.WriteLine("❌ Token d'accès non trouvé");
                    return null;
                }

                // Cache du token
                _cachedToken = tokenResponse.AccessToken;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 300); // 5 min de marge

                _logger.LogInformation("Token d'accès obtenu avec succès");
                Console.WriteLine("✅ Authentification réussie");

                return _cachedToken;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Erreur réseau lors de l'authentification");
                Console.WriteLine($"❌ Erreur réseau d'authentification: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Erreur de parsing de la réponse d'authentification");
                Console.WriteLine($"❌ Erreur de parsing de l'authentification: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur inattendue lors de l'authentification");
                Console.WriteLine($"❌ Erreur d'authentification inattendue: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Vérifie si la configuration d'authentification est valide
        /// </summary>
        /// <returns>True si la configuration est valide</returns>
        public bool ValidateConfiguration()
        {
            var tenantId = _configuration["TenantId"];
            var clientId = _configuration["ClientId"];
            var clientSecret = _configuration["ClientSecret"];
            var resource = _configuration["Resource"];

            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogError("TenantId manquant dans la configuration");
                Console.WriteLine("❌ TenantId manquant dans appsettings.json");
                return false;
            }

            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogError("ClientId manquant dans la configuration");
                Console.WriteLine("❌ ClientId manquant dans appsettings.json");
                return false;
            }

            if (string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("ClientSecret manquant dans la configuration");
                Console.WriteLine("❌ ClientSecret manquant dans appsettings.json");
                return false;
            }

            if (string.IsNullOrEmpty(resource))
            {
                _logger.LogError("Resource manquant dans la configuration");
                Console.WriteLine("❌ Resource manquant dans appsettings.json");
                return false;
            }

            // Vérification du format des URLs
            if (!Uri.IsWellFormedUriString(resource, UriKind.Absolute))
            {
                _logger.LogError($"Resource n'est pas une URL valide: {resource}");
                Console.WriteLine($"❌ Resource n'est pas une URL valide: {resource}");
                return false;
            }

            _logger.LogInformation("Configuration d'authentification valide");
            Console.WriteLine("✅ Configuration d'authentification valide");
            return true;
        }

        /// <summary>
        /// Force le renouvellement du token (invalide le cache)
        /// </summary>
        public void InvalidateToken()
        {
            _cachedToken = null;
            _tokenExpiry = DateTime.MinValue;
            _logger.LogInformation("Token invalidé - sera renouvelé au prochain appel");
        }

        /// <summary>
        /// Teste l'authentification en tentant d'obtenir un token
        /// </summary>
        /// <returns>True si l'authentification fonctionne</returns>
        public async Task<bool> TestAuthenticationAsync()
        {
            try
            {
                var token = await GetAccessTokenAsync();
                return !string.IsNullOrEmpty(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du test d'authentification");
                return false;
            }
        }

        /// <summary>
        /// Affiche des informations sur le token actuel (pour debug)
        /// </summary>
        public void DisplayTokenInfo()
        {
            if (string.IsNullOrEmpty(_cachedToken))
            {
                Console.WriteLine("ℹ️ Aucun token en cache");
                return;
            }

            var timeRemaining = _tokenExpiry - DateTime.UtcNow;
            if (timeRemaining.TotalMinutes > 0)
            {
                Console.WriteLine($"ℹ️ Token valide encore {timeRemaining.TotalMinutes:F0} minutes");
            }
            else
            {
                Console.WriteLine("⚠️ Token expiré");
            }
        }
    }

    /// <summary>
    /// Modèle pour la réponse d'authentification Azure AD
    /// </summary>
    public class AuthTokenResponse
    {
        public string? TokenType { get; set; }
        public int ExpiresIn { get; set; }
        public string? ExtExpiresIn { get; set; }
        public string? AccessToken { get; set; }

        // Propriétés JSON avec les noms d'origine
        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string? JsonTokenType
        {
            get => TokenType;
            set => TokenType = value;
        }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public string? JsonExpiresIn
        {
            get => ExpiresIn.ToString();
            set => ExpiresIn = int.TryParse(value, out var result) ? result : 3600;
        }

        [System.Text.Json.Serialization.JsonPropertyName("ext_expires_in")]
        public string? JsonExtExpiresIn
        {
            get => ExtExpiresIn;
            set => ExtExpiresIn = value;
        }

        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? JsonAccessToken
        {
            get => AccessToken;
            set => AccessToken = value;
        }
    }
}