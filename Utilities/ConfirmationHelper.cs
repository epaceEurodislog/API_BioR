using DynamicsApiToDatabase.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Utilities
{
    /// <summary>
    /// Classe helper pour la confirmation de réception d'articles et commandes
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

        // ==========================================
        // MÉTHODES POUR COMMANDES INDIVIDUELLES
        // ==========================================

        /// <summary>
        /// Confirme une Purchase Order avec mise à jour INT3PLStatus
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="purchaseOrderId">ID de la Purchase Order</param>
        /// <param name="int3plStatus">Statut INT3PL à appliquer (défaut: "Processed")</param>
        /// <returns>True si la confirmation a réussi</returns>
        public static async Task<bool> ConfirmPurchaseOrderAsync(IServiceProvider serviceProvider, string purchaseOrderId, string int3plStatus = "Processed")
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusConfirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await statusConfirmationService.ConfirmPurchaseOrderWithStatusUpdateAsync(token, purchaseOrderId, int3plStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation Purchase Order {purchaseOrderId}");
                return false;
            }
        }

        /// <summary>
        /// Confirme une Return Order avec mise à jour INT3PLStatus
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="returnOrderId">ID de la Return Order</param>
        /// <param name="int3plStatus">Statut INT3PL à appliquer (défaut: "Processed")</param>
        /// <returns>True si la confirmation a réussi</returns>
        public static async Task<bool> ConfirmReturnOrderAsync(IServiceProvider serviceProvider, string returnOrderId, string int3plStatus = "Processed")
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusConfirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await statusConfirmationService.ConfirmReturnOrderWithStatusUpdateAsync(token, returnOrderId, int3plStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation Return Order {returnOrderId}");
                return false;
            }
        }

        /// <summary>
        /// Confirme une Transfer Order avec mise à jour INT3PLStatus
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="transferOrderId">ID de la Transfer Order</param>
        /// <param name="int3plStatus">Statut INT3PL à appliquer (défaut: "Processed")</param>
        /// <returns>True si la confirmation a réussi</returns>
        public static async Task<bool> ConfirmTransferOrderAsync(IServiceProvider serviceProvider, string transferOrderId, string int3plStatus = "Processed")
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusConfirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await statusConfirmationService.ConfirmTransferOrderWithStatusUpdateAsync(token, transferOrderId, int3plStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation Transfer Order {transferOrderId}");
                return false;
            }
        }

        /// <summary>
        /// Confirme une Sales Order avec mise à jour INT3PLStatus
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="salesOrderId">ID de la Sales Order</param>
        /// <param name="int3plStatus">Statut INT3PL à appliquer (défaut: "Processed")</param>
        /// <returns>True si la confirmation a réussi</returns>
        public static async Task<bool> ConfirmSalesOrderAsync(IServiceProvider serviceProvider, string salesOrderId, string int3plStatus = "Processed")
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusConfirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                return await statusConfirmationService.ConfirmSalesOrderWithStatusUpdateAsync(token, salesOrderId, int3plStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation Sales Order {salesOrderId}");
                return false;
            }
        }

        // ==========================================
        // MÉTHODES POUR CONFIRMATIONS MULTIPLES
        // ==========================================

        /// <summary>
        /// Confirme plusieurs commandes d'un type spécifique
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="orderType">Type de commande ("Purchase", "Return", "Transfer", "Sales")</param>
        /// <param name="orderIds">Liste des IDs de commandes</param>
        /// <param name="int3plStatus">Statut INT3PL à appliquer</param>
        /// <returns>Nombre de commandes confirmées avec succès</returns>
        public static async Task<int> ConfirmMultipleOrdersAsync(IServiceProvider serviceProvider, string orderType, List<string> orderIds, string int3plStatus = "Processed")
        {
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var statusConfirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return 0;
                }

                return orderType.ToUpper() switch
                {
                    "PURCHASE" => await statusConfirmationService.ConfirmMultiplePurchaseOrdersWithStatusAsync(token, orderIds, int3plStatus),
                    "RETURN" => await statusConfirmationService.ConfirmMultipleReturnOrdersWithStatusAsync(token, orderIds, int3plStatus),
                    "TRANSFER" => await statusConfirmationService.ConfirmMultipleTransferOrdersWithStatusAsync(token, orderIds, int3plStatus),
                    "SALES" => await statusConfirmationService.ConfirmMultipleSalesOrdersWithStatusAsync(token, orderIds, int3plStatus),
                    _ => throw new ArgumentException($"Type de commande non reconnu: {orderType}")
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation multiple {orderType}");
                return 0;
            }
        }

        /// <summary>
        /// Confirme toutes les commandes actives d'un type spécifique
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="orderType">Type de commande ("Purchase", "Return", "Transfer", "Sales")</param>
        /// <param name="int3plStatus">Statut INT3PL à appliquer</param>
        /// <returns>Nombre de commandes confirmées avec succès</returns>
        public static async Task<int> ConfirmAllActiveOrdersAsync(IServiceProvider serviceProvider, string orderType, string int3plStatus = "Processed")
        {
            var sqlServerService = serviceProvider.GetRequiredService<SqlServerDatabaseService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                var orderIds = orderType.ToUpper() switch
                {
                    "PURCHASE" => await sqlServerService.GetActivePurchaseOrderIdsAsync(),
                    "RETURN" => await sqlServerService.GetActiveReturnOrderIdsAsync(),
                    "TRANSFER" => await sqlServerService.GetActiveTransferOrderIdsAsync(),
                    "SALES" => await sqlServerService.GetActiveSalesOrderIdsAsync(),
                    _ => throw new ArgumentException($"Type de commande non reconnu: {orderType}")
                };

                if (orderIds.Count == 0)
                {
                    logger.LogInformation($"Aucune commande {orderType} active trouvée");
                    return 0;
                }

                logger.LogInformation($"📤 Confirmation de {orderIds.Count} commandes {orderType} actives...");

                return await ConfirmMultipleOrdersAsync(serviceProvider, orderType, orderIds, int3plStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Erreur lors de la confirmation des commandes {orderType} actives");
                return 0;
            }
        }

        /// <summary>
        /// Confirme toutes les commandes actives (tous types) avec rapport détaillé
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="int3plStatus">Statut INT3PL à appliquer</param>
        /// <returns>Dictionnaire avec les résultats par type de commande</returns>
        public static async Task<Dictionary<string, int>> ConfirmAllActiveOrdersWithReportAsync(IServiceProvider serviceProvider, string int3plStatus = "Processed")
        {
            var results = new Dictionary<string, int>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                logger.LogInformation("🚀 Début confirmation de toutes les commandes actives...");

                // Confirmer les Purchase Orders
                var purchaseCount = await ConfirmAllActiveOrdersAsync(serviceProvider, "Purchase", int3plStatus);
                results["Purchase"] = purchaseCount;

                await Task.Delay(1000); // Pause entre les types

                // Confirmer les Return Orders
                var returnCount = await ConfirmAllActiveOrdersAsync(serviceProvider, "Return", int3plStatus);
                results["Return"] = returnCount;

                await Task.Delay(1000); // Pause entre les types

                // Confirmer les Transfer Orders
                var transferCount = await ConfirmAllActiveOrdersAsync(serviceProvider, "Transfer", int3plStatus);
                results["Transfer"] = transferCount;

                await Task.Delay(1000); // Pause entre les types

                // Confirmer les Sales Orders
                var salesCount = await ConfirmAllActiveOrdersAsync(serviceProvider, "Sales", int3plStatus);
                results["Sales"] = salesCount;

                var totalConfirmed = purchaseCount + returnCount + transferCount + salesCount;
                logger.LogInformation($"🎯 Confirmation terminée: {totalConfirmed} commandes au total");

                return results;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la confirmation globale des commandes");
                return results;
            }
        }

        // ==========================================
        // MÉTHODES DE TEST ET DIAGNOSTIC
        // ==========================================

        /// <summary>
        /// Teste la confirmation d'une commande spécifique pour diagnostic
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="orderType">Type de commande</param>
        /// <param name="orderId">ID de la commande</param>
        /// <param name="int3plStatus">Statut INT3PL</param>
        /// <returns>True si le test a réussi</returns>
        public static async Task<bool> TestSingleOrderConfirmationAsync(IServiceProvider serviceProvider, string orderType, string orderId, string int3plStatus = "Processed")
        {
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                logger.LogInformation($"🧪 Test de confirmation {orderType} Order: {orderId}");

                var success = orderType.ToUpper() switch
                {
                    "PURCHASE" => await ConfirmPurchaseOrderAsync(serviceProvider, orderId, int3plStatus),
                    "RETURN" => await ConfirmReturnOrderAsync(serviceProvider, orderId, int3plStatus),
                    "TRANSFER" => await ConfirmTransferOrderAsync(serviceProvider, orderId, int3plStatus),
                    "SALES" => await ConfirmSalesOrderAsync(serviceProvider, orderId, int3plStatus),
                    _ => throw new ArgumentException($"Type de commande non reconnu: {orderType}")
                };

                var result = success ? "✅ SUCCÈS" : "❌ ÉCHEC";
                logger.LogInformation($"🧪 Test {orderType} {orderId}: {result}");

                return success;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"❌ Erreur lors du test {orderType} {orderId}");
                return false;
            }
        }

        /// <summary>
        /// Valide les endpoints de confirmation avant utilisation
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <returns>True si tous les endpoints sont accessibles</returns>
        public static async Task<bool> ValidateConfirmationEndpointsAsync(IServiceProvider serviceProvider)
        {
            var statusConfirmationService = serviceProvider.GetRequiredService<StatusConfirmationService>();
            var authService = serviceProvider.GetRequiredService<AuthenticationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<StatusConfirmationService>>();

            try
            {
                logger.LogInformation("🔍 Validation des endpoints de confirmation...");

                var token = await authService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogError("Impossible d'obtenir le token d'authentification");
                    return false;
                }

                var isValid = await statusConfirmationService.TestApiConnectivityAsync(token);

                if (isValid)
                {
                    logger.LogInformation("✅ Tous les endpoints de confirmation sont accessibles");
                }
                else
                {
                    logger.LogError("❌ Problème d'accès aux endpoints de confirmation");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Erreur lors de la validation des endpoints");
                return false;
            }
        }

        // ==========================================
        // MÉTHODES POUR ARTICLES
        // ==========================================

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
                var itemIds = await sqlServerService.GetArticleIdsFromDateAsync(fromDate);

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

        // ==========================================
        // MÉTHODES D'AFFICHAGE ET RAPPORTS
        // ==========================================

        /// <summary>
        /// Affiche un rapport détaillé des confirmations de commandes
        /// </summary>
        /// <param name="results">Résultats des confirmations par type</param>
        public static void DisplayConfirmationReport(Dictionary<string, int> results)
        {
            Console.WriteLine("\n📊 === RAPPORT DE CONFIRMATION DES COMMANDES === 📊");

            var totalConfirmed = results.Values.Sum();
            var totalTypes = results.Count;

            Console.WriteLine($"🎯 Résumé global: {totalConfirmed} commandes confirmées sur {totalTypes} types");
            Console.WriteLine();

            foreach (var result in results)
            {
                var orderType = result.Key;
                var confirmedCount = result.Value;
                var icon = confirmedCount > 0 ? "✅" : "➖";

                Console.WriteLine($"{icon} {orderType} Orders:");
                Console.WriteLine($"   📤 Confirmées: {confirmedCount}");
                Console.WriteLine($"   🔄 Statut INT3PL: Processed");
                Console.WriteLine();
            }

            if (totalConfirmed > 0)
            {
                Console.WriteLine($"🎉 Toutes les confirmations terminées avec succès!");
            }
            else
            {
                Console.WriteLine("ℹ️ Aucune commande n'était en attente de confirmation");
            }
        }

        /// <summary>
        /// Affiche un rapport de confirmation d'articles
        /// </summary>
        /// <param name="confirmedCount">Nombre d'articles confirmés</param>
        /// <param name="totalCount">Nombre total d'articles</param>
        public static void DisplayItemConfirmationReport(int confirmedCount, int totalCount)
        {
            var successRate = totalCount > 0 ? (double)confirmedCount / totalCount * 100 : 0;

            Console.WriteLine($"\n📊 === RAPPORT DE CONFIRMATION ARTICLES === 📊");
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