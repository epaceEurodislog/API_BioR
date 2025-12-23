namespace DynamicsApiToDatabase.Models.INT39
{
    public class TrackingNumberModel
    {
        // Identifiants
        public string ACT_CODE { get; set; } = "COSMETIQUE";
        public string OPE_CCLI { get; set; } = "BR";
        public string OPE_REDO { get; set; } // Référence donneur ordre (BROrderId)
        public string OPE_KEYU { get; set; } // N° Expédition STACI (BR3PLPackingSlipId)
        
        // Statut et modifications
        public string OPE_STAT { get; set; } // Statut commande
        public DateTime? OPE_MODA { get; set; } // Date modification
        public TimeSpan? OPE_MOHE { get; set; } // Heure modification
        
        // Transport
        public string OPE_CTRA { get; set; } // Code transporteur (CarrierCode)
        public string SEX_URLT { get; set; } // URL Tracking (BRTrackingNumber)
        
        // Documentation
        public string OPE_TOP28 { get; set; } // Doc requise
        public string OPE_TOP22 { get; set; } // Doc reçu (BRDocStatus)
        public DateTime? OPE_DATEHEURE11 { get; set; } // Date/Heure doc reçu (BRDocStatusDate)
        
        // Support expédition
        public string SEX_SUPR { get; set; } // Support expédition/regroupement
        
        // Type de commande
        public string OrderType { get; set; } // "SALES" ou "TRANSFER"
    }
}