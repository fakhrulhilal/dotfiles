// Creates the Azure Blob Storage containers when they don't exist yet and keeps their public access level in sync;
// safe to re-run.
// Runs with bun: `bun /migrations/migrate.ts`
// https://learn.microsoft.com/rest/api/storageservices/blob-service-rest-api
import { createHmac } from "node:crypto";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";

type Acl = "public-blob" | "public-container" | "private";
type ContainerConfig = { containerName: string; acl?: Acl | null };

// https://learn.microsoft.com/rest/api/storageservices/set-container-acl
const publicAccess: Record<Acl, string | undefined> = {
    "public-blob": "blob",
    "public-container": "container",
    private: undefined,
};

const env = (name: string, fallback?: string): string => {
    const value = process.env[name] ?? fallback;
    if (!value) throw new Error(`${name} is required`);
    return value;
};

// BLOB_CONNECTIONSTRING wins; otherwise build it from BLOB_URL and BLOB_ACCOUNT_KEY_FILE (preferred) or BLOB_ACCOUNT_KEY.
// The account name comes from BLOB_ACCOUNT_NAME, or the first label of a production-style URL
// (https://<account>.blob.<host>).
const accountKey = (): string => process.env.BLOB_ACCOUNT_KEY_FILE
    ? readFileSync(process.env.BLOB_ACCOUNT_KEY_FILE, "utf8").trim()
    : env("BLOB_ACCOUNT_KEY");
const connectionString = process.env.BLOB_CONNECTIONSTRING
    ?? `BlobEndpoint=${env("BLOB_URL")};AccountName=${process.env.BLOB_ACCOUNT_NAME ?? ""};AccountKey=${accountKey()};`;
const settings = Object.fromEntries(
    connectionString
        .split(";")
        .filter(part => part.includes("="))
        .map(part => [part.slice(0, part.indexOf("=")).toLowerCase(), part.slice(part.indexOf("=") + 1)]),
);
if (!settings.blobendpoint || !settings.accountkey) {
    throw new Error("Connection string must contain BlobEndpoint and AccountKey");
}

const endpoint = settings.blobendpoint.replace(/\/+$/, "");
const account = settings.accountname || new URL(endpoint).hostname.split(".")[0];
const key = Buffer.from(settings.accountkey, "base64");
const configFile = env("BLOB_CONFIG", join(dirname(import.meta.path), "containers.json"));
const trustServerCertificate = process.env.BLOB_TRUST_SERVER_CERTIFICATE === "true";

// https://learn.microsoft.com/rest/api/storageservices/authorize-with-shared-key
function authToken(verb: string, url: URL, headers: Record<string, string>): string {
    const canonicalHeaders = Object.entries(headers)
        .filter(([name]) => name.startsWith("x-ms-"))
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([name, value]) => `${name}:${value}\n`)
        .join("");
    const canonicalQuery = [...url.searchParams]
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([name, value]) => `\n${name.toLowerCase()}:${value}`)
        .join("");
    // VERB, then 11 standard headers that are all empty here (Content-Length is empty when 0)
    const payload = `${verb}\n${"\n".repeat(11)}${canonicalHeaders}/${account}${url.pathname}${canonicalQuery}`;
    const signature = createHmac("sha256", key).update(payload, "utf8").digest("base64");
    return `SharedKey ${account}:${signature}`;
}

async function put(path: string, access: string | undefined): Promise<Response> {
    const url = new URL(`${endpoint}/${path}`);
    const headers: Record<string, string> = {
        "x-ms-date": new Date().toUTCString(),
        "x-ms-version": "2021-12-02",
        ...(access ? { "x-ms-blob-public-access": access } : {}),
    };
    return fetch(url, {
        method: "PUT",
        headers: { ...headers, authorization: authToken("PUT", url, headers) },
        tls: { rejectUnauthorized: !trustServerCertificate },
    });
}

// PUT a new container; a conflict means it already exists, so only its public access level is updated
// https://learn.microsoft.com/rest/api/storageservices/create-container
async function create(container: ContainerConfig): Promise<void> {
    const acl = container.acl ?? "private";
    const access = publicAccess[acl];
    const label = `container '${container.containerName}' (${acl})`;
    const response = await put(`${container.containerName}?restype=container`, access);
    if (response.status === 201) {
        console.log(`Created: ${label}`);

        return;
    }

    if (response.status !== 409) {
        throw new Error(`Failed to create ${label}: HTTP ${response.status} ${await response.text()}`);
    }

    const update = await put(`${container.containerName}?restype=container&comp=acl`, access);
    if (update.status === 200) console.log(`Exists: ${label}`);
    else throw new Error(`Failed to set access of ${label}: HTTP ${update.status} ${await update.text()}`);
}

const containers = (await Bun.file(configFile).json()) as ContainerConfig[];
if (!Array.isArray(containers)
    || containers.some(c => !c.containerName || (c.acl != null && !(c.acl in publicAccess)))) {
    throw new Error(`${configFile} must be an array of { containerName, acl: public-blob | public-container | private }`);
}

console.log(`Start migration from ${configFile} to ${endpoint} (account '${account}')`);
console.log(`Connection string: DefaultEndpointsProtocol=${new URL(endpoint).protocol.slice(0, -1)};`
    + `AccountName=${account};AccountKey=${settings.accountkey};BlobEndpoint=${endpoint};`);
for (const container of containers) await create(container);