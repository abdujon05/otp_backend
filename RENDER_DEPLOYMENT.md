# Deploy PayomAuth on Render

The repository includes a Dockerfile, so Render can build and run this .NET 10
application without a native .NET runtime selection.

## Before you begin

1. Rotate the database password and Payom token that were previously stored in
   local configuration.
2. Push the project, including `Dockerfile`, to GitHub, GitLab, or Bitbucket.
3. Keep all secrets out of Git. `appsettings.json` is a safe template only.

## Create PostgreSQL

In the Render dashboard, select **New** > **PostgreSQL**. Create the database
in the same Render region as the API. After it is ready, open its **Connect**
page and use its internal host and credentials for the API service.

Create an Npgsql connection string from those values:

```text
Host=<internal-host>;Port=5432;Database=<database>;Username=<user>;Password=<password>
```

The internal Render host works only for services in the same Render workspace
and region. Do not use it from a local computer.

## Create the web service

1. Select **New** > **Web Service** and connect the repository.
2. Select the deployment branch.
3. Set **Runtime** to `Docker`.
4. Leave the Dockerfile path as `./Dockerfile`.
5. Set **Health Check Path** to `/api/health`.
6. Choose a compute plan and click **Create Web Service**.

Render builds the Dockerfile and starts the API on port `10000`. No Render build
or start command is required for this Docker-based service.

The Docker image enables ASP.NET Core forwarded headers so HTTPS redirection
recognizes Render's TLS-terminating proxy correctly.

## Add environment variables

Open the web service's **Environment** page and add these variables. Replace
every placeholder with a real deployment value:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnectionString=Host=<internal-host>;Port=5432;Database=<database>;Username=<user>;Password=<password>
PayomOptions__BaseUri=gateway.payom.tj
PayomOptions__JwtToken=<rotated-payom-token>
PayomOptions__SenderName=<approved-sender-name>
PayomOptions__MessageTemplate=YOUR CODE: {code}
Otp__HashKey=<new-stable-random-secret-at-least-32-utf8-bytes>
```

Generate the OTP key once with:

```bash
openssl rand -hex 32
```

Do not change that key after users have active verification challenges, because
existing codes are hashed with it. Render keeps environment variables secret
and makes them available to the service at runtime.

## Verify the deployment

After Render reports the deploy as live, open:

```text
https://<your-service-name>.onrender.com/api/health
```

The endpoint must return HTTP 200 before configuring the frontend. The API
creates the required PostgreSQL tables at startup, so confirm the configured
database account can create tables.

## Connect the local React app

In the React project's `.env.local`, set:

```env
VITE_API_BASE_URL=https://<your-service-name>.onrender.com
```

Restart the React development server. The API currently allows browser requests
from any origin, so a local React development server can call the SMS endpoints.

## Operational notes

- Render's health check uses `GET /api/health`.
- Monitor the Render service logs for API startup, PostgreSQL, and Payom errors.
- The application creates tables at startup; adopt migrations before making
  destructive schema changes in later releases.
- A successful phone verification is not an application login. Add a separate
  registration or authentication flow if the frontend needs user sessions.
