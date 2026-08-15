#!/usr/bin/env dotnet --

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true
#:property AssemblyName=kafka#
#:property TrimmerRootDescriptor=KafkaCliTrimmerRoots.xml
#:property TrimMode=partial

#:include ./helpers/KafkaHelper.cs

#:package ConsoleAppFramework@*
#:package Spectre.Console@*

using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using ConsoleAppFramework;
using PrettyConsole;
using Spectre.Console;
using static Dotfiles.Helpers.KafkaHelper;
using CC = System.ConsoleColor;

ConsoleApp.Create().Run(args);
return 0;

[RegisterCommands]
sealed class Commands
{
    /// <summary>
    /// Produce a message to a Kafka topic
    /// </summary>
    /// <param name="kafkaUrl">Kafka connection URL. Default: from environment variable KAFKA_URL</param>
    /// <param name="registryUrl">
    /// Schema Registry URL, required when message or message path is specified. Default: from environment variable SCHEMA_REGISTRY_URL.
    /// </param>
    /// <param name="topic">-t, Kafka topic to produce</param>
    /// <param name="message">-m, Message payload</param>
    /// <param name="messagePath">-p, Path to a file containing the message payload</param>
    /// <param name="key">-k, Message key</param>
    /// <param name="header">Message headers in 'Key:Value' format (can be specified multiple times). It also accepts special values like $uuid and $guid in Value (which basically the same thing).</param>
    [Command("produce|p")]
    public async Task<int> ProductAsync(
        string topic,
        [HideDefaultValue] string? kafkaUrl = null,
        [HideDefaultValue] string? registryUrl = null,
        [HideDefaultValue] string? message = null, [HideDefaultValue] string? messagePath = null,
        [HideDefaultValue] string? key = null, params string[] header)
    {
        using var producer =
            BuildKafkaProducerClient<string?, byte[]>(kafkaUrl) ??
            throw new ArgumentNullException(nameof(kafkaUrl), "Kafka URL is not specified properly");
        var kafkaMessage = new Message<string?, byte[]> { Key = key, Value = null! };
        Console.WriteLine("📩 Producing message to Kafka");
        Console.WriteLineInterpolated($"   Topic: {CC.Cyan}{topic}{CC.Default}");
        kafkaMessage.PopulateHeaders(header);
        if (!string.IsNullOrEmpty(message) || !string.IsNullOrEmpty(messagePath))
        {
            using var schemaRegistryClient =
                BuildSchemaRegistryClient(registryUrl) ??
                throw new ArgumentNullException(nameof(registryUrl), "Schema registry URL is not specified properly");
            var payload = kafkaMessage switch
            {
                _ when !string.IsNullOrEmpty(messagePath) => await File.ReadAllTextAsync(messagePath),
                _ when !string.IsNullOrEmpty(message) => message,
                _ => throw new InvalidOperationException("Message payload is not specified.")
            };
            var schema = await schemaRegistryClient.TryPopulateValue(kafkaMessage, topic, payload);
            if (schema is null)
            {
                Console.WriteLineInterpolated(
                    $" {CC.Red}❌ Error{CC.Default}: Could not find valid schema for topic '{topic}'");
                return 1;
            }

            Console.WriteLineInterpolated($"   Using schema ID: {CC.Cyan}{schema.Value}{CC.Default}");
        }

        var result = await producer.ProduceAsync(topic, kafkaMessage);
        Console.WriteLine("✅ Message produced successfully");
        Console.WriteLineInterpolated($"   Partition: {CC.Cyan}{result.Partition.Value}{CC.Default}");
        Console.WriteLineInterpolated($"   Offset: {CC.Cyan}{result.Offset.Value}{CC.Default}");
        return 0;
    }

    /// <summary>
    /// Produce batch messages to a Kafka topic from given file with specified format
    /// </summary>
    /// <param name="kafkaUrl">Kafka connection URL. Default: from environment variable KAFKA_URL</param>
    /// <param name="registryUrl">
    /// Schema Registry URL, required when message or message path is specified. Default: from environment variable SCHEMA_REGISTRY_URL.
    /// </param>
    /// <param name="topic">-t, Kafka topic to produce</param>
    /// <param name="messagePath">-p, Path to a file containing the message payload</param>
    /// <param name="messageFormat">-f, Read from file per line, and use this format to produce batch message. Replace file content with #DATA#</param>
    /// <param name="batchSize">-b, Batch size of messages to produce in a single batch</param>
    /// <param name="keyFormat">-k, Message key</param>
    /// <param name="header">Message headers in 'Key:Value' format (can be specified multiple times). It also accepts special values like $uuid and $guid in Value (which basically the same thing).</param>
    [Command("batch-produce|b")]
    public async Task<int> BatchProductAsync(
        string topic, string messagePath, string messageFormat,
        int batchSize = 50,
        [HideDefaultValue] string? kafkaUrl = null,
        [HideDefaultValue] string? registryUrl = null,
        [HideDefaultValue] string? keyFormat = null, params string[] header)
    {
        if (!File.Exists(messagePath))
        {
            Console.WriteLineInterpolated($"{CC.Red}❌ Error{CC.Default}: Message file not found: {messagePath}");
            return 1;
        }

        using var producer =
            BuildKafkaProducerClient<string?, byte[]>(kafkaUrl) ??
            throw new ArgumentNullException(nameof(kafkaUrl), "Kafka URL is not specified properly");
        using var schemaRegistryClient =
            BuildSchemaRegistryClient(registryUrl) ??
            throw new ArgumentNullException(nameof(registryUrl), "Schema registry URL is not specified properly");
        var schema = await schemaRegistryClient.GetSchema(topic)
                     ?? throw new InvalidOperationException($"Could not find valid schema for topic '{topic}'");
        Console.WriteLine();
        var serializationContext = new SerializationContext(MessageComponentType.Value, topic);
        var table = new Table().Border(TableBorder.Simple).Expand();
        table.Title($"[green]{topic}[/]\n({schema.Id}: {schema.Schema.Namespace}.{schema.Schema.Name})");
        table.AddColumn("Partition", x => x.RightAligned()).Width(40);
        table.AddColumn("Offset", x => x.RightAligned()).Width(40);
        table.AddColumn("Payload", x => x.LeftAligned()).Width(100).Expand();
        await AnsiConsole.Live(table).StartAsync(async ctx =>
        {
            int pageNumber = 1;
            ctx.Refresh();
            var producingTasks = new List<Task<ProduceResult>>(batchSize);
            await foreach (var line in File.ReadLinesAsync(messagePath))
            {
                var data = line.Trim();
                var kafkaMessage = new Message<string?, byte[]>
                    { Key = keyFormat?.Replace("#DATA#", data), Value = null! };
                kafkaMessage.PopulateHeaders(header);
                var payload = messageFormat.Replace("#DATA#", data);
                await kafkaMessage.TryPopulateValue(schema, payload, serializationContext);
                producingTasks.Add(ProduceMessageAsync(producer, topic, kafkaMessage, payload));
                await ProduceAndPrint(count => count >= batchSize);
            }

            await ProduceAndPrint(count => count > 0);

            async Task ProduceAndPrint(Predicate<int> shouldProceed)
            {
                if (!shouldProceed(producingTasks.Count)) return;

                table.Caption($"Page: [blue]{pageNumber}[/]");
                table.Rows.Clear();
                var results = await Task.WhenAll(producingTasks);
                foreach (var result in results)
                    table.AddRow(result.Partition.ToString(), result.Offset.ToString(), result.Payload);

                ctx.Refresh();
                await Task.Delay(200);
                producingTasks.Clear();
                pageNumber++;
            }
        });

        Console.WriteLine("✅ Message produced successfully");
        return 0;

        static async Task<ProduceResult> ProduceMessageAsync(
            IProducer<string?, byte[]> kafkaProducer,
            string targetTopic,
            Message<string?, byte[]> msg,
            string currentPayload)
        {
            var result = await kafkaProducer.ProduceAsync(targetTopic, msg);
            return new ProduceResult(currentPayload, result.Partition.Value, result.Offset.Value);
        }
    }

    /// <summary>
    /// Initialize Kafka topics and register schemas
    /// </summary>
    /// <param name="kafkaUrl">Kafka connection URL. Default: from environment variable KAFKA_URL</param>
    /// <param name="registryUrl">Schema Registry URL. Default: from environment variable SCHEMA_REGISTRY_URL</param>
    /// <param name="configPath">-c, Path to Kafka topics configuration file. Default: from environment variable KAFKA_TOPIC_FILE.</param>
    /// <param name="schemaDir">-d, Directory containing schema files. Default: from environment variable KAFKA_SCHEMA_FOLDER.</param>
    /// <param name="partition">-p, Default number of partitions per topic when not specified in config</param>
    /// <param name="replication">-r, Default replication factor when not specified in config</param>
    /// <param name="cancellationToken"></param>
    [Command("init|i")]
    public async Task<int> InitAsync(
        [HideDefaultValue] string? kafkaUrl = null,
        [HideDefaultValue] string? registryUrl = null,
        [HideDefaultValue] string? configPath = null, [HideDefaultValue] string? schemaDir = null,
        int partition = 3, short replication = 1,
        CancellationToken cancellationToken = default)
    {
        var error = Validate();
        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLineInterpolated($"{CC.Red}❌ Error{CC.Default}: {error}");
            return 1;
        }

        Console.WriteLine("🚀 Starting Kafka initialization...");
        Console.WriteLineInterpolated($"{CC.White}⚡{CC.Default} Configuration file: {CC.Cyan}{configPath}{CC.Default}");
        Console.WriteLineInterpolated($"{CC.White}⚡{CC.Default} Schema folder: {CC.Cyan}{schemaDir}{CC.Default}");
        Console.WriteLineInterpolated(
            $"{CC.White}⚡{CC.Default} Default partition per topic: {CC.Cyan}{partition}{CC.Default}");
        Console.WriteLineInterpolated(
            $"{CC.White}⚡{CC.Default} Default replication factor: {CC.Cyan}{replication}{CC.Default}");

        var configFile = await File.ReadAllBytesAsync(configPath!, cancellationToken);
        var topicSpecifications = JsonSerializer.Deserialize(configFile, JsonOpt.Default.SchemaConfigArray) ?? [];
        if (topicSpecifications.Length == 0)
        {
            Console.WriteLineInterpolated(
                $"{CC.Yellow}⚠ Warning{CC.Default}: No topic configurations found in the config file.");
            return 0;
        }

        var createdTopicCount = 0;
        var skippedTopics = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var failedTopics = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var registeredSchemaCount = 0;
        var failedRegisterSchemas = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        using var adminClient =
            BuildKafkaAdminClient(kafkaUrl)
            ?? throw new ArgumentNullException(nameof(kafkaUrl), "Kafka URL is not specified properly");
        using var schemaRegistryClient =
            BuildSchemaRegistryClient(registryUrl) ??
            throw new ArgumentNullException(nameof(registryUrl), "Schema registry URL is not specified properly");
        foreach (var spec in topicSpecifications)
        {
            Console.WriteLineInterpolated(
                $"{CC.White}㉿{CC.Default} Found topic config: {CC.Cyan}{spec.Topic}{CC.Default} with schema {CC.Cyan}{spec.ValueSchema}{CC.Default}");
            switch (await adminClient.RegisterTopicAsync(spec.Topic, spec.Partition ?? partition,
                        spec.Replication ?? replication))
            {
                case ErrorCodes.Success:
                    createdTopicCount++;
                    break;
                case ErrorCodes.Skipped:
                    skippedTopics.Add(spec.Topic);
                    break;
                case ErrorCodes.Failed:
                    failedTopics.Add(spec.Topic);
                    continue;
            }

            switch (await schemaRegistryClient.RegisterSchemaAsync(spec.Topic, spec.ValueSchema, schemaDir!,
                        cancellationToken))
            {
                case ErrorCodes.Success:
                    registeredSchemaCount++;
                    break;
                case ErrorCodes.Failed:
                    failedRegisterSchemas.Add(spec.Topic);
                    break;
            }
        }

        Console.WriteLine("📊 Initialization Summary:");
        Console.WriteLineInterpolated(
            $"{CC.Green}✓{CC.Default} Topics created: {createdTopicCount}/{topicSpecifications.Length}");
        if (skippedTopics.Count > 0)
            Console.WriteLineInterpolated(
                $"{CC.Yellow}⚠{CC.Default} Topics skipped (already exist): {string.Join(", ", skippedTopics)}");
        if (failedTopics.Count > 0)
            Console.WriteLineInterpolated($"{CC.Red}✗{CC.Default} Topics failed: {string.Join(", ", failedTopics)}");
        Console.WriteLineInterpolated(
            $"{CC.Green}✓{CC.Default} Schemas registered: {registeredSchemaCount}/{topicSpecifications.Length}");
        if (failedRegisterSchemas.Count > 0)
            Console.WriteLineInterpolated(
                $"{CC.Red}✗{CC.Default} Schemas failed to register: {string.Join(", ", failedRegisterSchemas)}");
        Console.WriteLine("🎉 Kafka initialization completed successfully!");

        return 0;

        string? Validate()
        {
            if (!File.Exists(configPath ?? Environment.GetEnvironmentVariable("KAFKA_TOPIC_FILE")))
                return $"Config file not found: {configPath}";
            if (!Directory.Exists(schemaDir ?? Environment.GetEnvironmentVariable("KAFKA_SCHEMA_FOLDER")))
                return $"Schema directory not found: {schemaDir}";
            return null;
        }
    }
}

sealed record SchemaConfig(
    [property: JsonPropertyName("name")] string Topic,
    string ValueSchema,
    int? Partition,
    short? Replication);

[JsonSerializable(typeof(SchemaConfig[]))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
partial class JsonOpt : JsonSerializerContext;

enum ErrorCodes
{
    Success,
    Skipped,
    Failed
}

internal readonly record struct ProduceResult(string Payload, int Partition, long Offset);