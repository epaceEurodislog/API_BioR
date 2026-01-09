using DynamicsApiToDatabase.Models.INT39;
using DynamicsApiToDatabase.DataAccess.INT39;
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
        private readonly IJsonOutService _jsonOutService;
        private readonly SpeedWmsTrackingRepository _repository;
        private readonly TrackingNumberService _trackingNumberService;
        private readonly string _baseUrl;

        public TrackingNumberIntegrationService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<TrackingNumberIntegrationService> logger,
            AuthenticationService authService,
            IJsonOutService jsonOutService,
            SpeedWmsTrackingRepository repository,
            TrackingNumberService trackingNumberService)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _authService = authService;
            _jsonOutService = jsonOutService;
            _repository = repository;
            _trackingNumberService = trackingNumberService;
            _baseUrl = configuration["ResourceUrl"];
        }

/// <summary>
/// Transforme les données SPEED en requête D365
/// </summary>
private TrackingNumberD365Request MapToD365Request(TrackingNumberModel model)
{
    // Conversion de OPE_DATETIME (string "YYYYMMDD HHmmss") vers format ISO
    string docStatusDate = "1900-01-01T00:00:00Z"; // Valeur par défaut
    
    if (!string.IsNullOrWhiteSpace(model.OPE_DATETIME)) // ✅ Ajout WhiteSpace check
    {
        try
        {
            var trimmed = model.OPE_DATETIME.Trim();
            
            if (trimmed.Length >= 15)
            {
                var parts = trimmed.Split(' ');
                if (parts.Length == 2 && parts[0].Length == 8 && parts[1].Length >= 6)
                {
                    var datePart = parts[0]; // YYYYMMDD
                    var timePart = parts[1].PadRight(6, '0'); // HHmmss
                    
                    if (int.TryParse(datePart.Substring(0, 4), out var year) &&
                        int.TryParse(datePart.Substring(4, 2), out var month) &&
                        int.TryParse(datePart.Substring(6, 2), out var day) &&
                        int.TryParse(timePart.Substring(0, 2), out var hour) &&
                        int.TryParse(timePart.Substring(2, 2), out var minute) &&
                        int.TryParse(timePart.Substring(4, 2), out var second))
                    {
                        var dateTime = new DateTime(year, month, day, hour, minute, second);
                        docStatusDate = dateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Impossible de parser OPE_DATETIME: '{model.OPE_DATETIME}' - {ex.Message}");
        }
    }

    // Conversion OPE_TOP22 : 'Oui' -> '1', 'Non' -> '0'
    string brDocuStatus = model.OPE_TOP22 == "Oui" ? "1" : "0";

    return new TrackingNumberD365Request
    {
        DataAreaId = "br",
        BROrderId = model.OPE_REDO ?? string.Empty,
        BRTrackingNumber = model.SEX_TRAK ?? string.Empty,
        BR3PLPackingSlipId = model.OPE_KEYU ?? string.Empty,
        BRDocuStatus = brDocuStatus,
        BRDOcStatusDate = docStatusDate,
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
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ Erreur POST Tracking Number : {response.StatusCode} - {errorContent}");
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
        /// Processus complet : SELECT Speed → INSERT JSON_OUT → UPDATE TOP15 → Envoi API → UPDATE JSON_OUT
        /// </summary>
        public async Task<bool> ProcessTrackingNumberAsync(TrackingNumberModel model)
        {
            string importId = $"INT39_{model.OPE_REDO}_{DateTime.Now:yyyyMMddHHmmss}";
            
            _logger.LogInformation($"🔄 Traitement complet Tracking Number : {model.OPE_REDO}");

            try
            {
                var request = MapToD365Request(model);
                
                // ÉTAPE 1: SELECT données depuis Speed (déjà faits lors du GetAllTrackingNumbersAsync)
                _logger.LogDebug($"📋 ÉTAPE 1 : Données Speed chargées pour {model.OPE_REDO}");

                // ÉTAPE 2: INSERT dans JSON_OUT (statut EN_ATTENTE)
                _logger.LogInformation($"📝 ÉTAPE 2 : INSERT JSON_OUT");
                await LogPendingToJsonOutAsync(request, importId);

                // ÉTAPE 3: UPDATE OPE_TOP15=1 dans Speed pour cette commande
                _logger.LogInformation($"🔄 ÉTAPE 3 : Mise à jour OPE_TOP15 pour {model.OPE_REDO}");
                try
                {
                    var rowsUpdated = await _trackingNumberService.UpdateOpeTop15ForInt39TrackingAsync();
                    _logger.LogInformation($"✅ {rowsUpdated} ligne(s) mise(s) à jour - OPE_TOP15 = 1");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"⚠️ Erreur lors de la mise à jour OPE_TOP15 pour {model.OPE_REDO}");
                    // Continue quand même avec l'envoi API
                }

                // ÉTAPE 4: POST vers BRTrackingNumbers (Envoi API)
                _logger.LogInformation($"🚀 ÉTAPE 4 : Envoi API");
                var postSuccess = await PostTrackingNumberInternalAsync(request);
                if (!postSuccess)
                {
                    _logger.LogError($"❌ Échec POST, arrêt du traitement pour {model.OPE_REDO}");
                    await UpdateJsonOutErrorAsync(request, importId, "POST échoué");
                    return false;
                }

                // Petit délai entre les deux appels API
                await Task.Delay(500);

                // Validation POST (optionnel)
                var validateSuccess = await ValidateTrackingNumberAsync(model.OPE_REDO);
                if (!validateSuccess)
                {
                    _logger.LogWarning($"⚠️ POST réussi mais validation échouée pour {model.OPE_REDO}");
                    await UpdateJsonOutErrorAsync(request, importId, "Validation échouée");
                    return false;
                }

                // ÉTAPE 5: UPDATE JSON_OUT de EN_ATTENTE à ENVOYE
                _logger.LogInformation($"📝 ÉTAPE 5 : UPDATE JSON_OUT - ENVOYE");
                await UpdateJsonOutSuccessAsync(request, importId);

                // UPDATE OPE_TOP39=1 dans OPE_DAT (marquage comme traité)
                await _repository.MarkTrackingAsProcessedAsync(
                    new List<string> { model.OPE_KEYU }, 
                    importId, 
                    success: true);

                _logger.LogInformation($"✅ Traitement complet réussi pour {model.OPE_REDO}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception traitement tracking {model.OPE_REDO}");
                await UpdateJsonOutErrorAsync(MapToD365Request(model), importId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// ÉTAPE A: INSERT initial dans JSON_OUT avec statut EN_ATTENTE
        /// </summary>
        private async Task LogPendingToJsonOutAsync(TrackingNumberD365Request request, string importId)
        {
            try
            {
                var jsonPayload = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                await _jsonOutService.LogJsonSentAsync(
                    itemId: request.BR3PLPackingSlipId,
                    jsonPayload: jsonPayload,
                    endpoint: "INT39_TRACKING",
                    responseContent: "EN_ATTENTE",
                    httpCode: 0,
                    importId: importId,
                    status: "EN_ATTENTE"
                );

                _logger.LogDebug($"📝 Tracking {request.BROrderId} enregistré dans JSON_OUT - EN_ATTENTE");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur INSERT JSON_OUT pour {request.BROrderId}");
                throw; // Bloquer si INSERT échoue
            }
        }

        /// <summary>
        /// ÉTAPE B: POST vers BRTrackingNumbers (version interne sans double log)
        /// </summary>
        private async Task<bool> PostTrackingNumberInternalAsync(TrackingNumberD365Request request)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var endpoint = $"{_baseUrl}data/BRTrackingNumbers";

                _logger.LogInformation($"📤 POST Tracking Number - OrderId: {request.BROrderId}");

                var jsonContent = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    WriteIndented = false
                });

                _logger.LogDebug($"JSON Request:\n{jsonContent}");

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ Tracking Number créé/mis à jour : {request.BROrderId}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ Erreur POST Tracking Number : {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception POST Tracking Number : {request.BROrderId}");
                return false;
            }
        }

        /// <summary>
        /// ÉTAPE D: UPDATE JSON_OUT avec succès (ENVOYE)
        /// </summary>
        private async Task UpdateJsonOutSuccessAsync(TrackingNumberD365Request request, string importId)
        {
            try
            {
                await _jsonOutService.UpdateJsonOutStatusAsync(
                    itemId: request.BR3PLPackingSlipId,
                    importId: importId,
                    newStatus: "ENVOYE",
                    responseContent: "Tracking envoyé avec succès",
                    httpCode: 200
                );

                _logger.LogDebug($"📝 Tracking {request.BROrderId} mis à jour - ENVOYE");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur UPDATE JSON_OUT succès pour {request.BROrderId}");
            }
        }

        /// <summary>
        /// <summary>
        /// ÉTAPE D (erreur): UPDATE JSON_OUT avec erreur (ERREUR)
        /// </summary>
        private async Task UpdateJsonOutErrorAsync(TrackingNumberD365Request request, string importId, string errorMessage)
        {
            try
            {
                await _jsonOutService.UpdateJsonOutStatusAsync(
                    itemId: request.BR3PLPackingSlipId,
                    importId: importId,
                    newStatus: "ERREUR",
                    responseContent: errorMessage,
                    httpCode: 500
                );

                _logger.LogDebug($"📝 Tracking {request.BROrderId} mis à jour - ERREUR");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"⚠️ Erreur UPDATE JSON_OUT erreur pour {request.BROrderId}");
            }
        }
    }
}