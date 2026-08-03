using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FaceSearchApp.Models
{
    public class VlmResponse
    {
        [JsonPropertyName("description")]
        public string? Desc { get; set; }

        [JsonPropertyName("evidence")]
        public string? Result { get; set; }

        [JsonPropertyName("vlm_decision")]
        public string? Decision { get; set; }

        [JsonPropertyName("vlm_conf")]
        public JsonElement Confidence { get; set; }

        [JsonPropertyName("request_id")]
        public string? Id { get; set; }
    }
}
