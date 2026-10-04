// Bootstraps Rootprint (local only) through its public HTTP API; idempotent, safe to re-run:
// 1. first admin via /api/auth/setup-admin (only while /api/auth/bootstrap says it is needed)
// 2. ingest API key for the OTLP log index, token written to TOKEN_FILE for the OTel collector
// Runs inside the rootprint image with bun: `bun /tools/init_rootprint.ts`
// https://docs.rootprint.io/api/overview
import { chmod, mkdir, writeFile } from "node:fs/promises";
import { dirname } from "node:path";

const env = (name: string, fallback?: string): string => {
    const value = process.env[name] ?? fallback;
    if (!value) throw new Error(`${name} is required`);
    return value;
};

const baseUrl = env("ROOTPRINT_URL", "http://rootprint:8080");
const origin = env("ORIGIN", baseUrl);
const admin = { name: env("ADMIN_NAME", "Admin"), email: env("ADMIN_EMAIL").toLowerCase(), password: env("ADMIN_PASSWORD") };
const keyName = env("INGEST_KEY_NAME", "otel-collector");
const indexId = env("INGEST_INDEX_ID", "otel-logs-v0_9");
const tokenFile = env("TOKEN_FILE", "/secrets/ingest.token");

let cookie = "";

async function api<T>(method: string, path: string, body?: unknown): Promise<{ status: number; data: T; response: Response }> {
    const response = await fetch(`${baseUrl}${path}`, {
        method,
        headers: { origin, cookie, ...(body === undefined ? {} : { "content-type": "application/json" }) },
        body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    const data = (text ? JSON.parse(text) : undefined) as T;
    return { status: response.status, data, response };
}

async function expectOk<T>(method: string, path: string, body?: unknown): Promise<T> {
    const { status, data, response } = await api<T>(method, path, body);
    if (!response.ok) throw new Error(`${method} ${path} failed: HTTP ${status} ${JSON.stringify(data)}`);
    return data;
}

const { needsSetupAdmin } = await expectOk<{ needsSetupAdmin: boolean }>("GET", "/api/auth/bootstrap");
if (needsSetupAdmin) {
    await expectOk("POST", "/api/auth/setup-admin", admin);
    console.log(`Created first admin '${admin.email}'`);
} else {
    console.log("First admin already set up, skipping");
}

const signIn = await api("POST", "/api/auth/sign-in/email", { email: admin.email, password: admin.password });
if (!signIn.response.ok) {
    throw new Error(`Sign-in as '${admin.email}' failed (HTTP ${signIn.status}); `
        + "if an admin was created manually, reset it: https://docs.rootprint.io/configuration/reset-admin-password");
}
cookie = signIn.response.headers.getSetCookie().map(c => c.split(";")[0]).join("; ");

// the OTLP log index is created by Quickwit on start, give it a moment on a fresh volume
for (let attempt = 1; ; attempt++) {
    const indexes = await expectOk<{ indexId: string }[]>("GET", "/api/indexes");
    if (indexes.some(i => i.indexId === indexId)) break;
    if (attempt === 30) throw new Error(`Index '${indexId}' not found`);
    await Bun.sleep(2000);
}

type ApiKey = { id: number; name: string };
const keys = await expectOk<ApiKey[]>("GET", "/api/api-keys");
let keyId = keys.find(k => k.name === keyName)?.id;
if (keyId === undefined) {
    ({ summary: { id: keyId } } = await expectOk<{ summary: ApiKey }>("POST", "/api/api-keys", { name: keyName, indexId }));
    console.log(`Created ingest key '${keyName}' for index '${indexId}'`);
}
const { token } = await expectOk<{ token: string }>("GET", `/api/api-keys/${keyId}`);

await mkdir(dirname(tokenFile), { recursive: true });
await writeFile(tokenFile, token);
await chmod(tokenFile, 0o644);
console.log(`Ingest key '${keyName}' written to ${tokenFile}`);
