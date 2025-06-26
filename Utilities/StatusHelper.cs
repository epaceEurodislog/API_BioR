// Fichier: Utilities/StatusHelper.cs
// Classe utilitaire pour faciliter la mise à jour des statuts

using DynamicsApiToDatabase.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Utilities
{
    /// <summary>
    /// Classe helper pour la mise à jour des statuts d'articles
    /// </summary>
    public static class StatusHelper
    {
        /// <summary>
        /// Constantes pour les statuts les plus courants
        /// </summary>
        public static class StatusConstants
        {
            public const string None = "None";
            public const string Retrieved = "Récupéré";
            public const string Processed = "Traité";
            public const string InProgress = "En cours";
            public const string Error = "Erreur";
            public const string Shipped = "Expédié";
        }

        /// <summary>
        /// Méthode simple pour marquer un article comme récupéré
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="itemId">ID de l'article</param>
        /// <returns>True si la mise à jour a réussi</returns>
        public static async Task<bool> MarkArticleAsRetrievedAsync(IServiceProvider serviceProvider, string itemId)
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusService = serviceProvider.GetRequiredService<StatusUpdateService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusUpdateService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await statusService.UpdateArticleStatusAsync(token, itemId, StatusConstants.Retrieved);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la mise à jour du statut pour l'article {itemId}");
                return false;
            }
        }

        /// <summary>
        /// Méthode pour marquer plusieurs articles comme récupérés
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="itemIds">Liste des IDs d'articles</param>
        /// <returns>Nombre d'articles mis à jour avec succès</returns>
        public static async Task<int> MarkMultipleArticlesAsRetrievedAsync(IServiceProvider serviceProvider, List<string> itemIds)
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusService = serviceProvider.GetRequiredService<StatusUpdateService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusUpdateService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return 0;
                }

                var updates = itemIds.Select(id => new ArticleStatusUpdate
                {
                    ItemId = id,
                    NewStatus = StatusConstants.Retrieved
                }).ToList();

                return await statusService.UpdateMultipleArticleStatusAsync(token, updates);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la mise à jour multiple des statuts");
                return 0;
            }
        }

        /// <summary>
        /// Méthode générique pour mettre à jour le statut d'un article
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="itemId">ID de l'article</param>
        /// <param name="newStatus">Nouveau statut</param>
        /// <returns>True si la mise à jour a réussi</returns>
        public static async Task<bool> UpdateArticleStatusAsync(IServiceProvider serviceProvider, string itemId, string newStatus)
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusService = serviceProvider.GetRequiredService<StatusUpdateService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusUpdateService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await statusService.UpdateArticleStatusAsync(token, itemId, newStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la mise à jour du statut pour l'article {itemId}");
                return false;
            }
        }
    }
}