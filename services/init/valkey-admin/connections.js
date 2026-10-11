// Valkey Admin keeps its connection list in the browser (localStorage), so the servers of this stack are seeded on the
// first visit. Later edits made in the UI are kept: nothing is written once the list exists.
(() => {
    const key = "VALKEY_CONNECTIONS";
    if (localStorage.getItem(key) !== null) return;

    const servers = [
        { alias: "Valkey", host: "cache-valkey", port: "6379" },
        { alias: "Redis", host: "cache-redis", port: "6379" },
    ];
    const connections = {};
    for (const { alias, host, port } of servers) {
        const id = `${host}-${port}-db0`.replace(/[^a-zA-Z0-9_-]/g, "-");
        connections[id] = {
            // an empty password plus a history entry makes the UI connect on its own at start up
            connectionDetails: {
                host,
                port,
                alias,
                password: "",
                tls: false,
                verifyTlsCertificate: false,
                endpointType: "node",
                db: 0,
            },
            status: "Not Connected",
            errorMessage: null,
            connectionHistory: [ { timestamp: Date.now(), event: "Connected" } ],
            searchableText: [ id, host, port, alias ].join(" ").toLowerCase(),
        };
    }

    localStorage.setItem(key, JSON.stringify(connections));
})();