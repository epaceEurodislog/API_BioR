// Fichier: Services/SpeedWmsDataService.cs
// Service pour lire les données BL depuis la base SpeedWMS_MSY_SF_RCT
// READ ONLY - Aucune modification des données SpeedWMS

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Service pour lire les données BL depuis SpeedWMS (READ ONLY)
    /// </summary>
    public class SpeedWmsDataService
    {
        private readonly string _speedWmsConnectionString;
        private readonly ILogger<SpeedWmsDataService> _logger;
        private readonly BLExportConfig _config;

        public SpeedWmsDataService(
            IConfiguration configuration,
            ILogger<SpeedWmsDataService> logger)
        {
            _speedWmsConnectionString = configuration.GetConnectionString("SpeedWmsConnection")
                ?? throw new ArgumentNullException("SpeedWmsConnection manquante dans la configuration");
            _logger = logger;

            // Configuration par défaut (peut être surchargée via appsettings.json)
            _config = new BLExportConfig
            {
                SpeedWmsConnectionString = _speedWmsConnectionString,
                DefaultInventLocationId = "RECNOLP",
                BatchSize = 50
            };
        }

        /// <summary>
        /// Récupère tous les BL disponibles dans SpeedWMS
        /// </summary>
        public async Task<List<SpeedWmsBLData>> GetAllAvailableBLsAsync()
        {
            var blList = new List<SpeedWmsBLData>();

            try
            {
                _logger.LogInformation("🔍 Début récupération des BL depuis SpeedWMS...");

                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                // Récupérer les en-têtes des BL
                var blHeaders = await GetBLHeadersAsync(connection);
                _logger.LogInformation($"📊 {blHeaders.Count} BL trouvés dans SpeedWMS");

                // Pour chaque BL, récupérer les lignes d'articles et supports
                foreach (var blHeader in blHeaders)
                {
                    try
                    {
                        // Récupérer les lignes d'articles
                        blHeader.Lines = await GetBLLinesAsync(connection, blHeader.OpeKeyu);

                        // Récupérer les données de support/emballage
                        blHeader.Supports = await GetBLSupportsAsync(connection, blHeader.OpeKeyu);

                        _logger.LogDebug($"📄 BL {blHeader.OpeKeyu}: {blHeader.Lines.Count} lignes, {blHeader.Supports.Count} supports");

                        blList.Add(blHeader);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Erreur lors de la récupération des détails pour BL {blHeader.OpeKeyu}");
                        // Continue avec les autres BL même si un échoue
                    }
                }

                _logger.LogInformation($"✅ {blList.Count} BL récupérés avec succès depuis SpeedWMS");
                return blList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des BL depuis SpeedWMS");
                return blList;
            }
        }

        /// <summary>
        /// Récupère les en-têtes des BL depuis la table OPE_DAT
        /// </summary>
        private async Task<List<SpeedWmsBLData>> GetBLHeadersAsync(SqlConnection connection)
        {
            var blHeaders = new List<SpeedWmsBLData>();

            try
            {
                const string sql = @"
                    SELECT 
                        OPE_KEYU,
                        ISNULL(OPE_REDO, '') as OPE_REDO,
                        ISNULL(OPE_ALPHA17, '') as OPE_ALPHA17,
                        ISNULL(OPE_CTRA, '') as OPE_CTRA,
                        ISNULL(OPE_ALPHA40, '') as OPE_ALPHA40,
                        ISNULL(OPE_ALPHA41, '') as OPE_ALPHA41,
                        OPE_MODA,
                        ISNULL(OPE_TOP22, '') as OPE_TOP22,
                        ISNULL(OPE_STAT, '') as OPE_STAT,
                        FNC008DATE,
                        ISNULL(OPE_DAT_DATEHEURRE, '') as OPE_DAT_DATEHEURRE
                    FROM OPE_DAT
                    WHERE OPE_KEYU IS NOT NULL
                    AND OPE_KEYU != ''
                    ORDER BY OPE_KEYU";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var blData = new SpeedWmsBLData
                    {
                        OpeKeyu = reader.GetString("OPE_KEYU"),
                        OpeRedo = reader.GetString("OPE_REDO"),
                        OpeAlpha17 = reader.GetString("OPE_ALPHA17"),
                        OpeCtra = reader.GetString("OPE_CTRA"),
                        OpeAlpha40 = reader.GetString("OPE_ALPHA40"),
                        OpeAlpha41 = reader.GetString("OPE_ALPHA41"),
                        OpeModa = reader.IsDBNull("OPE_MODA") ? null : reader.GetDateTime("OPE_MODA"),
                        OpeTop22 = reader.GetString("OPE_TOP22"),
                        OpeStat = reader.GetString("OPE_STAT"),
                        Fnc008Date = reader.IsDBNull("FNC008DATE") ? null : reader.GetDateTime("FNC008DATE"),
                        DataHeurreIc = reader.GetString("OPE_DAT_DATEHEURRE")
                    };

                    blHeaders.Add(blData);
                }

                _logger.LogDebug($"📋 {blHeaders.Count} en-têtes BL récupérés depuis OPE_DAT");
                return blHeaders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des en-têtes BL");
                throw;
            }
        }

        /// <summary>
        /// Récupère les lignes d'articles pour un BL donné
        /// </summary>
        private async Task<List<SpeedWmsBLLine>> GetBLLinesAsync(SqlConnection connection, string opeKeyu)
        {
            var lines = new List<SpeedWmsBLLine>();

            try
            {
                const string sql = @"
                    SELECT 
                        MIL.OPE_KEYU,
                        ISNULL(MIL.MIL_DAT_ART_CODE, '') as MIL_DAT_ART_CODE,
                        ISNULL(MIL.MIL_DAT_MIL_QTTP, 0) as MIL_DAT_MIL_QTTP,
                        ISNULL(MIL.MIL_DAT_MIL_QTTA, 0) as MIL_DAT_MIL_QTTA,
                        ISNULL(MIL.MIL_DAT_MIL_QTMA, 0) as MIL_DAT_MIL_QTMA,
                        ISNULL(MIL.MIL_DAT_MIL_LOT1P, '') as MIL_DAT_MIL_LOT1P,
                        ISNULL(MIL.MIL_DAT_MIL_LOT2P, '') as MIL_DAT_MIL_LOT2P,
                        ISNULL(MIL.MIL_DAT_MIL_SUPP, '') as MIL_DAT_MIL_SUPP,
                        -- RG5: Max des MIE_MODA rattaché à l'OPE_KEYU en statut 040
                        (SELECT MAX(MIE_MODA) 
                         FROM MIE_DAT 
                         WHERE MIE_DAT.OPE_KEYU = MIL.OPE_KEYU 
                         AND ISNULL(MIE_STAT, '') = '040') as MAX_MIE_MODA,
                        -- DLUO minimum calculée
                        (SELECT MIN(OPL_DLOM) 
                         FROM OPL_DAT 
                         WHERE OPL_DAT.OPE_KEYU = MIL.OPE_KEYU) as MIN_DLUO
                    FROM MIL_DAT MIL
                    WHERE MIL.OPE_KEYU = @OpeKeyu
                    ORDER BY MIL.MIL_DAT_ART_CODE";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@OpeKeyu", opeKeyu);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var line = new SpeedWmsBLLine
                    {
                        OpeKeyu = reader.GetString("OPE_KEYU"),
                        ArtCode = reader.GetString("MIL_DAT_ART_CODE"),
                        QttePreparee = reader.GetDecimal("MIL_DAT_MIL_QTTP"),
                        QttePrevue = reader.GetDecimal("MIL_DAT_MIL_QTTA"),
                        QtteManquante = reader.GetDecimal("MIL_DAT_MIL_QTMA"),
                        Lot1 = reader.GetString("MIL_DAT_MIL_LOT1P"),
                        Lot2 = reader.GetString("MIL_DAT_MIL_LOT2P"),
                        Support = reader.GetString("MIL_DAT_MIL_SUPP"),
                        MaxMieModa = reader.IsDBNull("MAX_MIE_MODA") ? null : reader.GetDateTime("MAX_MIE_MODA"),
                        DluoMin = reader.IsDBNull("MIN_DLUO") ? null : reader.GetDateTime("MIN_DLUO")
                    };

                    lines.Add(line);
                }

                _logger.LogDebug($"📄 {lines.Count} lignes récupérées pour BL {opeKeyu}");
                return lines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la récupération des lignes pour BL {opeKeyu}");
                return lines;
            }
        }

        /// <summary>
        /// Récupère les données de support/emballage pour un BL donné
        /// </summary>
        private async Task<List<SpeedWmsSupportData>> GetBLSupportsAsync(SqlConnection connection, string opeKeyu)
        {
            var supports = new List<SpeedWmsSupportData>();

            try
            {
                const string sql = @"
                    SELECT 
                        SEX.SEX_SUPP as SupportId,
                        ISNULL(SEX.SEX_POISR, 0) as SEX_POISR,
                        ISNULL(SEX.SEX_PROF, 0) as SEX_PROF,
                        ISNULL(SEX.SEX_LARG, 0) as SEX_LARG,
                        ISNULL(SEX.SEX_HAUT, 0) as SEX_HAUT,
                        ISNULL(SEX.SEX_SUPR, '') as SEX_SUPR,
                        -- Dimensions emballage regroupement
                        ISNULL(EMB.EMB_PROF, 0) as EMB_PROF,
                        ISNULL(EMB.EMB_LARG, 0) as EMB_LARG,
                        ISNULL(EMB.EMB_HAUT, 0) as EMB_HAUT,
                        -- RG4: Somme des SEX_POISR rattaché au SEX_SUPR + Poids emballage
                        (SELECT SUM(ISNULL(S2.SEX_POISR, 0)) 
                         FROM SEX_DAT S2 
                         WHERE S2.SEX_SUPR = SEX.SEX_SUPR
                         AND S2.OPE_KEYU = SEX.OPE_KEYU) + ISNULL(EMB.EMB_POISR, 0) as POIDS_REGROUPEMENT,
                        -- Type emballage pour RG2 et RG3 (à définir selon la logique métier)
                        ISNULL(EMB.EMB_TYPE, '') as EMB_TYPE
                    FROM SEX_DAT SEX
                    LEFT JOIN EMB_DAT EMB ON SEX.SEX_SUPR = EMB.EMB_CODE
                    WHERE SEX.OPE_KEYU = @OpeKeyu
                    ORDER BY SEX.SEX_SUPP";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@OpeKeyu", opeKeyu);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var support = new SpeedWmsSupportData
                    {
                        SupportId = reader.GetString("SupportId"),
                        Poids = reader.GetDecimal("SEX_POISR"),
                        Longueur = reader.GetDecimal("SEX_PROF"),
                        Largeur = reader.GetDecimal("SEX_LARG"),
                        Hauteur = reader.GetDecimal("SEX_HAUT"),
                        SupportRegroupement = reader.GetString("SEX_SUPR"),
                        LongueurRegroupement = reader.GetDecimal("EMB_PROF"),
                        LargeurRegroupement = reader.GetDecimal("EMB_LARG"),
                        HauteurRegroupement = reader.GetDecimal("EMB_HAUT"),
                        PoidsRegroupement = reader.GetDecimal("POIDS_REGROUPEMENT"),

                        // RG2 et RG3: Déterminer type selon emballage
                        SupportType = DeterminePackageType(reader.GetString("EMB_TYPE"), false),
                        TypeRegroupement = DeterminePackageType(reader.GetString("EMB_TYPE"), true)
                    };

                    supports.Add(support);
                }

                _logger.LogDebug($"📦 {supports.Count} supports récupérés pour BL {opeKeyu}");
                return supports;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la récupération des supports pour BL {opeKeyu}");
                return supports;
            }
        }

        /// <summary>
        /// Détermine le type d'emballage selon les règles RG2 et RG3
        /// </summary>
        private string DeterminePackageType(string embType, bool isGrouping)
        {
            // RG2 et RG3: Palette ou Colis fonction du type emballage
            // Cette logique devra être adaptée selon les codes d'emballage réels
            if (string.IsNullOrEmpty(embType))
                return "Colis"; // Valeur par défaut

            // Exemples de logique (à adapter selon les codes réels)
            embType = embType.ToUpper();

            if (embType.Contains("PAL") || embType.Contains("PALETTE"))
                return "Palette";

            if (embType.Contains("COL") || embType.Contains("COLIS") || embType.Contains("CARTON"))
                return "Colis";

            // Valeur par défaut
            return isGrouping ? "Palette" : "Colis";
        }

        /// <summary>
        /// Transforme les données brutes SpeedWMS en données d'export structurées
        /// </summary>
        public async Task<List<BLExportData>> TransformToBLExportDataAsync(List<SpeedWmsBLData> speedWmsData)
        {
            var exportDataList = new List<BLExportData>();

            try
            {
                _logger.LogInformation($"🔄 Transformation de {speedWmsData.Count} BL SpeedWMS...");

                foreach (var speedBL in speedWmsData)
                {
                    try
                    {
                        var exportBL = await TransformSingleBLAsync(speedBL);
                        exportDataList.Add(exportBL);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Erreur transformation BL {speedBL.OpeKeyu}");

                        // Créer un BL en erreur pour traçabilité
                        var errorBL = new BLExportData
                        {
                            BLNumber = speedBL.OpeKeyu,
                            HasErrors = true,
                            ErrorMessages = new List<string> { ex.Message }
                        };
                        exportDataList.Add(errorBL);
                    }
                }

                _logger.LogInformation($"✅ {exportDataList.Count} BL transformés ({exportDataList.Count(b => !b.HasErrors)} succès, {exportDataList.Count(b => b.HasErrors)} erreurs)");
                return exportDataList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la transformation des BL");
                return exportDataList;
            }
        }

        /// <summary>
        /// Transforme un BL SpeedWMS en BL d'export
        /// </summary>
        private async Task<BLExportData> TransformSingleBLAsync(SpeedWmsBLData speedBL)
        {
            var exportBL = new BLExportData
            {
                BLNumber = speedBL.OpeKeyu,
                ImportId = GenerateImportId(speedBL.OpeKeyu),
                TransRefId = speedBL.OpeRedo,
                PickingRouteId = speedBL.OpeAlpha17,
                CarrierCode = speedBL.OpeCtra,
                CarrierServiceCode = BuildCarrierServiceCode(speedBL.OpeAlpha40, speedBL.OpeAlpha41),
                InventLocationId = _config.DefaultInventLocationId,
                DocStatus = speedBL.OpeTop22,
                DocStatusDate = ParseDateHeure(speedBL.DataHeurreIc),
                CreatedDate = DateTime.Now
            };

            // RG6: Date expédition seulement si OPE_STAT=070
            if (speedBL.OpeStat == "070" && speedBL.OpeModa.HasValue)
            {
                exportBL.ShippingDate = speedBL.OpeModa;
            }

            // Transformer les lignes d'articles avec regroupement
            exportBL.Lines = await TransformBLLinesAsync(speedBL.Lines);

            // RG5: Date fin préparation = Max des MIE_MODA des lignes
            var maxEndDatePrep = speedBL.Lines
                .Where(l => l.MaxMieModa.HasValue)
                .Select(l => l.MaxMieModa.Value)
                .DefaultIfEmpty()
                .Max();

            if (maxEndDatePrep != default(DateTime))
            {
                exportBL.EndDatePrep = maxEndDatePrep;
            }

            // Transformer les supports
            exportBL.Supports = TransformBLSupports(speedBL.Supports);

            _logger.LogDebug($"✅ BL {speedBL.OpeKeyu} transformé: {exportBL.Lines.Count} lignes, {exportBL.Supports.Count} supports");

            return exportBL;
        }

        /// <summary>
        /// Transforme et regroupe les lignes d'articles
        /// </summary>
        private async Task<List<BLExportLine>> TransformBLLinesAsync(List<SpeedWmsBLLine> speedLines)
        {
            var exportLines = new List<BLExportLine>();

            // Regroupement par article (ART_CODE)
            var groupedLines = speedLines.GroupBy(l => l.ArtCode);

            foreach (var group in groupedLines)
            {
                var exportLine = new BLExportLine
                {
                    ItemId = group.Key,
                    TotalQuantity = group.Sum(l => l.QttePreparee),
                    PlannedQuantity = group.Sum(l => l.QttePrevue),
                    MissingQuantity = group.Sum(l => l.QtteManquante),
                    BatchIds = group.Where(l => !string.IsNullOrEmpty(l.Lot1))
                                   .Select(l => l.Lot1)
                                   .Distinct()
                                   .ToList(),
                    SerialIds = group.Where(l => !string.IsNullOrEmpty(l.Lot2))
                                    .Select(l => l.Lot2)
                                    .Distinct()
                                    .ToList(),
                    SupportIds = group.Where(l => !string.IsNullOrEmpty(l.Support))
                                     .Select(l => l.Support)
                                     .Distinct()
                                     .ToList(),
                    MinDluo = group.Where(l => l.DluoMin.HasValue)
                                  .Select(l => l.DluoMin.Value)
                                  .DefaultIfEmpty()
                                  .Min()
                };

                // Si pas de DLUO valide, mettre null
                if (exportLine.MinDluo == default(DateTime))
                {
                    exportLine.MinDluo = null;
                }

                exportLines.Add(exportLine);
            }

            return exportLines;
        }

        /// <summary>
        /// Transforme les données de supports
        /// </summary>
        private List<BLSupportInfo> TransformBLSupports(List<SpeedWmsSupportData> speedSupports)
        {
            var supportInfos = new List<BLSupportInfo>();

            foreach (var speedSupport in speedSupports)
            {
                var supportInfo = new BLSupportInfo
                {
                    SupportId = speedSupport.SupportId,
                    SupportType = speedSupport.SupportType,
                    Weight = speedSupport.Poids,
                    Length = speedSupport.Longueur,
                    Width = speedSupport.Largeur,
                    Height = speedSupport.Hauteur,
                    GroupingSupportId = speedSupport.SupportRegroupement,
                    GroupingType = speedSupport.TypeRegroupement,
                    GroupingWeight = speedSupport.PoidsRegroupement,
                    GroupingLength = speedSupport.LongueurRegroupement,
                    GroupingWidth = speedSupport.LargeurRegroupement,
                    GroupingHeight = speedSupport.HauteurRegroupement
                };

                supportInfos.Add(supportInfo);
            }

            return supportInfos;
        }

        /// <summary>
        /// Génère l'ImportId au format {BL}_{timestamp}
        /// </summary>
        private string GenerateImportId(string blNumber)
        {
            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            return $"{blNumber}_{timestamp}";
        }

        /// <summary>
        /// Construit le code service transporteur selon RG1
        /// </summary>
        private string BuildCarrierServiceCode(string alpha40, string alpha41)
        {
            // RG1: @ = séparateur de champ (à confirmer ce que souhaite BR)
            if (string.IsNullOrEmpty(alpha40) && string.IsNullOrEmpty(alpha41))
                return "";

            if (string.IsNullOrEmpty(alpha41))
                return alpha40;

            if (string.IsNullOrEmpty(alpha40))
                return alpha41;

            return $"{alpha40}@{alpha41}";
        }

        /// <summary>
        /// Parse une date/heure au format string (à adapter selon le format réel)
        /// </summary>
        private DateTime? ParseDateHeure(string dateHeureStr)
        {
            if (string.IsNullOrEmpty(dateHeureStr))
                return null;

            // Essayer plusieurs formats de date
            var formats = new[]
            {
                "yyyy-MM-dd HH:mm:ss",
                "dd/MM/yyyy HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "dd/MM/yyyy HH:mm",
                "yyyy-MM-dd HH:mm"
            };

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(dateHeureStr, format, null, System.Globalization.DateTimeStyles.None, out var result))
                {
                    return result;
                }
            }

            // Si aucun format ne fonctionne, essayer un parsing générique
            if (DateTime.TryParse(dateHeureStr, out var genericResult))
            {
                return genericResult;
            }

            _logger.LogWarning($"⚠️ Impossible de parser la date: '{dateHeureStr}'");
            return null;
        }

        /// <summary>
        /// Test de connectivité à SpeedWMS
        /// </summary>
        public async Task<bool> TestSpeedWmsConnectionAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Test de connectivité SpeedWMS...");

                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                // Test simple avec comptage des BL
                const string testSql = "SELECT COUNT(*) FROM OPE_DAT";
                using var command = new SqlCommand(testSql, connection);
                var count = (int)await command.ExecuteScalarAsync();

                _logger.LogInformation($"✅ Connexion SpeedWMS OK - {count} enregistrements dans OPE_DAT");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur de connexion SpeedWMS");
                return false;
            }
        }

        /// <summary>
        /// Obtient des statistiques sur les données SpeedWMS
        /// </summary>
        public async Task<SpeedWmsStatistics> GetSpeedWmsStatisticsAsync()
        {
            var stats = new SpeedWmsStatistics();

            try
            {
                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                // Compter les BL totaux
                const string countSql = @"
                    SELECT 
                        COUNT(*) as TotalBL,
                        COUNT(CASE WHEN OPE_STAT = '070' THEN 1 END) as BLExpedies,
                        COUNT(CASE WHEN OPE_MODA IS NOT NULL THEN 1 END) as BLAvecDateExpedition
                    FROM OPE_DAT 
                    WHERE OPE_KEYU IS NOT NULL AND OPE_KEYU != ''";

                using var command = new SqlCommand(countSql, connection);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    stats.TotalBLs = reader.GetInt32("TotalBL");
                    stats.BLsShipped = reader.GetInt32("BLExpedies");
                    stats.BLsWithShippingDate = reader.GetInt32("BLAvecDateExpedition");
                }

                _logger.LogInformation($"📊 Statistiques SpeedWMS: {stats.TotalBLs} BL total, {stats.BLsShipped} expédiés");
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur récupération statistiques SpeedWMS");
                return stats;
            }
        }
    }

    /// <summary>
    /// Statistiques des données SpeedWMS
    /// </summary>
    public class SpeedWmsStatistics
    {
        public int TotalBLs { get; set; }
        public int BLsShipped { get; set; }
        public int BLsWithShippingDate { get; set; }
        public int TotalLines { get; set; }
        public int TotalSupports { get; set; }

        public string GetSummary()
        {
            return $"SpeedWMS: {TotalBLs} BL total, {BLsShipped} expédiés, {BLsWithShippingDate} avec date expédition";
        }
    }
}