# AW workspace

Shared Rust UI policy for Excel AW and Word AW: a local profile for the same OS user and a document strip with close and reorder actions. Document contents and authentication credentials are not stored here.

The image import and profile card originate in Word AW's MIT/Apache-2.0 UI (`crates/ui-egui/src/profile.rs`, revision b6d353903e3839a3260ce96df86114631fc2d425). The shared persistence, corner avatar, tab controls and regression coverage extend that implementation.

Run `cargo test -p aw-workspace` and `cargo clippy -p aw-workspace --all-targets -- -D warnings` from the launcher repository. Both applications consume an exact Git revision of this crate. Their own commands handle selection, creation and unsaved-document confirmation.
