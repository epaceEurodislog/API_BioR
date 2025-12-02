// Fichier: Services/SimplePurchaseLogger.cs
// Logger simple pour tracer les Purchase Orders envoyées

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services
{
    /// <summary>
    /// Logger simple pour tracer les Purchase Orders confirmées
    /// </summary>
    public class SimplePurchaseLogger
    {
        private readonly ILogger<SimplePurchaseLogger> _logger;
        private readonly string _logFilePath;
        private static readonly object _fileLock = new object();

        public SimplePurchaseLogger(ILogger<SimplePurchaseLogger> logger)
        {
            _logger = logger;

            // Créer un fichier de log dans le dossier courant
            var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            _logFilePath = Path.Combine(logDirectory, "Purchase_Orders_Sent.log");

            // Créer le fichier avec header si nécessaire
            InitializeLogFile();
        }

        /// <summary>
        /// Initialise le fichier de log avec un header
        /// </summary>
        private void InitializeLogFile()
        {
            try
            {
                lock (_fileLock)
                {
                    if (!File.Exists(_logFilePath))
                    {
                        var header = new StringBuilder();
                        header.AppendLine("=".PadRight(100, '='));
                        header.AppendLine("PURCHASE ORDERS - LOG DES CONFIRMATIONS ENVOYÉES");
                        header.AppendLine("=".PadRight(100, '='));
                        header.AppendLine();
                        header.AppendLine($"Format: [Date/Heure] | PurchId | PurchTableVersion | Endpoint | Statut");
                        header.AppendLine();
                        header.AppendLine("-".PadRight(100, '-'));
                        header.AppendLine();

                        File.WriteAllText(_logFilePath, header.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de l'initialisation du fichier de log");
            }
        }

        /// <summary>
        /// Log une Purchase Order envoyée
        /// </summary>
        public void LogPurchaseOrderSent(
            string purchId,
            string purchTableVersion,
            string endpoint,
            bool success,
            string errorMessage = null)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var status = success ? "✅ SUCCESS" : "❌ FAILED";

                var logLine = new StringBuilder();
                logLine.Append($"[{timestamp}] | ");
                logLine.Append($"{purchId,-15} | ");
                logLine.Append($"{purchTableVersion,-15} | ");
                logLine.Append($"{status}");

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    logLine.Append($" | Erreur: {errorMessage}");
                }

                logLine.AppendLine();

                lock (_fileLock)
                {
                    File.AppendAllText(_logFilePath, logLine.ToString());
                }

                _logger.LogDebug($"📝 Purchase Order {purchId} loggée dans {Path.GetFileName(_logFilePath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors du logging de Purchase Order {purchId}");
            }
        }

        /// <summary>
        /// Log avec détails complets (optionnel)
        /// </summary>
        public void LogPurchaseOrderDetails(
            string purchId,
            string purchTableVersion,
            string payloadJson,
            bool success,
            string responseContent = null)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var status = success ? "✅ SUCCESS" : "❌ FAILED";

                var logEntry = new StringBuilder();
                logEntry.AppendLine();
                logEntry.AppendLine($"{'=',-100}".PadRight(100, '='));
                logEntry.AppendLine($"[{timestamp}] Purchase Order: {purchId}");
                logEntry.AppendLine($"{'=',-100}".PadRight(100, '='));
                logEntry.AppendLine($"PurchTableVersion: {purchTableVersion}");
                logEntry.AppendLine($"Statut: {status}");
                logEntry.AppendLine();
                logEntry.AppendLine("Payload envoyé:");
                logEntry.AppendLine(payloadJson);

                if (!string.IsNullOrEmpty(responseContent))
                {
                    logEntry.AppendLine();
                    logEntry.AppendLine("Réponse:");
                    logEntry.AppendLine(responseContent);
                }

                logEntry.AppendLine();

                lock (_fileLock)
                {
                    File.AppendAllText(_logFilePath, logEntry.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors du logging détaillé de Purchase Order {purchId}");
            }
        }

        /// <summary>
        /// Génère un rapport quotidien
        /// </summary>
        public string GenerateDailySummary()
        {
            try
            {
                if (!File.Exists(_logFilePath))
                {
                    return "Aucun log disponible.";
                }

                var lines = File.ReadAllLines(_logFilePath);
                var today = DateTime.Now.Date;

                int totalToday = 0;
                int successToday = 0;
                int failedToday = 0;
                var purchaseIds = new System.Collections.Generic.List<string>();

                foreach (var line in lines)
                {
                    if (line.Contains("[" + today.ToString("yyyy-MM-dd")))
                    {
                        totalToday++;

                        if (line.Contains("✅ SUCCESS"))
                        {
                            successToday++;
                        }
                        else if (line.Contains("❌ FAILED"))
                        {
                            failedToday++;
                        }

                        // Extraire le PurchId
                        var parts = line.Split('|');
                        if (parts.Length > 1)
                        {
                            var purchId = parts[1].Trim();
                            if (!string.IsNullOrEmpty(purchId))
                            {
                                purchaseIds.Add(purchId);
                            }
                        }
                    }
                }

                var summary = new StringBuilder();
                summary.AppendLine($"\n📊 RAPPORT QUOTIDIEN - {today:yyyy-MM-dd}");
                summary.AppendLine($"{'=',-50}".PadRight(50, '='));
                summary.AppendLine($"Total Purchase Orders envoyées: {totalToday}");
                summary.AppendLine($"✅ Succès: {successToday}");
                summary.AppendLine($"❌ Échecs: {failedToday}");
                summary.AppendLine($"📈 Taux de succès: {(totalToday > 0 ? (double)successToday / totalToday * 100 : 0):F1}%");
                summary.AppendLine();
                summary.AppendLine("Purchase Orders traitées aujourd'hui:");
                foreach (var purchId in purchaseIds)
                {
                    summary.AppendLine($"  - {purchId}");
                }

                return summary.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la génération du rapport quotidien");
                return "Erreur lors de la génération du rapport.";
            }
        }

        /// <summary>
        /// Vérifie si une Purchase Order a été envoyée
        /// </summary>
        public bool WasPurchaseOrderSent(string purchId)
        {
            try
            {
                if (!File.Exists(_logFilePath))
                {
                    return false;
                }

                var content = File.ReadAllText(_logFilePath);
                return content.Contains($"| {purchId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erreur lors de la vérification de Purchase Order {purchId}");
                return false;
            }
        }

        /// <summary>
        /// Récupère toutes les Purchase Orders envoyées aujourd'hui
        /// </summary>
        public System.Collections.Generic.List<string> GetTodayPurchaseOrders()
        {
            var result = new System.Collections.Generic.List<string>();

            try
            {
                if (!File.Exists(_logFilePath))
                {
                    return result;
                }

                var lines = File.ReadAllLines(_logFilePath);
                var today = DateTime.Now.Date.ToString("yyyy-MM-dd");

                foreach (var line in lines)
                {
                    if (line.Contains($"[{today}"))
                    {
                        var parts = line.Split('|');
                        if (parts.Length > 1)
                        {
                            var purchId = parts[1].Trim();
                            if (!string.IsNullOrEmpty(purchId) && !result.Contains(purchId))
                            {
                                result.Add(purchId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la récupération des Purchase Orders d'aujourd'hui");
            }

            return result;
        }

        /// <summary>
        /// Obtient le chemin du fichier de log
        /// </summary>
        public string GetLogFilePath()
        {
            return _logFilePath;
        }
    }
}