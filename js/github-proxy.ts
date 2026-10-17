#!/usr/bin/env -S bun run

// Local HTTPS proxy (CONNECT only) that sends GitHub's Fastly hosts (raw/objects/release-assets.githubusercontent.com,
// *.github.io) to the fastest edge IP instead of a random one. TLS stays end to end, so certificates are untouched.
// Usage: github-proxy.ts &; export HTTPS_PROXY=http://127.0.0.1:8899

import { Command, InvalidArgumentError } from "@commander-js/extra-typings";
import { lookup } from "node:dns/promises";
import net from "node:net";

const Config = {
    name : "github-proxy",
    description : "Local HTTPS proxy that pins GitHub's download CDN to its fastest edge IP",
    version : "0.1.0",
};

const EdgeIps = [ "185.199.108.133", "185.199.109.133", "185.199.110.133", "185.199.111.133" ];
const ProbeAsset = "https://github.com/openssl/openssl/releases/download/openssl-4.0.3/openssl-4.0.3.tar.gz";

// GitHub's Fastly range 185.199.108.0/22
const isGitHubEdge = (ip: string) => /^185\.199\.(10[89]|11[01])\.\d+$/.test(ip);

function parseInteger(value: string): number {
    const parsed = Number(value);
    if (!Number.isInteger(parsed) || parsed < 1) throw new InvalidArgumentError("Expected a positive integer.");

    return parsed;
}

function parseEdgeIp(value: string): string {
    if (!isGitHubEdge(value)) throw new InvalidArgumentError("Not a GitHub edge IP (185.199.108.0/22).");

    return value;
}

async function probeFastestEdge(seconds: number): Promise<string> {
    const redirect = await fetch(ProbeAsset, { method : "HEAD", redirect : "manual" });
    const location = redirect.headers.get("location");
    if (!location) throw new Error(`No redirect from ${ProbeAsset} (HTTP ${redirect.status})`);

    const host = new URL(location).hostname;
    let best = { ip : EdgeIps[0], speed : -1 };
    for (const ip of EdgeIps) {
        const curl = Bun.spawn([
            "curl", "-s", "-o", "/dev/null", "-r", "0-50000000", "--max-time", String(seconds),
            "--resolve", `${host}:443:${ip}`, "-w", "%{speed_download}", location,
        ]);
        const speed = Number(await new Response(curl.stdout).text()) || 0;
        await curl.exited;
        console.log(`  ${ip.padEnd(16)} ${Math.round(speed / 1024)} KB/s`);
        if (speed > best.speed) best = { ip, speed };
    }

    return best.ip;
}

async function resolveTarget(host: string, edgeIp: string): Promise<string> {
    if (net.isIP(host)) return host;

    const addresses = await lookup(host, { all : true });
    return addresses.some(a => isGitHubEdge(a.address)) ? edgeIp : addresses[0].address;
}

function serve(port: number, edgeIp: string) {
    const server = net.createServer(client => {
        let head = Buffer.alloc(0);
        const onData = async (chunk: Buffer) => {
            head = Buffer.concat([ head, chunk ]);
            const end = head.indexOf("\r\n\r\n");
            if (end < 0) return;

            client.off("data", onData);
            client.pause();
            const match = /^CONNECT ([^\s:]+):(\d+) HTTP\/1\.[01]\r\n/.exec(head.toString("latin1", 0, end + 2));
            if (!match) {
                client.end("HTTP/1.1 405 Method Not Allowed\r\nConnection: close\r\n\r\n");
                return;
            }

            const [ , host, port ] = match;
            let target: string;
            try {
                target = await resolveTarget(host, edgeIp);
            } catch {
                client.end("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n");
                return;
            }

            const upstream = net.connect(Number(port), target, () => {
                client.write("HTTP/1.1 200 Connection Established\r\n\r\n");
                const rest = head.subarray(end + 4);
                if (rest.length) upstream.write(rest);
                client.pipe(upstream);
                upstream.pipe(client);
                client.resume();
            });
            upstream.on("error", () => client.destroy());
            client.on("error", () => upstream.destroy());
            if (target === edgeIp) console.log(`${host}:${port} -> ${edgeIp}`);
        };
        client.on("data", onData);
        client.on("error", () => client.destroy());
    });

    server.listen(port, "127.0.0.1", () => {
        console.log(`Listening on http://127.0.0.1:${port}, GitHub edge pinned to ${edgeIp}`);
        console.log(`export HTTPS_PROXY=http://127.0.0.1:${port}`);
    });
}

await new Command()
    .name(Config.name)
    .description(Config.description)
    .version(Config.version)
    .option("-p, --port <port>", "Port to listen on (127.0.0.1 only)", parseInteger, 8899)
    .option("--ip <ip>", "Edge IP to pin instead of probing all four", parseEdgeIp)
    .option("--probe-seconds <seconds>", "Download time per edge when probing", parseInteger, 4)
    .action(async ({ port, ip, probeSeconds }) => {
        if (!ip) console.log("Probing GitHub edge IPs...");
        serve(port, ip ?? await probeFastestEdge(probeSeconds));
    })
    .parseAsync(Bun.argv);
