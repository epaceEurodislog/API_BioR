// Fichier: Utilities/ConfirmationHelper.cs
// Classe utilitaire pour faciliter les confirmations de réception

using DynamicsApiToDatabase.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Utilities
{
    /// <summary>
    /// Classe helper pour la confirmation de réception d'articles
    /// </summary>
    public static class ConfirmationHelper
    {
        /// <summary>
        /// Confirme la réception d'un seul article
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="itemId">ID de l'article</param>
        /// <returns>True si la confirmation a réussi</returns>
        public static async Task<bool> ConfirmSingleItemAsync(IServiceProvider serviceProvider, string itemId)
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var confirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await confirmationService.ConfirmItemReceivedAsync(token, itemId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation pour l'article {itemId}");
                return false;
            }
        }

        /// <summary>
        /// Confirme la réception de plusieurs articles
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="itemIds">Liste des IDs d'articles</param>
        /// <returns>Nombre d'articles confirmés avec succès</returns>
        public static async Task<int> ConfirmMultipleItemsAsync(IServiceProvider serviceProvider, List<string> itemIds)
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var confirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return 0;
                }

                return await confirmationService.ConfirmMultipleItemsReceivedAsync(token, itemIds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la confirmation multiple d'articles");
                return 0;
            }
        }

        /// <summary>
        /// Confirme la réception d'un article avec retry automatique
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="itemId">ID de l'article</param>
        /// <param name="maxRetries">Nombre maximum de tentatives</param>
        /// <returns>True si la confirmation a réussi</returns>
        public static async Task<bool> ConfirmSingleItemWithRetryAsync(IServiceProvider serviceProvider, string itemId, int maxRetries = 3)
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var confirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await confirmationService.ConfirmItemReceivedWithRetryAsync(token, itemId, maxRetries);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation avec retry pour l'article {itemId}");
                return false;
            }
        }

        /// <summary>
        /// Confirme tous les articles présents dans la base de données depuis une date donnée
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="fromDate">Date à partir de laquelle confirmer les articles</param>
        /// <returns>Nombre d'articles confirmés</returns>
        public static async Task<int> ConfirmAllItemsFromDateAsync(IServiceProvider serviceProvider, DateTime fromDate)
        {
            var sqlServerService = serviceProvider.GetRequiredService<SqlServerDatabaseService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                // Récupérer tous les articles depuis la date donnée
                var itemIds = await GetArticleIdsFromDateAsync(sqlServerService, fromDate);

                if (itemIds.Count == 0)
                {
                    logger.LogInformation($"Aucun article trouvé depuis le {fromDate:dd/MM/yyyy}");
                    return 0;
                }

                logger.LogInformation($"📤 Confirmation de {itemIds.Count} articles depuis le {fromDate:dd/MM/yyyy}");

                return await ConfirmMultipleItemsAsync(serviceProvider, itemIds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation des articles depuis le {fromDate:dd/MM/yyyy}");
                return 0;
            }
        }

        /// <summary>
        /// Récupère les IDs des articles depuis une date donnée
        /// </summary>
        private static async Task<List<string>> GetArticleIdsFromDateAsync(SqlServerDatabaseService sqlServerService, DateTime fromDate)
        {
            return await sqlServerService.GetArticleIdsFromDateAsync(fromDate);
        }

        /// <summary>
        /// Affiche un rapport de confirmation
        /// </summary>
        /// <param name="confirmedCount">Nombre d'articles confirmés</param>
        /// <param name="totalCount">Nombre total d'articles</param>
        public static void DisplayConfirmationReport(int confirmedCount, int totalCount)
        {
            var successRate = totalCount > 0 ? (double)confirmedCount / totalCount * 100 : 0;

            Console.WriteLine($"\n📊 === RAPPORT DE CONFIRMATION === 📊");
            Console.WriteLine($"✅ Articles confirmés: {confirmedCount}");
            Console.WriteLine($"📦 Total articles: {totalCount}");
            Console.WriteLine($"📈 Taux de succès: {successRate:F1}%");

            if (confirmedCount < totalCount)
            {
                Console.WriteLine($"⚠️ Échecs: {totalCount - confirmedCount} articles");
            }
        }
    }
}