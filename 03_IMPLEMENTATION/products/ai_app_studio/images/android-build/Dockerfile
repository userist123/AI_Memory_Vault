# aas-android-build: offline Capacitor 8 debug-APK build image (DESIGN 3.5; probes PB7a/PB7b in tests/build).
# Build context = kit/ (same as aas-web-build), studio files from this directory:
#   docker build -f images/android-build/Dockerfile --build-context aas=images/android-build -t aas-android-build:dev kit
# The image is built WITH network; every download is pinned by sha256 (ADD --checksum) and the base images by digest.
# `docker run` of it is always --network none (core/aas_build/runner.py build_android_argv).
# Facts checked against the kit's @capacitor/android 8.6.2 (the kit pins 8.6.2, not 8.5.2):
#   compileSdk/targetSdk 36, minSdk 24, AGP 8.13.0, template wrapper Gradle 8.14.3.
# Pins (also in ../DIGESTS.json): Temurin 21.0.13.1+2 (Adoptium API checksum); Android zips: sha1 from Google's
# repository2-3.xml matched, sha256 computed from the same bytes; Gradle 8.14.3-bin from services.gradle.org .sha256.

# ---- 1. fetch: pinned downloads, unpacked with busybox (the archives never reach the final image) -------------------
FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5 AS fetch
ADD --checksum=sha256:bb8a93f9ae6e7a693b2b721e85dbeff26ae1a77447985a8d7afcdd9c7cf6222e \
    https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13.1%2B2/OpenJDK21U-jdk_x64_linux_hotspot_21.0.13.1_2.tar.gz /dl/jdk.tar.gz
ADD --checksum=sha256:37607369a28c5b640b3a7998868d45898ebcb777565a0e85f9acf36f29631d2e \
    https://dl.google.com/android/repository/platform-36_r02.zip /dl/platform.zip
ADD --checksum=sha256:5d9ac77fb6ff43d9da518a337b4fcf8f9097113df531d99ccefe80ef7ce8250b \
    https://dl.google.com/android/repository/build-tools_r36_linux.zip /dl/build-tools.zip
ADD --checksum=sha256:bd71102213493060956ec229d946beee57158dbd89d0e62b91bca0fa2c5f3531 \
    https://services.gradle.org/distributions/gradle-8.14.3-bin.zip /dl/gradle.zip
RUN set -eu; mkdir -p /opt/jdk /opt/android-sdk/platforms /opt/android-sdk/build-tools /x \
 && tar xzf /dl/jdk.tar.gz -C /opt/jdk --strip-components=1 \
 && unzip -q /dl/platform.zip -d /x/p && mv /x/p/android-36 /opt/android-sdk/platforms/android-36 \
 && unzip -q /dl/build-tools.zip -d /x/b && mv /x/b/android-16 /opt/android-sdk/build-tools/36.0.0 \
 && unzip -q /dl/gradle.zip -d /x/g && mv /x/g/gradle-8.14.3 /opt/gradle \
 && chmod -R a+rX,go-w /opt \
 && chmod a+x /opt/android-sdk/build-tools/36.0.0/aapt /opt/android-sdk/build-tools/36.0.0/aapt2 \
      /opt/android-sdk/build-tools/36.0.0/aidl /opt/android-sdk/build-tools/36.0.0/zipalign \
      /opt/android-sdk/build-tools/36.0.0/apksigner /opt/android-sdk/build-tools/36.0.0/d8 \
      /opt/gradle/bin/gradle \
 && rm -rf /x /dl

# ---- 2. template: `cap add android` for the kit (Capacitor CLI + @capacitor/android from the kit lockfile) ----------
FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5 AS template
ARG ANDROID_PLATFORM=36
RUN npm install -g pnpm@10.28.2 --ignore-scripts --no-audit --no-fund && pnpm --version | grep -x 10.28.2
WORKDIR /w
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml .npmrc ./
ENV CI=1 npm_config_update_notifier=false
RUN pnpm install --frozen-lockfile --ignore-scripts --store-dir /tmp/store && rm -rf /tmp/store
RUN set -eu; mkdir dist && printf '<!doctype html><title>App</title>\n' > dist/index.html \
 && printf '{"appId":"dev.aiappstudio.p0000000000000000","appName":"App","webDir":"dist","android":{"allowMixedContent":false}}\n' > capacitor.config.json \
 && pnpm exec cap add android \
 && grep -q "compileSdkVersion = ${ANDROID_PLATFORM}\$" android/variables.gradle \
 && grep -q "rootProject.ext.compileSdkVersion : ${ANDROID_PLATFORM}\$" node_modules/@capacitor/android/capacitor/build.gradle \
 && grep -q "com.android.tools.build:gradle:8.13.0'" node_modules/@capacitor/android/capacitor/build.gradle \
 && test -f android/app/src/main/java/dev/aiappstudio/p0000000000000000/MainActivity.java \
 && mkdir -p /out/node_modules/@capacitor \
 && cp -RL node_modules/@capacitor/android /out/node_modules/@capacitor/android \
 && rm -rf android/app/src/test android/app/src/androidTest android/app/build \
 && cp -R android /out/android
COPY --from=aas template/ /out/android/

# ---- 3. jdk base: digest-pinned Debian (python:3.12-slim) + Temurin 21 ------------------------------------------------
FROM python:3.12-slim@sha256:5e2bdb1b0d542d5039cffb8181c65930093949194a65c365dfb1b0fe13a4fa5b AS jdk
COPY --from=fetch /opt/jdk /opt/jdk
ENV JAVA_HOME=/opt/jdk PATH=/opt/jdk/bin:/opt/gradle/bin:/opt/android-sdk/build-tools/36.0.0:/usr/local/bin:/usr/bin:/bin \
    ANDROID_HOME=/opt/android-sdk ANDROID_SDK_ROOT=/opt/android-sdk

# ---- 4. base: SDK subset + Gradle + studio template -----------------------------------------------------------------
# Licence: platform-36 and build-tools 36.0.0 use "android-sdk-license" (repository2-3.xml). cmdline-tools 23
# (commandlinetools-linux-16111833, sha256 0877a1d0...cfa8) refuses `sdkmanager --licenses` ("no longer needed") and its
# replacement `android` downloads an unpinned CLI, so no cmdline-tools are used. The acceptance is written
# non-interactively as sdkmanager did: sha1 of the (stripped) licence text in $ANDROID_HOME/licenses. AGP never installs
# anything here (android.builder.sdkDownload=false, packages pre-placed), so the file is a record, not a gate.
FROM jdk AS base
COPY --from=fetch /opt/android-sdk /opt/android-sdk
RUN mkdir -p /opt/android-sdk/licenses && printf '\n9002c006f4b8d9a16e715a9fa4df30ddb8abf9d9' > /opt/android-sdk/licenses/android-sdk-license
COPY --from=fetch /opt/gradle /opt/gradle
COPY --from=template /out /opt/aas/android-template
ENV GRADLE_USER_HOME=/tmp/gradle-home ANDROID_USER_HOME=/tmp/android-user HOME=/tmp/home \
    GRADLE_OPTS="-Xmx256m -Dorg.gradle.native=false"

# ---- 5. resolve: one online build writes verification-metadata.xml and fills the dependency cache -----------------
FROM base AS resolve
RUN set -eu; cp -R /opt/aas/android-template /tmp/r && mkdir -p /tmp/home && cd /tmp/r/android \
 && keytool -genkeypair -noprompt -storetype PKCS12 -keystore /tmp/resolve.keystore -storepass android -keypass android \
      -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 2 -dname "CN=resolve" >/dev/null 2>&1 \
 && gradle --no-daemon --console=plain --write-verification-metadata sha256 -PaasKeystore=/tmp/resolve.keystore assembleDebug \
 && test -f app/build/outputs/apk/debug/app-debug.apk \
 && mkdir -p /opt/gradle-ro /opt/meta && cp gradle/verification-metadata.xml /opt/meta/ \
 && cp -R /tmp/gradle-home/caches/modules-2 /opt/gradle-ro/modules-2 \
 && find /opt/gradle-ro \( -name '*.lock' -o -name 'gc.properties' \) -delete \
 && test "$(ls /opt/android-sdk/build-tools)" = "36.0.0" && test "$(ls /opt/android-sdk/platforms)" = "android-36"

# ---- 6. final ------------------------------------------------------------------------------------------------------
FROM base
COPY --from=resolve /opt/gradle-ro /opt/gradle-ro
COPY --from=resolve /opt/meta/verification-metadata.xml /opt/aas/android-template/android/gradle/verification-metadata.xml
COPY --from=aas aas-build-android /usr/local/bin/aas-build-android
RUN sed -i 's/\r$//' /usr/local/bin/aas-build-android && chmod 0755 /usr/local/bin/aas-build-android \
 && find /opt/aas/android-template -type f \( -name '*.gradle' -o -name '*.properties' \) -exec sed -i 's/\r$//' {} + \
 && chmod -R a+rX,go-w /opt/aas /opt/gradle-ro \
 && /usr/sbin/useradd -M -u 1000 -d /tmp/home aas
ENV GRADLE_RO_DEP_CACHE=/opt/gradle-ro CI=1
USER 1000:1000
WORKDIR /tmp
# Run (runner.build_android_argv): --network none --read-only --tmpfs /tmp --memory 4g --cpus 4 --cap-drop ALL
#   -v <dist>:/dist:ro -v <keys>/aas-debug.keystore:/keys/aas-debug.keystore:ro -v <out>:/out:rw -e AAS_APP_ID=...
ENTRYPOINT ["/usr/local/bin/aas-build-android"]
CMD ["build"]
