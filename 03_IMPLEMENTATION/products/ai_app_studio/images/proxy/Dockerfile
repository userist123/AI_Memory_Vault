# aas-proxy: allow-list CONNECT proxy (only registry.npmjs.org:443) for the install stage.
#   docker build -f images/proxy/Dockerfile -t aas-proxy:dev images/proxy
# Run attached to the --internal network `aas-egress` AND one ordinary network that has internet; install containers
# live on aas-egress only (no gateway), so this container is their only way out.
FROM node:24-alpine@sha256:116b800c3754b440ee0aa3ed2f20f5bf3d99a554bfd9adeea09ea8017f6e85f5
COPY proxy.js /opt/aas-proxy/proxy.js
RUN chmod 0644 /opt/aas-proxy/proxy.js
USER 1000:1000
EXPOSE 3128
CMD ["node", "/opt/aas-proxy/proxy.js"]
