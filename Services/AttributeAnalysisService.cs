using FaceSearchApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FaceSearchApp.Services
{
    public class AttributeAnalysisService : IDisposable
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private string _token = string.Empty;
        private string _baseUrl = string.Empty;

        public bool IsConnected { get; private set; }

        public AttributeAnalysisService()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        // ── 로그인 ────────────────────────────────────────────────────

        public async Task<(bool Success, string Error)> ConnectAsync(
            string baseUrl, string username, string password)
        {
            try
            {
                _baseUrl = baseUrl.TrimEnd('/');

                var body = JsonSerializer.Serialize(new LoginRequest
                {
                    Username = username,
                    Password = password,
                    AccountType = "2"
                });

                // GET with JSON body (서버 스펙에 따라 POST로 변경 가능)
                var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/GUNS/mgr/login")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var res = await _http.SendAsync(req);
                var json = await res.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<LoginResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (data?.Success == true && data.Code == "0000" && !string.IsNullOrEmpty(data.Data))
                {
                    _token = data.Data;
                    IsConnected = true;
                    return (true, string.Empty);
                }

                IsConnected = false;
                return (false, $"[{data?.Code}] {data?.Msg ?? "로그인 실패"}");
            }
            catch (Exception ex)
            {
                IsConnected = false;
                return (false, ex.Message);
            }
        }

        public void Disconnect()
        {
            _token = string.Empty;
            IsConnected = false;
        }

        // ── 이미지 속성 분석 ─────────────────────────────────────────

        public async Task<(bool Success, string Error, AttributeApiResponse? Data)> AnalyzeAsync(
            string base64Image)
        {
            if (!IsConnected || string.IsNullOrEmpty(_token))
                return (false, "서버에 연결되어 있지 않습니다.", null);

            try
            {
                var url = $"{_baseUrl}/COGNITIVESVC/cognitive/pedestrian/getAttributeByImageBase64";
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(base64Image), "figureImageBase64");

                var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _token);

                var res = await _http.SendAsync(req);
                var json = await res.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<AttributeApiResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (data?.Success == true && data.Code == "0000")
                    return (true, string.Empty, data);

                return (false, $"[{data?.Code}] {data?.Msg ?? "분석 실패"}", null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
