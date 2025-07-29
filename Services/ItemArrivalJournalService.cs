// Fichier: Services/ItemArrivalJournalService.cs
// Service principal pour traiter les journaux de réception ItemArrival
// Gère les 3 endpoints : Headers, Lines, et Confirmation Post

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;
using DynamicsApiToDatabase.Services;

namespace DynamicsApiToDatabase.Services
{
    public interface IItemArrivalJournalService
    {
        Task<ItemArrivalJournalReport> ProcessAllJournalsAsync(string authToken);
        Task<ItemArrivalJournalResult> ProcessSingleJournalAsync(string authToken, ItemArrivalJournalData journal);
    }

    /// <summary>
    /// Service principal pour traiter les journaux de réception
    /// Architecture similaire à BLExportService avec 3 POST séquentiels
    /// </summary>
    public class ItemArrivalJournalService : IItemArrivalJournalService
    {
        private readonly IREEDataService _reeDataService;
        private readonly IJsonOutService _jsonOutService;
        private readonly ILogger<ItemArrivalJournalService> _logger;
        private readonly HttpClient _httpClient;
        private readonly ItemArrivalJournalConfig _config;
        private readonly string _baseUrl;

        public ItemArrivalJournalService(
            IREEDataService reeDataService,
            IJsonOutService jsonOutService,
            ILogger<ItemArrivalJournalService> logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _reeDataService = reeDataService;
            _jsonOutService = jsonOutService;
            _logger = logger;
            _httpClient = httpClient;
            _baseUrl = configuration["ResourceUrl"] ?? throw new ArgumentNullException("ResourceUrl manquante");

            _config = new ItemArrivalJournalConfig
            {
                DynamicsBaseUrl = _baseUrl,
                HeadersEndpoint = configuration["ItemArrivalJournal:HeadersEndpoint"] ?? "data/ItemArrivalJournalHeadersV2",
                LinesEndpoint = configuration["ItemArrivalJournal:LinesEndpoint"] ?? "data/ItemArrivalJournalLinesV2",
                ConfirmationEndpoint = configuration["ItemArrivalJournal:ConfirmationEndpoint"] ?? "api/services/BRINT41PostJournalServiceGroup/BRINT41PostJournalService/post",
                MaxRetryAttempts = configuration.GetValue<int>("ItemArrivalJournal:MaxRetryAttempts", 3),
                RetryDelaySeconds = configuration.GetValue<int>("ItemArrivalJournal:RetryDelaySeconds", 30),
                BatchSize = configuration.GetValue<int>("ItemArrivalJournal:BatchSize", 10),
                EnableConfirmationPost = configuration.GetValue<bool>("ItemArrivalJournal:EnableConfirmationPost", true)
            };
        }

        /// <summary>
        /// Traite tous les journaux de réception en attente
        /// </summary>
        public async Task<ItemArrivalJournalReport> ProcessAllJournalsAsync(string authToken)
        {
            var report = new ItemArrivalJournalReport();
            var startTime = DateTime.Now;

            try
            {
                _logger.LogInformation("🚀 === DÉBUT TRAITEMENT JOURNAUX DE RÉCEPTION === 🚀");

                // Récupérer les journaux en attente
                var journals = await _reeDataService.GetPendingJournalsAsync();
                report.TotalJournals = journals.Count;

                if (journals.Count == 0)
                {
                    _logger.LogInformation("📭 Aucun journal de réception en attente");
                    report.TotalProcessingTime = DateTime.Now - startTime;
                    return report;
                }

                _logger.LogInformation($"📝 {journals.Count} journaux de réception à traiter");

                // Traitement par batch
                var batches = journals.Chunk(_config.BatchSize);
                var batchNumber = 1;

                foreach (var batch in batches)
                {
                    _logger.LogInformation($"📦 Traitement batch {batchNumber} ({batch.Length} journaux)");

                    var batchTasks = batch.Select(journal => ProcessSingleJournalAsync(authToken, journal));
                    var batchResults = await Task.WhenAll(batchTasks);

                    foreach (var result in batchResults)
                    {
                        if (result.Success)
                        {
                            report.SuccessfulJournals++;
                            report.TotalHeaders += result.HeadersSent;
                            report.TotalLines += result.LinesSent;
                            if (result.IsConfirmed)
                            {
                                report.ConfirmedJournals++;
                            }
                        }
                        else
                        {
                            report.FailedJournals++;
                            report.ErrorMessages.Add($"Journal {result.JournalNumber}: {result.ErrorMessage}");
                        }
                    }

                    batchNumber++;

                    // Pause entre les batches
                    if (batchNumber <= batches.Count())
                    {
                        await Task.Delay(1000);
                    }
                }

                report.TotalProcessingTime = DateTime.Now - startTime;

                _logger.LogInformation("✅ === FIN TRAITEMENT JOURNAUX DE RÉCEPTION === ✅");
                _logger.LogInformation(report.GetSummary());

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du traitement des journaux de réception");
                report.ErrorMessages.Add($"Erreur globale: {ex.Message}");
                report.TotalProcessingTime = DateTime.Now - startTime;
                return report;
            }
        }

        /// <summary>
        /// Traite un journal de réception individuel avec les 3 POST séquentiels
        /// </summary>
        public async Task<ItemArrivalJournalResult> ProcessSingleJournalAsync(string authToken, ItemArrivalJournalData journal)
        {
            var result = new ItemArrivalJournalResult
            {
                JournalNumber = journal.JournalNumber,
                PackingSlipId = journal.PackingSlipId
            };
            var startTime = DateTime.Now;

            try
            {
                _logger.LogInformation($"🔄 Traitement journal {journal.JournalNumber} (PackingSlip: {journal.PackingSlipId})");

                await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "Début traitement", ItemArrivalJournalStatusConstants.Processing);

                // ÉTAPE 1: Envoyer les en-têtes
                _logger.LogDebug($"📤 1/3 - Envoi en-têtes journal {journal.JournalNumber}");
                var headersSuccess = await SendJournalHeaderAsync(authToken, journal);

                if (!headersSuccess)
                {
                    result.Success = false;
                    result.Status = ItemArrivalJournalStatusConstants.Error;
                    result.ErrorMessage = "Échec envoi en-têtes";
                    result.ProcessingTime = DateTime.Now - startTime;
                    await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "", result.Status, result.ErrorMessage);
                    return result;
                }

                result.HeadersSent = 1;
                await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "En-têtes envoyés", ItemArrivalJournalStatusConstants.HeadersSent);

                // ÉTAPE 2: Envoyer les lignes
                _logger.LogDebug($"📄 2/3 - Envoi {journal.Lines.Count} lignes journal {journal.JournalNumber}");
                var linesSuccess = await SendJournalLinesAsync(authToken, journal);

                if (!linesSuccess)
                {
                    result.Success = false;
                    result.Status = ItemArrivalJournalStatusConstants.Error;
                    result.ErrorMessage = "Échec envoi lignes";
                    result.ProcessingTime = DateTime.Now - startTime;
                    await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "", result.Status, result.ErrorMessage);
                    return result;
                }

                result.LinesSent = journal.Lines.Count;
                await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, $"{journal.Lines.Count} lignes envoyées", ItemArrivalJournalStatusConstants.LinesSent);

                // ÉTAPE 3: Confirmation (si activée)
                if (_config.EnableConfirmationPost)
                {
                    _logger.LogDebug($"✅ 3/3 - Confirmation journal {journal.JournalNumber}");
                    var confirmationSuccess = await SendJournalConfirmationAsync(authToken, journal);

                    if (confirmationSuccess)
                    {
                        result.Success = true;
                        result.Status = ItemArrivalJournalStatusConstants.Confirmed;
                        result.IsConfirmed = true;
                        _logger.LogInformation($"✅ Journal {journal.JournalNumber} traité avec succès et confirmé");
                        await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "Journal confirmé", result.Status);
                    }
                    else
                    {
                        result.Success = false;
                        result.Status = ItemArrivalJournalStatusConstants.PendingRetry;
                        result.ErrorMessage = "Confirmation échouée, en attente retry";
                        await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "", result.Status, result.ErrorMessage);
                    }
                }
                else
                {
                    // Si confirmation désactivée, considérer comme réussi après envoi des lignes
                    result.Success = true;
                    result.Status = ItemArrivalJournalStatusConstants.LinesSent;
                    await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "Journal traité (confirmation désactivée)", result.Status);
                }

                result.ProcessingTime = DateTime.Now - startTime;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception lors du traitement journal {journal.JournalNumber}");
                result.Success = false;
                result.Status = ItemArrivalJournalStatusConstants.Error;
                result.ErrorMessage = ex.Message;
                result.ProcessingTime = DateTime.Now - startTime;

                await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId, "", result.Status, ex.Message);
                return result;
            }
        }

        /// <summary>
        /// Envoie les en-têtes du journal (1er POST)
        /// </summary>
        private async Task<bool> SendJournalHeaderAsync(string authToken, ItemArrivalJournalData journal)
        {
            try
            {
                var headerPayload = new ItemArrivalJournalHeaderPayload
                {
                    DataAreaId = journal.DataAreaId,
                    JournalNameId = journal.JournalNameId,
                    DefaultReceivingSiteId = journal.DefaultReceivingSiteId,
                    DefaultReceivingWarehouseId = journal.DefaultReceivingWarehouseId,
                    DefaultReceivingWarehouseLocationId = journal.DefaultReceivingWarehouseLocationId,
                    DefaultTransactionReferenceNumber = journal.TransactionReferenceNumber,
                    PackingSlipId = journal.PackingSlipId
                };

                var endpoint = $"{_baseUrl}/{_config.HeadersEndpoint}";
                var success = await SendPayloadAsync(authToken, endpoint, headerPayload, journal.JournalNumber);

                if (success)
                {
                    _logger.LogInformation($"✅ En-têtes envoyés pour journal {journal.JournalNumber}");
                    await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId,
                        JsonSerializer.Serialize(headerPayload), "ItemArrivalHeaders");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur envoi en-têtes journal {journal.JournalNumber}");
                return false;
            }
        }

        /// <summary>
        /// Envoie les lignes du journal (2ème POST)
        /// </summary>
        private async Task<bool> SendJournalLinesAsync(string authToken, ItemArrivalJournalData journal)
        {
            try
            {
                var successCount = 0;
                var endpoint = $"{_baseUrl}/{_config.LinesEndpoint}";

                foreach (var line in journal.Lines)
                {
                    var linePayload = new ItemArrivalJournalLinePayload
                    {
                        DataAreaId = journal.DataAreaId,
                        JournalNumber = journal.JournalNumber,
                        LineNumber = line.LineNumber,
                        ItemNumber = line.ItemNumber,
                        TransactionDate = journal.TransactionDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ItemSerialNumber = line.ItemSerialNumber,
                        ItemBatchNumber = line.ItemBatchNumber,
                        ItemQuantity = line.ItemQuantity,
                        ExpDate = line.ExpDate?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "1900-01-01T00:00:00Z",
                        ReceivingInventoryStatusId = line.ReceivingInventoryStatusId
                    };

                    var success = await SendPayloadAsync(authToken, endpoint, linePayload, journal.JournalNumber);
                    if (success)
                    {
                        successCount++;
                        await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId,
                            JsonSerializer.Serialize(linePayload), "ItemArrivalLines");
                    }

                    // Petite pause entre les lignes
                    await Task.Delay(200);
                }

                var allSuccess = successCount == journal.Lines.Count;
                var message = $"{successCount}/{journal.Lines.Count} lignes envoyées";

                if (allSuccess)
                {
                    _logger.LogInformation($"✅ Journal {journal.JournalNumber}: {message}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ Journal {journal.JournalNumber}: {message} - succès partiel");
                }

                return allSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur envoi lignes journal {journal.JournalNumber}");
                return false;
            }
        }

        /// <summary>
        /// Envoie la confirmation du journal (3ème POST)
        /// </summary>
        private async Task<bool> SendJournalConfirmationAsync(string authToken, ItemArrivalJournalData journal)
        {
            try
            {
                var confirmationPayload = new ItemArrivalJournalConfirmationPayload
                {
                    Request = new ItemArrivalJournalConfirmationRequest
                    {
                        DataAreaId = journal.DataAreaId,
                        JournalNumber = journal.JournalNumber
                    }
                };

                var endpoint = $"{_baseUrl}/{_config.ConfirmationEndpoint}";
                var success = await SendPayloadAsync(authToken, endpoint, confirmationPayload, journal.JournalNumber);

                if (success)
                {
                    _logger.LogInformation($"✅ Confirmation envoyée pour journal {journal.JournalNumber}");
                    await _jsonOutService.LogItemArrivalJournalAsync(journal.JournalNumber, journal.PackingSlipId,
                        JsonSerializer.Serialize(confirmationPayload), "ItemArrivalConfirmation");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur confirmation journal {journal.JournalNumber}");
                return false;
            }
        }

        /// <summary>
        /// Envoie un payload individuel vers Dynamics
        /// </summary>
        private async Task<bool> SendPayloadAsync<T>(string authToken, string endpoint, T payload, string journalNumber)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                _logger.LogDebug($"📤 POST {endpoint}");
                _logger.LogTrace($"📄 Payload: {json}");

                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"✅ POST réussi: {response.StatusCode}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ POST échoué: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Exception lors de l'envoi vers {endpoint}");
                return false;
            }
        }
    }
}