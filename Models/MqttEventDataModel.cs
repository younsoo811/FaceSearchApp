using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FaceSearchApp.Models
{
    public class MqttEventDataModel
    {
        public string Sender { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;

        public string CreateTime { get; set; } = string.Empty;

        public string? Topic { get; set; }

        public EventItemModel EventItem { get; set; } = new();

        public AdditionalDataModel? AdditionalData { get; set; }
    }

    public class EventItemModel
    {
        public string EventType { get; set; } = string.Empty;

        public string ClassType { get; set; } = string.Empty; 

        public List<double> Confidence { get; set; } = new();
        //public double Confidence { get; set; }
        public Dictionary<string, double> Confidences { get; set; } = new();

        public int ObjectId { get; set; }

        public BoundingBoxModel BoundingBox { get; set; } = new();
        public Dictionary<string, List<double>> BoundingBoxs { get; set; } = new();

        // object 또는 "" 대응
        [JsonConverter(typeof(EmptyStringToNullConverter<AreaInfoModel>))]
        public AreaInfoModel? AreaInfo { get; set; }
        [JsonConverter(typeof(EmptyStringToNullConverter<LineInfoModel>))]
        public LineInfoModel? LineInfo { get; set; }

        public ImageInfoModel ImageInfo { get; set; } = new();

        public string Description { get; set; } = string.Empty;
    }

    public class BoundingBoxModel
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class AreaInfoModel
    {
        public string AreaName { get; set; } = string.Empty;

        public string AreaEvent { get; set; } = string.Empty;
    }

    public class LineInfoModel
    {
        public string LineName { get; set; } = string.Empty;

        public int Counting { get; set; }
    }

    public class ImageInfoModel
    {
        public string Base64Data { get; set; } = string.Empty;
        public string FullFrameBase64Data { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }

    public class AdditionalDataModel
    {
        public float? Confidence { get; set; }
        public Dictionary<string, object>? CustomProperties { get; set; }
    }

    public class EmptyStringToNullConverter<T> : JsonConverter<T?>
    where T : class
    {
        public override T? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            // "" 처리
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();

                if (string.IsNullOrWhiteSpace(str))
                    return null;
            }

            // object 처리
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public override void Write(
            Utf8JsonWriter writer,
            T? value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
