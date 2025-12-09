// Fichier: Services/INT48/InventoryTransformationService.cs
// Service de transformation des données SpeedWMS vers Dynamics 365
// Mapping MVT_DAT -> InventoryAdjustmentRequest

using System;
using System.Collections.Generic;
using System.Linq;
using DynamicsApiToDatabase.Models.INT48;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynamicsApiToDatabase.Services.INT48
{
    public class InventoryTransformationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InventoryTransformationService> _logger;

        // Mapping des statuts de stock SpeedWMS vers Dynamics
        private readonly Dictionary<string, string> _statusMapping = new()
        {
            { "A1", "Disponible" },
            { "A4", "Disponible" },
            { "A7", "Disponible" },
            { "E1", "Disponible" },
            { "C2", "Quarantine" },
            { "C3", "Quarantine" },
            { "B0", "Bloqué" },
            { "B1", "Bloqué" },
            { "B2", "Bloqué" },
            { "B3", "Bloqué" }
        };

        public InventoryTransformationService(
            IConfiguration configuration, 
            ILogger<InventoryTransformationService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Transforme une liste de mouvements SpeedWMS en requêtes d'ajustement Dynamics
        /// </summary>
        public List<InventoryAdjustmentRequest> TransformMovementsToAdjustments(
            List<SpeedWmsInventoryMovement> movements)
        {
            _logger.LogInformation($"🔄 Transformation de {movements.Count} mouvements SpeedWMS");

            var adjustments = new List<InventoryAdjustmentRequest>();

            foreach (var movement in movements)
            {
                try
                {
                    var adjustment = TransformSingleMovement(movement);
                    if (adjustment != null)
                    {
                        adjustments.Add(adjustment);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Erreur transformation du mouvement {movement.MVT_KEYU}");
                }
            }

            _logger.LogInformation($"✅ {adjustments.Count} ajustements créés avec succès");
            return adjustments;
        }

        /// <summary>
        /// Transforme un mouvement individuel
        /// </summary>
        private InventoryAdjustmentRequest? TransformSingleMovement(SpeedWmsInventoryMovement movement)
        {
            // Validation de base
            if (string.IsNullOrEmpty(movement.ART_CODE))
            {
                _logger.LogWarning($"⚠️ MVT_KEYU {movement.MVT_KEYU}: ART_CODE manquant");
                return null;
            }

            // Utiliser le statut déjà calculé depuis la requête SQL
            string statusId = movement.QUA_CODE ?? "Disponible";

            // Utiliser la date déjà formatée depuis la requête SQL
            string transDate = movement.MVT_CRDA ?? DateTime.Now.ToString("yyyy-MM-dd");

            // Utiliser MVT_REF directement comme WMSLocationId (déjà "RECNOLP" depuis la requête)
            string wmsLocationId = movement.MVT_REF ?? "RECNOLP";

            // Utiliser MVT_MOT comme ExtendedDimension1 (déjà transformé depuis la requête)
            string extendedDimension1 = movement.MVT_MOT ?? GenerateExtendedDimension();

            var adjustment = new InventoryAdjustmentRequest
            {
                Id = movement.MVT_KEYU,
                ProductId = movement.ART_CODE,  // Déjà substring(3,35) depuis la requête
                OrganizationId = "BR",
                Dimensions = new InventoryDimensions
                {
                    SiteId = "S01",
                    LocationId = "12",
                    BatchId = movement.STK_LOT1 ?? "",      // STK_LOT1 = batchId
                    WMSLocationId = wmsLocationId,
                    SerialId = movement.STK_LOT2 ?? "",     // STK_LOT2 = serialId
                    StatusId = statusId,
                    ExtendedDimension1 = extendedDimension1
                },
                QuantityDataSource = "@iv",
                PhysicalMeasure = "adjustment",
                Quantity = movement.MVT_QTE,                // Déjà concat(sens,qty) depuis la requête
                ExternalReferenceCategory = movement.MOT_CODE ?? "",  // MOT_CODE transformé par CASE
                ExternalReferenceId = movement.TMV_CODE ?? "",        // TMV_CODE
                TransDate = transDate
            };

            _logger.LogDebug($"✅ Mouvement {movement.MVT_KEYU} transformé: " +
                           $"Article={adjustment.ProductId}, " +
                           $"Qty={adjustment.Quantity}, " +
                           $"Status={statusId}");

            return adjustment;
        }

        /// <summary>
        /// Mappe le statut de stock SpeedWMS vers Dynamics
        /// </summary>
        private string MapStockStatusToInventoryStatus(string? stkLot1)
        {
            if (string.IsNullOrEmpty(stkLot1))
                return "Disponible";

            return _statusMapping.TryGetValue(stkLot1, out var status) 
                ? status 
                : "Disponible";
        }

        /// <summary>
        /// Formate la date de transaction au format ISO 8601
        /// </summary>
        private string FormatTransactionDate(string? mvtCrda)
        {
            if (string.IsNullOrEmpty(mvtCrda))
                return DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                // Essayer de parser la date
                if (DateTime.TryParse(mvtCrda, out var date))
                {
                    return date.ToString("yyyy-MM-dd");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Date invalide '{mvtCrda}': {ex.Message}");
            }

            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Extrait l'emplacement WMS depuis la référence du mouvement
        /// </summary>
        private string ExtractWmsLocation(string? mvtRef)
        {
            if (string.IsNullOrEmpty(mvtRef))
                return "RECNOLP"; // Valeur par défaut

            // Le MVT_REF peut contenir directement l'emplacement comme "RECNOLP" ou "BR3R12512"
            return mvtRef;
        }

        /// <summary>
        /// Génère une dimension étendue unique
        /// Format: chaîne aléatoire de 50 caractères alphanumériques
        /// </summary>
        private string GenerateExtendedDimension()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 50)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}