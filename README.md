# Daily Chess Puzzle

## API configuration

Release Flutter builds must set an HTTPS API URL:

```text
flutter build appbundle --dart-define=API_BASE_URL=https://api.example.com
```

The HTTP address in `ApiConstants` is for local development only. Android allows
cleartext traffic only in the debug manifest.

## Android release signing

Set `ANDROID_KEYSTORE_PATH`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, and
`ANDROID_STORE_PASSWORD` in the release build environment. Release builds do not
use the debug signing key.

## iOS Google Sign-In

Set Xcode build settings `GOOGLE_IOS_CLIENT_ID` and
`GOOGLE_IOS_REVERSED_CLIENT_ID` from the iOS OAuth client in Google Cloud. The
web OAuth client ID used by the API is not a substitute for the iOS client.
Register the Android package ID and signing certificate with Google as well.

## Email verification (SMTP)

Email/password registration requires SMTP settings. Configure these values with
User Secrets or environment variables before registering accounts:

- `Email__Smtp__Host`
- `Email__Smtp__Port` (usually `587`)
- `Email__Smtp__Username`
- `Email__Smtp__Password`
- `Email__Smtp__FromEmail`
- `Email__Smtp__FromName` (optional)
- `Email__Smtp__EnableSsl` (defaults to `true`)

Existing accounts stay verified after the migration. New password accounts
must verify the emailed six-digit code before they can log in. Google accounts
are already email-verified by Google.

Access tokens last 60 minutes and refresh tokens last 30 days. Refresh tokens
are stored as hashes in the database and rotated on every refresh. Apply the
`AddRefreshTokens` migration before launching the updated API.

## Database migrations

Apply pending migrations during deployment, before starting API instances:

```text
dotnet ef database update --project backend/DailyChessPuzzleApi
```

For local PostgreSQL and Redis, run `docker compose up -d`. The development
PostgreSQL connection string is
`Host=localhost;Port=5432;Database=dailychess;Username=dailychess;Password=dailychess-local`.
The API health endpoint is available at `/health`.

GitHub Actions builds the backend and runs Flutter formatting and static
analysis on pushes and pull requests.

If the API runs behind a reverse proxy, configure its trusted IP addresses under
`ForwardedHeaders:KnownProxies`. Do not accept forwarded headers from untrusted
clients.
