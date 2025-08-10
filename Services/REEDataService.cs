// Fichier: Services/REEDataService.cs
// Service d'accès aux données REE_DAT pour les journaux de réception
// VERSION FINALE COMPLÈTE avec les VRAIS noms de colonnes SpeedWMS

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
    /// AVEC LES VRAIS NOMS DE COLONNES SpeedWMS
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
        public async Task<List<ItemArrivalJournalData>> GetPendingJournalsAsync()
        {
            var journals = new List<ItemArrivalJournalData>();

            using (var connection = new SqlConnection(_speedWmsConnectionString))
            {
                try
                {
                    // MODIFICATION RD 06/08/2025
                    //await connection.OpenAsync();

                    _logger.LogInformation("🔍 Récupération des journaux de réception en attente...");
                    const string sql = @"
                        SELECT
                            REE.REE_NORE,                      -- PackingSlipId
                            --REL.REA_RFCE,                      -- DefaultTransactionReferenceNumber (était MVT.REA_RFTI)
                            case 
		                        when 
                                    (
                                        select 
                                            rea_rfti 
                                        from 
                                            REA_DAT 
                                        where 1=1	
                                            and act_code = REE.ACT_CODE 
                                            and REA_RFCE = REL.rea_rfce 
                                            and ART_CODE = REL.art_code 
                                            and REA_RFCL = REL.REA_RFCL
                                    ) like '%SO%'
			                    then 
                                    (
                                        case 
                                            when 
                                                CHARINDEX('-',REL.REA_RFCE) > 0 
                                            then 
                                                SUBSTRING(REL.REA_RFCE,1,CHARINDEX('-',REL.REA_RFCE)) else REL.REA_RFCE 
                                        end
                                    )
		                        else 
                                    (
                                        select 
                                            rea_rfti 
                                        from 
                                            REA_DAT 
                                        where 1=1	
                                            and act_code = REE.ACT_CODE 
                                            and REA_RFCE = REL.rea_rfce 
                                            and ART_CODE = REL.art_code 
                                            and REA_RFCL = REL.REA_RFCL
                                    ) 
	                        end as REA_RFCE,                      -- DefaultTransactionReferenceNumber   
                            REE.REE_DARE,                      -- TransactionDate
                            REL.REL_NORL,                      -- LineNumber (était MVT.NoLR)
                            substring(REL.ART_CODE,3,len(REL.ART_CODE)) as ART_CODE,                      -- ItemNumber
                            REL.REL_QTRE,                      -- ItemQuantity (était ART_QTEUB)
                            coalesce(REL.REL_LOT1,''),         -- ItemBatchNumber
                            coalesce(REL.REL_DLUO,''),         -- ExpDate
                            REL.REL_LOT2,                      -- ItemSerialNumber
                            REE.REE_ETRE,                      -- Status
                            REE.ACT_CODE,                      -- ActivityCode
                            REE.REE_CCLI,                      -- DefaultReceivingWarehouseId
                            REL.QUA_CODE                       -- ReceivingInventoryStatusId
                        FROM 
                            REE_DAT REE
                        INNER JOIN 
                            REL_DAT REL ON REE.ACT_CODE = REL.ACT_CODE and REE.REE_NORE = REL.REE_NORE and REE.REE_KEYU = REL.REE_KEYU
                        WHERE 
                            1=1
                            AND REE.ACT_CODE = 'COSMETIQUE'
                            AND REE.REE_NORE IS NOT NULL
                            AND REE.REE_NORE != ''
                            AND REL.REA_RFCE IS NOT NULL
                            AND REL.REA_RFCE != ''
                            AND (
                                select rea_rfti 
                                from REA_DAT 
                                where 1=1	
                                and act_code = REE.ACT_CODE 
                                and REA_RFCE = REL.rea_rfce 
                                and ART_CODE = REL.art_code 
                                and REA_RFCL = REL.REA_RFCL
                            ) IS NOT NULL
			                AND (
                                select rea_rfti 
                                from REA_DAT 
                                where 1=1	
                                and act_code = REE.ACT_CODE 
                                and REA_RFCE = REL.rea_rfce 
                                and ART_CODE = REL.art_code 
                                and REA_RFCL = REL.REA_RFCL
                            ) != ''
                            AND REL.ART_CODE IS NOT NULL
                            AND REL.ART_CODE != ''
                            AND (
                                select rea_crqi 
                                from REA_DAT 
                                where 1=1	
                                and act_code = REE.ACT_CODE 
                                and REA_RFCE = REL.rea_rfce 
                                and ART_CODE = REL.art_code 
                                and REA_RFCL = REL.REA_RFCL
                            ) = 'Interface EUDL' --pour les tests
                            AND REE.REE_ETRE = '200' -- =seulement les réceptions cloturées
                            --AND REE.REE_NORE = 35
                        ORDER BY 
                            REE.REE_DARE DESC, REE.REE_NORE";

                    //MODIFICATION RD 06/08/2025
                    //using var command = new SqlCommand(sql, connection);

                    //AJOUT RD 06/08/2025
                    SqlCommand command = new SqlCommand(sql, connection);
                    
                    //AJOUT RD 06/08/2025
                    //_logger.LogInformation(_speedWmsConnectionString);
                    _logger.LogInformation($"Statut de la connexion avant Open : {connection.State}");
                    connection.Open();
                    _logger.LogInformation($"Statut de la connexion après Open : {connection.State}");

                    //MODIFICATION RD 06/08/2025
                    //using var reader = await command.ExecuteReaderAsync();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        _logger.LogInformation("✅ Requête exécutée avec succès, lecture des données...");

                        var journalDict = new Dictionary<string, ItemArrivalJournalData>();

                        while (reader.Read())
                        {

                            var packingSlipId = SafeGetInt(reader, "REE_NORE");          // PackingSlipId
                            var transactionRefNumber = SafeGetString(reader, "REA_RFCE");   // DefaultTransactionReferenceNumber
                            var lineNumber = SafeGetInt(reader, "REL_NORL");                // LineNumber

                            //MODIFICATION RD 06/08/2025
                            //if (int.IsNullOrEmpty(packingSlipId) || string.IsNullOrEmpty(transactionRefNumber))
                            if (packingSlipId == 0 || string.IsNullOrEmpty(transactionRefNumber))
                            {
                                continue;
                            }

                            var journalKey = $"{packingSlipId}_{transactionRefNumber}";

                            if (!journalDict.ContainsKey(journalKey))
                            {
                                var journal = new ItemArrivalJournalData
                                {
                                    PackingSlipId = Convert.ToString(packingSlipId),                                 // REE_NORE
                                    //MODIFICATION RD 08/08/2025
                                    //JournalNumber = await GenerateJournalNumberAsync(connection),       // JournalNumber
                                    JournalLog = await GenerateJournalNumberAsync(connection) + $"_{packingSlipId}", // JournalLog
                                    
                                    TransactionReferenceNumber = transactionRefNumber,             // REL.REA_RFCE
                                    TransactionDate = SafeGetDateTime(reader, "REE_DARE"),         // REE_DARE => dans les lignes
                                    DataAreaId = "BR",                                             // DataAreaId (fixe)
                                    JournalNameId = "ARR",                                         // JournalNameId (fixe)
                                    DefaultReceivingSiteId = "S01",                                // DefaultReceivingSiteId (fixe)

                                    //MODIFICATION RD 07/08/2025
                                    //DefaultReceivingWarehouseId = SafeGetString(reader, "REE_CCLI"), // REE_CCLI
                                    DefaultReceivingWarehouseId = "12",

                                    DefaultReceivingWarehouseLocationId = "RECNOLP"                // DefaultReceivingWarehouseLocationId (fixe)
                                };

                                journalDict[journalKey] = journal;
                            }

                            // Ajouter la ligne d'article
                            var line = new ItemArrivalJournalLine
                            {
                                LineNumber = lineNumber,                                           // REL_NORL
                                ItemNumber = SafeGetString(reader, "ART_CODE"),                   // ART_CODE
                                ItemQuantity = SafeGetLong(reader, "REL_QTRE"),                   // ART_QTEU
                                ItemBatchNumber = SafeGetString(reader, "REL_LOT1"),              // REL_LOT1
                                ExpDate = SafeGetNullableDateTime(reader, "REL_DLUO"),            // REL_DLUO
                                ItemSerialNumber = SafeGetString(reader, "REL_LOT2"),             // REL_LOT2
                                ReceivingInventoryStatusId = MapQualityCodeToInventoryStatus(
                                    SafeGetString(reader, "QUA_CODE"))                            // QUA_CODE
                            };

                            if (!string.IsNullOrEmpty(line.ItemNumber) && line.ItemQuantity > 0)
                            {
                                journalDict[journalKey].Lines.Add(line);
                            }

                        }

                        journals.AddRange(journalDict.Values);                        

                        // Supprimer les journaux sans lignes
                        journals.RemoveAll(j => j.Lines.Count == 0);

                        _logger.LogInformation($"✅ {journals.Count} journaux de réception récupérés");

                        return journals;
                    }

                    // var journalDict = new Dictionary<string, ItemArrivalJournalData>();

                    // while (await reader.ReadAsync())
                    // {
                    //     var packingSlipId = SafeGetString(reader, "REE_NORE");          // PackingSlipId
                    //     var transactionRefNumber = SafeGetString(reader, "REA_RFCE");   // DefaultTransactionReferenceNumber
                    //     var lineNumber = SafeGetInt(reader, "REL_NORL");                // LineNumber

                    //     if (string.IsNullOrEmpty(packingSlipId) || string.IsNullOrEmpty(transactionRefNumber))
                    //     {
                    //         continue;
                    //     }

                    //     var journalKey = $"{packingSlipId}_{transactionRefNumber}";

                    //     if (!journalDict.ContainsKey(journalKey))
                    //     {
                    //         var journal = new ItemArrivalJournalData
                    //         {
                    //             JournalNumber = await GenerateJournalNumberAsync(connection),  // JournalNumber
                    //             PackingSlipId = packingSlipId,                                 // REE_NORE
                    //             TransactionReferenceNumber = transactionRefNumber,             // REL.REA_RFCE
                    //             TransactionDate = SafeGetDateTime(reader, "REE_DARE"),         // REE_DARE
                    //             DataAreaId = "BR",                                             // DataAreaId (fixe)
                    //             JournalNameId = "ARR",                                         // JournalNameId (fixe)
                    //             DefaultReceivingSiteId = "S01",                                // DefaultReceivingSiteId (fixe)
                    //             DefaultReceivingWarehouseId = SafeGetString(reader, "REE_CCLI"), // REE_CCLI
                    //             DefaultReceivingWarehouseLocationId = "RECNOLP"                // DefaultReceivingWarehouseLocationId (fixe)
                    //         };

                    //         journalDict[journalKey] = journal;
                    //     }

                    //     // Ajouter la ligne d'article
                    //     var line = new ItemArrivalJournalLine
                    //     {
                    //         LineNumber = lineNumber,                                           // REL_NORL
                    //         ItemNumber = SafeGetString(reader, "ART_CODE"),                   // ART_CODE
                    //         ItemQuantity = SafeGetLong(reader, "ART_QTEU"),                   // ART_QTEU
                    //         ItemBatchNumber = SafeGetString(reader, "REL_LOT1"),              // REL_LOT1
                    //         ExpDate = SafeGetNullableDateTime(reader, "REL_DLUO"),            // REL_DLUO
                    //         ItemSerialNumber = SafeGetString(reader, "REL_LOT2"),             // REL_LOT2
                    //         ReceivingInventoryStatusId = MapQualityCodeToInventoryStatus(
                    //             SafeGetString(reader, "QUA_CODE"))                            // QUA_CODE
                    //     };

                    //     if (!string.IsNullOrEmpty(line.ItemNumber) && line.ItemQuantity > 0)
                    //     {
                    //         journalDict[journalKey].Lines.Add(line);
                    //     }
                    // }

                    // journals.AddRange(journalDict.Values);

                    // // Supprimer les journaux sans lignes
                    // journals.RemoveAll(j => j.Lines.Count == 0);

                    // _logger.LogInformation($"✅ {journals.Count} journaux de réception récupérés");

                    // return journals;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Erreur lors de la récupération des journaux REE_DAT");
                    return journals;
                }
            }
        }

        /// <summary>
        /// ✅ Mapping du code qualité SpeedWMS vers le statut d'inventaire Dynamics
        /// </summary>
        private string MapQualityCodeToInventoryStatus(string qualityCode)
        {
            //MODIFICATION RD 07/08/2025
            // return qualityCode?.ToUpper() switch
            // {
            //     "OK" => "Available",
            //     "NOK" => "Blocked",
            //     "QUAR" => "QuarantineOrdered",
            //     "QUARANTINE" => "QuarantineOrdered",
            //     "BLOCKED" => "Blocked",
            //     _ => "Available" // Par défaut
            // };
            return qualityCode?.ToUpper() switch
            {
                "STD" => "Disponible",
                "NOK" => "Bloque3PL",
                "QUAR" => "QuarantineOrdered",
                "QUARANTINE" => "QuarantineOrdered",
                "BLOCKED" => "Bloque3PL",
                _ => "Disponible" // Par défaut
            };
        }

        /// <summary>
        /// ✅ Génère un numéro de journal unique
        /// </summary>
         private async Task<string> GenerateJournalNumberAsync(SqlConnection connection)
         {
             try
             {
                 var datePrefix = DateTime.Now.ToString("yyyyMM");
                 var journalNumber = $"JA{datePrefix}{DateTime.Now:ddHHmmss}";

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
        /// ✅ Analyse la structure des tables avec les VRAIS noms de colonnes
        /// </summary>
        public async Task<string> AnalyzeREEStructureAsync()
        {
            try
            {
                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "📊 === ANALYSE STRUCTURE REE/MVT/REL === 📊\n\n";

                //MODIFICATION RD 06/08/2025
                //var tablesToTest = new[] { "REE_DAT", "MVT_DAT", "REL_DAT" };
                var tablesToTest = new[] { "REE_DAT", "REL_DAT" };

                foreach (var tableName in tablesToTest)
                {
                    report += await GetTableStructureAsync(connection, tableName);
                    report += "\n";
                }

                // ✅ TEST AVEC LES VRAIS NOMS DE COLONNES
                report += "🔍 TEST DONNÉES AVEC VRAIS NOMS DE COLONNES:\n";
                try
                {
                    //MODIFICATION RD 06/08/2025
                    // const string testSql = @"
                    //     SELECT TOP 3
                    //         REE.REE_NORE as PackingSlipId,
                    //         REE.REE_DARE as TransactionDate,
                    //         REE.REE_ETRE as Status,
                    //         REE.QUA_CODE as QualityCode,
                    //         MVT.REA_RFTI as TransactionRef,
                    //         MVT.NoLR as LineNumber
                    //     FROM REE_DAT REE
                    //     LEFT JOIN MVT_DAT MVT ON REE.REE_KEYU = MVT.REE_KEYU
                    //     WHERE REE.REE_NORE IS NOT NULL
                    //     ORDER BY REE.REE_KEYU DESC";

                    const string testSql = @"
                        SELECT TOP 3
                            REE.REE_NORE as PackingSlipId,
                            REE.REE_DARE as TransactionDate,
                            REE.REE_ETRE as Status,
                            REL.QUA_CODE as QualityCode,
                            REL.REA_RFCE as TransactionRef,
                            REL.REL_NORL as LineNumber
                        FROM 
                            REE_DAT REE
                        LEFT OUTER JOIN 
                            REL_DAT REL ON REE.ACT_CODE = REL.ACT_CODE and REE.REE_NORE = REL.REE_NORE and REE.REE_KEYU = REL.REE_KEYU
                        WHERE 1=1
                            AND REE.ACT_CODE = 'COSMETIQUE'
                            AND REE.REE_NORE IS NOT NULL
                        ORDER BY 
                            REE.REE_KEYU DESC";

                    using var testCommand = new SqlCommand(testSql, connection);
                    using var testReader = await testCommand.ExecuteReaderAsync();

                    while (await testReader.ReadAsync())
                    {
                        var packingSlip = SafeGetInt(testReader, "PackingSlipId");
                        var transactionRef = SafeGetString(testReader, "TransactionRef");
                        var status = SafeGetString(testReader, "Status");
                        var qualityCode = SafeGetString(testReader, "QualityCode");

                        report += $"   PackingSlip: {packingSlip} | TransactionRef: {transactionRef} | Status: {status} | Quality: {qualityCode}\n";
                    }

                    report += "✅ Test réussi avec les vrais noms de colonnes\n";
                }
                catch (Exception testEx)
                {
                    report += $"❌ Erreur test données: {testEx.Message}\n";
                }

                return report;
            }
            catch (Exception ex)
            {
                var errorReport = $"❌ Erreur analyse structure REE: {ex.Message}";
                _logger.LogError(ex, errorReport);
                return errorReport;
            }
        }

        /// <summary>
        /// ✅ Diagnostic des tables REE avec exemples de données réelles
        /// </summary>
        public async Task<string> DiagnoseREETablesAsync()
        {
            try
            {
                using var connection = new SqlConnection(_speedWmsConnectionString);
                await connection.OpenAsync();

                var report = "🔍 === DIAGNOSTIC TABLES REE === 🔍\n\n";

                // ✅ Test jointure complète avec les vrais noms
                try
                {
                    //MODIFICATION RD 06/08/2025
                    // const string joinTestSql = @"
                    //     SELECT TOP 5
                    //         REE.REE_KEYU,
                    //         REE.REE_NORE as PackingSlipId,
                    //         REE.REE_DARE as TransactionDate,
                    //         REE.QUA_CODE as QualityCode,
                    //         MVT.REA_RFCE as DefaultTransactionRef,
                    //         MVT.REA_RFTI as TransactionRef,
                    //         MVT.NoLR as LineNumber,
                    //         REL.ART_CODE as ItemCode,
                    //         REL.ART_QTEU as ItemQuantity
                    //     FROM REE_DAT REE
                    //     LEFT JOIN MVT_DAT MVT ON REE.REE_KEYU = MVT.REE_KEYU
                    //     LEFT JOIN REL_DAT REL ON MVT.NoLR = REL.NoLR
                    //     WHERE REE.REE_NORE IS NOT NULL
                    //     ORDER BY REE.REE_KEYU DESC";

                    const string joinTestSql = @"
                        SELECT TOP 5
                            REE.REE_KEYU,
                            REE.REE_NORE as PackingSlipId,
                            REE.REE_DARE as TransactionDate,
                            REL.QUA_CODE as QualityCode,
                            REL.REA_RFCE as DefaultTransactionRef,
                            REL.REL_NoLR as LineNumber,
                            REL.ART_CODE as ItemCode,
                            REL.REL_QTRE as ItemQuantity
                        FROM 
                            REE_DAT REE
                        LEFT OUTER JOIN 
                            REL_DAT REL ON REE.ACT_CODE = REL.ACT_CODE and REE.REE_NORE = REL.REE_NORE and REE.REE_KEYU = REL.REE_KEYU
						WHERE 1=1
						    AND REE.ACT_CODE = 'COSMETIQUE'
						    AND REE.REE_NORE IS NOT NULL
                        ORDER BY 
                            REE.REE_KEYU DESC";

                    using var joinCommand = new SqlCommand(joinTestSql, connection);
                    using var joinReader = await joinCommand.ExecuteReaderAsync();

                    report += "✅ Test jointure REE_DAT -> REL_DAT:\n";

                    while (await joinReader.ReadAsync())
                    {
                        var reeKeyu = SafeGetInt(joinReader, "REE_KEYU");
                        var packingSlip = SafeGetInt(joinReader, "PackingSlipId");

                        //MODIFICATION RD 06/08/2025
                        //var transactionRef = SafeGetInt(joinReader, "TransactionRef");
                        var transactionRef = SafeGetString(joinReader, "DefaultTransactionRef");

                        var itemCode = SafeGetString(joinReader, "ItemCode");
                        var quantity = SafeGetLong(joinReader, "ItemQuantity");

                        //MODIFICATION RD 06/08/2025
                        // report += $"   REE_KEYU={reeKeyu}, PackingSlip={packingSlip}, TransactionRef={transactionRef}, " +
                        //          $"Item={itemCode}, Qty={quantity}\n";
                        report += $"   REE_KEYU={reeKeyu}, PackingSlip={packingSlip}, DefaultTransactionRef={transactionRef}, " +
                                    $"Item={itemCode}, Qty={quantity}\n";
                    }

                    report += "✅ Test jointure réussi\n";
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
                _logger.LogError(ex, errorReport);
                return errorReport;
            }
        }

        // ==========================================
        // MÉTHODES UTILITAIRES POUR LECTURE SÉCURISÉE
        // ==========================================

        private static string SafeGetString(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.IsDBNull(columnName) ? "" : reader.GetString(columnName);
            }
            catch
            {
                return "";
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

        private static int SafeGetInt(SqlDataReader reader, string columnName)
        {
            try
            {
                if (reader.IsDBNull(columnName)) return 0;
                var value = reader[columnName];
                if (value is int intVal) return intVal;
                if (int.TryParse(value.ToString(), out var parsedVal)) return parsedVal;
                return 0;
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
                if (reader.IsDBNull(columnName)) return 0;
                var value = reader[columnName];
                if (value is long longVal) return longVal;
                if (value is int intVal) return intVal;
                if (value is decimal decVal) return (long)decVal;
                if (long.TryParse(value.ToString(), out var parsedVal)) return parsedVal;
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<string> GetTableStructureAsync(SqlConnection connection, string tableName)
        {
            try
            {
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

                const string columnsSql = @"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
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

                    tableReport += $"   {columnName,-25} {dataType,-15} {isNullable}\n";
                }

                tableReport += $"   Total: {columnCount} colonnes\n";
                return tableReport;
            }
            catch (Exception ex)
            {
                return $"❌ Erreur analyse table {tableName}: {ex.Message}\n";
            }
        }
    }
}