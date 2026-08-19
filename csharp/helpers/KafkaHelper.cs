#:include ../models/Url.cs
#:include ../models/Result.cs

#:package Confluent.Kafka@2.4.0
#:package Confluent.SchemaRegistry.Serdes.Avro@2.4.0

using System.Text.Json;
using Avro;
using Avro.Generic;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Dotfiles.Models;

namespace Dotfiles.Helpers;

using Result = Result<KafkaError>;
public readonly record struct SchemaInfo(
    int Id,
    RecordSchema Schema,
    AvroSerializer<GenericRecord> Serializer,
    string Topic
);

internal static class KafkaHelper
{
    private static void ToConsole(this Error error) => Console.Error.WriteLine($"ERROR: {error.Reason}");

    private static void ToConsole(this LogMessage log) => Console.WriteLine($"[{log.Level}] {log.Message}");

    private static void WriteToConsole<TKey, TMessage>(IProducer<TKey, TMessage> _, Error error) => error.ToConsole();

    private static void WriteToConsole<TKey, TMessage>(IProducer<TKey, TMessage> _, LogMessage log) => log.ToConsole();

    private static void WriteToConsole(IAdminClient _, Error error) => error.ToConsole();

    private static void WriteToConsole(IAdminClient _, LogMessage log) => log.ToConsole();

    public static IProducer<TKey, TMessage>? BuildKafkaProducerClient<TKey, TMessage>(
        string? url,
        string fallbackEnvName = "KAFKA_URL",
        Action<IProducer<TKey, TMessage>, Error>? errorHandler = null,
        Action<IProducer<TKey, TMessage>, LogMessage>? logHandler = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var config = Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToProducerConfig()
            : throw new FormatException("Invalid Kafka URL format.");
        return new ProducerBuilder<TKey, TMessage>(config)
            .SetErrorHandler(errorHandler ?? WriteToConsole)
            .SetLogHandler(logHandler ?? WriteToConsole)
            .Build();
    }

    public static IProducer<TKey, TMessage>? BuildKafkaProducerClient<TKey, TMessage>(
        Url url,
        Action<IProducer<TKey, TMessage>, Error>? errorHandler = null,
        Action<IProducer<TKey, TMessage>, LogMessage>? logHandler = null) =>
        new ProducerBuilder<TKey, TMessage>(url.ToProducerConfig())
            .SetErrorHandler(errorHandler ?? WriteToConsole)
            .SetLogHandler(logHandler ?? WriteToConsole)
            .Build();

    public static IAdminClient? BuildKafkaAdminClient(
        string? url,
        string fallbackEnvName = "KAFKA_URL",
        Action<IAdminClient, Error>? errorHandler = null,
        Action<IAdminClient, LogMessage>? logHandler = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var config = Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToAdminConfig()
            : throw new FormatException("Invalid Kafka URL format.");
        return new AdminClientBuilder(config)
            .SetErrorHandler(errorHandler ?? WriteToConsole)
            .SetLogHandler(logHandler ?? WriteToConsole)
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
        public async ValueTask<Result.WithValue<SchemaInfo>> GetSchema(string topic)
        {
            ArgumentException.ThrowIfNullOrEmpty(topic);

            var subject = $"{topic}-value";
            var metadata = await client.GetLatestSchemaAsync(subject);
            if (Avro.Schema.Parse(metadata.SchemaString) is not RecordSchema avroSchema) return KafkaError.NotFound;

            var serializer = new AvroSerializer<GenericRecord>(client);
            return new SchemaInfo(metadata.Id, avroSchema, serializer, topic);
        }

        public async Task<Result.WithValue<string>> RegisterSchemaAsync(
            string topicName, string schemaFile, string schemaDir, CancellationToken cancellationToken)
        {
            var subject = $"{topicName}-value";
            var schemaPath = Path.Combine(schemaDir, schemaFile);
            if (!File.Exists(schemaPath)) return KafkaError.NotFound;

            try
            {
                var schemaContent = await File.ReadAllTextAsync(schemaPath, cancellationToken);
                var schema = new Confluent.SchemaRegistry.Schema(schemaContent, SchemaType.Avro);
                var schemaId = await client.RegisterSchemaAsync(subject, schema);
                return schemaId.ToString();
            }
            catch (Exception ex)
            {
                return KafkaError.Unknown(ex.Message);
            }
        }

        public async ValueTask<Result.WithValue<int>> TryPopulateValue(Message<string?, byte[]> message, string topic,
            string jsonPayload)
        {
            ArgumentException.ThrowIfNullOrEmpty(jsonPayload);
            ArgumentException.ThrowIfNullOrEmpty(topic);
            ArgumentNullException.ThrowIfNull(message);

            var result = await client.GetSchema(topic);
            if (result is not Result.Success<SchemaInfo> { Value: var schemaInfo })
                return result.Error;

            var record = JsonToGenericRecord(jsonPayload, schemaInfo.Schema);
            var serializedPayload = await schemaInfo.Serializer.SerializeAsync(record,
                new SerializationContext(MessageComponentType.Value, topic));
            message.Value = serializedPayload;
            return schemaInfo.Id;
        }
    }

    extension(IAdminClient client)
    {
        public async Task<Result> RegisterTopicAsync(string topicName, int partitions, short replication)
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
                return Result.Ok();
            }
            catch (Exception ex) when (ex.Message.Contains("already exists"))
            {
                return KafkaError.AlreadyExists;
            }
            catch (Exception exc)
            {
                return KafkaError.Unknown(exc.Message);
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
            config.Set("socket.connection.setup.timeout.ms", "");
            return config;
        }

        public AdminClientConfig ToAdminConfig()
        {
            var result = url.ToConfig<AdminClientConfig>();
            string[] validConfigs =
            [
                "client.id",
                "message.max.bytes",
                "message.copy.max.bytes",
                "receive.message.max.bytes",
                "max.in.flight",
                "topic.metadata.refresh.interval.ms",
                "metadata.max.age.ms",
                "topic.metadata.refresh.fast.interval.ms",
                "topic.metadata.refresh.sparse",
                "topic.metadata.propagation.max.ms",
                "topic.blacklist",
                "debug",
                "socket.timeout.ms",
                "socket.send.buffer.bytes",
                "socket.receive.buffer.bytes",
                "socket.keepalive.enable",
                "socket.nagle.disable",
                "socket.max.fails",
                "broker.address.ttl",
                "socket.connection.setup.timeout.ms",
                "connections.max.idle.ms",
                "reconnect.backoff.ms",
                "reconnect.backoff.max.ms",
                "statistics.interval.ms",
                "log.queue",
                "log.thread.name",
                "enable.random.seed",
                "log.connection.close",
                "internal.termination.signal",
                "api.version.request",
                "api.version.request.timeout.ms",
                "api.version.fallback.ms",
                "broker.version.fallback",
                "allow.auto.create.topics",
                "ssl.cipher.suites",
                "ssl.curves.list",
                "ssl.sigalgs.list",
                "ssl.key.location",
                "ssl.key.password",
                "ssl.key.pem",
                "ssl.certificate.location",
                "ssl.certificate.pem",
                "ssl.ca.location",
                "ssl.ca.pem",
                "ssl.ca.certificate.stores",
                "ssl.crl.location",
                "ssl.keystore.location",
                "ssl.keystore.password",
                "ssl.providers",
                "ssl.engine.location",
                "ssl.engine.id",
                "enable.ssl.certificate.verification",
                "ssl.endpoint.identification.algorithm",
                "sasl.kerberos.service.name",
                "sasl.kerberos.principal",
                "sasl.kerberos.kinit.cmd",
                "sasl.kerberos.keytab",
                "sasl.kerberos.min.time.before.relogin",
                "sasl.oauthbearer.config",
                "enable.sasl.oauthbearer.unsecure.jwt",
                "sasl.oauthbearer.method",
                "sasl.oauthbearer.client.id",
                "sasl.oauthbearer.client.secret",
                "sasl.oauthbearer.scope",
                "sasl.oauthbearer.extensions",
                "sasl.oauthbearer.token.endpoint.url",
                "plugin.library.paths",
                "client.rack",
                "client.dns.lookup"
            ];
            var compareMode = StringComparer.OrdinalIgnoreCase;
            foreach (var pair in url.Extras.Where(pair => validConfigs.Contains(pair.Key, compareMode)))
                result.Set(pair.Key, pair.Value);

            return result;
        }

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

public enum Codes
{
    None = 0,
    AlreadyExists,
    Invalid,
    NotFound,
    Unknown
}

public sealed record KafkaError(Codes Code, string? Message = null)
{
    public static KafkaError NotFound => new(Codes.NotFound);
    public static KafkaError AlreadyExists => new(Codes.AlreadyExists);
    public static KafkaError Unknown(string message) => new(Codes.Unknown, message);
}