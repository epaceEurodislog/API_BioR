using System.Text.Json.Serialization;

namespace DynamicsApiToDatabase.Models.INT39
{
    public class TrackingNumberD365Request
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = "br";

        [JsonPropertyName("BROrderId")]
        public string BROrderId { get; set; }

        [JsonPropertyName("BRTrackingNumber")]
        public string BRTrackingNumber { get; set; }

        [JsonPropertyName("BR3PLPackingSlipId")]
        public string BR3PLPackingSlipId { get; set; }

        [JsonPropertyName("BRDocuStatus")]
        public string BRDocuStatus { get; set; }

        [JsonPropertyName("BRDOcStatusDate")]
        public string BRDOcStatusDate { get; set; }

        [JsonPropertyName("CarrierCode")]
        public string CarrierCode { get; set; }
    }
}