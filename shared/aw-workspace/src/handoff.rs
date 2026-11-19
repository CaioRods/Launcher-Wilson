//! Private, bounded checkpoints for moving one document out of a multi-document process.
use serde::{Deserialize, Serialize, de::DeserializeOwned};
use std::io::{Read, Write};
use std::path::PathBuf;
const LIMIT: u64 = 128 * 1024 * 1024;
#[derive(Serialize, Deserialize)]
pub struct Checkpoint<T> {
    pub version: u8,
    pub app: u8,
    pub source_pid: u32,
    pub document: u64,
    pub revision: u64,
    pub payload: T,
    pub ui: serde_json::Value,
}
fn path(id: &str) -> Result<PathBuf, String> {
    if id.len() != 64 || !id.bytes().all(|b| b.is_ascii_hexdigit()) {
        return Err("Invalid transfer identifier".into());
    }
    let base = std::env::var_os(if cfg!(windows) { "APPDATA" } else { "XDG_DATA_HOME" })
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("HOME").map(|p| PathBuf::from(p).join(".local/share")))
        .ok_or("No private user folder")?;
    Ok(base.join("AlimentosWilson").join("transfers").join(format!("{id}.json")))
}
struct Bounded(std::fs::File, u64);
impl Write for Bounded {
    fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
        if bytes.len() as u64 > self.1 {
            return Err(std::io::Error::other("Transfer exceeds 128 MiB"));
        }
        let n = self.0.write(bytes)?;
        self.1 -= n as u64;
        Ok(n)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        self.0.flush()
    }
}
pub fn write<T: Serialize>(id: &str, checkpoint: &Checkpoint<T>) -> Result<(), String> {
    let path = path(id)?;
    let folder = path.parent().ok_or("Invalid transfer folder")?;
    std::fs::create_dir_all(folder).map_err(|e| e.to_string())?;
    let mut options = std::fs::OpenOptions::new();
    options.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    let file = options.open(&path).map_err(|e| e.to_string())?;
    let mut writer = std::io::BufWriter::new(Bounded(file, LIMIT));
    let result = serde_json::to_writer(&mut writer, checkpoint).map_err(|e| e.to_string()).and_then(|()| writer.flush().map_err(|e| e.to_string()));
    drop(writer);
    if result.is_err() {
        let _ = std::fs::remove_file(path);
    }
    result
}
pub fn read<T: DeserializeOwned>(id: &str, app: u8) -> Result<Checkpoint<T>, String> {
    let file = std::fs::File::open(path(id)?).map_err(|e| e.to_string())?;
    if file.metadata().map_err(|e| e.to_string())?.len() > LIMIT {
        return Err("Transfer exceeds 128 MiB".into());
    }
    let checkpoint: Checkpoint<T> = serde_json::from_reader(std::io::BufReader::new(file.take(LIMIT + 1))).map_err(|e| e.to_string())?;
    if checkpoint.version != 1 || checkpoint.app != app {
        return Err("Incompatible transfer".into());
    }
    Ok(checkpoint)
}
#[cfg(test)]
mod tests {
    #[test]
    fn rejects_arbitrary_paths() {
        for id in ["../../document", "", "/tmp/book"] {
            assert!(super::path(id).is_err());
        }
    }
}
