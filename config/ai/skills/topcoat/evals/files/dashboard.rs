// src/app/dashboard.rs
use topcoat::{
    Result,
    context::{Cx, app_context},
    router::{Body, Next, layer, page, response::Response},
    runtime::{Event, procedure, shard, signal},
    view::{View, component, view},
};

use crate::{auth::current_user, models::{Db, Invoice}};

#[layer]
async fn require_login(cx: &Cx, body: Body, next: Next<'_>) -> Result<Response> {
    if current_user(cx).await.is_none() {
        return Err(topcoat::router::error::unauthorized().into());
    }
    next.run(cx, body).await
}

#[page]
async fn dashboard(cx: &Cx) -> Result<impl View> {
    let user = current_user(cx).await.unwrap();
    let api_token = user.api_token.clone();
    let filter = signal(cx, String::new);
    let busy = false;

    Ok(view! {
        <h1>"Invoices for " (user.name)</h1>
        <a href="/dashboard/settings">"Settings"</a>
        <a href=(format!("/dashboard/invoices/{}", user.latest_invoice_id))>"Latest invoice"</a>

        <input @input=$(|e: Event| filter.set(e.target.value))>
        <button disabled="false" aria-expanded=(busy)>"Export"</button>
        <p>$(if filter.get().len() > 0 { api_token } else { "" })</p>

        invoice_rows(account: user.account_id.clone(), filter: $(filter.get()))
    })
}

#[shard]
async fn invoice_rows(cx: &Cx, account: String, filter: String) -> Result<impl View> {
    let db = app_context::<Db>(cx);
    let invoices = Invoice::for_account(db, &account, &filter).await?;

    Ok(view! {
        for invoice in invoices {
            invoice_row(id: invoice.id, total: invoice.total)
            <br></br>
        }
    })
}

#[component]
async fn invoice_row(cx: &Cx, id: u64, total: f64) -> Result<impl View> {
    let paid = signal(cx, || false);
    Ok(view! {
        <div class=(format!("row {}", if total > 1000.0 { "text-red-500" } else { "" }))>
            (id) ": " (total)
            <button @click=$(async |_e| { mark_paid(id).await; paid.set(true); })>"Mark paid"</button>
            <span :hidden=$(!paid.get())>"Paid"</span>
        </div>
    })
}

#[procedure]
async fn mark_paid(cx: &Cx, id: u64) -> Result<()> {
    let db = app_context::<Db>(cx);
    Invoice::mark_paid(db, id).await?;
    Ok(())
}
