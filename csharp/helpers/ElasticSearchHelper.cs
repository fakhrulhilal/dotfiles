#:include HttpHelper.cs
#:include ../models/PagedQuery.cs

using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dotfiles.Models;

namespace Dotfiles.Helpers;

public static class ElasticSearchHelper
{
    extension(HttpClient client)
    {
        public async Task ElasticBulkUpdate(string indexName, string bulkUpdate)
        {
            await client.Request($"/{indexName}/_bulk", HttpMethod.Post, bulkUpdate,
                onRequesting: request => request.Headers.Add("Content-Type", "application/x-ndjson"));
        }

        public async Task<int> ElasticCountSearch(string indexName, string jsonQuery)
        {
            var json =
                /* lang=json */
                $$"""
                  {
                    "query": {{jsonQuery}}
                  }
                  """;
            var response = await client.Request($"/{indexName}/_count", HttpMethod.Post,
                ElasticJsonOpt.Default.ElasticCountResponse, json);
            return response?.Total ??
                   throw new InvalidOperationException("Unable to calculate total from ElasticSearch");
        }

        public async Task ElasticScrollableSearch<T>(string indexName, string jsonQuery, string jsonSort,
            PagedQuery query, Action<IEnumerable<T>> onReceiving,
            JsonTypeInfo<ElasticScrollableSearchResponse<T>> jsonConverter)
            where T : class
        {
            const string scrollDuration = "1m";
            var json =
                /* lang=json */
                $$"""
                  {
                    "size": {{query.PageSize}},
                    "_source": [
                      "original_id",
                      "activity_type_id",
                      "deleted_at"
                    ],
                    "query": {{jsonQuery}},
                    "sort": {{jsonSort}}
                  }
                  """;
            var response = await client.Request($"/{indexName}/_search?scroll={scrollDuration}", HttpMethod.Post,
                jsonConverter, json);
            if (response is not { Hits.Total.Value: > 0, ScrollId: var scrollId } || string.IsNullOrEmpty(scrollId))
                return;

            var records = response.Hits.Records.Select(x => x.Data);
            onReceiving(records);

            var jsonScrollRequest =
                $$"""
                  {
                      "scroll": "{{scrollDuration}}",
                      "scroll_id": "{{scrollId}}"
                  }
                  """;
            while (true)
            {
                response = await client.Request("/_search/scroll", HttpMethod.Post, jsonConverter, jsonScrollRequest);
                if (response is { Hits.Total.Value: > 0 })
                {
                    onReceiving(response.Hits.Records.Select(x => x.Data));
                }
                else
                {
                    await client.Request($"/_search/scroll/{scrollId}", HttpMethod.Delete);
                    break;
                }
            }
        }
    }
}

// ReSharper disable UnusedAutoPropertyAccessor.Global - Used internally by JSON converter

public abstract class ElasticResponse
{
    [JsonPropertyName("_shards")]
    public ElasticShardResponse Shards { get; set; } = null!;
}

public sealed class ElasticCountResponse : ElasticResponse
{
    [JsonPropertyName("count")]
    public int Total { get; set; }
}

public class ElasticSearchResponse<T> : ElasticResponse where T : class
{
    [JsonPropertyName("timed_out")]
    public bool IsTimedOut { get; set; }

    [JsonPropertyName("took")]
    public int Elapsed { get; set; }

    [JsonPropertyName("hits")]
    public ElasticHitResponse<T> Hits { get; set; } = null!;
}

public sealed class ElasticScrollableSearchResponse<T> : ElasticSearchResponse<T> where T : class
{
    [JsonPropertyName("_scroll_id")]
    public string ScrollId { get; set; } = null!;
}

public sealed class ElasticShardResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("successful")]
    public int Successful { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }
}

public sealed class ElasticHitResponse<T> where T : class
{
    [JsonPropertyName("total")]
    public ElasticHitTotalResponse Total { get; set; } = null!;

    [JsonPropertyName("max_score")]
    public decimal? MaxScore { get; set; }

    [JsonPropertyName("hits")]
    public IEnumerable<ElasticHitItemResponse<T>> Records { get; set; } = null!;
}

public sealed class ElasticHitTotalResponse
{
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("relation")]
    public string Relation { get; set; } = null!;
}

public sealed class ElasticHitItemResponse<T> where T : class
{
    [JsonPropertyName("_index")]
    public string Index { get; set; } = null!;

    [JsonPropertyName("_type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("_id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("_score")]
    public decimal? Score { get; set; }

    [JsonPropertyName("_source")]
    public T Data { get; set; } = null!;
}

[JsonSerializable(typeof(ElasticCountResponse))]
internal sealed partial class ElasticJsonOpt : JsonSerializerContext;