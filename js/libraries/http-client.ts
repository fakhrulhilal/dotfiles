import { Guard } from "./language";

/**
 * HTTP auth strategy
 */
export interface Auth {
    getAuthHeader(): Promise<string | null>;
}

/**
 * Bearer token auth strategy. Also covers JWT — a JWT is just a bearer token.
 */
export class BearerAuth implements Auth {
    constructor(private token: string | (() => string | Promise<string>)) {
    }

    async getAuthHeader(): Promise<string> {
        const t = typeof this.token === "function" ? await this.token() : this.token;
        return `Bearer ${t}`;
    }
}

/**
 * Basic auth strategy.
 */
export class BasicAuth implements Auth {
    constructor(private username: string, private password: string) {
    }

    async getAuthHeader(): Promise<string> {
        const raw = `${this.username}:${this.password}`;
        const encoded =
            typeof btoa === "function"
                ? btoa(raw)
                : Buffer.from(raw, "utf-8").toString("base64"); // Node fallback
        return `Basic ${encoded}`;
    }
}

/**
 * OIDC client-credentials flow, with token caching + auto-refresh.
 */
export class OidcClientCredentialsAuth implements Auth {
    private accessToken?: string;
    private expiresAt = 0;

    constructor(
        private readonly config: {
            tokenUrl: string;
            clientId: string;
            clientSecret?: string;
            scope?: string;
        }
    ) {
    }

    async getAuthHeader(): Promise<string> {
        if (!this.accessToken || Date.now() >= this.expiresAt) {
            await this.refresh();
        }
        return `Bearer ${this.accessToken}`;
    }

    private async refresh(): Promise<void> {
        const body = new URLSearchParams({
            grant_type : "client_credentials",
            client_id : this.config.clientId,
            ...(this.config.clientSecret ? { client_secret : this.config.clientSecret } : {}),
            ...(this.config.scope ? { scope : this.config.scope } : {}),
        });

        const res = await fetch(this.config.tokenUrl, {
            method : "POST",
            headers : { "Content-Type" : "application/x-www-form-urlencoded" },
            body,
        });

        if (!res.ok) {
            throw new Error(`OIDC token request failed: ${res.status} ${res.statusText}`);
        }

        const data = (await res.json()) as { access_token: string; expires_in: number };
        this.accessToken = data.access_token;
        // refresh 30s before actual expiry
        this.expiresAt = Date.now() + (data.expires_in - 30) * 1000;
    }
}

/**
 * Thrown when an HTTP response cannot be turned into the expected type.
 * Carries enough context (status, request id, body snippet) to debug.
 */
export class HttpResponseError extends Error {
    constructor(
        public readonly status: number,
        public readonly statusText: string,
        public readonly bodySnippet?: string,
        message?: string,
    ) {
        super(message ?? `HTTP ${status} ${statusText}`);
        this.name = "HttpResponseError";
    }
}

export type HeaderValue = string | ((client: HttpClient) => string);

export interface RequestOptions {
    headers?: Record<string, HeaderValue>;
    query?: Record<string, string | number | boolean | undefined>;
    signal?: AbortSignal;
    bodySnippetLength?: number;
    anonymous?: boolean;
}

/**
 * HTTP client with auth, JSON parsing, and error handling.
 */
export class HttpClient {
    constructor(
        private baseUrl: string,
        private auth?: Auth,
        private defaultHeaders: Record<string, HeaderValue> = {}
    ) {
    }

    /**
     * Call HTTP GET request and parse the JSON response
     * @param path Path relative to the base URL
     * @param options Optional request options
     * @param guard An optional guard function to validate the response shape.
     * @returns
     */
    get<TResponse>(path: string, options?: RequestOptions, guard?: Guard<TResponse>): Promise<TResponse> {
        return this.request<TResponse>("GET", path, undefined, options, guard);
    }

    /**
     * Call HTTP POST request and parse the JSON response
     * @param path Path relative to the base URL
     * @param body Request body
     * @param options Optional request options
     * @param guard An optional guard function to validate the response shape.
     * @returns
     */
    post<TResponse, TBody = unknown>(
        path: string,
        body?: TBody,
        options?: RequestOptions,
        guard?: Guard<TResponse>,
    ): Promise<TResponse> {
        return this.request<TResponse>("POST", path, body, options, guard);
    }

    /**
     * Call HTTP PUT request and parse the JSON response
     * @param path Path relative to the base URL
     * @param body Request body
     * @param options Optional request options
     * @param guard An optional guard function to validate the response shape.
     * @returns
     */
    put<TResponse, TBody = unknown>(
        path: string,
        body?: TBody,
        options?: RequestOptions,
        guard?: Guard<TResponse>,
    ): Promise<TResponse> {
        return this.request<TResponse>("PUT", path, body, options, guard);
    }

    /**
     * Call HTTP PATCH request and parse the JSON response
     * @param path Path relative to the base URL
     * @param body Request body
     * @param options Optional request options
     * @param guard An optional guard function to validate the response shape.
     * @returns
     */
    patch<TResponse, TBody = unknown>(
        path: string,
        body?: TBody,
        options?: RequestOptions,
        guard?: Guard<TResponse>,
    ): Promise<TResponse> {
        return this.request<TResponse>("PATCH", path, body, options, guard);
    }

    /**
     * Call HTTP DELETE request and parse the JSON response
     * @param path Path relative to the base URL
     * @param options Optional request options
     * @param guard An optional guard function to validate the response shape.
     * @returns
     */
    delete<TResponse = void>(path: string, options?: RequestOptions, guard?: Guard<TResponse>): Promise<TResponse> {
        return this.request<TResponse>("DELETE", path, undefined, options, guard);
    }

    /**
     * Call HTTP request and parse the JSON response
     * @param method HTTP method
     * @param path Path relative to the base URL
     * @param body Request body
     * @param options Optional request options
     * @param guard An optional guard function to validate the response shape.
     * @returns
     */
    async request<TResponse>(
        method: string,
        path: string,
        body: unknown,
        options: RequestOptions = {},
        guard?: Guard<TResponse>
    ): Promise<TResponse> {
        const headers = this.buildHeaders({
            "Content-Type" : "application/json",
            ...this.defaultHeaders,
            ...options.headers,
        });

        if (!options.anonymous && this.auth) {
            const authHeader = await this.auth.getAuthHeader();
            if (authHeader) headers["Authorization"] = authHeader;
        }

        const response = await fetch(this.buildUrl(path, options.query), {
            method,
            headers,
            body : body !== undefined ? JSON.stringify(body) : undefined,
            signal : options.signal,
        });

        const rawBody = await response.text();
        const httpError = (message?: string): HttpResponseError =>
            new HttpResponseError(
                response.status,
                response.statusText,
                message,
                rawBody.slice(0, options?.bodySnippetLength ?? 500)
            );

        if (!response.ok) throw httpError();

        if (rawBody.trim().length === 0) {
            throw new HttpResponseError(
                response.status, response.statusText, undefined, `${method}: empty response body`);
        }

        let parsed: unknown;
        try {
            parsed = JSON.parse(rawBody);
        } catch {
            throw httpError(`${method}: response is not valid JSON`);
        }

        if (guard(parsed)) return parsed;

        throw httpError(`${method}: unexpected response shape`);
    }

    private buildHeaders(values: Record<string, HeaderValue>): Headers {
        const headers = new Headers();

        for (const [name, value] of Object.entries(values)) {
            headers.set(name, typeof value === "function" ? value(this) : value);
        }

        return headers;
    }

    private buildUrl(path: string, query?: RequestOptions["query"]): string {
        const base = this.baseUrl.endsWith("/") ? this.baseUrl : `${this.baseUrl}/`;
        const url = new URL(path.replace(/^\//, ""), base);

        if (query) {
            for (const [key, value] of Object.entries(query)) {
                if (value !== undefined) url.searchParams.set(key, String(value));
            }
        }

        return url.toString();
    }
}

