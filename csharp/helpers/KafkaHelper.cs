#:include ../models/Url.cs

#:package Confluent.Kafka@2.4.0
#:package Confluent.SchemaRegistry.Serdes.Avro@2.4.0
#:package PrettyConsole@*

using System.Text.Json;
using Avro;
using Avro.Generic;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using PrettyConsole;
using Dotfiles.Models;
using CC = System.ConsoleColor;

namespace Dotfiles.Helpers;

public readonly record struct SchemaInfo(
    int Id,
    RecordSchema Schema,
    AvroSerializer<GenericRecord> Serializer,
    string Topic
);

internal static class KafkaHelper
{
    public static void ToConsole(this Error error) =>
        Console.WriteLineInterpolated($"{CC.Red}Error{CC.Default}: {error.Reason}");

    public static void ToConsole(this LogMessage logMessage)
    {
        switch (logMessage.Level)
        {
            case SyslogLevel.Warning:
                Console.WriteLineInterpolated($"[{CC.Yellow}]<{logMessage.Level:u}>{CC.Default} {logMessage.Message}");
                break;
            case SyslogLevel.Error or SyslogLevel.Critical:
                Console.WriteLineInterpolated($"[{CC.Red}]<{logMessage.Level}>{CC.Default} {logMessage.Message}");
                break;
            default:
                Console.WriteLineInterpolated($"{CC.White}<{logMessage.Level}>{CC.Default} {logMessage.Message}");
                break;
        }
    }

    public static IProducer<TKey, TMessage>? BuildKafkaProducerClient<TKey, TMessage>(string? url,
        string fallbackEnvName = "KAFKA_URL")
    {
        if (string.IsNullOrWhiteSpace(url))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var config = Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToProducerConfig()
            : throw new FormatException("Invalid Kafka URL format.");
        return new ProducerBuilder<TKey, TMessage>(config)
            .SetErrorHandler((_, error) => error.ToConsole())
            .SetLogHandler((_, log) => log.ToConsole())
            .Build();
    }

    public static IProducer<TKey, TMessage>? BuildKafkaProducerClient<TKey, TMessage>(Url url) =>
        new ProducerBuilder<TKey, TMessage>(url.ToProducerConfig())
            .SetErrorHandler((_, error) => error.ToConsole())
            .SetLogHandler((_, log) => log.ToConsole())
            .Build();

    public static IAdminClient? BuildKafkaAdminClient(string? url, string fallbackEnvName = "KAFKA_URL")
    {
        if (string.IsNullOrWhiteSpace(url))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var config = Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToAdminConfig()
            : throw new FormatException("Invalid Kafka URL format.");
        return new AdminClientBuilder(config)
            .SetErrorHandler((_, error) => error.ToConsole())
            .SetLogHandler((_, log) => log.ToConsole())
            .Build();
    }

    public static ISchemaRegistryClient? BuildSchemaRegistryClient(string? url,
        string fallbackEnvName = "SCHEMA_REGISTRY_URL")
    {
        if (string.IsNullOrWhiteSpace(url))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var config = Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToSchemaRegistryConfig()
            : throw new FormatException("Invalid Schema Registry URL format.");
        return new CachedSchemaRegistryClient(config);
    }

    extension(ISchemaRegistryClient client)
    {
        public async ValueTask<SchemaInfo?> GetSchema(string topic)
        {
            ArgumentException.ThrowIfNullOrEmpty(topic);

            var subject = $"{topic}-value";
            var metadata = await client.GetLatestSchemaAsync(subject);
            if (Avro.Schema.Parse(metadata.SchemaString) is not RecordSchema avroSchema) return null;

            var serializer = new AvroSerializer<GenericRecord>(client);
            return new SchemaInfo(metadata.Id, avroSchema, serializer, topic);
        }

        public async Task<ErrorCodes> RegisterSchemaAsync(
            string topicName, string schemaFile, string schemaDir, CancellationToken cancellationToken)
        {
            var subject = $"{topicName}-value";
            var schemaPath = Path.Combine(schemaDir, schemaFile);
            if (!File.Exists(schemaPath))
            {
                Console.WriteLineInterpolated(
                    $"    {CC.Yellow}⚠{CC.Default} Schema file not found: {CC.Cyan}{schemaPath}{CC.Default}");
                return ErrorCodes.Failed;
            }

            try
            {
                var schemaContent = await File.ReadAllTextAsync(schemaPath, cancellationToken);
                var schema = new Confluent.SchemaRegistry.Schema(schemaContent, SchemaType.Avro);
                var schemaId = await client.RegisterSchemaAsync(subject, schema);
                Console.WriteLineInterpolated(
                    $"    {CC.Green}✓{CC.Default} Registered schema for {CC.Cyan}{subject}{CC.Default} (ID: {CC.Cyan}{schemaId}){CC.Default}");
                return ErrorCodes.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLineInterpolated(
                    $"    {CC.Red}✗{CC.Default} Failed to register schema for {CC.Cyan}{subject}{CC.Default}: {CC.Cyan}{ex.Message}{CC.Default}");
                return ErrorCodes.Failed;
            }
        }

        public async ValueTask<int?> TryPopulateValue(Message<string?, byte[]> message, string topic,
            string jsonPayload)
        {
            ArgumentException.ThrowIfNullOrEmpty(jsonPayload);
            ArgumentException.ThrowIfNullOrEmpty(topic);
            ArgumentNullException.ThrowIfNull(message);

            if (await client.GetSchema(topic) is not { } schemaInfo) return null;

            var record = JsonToGenericRecord(jsonPayload, schemaInfo.Schema);
            var serializedPayload = await schemaInfo.Serializer.SerializeAsync(record,
                new SerializationContext(MessageComponentType.Value, topic));
            message.Value = serializedPayload;
            return schemaInfo.Id;
        }
    }

    extension(IAdminClient client)
    {
        public async Task<ErrorCodes> RegisterTopicAsync(string topicName, int partitions, short replication)
        {
            try
            {
                var topicSpec = new TopicSpecification
                {
                    Name = topicName,
                    NumPartitions = partitions,
                    ReplicationFactor = replication
                };
                await client.CreateTopicsAsync([topicSpec]);
                Console.WriteLineInterpolated(
                    $"    {CC.Green}✓{CC.Default} Topic created (partitions: {partitions}, replication: {replication})");
                return ErrorCodes.Success;
            }
            catch (Exception ex) when (ex.Message.Contains("already exists"))
            {
                Console.WriteLineInterpolated($"    {CC.Yellow}⚠{CC.Default} Topic already exists");
                return ErrorCodes.Skipped;
            }
            catch (Exception ex)
            {
                Console.WriteLineInterpolated($"    {CC.Red}✗{CC.Default} Failed to create topic: {ex.Message}");
                return ErrorCodes.Failed;
            }
        }
    }

    extension(Message<string?, byte[]> message)
    {
        public void PopulateHeaders(string[] headers, char separator = ':')
        {
            if (headers is not { Length: > 0 }) return;

            message.Headers ??= [];
            foreach (var header in headers)
            {
                var separatorIndex = header.IndexOf(separator);
                if (separatorIndex <= 0 || separatorIndex >= header.Length - 1) continue;

                var key = header[..separatorIndex];
                var value = header[(separatorIndex + 1)..].Trim() switch
                {
                    "$uuid" or "$guid" => Guid.CreateVersion7().ToString(),
                    var val => val
                };
                message.Headers.Add(key, System.Text.Encoding.UTF8.GetBytes(value));
            }
        }

        public async Task TryPopulateValue(SchemaInfo schemaInfo, string jsonPayload,
            SerializationContext? context = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(jsonPayload);

            var record = JsonToGenericRecord(jsonPayload, schemaInfo.Schema);
            var serializedPayload = await schemaInfo.Serializer.SerializeAsync(record,
                context ?? new(MessageComponentType.Value, schemaInfo.Topic));
            message.Value = serializedPayload;
        }

        private static GenericRecord JsonToGenericRecord(string json, RecordSchema schema)
        {
            var record = new GenericRecord(schema);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var field in schema.Fields)
            {
                if (root.TryGetProperty(field.Name, out var value))
                {
                    var avroValue = ConvertJsonValue(value, field.Schema);
                    record.Add(field.Name, avroValue);
                }
            }

            return record;
        }

        private static object? ConvertJsonValue(JsonElement token, Avro.Schema schema)
        {
            // Handle union types (e.g., ["null", "string"])
            if (schema is UnionSchema unionSchema)
            {
                if (token.ValueKind == JsonValueKind.Null ||
                    unionSchema.Schemas.FirstOrDefault(x => x.Tag != Avro.Schema.Type.Null) is not { } nonNullSchema)
                    return null;

                schema = nonNullSchema;
            }

            return schema.Tag switch
            {
                Avro.Schema.Type.String => token.ToString(),
                Avro.Schema.Type.Int => token.GetInt32(),
                Avro.Schema.Type.Long => token.GetInt64(),
                Avro.Schema.Type.Float => token.GetSingle(),
                Avro.Schema.Type.Double => token.GetDouble(),
                Avro.Schema.Type.Boolean => token.GetBoolean(),
                Avro.Schema.Type.Array => token.EnumerateArray()
                    .Select(t => ConvertJsonValue(t, ((ArraySchema)schema).ItemSchema)).ToArray(),
                Avro.Schema.Type.Bytes => Convert.FromBase64String(token.ToString()),
                _ => token.GetRawText()
            };
        }
    }

    extension(Url url)
    {
        private TConfig ToConfig<TConfig>() where TConfig : ClientConfig, new()
        {
            var config = new TConfig { Acks = Acks.All };
            if (!string.IsNullOrEmpty(url.Host))
                config.BootstrapServers = $"{url.Host}:{url.Port ?? 9092}";
            var useCredential = !string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password);
            config.SecurityProtocol = (url.Secure, useCredential) switch
            {
                (true, true) => SecurityProtocol.SaslSsl,
                (true, false) => SecurityProtocol.Ssl,
                (false, true) => SecurityProtocol.SaslPlaintext,
                _ => SecurityProtocol.Plaintext
            };
            if (useCredential)
            {
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = url.Username;
                config.SaslPassword = url.Password;
            }

            if (url.Secure)
            {
                if (url.Extras.TryGetValue("ca.path", out var caPath) && !string.IsNullOrEmpty(caPath))
                    config.SslCaLocation = caPath;
                if (url.Extras.TryGetValue("trustServerCertificate", out var trustValue))
                {
                    var alwaysTrust = (bool.TryParse(trustValue, out var boolValue) && boolValue)
                                      || (int.TryParse(trustValue, out var intValue) && intValue != 0);
                    if (alwaysTrust)
                        config.SslEndpointIdentificationAlgorithm = SslEndpointIdentificationAlgorithm.None;
                }
            }

            if (url.Extras.TryGetValue("ack", out var ackValue) &&
                Enum.TryParse<Acks>(ackValue, true, out var acks))
                config.Acks = acks;
            return config;
        }

        public AdminClientConfig ToAdminConfig() => url.ToConfig<AdminClientConfig>();

        public ProducerConfig ToProducerConfig()
        {
            var config = url.ToConfig<ProducerConfig>();
            config.MessageTimeoutMs = url.Extras.TryGetValue("message.timeout.ms", out var timeoutValue) &&
                                      int.TryParse(timeoutValue, out var timeoutMs)
                ? timeoutMs
                : 5_000;
            return config;
        }

        public SchemaRegistryConfig ToSchemaRegistryConfig()
        {
            var config = new SchemaRegistryConfig();
            if (!string.IsNullOrEmpty(url.Host))
                config.Url = $"{(url.Secure ? "https" : "http")}://{url.Host}:{url.Port ?? (url.Secure ? 443 : 80)}";
            if (!string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password))
            {
                config.BasicAuthUserInfo = $"{url.Username}:{url.Password}";
                config.BasicAuthCredentialsSource = AuthCredentialsSource.UserInfo;
            }

            return config;
        }
    }
}