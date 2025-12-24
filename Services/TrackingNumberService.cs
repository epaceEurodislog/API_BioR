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

            _speedConnectionString = configuration.GetConnectionString("SpeedWmsConnection") 
                ?? configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        /// <summary>
        /// Récupère les tracking numbers pour les COMMANDES CLIENT (Sales Orders)
        /// </summary>
        public async Task<List<TrackingNumberModel>> GetSalesOrdersTrackingNumbersAsync(int? limit = null)
        {
            _logger.LogInformation("📦 Récupération des tracking numbers COMMANDES CLIENT depuis SPEED...");

            try
            {
                using var connection = new SqlConnection(_speedConnectionString);
                await connection.OpenAsync();

                // Requête SQL fournie par le collègue - COMMANDES CLIENT
                var query = @"
                    SELECT " + (limit.HasValue ? $"TOP {limit.Value}" : "") + @"
                        OPE_DAT.ACT_CODE,
                        OPE_DAT.OPE_CCLI,
                        OPE_DAT.OPE_REDO,
                        OPE_DAT.OPE_RTIE,
                        CASE 
                            WHEN OPE_DAT.OPE_STAT = '010' THEN 'EN SAISIE'
                            WHEN OPE_DAT.OPE_STAT = '020' THEN 'EN VAGUE'
                            WHEN OPE_DAT.OPE_STAT = '030' THEN 'EN PREPA'
                            WHEN OPE_DAT.OPE_STAT = '040' THEN 'VALIDEE'
                            WHEN OPE_DAT.OPE_STAT = '050' THEN 'ANNULEE'
                            WHEN OPE_DAT.OPE_STAT = '060' THEN 'EN EXPEDITION'
                            WHEN OPE_DAT.OPE_STAT = '070' THEN 'EXPEDIEE'
                        END AS OPE_STAT,
                        OPE_DAT.OPE_MODA,
                        OPE_DAT.OPE_MOHE,
                        OPE_DAT.OPE_CTRA,
                        CASE WHEN OPE_DAT.OPE_TOP28 = '0' THEN 'Non' ELSE 'Oui' END AS OPE_TOP28,
                        CASE WHEN OPE_DAT.OPE_TOP22 = '0' THEN 'Non' ELSE 'Oui' END AS OPE_TOP22,
                        CONCAT(OPE_DAT.OPE_DATE1, ' ', OPE_DAT.OPE_HEURE1) AS OPE_DATETIME,
                        CASE WHEN COALESCE(SEX_DAT.SEX_SUPR, '') = '' THEN COALESCE(SEX_DAT.SEX_SUPE, '') ELSE COALESCE(SEX_DAT.SEX_SUPR, '') END AS SEX_TRACKING,
                        COALESCE(SEX_DAT.SEX_URLT, '') AS SEX_URLT,
                        OPE_DAT.OPE_KEYU
                    FROM ope_dat
                    LEFT OUTER JOIN sex_dat ON ope_dat.act_code = sex_dat.sex_act AND ope_dat.ope_nooe = sex_dat.sex_nooe
                    WHERE ope_dat.act_code = 'COSMETIQUE'
                    AND ope_dat.ope_crqi = 'Interface'
                    AND coalesce(OPE_DAT.OPE_TOP15,0) <> 1
                    AND ope_dat.ope_ccli = 'BR'";

                var results = await connection.QueryAsync<TrackingNumberModel>(query);
                var trackingNumbers = results.ToList();

                // Marquer comme Sales Orders
                trackingNumbers.ForEach(t => t.OrderType = "SALES");

                _logger.LogInformation($"✅ {trackingNumbers.Count} tracking numbers SALES ORDERS récupérés");
                return trackingNumbers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des tracking numbers SALES ORDERS");
                throw;
            }
        }

        /// <summary>
        /// Récupère les tracking numbers pour les ORDRES DE TRANSFERT (Transfer Orders)
        /// </summary>
        public async Task<List<TrackingNumberModel>> GetTransferOrdersTrackingNumbersAsync(int? limit = null)
        {
            _logger.LogInformation("📦 Récupération des tracking numbers ORDRES DE TRANSFERT depuis SPEED...");

            try
            {
                using var connection = new SqlConnection(_speedConnectionString);
                await connection.OpenAsync();

                // Requête SQL fournie par le collègue - ORDRES DE TRANSFERT
                var query = @"
                    SELECT " + (limit.HasValue ? $"TOP {limit.Value}" : "") + @"
                        OPE_DAT.ACT_CODE,
                        OPE_DAT.OPE_CCLI,
                        OPE_DAT.OPE_REDO,
                        OPE_DAT.OPE_RTIE,
                        CASE 
                            WHEN OPE_DAT.OPE_STAT = '010' THEN 'EN SAISIE'
                            WHEN OPE_DAT.OPE_STAT = '020' THEN 'EN VAGUE'
                            WHEN OPE_DAT.OPE_STAT = '030' THEN 'EN PREPA'
                            WHEN OPE_DAT.OPE_STAT = '040' THEN 'VALIDEE'
                            WHEN OPE_DAT.OPE_STAT = '050' THEN 'ANNULEE'
                            WHEN OPE_DAT.OPE_STAT = '060' THEN 'EN EXPEDITION'
                            WHEN OPE_DAT.OPE_STAT = '070' THEN 'EXPEDIEE'
                        END AS OPE_STAT,
                        OPE_DAT.OPE_MODA,
                        OPE_DAT.OPE_MOHE,
                        OPE_DAT.OPE_CTRA,
                        CASE WHEN OPE_DAT.OPE_TOP28 = '0' THEN 'Non' ELSE 'Oui' END AS OPE_TOP28,
                        CASE WHEN OPE_DAT.OPE_TOP22 = '0' THEN 'Non' ELSE 'Oui' END AS OPE_TOP22,
                        CONCAT(OPE_DAT.OPE_DATE1, ' ', OPE_DAT.OPE_HEURE1) AS OPE_DATETIME,
                        CASE WHEN COALESCE(SEX_DAT.SEX_SUPR, '') = '' THEN SEX_DAT.SEX_SUPE ELSE SEX_DAT.SEX_SUPR END AS SEX_TRACKING,
                        COALESCE(SEX_DAT.SEX_URLT, '') AS SEX_URLT,
                        OPE_DAT.OPE_KEYU
                    FROM ope_dat
                    LEFT OUTER JOIN sex_dat ON ope_dat.act_code = sex_dat.sex_act AND ope_dat.ope_nooe = sex_dat.sex_nooe
                    WHERE ope_dat.act_code = 'COSMETIQUE'
                    AND ope_dat.ope_crqi = 'Interface'
                    AND coalesce(OPE_DAT.OPE_TOP15,0) <> 1
                    AND ope_dat.ope_ccli = 'BR'";

                var results = await connection.QueryAsync<TrackingNumberModel>(query);
                var trackingNumbers = results.ToList();

                // Marquer comme Transfer Orders
                trackingNumbers.ForEach(t => t.OrderType = "TRANSFER");

                _logger.LogInformation($"✅ {trackingNumbers.Count} tracking numbers TRANSFER ORDERS récupérés");
                return trackingNumbers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des tracking numbers TRANSFER ORDERS");
                throw;
            }
        }

        /// <summary>
        /// Récupère TOUS les tracking numbers (Sales + Transfer)
        /// </summary>
        public async Task<List<TrackingNumberModel>> GetAllTrackingNumbersAsync(int? limit = null)
        {
            _logger.LogInformation("📦 Récupération de TOUS les tracking numbers depuis SPEED...");

            var salesOrders = await GetSalesOrdersTrackingNumbersAsync(limit);
            var transferOrders = await GetTransferOrdersTrackingNumbersAsync(limit);

            var allTracking = new List<TrackingNumberModel>();
            allTracking.AddRange(salesOrders);
            allTracking.AddRange(transferOrders);

            _logger.LogInformation($"✅ Total : {allTracking.Count} tracking numbers récupérés " +
                $"(Sales: {salesOrders.Count}, Transfer: {transferOrders.Count})");

            return allTracking;
        }

        /// <summary>
        /// Récupère un tracking number pour un numéro de commande spécifique
        /// </summary>
        public async Task<TrackingNumberModel> GetTrackingNumberByOrderIdAsync(string orderId)
        {
            _logger.LogInformation($"🔍 Recherche tracking pour commande : {orderId}");

            var allTracking = await GetAllTrackingNumbersAsync();
            var result = allTracking.FirstOrDefault(t => t.OPE_REDO == orderId);

            if (result != null)
            {
                _logger.LogInformation($"✅ Tracking trouvé pour {orderId} (Type: {result.OrderType})");
            }
            else
            {
                _logger.LogWarning($"⚠️ Aucun tracking trouvé pour {orderId}");
            }

            return result;
        }

        /// <summary>
        /// Met à jour le flag OPE_TOP15 dans Speed pour les commandes INT39_TRACKING traitées
        /// Respecte le processus d'update défini dans le document de refonte
        /// </summary>
        public async Task<int> UpdateOpeTop15ForInt39TrackingAsync()
        {
            _logger.LogInformation("🔄 Mise à jour OPE_TOP15 pour les commandes INT39_TRACKING...");

            try
            {
                using var connection = new SqlConnection(_speedConnectionString);
                await connection.OpenAsync();

                const string updateQuery = @"
                    UPDATE OPE_DAT SET OPE_DAT.OPE_TOP15 = OPE_UPD.NEW_OPE_TOP15
                    FROM (
                        SELECT 
                            OPE_DAT.act_code AS 'ACT_CODE', 
                            ope_dat.ope_keyu AS 'OPE_KEYU', 
                            ope_dat.OPE_REDO AS 'OPE_REDO', 
                            COALESCE(OPE_DAT.OPE_TOP15, 0) AS 'OLD_OPE_TOP15', 
                            1 AS 'NEW_OPE_TOP15'
                        FROM OPE_DAT 
                        WHERE OPE_DAT.ACT_CODE = 'COSMETIQUE' 
                            AND OPE_DAT.OPE_CCLI = 'BR' 
                            AND OPE_DAT.ope_stat = '070'
                            AND OPE_DAT.OPE_NoOE IN (
                                SELECT ope_nooe 
                                FROM MVT_DAT 
                                WHERE act_code = 'COSMETIQUE' 
                                    AND TMV_CODE = '40110'
                            )
                            AND OPE_DAT.OPE_REDO IN (
                                SELECT SUBSTRING(
                                    JSON_API.JSON_DATA, 
                                    CHARINDEX('BROrderId', JSON_API.JSON_DATA, 1) + 12, 
                                    CHARINDEX(',', JSON_API.JSON_DATA, CHARINDEX('BROrderId', JSON_API.JSON_DATA, 1)) - CHARINDEX('BROrderId', JSON_API.JSON_DATA, 1) - 13
                                ) AS OPE_REDO
                                FROM MiddleWare.dbo.JSON_OUT JSON_API
                                WHERE JSON_API.JSON_DEST = 'INT39_TRACKING'
                                    AND JSON_API.JSON_CCLI = 'BR'
                            )
                            AND COALESCE(OPE_DAT.OPE_TOP15, 0) <> 1
                    ) AS OPE_UPD
                    WHERE ope_dat.act_code = ope_upd.act_code 
                        AND ope_dat.ope_keyu = ope_upd.ope_keyu 
                        AND ope_dat.ope_redo = ope_upd.ope_redo";

                using var command = new SqlCommand(updateQuery, connection);
                var rowsAffected = await command.ExecuteNonQueryAsync();

                _logger.LogInformation($"✅ {rowsAffected} ligne(s) mise(s) à jour - OPE_TOP15 = 1 pour INT39_TRACKING");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la mise à jour OPE_TOP15 pour INT39_TRACKING");
                throw;
            }
        }
    }
}