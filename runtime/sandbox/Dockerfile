# The existing repository Node base pin is reused; changing this image is a reviewed runtime change.
FROM --platform=linux/amd64 node:22-alpine@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402
USER 0:0
COPY --chown=0:0 --chmod=0555 runner.mjs verify-image.mjs /opt/hirezero/
COPY --chown=0:0 --chmod=0444 pin.json /opt/hirezero/pin.json
RUN --network=none /usr/local/bin/node /opt/hirezero/verify-image.mjs \
    && chmod 0444 /opt/hirezero/image-inventory.json \
    && chmod 0555 /opt/hirezero
ENV UV_THREADPOOL_SIZE=1
LABEL io.hirezero.sandbox.worker="2"
WORKDIR /work
ENTRYPOINT ["/usr/local/bin/node", "--v8-pool-size=1", "/opt/hirezero/runner.mjs"]
