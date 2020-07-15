// Copyright (c) Microsoft. All rights reserved.

use failure::ResultExt;
use futures::Future;
use hyper::header::{CONTENT_LENGTH, CONTENT_TYPE};
use hyper::{Body, Request, Response, StatusCode};
use log::debug;
use serde::Serialize;

use edgelet_core::{Module, ModuleRuntime, RuntimeOperation};
use edgelet_http::route::{Handler, Parameters};
use edgelet_http::Error as HttpError;

use crate::error::{Error, ErrorKind};
use crate::IntoResponse;

pub struct GetSystemMetadata<M> {
    runtime: M,
}

impl<M> GetSystemMetadata<M> {
    pub fn new(runtime: M) -> Self {
        GetSystemMetadata { runtime }
    }
}

impl<M> Handler<Parameters> for GetSystemMetadata<M>
where
    M: 'static + ModuleRuntime + Send,
    <M::Module as Module>::Config: Serialize,
{
    fn handle(
        &self,
        _req: Request<Body>,
        _params: Parameters,
    ) -> Box<dyn Future<Item = Response<Body>, Error = HttpError> + Send> {
        debug!("Get System Resources");

        let response = self
            .runtime
            .system_metadata()
            .then(|system_metadata| -> Result<_, Error> {
                // let body = serde_json::to_string(&system_metadata).context(
                //     ErrorKind::RuntimeOperation(RuntimeOperation::SystemResources),
                // )?;

                let response = Response::builder()
                    .status(StatusCode::OK)
                    .header(CONTENT_TYPE, "text/plain; charset=utf-8")
                    .header(CONTENT_LENGTH, body.len().to_string().as_str())
                    .body(system_metadata.into())
                    .context(ErrorKind::RuntimeOperation(
                        RuntimeOperation::SystemResources,
                    ))?;
                Ok(response)
            })
            .or_else(|e| Ok(e.into_response()));

        Box::new(response)
    }
}
