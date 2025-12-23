using DynamicsApiToDatabase.Models.INT39;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace DynamicsApiToDatabase.Services
{
    public class TrackingNumberIntegrationService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TrackingNumberIntegrationService> _logger;
        private readonly AuthenticationService _authService;
        private readonly string _baseUrl;

        public TrackingNumberIntegrationService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<TrackingNumberIntegrationService> logger,
            AuthenticationService authService)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _authService = authService;
            _baseUrl = configuration["ResourceUrl"];
        }

        /// <summary>
        /// Transforme les données SPEED en requête D365
        /// </summary>
        private TrackingNumberD365Request MapToD365Request(TrackingNumberModel model)
        {
            // Gestion de la date de statut de documentation
            string docStatusDate = "1900-01-01T00:00:00Z"; // Valeur par défaut
            if (model.OPE_DATEHEURE11.HasValue)
            {
                docStatusDate = model.OPE_DATEHEURE11.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
            }

            return new TrackingNumberD365Request
            {
                DataAreaId = "br",
                BROrderId = model.OPE_REDO, // Référence donneur ordre
                BRTrackingNumber = model.SEX_URLT ?? string.Empty, // URL Tracking
                BR3PLPackingSlipId = model.OPE_KEYU ?? string.Empty, // N° Expédition STACI
                BRDocStatus = model.OPE_TOP22 ?? "0", // Doc reçu (0 ou 1)
                BRDocStatusDate = docStatusDate,
                CarrierCode = model.OPE_CTRA ?? string.Empty
            };
        }

        /// <summary>
        /// Étape 1 : POST vers BRTrackingNumbers (création/mise à jour)
        /// </summary>
        public async Task<bool> PostTrackingNumberAsync(TrackingNumberModel model)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var request = MapToD365Request(model);
                var endpoint = $"{_baseUrl}data/BRTrackingNumbers";

                _logger.LogInformation($"📤 POST Tracking Number - OrderId: {request.BROrderId}");

                var jsonContent = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                _logger.LogDebug($"JSON Request:\n{jsonContent}");

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Tracking Number créé/mis à jour : {request.BROrderId}");
                    
                    // Log dans JSON_OUT
                    await LogToJsonOutAsync(request, "POST_SUCCESS", await response.Content.ReadAsStringAsync());
                    
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ Erreur POST Tracking Number : {response.StatusCode} - {errorContent}");
                    
                    await LogToJsonOutAsync(request, "POST_ERROR", errorContent);
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception POST Tracking Number : {model.OPE_REDO}");
                return false;
            }
        }

        /// <summary>
        /// Étape 2 : POST vers PostTrackingNumber (validation)
        /// </summary>
        public async Task<bool> ValidateTrackingNumberAsync(string orderId)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var endpoint = $"{_baseUrl}data/BRTrackingNumbers/Microsoft.Dynamics.DataEntities.PostTrackingNumber";

                _logger.LogInformation($"✔️ Validation Tracking Number - OrderId: {orderId}");

                // Le body peut être vide ou contenir l'OrderId selon l'API
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Tracking Number validé : {orderId}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ Erreur validation Tracking Number : {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception validation Tracking Number : {orderId}");
                return false;
            }
        }

        /// <summary>
        /// Processus complet : POST + Validation
        /// </summary>
        public async Task<bool> ProcessTrackingNumberAsync(TrackingNumberModel model)
        {
            _logger.LogInformation($"🔄 Traitement complet Tracking Number : {model.OPE_REDO}");

            // Étape 1 : POST
            var postSuccess = await PostTrackingNumberAsync(model);
            if (!postSuccess)
            {
                _logger.LogError($"❌ Échec POST, arrêt du traitement pour {model.OPE_REDO}");
                return false;
            }

            // Petit délai entre les deux appels
            await Task.Delay(500);

            // Étape 2 : Validation
            var validateSuccess = await ValidateTrackingNumberAsync(model.OPE_REDO);
            if (!validateSuccess)
            {
                _logger.LogWarning($"⚠️ POST réussi mais validation échouée pour {model.OPE_REDO}");
                return false;
            }

            _logger.LogInformation($"✅ Traitement complet réussi pour {model.OPE_REDO}");
            return true;
        }

        /// <summary>
        /// Log dans la table JSON_OUT_INT39
        /// </summary>
        private async Task LogToJsonOutAsync(TrackingNumberD365Request request, string status, string response)
        {
            // TODO: Implémenter l'insertion dans JSON_OUT_INT39
            // Similaire aux autres endpoints
            _logger.LogDebug($"📝 Log JSON_OUT_INT39 : {status} - {request.BROrderId}");
        }
    }
}