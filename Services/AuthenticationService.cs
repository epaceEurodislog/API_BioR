// Fichier: Services/AuthenticationService.cs
// Service d'authentification avec l'API Dynamics 365

using System;
using System.Collections.Generic;
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
    /// Service d'authentification avec l'API Dynamics 365
    /// </summary>
    public class AuthenticationService
    {
        private readonly ILogger<AuthenticationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public AuthenticationService(ILogger<AuthenticationService> logger, IConfiguration configuration, HttpClient httpClient)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Obtient un token d'accès pour l'API Dynamics
        /// </summary>
        /// <returns>Le token d'accès ou null en cas d'erreur</returns>
        public async Task<string> GetAccessTokenAsync()
        {
            try
            {
                _logger.LogInformation("Demande d'authentification auprès de l'API Dynamics");
                Console.WriteLine("🔐 Authentification en cours...");

                var tokenUrl = $"https://login.microsoftonline.com/{_configuration["TenantId"]}/oauth2/token";

                var parameters = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _configuration["ClientId"]),
                    new KeyValuePair<string, string>("client_secret", _configuration["ClientSecret"]),
                    new KeyValuePair<string, string>("resource", _configuration["Resource"])
                };

                var content = new FormUrlEncodedContent(parameters);
                var response = await _httpClient.PostAsync(tokenUrl, content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Erreur d'authentification HTTP {response.StatusCode}: {responseText}");
                    Console.WriteLine($"❌ Erreur d'authentification: {response.StatusCode}");
                    return null;
                }

                var tokenData = JsonSerializer.Deserialize<TokenResponse>(responseText);

                if (string.IsNullOrEmpty(tokenData?.access_token))
                {
                    _logger.LogError("Token d'accès vide dans la réponse");
                    Console.WriteLine("❌ Token d'accès vide");
                    return null;
                }

                _logger.LogInformation("Token d'accès obtenu avec succès");
                Console.WriteLine("✅ Authentification réussie");
                return tokenData.access_token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'obtention du token d'accès");
                Console.WriteLine($"❌ Erreur d'authentification: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Vérifie si les paramètres de configuration sont corrects
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

            _logger.LogInformation("Configuration d'authentification valide");
            return true;
        }
    }
}