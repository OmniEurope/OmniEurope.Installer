<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Publishing to NuGet.org

Two packages are published together, with the same version (`Version` in `src/Directory.Build.props`):

| Package | Content |
|---|---|
| `OmniEurope.Installer` | The library (`lib/net10.0` and `lib/net11.0`, Windows only): MSI tables, `msi.dll` calls, cabinet writer, setup payload. |
| `OmniEurope.Installer.Tool` | The `oe-installer` .NET tool, with the setup host (`OmniEurope.Installer.Setup.exe`) bundled next to `oe-installer.dll`. |

Each package comes with its `.snupkg` symbol package. Nothing is published from a developer machine: the publication
is the `Publish NuGet` workflow (`.github/workflows/publish-nuget.yml`), run when a GitHub Release is published. It
signs in to NuGet.org with trusted publishing (a short-lived key issued through GitHub's OpenID Connect token), so
no API key is stored anywhere.

## Prerequisites (once)

1. **GitHub environment `release`** in the repository settings (Settings, Environments). Add its protection rules
   (for example required reviewers); the publish job waits for them.
2. **Secret `NUGET_USER`**: the nuget.org account name (the profile name, not an email address) that owns the
   packages. Store it as a secret of the `release` environment (or of the repository).
3. **Trusted publishing policy** on <https://www.nuget.org/account/trustedpublishing>, with:
   - repository owner and name: `OmniEurope` and `OmniEurope.Installer`;
   - workflow file: `publish-nuget.yml`;
   - environment: `release`.

## Steps

1. Set the new `Version` in `src/Directory.Build.props` and move the changelog entries under it in `CHANGELOG.md`.
2. Push `main` (merge `develop` into it) and wait for the `CI` workflow to be green on that commit: it builds, runs
   the tests, packs both packages, checks their content (`.github/scripts/test-packages.ps1`) and uploads them as the
   `nuget-package` artifact.
3. Create a GitHub Release whose tag is the version without prefix (for example `1.0.0`), targeting `main`, and
   publish it.
4. Follow the `Publish NuGet` run. It finds the successful CI run of the release commit, downloads the exact
   packages that run built, checks again that their version equals the tag, then pushes each package with
   `--skip-duplicate` (pushing a version that already exists is ignored, not an error).
5. Check <https://www.nuget.org/packages/OmniEurope.Installer> and
   <https://www.nuget.org/packages/OmniEurope.Installer.Tool>; a new package can take several minutes to be listed
   and validated.

A version published to nuget.org can be unlisted but never replaced: fix forward with a new version.

## Checking the packages locally

```powershell
dotnet build OmniEurope.Installer.slnx -c Release
dotnet pack src/OmniEurope.Installer/OmniEurope.Installer.csproj -c Release --no-build -o artifacts/packages
dotnet pack src/OmniEurope.Installer.Tool/OmniEurope.Installer.Tool.csproj -c Release --no-build -o artifacts/packages
./.github/scripts/test-packages.ps1 -PackageDirectory artifacts/packages -ExpectedVersion 1.0.0
```
