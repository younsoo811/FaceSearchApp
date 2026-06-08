using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FaceSearchApp.Services
{
    public class BenchmarkService : IDisposable
    {
        // 고성능 병렬 요청을 위해 SocketsHttpHandler 직접 구성
        private readonly HttpClient _http = new(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 100,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true,

            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true
            }
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private string _token = string.Empty;
        private string _baseUrl = string.Empty;

        public bool IsConnected { get; private set; }

        // ── 로그인 ────────────────────────────────────────────────────

        public async Task<(bool Success, string Error)> ConnectAsync(
            string baseUrl, string username, string password)
        {
            try
            {
                _baseUrl = baseUrl.TrimEnd('/');

                var body = JsonSerializer.Serialize(new
                {
                    username,
                    password,
                    accountType = "2"
                });

                var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/GUNS/mgr/login")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var res = await _http.SendAsync(req);
                var json = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                string code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                string data = root.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
                string msg = root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "";

                if (success && code == "0000" && !string.IsNullOrEmpty(data))
                {
                    _token = data;
                    IsConnected = true;
                    return (true, string.Empty);
                }

                IsConnected = false;
                return (false, $"[{code}] {msg}");
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

        // ── 원시 분석 요청 (스레드 안전, 병렬 호출 가능) ─────────────

        public async Task<(bool Success, string Error, string RawJson, long ResponseMs)>
            AnalyzeRawAsync(string base64Image, CancellationToken token = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var url = $"{_baseUrl}/COGNITIVESVC/cognitive/pedestrian/getAttributeByImageBase64";
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(base64Image), "figureImageBase64");

                var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _token);

                var res = await _http.SendAsync(req, token);
                sw.Stop();
                var json = await res.Content.ReadAsStringAsync(token);

                // 응답 코드 빠른 확인
                bool ok = json.Contains("\"code\":\"0000\"") || json.Contains("\"success\":true");
                return (ok, ok ? string.Empty : "서버 오류 응답", json, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return (false, "요청 취소됨", string.Empty, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return (false, ex.Message, string.Empty, sw.ElapsedMilliseconds);
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
