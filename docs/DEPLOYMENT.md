# Deploying Playbook (paid launch)

Playbook is a single Blazor Server app on Fly.io (`playbook-genie`), sold through Lemon Squeezy as a one-time
purchase. Access is a Lemon Squeezy **license key**: customers buy → Lemon Squeezy emails a key → they enter it on
`/unlock` → a signed, encrypted cookie keeps them signed in. Refunded/disabled keys stop working.

## How access works

| Piece | Where |
| --- | --- |
| Public pages (landing, terms, privacy, refunds, unlock, welcome) | `Components/Pages/*`, no sign-in |
| Product pages | everything under `Features/` — `[Authorize]` is applied to the whole folder by `Features/_Imports.razor` (default-deny) |
| License check | `LemonSqueezyLicenseService` — activates the key, **rejects keys from any other store/product** |
| Sign-in cookie | `playbook.session`, encrypted with Data Protection keys persisted on the volume (`/data/dp-keys`) |
| Re-validation | every `Access__RevalidateHours` (12h) the key is re-checked; refunded/disabled → signed out. A Lemon Squeezy outage never locks anyone out (7-day grace) |
| Instant revocation | `POST /webhooks/lemonsqueezy` (HMAC-verified) on `license_key_updated` |
| Per-customer data | `/data/users/<hash of key>/…` — leagues, team choice, drafts, personal learning |

## One-time setup (≈ 30 minutes)

### 1. Lemon Squeezy
1. Create the store, then a product **Playbook** at **$4.99**, single payment.
2. In the product → *Variants* → enable **Generate license keys**. Set **Activation limit** to ~5–10 (each browser
   you sign in on uses one activation) and **Expires**: never.
3. Product → *Share* → copy the **checkout URL** (`https://<store>.lemonsqueezy.com/checkout/buy/<uuid>`).
4. Product → *Confirmation modal* / *Receipt button*: set the redirect to `https://<your-domain>/welcome`.
5. Note your **Store ID** (Settings → Stores) and **Product ID** (product page URL or API).
6. Settings → *Webhooks* → add `https://<your-domain>/webhooks/lemonsqueezy`, events **license_key_updated**
   (and optionally `order_refunded` for your own records), choose a **signing secret** (long random string).
7. Complete payout/tax onboarding and store approval. Lemon Squeezy reviews the site: the Terms, Privacy and Refund
   pages are already live at `/terms`, `/privacy`, `/refunds`.

### 2. Fly.io secrets and settings
```bash
fly secrets set \
  Access__StoreId=<store id> \
  Access__ProductId=<product id> \
  Access__CheckoutUrl="https://<store>.lemonsqueezy.com/checkout/buy/<uuid>" \
  Access__WebhookSecret="<signing secret>" \
  Access__SupportEmail="you@yourdomain.com" \
  PropLines__OddsApi__ApiKey="<the-odds-api key>" \
  DraftImageIngestion__ApiKey="<anthropic key>"          # optional: draft screenshot import
# optional
fly secrets set Analytics__PlausibleDomain="yourdomain.com" Access__AdminUserIds="<your account id>"
```
The app **refuses to start** in production if `StoreId`, `ProductId`, `CheckoutUrl` or `WebhookSecret` are missing
(the old version keeps running — Fly's health check on `/healthz` fails the new one). `Access__Mode=Open` disables
the paywall entirely; never set it in production.

### 3. Domain
`fly certs add <your-domain>` and point DNS at Fly. Then make sure `AllowedHosts` / the Lemon Squeezy URLs above use
that domain. (`playbook-genie.fly.dev` works too.)

### 4. Ship
Merge to `main`. `.github/workflows/fly-deploy.yml` builds, runs the tests, then `flyctl deploy`. Needs the
`FLY_API_TOKEN` repository secret (already configured for the current app).

### 5. Verify with a real purchase
Lemon Squeezy *test mode*: buy with card `4242 4242 4242 4242`, receive the key, unlock, connect a Sleeper league,
refund the order in the dashboard and confirm the key stops working (instantly via webhook, or within 12 h).
Then switch the store to live mode (live mode has its own product/ids/secret — update the Fly secrets).

## Operations
* **Logs / errors:** `fly logs`. Unhandled exceptions are logged with a request id that customers see on the error
  page. For alerting, add Sentry later (`Sentry.AspNetCore`) — not needed to launch.
* **Operator monitor:** the Dashboard's provider/sync monitor is hidden from customers. To see it, put your account
  id (Account page) in `Access__AdminUserIds`.
* **Deleting a customer's data:** delete `/data/users/<id>` over `fly ssh console`.
* **Scaling:** customer data is file-backed on one volume → run exactly **one** machine. Moving to a database is
  the first step before horizontal scaling.
* **Backups:** `fly volumes snapshots list` (Fly takes daily snapshots by default).
* **Lost keys:** customers can retrieve them from their Lemon Squeezy receipt/“My Orders”.

## Local development
`dotnet run --project src/Playbook.Web` runs in Development with `Access:Mode=Open` (no paywall). To exercise the
paywall locally set `Access__Mode=LicenseRequired` plus the settings above.
