# KafkaCli (`dotkafka`)

A Kafka client for local and test environments: create topics, register Avro schemas in a Schema Registry, and produce
messages, one at a time or in batches. Source: [`csharp/KafkaCli.cs`](../csharp/KafkaCli.cs). See [Home](Home.md) for
installing it.

```
dotkafka <command> [options]
```

| Command                | Alias | Purpose                                                  |
|------------------------|-------|----------------------------------------------------------|
| `produce`              | `p`   | Produce one message                                      |
| `batch-produce`        | `b`   | Produce many messages from a file, one message per line  |
| `init`                 | `i`   | Create all topics and register all schemas from a config |
| `topic create <topic>` |       | Create one topic and optionally register its schema      |
| `topic delete <topic>` |       | Delete one topic                                         |

## Configuration

| Option           | Environment variable  | Used by                                  |
|------------------|-----------------------|------------------------------------------|
| `--kafka-url`    | `KAFKA_URL`           | every command                            |
| `--registry-url` | `SCHEMA_REGISTRY_URL` | everything except `topic delete`         |
| `-c`             | `KAFKA_TOPIC_FILE`    | `init`: the topics config file           |
| `-d`             | `KAFKA_SCHEMA_FOLDER` | `init`, `topic create`: schema directory |

```shell
export KAFKA_URL=localhost:9092
export SCHEMA_REGISTRY_URL=http://localhost:8081
```

### Connection URLs

Both URLs use the format `[scheme://][user:password@]host[:port][?option=value]`.

- **Kafka:** the port defaults to `9092` and `acks` to `all`. Credentials in the URL enable SASL with the `PLAIN`
  mechanism, and TLS is enabled by a scheme such as `ssl://` or `tls://`, or by `?secure=true`. Together they select the
  security protocol `Plaintext`, `Ssl`, `SaslPlaintext` or `SaslSsl`. With TLS on, `?ca.path=<file>` sets the CA
  certificate and `?trustServerCertificate=true` skips the host name check. Every other query option is passed to the
  Kafka client as a
  [librdkafka setting](https://github.com/confluentinc/librdkafka/blob/master/CONFIGURATION.md), for example
  `sasl.mechanism`, `acks`, `client.id` or `compression.type`. Unknown names are ignored.
- **Schema Registry:** the port defaults to `80`, or `443` with TLS. Credentials in the URL are used for basic
  authentication.

```shell
export KAFKA_URL='kafka+secure://user:secret@broker.example.com:9093?sasl.mechanism=SCRAM-SHA-512'
# anonymous, and unsecred protocol
export KAFKA_URL='kafka://broker:9092?ack=none'
export SCHEMA_REGISTRY_URL='https://user:secret@registry.example.com'
```

## Schemas

Only Avro is supported. A schema is registered under the subject `<topic>-value`, and producing a message uses the
latest schema of that subject. Schema files are Avro schema files (for example `order.avsc`) inside the schema
directory.

## Commands

### `produce`

| Option               | Description                                                               |
|----------------------|---------------------------------------------------------------------------|
| `-t, --topic`        | Topic to produce to. Required                                             |
| `-m, --message`      | The payload as JSON                                                       |
| `-p, --message-path` | A file that contains the payload, an alternative to `-m`                  |
| `-k, --key`          | (optional) Message key                                                    |
| `--header`           | `Key:Value`, repeatable. `$uuid` or `$guid` as value inserts a fresh GUID |

Serializes a JSON payload with the topic's latest Avro schema and sends it. It prints the schema ID, partition and
offset.

```shell
dotkafka produce -t orders -m '{"id": 42, "status": "NEW"}' -k 42
dotkafka produce -t orders -p ./order.json --header 'source:cli' --header 'trace-id: $uuid'
```

Without `-m` and `-p` a record with a null value is sent, and no Schema Registry is needed.

### `batch-produce`

| Option                 | Description                                             |
|------------------------|---------------------------------------------------------|
| `-t, --topic`          | Topic to produce to. Required                           |
| `-p, --message-path`   | File with one value per line. Required                  |
| `-f, --message-format` | JSON payload with `#DATA#` as the placeholder. Required |
| `-k, --key-format`     | Message key, `#DATA#` is replaced as well               |
| `-b, --batch-size`     | Messages sent and shown per batch. Default `50`         |
| `--header`             | Same as in `produce`                                    |

Reads a file line by line and sends one message per line. Each line replaces `#DATA#` in the message format, so the file
only has to hold the parts that change. The messages go out in batches, and a table shows the partition, offset and
payload of every batch.

```shell
# ids.txt holds one id per line
> cat ids.txt
12
34
2
4

> dotkafka batch-produce -t order.received -p ids.txt \
    -f '{"cart_id": #DATA#, "status": "NEW"}' -k 'id:#DATA#' -b 100
```
The topic must already have a schema in the Schema Registry.

### `init`

| Option              | Description                                                        |
|---------------------|--------------------------------------------------------------------|
| `-c, --config-path` | Topics config file, or from env `KAFKA_TOPIC_FILE`                 |
| `-d, --schema-dir`  | Directory with the schema files, or from env `KAFKA_SCHEMA_FOLDER` |
| `-p, --partition`   | Default partitions per topic. Default `3`                          |
| `-r, --replication` | Default replication factor. Default `1`                            |

Creates every topic in a config file and registers its schema. It is safe to run again: a topic that exists is skipped
and reported. A missing schema file is a warning, not an error. A summary is printed at the end.

```shell
export KAFKA_TOPIC_FILE=~/projects/spec/topics.json
export KAFKA_SCHEMA_FOLDER=~/projects/spec/schema
dotkafka init -c topics.json
```

Sample of `~/projects/spec/topics.json` config file is a JSON array:
```json
[
  { "name": "notification.email", "value-schema": "email-notification.json", "partition": 10 },
  { "name": "order.received", "value-schema": "orders/checkout.avsc", "partition": 6 },
  { "name": "order.shipped", "value-schema": "orders/shipping.avsc" },
  { "name": "order.paid", "value-schema": "orders/payment.avsc", "replication": 1 }
]
```

Sample of the directory structure exposed through env `KAFKA_SCHEMA_FOLDER`
```
\-- ~/projects/spec/schema
    |-- email-notification.json
    \-- orders
        |--checkout.avsc
        |--shipping.avsc
        |--payment.avsc
```

`value-schema` is a file name inside the schema directory. `partition` and `replication` are optional and fall back to
the command defaults.

### `topic create` and `topic delete`

```shell
dotkafka topic create orders --schema-file order.avsc -d ./schemas -p 6
dotkafka topic delete orders
```

`topic create` takes the same `-d`, `-p` and `-r` options as `init`, plus `--schema-file`, which names a file inside the
schema directory. Without a schema file, only the topic is created.

`topic delete` asks for no confirmation. It removes the topic and its data from the cluster the URL points at.

## Errors

Problems with files or directories are printed as `Error: <message>` with exit code `1`. A missing or malformed
`--kafka-url` or `--registry-url` makes the command fail with an exception message.