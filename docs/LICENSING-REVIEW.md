# Licensing review — 20 September 2026

Scope: the original source and the published Windows 1.0.1 installer; remediation in 1.0.2. This is an engineering review of published terms and package contents, not a legal opinion or certification. John Lightfoot remains the credited creator; original code and icon remain MIT.

## NDI findings and changes

The local SDK agreement was read from `NDI SDK License Agreement.pdf` in the installed NDI 6 SDK. Its substantive requirements were compared with the current [published agreement](https://downloads.ndi.tv/SDK/NDI_SDK/NDI%20SDK%20License%20Agreement.pdf), [licensing documentation](https://docs.ndi.video/all/developing-with-ndi/sdk/licensing) and [redistribution documentation](https://docs.ndi.video/all/developing-with-ndi/sdk/software-distribution).

The SDK documentation expressly permits redistributing the official executable in its Redist directory, including running it from another installer. Silent runtime installation requires the licence terms to be covered elsewhere. Our runtime installer runs interactively, and our own setup now presents third-party terms whether or not NDI is already installed. No SDK tools, headers or loose runtime DLLs are added to the source repository.

The bundled runtime reports version 6.3.2.0, matching the latest version listed in NDI's release notes on the review date. SHA-256 of the locally supplied official SDK redistributable: `9456C84E511F2ECC17DE0B733266913F4125BD86D411D1E33BD9356B390381EF`. Windows reported it as unsigned; this review does not assert an Authenticode signature. Its provenance is the installed official SDK Redist directory. [Release notes](https://docs.ndi.video/all/developing-with-ndi/sdk/release-notes).

Gaps found in 1.0.1:
- No in-app link next to NDI controls, despite the documented linking requirement.
- Generic attribution existed, but the package did not include scoped end-user terms addressing SDK agreement section 3(d), particularly when a runtime was already installed.
- MIT statements needed a clearer distinction between original code and proprietary NDI components.

Changes in 1.0.2:
- Clickable NDI website attribution near the dashboard title and slot-name field.
- A Licences and credits menu with notices and links to the actual installed terms.
- Setup presents THIRD-PARTY-TERMS.txt and installs it for later reference. The terms cover NDI-specific modification/reverse-engineering restrictions, notices, warranties, liability, exports and downstream developer requirements. They expressly preserve MIT and open-source rights for independent components.
- README, installer notices and release notes identify NDI as a separate proprietary component and avoid suggesting endorsement.

Boundaries: NDI's agreement excludes some fixed-purpose hardware and restricted hosted/cloud environments from the ordinary SDK grant. This release targets a general-purpose Windows desktop. Future redistribution must recheck current SDK versions and terms. This review does not verify every user's intended deployment or codec patent obligations.

## FFmpeg findings and changes

The exact local executable identifies itself as `9.0.2-essentials_build-www.gyan.dev`. SHA-256: `3256173F3F8BFFD7DF12227C68ADF68025EDB1832273A9530688A7BB1ED8EDEC`. Its configuration includes `--enable-gpl --enable-version3 --enable-static`, with x264, x265 and numerous other libraries; no `--enable-nonfree` flag was found. The provider README identifies GPLv3 and FFmpeg core commit `946fcce07b`.

[FFmpeg's licensing page](https://ffmpeg.org/legal.html) distinguishes its usual LGPL licence from optional GPL components. The LGPL linking checklist must not be misapplied to this GPLv3 executable. [Gyan's build page](https://www.gyan.dev/ffmpeg/builds/) confirms its static builds are GPLv3. The GPLv3 supplied in the actual package was read, especially sections 1, 5 and 6. Section 6(d) requires equivalent access to corresponding source and permits a different source server with clear directions, but the distributor retains responsibility for availability. Corresponding source includes the necessary build scripts and relevant dependencies; the core FFmpeg source alone is not sufficient evidence for this large static build.

1.0.1 included a licence and general links but did not establish a complete matching source package covering linked libraries and build scripts. The available provider README and release page did not close that gap. Using an unmodified binary and giving it away does not remove its source-distribution obligations. The GPL's aggregation provision may allow independent components to retain their licences; this app uses FFmpeg as a separate process with ordinary media streams. That is our engineering basis for retaining MIT on the original code, not a blanket legal conclusion about every possible integration.

Remediation selected by the owner:
- Do not embed or rehost FFmpeg in 1.0.2 or future release packages until corresponding-source obligations can be verified.
- Instead, setup downloads the unmodified, pinned provider ZIP directly to the user's computer over HTTPS and checks SHA-256 before extracting. This changes the project's distribution boundary; it is not a claim to certify the provider's licensing compliance.
- Install the provider's licence and README alongside its binary; also ship a GPLv3 text and clear component notices.
- Gracefully complete installation if downloading or checksum verification fails, with manual setup instructions and a download-page link. Do not copy unverified archive contents.
- Keep NDI restrictions expressly separate from FFmpeg/GPL and original MIT rights.
- Use a clean release payload and exclude tools from the embedded installer files, preventing accidental inclusion of an older local FFmpeg executable.

The old 1.0.1 binary release was moved to draft to stop further public downloads, retaining it for review. That does not retroactively discharge any obligations from copies already distributed. Complete corresponding sources for that old binary still need to be obtained from the build provider or otherwise reconstructed; no promise that those historical obligations are resolved is made here. No source offer that cannot currently be fulfilled has been issued, and no contact message has been sent on the owner's behalf.

## Release validation

Build and installer checks verify the terms/notices, downloaded FFmpeg checksum and launch, and install/uninstall behaviour. An isolated installer build with an unreachable download URL exercises the fallback path without changing system networking. The source and release notes record the online-install requirement and manual fallback. Media processing remains unchanged except for the clearer missing-FFmpeg error.

Before a future release: recheck NDI's current terms/version, review any FFmpeg source or download change, retain component notices, rerun online/failure-path checks, and avoid republishing the 1.0.1 bundle without addressing its source gap. Seek qualified advice for unresolved historical distribution, patent questions or deployments outside the ordinary desktop scope.

### Verified for 1.0.2

- Release build completed without warnings or errors.
- Actual online install fetched FFmpeg from the pinned provider URL, verified the archive, and produced the expected executable SHA-256. FFmpeg ran successfully.
- An isolated build using an unreachable loopback download endpoint completed installation with the manual-setup message recorded in its log. No FFmpeg executable was installed; the app still launched and displayed its missing-FFmpeg status.
- Both installs included original MIT licence, GPLv3 text, component notices and installer terms. Both created the Start-menu shortcut and uninstalled cleanly.
- Saved slot settings and Windows NDI settings were unchanged in both tests.
- Dashboard and slot-editor rendering were inspected for the attribution links and missing-FFmpeg indication.

Raw logs are retained locally under test-results/installer-smoke.txt and test-results/installer-offline-smoke.txt. The offline fixture is not a release asset. Existing local NDI was detected during these tests; the missing-NDI interactive flow was not reinstalled or newly accepted during this review.
