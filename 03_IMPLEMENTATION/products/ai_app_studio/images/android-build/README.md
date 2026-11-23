# aas-android-build (skeleton, not built in slice S3)

Not built here: needs ~3-5 GB of SDK + Gradle cache and a CI step that generates `verification-metadata.xml`.
Exact steps for the follow-up slice:

1. Pin: `docker pull eclipse-temurin:21-jdk-jammy`, record the RepoDigest; download `commandlinetools-linux-<N>_latest.zip`
   from dl.google.com and record its sha256; Gradle 8.14.3 `-bin.zip` sha256 from services.gradle.org (published `.sha256`).
   Put all three in `../DIGESTS.json` and pass them as `--build-arg`.
2. Generate the template: in a scratch dir with the kit, `pnpm exec cap add android` (output = `android/`), then set
   `distributionUrl` in `gradle/wrapper/gradle-wrapper.properties` to the pre-placed zip plus `distributionSha256Sum`.
3. Resolve once WITH network: `./gradlew --write-verification-metadata sha256 assembleDebug` (writes
   `gradle/verification-metadata.xml`), then copy `$GRADLE_USER_HOME/caches/modules-2` to `/opt/gradle-ro`.
4. Fail the image build when `compileSdkVersion` in `node_modules/@capacitor/android` `variables.gradle` != `ANDROID_PLATFORM`
   (currently 36, AGP 8.13.0, Gradle template 8.14.3).
5. Write `/usr/local/bin/aas-build-android` (copy template to /tmp, copy /dist into `app/src/main/assets/public`, run
   `gradlew --offline --no-daemon --dependency-verification strict -Dorg.gradle.jvmargs=-Xmx2g assembleDebug` with the
   mounted studio keystore (aas-debug.keystore, never the owner's), write the APK to /out).
6. Proofs PB7a (offline strict succeeds; deleting one cache entry fails with "No cached version") and PB7b (APK signature).
