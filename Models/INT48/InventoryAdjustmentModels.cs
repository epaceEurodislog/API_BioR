// Fichier: Models/INT48/InventoryAdjustmentModels.cs
// Modèles pour l'intégration INT48 - Ajustements d'inventaire Dynamics 365
// VERSION COMPLÈTE - Basée sur MVT_DAT SpeedWMS

using System;
using System.Collections.Generic;

namespace DynamicsApiToDatabase.Models.INT48
{
    /// <summary>
    /// Requête d'ajustement d'inventaire vers Dynamics 365
    /// Format attendu par l'endpoint bulk adjustment
    /// </summary>
    public class InventoryAdjustmentRequest
    {
        public string Id { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public InventoryDimensions Dimensions { get; set; } = new();
        public string QuantityDataSource { get; set; } = "@iv";
        public string PhysicalMeasure { get; set; } = "adjustment";
        public decimal Quantity { get; set; }
        public string ExternalReferenceCategory { get; set; } = string.Empty;
        public string ExternalReferenceId { get; set; } = string.Empty;
        public string TransDate { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dimensions d'inventaire pour localiser l'article
    /// </summary>
    public class InventoryDimensions
    {
        public string SiteId { get; set; } = string.Empty;
        public string LocationId { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string WMSLocationId { get; set; } = string.Empty;
        public string SerialId { get; set; } = string.Empty;
        public string StatusId { get; set; } = string.Empty;
        public string ExtendedDimension1 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mouvement d'inventaire depuis SpeedWMS (table MVT_DAT)
    /// Inclut les transformations métier de la requête SQL
    /// </summary>
    public class SpeedWmsInventoryMovement
    {
        public string MVT_KEYU { get; set; } = string.Empty;
        public string ART_CODE { get; set; } = string.Empty;
        public decimal MVT_QTE { get; set; }
        public string? STK_LOT1 { get; set; }
        public string? STK_LOT2 { get; set; }
        public string? TMV_CODE { get; set; }
        public string? MVT_REF { get; set; }
        public string? MOT_CODE { get; set; }
        public string? MVT_CRDA { get; set; }
        public string? QUA_CODE { get; set; }      // Code qualité/statut (Disponible, Bloque3PL, etc.)
        public DateTime? STK_DLU { get; set; }
        public string? MVT_MOT { get; set; }
    }

    /// <summary>
    /// Statistiques de traitement des ajustements d'inventaire
    /// </summary>
    public class InventoryAdjustmentStatistics
    {
        public int TotalMovementsFound { get; set; }
        public int MovementsProcessedSuccessfully { get; set; }
        public int MovementsWithErrors { get; set; }
        public int TotalPayloadsSent { get; set; }
        public int BatchesSent { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        
        public double SuccessRate => TotalMovementsFound > 0 
            ? (double)MovementsProcessedSuccessfully / TotalMovementsFound * 100 
            : 0;
    }

    /// <summary>
    /// Réponse de traitement d'un batch d'ajustements
    /// </summary>
    public class InventoryAdjustmentBatchResponse
    {
        public bool Success { get; set; }
        public List<string> ProcessedIds { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public int TotalProcessed { get; set; }
        public int TotalErrors { get; set; }
    }
}