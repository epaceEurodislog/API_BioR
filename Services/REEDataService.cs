// Fichier: Services/REEDataService.cs
// Service d'accès aux données REE_DAT pour les journaux de réception
// Équivalent de SpeedWmsDataService.cs pour les données de réception

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DynamicsApiToDatabase.Models;
using System.Data;

namespace DynamicsApiToDatabase.Services
{
    public interface IREEDataService
    {
        Task<List<ItemArrivalJournalData>> GetPendingJournalsAsync();
        Task<string> AnalyzeREEStructureAsync();
        Task<string> DiagnoseREETablesAsync();
    }

    /// <summary>
    /// Service pour récupérer les données de réception depuis REE_DAT, MVT_DAT, REL_DAT
    /// Structure basée sur le mapping fourni pour ItemArrivalJournal
    /// </summary>
    public class REEDataService : IREEDataService
    {
        private readonly string _speedWmsConnectionString;
        private readonly ILogger<REEDataService> _logger;

        public REEDataService(IConfiguration configuration, ILogger<REEDataService> logger)
        {
            _speedWmsConnectionString = configuration.GetConnectionString("SpeedWmsConnection")
                ?? throw new ArgumentNullException("SpeedWmsConnection manquante");
            _logger = logger;
        }

        /// <summary>
        /// Récupère tous les journaux de réception en attente de traitement
        /// </summary>
        public async Task<List<ItemArrivalJournalData>> GetPendingJournalsAsync()
        {
            var journals = new List<ItemArrivalJournalData>();

            try
            {
                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                _logger.LogInformation("🔍 Récupération des journaux de réception en attente...");

                // Requête principale pour récupérer les données REE_DAT avec leurs mouvements
                const string sql = @"
                    SELECT DISTINCT
                        REE.REE_NOREIN,
                        REE.REE_DARE,
                        REE.REE_ETRE,
                        REE.ACT_CODE,
                        REE.REE_CCLI,
                        REE.QUA_CODE,
                        MVT.REA_RFCE,
                        MVT.REA_RFTI,
                        MVT.NoLR
                    FROM REE_DAT REE
                    INNER JOIN MVT_DAT MVT ON REE.REE_KEYU = MVT.REE_KEYU
                    WHERE REE.REE_ETRE = '200'
                      AND REE.REE_NOREIN IS NOT NULL
                      AND REE.REE_NOREIN != ''
                      AND MVT.REA_RFTI IS NOT NULL
                      AND MVT.REA_RFTI != ''
                    ORDER BY REE.REE_DARE DESC, REE.REE_NOREIN";

                using var command = new SqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                var journalDict = new Dictionary<string, ItemArrivalJournalData>();

                while (await reader.ReadAsync())
                {
                    var packingSlipId = SafeGetString(reader, "REE_NOREIN");
                    var transactionRefNumber = SafeGetString(reader, "REA_RFTI");

                    // Clé unique basée sur PackingSlipId + TransactionReferenceNumber
                    var journalKey = $"{packingSlipId}_{transactionRefNumber}";

                    if (!journalDict.ContainsKey(journalKey))
                    {
                        var journal = new ItemArrivalJournalData
                        {
                            JournalNumber = await GenerateJournalNumberAsync(connection),
                            PackingSlipId = packingSlipId,
                            TransactionReferenceNumber = transactionRefNumber,
                            TransactionDate = SafeGetDateTime(reader, "REE_DARE"),
                            DataAreaId = "BR",
                            JournalNameId = "ARR",
                            DefaultReceivingSiteId = "S01",
                            DefaultReceivingWarehouseId = "12",
                            DefaultReceivingWarehouseLocationId = "RECNOLP"
                        };

                        journalDict[journalKey] = journal;
                    }

                    // Ajouter les informations de mouvement au journal
                    var noLR = SafeGetInt(reader, "NoLR");
                    if (noLR > 0)
                    {
                        await AddJournalLinesAsync(connection, journalDict[journalKey], noLR,
                            SafeGetString(reader, "QUA_CODE"));
                    }
                }

                journals.AddRange(journalDict.Values);
                _logger.LogInformation($"✅ {journals.Count} journaux de réception récupérés");

                return journals;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des journaux REE_DAT");
                return journals;
            }
        }

        /// <summary>
        /// Ajoute les lignes d'articles à un journal basées sur NoLR
        /// </summary>
        private async Task AddJournalLinesAsync(SqlConnection connection, ItemArrivalJournalData journal,
            int noLR, string quaCode)
        {
            try
            {
                const string linesSql = @"
                    SELECT 
                        REL.ART_CODE,
                        SUM(REL.ART_QTEUB) as TOTAL_QTE,    -- Cumul par NoLR selon RG1
                        REL.REL_LOT1,
                        REL.REL_DLUO,
                        REL.REL_LOT2
                    FROM REL_DAT REL
                    WHERE REL.NoLR = @NoLR
                      AND REL.ART_CODE IS NOT NULL
                      AND REL.ART_CODE != ''
                    GROUP BY REL.ART_CODE, REL.REL_LOT1, REL.REL_DLUO, REL.REL_LOT2
                    ORDER BY REL.ART_CODE";

                using var linesCommand = new SqlCommand(linesSql, connection);
                linesCommand.Parameters.AddWithValue("@NoLR", noLR);

                using var linesReader = await linesCommand.ExecuteReaderAsync();

                while (await linesReader.ReadAsync())
                {
                    var line = new ItemArrivalJournalLine
                    {
                        LineNumber = noLR,
                        ItemNumber = SafeGetString(linesReader, "ART_CODE"),
                        ItemQuantity = SafeGetLong(linesReader, "TOTAL_QTE"),
                        ItemBatchNumber = SafeGetString(linesReader, "REL_LOT1"),
                        ItemSerialNumber = SafeGetString(linesReader, "REL_LOT2"),
                        ExpDate = SafeGetNullableDateTime(linesReader, "REL_DLUO"),
                        ReceivingInventoryStatusId = QualityCodeMapper.MapToInventoryStatus(quaCode)
                    };

                    journal.Lines.Add(line);
                }

                _logger.LogDebug($"📄 {journal.Lines.Count} lignes ajoutées pour journal {journal.PackingSlipId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de l'ajout des lignes pour NoLR {noLR}");
                journal.HasErrors = true;
                journal.ErrorMessages.Add($"Erreur lignes NoLR {noLR}: {ex.Message}");
            }
        }

        /// <summary>
        /// Génère un numéro de journal unique
        /// </summary>
        private async Task<string> GenerateJournalNumberAsync(SqlConnection connection)
        {
            try
            {
                // Logique de génération de numéro de journal
                // Exemple : JA + année + mois + séquence
                var datePrefix = DateTime.Now.ToString("yyyyMM");

                const string countSql = @"
                    SELECT COUNT(*) 
                    FROM JSON_OUT 
                    WHERE JSON_DEST = 'ItemArrivalHeaders'
                      AND JSON_CRDA >= @StartOfMonth";

                using var countCommand = new SqlCommand(countSql, connection);
                countCommand.Parameters.AddWithValue("@StartOfMonth", new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1));

                var count = (int)await countCommand.ExecuteScalarAsync() + 1;
                var journalNumber = $"JA{datePrefix}{count:D4}";

                _logger.LogDebug($"📝 Numéro de journal généré: {journalNumber}");
                return journalNumber;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Erreur génération numéro journal, utilisation fallback");
                return $"JA{DateTime.Now:yyyyMMddHHmmss}";
            }
        }

        /// <summary>
        /// Analyse la structure des tables REE_DAT, MVT_DAT, REL_DAT
        /// </summary>
        public async Task<string> AnalyzeREEStructureAsync()
        {
            try
            {
                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "📊 === ANALYSE STRUCTURE REE/MVT/REL === 📊\n\n";

                // Tester les tables de réception
                var tablesToTest = new[] { "REE_DAT", "MVT_DAT", "REL_DAT" };

                foreach (var tableName in tablesToTest)
                {
                    report += await AnalyzeTableStructureAsync(connection, tableName);
                    report += "\n";
                }

                _logger.LogInformation("✅ Analyse structure REE terminée");
                return report;
            }
            catch (Exception ex)
            {
                var errorReport = $"❌ Erreur analyse structure REE: {ex.Message}";
                _logger.LogError(ex, "❌ Erreur lors de l'analyse de structure REE");
                return errorReport;
            }
        }

        /// <summary>
        /// Diagnostic des tables REE avec exemples de données
        /// </summary>
        public async Task<string> DiagnoseREETablesAsync()
        {
            try
            {
                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "🔍 === DIAGNOSTIC TABLES REE === 🔍\n\n";

                // Test de jointure REE_DAT -> MVT_DAT -> REL_DAT
                try
                {
                    const string joinTestSql = @"
                        SELECT TOP 5
                            REE.REE_KEYU,
                            REE.REE_NOREIN,
                            REE.REE_DARE,
                            REE.QUA_CODE,
                            MVT.REA_RFCE,
                            MVT.REA_RFTI,
                            MVT.NoLR,
                            REL.ART_CODE,
                            REL.ART_QTEUB
                        FROM REE_DAT REE
                        LEFT JOIN MVT_DAT MVT ON REE.REE_KEYU = MVT.REE_KEYU
                        LEFT JOIN REL_DAT REL ON MVT.NoLR = REL.NoLR
                        WHERE REE.REE_NOREIN IS NOT NULL
                        ORDER BY REE.REE_KEYU DESC";

                    using var joinCommand = new SqlCommand(joinTestSql, connection);
                    using var joinReader = await joinCommand.ExecuteReaderAsync();

                    report += "✅ Test jointure REE_DAT -> MVT_DAT -> REL_DAT:\n";

                    while (await joinReader.ReadAsync())
                    {
                        var reeKeyu = SafeGetInt(joinReader, "REE_KEYU");
                        var packingSlip = SafeGetString(joinReader, "REE_NOREIN");
                        var orderRef = SafeGetString(joinReader, "REA_RFTI");
                        var artCode = SafeGetString(joinReader, "ART_CODE");
                        var quantity = SafeGetLong(joinReader, "ART_QTEUB");

                        report += $"   REE_KEYU={reeKeyu}, PackingSlip={packingSlip}, Order={orderRef}, " +
                                 $"Article={artCode}, Qty={quantity}\n";
                    }
                }
                catch (Exception ex)
                {
                    report += $"❌ Erreur test jointure: {ex.Message}\n";
                }

                return report;
            }
            catch (Exception ex)
            {
                var errorReport = $"❌ Erreur diagnostic REE: {ex.Message}";
                _logger.LogError(ex, "❌ Erreur lors du diagnostic REE");
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

                    tableReport += $"   {columnName,-25} {dataType}{lengthStr,-15} {nullableStr}\n";
                }

                tableReport += $"   Total: {columnCount} colonnes\n";
                return tableReport;
            }
            catch (Exception ex)
            {
                return $"❌ Erreur analyse table {tableName}: {ex.Message}\n";
            }
        }

        // Méthodes utilitaires pour la lecture sécurisée des données

        private static string SafeGetString(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.IsDBNull(columnName) ? "" : reader.GetString(columnName).Trim();
            }
            catch
            {
                return "";
            }
        }

        private static int SafeGetInt(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.IsDBNull(columnName) ? 0 : reader.GetInt32(columnName);
            }
            catch
            {
                return 0;
            }
        }

        private static long SafeGetLong(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.IsDBNull(columnName) ? 0 : reader.GetInt64(columnName);
            }
            catch
            {
                return 0;
            }
        }

        private static DateTime SafeGetDateTime(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.IsDBNull(columnName) ? DateTime.Now : reader.GetDateTime(columnName);
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static DateTime? SafeGetNullableDateTime(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.IsDBNull(columnName) ? null : reader.GetDateTime(columnName);
            }
            catch
            {
                return null;
            }
        }
    }
}