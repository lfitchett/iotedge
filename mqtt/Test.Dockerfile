FROM mqtt AS builder
FROM mqtt-test

ENV RUST_LOG=info

COPY --from=builder \
    /usr/local/bin/ \
    /usr/local/bin/

CMD exec /usr/local/bin/mqttd & \
    npm start --prefix /home/titan/iottestware.webserver