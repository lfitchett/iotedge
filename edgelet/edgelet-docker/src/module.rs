// Copyright (c) Microsoft. All rights reserved.

use std::str::FromStr;

use chrono::prelude::*;
use failure::ResultExt;
use futures::Future;
use hyper::client::connect::Connect;

use bollard::service::{ContainerState, ContainerStateStatusEnum};
use edgelet_core::{
    Module, ModuleOperation, ModuleRuntimeState, ModuleStatus, ModuleTop, RuntimeOperation,
};
use edgelet_utils::ensure_not_empty_with_context;

use crate::client::DockerClient;
use crate::config::DockerConfig;
use crate::error::{Error, ErrorKind, Result};

type Deserializer = &'static mut serde_json::Deserializer<serde_json::de::IoRead<std::io::Empty>>;

pub const MODULE_TYPE: &str = "docker";
pub const MIN_DATE: &str = "0001-01-01T00:00:00Z";

pub struct DockerModule {
    client: DockerClient,
    name: String,
    config: DockerConfig,
}

impl std::fmt::Debug for DockerModule {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("DockerModule").finish()
    }
}

impl DockerModule {
    pub fn new(client: DockerClient, name: String, config: DockerConfig) -> Result<Self> {
        ensure_not_empty_with_context(&name, || ErrorKind::InvalidModuleName(name.clone()))?;

        Ok(DockerModule {
            client,
            name,
            config,
        })
    }
}

#[async_trait::async_trait]
pub trait DockerModuleTop {
    async fn top(&self) -> Result<ModuleTop>;
}

#[async_trait::async_trait]
impl DockerModuleTop for DockerModule {
    async fn top(&self) -> Result<ModuleTop> {
        let error = || {
            Error::from(ErrorKind::RuntimeOperation(RuntimeOperation::TopModule(
                self.name.clone(),
            )))
        };

        let top_response = self
            .client
            .docker
            .top_processes::<&str>(&self.name, None)
            .await
            .map_err(|_| error())?;

        let pids: Vec<i32> = if let Some(titles) = top_response.titles {
            let pid_index: usize = titles
                .iter()
                .position(|s| s.as_str() == "PID")
                .ok_or_else(error)?;

            if let Some(processes) = top_response.processes {
                processes
                    .iter()
                    .map(|process| {
                        let str_pid = process.get(pid_index).ok_or_else(error)?;
                        str_pid.parse::<i32>().map_err(|_| error())
                    })
                    .collect::<Result<Vec<i32>>>()?
            } else {
                return Err(error());
            }
        } else {
            return Err(error());
        };

        Ok(ModuleTop::new(self.name.clone(), pids))
    }
}

fn status_from_exit_code(exit_code: Option<i64>) -> Option<ModuleStatus> {
    exit_code.map(|code| {
        if code == 0 {
            ModuleStatus::Stopped
        } else {
            ModuleStatus::Failed
        }
    })
}

pub fn runtime_state(
    id: Option<String>,
    response_state: Option<&ContainerState>,
) -> ModuleRuntimeState {
    response_state.map_or_else(ModuleRuntimeState::default, |state| {
        let status = state
            .status
            .and_then(|status| match status {
                ContainerStateStatusEnum::CREATED
                | ContainerStateStatusEnum::PAUSED
                | ContainerStateStatusEnum::RESTARTING => Some(ModuleStatus::Stopped),
                ContainerStateStatusEnum::REMOVING
                | ContainerStateStatusEnum::DEAD
                | ContainerStateStatusEnum::EXITED => status_from_exit_code(state.exit_code),
                ContainerStateStatusEnum::RUNNING => Some(ModuleStatus::Running),
                _ => Some(ModuleStatus::Unknown),
            })
            .unwrap_or(ModuleStatus::Unknown);
        ModuleRuntimeState::default()
            .with_status(status)
            .with_exit_code(state.exit_code)
            .with_status_description(state.status.map(|s| s.to_string()))
            .with_started_at(
                state
                    .started_at
                    .as_ref()
                    .and_then(|d| if d == MIN_DATE { None } else { Some(d) })
                    .and_then(|started_at| DateTime::from_str(started_at).ok()),
            )
            .with_finished_at(
                state
                    .finished_at
                    .as_ref()
                    .and_then(|d| if d == MIN_DATE { None } else { Some(d) })
                    .and_then(|finished_at| DateTime::from_str(finished_at).ok()),
            )
            .with_image_id(id)
            .with_pid(state.pid)
    })
}

#[async_trait::async_trait]
impl Module for DockerModule {
    type Config = DockerConfig;
    type Error = Error;
    fn name(&self) -> &str {
        &self.name
    }

    fn type_(&self) -> &str {
        MODULE_TYPE
    }

    fn config(&self) -> &Self::Config {
        &self.config
    }

    async fn runtime_state(&self) -> Result<ModuleRuntimeState> {
        let inspect = self
            .client
            .docker
            .inspect_container(&self.name, None)
            .await
            .map_err(|_| Error::from(ErrorKind::ModuleOperation(ModuleOperation::RuntimeState)))?;

        Ok(runtime_state(inspect.id.clone(), inspect.state.as_ref()))
    }
}

#[cfg(test)]
mod tests {
    use super::{parse_top_response, Deserializer, InlineResponse2001, Utc, MIN_DATE};

    use std::string::ToString;

    use hyper::Client;
    use serde::Serialize;
    use time::Duration;

    use docker::apis::client::APIClient;
    use docker::apis::configuration::Configuration;
    use docker::models::{ContainerCreateBody, InlineResponse200, InlineResponse200State};
    use edgelet_core::{Module, ModuleStatus};
    use edgelet_test_utils::JsonConnector;

    use crate::client::DockerClient;
    use crate::config::DockerConfig;
    use crate::module::DockerModule;

    fn create_api_client<T: Serialize>(body: T) -> DockerClient<JsonConnector> {
        let client = Client::builder().build(JsonConnector::new(&body));

        let mut config = Configuration::new(client);
        config.base_path = "http://localhost/".to_string();
        config.uri_composer =
            Box::new(|base_path, path| Ok(format!("{}{}", base_path, path).parse().unwrap()));

        DockerClient::new(APIClient::new(config))
    }

    #[test]
    fn new_instance() {
        let docker_module = DockerModule::new(
            create_api_client("boo"),
            "mod1".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap();

        assert_eq!("mod1", docker_module.name());
        assert_eq!("docker", docker_module.type_());
        assert_eq!("ubuntu", docker_module.config().image());
    }

    #[test]
    fn empty_name_fails() {
        let _ = DockerModule::new(
            create_api_client("boo"),
            "".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap_err();
    }

    #[test]
    fn white_space_name_fails() {
        let _ = DockerModule::new(
            create_api_client("boo"),
            "     ".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap_err();
    }

    fn get_inputs() -> Vec<(&'static str, i64, ModuleStatus)> {
        vec![
            ("created", 0, ModuleStatus::Stopped),
            ("paused", 0, ModuleStatus::Stopped),
            ("restarting", 0, ModuleStatus::Stopped),
            ("removing", 0, ModuleStatus::Stopped),
            ("dead", 0, ModuleStatus::Stopped),
            ("exited", 0, ModuleStatus::Stopped),
            ("removing", -1, ModuleStatus::Failed),
            ("dead", -2, ModuleStatus::Failed),
            ("exited", -42, ModuleStatus::Failed),
            ("running", 0, ModuleStatus::Running),
        ]
    }

    #[test]
    fn module_status() {
        let inputs = get_inputs();

        for &(docker_status, exit_code, ref module_status) in &inputs {
            let docker_module = DockerModule::new(
                create_api_client(
                    InlineResponse200::new().with_state(
                        InlineResponse200State::new()
                            .with_status(docker_status.to_string())
                            .with_exit_code(exit_code),
                    ),
                ),
                "mod1".to_string(),
                DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                    .unwrap(),
            )
            .unwrap();

            let state = tokio::runtime::current_thread::Runtime::new()
                .unwrap()
                .block_on(docker_module.runtime_state())
                .unwrap();
            assert_eq!(module_status, state.status());
        }
    }

    #[test]
    fn module_runtime_state() {
        let started_at = Utc::now().to_rfc3339();
        let finished_at = (Utc::now() + Duration::hours(1)).to_rfc3339();
        let docker_module = DockerModule::new(
            create_api_client(
                InlineResponse200::new()
                    .with_state(
                        InlineResponse200State::new()
                            .with_exit_code(10)
                            .with_status("running".to_string())
                            .with_started_at(started_at.clone())
                            .with_finished_at(finished_at.clone())
                            .with_pid(1234),
                    )
                    .with_id("mod1".to_string())
                    .with_exec_i_ds(vec!["id1".to_string(), "id2".to_string()]),
            ),
            "mod1".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap();

        let runtime_state = tokio::runtime::current_thread::Runtime::new()
            .unwrap()
            .block_on(docker_module.runtime_state())
            .unwrap();

        assert_eq!(ModuleStatus::Running, *runtime_state.status());
        assert_eq!(10, runtime_state.exit_code().unwrap());
        assert_eq!(&"running", &runtime_state.status_description().unwrap());
        assert_eq!(started_at, runtime_state.started_at().unwrap().to_rfc3339());
        assert_eq!(
            finished_at,
            runtime_state.finished_at().unwrap().to_rfc3339()
        );
        assert_eq!(Some(1234), runtime_state.pid());
    }

    #[test]
    fn module_runtime_state_failed_from_dead() {
        let started_at = Utc::now().to_rfc3339();
        let finished_at = (Utc::now() + Duration::hours(1)).to_rfc3339();
        let docker_module = DockerModule::new(
            create_api_client(
                InlineResponse200::new()
                    .with_state(
                        InlineResponse200State::new()
                            .with_exit_code(10)
                            .with_status("dead".to_string())
                            .with_started_at(started_at.clone())
                            .with_finished_at(finished_at.clone()),
                    )
                    .with_id("mod1".to_string()),
            ),
            "mod1".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap();

        let runtime_state = tokio::runtime::current_thread::Runtime::new()
            .unwrap()
            .block_on(docker_module.runtime_state())
            .unwrap();

        assert_eq!(ModuleStatus::Failed, *runtime_state.status());
        assert_eq!(10, runtime_state.exit_code().unwrap());
        assert_eq!(&"dead", &runtime_state.status_description().unwrap());
        assert_eq!(started_at, runtime_state.started_at().unwrap().to_rfc3339());
        assert_eq!(
            finished_at,
            runtime_state.finished_at().unwrap().to_rfc3339()
        );
    }

    #[test]
    fn module_runtime_state_with_bad_started_at() {
        let started_at = "not really a date".to_string();
        let finished_at = (Utc::now() + Duration::hours(1)).to_rfc3339();
        let docker_module = DockerModule::new(
            create_api_client(
                InlineResponse200::new()
                    .with_state(
                        InlineResponse200State::new()
                            .with_exit_code(10)
                            .with_status("running".to_string())
                            .with_started_at(started_at)
                            .with_finished_at(finished_at),
                    )
                    .with_id("mod1".to_string()),
            ),
            "mod1".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap();

        let runtime_state = tokio::runtime::current_thread::Runtime::new()
            .unwrap()
            .block_on(docker_module.runtime_state())
            .unwrap();

        assert_eq!(None, runtime_state.started_at());
    }

    #[test]
    fn module_runtime_state_with_bad_finished_at() {
        let started_at = Utc::now().to_rfc3339();
        let finished_at = "nope, not a date".to_string();
        let docker_module = DockerModule::new(
            create_api_client(
                InlineResponse200::new()
                    .with_state(
                        InlineResponse200State::new()
                            .with_exit_code(10)
                            .with_status("running".to_string())
                            .with_started_at(started_at)
                            .with_finished_at(finished_at),
                    )
                    .with_id("mod1".to_string()),
            ),
            "mod1".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap();

        let runtime_state = tokio::runtime::current_thread::Runtime::new()
            .unwrap()
            .block_on(docker_module.runtime_state())
            .unwrap();

        assert_eq!(None, runtime_state.finished_at());
    }

    #[test]
    fn module_runtime_state_with_min_dates() {
        let started_at = MIN_DATE.to_string();
        let finished_at = MIN_DATE.to_string();
        let docker_module = DockerModule::new(
            create_api_client(
                InlineResponse200::new()
                    .with_state(
                        InlineResponse200State::new()
                            .with_exit_code(10)
                            .with_status("stopped".to_string())
                            .with_started_at(started_at)
                            .with_finished_at(finished_at),
                    )
                    .with_id("mod1".to_string()),
            ),
            "mod1".to_string(),
            DockerConfig::new("ubuntu".to_string(), ContainerCreateBody::new(), None, None)
                .unwrap(),
        )
        .unwrap();

        let runtime_state = tokio::runtime::current_thread::Runtime::new()
            .unwrap()
            .block_on(docker_module.runtime_state())
            .unwrap();

        assert_eq!(None, runtime_state.started_at());
        assert_eq!(None, runtime_state.finished_at());
    }

    #[test]
    fn parse_top_response_returns_pid_array() {
        let response = InlineResponse2001::new()
            .with_titles(vec!["PID".to_string()])
            .with_processes(vec![vec!["123".to_string()]]);

        let pids = parse_top_response::<Deserializer>(&response);

        assert_eq!(vec![123], pids.unwrap());
    }

    #[test]
    fn parse_top_response_returns_error_when_titles_is_missing() {
        let response = InlineResponse2001::new().with_processes(vec![vec!["123".to_string()]]);

        let pids = parse_top_response::<Deserializer>(&response);

        assert_eq!("missing field `Titles`", format!("{}", pids.unwrap_err()));
    }

    #[test]
    fn parse_top_response_returns_error_when_pid_title_is_missing() {
        let response = InlineResponse2001::new().with_titles(vec!["Command".to_string()]);

        let pids = parse_top_response::<Deserializer>(&response);

        assert_eq!(
            "invalid value: sequence, expected array including the column title 'PID'",
            format!("{}", pids.unwrap_err())
        );
    }

    #[test]
    fn parse_top_response_returns_error_when_processes_is_missing() {
        let response = InlineResponse2001::new().with_titles(vec!["PID".to_string()]);

        let pids = parse_top_response::<Deserializer>(&response);

        assert_eq!(
            "missing field `Processes`",
            format!("{}", pids.unwrap_err())
        );
    }

    #[test]
    fn parse_top_response_returns_error_when_process_pid_is_missing() {
        let response = InlineResponse2001::new()
            .with_titles(vec!["Command".to_string(), "PID".to_string()])
            .with_processes(vec![vec!["sh".to_string()]]);

        let pids = parse_top_response::<Deserializer>(&response);

        assert_eq!(
            "invalid length 1, expected at least 2 columns",
            format!("{}", pids.unwrap_err())
        );
    }

    #[test]
    fn parse_top_response_returns_error_when_process_pid_is_not_i32() {
        let response = InlineResponse2001::new()
            .with_titles(vec!["PID".to_string()])
            .with_processes(vec![vec!["xyz".to_string()]]);

        let pids = parse_top_response::<Deserializer>(&response);

        assert_eq!(
            "invalid value: string \"xyz\", expected a process ID number",
            format!("{}", pids.unwrap_err())
        );
    }
}
