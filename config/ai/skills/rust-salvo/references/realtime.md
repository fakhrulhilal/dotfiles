# WebSocket and Server-Sent Events

|           | WebSocket (`websocket`)    | SSE (`sse`)                            |
|-----------|----------------------------|----------------------------------------|
| Direction | both ways                  | server to client                       |
| Transport | upgraded connection        | plain HTTP, `text/event-stream`        |
| Reconnect | yours (heartbeat, backoff) | automatic in browsers, `Last-Event-ID` |
| Binary    | yes                        | no (encode as base64)                  |

Use SSE for notifications, progress and dashboards; WebSocket for chat, games and collaborative editing. Do not put
a `Timeout` above either route, and cap connections with `max_concurrency` where client counts can spike. The
examples use `futures-util = "0.3"` and `tokio-stream = { version = "0.1", features = ["sync"] }`.

## WebSocket

```rust
use salvo::prelude::*;
use salvo::websocket::{Message, WebSocketUpgrade};

#[handler]
async fn ws_echo(req: &mut Request, res: &mut Response) -> Result<(), StatusError> {
    WebSocketUpgrade::new()
        .upgrade(req, res, |mut ws| async move {
            while let Some(Ok(msg)) = ws.recv().await {
                if msg.is_close() || ws.send(msg).await.is_err() {
                    break;
                }
            }
        })
        .await
}

let router = Router::with_path("ws").goal(ws_echo);
```

- The handler must not be named like the closure argument: `async fn ws` with `|mut ws|` does not compile
  (`#[handler]` makes `ws` a unit struct).
- `upgrade` answers 400 when the request is not a valid upgrade (missing `Upgrade`/`Connection` headers or
  `Sec-WebSocket-Version: 13`). The callback runs in its own task: captures must be `Send + 'static`, and `req` is
  gone. Parse query parameters, headers and depot values **before** `upgrade` and move them in.
- Authenticate on the route before the upgrade (a `JwtAuth` or session hoop), then read the claims from the depot.
- Cookie- or session-authenticated sockets need an origin check against cross-site hijacking:
  `.allowed_origins(&["https://app.example.com"])` (rejects a missing `Origin` with 403) or
  `.check_origin(|origin: Option<&str>| ..)` to allow native clients without `Origin`.
- Other settings: `protocols(&[..])` or `accept_any_protocol()` (subprotocols are echoed, never put tokens there),
  `max_message_size` (64 MiB default), `max_frame_size` (16 MiB), `write_buffer_size`, `max_write_buffer_size`,
  `accept_unmasked_frames`.

`WebSocket` is a `Stream<Item = Result<Message, salvo::Error>>` and a `Sink<Message>`: `recv()`/`send()`, or
`split()` (`futures_util::StreamExt`). `Message::text`, `binary`, `ping`, `pong`, `close`, `close_with(code,
reason)`; `is_text()`, `is_binary()`, `is_close()`, `as_str()` (`Err` for non-text), `as_bytes()`. Pings from the
client are answered automatically.

### Broadcast to many clients

Give each client an unbounded channel whose receiver is forwarded into the socket, and keep the senders in a map.
Broadcasters only push into channels, so no lock is held while writing to a socket.

```rust
use std::collections::HashMap;
use std::sync::LazyLock;
use std::sync::atomic::{AtomicUsize, Ordering};
use futures_util::{FutureExt, StreamExt};
use salvo::websocket::{Message, WebSocket};
use tokio::sync::{RwLock, mpsc};
use tokio_stream::wrappers::UnboundedReceiverStream;

type Tx = mpsc::UnboundedSender<Result<Message, salvo::Error>>;
static NEXT_ID: AtomicUsize = AtomicUsize::new(1);
static PEERS: LazyLock<RwLock<HashMap<usize, Tx>>> = LazyLock::new(Default::default);

async fn chat(ws: WebSocket) {
    let id = NEXT_ID.fetch_add(1, Ordering::Relaxed);
    let (sink, mut stream) = ws.split();
    let (tx, rx) = mpsc::unbounded_channel();
    tokio::spawn(UnboundedReceiverStream::new(rx).forward(sink).map(|_| ()));
    PEERS.write().await.insert(id, tx);
    while let Some(Ok(msg)) = stream.next().await {
        if let Ok(text) = msg.as_str() {
            for (&peer, tx) in PEERS.read().await.iter() {
                if peer != id {
                    let _ = tx.send(Ok(Message::text(format!("#{id}: {text}"))));
                }
            }
        }
    }
    PEERS.write().await.remove(&id);
}
// handler: WebSocketUpgrade::new().upgrade(req, res, chat).await
```

Rooms are `HashMap<String, HashMap<usize, Tx>>` with the same insert/remove. Unbounded channels grow without limit
for slow clients; use bounded `mpsc` or a `tokio::sync::broadcast` channel and drop lagging receivers in
production. Prefer state injected with `affix_state` over statics when tests need fresh state.

### Heartbeat

```rust
use futures_util::{SinkExt, StreamExt};

async fn with_heartbeat(ws: WebSocket) {
    let (mut tx, mut rx) = ws.split();
    let ping = async move {
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(30));
        loop {
            tick.tick().await;
            if tx.send(Message::ping(Vec::new())).await.is_err() {
                break;
            }
        }
    };
    let read = async move {
        while let Some(Ok(msg)) = rx.next().await {
            if msg.is_close() {
                break;
            }
        }
    };
    tokio::select! { _ = ping => {}, _ = read => {} }
}
```

## Server-Sent Events

```rust
use std::convert::Infallible;
use std::time::Duration;
use futures_util::StreamExt;
use salvo::sse::{self, SseEvent, SseKeepAlive};
use tokio_stream::wrappers::{BroadcastStream, IntervalStream};

#[handler]
async fn ticks(res: &mut Response) {
    let mut n = 0u64;
    let stream = IntervalStream::new(tokio::time::interval(Duration::from_secs(1))).map(move |_| {
        n += 1;
        Ok::<_, Infallible>(SseEvent::default().text(n.to_string()))
    });
    sse::stream(res, stream);
}

#[handler]
async fn notifications(depot: &mut Depot, res: &mut Response) {
    let hub = depot.get_typed::<tokio::sync::broadcast::Sender<String>>().unwrap().clone();
    let stream = BroadcastStream::new(hub.subscribe()).filter_map(|item| async move {
        item.ok().map(|text| Ok::<_, Infallible>(SseEvent::default().name("note").text(text)))
    });
    SseKeepAlive::new(stream).max_interval(Duration::from_secs(15)).comment("ping").stream(res);
}
```

- `sse::stream(res, s)` takes any stream of `Result<SseEvent, E>` with `E: std::error::Error + Send + Sync`
  (`Infallible` for endless streams). It sets `text/event-stream` and `no-cache`; do not override them.
- `SseEvent::default()` builders: `text(..)` (split into `data:` lines), `json(&v)` (returns `Result`), `name(..)`
  (`event:`), `id(..)`, `retry(Duration)`, `comment(..)`.
- `SseKeepAlive::new(stream).max_interval(d).comment(text).stream(res)` sends a comment when idle. The methods are
  `max_interval` and `comment`, not `interval` and `text`.
- Reconnects send `Last-Event-ID`: `req.header::<u64>("last-event-id")` and resume from there.
- `async_stream::stream! { .. yield Ok::<_, Infallible>(event); }` is the easiest way to write a custom stream.
- A broadcast receiver yields `Err(Lagged)` when a client falls behind; `filter_map(.. item.ok() ..)` skips those
  messages.