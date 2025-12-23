using Dapper;
using DynamicsApiToDatabase.Models.INT39;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services
{
    public class TrackingNumberService
    {
        private readonly string _speedConnectionString;
        private readonly ILogger<TrackingNumberService> _logger;

        public TrackingNumberService(
            IConfiguration configuration,
            ILogger<TrackingNumberService> logger)
        {
            // Utiliser la même connexion que SpeedWmsDataService
            _speedConnectionString = configuration.GetConnectionString("SpeedWMS") 
                ?? configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        /// <summary>
        /// Récupère les tracking numbers depuis SPEED (requête SQL à fournir par collègue)
        /// </summary>
        public async Task<List<TrackingNumberModel>> GetTrackingNumbersAsync(
            DateTime? modifiedSince = null,
            int? limit = null)
        {
            _logger.LogInformation("📦 Récupération des tracking numbers depuis SPEED...");

            try
            {
                using var connection = new SqlConnection(_speedConnectionString);
                await connection.OpenAsync();

                // TODO: Remplacer par la requête SQL fournie par le collègue
                var query = @"
                    -- REQUÊTE SQL À FOURNIR
                    -- Doit récupérer les données de OPE_DAT et SEX_DAT
                    -- Filtres : ACT_CODE='COSMETIQUE', OPE_CCLI='BR'
                    -- Doit identifier le type (Sales Order ou Transfer Order)
                    SELECT TOP (@Limit)
                        OPE_DAT.ACT_CODE,
                        OPE_DAT.OPE_CCLI,
                        OPE_DAT.OPE_REDO,
                        OPE_DAT.OPE_KEYU,
                        OPE_DAT.OPE_STAT,
                        OPE_DAT.OPE_MODA,
                        OPE_DAT.OPE_MOHE,
                        OPE_DAT.OPE_CTRA,
                        OPE_DAT.OPE_TOP28,
                        OPE_DAT.OPE_TOP22,
                        OPE_DAT.OPE_DATEHEURE11,
                        SEX_DAT.SEX_SUPR,
                        SEX_DAT.SEX_URLT,
                        'SALES' AS OrderType -- ou 'TRANSFER' selon la logique
                    FROM OPE_DAT
                    LEFT JOIN SEX_DAT ON OPE_DAT.XXX = SEX_DAT.YYY
                    WHERE OPE_DAT.ACT_CODE = 'COSMETIQUE'
                      AND OPE_DAT.OPE_CCLI = 'BR'
                      AND (@ModifiedSince IS NULL OR OPE_DAT.OPE_MODA >= @ModifiedSince)
                    ORDER BY OPE_DAT.OPE_MODA DESC";

                var parameters = new
                {
                    ModifiedSince = modifiedSince,
                    Limit = limit ?? 1000
                };

                var results = await connection.QueryAsync<TrackingNumberModel>(query, parameters);
                var trackingNumbers = results.ToList();

                _logger.LogInformation($"✅ {trackingNumbers.Count} tracking numbers récupérés");
                return trackingNumbers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des tracking numbers");
                throw;
            }
        }

        /// <summary>
        /// Récupère les tracking numbers pour un numéro de commande spécifique
        /// </summary>
        public async Task<TrackingNumberModel> GetTrackingNumberByOrderIdAsync(string orderId)
        {
            _logger.LogInformation($"🔍 Recherche tracking pour commande : {orderId}");

            try
            {
                using var connection = new SqlConnection(_speedConnectionString);
                await connection.OpenAsync();

                var query = @"
                    -- Requête pour une commande spécifique
                    SELECT TOP 1
                        OPE_DAT.*,
                        SEX_DAT.*
                    FROM OPE_DAT
                    LEFT JOIN SEX_DAT ON OPE_DAT.XXX = SEX_DAT.YYY
                    WHERE OPE_DAT.OPE_REDO = @OrderId
                      AND OPE_DAT.ACT_CODE = 'COSMETIQUE'
                      AND OPE_DAT.OPE_CCLI = 'BR'";

                var result = await connection.QueryFirstOrDefaultAsync<TrackingNumberModel>(
                    query,
                    new { OrderId = orderId });

                if (result != null)
                {
                    _logger.LogInformation($"✅ Tracking trouvé pour {orderId}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ Aucun tracking trouvé pour {orderId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la recherche du tracking {orderId}");
                throw;
            }
        }
    }
}