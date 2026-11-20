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

## Database migrations

Apply pending migrations during deployment, before starting API instances:

```text
dotnet ef database update --project backend/DailyChessPuzzleApi
```

If the API runs behind a reverse proxy, configure its trusted IP addresses under
`ForwardedHeaders:KnownProxies`. Do not accept forwarded headers from untrusted
clients.
