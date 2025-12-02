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

                // 🚨 LIMITATION TEMPORAIRE POUR TESTS - Décommentez la ligne suivante
                // blHeaders = blHeaders.Take(100).ToList();
                //_logger.LogInformation($"⚠️ LIMITATION ACTIVÉE : Traitement des {blHeaders.Count} premiers BL seulement");

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
        /// Récupère les en-têtes des BL depuis la table OPE_DAT - VERSION FINALE CORRIGÉE
        /// </summary>
        private async Task<List<SpeedWmsBLData>> GetBLHeadersAsync(SqlConnection connection)
        {
            var blHeaders = new List<SpeedWmsBLData>();

            try
            {
                const string sql = @"
            SELECT 
                OPE_KEYU,                                    -- int NOT NULL
                ISNULL(OPE_REDO, '') as OPE_REDO,           -- varchar(50)
                ISNULL(OPE_ALPHA17, '') as OPE_ALPHA17,     -- varchar(255)
                ISNULL(OPE_CTRA, '') as OPE_CTRA,           -- varchar(35)
                ISNULL(OPE_ALPHA40, '') as OPE_ALPHA40,     -- varchar(50)
                ISNULL(OPE_ALPHA41, '') as OPE_ALPHA41,     -- varchar(50)
                OPE_MODA,                                    -- date NULL
                ISNULL(OPE_TOP22, 0) as OPE_TOP22,          -- numeric NULL
                ISNULL(OPE_STAT, '') as OPE_STAT            -- varchar(10)
            FROM OPE_DAT
            WHERE OPE_KEYU IS NOT NULL
            AND OPE_CRQI = 'INTERFACE'
            AND ACT_CODE = 'COSMETIQUE'
            AND OPE_STAT = '070'  -- Seulement les BL en préparation (statut 070)
            -- 🧪 FILTRE TEST : Seulement OPE_ALPHA17 = PP000448
            -- AND OPE_ALPHA17 in ('PP000285', 'PP000282')
            ORDER BY OPE_KEYU";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var blData = new SpeedWmsBLData
                    {
                        OpeKeyu = reader.GetInt32("OPE_KEYU").ToString(),
                        OpeRedo = SafeGetString(reader, "OPE_REDO"),
                        OpeAlpha17 = SafeGetString(reader, "OPE_ALPHA17"),
                        OpeCtra = SafeGetString(reader, "OPE_CTRA"),
                        OpeAlpha40 = SafeGetString(reader, "OPE_ALPHA40"),
                        OpeAlpha41 = SafeGetString(reader, "OPE_ALPHA41"),
                        OpeModa = SafeGetDateTime(reader, "OPE_MODA"),
                        OpeTop22 = SafeGetNumeric(reader, "OPE_TOP22").ToString(),
                        OpeStat = SafeGetString(reader, "OPE_STAT"),
                        Fnc008Date = null,
                        DataHeurreIc = ""
                    };

                    blHeaders.Add(blData);
                }

                _logger.LogInformation($"📋 {blHeaders.Count} BL trouvé(s) dans SpeedWMS");
                // 🧪 NOTE: filtres de test désactivés par défaut. Pour activer, décommenter les lignes correspondantes ci-dessus.

                foreach (var bl in blHeaders)
                {
                    _logger.LogDebug($"📦 BL trouvé: {bl.OpeKeyu} | ALPHA17={bl.OpeAlpha17} | REDO={bl.OpeRedo}");
                }

                return blHeaders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des en-têtes BL");
                throw;
            }
        }

        /// <summary>
        /// Récupère les lignes d'articles pour un BL donné - CORRECTION SELON VRAIE STRUCTURE
        /// </summary>
        private async Task<List<SpeedWmsBLLine>> GetBLLinesAsync(SqlConnection connection, string opeKeyu)
        {
            var lines = new List<SpeedWmsBLLine>();

            try
            {
                // ✅ CORRECTION : Utiliser la jointure correcte via OPE_NoOE
                // D'après le diagnostic, MIL_DAT n'a pas OPE_KEYU mais OPE_NoOE
                const string sql = @"
            SELECT 
                ISNULL(SUBSTRING(MIL.ART_CODE,3,35), '') as ART_CODE,               -- varchar(35)
                ISNULL(MIL.MIL_QTTP, 0) as MIL_QTTP,                -- decimal
                ISNULL(MIL.MIL_QTTA, 0) as MIL_QTTA,                -- decimal  
                ISNULL(MIL.MIL_QTMA, 0) as MIL_QTMA,                -- decimal
                ISNULL(MIL.MIL_LOT1P, '') as MIL_LOT1P,             -- varchar(100)
                ISNULL(MIL.MIL_LOT2P, '') as MIL_LOT2P,             -- varchar(100)
                ISNULL(MIL.MIL_SUPP, '') as MIL_SUPP                -- varchar(25)
            FROM MIL_DAT MIL
            INNER JOIN OPE_DAT OPE ON MIL.OPE_NoOE = OPE.OPE_NoOE and MIL.ACT_CODE = OPE.ACT_CODE
            WHERE OPE.OPE_KEYU = @OpeKeyu
            AND OPE.ACT_CODE = 'COSMETIQUE' -- Filtrer par activité COSMETIQUE
            ORDER BY MIL.ART_CODE";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@OpeKeyu", int.Parse(opeKeyu));

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var line = new SpeedWmsBLLine
                    {
                        OpeKeyu = opeKeyu,
                        ArtCode = SafeGetString(reader, "ART_CODE"),
                        QttePreparee = SafeGetDecimal(reader, "MIL_QTTP"),
                        QttePrevue = SafeGetDecimal(reader, "MIL_QTTA"),
                        QtteManquante = SafeGetDecimal(reader, "MIL_QTMA"),
                        Lot1 = SafeGetString(reader, "MIL_LOT1P"),
                        Lot2 = SafeGetString(reader, "MIL_LOT2P"),
                        Support = SafeGetString(reader, "MIL_SUPP"),
                        MaxMieModa = null,
                        DluoMin = null
                    };

                    lines.Add(line);
                }

                _logger.LogDebug($"📄 {lines.Count} lignes récupérées pour BL {opeKeyu} (structure corrigée)");
                return lines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la récupération des lignes pour BL {opeKeyu}");
                return lines;
            }
        }

        /// <summary>
        /// Récupère les données de support/emballage pour un BL donné - CORRECTION SELON VRAIE STRUCTURE
        /// </summary>
        private async Task<List<SpeedWmsSupportData>> GetBLSupportsAsync(SqlConnection connection, string opeKeyu)
        {
            var supports = new List<SpeedWmsSupportData>();

            try
            {
                // ✅ CORRECTION : Utiliser SEX_SUPE au lieu de SEX_SUPP (selon le diagnostic)
                const string sql = @"
            SELECT 
                ISNULL(SEX.SEX_SUPE, '') as SEX_SUPE,               -- varchar(25) - CORRIGÉ
                ISNULL(SEX.SEX_POISR, 0) as SEX_POISR,              -- bigint  
                ISNULL(SEX.SEX_PROF, 0) as SEX_PROF,                -- decimal
                ISNULL(SEX.SEX_LARG, 0) as SEX_LARG,                -- decimal
                ISNULL(SEX.SEX_HAUT, 0) as SEX_HAUT,                -- decimal
                ISNULL(SEX.SEX_SUPR, '') as SEX_SUPR                -- varchar(25)
            FROM SEX_DAT SEX
            INNER JOIN OPE_DAT OPE ON SEX.SEX_NoOE = OPE.OPE_NoOE
            WHERE OPE.OPE_KEYU = @OpeKeyu
            AND SEX.SEX_ACT = 'COSMETIQUE' -- Filtrer par activité COSMETIQUE
            ORDER BY SEX.SEX_SUPE";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@OpeKeyu", int.Parse(opeKeyu));

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var support = new SpeedWmsSupportData
                    {
                        SupportId = SafeGetString(reader, "SEX_SUPE"), // ✅ CORRIGÉ
                        Poids = SafeGetDecimal(reader, "SEX_POISR"),
                        Longueur = SafeGetDecimal(reader, "SEX_PROF"),
                        Largeur = SafeGetDecimal(reader, "SEX_LARG"),
                        Hauteur = SafeGetDecimal(reader, "SEX_HAUT"),
                        SupportRegroupement = SafeGetString(reader, "SEX_SUPR"),

                        // Valeurs par défaut pour les champs manquants
                        LongueurRegroupement = 0,
                        LargeurRegroupement = 0,
                        HauteurRegroupement = 0,
                        PoidsRegroupement = 0,
                        SupportType = "Colis",
                        TypeRegroupement = "Palette"
                    };

                    supports.Add(support);
                }

                _logger.LogDebug($"📦 {supports.Count} supports récupérés pour BL {opeKeyu} (structure corrigée)");
                return supports;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la récupération des supports pour BL {opeKeyu}");
                return supports;
            }
        }

        /// <summary>
        /// Version de diagnostic pour identifier les vraies colonnes disponibles
        /// À AJOUTER temporairement pour vérifier la structure
        /// </summary>
        public async Task<string> DiagnoseTableJoinsAsync()
        {
            try
            {
                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "🔍 === DIAGNOSTIC JOINTURES === 🔍\n\n";

                // Test 1: Vérifier les clés de jointure disponibles
                try
                {
                    const string joinTestSql = @"
                SELECT TOP 5
                    OPE.OPE_KEYU,
                    OPE.OPE_NoOE,
                    MIL.OPE_NoOE as MIL_OPE_NoOE,
                    MIL.ART_CODE
                FROM OPE_DAT OPE
                LEFT JOIN MIL_DAT MIL ON OPE.OPE_NoOE = MIL.OPE_NoOE
                WHERE OPE.OPE_KEYU IN (1,2,3,4,5)
                AND OPE.ACT_CODE = 'COSMETIQUE' -- Filtrer par activité COSMETIQUE
                ORDER BY OPE.OPE_KEYU";

                    using var joinCommand = new SqlCommand(joinTestSql, connection);
                    using var joinReader = await joinCommand.ExecuteReaderAsync();

                    report += "✅ Test jointure OPE_DAT <-> MIL_DAT via OPE_NoOE:\n";

                    while (await joinReader.ReadAsync())
                    {
                        var opeKeyu = joinReader.GetInt32("OPE_KEYU");
                        var opeNoOE = joinReader.IsDBNull("OPE_NoOE") ? "NULL" : joinReader.GetInt64("OPE_NoOE").ToString();
                        var milOpeNoOE = joinReader.IsDBNull("MIL_OPE_NoOE") ? "NULL" : joinReader.GetInt64("MIL_OPE_NoOE").ToString();
                        var artCode = joinReader.IsDBNull("ART_CODE") ? "NULL" : joinReader.GetString("ART_CODE");

                        report += $"   BL {opeKeyu}: OPE_NoOE={opeNoOE}, MIL_OPE_NoOE={milOpeNoOE}, ART={artCode}\n";
                    }
                }
                catch (Exception ex)
                {
                    report += $"❌ Erreur test jointure MIL_DAT: {ex.Message}\n";
                }

                report += "\n";

                // Test 2: Vérifier les clés de jointure pour SEX_DAT
                try
                {
                    const string sexJoinTestSql = @"
                SELECT TOP 5
                    OPE.OPE_KEYU,
                    OPE.OPE_NoOE,
                    SEX.SEX_NoOE as SEX_OPE_NoOE,
                    SEX.SEX_SUPE
                FROM OPE_DAT OPE
                LEFT JOIN SEX_DAT SEX ON OPE.OPE_NoOE = SEX.SEX_NoOE
                WHERE OPE.OPE_KEYU IN (1,2,3,4,5)
                AND OPE.ACT_CODE = 'COSMETIQUE' -- Filtrer par activité COSMETIQUE
                ORDER BY OPE.OPE_KEYU";

                    using var sexCommand = new SqlCommand(sexJoinTestSql, connection);
                    using var sexReader = await sexCommand.ExecuteReaderAsync();

                    report += "✅ Test jointure OPE_DAT <-> SEX_DAT via SEX_NoOE:\n";

                    while (await sexReader.ReadAsync())
                    {
                        var opeKeyu = sexReader.GetInt32("OPE_KEYU");
                        var opeNoOE = sexReader.IsDBNull("OPE_NoOE") ? "NULL" : sexReader.GetInt64("OPE_NoOE").ToString();
                        var sexOpeNoOE = sexReader.IsDBNull("SEX_OPE_NoOE") ? "NULL" : sexReader.GetInt64("SEX_OPE_NoOE").ToString();
                        var sexSupe = sexReader.IsDBNull("SEX_SUPE") ? "NULL" : sexReader.GetString("SEX_SUPE");

                        report += $"   BL {opeKeyu}: OPE_NoOE={opeNoOE}, SEX_NoOE={sexOpeNoOE}, SUPE={sexSupe}\n";
                    }
                }
                catch (Exception ex)
                {
                    report += $"❌ Erreur test jointure SEX_DAT: {ex.Message}\n";
                }

                return report;
            }
            catch (Exception ex)
            {
                return $"❌ Erreur diagnostic jointures: {ex.Message}";
            }
        }

        // ==========================================
        // MÉTHODES UTILITAIRES SÉCURISÉES
        // ==========================================

        /// <summary>
        /// Lecture sécurisée d'une chaîne de caractères - VERSION AMÉLIORÉE
        /// </summary>
        private string SafeGetString(SqlDataReader reader, string columnName)
        {
            try
            {
                if (reader.IsDBNull(columnName))
                    return "";

                var value = reader[columnName];
                return value?.ToString()?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Erreur lecture string {columnName}: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Lecture sécurisée d'un décimal - VERSION AMÉLIORÉE
        /// </summary>
        private decimal SafeGetDecimal(SqlDataReader reader, string columnName)
        {
            try
            {
                if (reader.IsDBNull(columnName))
                    return 0;

                var value = reader[columnName];

                // Conversion selon le type réel
                switch (value)
                {
                    case decimal d:
                        return d;
                    case double db:
                        return (decimal)db;
                    case float f:
                        return (decimal)f;
                    case int i:
                        return i;
                    case long l:
                        return l;
                    case byte b:
                        return b;
                    case short s:
                        return s;
                    case string str when !string.IsNullOrEmpty(str.Trim()):
                        var cleanStr = str.Trim().Replace(',', '.');
                        if (decimal.TryParse(cleanStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var result))
                            return result;
                        break;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Erreur conversion decimal {columnName}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Lecture sécurisée d'une date - VERSION AMÉLIORÉE
        /// </summary>
        private DateTime? SafeGetDateTime(SqlDataReader reader, string columnName)
        {
            try
            {
                if (reader.IsDBNull(columnName))
                    return null;

                var value = reader[columnName];

                // Tentative de conversion directe
                if (value is DateTime dateTimeValue)
                    return dateTimeValue;

                // Tentative de parsing depuis string
                var stringValue = value?.ToString();
                if (!string.IsNullOrEmpty(stringValue))
                {
                    if (DateTime.TryParse(stringValue, out var parsedDate))
                    {
                        return parsedDate;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Erreur conversion DateTime {columnName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Teste et analyse la structure des tables SpeedWMS pour adaptation
        /// À AJOUTER dans Services/SpeedWmsDataService.cs
        /// </summary>
        public async Task<string> AnalyzeSpeedWmsStructureAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Analyse de la structure SpeedWMS...");

                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "📊 === ANALYSE STRUCTURE SPEEDWMS === 📊\n\n";

                // Tester les tables principales
                var tablesToTest = new[] { "OPE_DAT", "MIL_DAT", "SEX_DAT", "MIE_DAT", "OPL_DAT", "EMB_DAT" };

                foreach (var tableName in tablesToTest)
                {
                    report += await AnalyzeTableStructureAsync(connection, tableName);
                    report += "\n";
                }

                _logger.LogInformation("✅ Analyse structure terminée");
                return report;
            }
            catch (Exception ex)
            {
                var errorReport = $"❌ Erreur analyse structure: {ex.Message}";
                _logger.LogError(ex, "❌ Erreur lors de l'analyse de structure SpeedWMS");
                return errorReport;
            }
        }

        /// <summary>
        /// Analyse la structure d'une table spécifique
        /// </summary>
        private async Task<string> AnalyzeTableStructureAsync(SqlConnection connection, string tableName)
        {
            try
            {
                // Vérifier si la table existe
                const string tableExistsSql = @"
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_NAME = @TableName";

                using var tableExistsCommand = new SqlCommand(tableExistsSql, connection);
                tableExistsCommand.Parameters.AddWithValue("@TableName", tableName);
                var tableExists = (int)await tableExistsCommand.ExecuteScalarAsync() > 0;

                if (!tableExists)
                {
                    return $"❌ Table {tableName}: N'EXISTE PAS";
                }

                // Récupérer la structure des colonnes
                const string columnsSql = @"
            SELECT 
                COLUMN_NAME,
                DATA_TYPE,
                IS_NULLABLE,
                ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) as MAX_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @TableName
            ORDER BY ORDINAL_POSITION";

                using var columnsCommand = new SqlCommand(columnsSql, connection);
                columnsCommand.Parameters.AddWithValue("@TableName", tableName);

                var tableReport = $"✅ Table {tableName}:\n";

                using var reader = await columnsCommand.ExecuteReaderAsync();
                var columnCount = 0;

                while (await reader.ReadAsync())
                {
                    columnCount++;
                    var columnName = reader.GetString("COLUMN_NAME");
                    var dataType = reader.GetString("DATA_TYPE");
                    var isNullable = reader.GetString("IS_NULLABLE");
                    var maxLength = reader.GetInt32("MAX_LENGTH");

                    var nullableStr = isNullable == "YES" ? "NULL" : "NOT NULL";
                    var lengthStr = maxLength > 0 ? $"({maxLength})" : "";

                    tableReport += $"   - {columnName}: {dataType}{lengthStr} {nullableStr}\n";
                }

                tableReport += $"   📊 Total: {columnCount} colonnes\n";

                // Tester un SELECT simple pour voir si la table est accessible
                await reader.CloseAsync();

                try
                {
                    var testSql = $"SELECT COUNT(*) FROM {tableName}";
                    using var testCommand = new SqlCommand(testSql, connection);
                    var rowCount = (int)await testCommand.ExecuteScalarAsync();
                    tableReport += $"   📦 Contient: {rowCount:N0} enregistrements\n";
                }
                catch (Exception ex)
                {
                    tableReport += $"   ⚠️ Erreur lecture: {ex.Message}\n";
                }

                return tableReport;
            }
            catch (Exception ex)
            {
                return $"❌ Table {tableName}: Erreur analyse - {ex.Message}\n";
            }
        }

        /// <summary>
        /// Test simple de connectivité SpeedWMS avec rapport détaillé
        /// VERSION ÉTENDUE de TestSpeedWmsConnectionAsync
        /// </summary>
        public async Task<string> TestSpeedWmsConnectionDetailedAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Test de connectivité SpeedWMS détaillé...");

                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "📊 === TEST CONNEXION SPEEDWMS === 📊\n";
                report += $"✅ Connexion établie avec succès\n";
                report += $"🔗 Serveur: {connection.DataSource}\n";
                report += $"🗄️ Base: {connection.Database}\n";
                report += $"👤 Utilisateur: {connection.ClientConnectionId}\n\n";

                // Test des tables principales
                var testResults = await AnalyzeSpeedWmsStructureAsync();
                report += testResults;

                _logger.LogInformation("✅ Test connectivité SpeedWMS terminé avec succès");
                return report;
            }
            catch (Exception ex)
            {
                var errorReport = $"❌ ÉCHEC CONNEXION SPEEDWMS\n";
                errorReport += $"💥 Erreur: {ex.Message}\n";
                errorReport += $"🔧 Vérifiez la chaîne de connexion SpeedWmsConnection dans appsettings.json\n";

                _logger.LogError(ex, "❌ Échec test connectivité SpeedWMS");
                return errorReport;
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
        /// ✅ Transforme un BL SpeedWMS en BL d'export avec ImportId = OPE_KEYU
        /// </summary>
        private async Task<BLExportData> TransformSingleBLAsync(SpeedWmsBLData speedBL)
        {
            var exportBL = new BLExportData
            {
                BLNumber = speedBL.OpeKeyu,
                ImportId = speedBL.OpeKeyu,  // ✅ CORRECTION: ImportId = OPE_KEYU directement
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

            _logger.LogDebug($"✅ BL {speedBL.OpeKeyu} transformé: ImportId={exportBL.ImportId}, {exportBL.Lines.Count} lignes, {exportBL.Supports.Count} supports");

            return exportBL;
        }

        /// <summary>
        /// Retire le préfixe client (2 premiers caractères) du code article
        /// Exemple: "BRGANTC" devient "GANTC"
        /// </summary>
        private string CleanItemId(string artCode)
        {
            if (string.IsNullOrEmpty(artCode) || artCode.Length <= 2)
            {
                return artCode;
            }

            return artCode.Substring(2);
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
                    ItemId = CleanItemId(group.Key),
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
        /// Lecture sécurisée d'un numeric (pour OPE_TOP22, etc.)
        /// </summary>
        private decimal SafeGetNumeric(SqlDataReader reader, string columnName)
        {
            try
            {
                if (reader.IsDBNull(columnName))
                    return 0;

                var value = reader[columnName];

                // Gestion des types numeric SQL Server
                if (value is decimal decimalValue)
                    return decimalValue;

                if (value is double doubleValue)
                    return (decimal)doubleValue;

                if (value is float floatValue)
                    return (decimal)floatValue;

                if (value is int intValue)
                    return intValue;

                if (value is long longValue)
                    return longValue;

                if (value is byte byteValue)
                    return byteValue;

                if (value is short shortValue)
                    return shortValue;

                // Tentative de parsing depuis string
                var stringValue = value?.ToString();
                if (!string.IsNullOrEmpty(stringValue))
                {
                    stringValue = stringValue.Trim().Replace(',', '.');

                    if (decimal.TryParse(stringValue, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedValue))
                    {
                        return parsedValue;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Erreur conversion numeric {columnName}: {ex.Message}");
                return 0;
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

        /// <summary>
        /// Méthode de diagnostic pour identifier les problèmes de types de données
        /// À AJOUTER dans Services/SpeedWmsDataService.cs
        /// </summary>
        public async Task<string> DiagnoseSpeedWmsDataTypesAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Diagnostic des types de données SpeedWMS...");

                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "📊 === DIAGNOSTIC TYPES DONNÉES SPEEDWMS === 📊\n\n";

                // Test 1: Analyser OPE_DAT en détail
                report += await DiagnoseTableAsync(connection, "OPE_DAT");
                report += "\n";

                // Test 2: Analyser MIL_DAT en détail
                report += await DiagnoseTableAsync(connection, "MIL_DAT");
                report += "\n";

                // Test 3: Analyser SEX_DAT en détail
                report += await DiagnoseTableAsync(connection, "SEX_DAT");
                report += "\n";

                // Test 4: Test d'un SELECT simple
                report += await TestSimpleSelectAsync(connection);

                _logger.LogInformation("✅ Diagnostic terminé");
                return report;
            }
            catch (Exception ex)
            {
                var errorReport = $"❌ Erreur diagnostic: {ex.Message}";
                _logger.LogError(ex, "❌ Erreur lors du diagnostic SpeedWMS");
                return errorReport;
            }
        }

        /// <summary>
        /// Diagnostique une table spécifique
        /// </summary>
        private async Task<string> DiagnoseTableAsync(SqlConnection connection, string tableName)
        {
            try
            {
                var report = $"🔍 Table {tableName}:\n";

                // Récupérer la structure des colonnes avec types détaillés
                const string columnsSql = @"
            SELECT 
                COLUMN_NAME,
                DATA_TYPE,
                IS_NULLABLE,
                ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) as MAX_LENGTH,
                ISNULL(NUMERIC_PRECISION, 0) as PRECISION,
                ISNULL(NUMERIC_SCALE, 0) as SCALE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @TableName
            ORDER BY ORDINAL_POSITION";

                using var columnsCommand = new SqlCommand(columnsSql, connection);
                columnsCommand.Parameters.AddWithValue("@TableName", tableName);

                using var reader = await columnsCommand.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var columnName = reader.GetString("COLUMN_NAME");
                    var dataType = reader.GetString("DATA_TYPE");
                    var isNullable = reader.GetString("IS_NULLABLE");
                    var maxLength = reader.GetInt32("MAX_LENGTH");
                    var precision = reader.GetInt32("PRECISION");
                    var scale = reader.GetInt32("SCALE");

                    var nullableStr = isNullable == "YES" ? "NULL" : "NOT NULL";
                    var typeDetails = dataType;

                    if (maxLength > 0)
                        typeDetails += $"({maxLength})";
                    else if (precision > 0)
                        typeDetails += $"({precision},{scale})";

                    report += $"   - {columnName}: {typeDetails} {nullableStr}\n";
                }

                await reader.CloseAsync();

                // Test d'un SELECT simple pour voir les vraies données
                try
                {
                    var testSql = $"SELECT TOP 3 * FROM {tableName}";
                    using var testCommand = new SqlCommand(testSql, connection);
                    using var testReader = await testCommand.ExecuteReaderAsync();

                    report += $"   📊 Échantillon données:\n";
                    var rowCount = 0;

                    while (await testReader.ReadAsync() && rowCount < 3)
                    {
                        rowCount++;
                        report += $"      Ligne {rowCount}: ";

                        for (int i = 0; i < testReader.FieldCount; i++)
                        {
                            var fieldName = testReader.GetName(i);
                            var fieldValue = testReader.IsDBNull(i) ? "NULL" : testReader[i]?.ToString();
                            var fieldType = testReader[i]?.GetType().Name ?? "NULL";

                            report += $"{fieldName}={fieldValue}({fieldType}) ";
                        }
                        report += "\n";
                    }
                }
                catch (Exception ex)
                {
                    report += $"   ❌ Erreur échantillon: {ex.Message}\n";
                }

                return report;
            }
            catch (Exception ex)
            {
                return $"❌ Erreur diagnostic table {tableName}: {ex.Message}\n";
            }
        }

        /// <summary>
        /// Test d'un SELECT simple pour identifier le problème exact
        /// </summary>
        private async Task<string> TestSimpleSelectAsync(SqlConnection connection)
        {
            var report = "🧪 === TESTS SELECT SIMPLES === 🧪\n";

            var testQueries = new[]
            {
        ("Compte OPE_DAT", "SELECT COUNT(*) FROM OPE_DAT"),
        ("Premier OPE_KEYU", "SELECT TOP 1 OPE_KEYU FROM OPE_DAT"),
        ("Types OPE_MODA", "SELECT TOP 3 OPE_KEYU, OPE_MODA FROM OPE_DAT WHERE OPE_MODA IS NOT NULL"),
        ("Compte MIL_DAT", "SELECT COUNT(*) FROM MIL_DAT"),
        ("Compte SEX_DAT", "SELECT COUNT(*) FROM SEX_DAT")
    };

            foreach (var (testName, query) in testQueries)
            {
                try
                {
                    using var command = new SqlCommand(query, connection);
                    using var reader = await command.ExecuteReaderAsync();

                    report += $"✅ {testName}: ";

                    if (await reader.ReadAsync())
                    {
                        var result = reader[0]?.ToString() ?? "NULL";
                        report += $"{result}\n";
                    }
                    else
                    {
                        report += "Aucun résultat\n";
                    }
                }
                catch (Exception ex)
                {
                    report += $"❌ {testName}: {ex.Message}\n";
                }
            }

            return report;
        }

        /// <summary>
        /// Version simplifiée de GetAllAvailableBLsAsync pour le diagnostic
        /// </summary>
        public async Task<string> TestBLRetrievalAsync()
        {
            try
            {
                _logger.LogInformation("🧪 Test récupération BL en mode diagnostic...");

                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "🧪 === TEST RÉCUPÉRATION BL === 🧪\n";

                // Test 1: Simple count
                try
                {
                    const string countSql = "SELECT COUNT(*) FROM OPE_DAT";
                    using var countCommand = new SqlCommand(countSql, connection);
                    var count = (int)await countCommand.ExecuteScalarAsync();
                    report += $"✅ Total BL dans OPE_DAT: {count}\n";
                }
                catch (Exception ex)
                {
                    report += $"❌ Erreur count OPE_DAT: {ex.Message}\n";
                    return report;
                }

                // Test 2: SELECT simplifié
                try
                {
                    const string simpleSql = @"
                SELECT TOP 5
                    OPE_KEYU,
                    OPE_REDO,
                    OPE_STAT
                FROM OPE_DAT
                WHERE OPE_KEYU IS NOT NULL
                ORDER BY OPE_KEYU";

                    using var simpleCommand = new SqlCommand(simpleSql, connection);
                    using var reader = await simpleCommand.ExecuteReaderAsync();

                    report += "✅ Échantillon BL:\n";
                    var rowCount = 0;

                    while (await reader.ReadAsync() && rowCount < 5)
                    {
                        rowCount++;
                        var opeKeyu = SafeGetString(reader, "OPE_KEYU");
                        var opeRedo = SafeGetString(reader, "OPE_REDO");
                        var opeStat = SafeGetString(reader, "OPE_STAT");

                        report += $"   BL {rowCount}: {opeKeyu} | {opeRedo} | {opeStat}\n";
                    }
                }
                catch (Exception ex)
                {
                    report += $"❌ Erreur SELECT simple: {ex.Message}\n";
                    return report;
                }

                // Test 3: SELECT avec OPE_MODA (source probable du problème)
                try
                {
                    const string dateSql = @"
                SELECT TOP 5
                    OPE_KEYU,
                    OPE_MODA
                FROM OPE_DAT
                WHERE OPE_KEYU IS NOT NULL
                ORDER BY OPE_KEYU";

                    using var dateCommand = new SqlCommand(dateSql, connection);
                    using var dateReader = await dateCommand.ExecuteReaderAsync();

                    report += "✅ Test dates OPE_MODA:\n";
                    var dateRowCount = 0;

                    while (await dateReader.ReadAsync() && dateRowCount < 5)
                    {
                        dateRowCount++;
                        var opeKeyu = SafeGetString(dateReader, "OPE_KEYU");
                        var opeModa = SafeGetDateTime(dateReader, "OPE_MODA");

                        report += $"   BL {dateRowCount}: {opeKeyu} | Date: {opeModa?.ToString("dd/MM/yyyy") ?? "NULL"}\n";
                    }
                }
                catch (Exception ex)
                {
                    report += $"❌ Erreur SELECT dates: {ex.Message}\n";
                    report += "💡 Le problème vient probablement de OPE_MODA\n";
                }

                return report;
            }
            catch (Exception ex)
            {
                return $"❌ Erreur test récupération BL: {ex.Message}";
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