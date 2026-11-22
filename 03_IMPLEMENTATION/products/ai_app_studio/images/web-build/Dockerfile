# aas-web-build: offline web build image (DESIGN 3.4). Build context = kit/ :
#   docker build -f images/web-build/Dockerfile --build-context scripts=images/web-build -t aas-web-build:dev kit
# The image is built WITH network (CI / first run); `docker run` of it is always --network none.
FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5

# pnpm pinned to an exact version (matches the pnpm that generated kit/pnpm-lock.yaml).
RUN npm install -g pnpm@10.28.2 --ignore-scripts --no-audit --no-fund \
 && pnpm --version | grep -x 10.28.2

# Kit dependencies pre-fetched into a baked store (no network at run time). Scripts are ignored; the store is root-owned
# and world-readable so the unprivileged run user can read but not alter it.
WORKDIR /opt/aas/kit
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml .npmrc ./
RUN pnpm fetch --frozen-lockfile --ignore-scripts --store-dir /opt/aas/store \
 && sha256sum pnpm-lock.yaml | cut -d' ' -f1 > /opt/aas/kit.lock.sha256 \
 && chmod -R a+rX /opt/aas

COPY --from=scripts aas-build-web /usr/local/bin/aas-build-web
RUN sed -i 's/\r$//' /usr/local/bin/aas-build-web && chmod 0755 /usr/local/bin/aas-build-web

ENV NODE_ENV=production CI=1 npm_config_update_notifier=false
USER 1000:1000
WORKDIR /tmp
ENTRYPOINT ["/usr/local/bin/aas-build-web"]
