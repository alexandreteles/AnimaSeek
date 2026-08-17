# Packaged runtime notices

These files are redistributed verbatim from the exact runtime packs and NuGet package resolved by the iOS build. The explicit versions keep release artifacts reproducible and make dependency upgrades fail review visibly.

| Packaged file | Source | SHA-256 |
|---|---|---|
| `DOTNET-RUNTIME-10.0.10-LICENSE` | `Microsoft.NETCore.App.Runtime.Mono.ios-arm64` 10.0.10, `LICENSE.TXT` | `cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310` |
| `DOTNET-RUNTIME-10.0.10-NOTICES` | `Microsoft.NETCore.App.Runtime.Mono.ios-arm64` 10.0.10, `THIRD-PARTY-NOTICES.TXT` | `66f1d4e44973185519bb4aa8a9718eb22fc7af2cc532e3ae9cfc4c127ee7fc54` |
| `MICROSOFT-IOS-26.5.10315-LICENSE` | `Microsoft.iOS.Runtime.ios.net10.0_26.5` 26.5.10315, `LICENSE` | `2ad5be3c5907b028a63e6ac9983dc88349c9a7dcabc6f531389a555b5c8628e6` |
| `MICROSOFT-NET-STRINGTOOLS-17.11.4-LICENSE` | `dotnet/msbuild` commit `37eb419ad2c986ac5530292e6ee08e962390249e`, `LICENSE` | `9aebac42398b50e652a84a7b92d70e85cd1a9c8746558a743dc6cec11b8d7d3e` |
| `MICROSOFT-NET-STRINGTOOLS-17.11.4-NOTICES` | `Microsoft.NET.StringTools` 17.11.4, `notices/THIRDPARTYNOTICES.txt` | `01bb7cfadd7ebc022a5ff5dad8c9df0574ee0330f6f58ee497a27c0792dcb4f4` |

The runtime license and notice files are byte-identical across the installed `ios-arm64`, `iossimulator-arm64`, and `iossimulator-x64` 10.0.10 packs. The Microsoft.iOS license is likewise identical across the installed device and simulator runtime packs for 26.5.10315.
