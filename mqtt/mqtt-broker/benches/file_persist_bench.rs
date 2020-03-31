use std::any::type_name;
use std::collections::{HashMap, VecDeque};
use std::io::Cursor;
use std::iter::FromIterator;

use async_trait::async_trait;
use bytes::Bytes;
use criterion::*;
use mqtt3::proto::{Publication, QoS};
use mqtt_broker::{
    BincodeFormat, BrokerState, ClientId, CompressedBincodeFormat,
    CompressedConsolidatedStateFormat, ConsolidatedStateFormat, FileFormat, FilePersistor, Persist,
    SessionState,
};
use tempfile::TempDir;
use tokio::runtime::Runtime;

fn test_write<F>(
    c: &mut Criterion,
    num_clients: u32,
    num_unique_messages: u32,
    num_shared_messages: u32,
    num_retained: u32,
    format: F,
) where
    F: FileFormat + Clone + Send + 'static,
{
    let name = format!(
        "{}: Write {} unique and {} shared messages for {} sessions with {} retained messages",
        type_name::<F>(),
        num_unique_messages,
        num_shared_messages,
        num_clients,
        num_retained
    );

    c.bench_function(&name, |b| {
        b.iter_batched(
            || {
                let state = make_fake_state(
                    num_clients,
                    num_unique_messages,
                    num_shared_messages,
                    num_retained,
                );

                let tmp_dir = TempDir::new().unwrap();
                let path = tmp_dir.path().to_owned();
                let persistor = FilePersistor::new(path, format.clone());
                let rt = Runtime::new().unwrap();

                (state, persistor, rt, tmp_dir)
            },
            |(state, mut persistor, mut rt, _tmp_dir)| {
                rt.block_on(async { persistor.store(state).await })
            },
            BatchSize::SmallInput,
        );
    });
}

fn test_read<F>(
    c: &mut Criterion,
    num_clients: u32,
    num_unique_messages: u32,
    num_shared_messages: u32,
    num_retained: u32,
    format: F,
) where
    F: FileFormat + Clone + Send + 'static,
{
    let name = format!(
        "{}: Read {} unique and {} shared messages for {} sessions with {} retained messages",
        type_name::<F>(),
        num_unique_messages,
        num_shared_messages,
        num_clients,
        num_retained
    );

    c.bench_function(&name, |b| {
        b.iter_batched(
            || {
                let state = make_fake_state(
                    num_clients,
                    num_unique_messages,
                    num_shared_messages,
                    num_retained,
                );

                let tmp_dir = TempDir::new().unwrap();
                let path = tmp_dir.path().to_owned();
                let mut persistor = FilePersistor::new(path, format.clone());

                Runtime::new()
                    .unwrap()
                    .block_on(async { persistor.store(state).await })
                    .unwrap();
                let rt = Runtime::new().unwrap();
                (persistor, rt, tmp_dir)
            },
            |(mut persistor, mut rt, _tmp_dir)| rt.block_on(async { persistor.load().await }),
            BatchSize::SmallInput,
        );
    });
}

fn make_fake_state(
    num_clients: u32,
    num_unique_messages: u32,
    num_shared_messages: u32,
    num_retained: u32,
) -> BrokerState {
    let retained = HashMap::from_iter((0..num_retained).map(|i| {
        (
            format!("Retained {}", i),
            make_fake_publish(format!("Retained {}", i)),
        )
    }));

    let shared_messages: Vec<Publication> = (0..num_shared_messages)
        .map(|_| make_fake_publish("Shared Topic".to_owned()))
        .collect();

    let sessions = (0..num_clients)
        .map(|i| {
            let waiting_to_be_sent = (0..num_unique_messages)
                .map(|_| make_fake_publish(format!("Topic {}", i)))
                .chain(shared_messages.clone());

            SessionState::from_parts(
                ClientId::from(format!("Session {}", i)),
                HashMap::new(),
                VecDeque::from_iter(waiting_to_be_sent),
            )
        })
        .collect();

    BrokerState::new(retained, sessions)
}

fn make_fake_publish(topic_name: String) -> Publication {
    Publication {
        topic_name,
        retain: false,
        qos: QoS::AtLeastOnce,
        payload: make_random_payload(10),
    }
}

fn make_random_payload(size: u32) -> Bytes {
    Bytes::from_iter((0..size).map(|_| rand::random::<u8>()))
}

fn bench(c: &mut Criterion) {
    let tests = &[
        (1, 1, 0, 0),
        (10, 10, 10, 10),
        (10, 100, 0, 0),
        (10, 0, 100, 0),
    ];

    for (clients, unique, shared, retained) in tests {
        macro_rules! test_formats {
            ( $( $f:expr ),* ) => {{
                $(
                    test_write(c, *clients, *unique, *shared, *retained, $f);
                    test_read(c, *clients, *unique, *shared, *retained, $f);
                )*
            }};
        }

        test_formats![
            ConsolidatedStateFormat::default(),
            CompressedConsolidatedStateFormat::default(),
            BincodeFormat::default(),
            CompressedBincodeFormat::default()
        ]
    }
}

criterion_group!(basic, bench);
criterion_main!(basic);

#[derive(Debug)]
pub struct BufferPersistor<F> {
    format: F,
    buffer: Vec<u8>,
}

impl<F> BufferPersistor<F> {
    pub fn new(format: F) -> Self {
        Self {
            format,
            buffer: vec![0_u8; 10 * 1024 * 1024],
        }
    }
}

#[async_trait]
impl<F> Persist for BufferPersistor<F>
where
    F: FileFormat + Clone + Send + 'static,
{
    type Error = F::Error;

    async fn load(&mut self) -> Result<Option<BrokerState>, Self::Error> {
        let reader = Cursor::new(&mut self.buffer);

        Ok(Some(self.format.load(reader)?))
    }

    async fn store(&mut self, state: BrokerState) -> Result<(), Self::Error> {
        let writer = Cursor::new(&mut self.buffer);

        self.format.store(writer, state)
    }
}
