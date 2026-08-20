 <div align="center">

<p><img src="Seeker.iOS/Assets.xcassets/AppIcon.appiconset/AppIcon-Any-1024.png" width="170" alt="AnimaSeek phoenix logo"></p>
<h2><b>AnimaSeek</b></h2>
<h4>A Soulseek client for iPhone.</h4>

</div>

AnimaSeek is a [Soulseek](https://en.wikipedia.org/wiki/Soulseek) client for iPhone written in C#, forked from [Seeker](https://github.com/jackBonadies/SeekerAndroid) for Android, supporting downloading, searching (including wishlist and filters), sharing, messages, chatrooms, port forwarding, user info, privileges, and more.

This work uses the [Soulseek.NET](https://github.com/jpdillingham/Soulseek.NET) library for communicating with the Soulseek server and network peers. It also references the unofficial [Soulseek Protocol Documentation](https://nicotine-plus.github.io/nicotine-plus/doc/SLSKPROTOCOL.html) implemented by the developers of the [Nicotine+](https://github.com/nicotine-plus/nicotine-plus) client.

- The Soulseek server, which makes this app possible, relies on donations. Donate: [here](https://www.slsknet.org/donate.php)

This app will always be completely free and open source software, no ads, no 'premium' versions or paid features, etc.

Builds are published on the [releases page](https://github.com/alexandreteles/AnimaSeek/releases).

---

## Screenshots

<div align="center">

[<img src="docs/screenshots/01_home.png" width=160>](docs/screenshots/01_home.png)
[<img src="docs/screenshots/02_search.png" width=160>](docs/screenshots/02_search.png)
[<img src="docs/screenshots/03_download_dialog.png" width=160>](docs/screenshots/03_download_dialog.png)
[<img src="docs/screenshots/04_transfers.png" width=160>](docs/screenshots/04_transfers.png)
[<img src="docs/screenshots/05_browse.png" width=160>](docs/screenshots/05_browse.png)
[<img src="docs/screenshots/06_about.png" width=160>](docs/screenshots/06_about.png)

</div>

---

## How to Install

I encourage you to build and deploy AnimaSeek yourself at least once. That gives you a chance to inspect, test, and verify the application code before relying on a published build. [DEVELOPMENT.md](DEVELOPMENT.md) covers the required toolchain, building the app, and installing it in the iOS Simulator or on a signed device.

Alternatively, you can sideload a release with [AltStore](https://altstore.io/) or [SideStore](https://sidestore.io/). Add the [AnimaSeek source](https://github.com/alexandreteles/AnimaSeek/releases/latest/download/altstore-source.json) to either app:

```text
https://github.com/alexandreteles/AnimaSeek/releases/latest/download/altstore-source.json
```

That URL follows the latest AnimaSeek release. If you want to prevent the app from updating past a particular version, use the `altstore-source.json` attached to that version on the [releases page](https://github.com/alexandreteles/AnimaSeek/releases) instead.

AnimaSeek is not currently available through AltStore PAL because publishing there would require Apple notarization and a paid Apple Developer Program membership, which costs 99 USD per year. If you would prefer a notarized build that is readily available through AltStore PAL, donations toward that cost are welcome. Whichever installation method you choose, you can use the [release provenance](#release-provenance-and-supply-chain-security) to verify that a build came from this project.

---

## Release Provenance and Supply-Chain Security

Every AnimaSeek release is immutable. For each IPA, the [iOS build workflow](.github/workflows/ios.yml) produces a CycloneDX Software Bill of Materials (SBOM), a cryptographically signed SLSA build-provenance attestation, and a signed SBOM attestation. The SBOM provides an inventory of the components included in the build, while the SLSA provenance binds the exact bytes of the IPA to this repository, the source commit, and the GitHub Actions workflow that produced it. The artifacts are attached to each release and recorded on the repository's [attestations page](https://github.com/alexandreteles/AnimaSeek/attestations).

I produce both the SBOM and SLSA artifacts to make the build and its dependencies easier to inspect, verify, and investigate for supply-chain issues. They do not prove that every dependency is safe, but they provide evidence that would otherwise be missing and make unexpected changes easier to detect. Because the release assets cannot be replaced after publication, that evidence remains tied to the original build.

This is especially useful if AnimaSeek builds appear on mirrors. Download the IPA and ask the [GitHub CLI](https://cli.github.com/) to verify its attestation:

```sh
gh attestation verify animaseek_vX.Y.Z_unsigned.ipa \
  --repo alexandreteles/AnimaSeek
```

A successful verification proves that the file's contents match an artifact built for this repository by GitHub Actions. A modified or independently rebuilt IPA will not match the signed attestation. Provenance establishes origin and integrity; it does not replace reviewing the source code or deciding whether you trust it. The [v1.1.0 release](https://github.com/alexandreteles/AnimaSeek/releases/tag/v1.1.0) is an example with its SBOM, SLSA provenance, attestations, and build information available for inspection.

In the future, I expect to add more transparent supply-chain checks directly to the public build workflow. Any checks I add will be visible in the workflow so their scope and results can be independently inspected.

---

## AI Usage Disclaimer

AnimaSeek is developed with the help of AI coding agents, which I use both for writing code and for generating assets. My workflow isn't fully vibed, however: it will always involve human planning as well as human reviews of any slop generated by the clankers.

I'm also not the biggest fan of what AI companies are doing (from [destroying rare books](https://futurism.com/artificial-intelligence/ai-companies-destroying-rare-books) to [the damage their data centers do to the environment](https://stpp.fordschool.umich.edu/sites/stpp/files/2025-07/stpp-data-centers-2025.pdf) and to [the communities living around them](https://theconversation.com/5-ways-data-centers-endanger-their-local-communities-and-the-country-as-a-whole-282348))
so I try to make my use of it as ethical as possible, if such an ethical use of AI can even exist. I understand if you would rather avoid this tool because of that, and I promise to keep doing things the right way. Sadly, AI is here to stay; if I am going to use it, I would rather do so in a way that benefits the communities I am part of.

### Tools Used

I used GPT-5.6-Sol through Codex for most of the porting effort, including planning, design, and code writing. Codex does not mark itself as a co-author on commits containing files it touched, so I am making its involvement explicit here.

After the initial port, I used Claude Opus 5 through Claude Code to fix UI issues and implement smaller features once Codex had done the heavy lifting. Claude Code marks the commits it co-authored, but I am still documenting its role here for transparency.

### Using AI to Contribute

I am completely fine with contributors using whatever tools fit their workflow. I only ask that you disclose when AI tools were used in a pull request, issue, or any other kind of contribution.

Contributions will still receive human review, and I am unlikely to adopt automated AI workflows for development or project operations. If you point an agent at this project, please also take the time to read the code, understand what it does, and write some comments yourself.

---

## License

AnimaSeek and the inherited Seeker code are distributed under the [GNU General Public License, version 3](LICENSE).
