# Static files, uploads and downloads

## Static directories (`serve-static`)

```rust
use salvo::serve_static::{StaticDir, StaticFile};

let router = Router::new()
    .push(Router::with_path("api/{**rest}").goal(api))                   // specific routes first
    .push(Router::with_path("favicon.ico").get(StaticFile::new("static/favicon.ico")))
    .push(Router::with_path("{**path}").get(StaticDir::new(["static", "public"]).defaults("index.html")));
```

- `StaticDir::new(roots)` takes one path or a list tried in order (first hit wins; good for theme overrides). It
  reads the remaining path from the route's wildcard, so mount it under `{**path}`.
- Builders: `defaults(names)` (index file for a directory), `fallback(name)` (served when nothing matches: SPAs),
  `auto_list(bool)` (directory listing), `include_dot_files(bool)`, `exclude(|path: &str| bool)`,
  `chunk_size(bytes)`, `disposition_type(..)`, `use_content_type_options(bool)`.
- Pre-compressed variants are served automatically when present and accepted: `file.br`, `file.zst`, `file.gz`,
  `file.deflate`. `compressed_variation(..)` changes the extensions.
- Responses carry ETag, Last-Modified and Range support, and `X-Content-Type-Options: nosniff`. SVG and other XML
  files default to `Content-Disposition: attachment` (uploaded XML cannot run in your origin); for trusted assets
  use `.disposition_type("inline")`.
- There is no cache-control builder. Add a hoop:

```rust
use salvo::http::header::{CACHE_CONTROL, HeaderValue};

#[handler]
async fn immutable(res: &mut Response) {
    res.headers_mut().insert(CACHE_CONTROL, HeaderValue::from_static("public, max-age=31536000, immutable"));
}
let assets = Router::with_path("assets/{**path}").hoop(immutable).get(StaticDir::new("dist/assets"));
```

Embedded assets (single binary) with `rust-embed = "8"`:

```rust
use rust_embed::RustEmbed;
use salvo::serve_static::static_embed;

#[derive(RustEmbed)]
#[folder = "dist"]
struct Assets;

let router = Router::with_path("{**path}").get(static_embed::<Assets>().fallback("index.html"));
```

## Uploads

Multipart parts are written to a temporary directory that is deleted when the request ends; copy or move the file
before returning.

```rust
#[handler]
async fn upload(req: &mut Request) -> Result<String, StatusError> {
    let file = req.file("file").await.ok_or_else(|| StatusError::bad_request().brief("no file"))?;
    let kind = file.content_type().map(|m| m.essence_str().to_owned()).unwrap_or_default();  // Option<Mime>
    if !["image/png", "image/jpeg"].contains(&kind.as_str()) || file.size() > 5 * 1024 * 1024 {
        return Err(StatusError::bad_request().brief("png or jpeg up to 5 MiB"));
    }
    let name = format!("{}.img", uuid::Uuid::new_v4());       // never trust file.name() as a path
    tokio::fs::copy(file.path(), format!("uploads/{name}")).await
        .map_err(|_| StatusError::internal_server_error())?;
    Ok(name)
}
```

- `req.file(name)` → `Option<&FilePart>`; `req.files(name)` → `Option<&Vec<FilePart>>`; `req.first_file()`;
  `req.all_files()` → `Vec<&FilePart>`. Text fields of the same form: `req.form::<T>(name).await`. All of these
  parse once and cache, so mix them freely.
- `FilePart`: `name()` (client file name, `Option<&str>`), `content_type()` (`Option<Mime>`, client-supplied),
  `size()`, `path()` (temporary file), `headers()`.
- Limit the body with `max_size(bytes)` (feature `size-limiter`, 413), not by checking `size()` afterwards.
- Raw bodies: `req.payload().await` (`&Bytes`, default size limit) or `req.payload_with_max_size(n)`.
- Document multipart endpoints with `request_body(content = Form, content_type = "multipart/form-data")`
  (openapi.md).

## Downloads with `NamedFile`

```rust
use salvo::fs::NamedFile;

#[handler]
async fn download(req: &mut Request, res: &mut Response) {
    let name = req.param::<String>("name").unwrap_or_default();
    if name.contains(['/', '\\']) || name.contains("..") {
        res.render(StatusError::bad_request());
        return;
    }
    NamedFile::builder(format!("files/{name}"))
        .attached_name(&name)                          // Content-Disposition: attachment
        .send(req.headers(), res)
        .await;                                        // returns (); renders 404 or 500 itself
}
```

- `send` returns `()`: a missing file renders `StatusError::not_found()`, an unreadable one a 500, both through the
  catcher. `.build().await` returns a `Result<NamedFile>` when you need to handle errors yourself.
- Builders: `attached_name`, `content_type(mime)` (a `Mime`, e.g. `salvo::http::mime::APPLICATION_PDF`, not a
  string), `disposition_type`, `use_etag(bool)`, `buffer_size`. Conditional requests (`If-None-Match`,
  `If-Modified-Since`) and `Range` are handled.
- Path values are percent-decoded (`a%2F..%2Fsecret` arrives as `a/../secret`), so check the decoded name as above,
  or resolve and compare with `canonicalize` against the base directory.