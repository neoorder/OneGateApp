# Android WebView compatibility gate

Android providers without `DOCUMENT_START_SCRIPT` previously opened a dApp without
installing OneGate's wallet provider, leaving the page unable to connect. The
Android launch page now checks this capability before it assigns a URL or creates
a native WebView handler. Unsupported devices see a native, localized page with
Update WebView and Close dApp actions. No late JavaScript injection is attempted.

`WebViewLaunchSupport` holds the capability decision and builds update links for
the installed provider package. The native page obtains that package through
AndroidX `WebViewCompat.GetCurrentWebViewPackage`; if absent, the link targets
`com.google.android.webview`. The market link is tried first, followed by its
HTTPS Google Play page. If neither can open, native guidance explains how to
update from the device's app store. Package names are URL-escaped. Closing uses
the existing close-window/back-navigation behavior.

Example: package `com.android.chrome` opens
`market://details?id=com.android.chrome`; an unsupported launch never writes
recently opened history or records a wallet connection. After an update the user
restarts OneGate and reopens the dApp. This is a local capability check, with no
wallet access, data migration, extra permissions, or background network requests.
The policy has constant cost. iOS and other platform launch paths are unchanged.

## Tests

The independent harness links the production helper. Tests cover supported and
unsupported providers, unknown package fallback, URI encoding, successful store
opening, browser fallback, failure, and all 15 locale resources.

```sh
dotnet test tests/p2-16/OneGate.WebViewSupport.Tests.csproj
```

## Simulator validation before commit

1. Android normal capability: open a dApp and verify normal wallet discovery.
2. In a temporary QA build, force the `documentStartScriptSupported` argument to
   `false` in `LaunchDAppPage.Android.cs`. Do not commit this override.
3. Open a dApp from catalog and an app link: native upgrade page appears, no
   remote content/bridge loads, no recently-opened or connection entry is added.
4. Verify Update targets the installed WebView package, the HTTPS fallback on an
   emulator without Play Store, and manual instructions if no handler exists.
5. Verify Close returns to the prior page or closes the dApp document window.
   Check English, simplified Chinese, and large text without clipped controls.
6. Remove the override and rebuild. Verify an iOS dApp still loads normally.

Keep screenshots outside the repository. Release with the normal app build after
both simulators and review pass.

Sources: [AndroidX capability detection](https://developer.android.com/reference/androidx/webkit/WebViewFeature)
and [installed WebView provider](https://developer.android.com/reference/androidx/webkit/WebViewCompat#getCurrentWebViewPackage(android.content.Context)).
