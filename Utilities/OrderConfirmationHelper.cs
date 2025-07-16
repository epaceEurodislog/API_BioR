// Fichier: Utilities/OrderConfirmationHelper.cs
// Classe utilitaire pour faciliter les confirmations de commandes

using DynamicsApiToDatabase.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Utilities
{
    /// <summary>
    /// Classe helper pour la confirmation des commandes Purchase/Return/Transfer
    /// </summary>
    public static class OrderConfirmationHelper
    {
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
        /// Confirme plusieurs commandes d'un type spécifique
        /// </summary>
        /// <param name="serviceProvider">Provider de services DI</param>
        /// <param name="orderType">Type de commande ("Purchase", "Return", "Transfer")</param>
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
        /// <param name="orderType">Type de commande ("Purchase", "Return", "Transfer")</param>
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

                var totalConfirmed = purchaseCount + returnCount + transferCount;
                logger.LogInformation($"🎯 Confirmation terminée: {totalConfirmed} commandes au total");

                return results;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la confirmation globale des commandes");
                return results;
            }
        }

        /// <summary>
        /// Affiche un rapport détaillé des confirmations
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
    }
}