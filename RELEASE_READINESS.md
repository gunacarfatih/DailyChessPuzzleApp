# Release readiness operations

## Android signing and Google sign-in

Release builds require `ANDROID_KEYSTORE_PATH`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, and `ANDROID_STORE_PASSWORD`. Configure the Android OAuth client in Google Cloud with the SHA-1 fingerprint of the exact release signing certificate (or Play App Signing certificate when distributed by Play). A missing or mismatched fingerprint prevents Google sign-in on that installed build.

## Reverse proxy and rate limits

The API trusts forwarded headers only from addresses in `ForwardedHeaders:KnownProxies`. Set the actual reverse proxy address in production (for environment configuration, use `ForwardedHeaders__KnownProxies__0=<proxy-ip>`). Do not trust arbitrary forwarded headers from public clients. Verify registration limits from separate client IPs after deployment.

## Database migrations and deployment

On Production startup, the API applies pending EF Core migrations before serving requests. The remote `deploy <sha>` script is not part of this repository, so confirm it starts the published container and waits for health checks. Ensure the API database identity has migration privileges during rollout.

## CI and deployment branches

Backend CI, Redis integration tests, and Flutter analysis/tests run on pushes to `main` and the configured development/fix branches. The ARM64 image is published on pushes to `main`; the manual production workflow accepts only a successful CI run for the requested SHA on `main`.

## Store privacy materials

Complete [PRIVACY.md](PRIVACY.md), publish it at a stable public URL, and link that URL from both store listings and the app. Fill the Play Data safety and Apple App Privacy forms from the actual production SDKs, SMTP provider, hosting, retention, and deletion behavior. Confirm the platform location permission descriptions match the country-only lookup described in the policy.
