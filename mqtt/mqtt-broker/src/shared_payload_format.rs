#![allow(dead_code)]
#![allow(unused_imports)]
#![allow(unused_variables)]

use std::collections::HashMap;
use std::io::{Read, Write};

use bytes::Bytes;

use crate::error::{Error, ErrorKind};
use crate::persist::FileFormat;
use crate::BrokerState;
/// Handles shared payloads
#[derive(Clone, Debug, Default)]
pub struct CustomFormat {
    payloads: Vec<Bytes>,
    payload_locations: HashMap<u64, u64>,
}

impl CustomFormat {
    pub fn new() -> Self {
        Self {
            payloads: Vec::new(),
            payload_locations: HashMap::new(),
        }
    }
}

impl FileFormat for CustomFormat {
    type Error = Error;

    fn load<R: Read>(&self, reader: R) -> Result<BrokerState, Self::Error> {
        Ok(BrokerState::default())
    }

    fn store<W: Write>(&self, writer: W, state: BrokerState) -> Result<(), Self::Error> {
        Ok(())
    }
}
