// Fichier: Services/AuthenticationService.cs
// Service d'authentification OAuth2 pour l'API Dynamics 365

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services
{
    public class AuthenticationService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AuthenticationService> _logger;

        private string? _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public AuthenticationService(
            IConfiguration configuration, 
            HttpClient httpClient,
            ILogger<AuthenticationService> logger)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Obtient un token d'accès pour l'API Dynamics 365
        /// </summary>
        public async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                // Vérifier si le token en cache est encore valide
                if (!string.IsNullOrEmpty(_cachedToken) && DateTime.Now < _tokenExpiry.AddMinutes(-5))
                {
                    _logger.LogDebug("Utilisation du token en cache");
                    return _cachedToken;
                }

                _logger.LogInformation("Demande d'un nouveau token d'authentification");

                var tenantId = _configuration["TenantId"];
                var clientId = _configuration["ClientId"];
                var clientSecret = _configuration["ClientSecret"];
                var resourceUrl = _configuration["ResourceUrl"];

                if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || 
                    string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(resourceUrl))
                {
                    _logger.LogError("Configuration d'authentification incomplète");
                    return null;
                }

                var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";

                var requestBody = new List<KeyValuePair<string, string>>
                {
                    new("grant_type", "client_credentials"),
                    new("client_id", clientId),
                    new("client_secret", clientSecret),
                    new("resource", resourceUrl.TrimEnd('/'))
                };

                var content = new FormUrlEncodedContent(requestBody);

                _logger.LogDebug($"Demande de token vers: {tokenUrl}");

                var response = await _httpClient.PostAsync(tokenUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Erreur d'authentification {response.StatusCode}: {responseContent}");
                    return null;
                }

                _logger.LogDebug($"Réponse de token reçue: {responseContent}");

                // Désérialiser avec gestion flexible des types
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent, options);

                if (tokenResponse?.AccessToken == null)
                {
                    _logger.LogError("Token d'accès manquant dans la réponse");
                    return null;
                }

                // Mettre en cache le token avec expiration
                _cachedToken = tokenResponse.AccessToken;
                
                // Par défaut, expiration à 1 heure, ou utiliser expires_in si disponible
                var expiresInSeconds = tokenResponse.ExpiresIn ?? 3600;
                _tokenExpiry = DateTime.Now.AddSeconds(expiresInSeconds);

                _logger.LogInformation($"Token obtenu avec succès, expire dans {expiresInSeconds} secondes");

                return _cachedToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'obtention du token d'authentification");
                return null;
            }
        }

        /// <summary>
        /// Valide la configuration d'authentification
        /// </summary>
        public bool ValidateConfiguration()
        {
            var tenantId = _configuration["TenantId"];
            var clientId = _configuration["ClientId"];
            var clientSecret = _configuration["ClientSecret"];
            var resourceUrl = _configuration["ResourceUrl"];

            var isValid = !string.IsNullOrEmpty(tenantId) &&
                         !string.IsNullOrEmpty(clientId) &&
                         !string.IsNullOrEmpty(clientSecret) &&
                         !string.IsNullOrEmpty(resourceUrl);

            if (!isValid)
            {
                _logger.LogError("Configuration d'authentification invalide. Vérifiez TenantId, ClientId, ClientSecret et ResourceUrl");
            }

            return isValid;
        }

        /// <summary>
        /// Force le renouvellement du token
        /// </summary>
        public void ClearTokenCache()
        {
            _cachedToken = null;
            _tokenExpiry = DateTime.MinValue;
            _logger.LogDebug("Cache du token vidé");
        }
    }

    /// <summary>
    /// Réponse d'authentification OAuth2 avec gestion flexible des types
    /// </summary>
    public class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        [JsonConverter(typeof(StringToIntConverter))]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("resource")]
        public string? Resource { get; set; }

        [JsonPropertyName("not_before")]
        [JsonConverter(typeof(StringToIntConverter))]
        public int? NotBefore { get; set; }

        [JsonPropertyName("expires_on")]
        [JsonConverter(typeof(StringToIntConverter))]
        public int? ExpiresOn { get; set; }
    }

    /// <summary>
    /// Convertisseur JSON pour gérer les entiers qui arrivent comme chaînes
    /// </summary>
    public class StringToIntConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                if (int.TryParse(stringValue, out var intValue))
                {
                    return intValue;
                }
                return null;
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt32();
            }
            else if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            throw new JsonException($"Cannot convert token type {reader.TokenType} to int");
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}