use std::{
    collections::HashMap,
    pin::Pin,
    task::{Context, Poll},
};

use futures_util::{
    future::{self, BoxFuture},
    stream::{FusedStream, FuturesUnordered, Stream},
    FutureExt, StreamExt,
};
use tokio::task::JoinError;
use tracing::{debug, error, info_span, warn};
use tracing_futures::Instrument;

use crate::{
    bridge::{Bridge, BridgeError, BridgeHandle},
    config_update::{BridgeUpdate, ConfigUpdater},
    persist::StreamWakeableState,
    settings::ConnectionSettings,
};

/// A type for a future that will be resolved to when `Bridge` exits.
type BridgeFuture = BoxFuture<'static, (String, Result<Result<(), BridgeError>, JoinError>)>;

/// Encapsulates logic from `BridgeController` on how it manages with
/// `BridgeFuture`s.
///
/// It represents a `FusedStream` of `Bridge` futures which resolves to a pair
/// bridge name and exit result. It stores shutdown handles for each `Bridge`
/// internally to request a stop when needed.
#[derive(Default)]
pub(crate) struct Bridges {
    bridge_handles: HashMap<String, BridgeHandle>,
    config_updaters: HashMap<String, ConfigUpdater>,
    bridges: FuturesUnordered<BridgeFuture>,
}

impl Bridges {
    pub(crate) async fn start_bridge<S>(&mut self, bridge: Bridge<S>, settings: &ConnectionSettings)
    where
        S: StreamWakeableState + Send + 'static,
    {
        let name = settings.name().to_owned();

        // save bridge handle
        let bridge_handle = bridge.handle();
        self.bridge_handles.insert(name.clone(), bridge_handle);

        // save config updater
        let config_updater = ConfigUpdater::new(bridge.handle());
        self.config_updaters.insert(name.clone(), config_updater);

        // start bridge
        let upstream_bridge = bridge.run().instrument(info_span!("bridge", name = %name));
        let task = tokio::spawn(upstream_bridge).map(|res| (name, res));
        self.bridges.push(Box::pin(task));
    }

    pub(crate) async fn send_update(&mut self, update: BridgeUpdate) {
        let endpoint = update.endpoint().to_owned();
        if let Some(config) = self.config_updaters.get_mut(&endpoint) {
            if let Err(err) = config.send_update(update).await {
                error!(
                    "error sending bridge update for {}, caused by: {:?}",
                    &endpoint, err
                );
            }
        } else {
            debug!("config for bridge {} not found", update.endpoint());
        }
    }

    pub(crate) async fn shutdown_bridge(&mut self, name: &str) {
        debug!("sending shutdown request to {} bridge...", name);

        if let Some(bridge_handle) = self.bridge_handles.remove(name) {
            bridge_handle.shutdown().await;
        } else {
            warn!("bridge {} not found", name);
        }
    }

    pub(crate) async fn shutdown_all(&mut self) {
        debug!("sending shutdown request to all bridges...");

        // sending shutdown signal to each bridge
        let shutdowns = self
            .bridge_handles
            .drain()
            .map(|(_, handle)| handle.shutdown());
        future::join_all(shutdowns).await;

        debug!("waiting for all bridges to exit...");

        // wait until all bridges finish
        while let Some((name, bridge)) = self.bridges.next().await {
            match bridge {
                Ok(Ok(_)) => debug!("bridge {} exited", name),
                Ok(Err(e)) => warn!(error = %e, "bridge {} exited with error", name),
                Err(e) => warn!(error = %e, "bridge {} panicked ", name),
            }
        }

        debug!("all bridges exited");
    }
}

impl Stream for Bridges {
    type Item = (
        String,
        Result<Result<(), BridgeError>, tokio::task::JoinError>,
    );

    fn poll_next(mut self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<Option<Self::Item>> {
        let poll = self.bridges.poll_next_unpin(cx);

        // remove redundant handlers when bridge exits
        if let Poll::Ready(Some((name, _))) = &poll {
            self.bridge_handles.remove(name);
            self.config_updaters.remove(name);
        }

        poll
    }
}

impl FusedStream for Bridges {
    fn is_terminated(&self) -> bool {
        self.bridges.is_terminated()
    }
}
