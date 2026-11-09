# Frontend guide: SMS phone verification

This API lets a user prove they can receive SMS messages at a phone number.
It **does not log the user in, create an account, or return an application
authentication token**. After verification, the frontend can continue to the
next product flow, but authenticated features need a separate backend
authentication endpoint.

## Endpoints

Use the API's configured base URL; append these paths:

| Action | Method and path |
| --- | --- |
| Request a verification code | `POST /api/auth/sms/send` |
| Verify the code | `POST /api/auth/sms/verify` |

Both endpoints accept and return JSON. Phone numbers must use Tajikistan's
international format: `+992` followed by nine digits. Send the code as a string
so leading zeroes are preserved.

## 1. Request a code

Send:

```json
{
  "telephone": "+992999999999"
}
```

Example:

```js
const response = await fetch(`${API_BASE_URL}/api/auth/sms/send`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ telephone: "+992999999999" }),
});

if (response.status === 202) {
  // Show the code-entry screen.
} else if (response.status === 429) {
  // Tell the user to wait before requesting another code.
} else {
  // Show a generic delivery error and allow retry.
}
```

Responses:

- `202 Accepted`: the SMS provider accepted the request. This does not guarantee
  the SMS has already arrived.
- `400 Bad Request`: invalid request data, including an invalid phone format.
- `429 Too Many Requests`: the phone is still in its resend cooldown, or the
  request exceeded the per-IP rate limit.
- `502 Bad Gateway`: the SMS provider could not accept the message or timed out.

The resend cooldown is 60 seconds per phone number. Show a countdown in the UI
and disable the resend button until it finishes. The backend is also rate
limited to 10 requests per IP per minute across both verification endpoints.

## 2. Verify the code

Send the same phone number and the six-digit code as a string:

```json
{
  "telephone": "+992999999999",
  "code": "012345"
}
```

Example:

```js
const response = await fetch(`${API_BASE_URL}/api/auth/sms/verify`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    telephone: "+992999999999",
    code: enteredCode,
  }),
});

if (response.ok) {
  const result = await response.json();
  // result is { verified: true, telephone: "+992999999999" }
  // Continue the phone-verification flow; this is not an auth token.
} else if (response.status === 429) {
  // Tell the user they are making requests too quickly.
} else if (response.status === 400) {
  // Ask the user to check the code or request a new one.
} else {
  // Show a generic error and allow retry.
}
```

Responses:

- `200 OK`: `{ "verified": true, "telephone": "+992999999999" }`
- `400 Bad Request`: code is incorrect, expired, or no active challenge exists;
  malformed request values are also rejected.
- `429 Too Many Requests`: the per-IP rate limit was exceeded.

Codes expire after five minutes. A challenge allows up to five verification
attempts. After success, the active challenge is removed and the phone is saved
as verified. If the code expires or attempts are exhausted, let the user
request a new one.

## Frontend behavior and error handling

- Keep the requested phone number for the verify request; do not rely on a
  user-editable value changing between send and verify.
- Keep the code as a string and use a six-digit input. Do not parse it as a
  number because codes can start with `0`.
- Do not show the code in logs, analytics, URLs, or persistent browser storage.
- Handle network errors and non-JSON responses gracefully. Use generic messages
  rather than exposing provider or server details to the user.
- A `202` means the provider accepted the SMS request, not that delivery is
  confirmed. Offer a resend action after the cooldown.
- The API does not currently return a session, access token, or login status.
  Do not treat `{ verified: true }` as authentication.
- Browser clients may need the backend to allow the frontend's origin through
  CORS. If the browser reports a CORS error, configure the allowed frontend
  origin on the API; do not work around it by sending Payom credentials from
  the browser.
