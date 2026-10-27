# Qualification candidate, not permission to browse. See pin.json and ADR 0100.
FROM --platform=linux/amd64 mcr.microsoft.com/playwright@sha256:d65108d1a8095d8b0c4a5e32bf1807a40a5ce90ff0c78473e1116b1c243235e8

USER 0:0
# The upstream image includes Node, Chromium and their libraries. No floating package install or browser download.
# Its test-browser directories are writable upstream; take ownership before serving this candidate to qualification.
RUN --network=none test "$(uname -m)" = x86_64 \
    && test -x /usr/bin/node \
    && test -x /ms-playwright/chromium-1248/chrome-linux64/chrome \
    && ln -s /usr/bin/node /usr/local/bin/node \
    && ln -s /ms-playwright/chromium-1248/chrome-linux64/chrome /usr/bin/chromium \
    && chown -R 0:0 /ms-playwright \
    && chmod -R go-w /ms-playwright

COPY --chown=0:0 --chmod=0555 driver.mjs protocol.mjs runner.mjs verify-image.mjs /opt/hirezero/browser/
COPY --chown=0:0 --chmod=0444 pin.json /opt/hirezero/browser/pin.json
RUN --network=none /usr/local/bin/node /opt/hirezero/browser/verify-image.mjs \
    && chmod 0444 /opt/hirezero/browser/image-inventory.json \
    && chmod 0555 /opt/hirezero/browser

LABEL io.hirezero.browser.worker="1" \
      io.hirezero.browser.candidate="1" \
      io.hirezero.browser.chromium="156.0.8078.4" \
      org.opencontainers.image.base.name="mcr.microsoft.com/playwright:v1.64.1-noble" \
      org.opencontainers.image.base.digest="sha256:d65108d1a8095d8b0c4a5e32bf1807a40a5ce90ff0c78473e1116b1c243235e8"
WORKDIR /work
ENTRYPOINT ["/usr/local/bin/node"]
CMD ["/opt/hirezero/browser/runner.mjs"]
