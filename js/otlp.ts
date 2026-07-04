// otel.ts
import { NodeSDK } from "@opentelemetry/sdk-node";
import { getNodeAutoInstrumentations } from "@opentelemetry/auto-instrumentations-node";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { ATTR_SERVICE_NAME, ATTR_SERVICE_VERSION } from "@opentelemetry/semantic-conventions";
import { Span, SpanOptions, SpanStatusCode, Tracer } from "@opentelemetry/api";

// Configure the SDK with auto-instrumentation
const sdk = new NodeSDK({
    resource : resourceFromAttributes({
        [ATTR_SERVICE_NAME] : "my-service",
        [ATTR_SERVICE_VERSION] : "1.0.0",
    }),
    // By default, this exports to http://localhost:4318/v1/traces
    traceExporter : new OTLPTraceExporter(),
    instrumentations : [getNodeAutoInstrumentations()],
});

// Start the SDK
sdk.start();

// Gracefully shut down the SDK on termination signals
const shutdownSignals = ["SIGTERM", "SIGINT"];
for (const signal of shutdownSignals) {
    process.on(signal, () => {
        sdk.shutdown()
            .then(() => console.log("OpenTelemetry SDK shut down successfully"))
            .catch((error) => console.error("Error shutting down OTel", error))
            .finally(() => process.exit(0));
    });
}

export function traceInvocation(
    tracer: Tracer,
    actionName: string,
    options: SpanOptions,
    fn: (span: Span) => Promise<any>,
) {
    return tracer.startActiveSpan(actionName, options, async (span) => {
        try {
            const result = await fn(span);
            span.setAttribute("command.result", result);
            span.setStatus({ code : SpanStatusCode.OK });
            return result;
        } catch (error) {
            span.recordException(error);
            span.setStatus({
                code : SpanStatusCode.ERROR,
                message : (error as Error).message,
            });
        } finally {
            span.end();
        }
    });
}