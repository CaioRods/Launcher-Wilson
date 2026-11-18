//! Shared local profile and document strip for the Alimentos Wilson applications.
#![deny(clippy::unwrap_used, clippy::expect_used, clippy::panic, clippy::unimplemented, clippy::todo, clippy::unreachable)]
#![forbid(unsafe_code)]

pub mod document_strip;
pub mod navigation;
pub mod profile;

#[cfg(not(target_arch = "wasm32"))]
pub mod control;
pub mod window_frame;
