using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FaceSearchApp.Models
{
    // ───────────── Response wrapper ─────────────
    public class ResponseDTO<T>
    {
        public string Code { get; set; } = string.Empty;
        public string Msg { get; set; } = string.Empty;
        public T? Data { get; set; }
        public bool Success { get; set; }
    }

    // ───────────── Response data ─────────────
    public class UploadResult
    {
        public string ImageId { get; set; } = string.Empty;
        public string ImageType { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
    }

    public class SearchResultItem
    {
        public string ImageId { get; set; } = string.Empty;
        public float Score { get; set; }
    }

    public class DeleteResult
    {
        public int RequestedCount { get; set; }
        public int DeletedCount { get; set; }
        public List<string> NotFoundIds { get; set; } = [];
    }

    public class ImageResult
    {
        public int RequestedCount { get; set; }
        public int FoundCount { get; set; }
        public List<string> NotFoundIds { get; set; } = [];
        public List<ImageItem> Images { get; set; } = [];
    }

    public class ImageItem
    {
        public string Id { get; set; } = string.Empty;
        public string Base64 { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class VectorPageResult
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPrevious { get; set; }
        public bool HasNext { get; set; }
        public string ImageType { get; set; } = string.Empty;
        public List<VectorItem> Vectors { get; set; } = [];
    }

    public class VectorItem
    {
        public string Id { get; set; } = string.Empty;
        public string ImageId { get; set; } = string.Empty;
        public float[] Vector { get; set; } = [];
        public DateTime CreatedAt { get; set; }
    }
}
