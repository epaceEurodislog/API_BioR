// Fichier: DataAccess/INT48/SpeedWmsInventoryRepository.cs
// Repository pour accéder aux données d'inventaire dans SpeedWMS
// Table source: MVT_DAT

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models.INT48;

namespace DynamicsApiToDatabase.DataAccess.INT48
{
    public class SpeedWmsInventoryRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<SpeedWmsInventoryRepository> _logger;

        public SpeedWmsInventoryRepository(
            IConfiguration configuration, 
            ILogger<SpeedWmsInventoryRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("SpeedWmsConnection")
                ?? throw new ArgumentNullException("SpeedWmsConnection manquante");
            _logger = logger;
        }

        /// <summary>
        /// ÉTAPE 1: Récupère les mouvements d'inventaire en attente de traitement
        /// Requête complète avec toutes les transformations métier
        /// Utilise ART_PAR.ART_CODC pour le productId
        /// </summary>
        public async Task<List<SpeedWmsInventoryMovement>> GetPendingInventoryMovementsAsync()
        {
            const string query = @"
                SELECT 
                    MVT_DAT.MVT_KEYU,
                    ART_PAR.ART_CODC as ART_CODE,
                    CONCAT(CASE WHEN MVT_DAT.MVT_SENS = '-' THEN '- ' ELSE '' END, FLOOR(MVT_DAT.MVT_QTE)) as MVT_QTE,
                    MVT_DAT.STK_LOT1,
                    MVT_DAT.STK_LOT2,
                    MVT_DAT.TMV_CODE,
                    MVT_DAT.MVT_REF,
                    CASE 
                        WHEN MVT_DAT.TMV_CODE = '40110' AND ART_PAR.ART_TOP2 = 1 THEN 'SOR_ASILAGE'
                        WHEN MVT_DAT.TMV_CODE = '10020' AND MVT_DAT.MOT_CODE = 'SAV' THEN 'ENT_GDS_SAV'
                        WHEN MVT_DAT.TMV_CODE = '10020' AND MVT_DAT.MOT_CODE <> 'SAV' THEN 'ENT_GDS_AUTRES'
                        WHEN MVT_DAT.TMV_CODE = '10060' AND (MVT_DAT.MOT_CODE = 'INVG' OR MVT_DAT.MOT_CODE <> 'INVG') THEN 'ENT_ING_INVG'
                        WHEN MVT_DAT.TMV_CODE = '10065' AND (MVT_DAT.MOT_CODE = 'INVT' OR MVT_DAT.MOT_CODE <> 'INVT') THEN 'ENT_INT_INVT'
                        WHEN MVT_DAT.TMV_CODE = '20020' AND MVT_DAT.MOT_CODE = 'DDE CLIENT' THEN 'ENT_CST_DDE CLIENT'
                        WHEN MVT_DAT.TMV_CODE = '20020' AND MVT_DAT.MOT_CODE <> 'DDE CLIENT' THEN 'ENT_CST_AUTRES'
                        WHEN MVT_DAT.TMV_CODE = '30020' AND MVT_DAT.MOT_CODE = 'DDE CLIENT' THEN 'SOR_CST_DDE CLIENT'
                        WHEN MVT_DAT.TMV_CODE = '30020' AND MVT_DAT.MOT_CODE <> 'DDE CLIENT' THEN 'SOR_CST_AUTRES'
                        WHEN MVT_DAT.TMV_CODE = '20050' AND MVT_DAT.MOT_CODE = 'DDE CLIENT' THEN 'ENT_TRA_DDE CLIENT'
                        WHEN MVT_DAT.TMV_CODE = '20050' AND MVT_DAT.MOT_CODE = 'ERR RECEP' THEN 'ENT_TRA_ERR RECEP'
                        WHEN MVT_DAT.TMV_CODE = '20050' AND MVT_DAT.MOT_CODE NOT IN ('DDE CLIENT','ERR RECEP') THEN 'ENT_TRA_AUTRES'
                        WHEN MVT_DAT.TMV_CODE = '30050' AND MVT_DAT.MOT_CODE = 'DDE CLIENT' THEN 'SOR_TRA_DDE CLISOR'
                        WHEN MVT_DAT.TMV_CODE = '30050' AND MVT_DAT.MOT_CODE = 'ERR RECEP' THEN 'SOR_TRA_ERR RECEP'
                        WHEN MVT_DAT.TMV_CODE = '30050' AND MVT_DAT.MOT_CODE NOT IN ('DDE CLIENT','ERR RECEP') THEN 'SOR_TRA_AUTRES'
                        WHEN MVT_DAT.TMV_CODE = '40020' AND MVT_DAT.MOT_CODE = 'CENT' THEN 'SOR_GDS_CENT'
                        WHEN MVT_DAT.TMV_CODE = '40020' AND MVT_DAT.MOT_CODE = 'SAV' THEN 'ENT_GDS_SAV'
                        WHEN MVT_DAT.TMV_CODE = '40020' AND MVT_DAT.MOT_CODE NOT IN ('CENT','SAV') THEN 'SOR_GDS_AUTRES'
                        WHEN MVT_DAT.TMV_CODE = '40060' AND (MVT_DAT.MOT_CODE = 'INVG' OR MVT_DAT.MOT_CODE <> 'INVG') THEN 'SOR_ING_INVG'
                        WHEN MVT_DAT.TMV_CODE = '40065' AND (MVT_DAT.MOT_CODE = 'INVT' OR MVT_DAT.MOT_CODE <> 'INVT') THEN 'SOR_INT_INVT'
                        ELSE MVT_DAT.MOT_CODE 
                    END as MOT_CODE,
                    MVT_DAT.MVT_CRDA,
                    CASE 
                        WHEN COALESCE(MVT_DAT.QUA_CODE,'') = 'STD' THEN 'Disponible'
                        WHEN COALESCE(MVT_DAT.QUA_CODE,'') = 'BQLOG' THEN 'Bloque3PL'
                        WHEN COALESCE(MVT_DAT.QUA_CODE,'') = 'BQQA' THEN 'Disponible'
                        WHEN COALESCE(MVT_DAT.QUA_CODE,'') = 'BQQAK' THEN 'Bloque3PL'
                        ELSE 'Disponible'
                    END as QUA_CODE,
                    MVT_DAT.STK_DLUO,
                    CASE 
                        WHEN MVT_DAT.MVT_MOT LIKE '%application de la séquence n°%' THEN 'blocage auto stock règle péremption contrat'
                        ELSE MVT_DAT.MVT_MOT 
                    END as MVT_MOT
                FROM MVT_DAT
                LEFT OUTER JOIN ART_PAR ON MVT_DAT.ACT_CODE = ART_PAR.ACT_CODE AND MVT_DAT.ART_CODE = ART_PAR.ART_CODE
                WHERE 
                    MVT_DAT.ACT_CODE = 'COSMETIQUE' 
                    AND (
                        MVT_DAT.TMV_CODE IN ('10020','10060','10065','20020','30020','20050','30050','20060','30060','40020','40060','40065')
                        OR (MVT_DAT.TMV_CODE = '40110' AND ART_PAR.ART_TOP2 = 1)
                    )
                    AND COALESCE(MVT_DAT.MVT_TOP3, 0) <> 1
                ORDER BY MVT_DAT.MVT_CRDA DESC";

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("🔍 Récupération des mouvements d'inventaire depuis SpeedWMS");
                
                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                var movements = new List<SpeedWmsInventoryMovement>();
                
                while (await reader.ReadAsync())
                {
                    // Extraire la quantité (peut avoir un préfixe "- " pour les mouvements négatifs)
                    string qtyString = reader.GetString(2).Trim();
                    decimal quantity = decimal.Parse(qtyString.Replace("- ", "-"));

                    movements.Add(new SpeedWmsInventoryMovement
                    {
                        // MVT_KEYU peut être un entier en base, on convertit génériquement en string
                        MVT_KEYU = reader.GetValue(0)?.ToString(),             // MVT_KEYU
                        ART_CODE = reader.GetString(1),                        // ART_CODE (ART_PAR.ART_CODC)
                        MVT_QTE = quantity,                                     // MVT_QTE (avec signe)
                        STK_LOT1 = reader.IsDBNull(3) ? null : reader.GetString(3),  // STK_LOT1 (batchId)
                        STK_LOT2 = reader.IsDBNull(4) ? null : reader.GetString(4),  // STK_LOT2 (serialId)
                        TMV_CODE = reader.IsDBNull(5) ? null : reader.GetString(5),  // TMV_CODE
                        MVT_REF = reader.IsDBNull(6) ? null : reader.GetString(6),   // MVT_REF
                        MOT_CODE = reader.IsDBNull(7) ? null : reader.GetString(7),  // MOT_CODE (CASE transformé)
                        MVT_CRDA = reader.IsDBNull(8) ? null : reader.GetDateTime(8).ToString("yyyy-MM-dd"),  // MVT_CRDA
                        QUA_CODE = reader.IsDBNull(9) ? null : reader.GetString(9),  // QUA_CODE (StatusId)
                        STK_DLU = reader.IsDBNull(10) ? null : reader.GetDateTime(10), // STK_DLUO
                        MVT_MOT = reader.IsDBNull(11) ? null : reader.GetString(11)   // MVT_MOT (extendedDimension1)
                    });
                }
                
                _logger.LogInformation($"📦 {movements.Count} mouvements d'inventaire récupérés");
                return movements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des mouvements");
                throw;
            }
        }

        /// <summary>
        /// ÉTAPE 3: Marque les mouvements comme traités dans SpeedWMS
        /// Mise à jour de MVT_TOP3 = 1 et MVT_DATE3 = date du jour (format yyyyMMdd)
        /// </summary>
        public async Task MarkMovementsAsProcessedAsync(
            List<string> mvtKeyus, 
            string importId,
            bool success)
        {
            const string updateQuery = @"
                UPDATE MVT_DAT
                SET 
                    MVT_TOP3 = 1,
                    MVT_DATE3 = @DateTraitement
                FROM (
                    SELECT
                        SUBSTRING(JSON_API.JSON_DATA, CHARINDEX('id', JSON_API.JSON_DATA, 1) + 5,
                            CHARINDEX(',', JSON_API.JSON_DATA, CHARINDEX('id', JSON_API.JSON_DATA, 1)) - CHARINDEX('id', JSON_API.JSON_DATA, 1) - 6) AS MVT_KEYU,
                        SUBSTRING(JSON_API.JSON_DATA, CHARINDEX('productId', JSON_API.JSON_DATA, 1) + 12,
                            CHARINDEX(',', JSON_API.JSON_DATA, CHARINDEX('productId', JSON_API.JSON_DATA, 1)) - CHARINDEX('productId', JSON_API.JSON_DATA, 1) - 13) AS ART_CODE,
                        SUBSTRING(JSON_API.JSON_DATA, CHARINDEX('organizationId', JSON_API.JSON_DATA, 1) + 17,
                            CHARINDEX(',', JSON_API.JSON_DATA, CHARINDEX('organizationId', JSON_API.JSON_DATA, 1)) - CHARINDEX('organizationId', JSON_API.JSON_DATA, 1) - 18) AS ART_CCLI
                    FROM MiddleWare.dbo.JSON_OUT JSON_API
                    WHERE JSON_API.JSON_DEST = 'INT48_ADJUSTMENT'
                        AND JSON_API.JSON_CCLI = 'BR'
                        AND JSON_API.JSON_IMPORT_ID = @ImportId
                ) AS MVT_UPD
                WHERE MVT_DAT.MVT_KEYU = MVT_UPD.MVT_KEYU
                    AND MVT_DAT.ART_CODE = CONCAT('BR', MVT_UPD.ART_CODE)
                    AND MVT_DAT.ACT_CODE = 'COSMETIQUE'
                    AND COALESCE(MVT_DAT.MVT_TOP3, 0) <> 1
                    AND MVT_DAT.MVT_KEYU IN ({0})";

            try
            {
                if (mvtKeyus == null || mvtKeyus.Count == 0)
                {
                    _logger.LogWarning("⚠️ Aucun mouvement à marquer comme traité");
                    return;
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Construire la liste des clés pour la clause IN
                var keyList = string.Join(",", mvtKeyus.ConvertAll(k => $"'{k.Replace("'", "''")}'"));
                var finalQuery = string.Format(updateQuery, keyList);

                using var command = new SqlCommand(finalQuery, connection);
                command.Parameters.AddWithValue("@DateTraitement", DateTime.Now.ToString("yyyyMMdd"));
                command.Parameters.AddWithValue("@ImportId", importId);

                var affectedRows = await command.ExecuteNonQueryAsync();

                _logger.LogInformation($"✅ {affectedRows} mouvements marqués comme traités (MVT_TOP3=1) pour batch {importId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur mise à jour statut des mouvements");
                // Ne pas propager l'erreur - le traitement principal a réussi
            }
        }

        /// <summary>
        /// Obtient des statistiques sur les mouvements d'inventaire
        /// </summary>
        public async Task<Dictionary<string, int>> GetInventoryStatisticsAsync()
        {
            const string statsQuery = @"
                SELECT 
                    COUNT(*) as TotalMovements,
                    COUNT(CASE WHEN StatutEnvoi = 'ENVOYE' THEN 1 END) as Sent,
                    COUNT(CASE WHEN StatutEnvoi = 'ERREUR' THEN 1 END) as Errors,
                    COUNT(CASE WHEN StatutEnvoi IS NULL THEN 1 END) as Pending
                FROM MVT_DAT
                WHERE TMV_CODE = '@ivadjustement'";

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand(statsQuery, connection);
                using var reader = await command.ExecuteReaderAsync();

                var stats = new Dictionary<string, int>
                {
                    ["TotalMovements"] = 0,
                    ["Sent"] = 0,
                    ["Errors"] = 0,
                    ["Pending"] = 0
                };

                if (await reader.ReadAsync())
                {
                    stats["TotalMovements"] = reader.GetInt32(0);
                    stats["Sent"] = reader.GetInt32(1);
                    stats["Errors"] = reader.GetInt32(2);
                    stats["Pending"] = reader.GetInt32(3);
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération statistiques");
                return new Dictionary<string, int>
                {
                    ["TotalMovements"] = 0,
                    ["Sent"] = 0,
                    ["Errors"] = 0,
                    ["Pending"] = 0
                };
            }
        }
    }
}