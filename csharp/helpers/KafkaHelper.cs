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

internal static class KafkaHelper {
    private static void ToConsole(this Error error) => Console.Error.WriteLine($"ERROR: {error.Reason}");

    private static void ToConsole(this LogMessage log) => Console.WriteLine($"[{log.Level}] {log.Message}");

    private static void WriteToConsole<TKey, TMessage>(IProducer<TKey, TMessage> _, Error error) => error.ToConsole();

    private static void WriteToConsole<TKey, TMessage>(IProducer<TKey, TMessage> _, LogMessage log) => log.ToConsole();

    private static void WriteToConsole(IAdminClient _, Error error) => error.ToConsole();

    private static void WriteToConsole(IAdminClient _, LogMessage log) => log.ToConsole();

    private static Action<string, TConfig> SetBool<TConfig>(Action<TConfig, bool> setter) {
        return (value, config) => {
            if (bool.TryParse(value, out var result)) setter(config, result);
            else if (int.TryParse(value, out var intValue)) setter(config, intValue != 0);
        };
    }

    private static Action<string, TConfig> SetInt<TConfig>(Action<TConfig, int> setter) {
        return (value, config) => {
            if (int.TryParse(value, out var result)) setter(config, result);
        };
    }

    private static Action<string, TConfig> SetDouble<TConfig>(Action<TConfig, double> setter) {
        return (value, config) => {
            if (double.TryParse(value, out var result)) setter(config, result);
        };
    }

    private static Action<string, TConfig> SetString<TConfig>(Action<TConfig, string> setter) {
        return (value, config) => {
            if (!string.IsNullOrWhiteSpace(value)) setter(config, value);
        };
    }

    private static Action<string, TConfig> SetEnum<TConfig, TValue>(Action<TConfig, TValue> setter)
        where TValue : struct, Enum {
        return (value, config) => {
            if (Enum.TryParse(value, out TValue result)) setter(config, result);
        };
    }

    private static readonly Dictionary<string, Action<string, ClientConfig>> ConfigFactories =
        new(StringComparer.InvariantCultureIgnoreCase) {
            ["sasl.mechanism"] = (value, config) => {
                SaslMechanism? parsed = value.ToUpperInvariant() switch {
                    var val when string.IsNullOrEmpty(val) => null,
                    var val when Enum.TryParse<SaslMechanism>(val, true, out var result) => result,
                    "SCRAM-SHA-256" => SaslMechanism.ScramSha256,
                    "SCRAM-SHA-512" => SaslMechanism.ScramSha512,
                    _ => null
                };
                if (parsed != null) config.SaslMechanism = parsed;
            },
            ["acks"] = (value, config) => {
                var parsed = value switch {
                    null => null,
                    "0" => Acks.None,
                    "1" => Acks.Leader,
                    "-1" => Acks.All,
                    var val when "all".Equals(val, StringComparison.InvariantCultureIgnoreCase) => Acks.All,
                    _ when int.TryParse(value, out var result) => (Acks?)result,
                    _ => null
                };
                if (parsed != null) config.Acks = parsed;
            },
            ["client.id"] = SetString<ClientConfig>((cfg, val) => cfg.ClientId = val),
            ["message.max.bytes"] = SetInt<ClientConfig>((cfg, val) => cfg.MessageMaxBytes = val),
            ["message.copy.max.bytes"] = SetInt<ClientConfig>((cfg, val) => cfg.MessageCopyMaxBytes = val),
            ["receive.message.max.bytes"] = SetInt<ClientConfig>((cfg, val) => cfg.ReceiveMessageMaxBytes = val),
            ["max.in.flight"] = SetInt<ClientConfig>((cfg, val) => cfg.MaxInFlight = val),
            ["topic.metadata.refresh.interval.ms"] =
                SetInt<ClientConfig>((cfg, val) => cfg.TopicMetadataRefreshIntervalMs = val),
            ["metadata.max.age.ms"] = SetInt<ClientConfig>((cfg, val) => cfg.MetadataMaxAgeMs = val),
            ["topic.metadata.refresh.fast.interval.ms"] =
                SetInt<ClientConfig>((cfg, val) => cfg.TopicMetadataRefreshFastIntervalMs = val),
            ["topic.metadata.refresh.sparse"] =
                SetBool<ClientConfig>((cfg, val) => cfg.TopicMetadataRefreshSparse = val),
            ["topic.metadata.propagation.max.ms"] =
                SetInt<ClientConfig>((cfg, val) => cfg.TopicMetadataPropagationMaxMs = val),
            ["socket.timeout.ms"] = SetInt<ClientConfig>((cfg, val) => cfg.SocketTimeoutMs = val),
            ["socket.send.buffer.bytes"] = SetInt<ClientConfig>((cfg, val) => cfg.SocketSendBufferBytes = val),
            ["socket.receive.buffer.bytes"] = SetInt<ClientConfig>((cfg, val) => cfg.SocketReceiveBufferBytes = val),
            ["socket.keepalive.enable"] = SetBool<ClientConfig>((cfg, val) => cfg.SocketKeepaliveEnable = val),
            ["socket.nagle.disable"] = SetBool<ClientConfig>((cfg, val) => cfg.SocketNagleDisable = val),
            ["socket.max.fails"] = SetInt<ClientConfig>((cfg, val) => cfg.SocketMaxFails = val),
            ["broker.address.ttl"] = SetInt<ClientConfig>((cfg, val) => cfg.BrokerAddressTtl = val),
            ["socket.connection.setup.timeout.ms"] =
                SetInt<ClientConfig>((cfg, val) => cfg.SocketConnectionSetupTimeoutMs = val),
            ["connections.max.idle.ms"] = SetInt<ClientConfig>((cfg, val) => cfg.ConnectionsMaxIdleMs = val),
            ["reconnect.backoff.ms"] = SetInt<ClientConfig>((cfg, val) => cfg.ReconnectBackoffMs = val),
            ["reconnect.backoff.max.ms"] = SetInt<ClientConfig>((cfg, val) => cfg.ReconnectBackoffMaxMs = val),
            ["statistics.interval.ms"] = SetInt<ClientConfig>((cfg, val) => cfg.StatisticsIntervalMs = val),
            ["log.queue"] = SetBool<ClientConfig>((cfg, val) => cfg.LogQueue = val),
            ["log.thread.name"] = SetBool<ClientConfig>((cfg, val) => cfg.LogThreadName = val),
            ["enable.random.seed"] = SetBool<ClientConfig>((cfg, val) => cfg.EnableRandomSeed = val),
            ["log.connection.close"] = SetBool<ClientConfig>((cfg, val) => cfg.LogConnectionClose = val),
            ["internal.termination.signal"] = SetInt<ClientConfig>((cfg, val) => cfg.InternalTerminationSignal = val),
            ["api.version.request"] = SetBool<ClientConfig>((cfg, val) => cfg.ApiVersionRequest = val),
            ["api.version.request.timeout.ms"] =
                SetInt<ClientConfig>((cfg, val) => cfg.ApiVersionRequestTimeoutMs = val),
            ["api.version.fallback.ms"] = SetInt<ClientConfig>((cfg, val) => cfg.ApiVersionFallbackMs = val),
            ["broker.version.fallback"] = SetString<ClientConfig>((cfg, val) => cfg.BrokerVersionFallback = val),
            ["allow.auto.create.topics"] = SetBool<ClientConfig>((cfg, val) => cfg.AllowAutoCreateTopics = val),
            ["security.protocol"] = SetEnum<ClientConfig, SecurityProtocol>((cfg, val) => cfg.SecurityProtocol = val),
            ["ssl.cipher.suites"] = SetString<ClientConfig>((cfg, val) => cfg.SslCipherSuites = val),
            ["ssl.curves.list"] = SetString<ClientConfig>((cfg, val) => cfg.SslCurvesList = val),
            ["ssl.sigalgs.list"] = SetString<ClientConfig>((cfg, val) => cfg.SslSigalgsList = val),
            ["ssl.key.location"] = SetString<ClientConfig>((cfg, val) => cfg.SslKeyLocation = val),
            ["ssl.key.password"] = SetString<ClientConfig>((cfg, val) => cfg.SslKeyPassword = val),
            ["ssl.key.pem"] = SetString<ClientConfig>((cfg, val) => cfg.SslKeyPem = val),
            ["ssl.certificate.location"] = SetString<ClientConfig>((cfg, val) => cfg.SslCertificateLocation = val),
            ["ssl.certificate.pem"] = SetString<ClientConfig>((cfg, val) => cfg.SslCertificatePem = val),
            ["ssl.ca.location"] = SetString<ClientConfig>((cfg, val) => cfg.SslCaLocation = val),
            ["ssl.ca.pem"] = SetString<ClientConfig>((cfg, val) => cfg.SslCaPem = val),
            ["ssl.ca.certificate.stores"] = SetString<ClientConfig>((cfg, val) => cfg.SslCaCertificateStores = val),
            ["ssl.crl.location"] = SetString<ClientConfig>((cfg, val) => cfg.SslCrlLocation = val),
            ["ssl.keystore.location"] = SetString<ClientConfig>((cfg, val) => cfg.SslKeystoreLocation = val),
            ["ssl.keystore.password"] = SetString<ClientConfig>((cfg, val) => cfg.SslKeystorePassword = val),
            ["ssl.providers"] = SetString<ClientConfig>((cfg, val) => cfg.SslProviders = val),
            ["ssl.engine.location"] = SetString<ClientConfig>((cfg, val) => cfg.SslEngineLocation = val),
            ["ssl.engine.id"] = SetString<ClientConfig>((cfg, val) => cfg.SslEngineId = val),
            ["enable.ssl.certificate.verification"] =
                SetBool<ClientConfig>((cfg, val) => cfg.EnableSslCertificateVerification = val),
            ["ssl.endpoint.identification.algorithm"] =
                SetEnum<ClientConfig, SslEndpointIdentificationAlgorithm>((cfg, val) =>
                    cfg.SslEndpointIdentificationAlgorithm = val),
            ["sasl.kerberos.service.name"] = SetString<ClientConfig>((cfg, val) => cfg.SaslKerberosServiceName = val),
            ["sasl.kerberos.principal"] = SetString<ClientConfig>((cfg, val) => cfg.SaslKerberosPrincipal = val),
            ["sasl.kerberos.kinit.cmd"] = SetString<ClientConfig>((cfg, val) => cfg.SaslKerberosKinitCmd = val),
            ["sasl.kerberos.keytab"] = SetString<ClientConfig>((cfg, val) => cfg.SaslKerberosKeytab = val),
            ["sasl.kerberos.min.time.before.relogin"] =
                SetInt<ClientConfig>((cfg, val) => cfg.SaslKerberosMinTimeBeforeRelogin = val),
            ["sasl.username"] = SetString<ClientConfig>((cfg, val) => cfg.SaslUsername = val),
            ["sasl.password"] = SetString<ClientConfig>((cfg, val) => cfg.SaslPassword = val),
            ["sasl.oauthbearer.config"] = SetString<ClientConfig>((cfg, val) => cfg.SaslOauthbearerConfig = val),
            ["enable.sasl.oauthbearer.unsecure.jwt"] =
                SetBool<ClientConfig>((cfg, val) => cfg.EnableSaslOauthbearerUnsecureJwt = val),
            ["sasl.oauthbearer.method"] =
                SetEnum<ClientConfig, SaslOauthbearerMethod>((cfg, val) => cfg.SaslOauthbearerMethod = val),
            ["sasl.oauthbearer.client.id"] = SetString<ClientConfig>((cfg, val) => cfg.SaslOauthbearerClientId = val),
            ["sasl.oauthbearer.client.secret"] =
                SetString<ClientConfig>((cfg, val) => cfg.SaslOauthbearerClientSecret = val),
            ["sasl.oauthbearer.scope"] = SetString<ClientConfig>((cfg, val) => cfg.SaslOauthbearerScope = val),
            ["sasl.oauthbearer.extensions"] =
                SetString<ClientConfig>((cfg, val) => cfg.SaslOauthbearerExtensions = val),
            ["sasl.oauthbearer.token.endpoint.url"] =
                SetString<ClientConfig>((cfg, val) => cfg.SaslOauthbearerTokenEndpointUrl = val),
            ["plugin.library.paths"] = SetString<ClientConfig>((cfg, val) => cfg.PluginLibraryPaths = val),
            ["client.rack"] = SetString<ClientConfig>((cfg, val) => cfg.ClientRack = val),
            ["client.dns.lookup"] = SetEnum<ClientConfig, ClientDnsLookup>((cfg, val) => cfg.ClientDnsLookup = val),
        };

    private static readonly Dictionary<string, Action<string, ProducerConfig>> ProducerConfigFactories =
        new(StringComparer.InvariantCultureIgnoreCase) {
            ["dotnet.producer.enable.background.poll"] =
                SetBool<ProducerConfig>((cfg, val) => cfg.EnableBackgroundPoll = val),
            ["dotnet.producer.enable.delivery.reports"] =
                SetBool<ProducerConfig>((cfg, val) => cfg.EnableDeliveryReports = val),
            ["dotnet.producer.delivery.report.fields"] =
                SetString<ProducerConfig>((cfg, val) => cfg.DeliveryReportFields = val),
            ["request.timeout.ms"] = SetInt<ProducerConfig>((cfg, val) => cfg.RequestTimeoutMs = val),
            ["message.timeout.ms"] = SetInt<ProducerConfig>((cfg, val) => cfg.MessageTimeoutMs = val),
            ["partitioner"] = SetEnum<ProducerConfig, Partitioner>((cfg, val) => cfg.Partitioner = val),
            ["compression.level"] = SetInt<ProducerConfig>((cfg, val) => cfg.CompressionLevel = val),
            ["transactional.id"] = SetString<ProducerConfig>((cfg, val) => cfg.TransactionalId = val),
            ["transaction.timeout.ms"] = SetInt<ProducerConfig>((cfg, val) => cfg.TransactionTimeoutMs = val),
            ["enable.idempotence"] = SetBool<ProducerConfig>((cfg, val) => cfg.EnableIdempotence = val),
            ["enable.gapless.guarantee"] = SetBool<ProducerConfig>((cfg, val) => cfg.EnableGaplessGuarantee = val),
            ["queue.buffering.max.messages"] =
                SetInt<ProducerConfig>((cfg, val) => cfg.QueueBufferingMaxMessages = val),
            ["queue.buffering.max.kbytes"] = SetInt<ProducerConfig>((cfg, val) => cfg.QueueBufferingMaxKbytes = val),
            ["linger.ms"] = SetDouble<ProducerConfig>((cfg, val) => cfg.LingerMs = val),
            ["message.send.max.retries"] = SetInt<ProducerConfig>((cfg, val) => cfg.MessageSendMaxRetries = val),
            ["retry.backoff.ms"] = SetInt<ProducerConfig>((cfg, val) => cfg.RetryBackoffMs = val),
            ["retry.backoff.max.ms"] = SetInt<ProducerConfig>((cfg, val) => cfg.RetryBackoffMaxMs = val),
            ["queue.buffering.backpressure.threshold"] =
                SetInt<ProducerConfig>((cfg, val) => cfg.QueueBufferingBackpressureThreshold = val),
            ["compression.type"] = SetEnum<ProducerConfig, CompressionType>((cfg, val) => cfg.CompressionType = val),
            ["batch.num.messages"] = SetInt<ProducerConfig>((cfg, val) => cfg.BatchNumMessages = val),
            ["batch.size"] = SetInt<ProducerConfig>((cfg, val) => cfg.BatchSize = val),
            ["sticky.partitioning.linger.ms"] =
                SetInt<ProducerConfig>((cfg, val) => cfg.StickyPartitioningLingerMs = val),
        };

    public static IProducer<TKey, TMessage>? BuildKafkaProducerClient<TKey, TMessage>(
        string? url,
        string fallbackEnvName = "KAFKA_URL",
        Action<IProducer<TKey, TMessage>, Error>? errorHandler = null,
        Action<IProducer<TKey, TMessage>, LogMessage>? logHandler = null) {
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
        Action<IAdminClient, LogMessage>? logHandler = null) {
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
        string fallbackEnvName = "SCHEMA_REGISTRY_URL") {
        if (string.IsNullOrWhiteSpace(url))
            url = Environment.GetEnvironmentVariable(fallbackEnvName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var config = Url.TryParse(url, out var parsedUrl)
            ? parsedUrl.ToSchemaRegistryConfig()
            : throw new FormatException("Invalid Schema Registry URL format.");
        return new CachedSchemaRegistryClient(config);
    }

    extension(ISchemaRegistryClient client) {
        public async ValueTask<Result.WithValue<SchemaInfo>> GetSchema(string topic) {
            ArgumentException.ThrowIfNullOrEmpty(topic);
            var subject = $"{topic}-value";
            var metadata = await client.GetLatestSchemaAsync(subject);
            if (Avro.Schema.Parse(metadata.SchemaString) is not RecordSchema avroSchema) return KafkaError.NotFound;

            var serializer = new AvroSerializer<GenericRecord>(client);
            return new SchemaInfo(metadata.Id, avroSchema, serializer, topic);
        }

        public async Task<Result.WithValue<string>> RegisterSchemaAsync(
            string topicName, string schemaFile, string schemaDir, CancellationToken cancellationToken) {
            var subject = $"{topicName}-value";
            var schemaPath = Path.Combine(schemaDir, schemaFile);
            if (!File.Exists(schemaPath)) return KafkaError.NotFound;

            try {
                var schemaContent = await File.ReadAllTextAsync(schemaPath, cancellationToken);
                var schema = new Confluent.SchemaRegistry.Schema(schemaContent, SchemaType.Avro);
                var schemaId = await client.RegisterSchemaAsync(subject, schema);
                return schemaId.ToString();
            }
            catch (Exception ex) {
                return KafkaError.Unknown(ex.Message);
            }
        }

        public async ValueTask<Result.WithValue<int>> TryPopulateValue(Message<string?, byte[]> message, string topic,
            string jsonPayload) {
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

    extension(IAdminClient client) {
        public async Task<Result> RegisterTopicAsync(string topicName, int partitions, short replication) {
            try {
                var topicSpec = new TopicSpecification {
                    Name = topicName, NumPartitions = partitions, ReplicationFactor = replication
                };
                await client.CreateTopicsAsync([topicSpec]);
                return Result.Ok();
            }
            catch (Exception ex) when (ex.Message.Contains("already exists")) {
                return KafkaError.AlreadyExists;
            }
            catch (Exception exc) {
                return KafkaError.Unknown(exc.Message);
            }
        }
    }

    extension(Message<string?, byte[]> message) {
        public void PopulateHeaders(string[] headers, char separator = ':') {
            if (headers is not { Length: > 0 }) return;

            message.Headers ??= [];
            foreach (var header in headers) {
                var separatorIndex = header.IndexOf(separator);
                if (separatorIndex <= 0 || separatorIndex >= header.Length - 1) continue;

                var key = header[..separatorIndex];
                var value = header[(separatorIndex + 1)..].Trim() switch {
                    "$uuid" or "$guid" => Guid.CreateVersion7().ToString(),
                    var val => val
                };
                message.Headers.Add(key, System.Text.Encoding.UTF8.GetBytes(value));
            }
        }

        public async Task TryPopulateValue(SchemaInfo schemaInfo, string jsonPayload,
            SerializationContext? context = null) {
            ArgumentException.ThrowIfNullOrEmpty(jsonPayload);
            var record = JsonToGenericRecord(jsonPayload, schemaInfo.Schema);
            var serializedPayload = await schemaInfo.Serializer.SerializeAsync(record,
                context ?? new(MessageComponentType.Value, schemaInfo.Topic));
            message.Value = serializedPayload;
        }

        private static GenericRecord JsonToGenericRecord(string json, RecordSchema schema) {
            var record = new GenericRecord(schema);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var field in schema.Fields) {
                if (root.TryGetProperty(field.Name, out var value)) {
                    var avroValue = ConvertJsonValue(value, field.Schema);
                    record.Add(field.Name, avroValue);
                }
            }

            return record;
        }

        private static object? ConvertJsonValue(JsonElement token, Avro.Schema schema) {
            // Handle union types (e.g., ["null", "string"])
            if (schema is UnionSchema unionSchema) {
                if (token.ValueKind == JsonValueKind.Null ||
                    unionSchema.Schemas.FirstOrDefault(x => x.Tag != Avro.Schema.Type.Null) is not { } nonNullSchema)
                    return null;

                schema = nonNullSchema;
            }

            return schema.Tag switch {
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

    extension(Url url) {
        private TConfig ToConfig<TConfig>() where TConfig : ClientConfig, new() {
            var config = new TConfig { Acks = Acks.All };
            if (!string.IsNullOrEmpty(url.Host))
                config.BootstrapServers = $"{url.Host}:{url.Port ?? 9092}";
            var useCredential = !string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password);
            config.SecurityProtocol = (url.Secure, useCredential) switch {
                (true, true) => SecurityProtocol.SaslSsl,
                (true, false) => SecurityProtocol.Ssl,
                (false, true) => SecurityProtocol.SaslPlaintext,
                _ => SecurityProtocol.Plaintext
            };
            if (useCredential) {
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = url.Username;
                config.SaslPassword = url.Password;
            }

            if (url.Secure) {
                if (url.Extras.TryGetValue("ca.path", out var caPath) && !string.IsNullOrEmpty(caPath))
                    config.SslCaLocation = caPath;
                if (url.Extras.TryGetValue("trustServerCertificate", out var trustValue)) {
                    var alwaysTrust = (bool.TryParse(trustValue, out var boolValue) && boolValue)
                                      || (int.TryParse(trustValue, out var intValue) && intValue != 0);
                    if (alwaysTrust)
                        config.SslEndpointIdentificationAlgorithm = SslEndpointIdentificationAlgorithm.None;
                }
            }

            foreach (var (key, value) in url.Extras) {
                if (ConfigFactories.TryGetValue(key, out var globalConfigFactory))
                    globalConfigFactory(value, config);
            }

            return config;
        }

        public AdminClientConfig ToAdminConfig() => url.ToConfig<AdminClientConfig>();

        public ProducerConfig ToProducerConfig() {
            var config = url.ToConfig<ProducerConfig>();
            foreach (var (key, value) in url.Extras) {
                if (ProducerConfigFactories.TryGetValue(key, out var producerConfigFactory))
                    producerConfigFactory(value, config);
            }

            return config;
        }

        public SchemaRegistryConfig ToSchemaRegistryConfig() {
            var config = new SchemaRegistryConfig();
            if (!string.IsNullOrEmpty(url.Host))
                config.Url = $"{(url.Secure ? "https" : "http")}://{url.Host}:{url.Port ?? (url.Secure ? 443 : 80)}";
            if (!string.IsNullOrEmpty(url.Username) && !string.IsNullOrEmpty(url.Password)) {
                config.BasicAuthUserInfo = $"{url.Username}:{url.Password}";
                config.BasicAuthCredentialsSource = AuthCredentialsSource.UserInfo;
            }

            return config;
        }
    }
}

public enum Codes {
    None = 0,
    AlreadyExists,
    Invalid,
    NotFound,
    Unknown
}

public sealed record KafkaError(Codes Code, string? Message = null) {
    public static KafkaError NotFound => new(Codes.NotFound);
    public static KafkaError AlreadyExists => new(Codes.AlreadyExists);
    public static KafkaError Unknown(string message) => new(Codes.Unknown, message);
}
