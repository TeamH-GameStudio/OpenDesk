using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Services;
using UnityEngine;
using VContainer;

namespace OpenDesk.AgentCreation.Soul
{
    /// <summary>
    /// Anthropic Messages API에 Haiku 모델로 1회 호출하여 Soul markdown을 생성한다.
    /// AnthropicApiChatService와 분리된 이유: 단일 책임(채팅 스트리밍) 유지.
    /// 키 소스는 동일한 IApiKeyVaultService("anthropic").
    /// </summary>
    public sealed class HaikuSoulGenerationService : ISoulGenerationService, IDisposable
    {
        private const string ApiUrl     = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

        private readonly IApiKeyVaultService _vault;
        private readonly HttpClient _http;
        private bool _disposed;

        [Inject]
        public HaikuSoulGenerationService(IApiKeyVaultService vault)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _http = new HttpClient { Timeout = RequestTimeout };
            _http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        }

        public async UniTask<string> GenerateAsync(AgentCreationData data, CancellationToken ct)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(data.AgentName))
                throw new InvalidOperationException("AgentName이 비어있어 Soul을 생성할 수 없습니다.");

            var apiKey = await _vault.GetKeyAsync("anthropic", ct);
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Anthropic API 키가 저장되어 있지 않습니다.");

            var body = BuildRequestJson(data);

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-api-key", apiKey);

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"네트워크 오류: {e.Message}", e);
            }

            if (!res.IsSuccessStatusCode)
            {
                var errBody = await res.Content.ReadAsStringAsync();
                var preview = errBody[..Math.Min(errBody.Length, 300)];
                throw new InvalidOperationException($"Anthropic API 오류 {(int)res.StatusCode}: {preview}");
            }

            var rawText = await ReadStreamingTextAsync(res, ct);
            var validated = SoulPrompt.ValidateAndNormalize(rawText);
            if (validated == null)
            {
                Debug.LogWarning($"[SoulGen] 응답 검증 실패. 원문 일부: {rawText[..Math.Min(rawText.Length, 200)]}");
                throw new InvalidOperationException("생성된 Soul이 형식 검증을 통과하지 못했습니다.");
            }

            Debug.Log($"[SoulGen] Soul 생성 성공 ({validated.Length}자)");
            return validated;
        }

        // ── 내부: SSE 스트림 → 누적 텍스트 ──

        private static async UniTask<string> ReadStreamingTextAsync(HttpResponseMessage res, CancellationToken ct)
        {
            var accumulated = new StringBuilder();
            using var stream = await res.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data:")) continue;

                var data = line.Substring(5).Trim();
                if (data == "[DONE]") break;

                var deltaText = ExtractDeltaText(data);
                if (!string.IsNullOrEmpty(deltaText))
                    accumulated.Append(deltaText);
            }

            return accumulated.ToString();
        }

        // ── 내부: JSON 빌더 / 파서 ──

        private static string BuildRequestJson(AgentCreationData data)
        {
            var userMsg = SoulPrompt.BuildUserMessage(data);
            var sb = new StringBuilder(512);
            sb.Append("{");
            sb.Append($"\"model\":\"{SoulPrompt.Model}\",");
            sb.Append($"\"max_tokens\":{SoulPrompt.MaxTokens},");
            sb.Append("\"stream\":true,");
            sb.Append($"\"system\":\"{Escape(SoulPrompt.System)}\",");
            sb.Append("\"messages\":[");
            sb.Append($"{{\"role\":\"user\",\"content\":\"{Escape(userMsg)}\"}}");
            sb.Append("]}");
            return sb.ToString();
        }

        private static readonly Regex TextDeltaPattern =
            new("\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        private static string ExtractDeltaText(string json)
        {
            if (!json.Contains("\"type\":\"content_block_delta\"")) return null;
            var match = TextDeltaPattern.Match(json);
            return match.Success ? Unescape(match.Groups[1].Value) : null;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\') { sb.Append(s[i]); continue; }
                if (i + 1 >= s.Length) break;
                var next = s[++i];
                switch (next)
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                         System.Globalization.CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(next); break;
                }
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http?.Dispose();
        }
    }
}
