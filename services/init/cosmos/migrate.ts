// Creates the Azure Cosmos DB (NoSQL API) database and its containers when they don't exist yet; safe to re-run.
// Runs with bun: `bun /migrations/migrate.ts`
// DB_MODE is accepted for parity with the SDK settings, but database and container management always goes through
// the gateway (REST API), so it doesn't change anything here.
// https://learn.microsoft.com/rest/api/cosmos-db/
import { createHmac } from "node:crypto";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";

type ContainerConfig = { containerName: string; partitionKey: string; throughput?: number | null };

const env = (name: string, fallback?: string): string => {
    const value = process.env[name] ?? fallback;
    if (!value) throw new Error(`${name} is required`);
    return value;
};

// DB_CONNECTIONSTRING wins; otherwise build it from DB_URL and DB_ACCOUNT_KEY_FILE (preferred) or DB_ACCOUNT_KEY
const accountKey = (): string => process.env.DB_ACCOUNT_KEY_FILE
    ? readFileSync(process.env.DB_ACCOUNT_KEY_FILE, "utf8").trim()
    : env("DB_ACCOUNT_KEY");
const connectionString = process.env.DB_CONNECTIONSTRING
    ?? `AccountEndpoint=${env("DB_URL")};AccountKey=${accountKey()};`;
const settings = Object.fromEntries(
    connectionString
        .split(";")
        .filter(part => part.includes("="))
        .map(part => [part.slice(0, part.indexOf("=")).toLowerCase(), part.slice(part.indexOf("=") + 1)]),
);
if (!settings.accountendpoint || !settings.accountkey) {
    throw new Error("Connection string must contain AccountEndpoint and AccountKey");
}

const endpoint = settings.accountendpoint.replace(/\/+$/, "");
const key = Buffer.from(settings.accountkey, "base64");
const dbName = env("DB_NAME");
const configFile = env("DB_CONFIG", join(dirname(import.meta.path), "containers.json"));
const defaultThroughput = process.env.DB_THROUGHPUT ? Number(process.env.DB_THROUGHPUT) : undefined;
const mode = env("DB_MODE", "gateway").toLowerCase();
const trustServerCertificate = process.env.DB_TRUST_SERVER_CERTIFICATE === "true";

if (mode !== "gateway" && mode !== "direct") throw new Error(`DB_MODE must be gateway or direct, got '${mode}'`);
if (defaultThroughput !== undefined && !Number.isInteger(defaultThroughput)) {
    throw new Error(`DB_THROUGHPUT must be a number, got '${process.env.DB_THROUGHPUT}'`);
}

// https://learn.microsoft.com/rest/api/cosmos-db/access-control-on-cosmosdb-resources
function authToken(verb: string, resourceType: string, resourceLink: string, date: string): string {
    const payload = `${verb}\n${resourceType}\n${resourceLink}\n${date}\n\n`.toLowerCase();
    const signature = createHmac("sha256", key).update(payload).digest("base64");
    return encodeURIComponent(`type=master&ver=1.0&sig=${signature}`);
}

// POST a new resource; a conflict means it already exists
async function create(resourceType: string, parentLink: string, label: string, body: unknown,
    headers: Record<string, string> = {}): Promise<void> {
    const date = new Date().toUTCString();
    const response = await fetch(`${endpoint}/${parentLink ? `${parentLink}/` : ""}${resourceType}`, {
        method: "POST",
        headers: {
            authorization: authToken("post", resourceType, parentLink, date),
            "x-ms-date": date,
            "x-ms-version": "2018-12-31",
            "content-type": "application/json",
            ...headers,
        },
        body: JSON.stringify(body),
        tls: { rejectUnauthorized: !trustServerCertificate },
    });
    if (response.status === 201) console.log(`Created: ${label}`);
    else if (response.status === 409) console.log(`Exists: ${label}`);
    else throw new Error(`Failed to create ${label}: HTTP ${response.status} ${await response.text()}`);
}

const containers = (await Bun.file(configFile).json()) as ContainerConfig[];
if (!Array.isArray(containers) || containers.some(c => !c.containerName || !c.partitionKey)) {
    throw new Error(`${configFile} must be an array of { ContainerName, PartitionKey, Throughput }`);
}

console.log(`Start migration from ${configFile} to ${endpoint}`);
await create("dbs", "", `database '${dbName}'`, { id: dbName });
for (const container of containers) {
    const throughput = container.throughput ?? defaultThroughput;
    const body = { id: container.containerName, partitionKey: { paths: [container.partitionKey], kind: "Hash", version: 2 } };
    if (throughput === undefined) {
        await create("colls", `dbs/${dbName}`, `container '${container.containerName}'`, body);
    } else {
        await create("colls", `dbs/${dbName}`, `container '${container.containerName}' (autoscale max ${throughput} RU/s)`,
            body, { "x-ms-cosmos-offer-autopilot-settings": JSON.stringify({ maxThroughput: throughput }) });
    }
}