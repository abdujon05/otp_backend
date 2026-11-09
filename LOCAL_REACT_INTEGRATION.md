# Use the deployed API from a local React app

This guide connects a React app running locally (for example, Vite on
`http://localhost:5173`) to a deployed PayomAuth API.

The API currently allows requests from any browser origin. Your React app must
use the deployed API's public HTTPS URL, not the local API URL.

## 1. Deploy the API

Deploy this project to a public HTTPS address, such as:

```text
https://api.example.com
```

Before deploying, configure the API's database connection, Payom settings, and
OTP hash key in the deployment environment. Do not put those backend secrets
in the React application.

Check that the deployment is reachable:

```text
GET https://api.example.com/api/health
```

It should return a JSON response with a `status` of `Running`.

## 2. Configure the React app

For a Vite React project, create or update `.env.local` in the React project's
root directory:

```env
VITE_API_BASE_URL=https://api.example.com
```

Replace the example URL with the real deployed API URL. Do not add a trailing
`/api` to this value.

Restart the Vite development server after saving the environment file:

```bash
npm run dev
```

## 3. Add an API client

Create `src/services/phoneVerification.js` in the React project:

```js
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL.replace(/\/$/, "");

async function readError(response, fallback) {
  try {
    const body = await response.json();
    return body.detail ?? body.message ?? fallback;
  } catch {
    return fallback;
  }
}

export async function sendVerificationCode(telephone) {
  const response = await fetch(`${apiBaseUrl}/api/auth/sms/send`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ telephone }),
  });

  if (response.status === 202) return;
  if (response.status === 429) {
    throw new Error("Please wait before requesting another code.");
  }

  throw new Error(await readError(response, "Could not send the SMS code."));
}

export async function verifyCode(telephone, code) {
  const response = await fetch(`${apiBaseUrl}/api/auth/sms/verify`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ telephone, code }),
  });

  if (response.ok) return response.json();
  if (response.status === 400) {
    throw new Error("The verification code is invalid or expired.");
  }
  if (response.status === 429) {
    throw new Error("Too many requests. Please wait and try again.");
  }

  throw new Error(await readError(response, "Could not verify the code."));
}
```

## 4. Call it from a React component

Keep both values as strings. In particular, do not convert the code to a
number: verification codes can start with `0`.

```jsx
import { useState } from "react";
import { sendVerificationCode, verifyCode } from "./services/phoneVerification";

export default function VerifyPhone() {
  const [telephone, setTelephone] = useState("+992");
  const [code, setCode] = useState("");
  const [message, setMessage] = useState("");

  async function handleSend() {
    try {
      await sendVerificationCode(telephone);
      setMessage("Code sent. Check your SMS messages.");
    } catch (error) {
      setMessage(error.message);
    }
  }

  async function handleVerify() {
    try {
      const result = await verifyCode(telephone, code);
      setMessage(`Phone verified: ${result.telephone}`);
      // Continue to registration or login. This API does not return a JWT.
    } catch (error) {
      setMessage(error.message);
    }
  }

  return (
    <section>
      <input
        value={telephone}
        onChange={(event) => setTelephone(event.target.value)}
        placeholder="+992999999999"
      />
      <button onClick={handleSend}>Send code</button>

      <input
        value={code}
        onChange={(event) => setCode(event.target.value)}
        inputMode="numeric"
        maxLength={6}
        placeholder="Six-digit code"
      />
      <button onClick={handleVerify}>Verify code</button>
      <p>{message}</p>
    </section>
  );
}
```

## API behavior

- Phone numbers must be `+992` followed by nine digits, such as
  `+992999999999`.
- `POST /api/auth/sms/send` returns `202 Accepted` when Payom accepts the SMS.
- Sending another code to the same phone number is limited to once every 60
  seconds.
- `POST /api/auth/sms/verify` returns `{ "verified": true, "telephone": "..." }`
  when the code is correct.
- Codes expire after five minutes and allow five verification attempts.
- The API limits each IP address to ten send/verify requests per minute.

## Security notes

- Only `VITE_API_BASE_URL` belongs in the frontend environment file; it is a
  public value.
- Never expose the Payom token, database connection string, or OTP hash key in
  React code, Git, or browser storage.
- A successful response verifies phone ownership only. It does not sign the
  user in or issue an access token. Call your separate registration or login
  flow afterward.
