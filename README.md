# PayomAuth SMS verification

The API sends a six-digit verification code through Payom and persists active
challenges and verified phone numbers in PostgreSQL. It verifies phone numbers
only; it does not create users or issue application JWTs.

## Configuration

Configure these values through environment variables in production, or through
.NET User Secrets for local development. `appsettings.json` is intentionally a
safe template and must not contain real credentials or secrets.

| Setting | Description |
| --- | --- |
| `ConnectionStrings:DefaultConnectionString` | PostgreSQL connection string |
| `PayomOptions:BaseUri` | Payom API host assigned in the account; accepts with or without `https://` |
| `PayomOptions:JwtToken` | Payom JWT bearer token |
| `PayomOptions:SenderName` | Approved sender name |
| `PayomOptions:MessageTemplate` | SMS text containing `{code}` |
| `Otp:HashKey` | Stable secret of at least 32 UTF-8 bytes for hashing OTPs |

ASP.NET configuration uses the usual precedence, so environment variables and
User Secrets override `appsettings.json`. Nested settings use double
underscores in environment-variable names, for example
`PayomOptions__JwtToken` and `ConnectionStrings__DefaultConnectionString`.

For local development, from `PayomAuth.Api`, set the required values with
`dotnet user-secrets set`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnectionString" "Host=localhost;Port=5432;Database=payom_auth;Username=postgres;Password=your-password"
dotnet user-secrets set "PayomOptions:BaseUri" "gateway.payom.tj"
dotnet user-secrets set "PayomOptions:JwtToken" "your-payom-token"
dotnet user-secrets set "PayomOptions:SenderName" "payom.tj"
dotnet user-secrets set "Otp:HashKey" "a-stable-secret-with-at-least-32-utf8-bytes"
```

Generate a random hash key with `openssl rand -hex 32` and store its output in
`Otp:HashKey`. Keep the same key across restarts. Payom free-text sending must
be enabled for the account. The application creates its PostgreSQL tables at
startup; the configured database user needs permission to create tables.

## Deploying to Render

This repository includes a `Dockerfile` for Render. See
[RENDER_DEPLOYMENT.md](./RENDER_DEPLOYMENT.md) for the precise service setup,
database configuration, and required environment variables.

## Endpoints

For the complete request contract and UX guidance, see
[FRONTEND_SMS_VERIFICATION.md](./FRONTEND_SMS_VERIFICATION.md).

Request an SMS:

```http
POST /api/auth/sms/send
Content-Type: application/json

{ "telephone": "+992999999999" }
```

Verify the code:

```http
POST /api/auth/sms/verify
Content-Type: application/json

{ "telephone": "+992999999999", "code": "123456" }
```

Successful verification returns `200` with `verified: true`. Codes expire after
five minutes, can be requested once per phone number per minute, and allow five
verification attempts. The send and verify endpoints also share a per-IP limit
of 10 requests per minute. Verified phone numbers are retained in
`verified_phones`; challenge codes are stored only as keyed hashes.

## Frontend integration guide

The frontend calls this backend's public **HTTPS base URL**. It does not call
Payom directly. For example, if the deployed API is at
`https://api.example.com`, the endpoints are:

| Purpose | Request |
| --- | --- |
| Request an SMS code | `POST https://api.example.com/api/auth/sms/send` |
| Verify the SMS code | `POST https://api.example.com/api/auth/sms/verify` |

Replace `https://api.example.com` with the actual public URL provided by the
backend operator. Do not include `/api` in the configured base URL.

### Configure the frontend

For a Vite frontend, add the public API URL to its local `.env.local` file:

```env
VITE_API_BASE_URL=https://api.example.com
```

In the deployed frontend, configure the same variable through its build or
hosting environment settings, using the deployed backend URL. Restart the
frontend development server after editing `.env.local`.

The API base URL is public. **Never put the Payom JWT, database connection
string, or OTP hash key in frontend source code or frontend environment
variables.** Those secrets belong only on the backend.

### Allow the frontend origin through CORS

Browsers block cross-origin calls unless the backend allows the frontend's
origin. The backend operator must set `Cors:AllowedOrigins` in the deployed
API's `appsettings.json` to the exact origin(s) that will call the API. An
origin is the scheme, hostname, and optional port only—no path or trailing
slash. For example:

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:5173",
    "https://your-frontend.example.com"
  ]
}
```

Replace the example frontend domain with the real one. Keep localhost origins
only when local development needs direct browser access. If the frontend uses
a different localhost port, add that exact origin. Redeploy or restart the API
after changing the allowlist. Do not use a wildcard origin.

### Send and verify a code

Example Vite client module:

```js
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL.replace(/\/$/, "");

export async function requestSmsCode(telephone) {
  const response = await fetch(`${apiBaseUrl}/api/auth/sms/send`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ telephone }),
  });

  if (response.status === 202) return;
  if (response.status === 429) {
    throw new Error("Please wait before requesting another code.");
  }
  if (response.status === 502) {
    throw new Error("The SMS could not be sent. Please try again later.");
  }
  throw new Error(`Could not request an SMS code (HTTP ${response.status}).`);
}

export async function verifySmsCode(telephone, code) {
  const response = await fetch(`${apiBaseUrl}/api/auth/sms/verify`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ telephone, code }),
  });

  if (response.ok) return response.json();
  if (response.status === 400) {
    throw new Error("The code is incorrect or expired. Check it or request a new one.");
  }
  if (response.status === 429) {
    throw new Error("Too many requests. Please wait and try again.");
  }
  throw new Error(`Could not verify the code (HTTP ${response.status}).`);
}
```

Send phone numbers in `+992` followed by nine digits. Keep the verification
code as a string because it may start with `0`.

- `202 Accepted` from the send endpoint means Payom accepted the request; it
  does not guarantee delivery.
- `400 Bad Request` from verify means the code is incorrect, expired, or there
  is no active challenge.
- `429 Too Many Requests` means a resend cooldown or request-rate limit applies.
- Codes expire after five minutes, allow five verification attempts, and can
  be requested once per phone number per minute.

Successful verification returns `{ "verified": true, "telephone": "..." }`.
This confirms only that the user passed the phone challenge; it is **not** a
login session or access token. User registration/login and issuing an
application session require a separate backend flow.
