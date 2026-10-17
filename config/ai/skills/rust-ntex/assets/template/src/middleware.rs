//! A middleware and a filter around the same request id.
use std::convert::Infallible;

use ntex::http::header::{HeaderName, HeaderValue};
use ntex::web::{WebRequest, WebResponse};
use ntex::{Ctx, Middleware, Service};

use crate::AppState;

const X_REQUEST_ID: HeaderName = HeaderName::from_static("x-request-id");

/// Gives every request an `x-request-id` and echoes it on the response. Middleware, because it touches both. The
/// fallback is the worker's request counter, which only shows the shape: use a UUID or similar in a real service.
pub struct AssignRequestId;

pub struct AssignRequestIdService<S> {
    service: S,
}

impl<S> Middleware<S, AppState> for AssignRequestId {
    type Service = AssignRequestIdService<S>;

    // Runs each time the application service is built, which is once per connection: keep it cheap.
    fn create(&self, _: &AppState, service: S) -> Self::Service {
        AssignRequestIdService { service }
    }
}

impl<S, R> Service<AppState, WebRequest<R>> for AssignRequestIdService<S>
where
    S: Service<AppState, WebRequest<R>, Res = WebResponse>,
{
    type Res = WebResponse;
    type Error = S::Error;

    // Without these the middleware reports itself always ready and never shuts the inner service down.
    ntex::forward_ready!(AppState, service);
    ntex::forward_shutdown!(AppState, service);

    async fn call(&self, mut req: WebRequest<R>, ctx: Ctx<'_, Self, AppState>) -> Result<WebResponse, S::Error> {
        let requests = &ctx.st().requests;
        requests.set(requests.get() + 1);

        let id = match req.headers().get(&X_REQUEST_ID) {
            Some(id) => id.clone(),
            None => HeaderValue::from(requests.get()),
        };
        req.headers_mut().insert(X_REQUEST_ID, id.clone());

        // Always call the inner service through `ctx` so readiness is respected. An error from a filter arrives
        // here as `Err` and is rendered outside this middleware, so it does not get the header.
        let mut res = ctx.call(&self.service, req).await?;
        res.headers_mut().insert(X_REQUEST_ID, id);
        Ok(res)
    }
}

/// The id as typed request state, so handlers get it as their second argument.
pub struct RequestId(pub String);

/// Filter: turns `WebRequest<()>` into `WebRequest<RequestId>`. Filters run after all middleware of their level and
/// only on the way in; returning `Err` stops the request.
pub async fn request_id(_: &AppState, req: WebRequest<()>) -> Result<WebRequest<RequestId>, Infallible> {
    let id = req
        .headers()
        .get(&X_REQUEST_ID)
        .and_then(|v| v.to_str().ok())
        .unwrap_or_default()
        .to_owned();
    Ok(req.map_state(|()| RequestId(id)))
}
