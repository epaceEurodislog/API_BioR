using System.Text.Json.Serialization;

namespace Dynamics365Integration.Models.INT39
{
    public class PostTrackingNumberResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; }
    }
}