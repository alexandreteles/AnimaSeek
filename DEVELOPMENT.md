# Developing AnimaSeek

AnimaSeek is an iOS port with a native programmatic UIKit application hierarchy over the portable Seeker core. The `net10.0-ios` head includes the four-tab shell and full feature navigation, platform-service composition, sandbox storage, reviewed settings import, typed URL and notification routing, background-task registrations, continued-processing transfers, NAT-PMP port mapping, an on-simulator filesystem harness, and semantic-release-driven release/AltStore automation in CI (unsigned IPAs with SBOM and provenance attestations; signed IPAs activate once signing secrets exist). Credentialed device signing, notarization, AltStore publication, and the complete real-device/network acceptance matrix remain external; development and automated testing default to `Debug Mock`, while release builds always use the non-mock `Release` configuration. See [DESIGN.md](DESIGN.md) for the design system, parity outcomes, and implementation evidence, and [PORTING.md](PORTING.md) for the original migration plan.

## Requirements

The known-good local toolchain is:

- macOS 26 on Apple silicon;
- Xcode 26.6 with an iOS 26 simulator runtime;
- .NET SDK 10.0.302 exactly (`global.json` pins `"version": "10.0.302"` with `"rollForward": "disable"` so release artifacts stay reproducible — no other SDK version satisfies it; confirm with `dotnet --list-sdks`);
- the .NET `ios` workload; and
- Rider 2026.2 or another editor, if desired.

The app has an iOS 26.0 deployment floor and is iPhone-only. Install the full Xcode application, not only the Command Line Tools, then finish Xcode's first-launch setup:

```sh
sudo xcode-select --switch /Applications/Xcode.app/Contents/Developer
sudo xcodebuild -runFirstLaunch
xcodebuild -version
xcrun simctl list runtimes
```

If changing the machine-wide Xcode selection is unavailable or undesirable, set the developer directory in each shell instead:

```sh
export DEVELOPER_DIR="/Applications/Xcode.app/Contents/Developer"
xcodebuild -version
```

The commands below use a task-specific variable so either a system or user-local .NET installation can be selected. For a normal installation use `ANIMASEEK_DOTNET=dotnet` after verifying that `dotnet --list-sdks` includes exactly `10.0.302`. For a user-local SDK installed by `dotnet-install.sh --version 10.0.302 --install-dir "$HOME/.dotnet"`, use:

```sh
ANIMASEEK_DOTNET="$HOME/.dotnet/dotnet"
"$ANIMASEEK_DOTNET" --info
"$ANIMASEEK_DOTNET" workload install ios
```

Run all remaining commands from the repository root.

## iOS development

Open [Seeker.iOS.slnf](Seeker.iOS.slnf) in Rider to load the iOS head, portable core, unit tests, Soulseek.NET, and Mono.Nat without loading the temporary Android head. `Seeker.sln` remains available when work needs both platforms.

`Debug Mock` defines `MOCK` and composes `MockSoulseekClient`, so it is the default configuration for platform and UI development without connecting to the live Soulseek network. Restore and build the Apple-silicon simulator app with:

```sh
"$ANIMASEEK_DOTNET" restore Seeker.iOS/Seeker.iOS.csproj \
  --runtime iossimulator-arm64

"$ANIMASEEK_DOTNET" build Seeker.iOS/Seeker.iOS.csproj \
  --configuration "Debug Mock" \
  --framework net10.0-ios \
  --runtime iossimulator-arm64 \
  --no-restore \
  -p:ArchiveOnBuild=false
```

The ARM64 iOS simulator target is AOT-compiled by the .NET iOS workload. Use this stricter build when validating that no interpreter or trim-dependent behavior has slipped into the port. It is the exact flag set CI applies to its `Debug Mock` pull-request build in [.github/workflows/ios.yml](.github/workflows/ios.yml); `Release` configurations enforce the same strict linker settings automatically through the project file:

```sh
"$ANIMASEEK_DOTNET" build Seeker.iOS/Seeker.iOS.csproj \
  --configuration "Debug Mock" \
  --framework net10.0-ios \
  --runtime iossimulator-arm64 \
  --no-restore \
  -p:UseInterpreter=false \
  -p:MtouchLink=Full \
  -p:TrimmerSingleWarn=false \
  -p:ILLinkTreatWarningsAsErrors=true \
  -p:ArchiveOnBuild=false
```

The resulting bundle is `Seeker.iOS/bin/Debug Mock/net10.0-ios/iossimulator-arm64/AnimaSeek.app`.

### Install and launch in Simulator

Choose any available iPhone running iOS 26. The following example uses the simulator installed on the current development machine:

```sh
xcrun simctl list devices available
xcrun simctl bootstatus "iPhone 17 Pro" -b
open -a Simulator

xcrun simctl install booted \
  "Seeker.iOS/bin/Debug Mock/net10.0-ios/iossimulator-arm64/AnimaSeek.app"
xcrun simctl launch booted com.animaseek.app
```

To stream application logs in another terminal:

```sh
xcrun simctl spawn booted log stream \
  --level info \
  --predicate 'process == "AnimaSeek"'
```

The root controller is the production four-tab UIKit shell. `Debug Mock` can open a destination directly for repeatable simulator inspection by passing `ANIMASEEK_UI_ROUTE` with one of `home`, `search`, `transfers`, `browse`, `settings`, `account`, `privileges`, `messages`, `rooms`, `users`, `profile`, `about`, `legal`, or `diagnostics`; `ANIMASEEK_UI_USER` supplies the Browse/Profile subject. For example:

```sh
SIMCTL_CHILD_ANIMASEEK_UI_ROUTE=search \
  xcrun simctl launch --terminate-running-process booted com.animaseek.app
```

`ANIMASEEK_UI_QUERY` additionally runs a search as soon as the search route opens, so the whole
request-to-rows path can be exercised without keyboard automation. `beethoven overture` and
`chiptest other` hit the deterministic curated mock corpus; `n:<count>` and `t:<milliseconds>` terms
size and pace generated responses, and `0results`, `1results`, `slowsearch`, and `wishlist` select
the corresponding mock behaviors:

```sh
SIMCTL_CHILD_ANIMASEEK_UI_ROUTE=search \
  SIMCTL_CHILD_ANIMASEEK_UI_QUERY="beethoven overture" \
  xcrun simctl launch --terminate-running-process booted com.animaseek.app
```

A successful route sweep validates the real coordinator, screen factory, feature presentation stores, and native views against the deterministic mock composition.

### Background transfers cannot be tested in Simulator

The Simulator refuses **every** `BGTaskScheduler` submission — continued-processing and app-refresh alike — with `BGTaskSchedulerErrorDomain` error 1 (`unavailable`), under either submission strategy. Launch handlers still register, so the app reaches the submission and reports the refusal honestly: a one-time notice on screen and `Background Transfers · Declined by iOS` in Diagnostics. Downloads therefore always pause when the app leaves the foreground on a simulator, and no system progress UI appears. This is a platform limitation, not app behavior; verify continued transfers on a physical iOS 26 device.

Confirm what the scheduler actually did with:

```sh
xcrun simctl spawn booted log show --last 10m --style compact \
  --predicate 'subsystem == "com.apple.BackgroundTasks"' --info --debug
```

A `submitTaskRequest: <BGContinuedProcessingTaskRequest: com.animaseek.app.transfers.batch0 …>` line proves the app asked; on device, the absence of that line is the app's fault and its presence points at the system's answer.

### Troubleshooting stale AOT artifacts

After a NuGet package version change (for example the MessagePack 2.5 → 3.1.8 upgrade), stale incremental build artifacts under `Seeker.iOS/obj` and `Seeker.iOS/bin` can make the simulator app abort at launch with a Mono error such as `Failed to load AOT module 'MessagePack' ... doesn't match assembly`. Delete the stale output and rebuild:

```sh
rm -rf Seeker.iOS/bin Seeker.iOS/obj
```

## Tests

Run the portable regression suite with the same filter used by CI:

```sh
"$ANIMASEEK_DOTNET" restore UnitTestCommon/UnitTestCommon.csproj
"$ANIMASEEK_DOTNET" test UnitTestCommon/UnitTestCommon.csproj \
  --configuration Release \
  --filter 'TestCategory!=RealData'
```

### On-simulator filesystem harness

`Seeker.iOS.FileSystemTests/` is the iOS replacement for the Android file-stream instrumentation tests. It exercises append, resume, finalization, collision-containment, and cleanup semantics against a real simulator filesystem. It is a local-only harness — CI does not run it. To run it against a booted iOS 26 simulator:

```sh
"$ANIMASEEK_DOTNET" restore Seeker.iOS.FileSystemTests/Seeker.iOS.FileSystemTests.csproj \
  --runtime iossimulator-arm64

"$ANIMASEEK_DOTNET" build Seeker.iOS.FileSystemTests/Seeker.iOS.FileSystemTests.csproj \
  --configuration Debug \
  --framework net10.0-ios \
  --runtime iossimulator-arm64 \
  --no-restore \
  -p:ArchiveOnBuild=false

xcrun simctl install booted \
  "Seeker.iOS.FileSystemTests/bin/Debug/net10.0-ios/iossimulator-arm64/AnimaSeek.FileSystemTests.app"
xcrun simctl launch --terminate-running-process booted com.animaseek.filesystemtests
```

The harness writes its verdict to `Library/Application Support/filesystem-semantics-result.txt` inside its data container within a few seconds of launch. A run passes when the first line of that file is the `IOS_FS_SEMANTICS_PASS` marker:

```sh
container="$(xcrun simctl get_app_container booted com.animaseek.filesystemtests data)"
cat "${container}/Library/Application Support/filesystem-semantics-result.txt"
```

## Signing and release

Simulator builds do not require an Apple signing identity. Device and distribution signing are intentionally not committed to the project: developers must add their Apple ID/team in Xcode or Rider and supply a certificate and provisioning profile for `com.animaseek.app`.

Once those assets exist, the local release entry point is:

```sh
"$ANIMASEEK_DOTNET" publish Seeker.iOS/Seeker.iOS.csproj \
  --configuration Release \
  --framework net10.0-ios \
  --runtime ios-arm64 \
  -p:ArchiveOnBuild=true \
  -p:BuildIpa=true \
  -p:CodesignKey="<certificate common name>" \
  -p:CodesignProvision="<profile name or UUID>"
```

Releases are cut automatically by [semantic-release](https://semantic-release.gitbook.io/) on pushes to `master` — never by hand-created tags. The `version` job in [.github/workflows/ios.yml](.github/workflows/ios.yml) dry-runs semantic-release against the conventional-commit history to decide whether a release is due and what `vMAJOR.MINOR.PATCH` it gets (config in [.releaserc.json](.releaserc.json)); the `release` job then builds the artifacts and a second semantic-release run pushes the tag and publishes the GitHub release with generated notes. Release builds always use the non-mock `Release` configuration.

Every release always carries an unsigned IPA (`animaseek_vX.Y.Z_unsigned.ipa`), which sideloading tools such as AltStore re-sign on install; the AltStore source metadata points at it. A signed IPA (`animaseek_vX.Y.Z_signed.ipa`) is added only when all step-scoped `APPLE_*` signing secrets are configured — missing secrets skip signing without blocking the release. Each release also ships a CycloneDX SBOM plus build-provenance and SBOM attestations, uploaded to the [repository attestation store](https://github.com/alexandreteles/AnimaSeek/attestations) and attached as release assets; verify a download with `gh attestation verify animaseek_vX.Y.Z_unsigned.ipa --repo alexandreteles/AnimaSeek`. Apple Developer registration, App ID and profile setup, notarization, and AltStore PAL publication still depend on external credentials and have not been exercised. Never commit signing certificates, passwords, profile secrets, or Apple credentials.

## Temporary Android validation harness

`Seeker/Seeker.csproj` and `InstrumentationTests/InstrumentationTests.csproj` remain at `net10.0-android` only as behavioral and compile-time regression harnesses while portable extraction continues. Android is not a shipping target of this fork, and the removed Android release/F-Droid workflows must not be restored.

Maintaining the harness requires the .NET `android` workload, Android API 36, JDK 17, and PowerShell 7 (`pwsh`). A representative unsigned Mock build is:

```sh
"$ANIMASEEK_DOTNET" workload install android

JAVA_HOME="/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home" \
  "$ANIMASEEK_DOTNET" build Seeker/Seeker.csproj \
  --configuration "Debug Mock" \
  --framework net10.0-android \
  --runtime android-arm64 \
  -p:AndroidKeyStore=false
```

The Android head is expected to leave the solution after the extracted core and iOS replacement tests cover its remaining validation role.
