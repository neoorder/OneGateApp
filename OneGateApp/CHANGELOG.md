# OneGate App Changelog

All notable changes to the OneGate app are documented in this file. Only changes
that affect the application itself are included.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Version ranges follow maintainer-approved commit boundaries, and each version
date is the date of its final commit. Later changes are listed under Unreleased.

## Unreleased

### Added

- Added opt-in encrypted remote DApp debugging with trusted LAN pairing, explicit request approval, logs, traces, JavaScript evaluation, and visible-WebView screenshots.
- Added per-platform DApp availability for Android, iOS, Mac Catalyst, and Windows.

### Changed

- Redesigned the Developer Center around a top-level developer-mode switch, grouped quick DApp launching and remote debugging, and hid debug tools while developer mode is disabled.
- Replaced the in-app email DApp submission form with the canonical GitHub issue form.

## 2.1.0 - 2026-07-18

### Added

- Added Activity Center, global search, expanded contact management, and redesigned contact selection.
- Added payment and authentication QR/deep-link generators and a wallet Security Center.
- Added a gaming hub, developer game performance HUD, in-app developer tools, and DApp log utilities.
- Added DApp reporting and submission flows, restricted-content settings, and catalog visibility controls.

### Changed

- Refreshed the visual system, wallet home, DApp browser and details, receive flow, asset picker, and message-signing review.
- Improved embedded DApps with document-start dAPI injection, bridge consistency, storage, media playback, fullscreen and orientation handling, and camera and motion permissions.
- Improved DApp cache versioning and separated cache data from wallet data.
- Improved accessibility semantics across wallet flows.
- Configured signing and package creation for Mac Catalyst release builds.

### Fixed

- Prevented sends from continuing after signing failures and surfaced reverted transaction execution as a failure.
- Improved transaction confirmation retry handling and send-address safety.
- Fixed the NEP-20/NEP-21 signature field order and a possible crash when opening a DApp.

## 2.0.3 - 2026-05-06

### Added

- Added developer mode and DApp testing tools.
- Added adaptive layouts, flyout branding, and a responsive news layout.

### Changed

- Added localized DApp tag filtering and support for query strings when launching DApps.

### Fixed

- Fixed DApp launching and several layout issues across iOS, Mac Catalyst, and Windows.
- Fixed duplicate-word handling during mnemonic verification, Apple platform metadata, and window resource cleanup.
- Fixed the About page on iOS.

## 2.0.2 - 2026-04-25

### Added

- Added Add to Home Screen support on Android and Windows.
- Added localized copied-to-clipboard notifications.

### Changed

- Improved page layouts, safe-area behavior, scrolling, and transaction-history labels.

### Fixed

- Fixed startup issues on iOS and improved iOS transaction details.
- Fixed biometric, authentication, wallet refresh, and language-selection flows.
- Prevented GAS transfers from exceeding the available balance after fees.
- Fixed popup positioning and general UI issues across platforms.

## 2.0.1 - 2026-04-21

### Added

- Imported the initial cross-platform OneGate application project.
- Added copying support when exporting wallet keys.

### Fixed

- Fixed startup crashes on iOS and Windows and an Android XAML theme crash.
- Fixed crashes when navigating between protected pages.
- Fixed wallet valuation display and multiple tab, asset picker, contact picker, search, and news settings issues.
