/*
 * IoT Edge Management API
 *
 * No description provided (generated by Swagger Codegen https://github.com/swagger-api/swagger-codegen)
 *
 * OpenAPI spec version: 2019-10-22
 *
 * Generated by: https://github.com/swagger-api/swagger-codegen.git
 */

use std::borrow::Borrow;
use std::sync::Arc;

use futures::{Future, Stream};
use typed_headers::http;

use super::{configuration, Error};

pub struct DeviceActionsApiClient<C: hyper::client::connect::Connect> {
    configuration: Arc<configuration::Configuration<C>>,
}

impl<C: hyper::client::connect::Connect> DeviceActionsApiClient<C> {
    pub fn new(configuration: Arc<configuration::Configuration<C>>) -> Self {
        DeviceActionsApiClient { configuration }
    }
}

pub trait DeviceActionsApi: Send + Sync {
    fn reprovision_device(
        &self,
        api_version: &str,
    ) -> Box<dyn Future<Item = (), Error = Error<serde_json::Value>>>;
}

impl<C> DeviceActionsApi for DeviceActionsApiClient<C>
where
    C: hyper::client::connect::Connect + 'static,
    <C as hyper::client::connect::Connect>::Transport: 'static,
    <C as hyper::client::connect::Connect>::Future: 'static,
{
    fn reprovision_device(
        &self,
        api_version: &str,
    ) -> Box<dyn Future<Item = (), Error = Error<serde_json::Value>>> {
        let configuration: &configuration::Configuration<C> = self.configuration.borrow();

        let method = hyper::Method::POST;

        let query = ::url::form_urlencoded::Serializer::new(String::new())
            .append_pair("api-version", &api_version.to_string())
            .finish();
        let uri_str = format!("/device/reprovision?{}", query);

        let uri = (configuration.uri_composer)(&configuration.base_path, &uri_str);
        let mut req = hyper::Request::builder();
        req.method(method).uri(uri.unwrap());
        if let Some(ref user_agent) = configuration.user_agent {
            req.header(http::header::USER_AGENT, &**user_agent);
        }
        let req = req
            .body(hyper::Body::empty())
            .expect("could not build hyper::Request");

        // send request
        Box::new(
            configuration
                .client
                .request(req)
                .map_err(Error::from)
                .and_then(|resp| {
                    let (http::response::Parts { status, .. }, body) = resp.into_parts();
                    body.concat2()
                        .and_then(move |body| Ok((status, body)))
                        .map_err(Error::from)
                })
                .and_then(|(status, body)| {
                    if status.is_success() {
                        Ok(body)
                    } else {
                        Err(Error::from((status, &*body)))
                    }
                })
                .and_then(|_| futures::future::ok(())),
        )
    }
}
