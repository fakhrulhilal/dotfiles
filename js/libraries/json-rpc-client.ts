import { AuthStrategy, HeaderValue, HttpClient, HttpResponseError, RequestOptions } from "./http-client";
import { Guard, trustShape } from "./language";

export interface JsonRpcError {
    error: {
        code: number;
        message: string;
        data?: {
            type: string;
            message: string;
            stack_trace: string[];
        };
    };
}

export interface JsonRpcResponse<T> {
    result: T;
}

interface IdempontencyKey {
    idempotency_key: string;
}

function isObject(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

export class JsonRpcHttpClient extends HttpClient {
    constructor(
        baseUrl: string,
        auth?: AuthStrategy,
        defaultHeaders: Record<string, HeaderValue> = {},
        private readonly config?: {
            idempotencyPath?: string
        }
    ) {
        const headers: Record<string, HeaderValue> = {
            'Request-Id' : () => crypto.randomUUID(),
            ...defaultHeaders,
        };
        super(baseUrl, auth, headers);
    }

    /**
     * Invoke a JSON-RPC method but going through regular REST endpoint.
     * @param path Path relative to the base URL
     * @param method HTTP method
     * @param body Request body
     * @param options
     * @param guard Type checker for the response shape.
     * @returns Response body inside `{ result: T }` envelope.
     */
    async invokeRest<T>(
        path: string,
        method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE",
        body: unknown,
        options: RequestOptions & { idempotent?: boolean } = {},
        guard?: Guard<JsonRpcResponse<T>>,
    ): Promise<T> {
        if (options.idempotent) {
            if (!this.config?.idempotencyPath) {
                throw new Error("Idempotency path not configured");
            }

            const idempotencyToken = await this.getIdempotencyToken();
            options.headers = {
                ...options.headers,
                'Idempotency-Key' : idempotencyToken,
            };
        }
        const response = await super.request<JsonRpcError | JsonRpcResponse<T>>(
            method,
            path,
            body,
            options,
            (response): response is JsonRpcError | JsonRpcResponse<T> =>
                errorGuard(response) || (guard?.(response) ?? resultEnvelope(trustShape<T>())(response)),
        );

        if (errorGuard(response)) {
            throw new HttpResponseError(response.error.code, response.error.message, `JSON-RPCerror: ${response.error.message}`);
        }

        return response.result;
    }

    private async getIdempotencyToken(): Promise<string> {
        const response = await this.post<JsonRpcResponse<IdempontencyKey>>(
            this.config.idempotencyPath,
            undefined,
            { anonymous : true },
            resultEnvelope((result): result is { idempotency_key: string } =>
                isObject(result) && "idempotency_key" in result &&
                typeof result.idempotency_key === "string",
            )
        );
        return response.result.idempotency_key;
    }
}

/**
 * Builds a guard for `{ result: T }` given a guard for `T`.
 */
export function resultEnvelope<T>(inner: Guard<T>): Guard<JsonRpcResponse<T>> {
    return (response): response is JsonRpcResponse<T> =>
        isObject(response) && "result" in response &&
        inner((response as { result: unknown }).result);
}

const errorGuard: Guard<JsonRpcError> = (response): response is JsonRpcError =>
    isObject(response) && "error" in response && isObject(response.error) &&
    "data" in response.error && isObject(response.error.data);

/**
 * Builds a guard for `{ result: { data: T } }`.
 */
export function resultDataEnvelop<T>(inner: Guard<T>): Guard<JsonRpcResponse<{ data: T }>> {
    return resultEnvelope((result): result is { data: T } =>
        isObject(result) && "data" in result &&
        isObject((result as { data?: unknown }).data) &&
        inner((result as { data: T }).data)
    )
}
