using FaceSearchApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FaceSearchApp.Services
{
    public class FaceApiService
    {
        private readonly HttpClient _http;
        // 요청 직렬화: enum → 정수 (서버 기본값과 일치, JsonStringEnumConverter 제거)
        private readonly JsonSerializerOptions _serializeOpts = new()
        {
            PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        // 응답 역직렬화: 대소문자 무시, enum 문자열/정수 모두 허용
        private readonly JsonSerializerOptions _deserializeOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public FaceApiService(HttpClient http) => _http = http;

        // ── generic POST helper ──────────────────────────────────────
        private async Task<ResponseDTO<T>?> PostAsync<T>(string endpoint, object body,
            CancellationToken ct = default)
        {
            var json    = JsonSerializer.Serialize(body, _serializeOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[API ▶] POST {endpoint}");
            System.Diagnostics.Debug.WriteLine($"[API ▶] {json}");
#endif

            var resp = await _http.PostAsync(endpoint, content, ct);
            var raw  = await resp.Content.ReadAsStringAsync(ct);

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[API ◀] {(int)resp.StatusCode} {resp.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"[API ◀] {raw}");
#endif

            return JsonSerializer.Deserialize<ResponseDTO<T>>(raw, _deserializeOpts);
        }

        // ── API methods ──────────────────────────────────────────────
        public Task<ResponseDTO<UploadResult>?> UploadAsync(string base64, ImageType imageType,
            CancellationToken ct = default)
            => PostAsync<UploadResult>("upload",
               new UploadRequest { Base64 = base64, ImageType = imageType }, ct);

        public Task<ResponseDTO<List<SearchResultItem>>?> SearchAsync(
            string base64, SearchType searchType,
            DateTime? startDate, DateTime? endDate, float minScore,
            CancellationToken ct = default)
            => PostAsync<List<SearchResultItem>>("search",
               new SearchRequest
               {
                   Base64 = base64,
                   SearchType = searchType,
                   StartDate = startDate,
                   EndDate = endDate,
                   MinScore = minScore
               }, ct);

        public Task<ResponseDTO<DeleteResult>?> DeleteAsync(ImageType imageType, List<string> ids,
            CancellationToken ct = default)
            => PostAsync<DeleteResult>("delete",
               new DeleteRequest { ImageType = imageType, Ids = ids }, ct);

        public Task<ResponseDTO<ImageResult>?> GetImagesAsync(List<string> ids,
            CancellationToken ct = default)
            => PostAsync<ImageResult>("images",
               new GetImageRequest { Ids = ids }, ct);

        public Task<ResponseDTO<VectorPageResult>?> GetVectorPageAsync(
            ImageType imageType, DateTime? startDate, DateTime? endDate,
            int page, int pageSize, CancellationToken ct = default)
            => PostAsync<VectorPageResult>("vectors/page",
               new GetVectorPageRequest
               {
                   ImageType = imageType,
                   StartDate = startDate,
                   EndDate = endDate,
                   Page = page,
                   PageSize = pageSize
               }, ct);
    }
}
