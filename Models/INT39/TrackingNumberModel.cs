namespace DynamicsApiToDatabase.Models.INT39
{
    public class TrackingNumberModel
    {
        // Identifiants
        public string ACT_CODE { get; set; } = "COSMETIQUE";
        public string OPE_CCLI { get; set; } = "BR";
       public string OPE_REDO { get; set; }        // Référence donneur ordre (BROrderId)
        public string OPE_RTIE { get; set; }        // Référence tiers
        public string OPE_STAT { get; set; }        // Statut texte (EN SAISIE, VALIDEE, etc.)
        public string OPE_MODA { get; set; }        // Date modification (string depuis SQL)
        public string OPE_MOHE { get; set; }        // Heure modification (string depuis SQL)
        public string OPE_CTRA { get; set; }        // Code transporteur (CarrierCode)
        public string OPE_TOP28 { get; set; }       // Doc requise ('Oui'/'Non')
        public string OPE_TOP22 { get; set; }       // Doc reçu ('Oui'/'Non' -> BRDocStatus)
        public string OPE_DATETIME { get; set; }    // Date/Heure (concat de OPE_DATE1 + OPE_HEURE1)
        public string SEX_EXPE { get; set; }    // Support d'expédition (SEX_SUPR ou SEX_SUPE)
        public string SEX_TRAK { get; set; }        // Numéro de Tracking (BRTrackingNumber)
        public string OPE_KEYU { get; set; }        // N° Expédition STACI (BR3PLPackingSlipId)
        
        // Type de commande (à définir selon la méthode d'appel)
        public string OrderType { get; set; }       // "SALES" ou "TRANSFER"
    }
}