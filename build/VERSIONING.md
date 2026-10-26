# Version and build identity

Use the owner's Lohe convention: a semantic release version (`major.minor.patch`) and a separate
build ID (`YYYY.DD.MM.revision`). The day comes before the month. The final revision increases for
each distinct build set, including private builds, and does not reset when the date changes.

`build-id.txt` is the shared source/build-set identity. Run `pwsh build/next-build.ps1` once before
starting a new build set; its date uses America/Phoenix. Windows, public Android and private Android
variants produced from that same source/build set share the ID and retain explicit platform/channel
names. Retries of the same build set keep its ID. Change the semantic release version only when
requested; all version fields must agree.

About shows release and build separately. Artifacts use
`Quantos-<release>-build-<build-id>-<platform>[-<configuration>].<extension>`.
Android's versionCode follows Lohe: `major * 10000 + minor * 100 + patch` (each part 0..99),
while versionName is the release version. Debug and public Release certificates are separate.

Keep GitHub release tags numeric (`v0.2.1`) while supporting pre-0.2.1 Windows clients: their updater
rejects build-suffixed tags. Put the build ID in the release title, About and asset filenames.
The asset still starts with `Quantos-` and ends with `-win-x64.zip`, so older clients find it.
Increment the release version for a newly published Windows update so those clients detect it.
Do not rename existing public releases or rewrite historic build identities.
