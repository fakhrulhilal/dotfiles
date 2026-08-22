#:include ../models/Url.cs

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Dotfiles.Models;

namespace Dotfiles.Helpers;

public static class HttpHelper {
    public static HttpClient? BuildHttpClient(string? url, string? fallbackEnvName = null) {
        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(fallbackEnvName))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        return Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToHttpClient()
            : throw new FormatException("Invalid HTTP URL format.");
    }

    extension(HttpClient client) {
        public async Task<TResponse?> Get<TResponse>(string path,
            JsonTypeInfo<TResponse> jsonConverter, OnWebRequested? onRequested = null, OnWebRequesting? onRequesting = null) =>
            await client.Request(path, HttpMethod.Get, jsonConverter, onRequested: onRequested, onRequesting: onRequesting);

        public async Task<TResponse?> Post<TResponse, TRequest>(string path, TRequest request,
            JsonTypeInfo<TRequest> requestConverter, JsonTypeInfo<TResponse> responseConverter,
            OnWebRequested? onRequested = null, OnWebRequesting? onRequesting = null) {
            var jsonRequest = JsonSerializer.Serialize(request, requestConverter);
            return await client.Request(path, HttpMethod.Post, responseConverter, jsonRequest, onRequested,
                onRequesting);
        }

        public async Task<TResponse?> Request<TResponse>(string path, HttpMethod method,
            JsonTypeInfo<TResponse> jsonConverter, string? jsonRequest = null, OnWebRequested? onRequested = null,
            OnWebRequesting? onRequesting = null) {
            var response = await client.GetResponse(path, method, jsonRequest, onRequesting);
            if (string.IsNullOrEmpty(response)) return default;

            onRequested?.Invoke(new WebResponseModel(method.Method, path, response));
            return JsonSerializer.Deserialize(response, jsonConverter);
        }

        public async Task Request(string path, HttpMethod method, string? jsonRequest = null,
            OnWebRequested? onRequested = null, OnWebRequesting? onRequesting = null) {
            var response = await client.GetResponse(path, method, jsonRequest, onRequesting);
            if (string.IsNullOrEmpty(response)) return;

            onRequested?.Invoke(new WebResponseModel(method.Method, path, response));
        }

        private async Task<string> GetResponse(string path, HttpMethod method, string? jsonRequest,
            OnWebRequesting? onRequesting) {
            using var httpRequest = new HttpRequestMessage(method, $"{client.BaseAddress}/{path.TrimStart('/')}");
            if (!string.IsNullOrWhiteSpace(client.DefaultRequestHeaders.Authorization?.Parameter))
                httpRequest.Headers.Authorization = client.DefaultRequestHeaders.Authorization;

            foreach (var header in client.DefaultRequestHeaders)
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);

            onRequesting?.Invoke(httpRequest);
            if (jsonRequest != null)
                httpRequest.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = await client.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
    }

    extension(Url url) {
        public HttpClient ToHttpClient() {
            HttpClientHandler? handler = null;
            if (url.Secure) {
                if (url.Extras.TryGetValue("trustServerCertificate", out var trustValue)) {
                    var alwaysTrust = (bool.TryParse(trustValue, out var boolValue) && boolValue)
                                      || (int.TryParse(trustValue, out var intValue) && intValue != 0);
                    if (alwaysTrust) {
                        handler = new HttpClientHandler {
                            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                        };
                    }
                }
            }

            var client = handler is not null ? new HttpClient(handler) : new HttpClient();
            if (!string.IsNullOrEmpty(url.Host)) {
                var builder = new StringBuilder();
                builder.Append(url.Secure ? "https://" : "http://");
                builder.Append(url.Host);
                if (url.Port.HasValue) builder.Append($":{url.Port.Value}");
                if (!string.IsNullOrEmpty(url.Path)) builder.Append($"/{url.Path.TrimStart('/')}".TrimEnd('/'));
                client.BaseAddress = new Uri(builder.ToString());
            }

            if (!string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password)) {
                var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{url.Username}:{url.Password}"));
                client.DefaultRequestHeaders.Authorization = new("Basic", credential);
            }

            return client;
        }
    }
}

public readonly record struct WebResponseModel(string Method, string Path, string Content);

public delegate void OnWebRequesting(HttpRequestMessage request);

public delegate void OnWebRequested(WebResponseModel model);
