using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FaceSearchApp.Models
{
    // ───────────── Enums ─────────────
    public enum ImageType { Normal, Target }
    public enum SearchType { Normal, Target }

    // ───────────── Requests ─────────────
    public class UploadRequest
    {
        public string Base64 { get; set; } = string.Empty;
        public ImageType ImageType { get; set; } = ImageType.Normal;
    }

    public class SearchRequest
    {
        public string Base64 { get; set; } = string.Empty;
        public SearchType SearchType { get; set; } = SearchType.Normal;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public float MinScore { get; set; } = 0.5f;
    }

    public class DeleteRequest
    {
        public ImageType ImageType { get; set; } = ImageType.Normal;
        public List<string> Ids { get; set; } = [];
    }

    public class GetImageRequest
    {
        public List<string> Ids { get; set; } = [];
    }

    public class GetVectorPageRequest
    {
        public ImageType ImageType { get; set; } = ImageType.Normal;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
