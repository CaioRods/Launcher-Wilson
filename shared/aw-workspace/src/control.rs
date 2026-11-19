//! Bounded authenticated requests to the running AWcontrol, with standalone fallback.
use serde::{Deserialize, Serialize};
use std::{
    io::{Read, Write},
    net::{Ipv4Addr, SocketAddrV4, TcpStream},
    path::{Path, PathBuf},
    time::Duration,
};
#[derive(Deserialize)]
struct Endpoint {
    port: u16,
    token: String,
}
#[derive(Serialize)]
struct Request<'a> {
    token: &'a str,
    app: u8,
    files: Vec<String>,
    startup: Option<&'a serde_json::Value>,
}
pub fn embedded() -> bool {
    cfg!(windows) && std::env::var_os("AWCONTROL_EMBEDDED").is_some_and(|v| v == "1")
}
fn endpoint_path() -> Option<PathBuf> {
    std::env::var_os("APPDATA").map(|p| PathBuf::from(p).join("AlimentosWilson/workspace.json"))
}
/// The server acknowledges only requests queued for its UI. Failed sends leave the caller standalone.
pub fn forward(app: u8, files: &[PathBuf]) -> bool {
    forward_startup(app, files, None)
}
fn forward_startup(app: u8, files: &[PathBuf], startup: Option<&serde_json::Value>) -> bool {
    let Some(path) = endpoint_path() else { return false };
    if !std::fs::metadata(&path).is_ok_and(|m| m.len() <= 4096) {
        return false;
    }
    let Ok(bytes) = std::fs::read(path) else { return false };
    if bytes.len() > 4096 {
        return false;
    }
    let Ok(endpoint) = serde_json::from_slice::<Endpoint>(&bytes) else { return false };
    send(&endpoint, app, files, startup).is_ok()
}
fn absolute(path: &Path) -> std::io::Result<String> {
    Ok(std::path::absolute(path)?.to_string_lossy().into_owned())
}
fn send(endpoint: &Endpoint, app: u8, files: &[PathBuf], startup: Option<&serde_json::Value>) -> std::io::Result<()> {
    if app > 1 || files.len() > 64 || endpoint.token.len() != 64 {
        return Err(std::io::Error::other("invalid workspace request"));
    }
    let files = files.iter().map(|p| absolute(p)).collect::<Result<Vec<_>, _>>()?;
    let mut bytes = serde_json::to_vec(&Request { token: &endpoint.token, app, files, startup })?;
    if bytes.len() > 65535 {
        return Err(std::io::Error::other("workspace request too large"));
    }
    bytes.push(b'\n');
    let timeout = Duration::from_millis(750);
    let mut socket = TcpStream::connect_timeout(&SocketAddrV4::new(Ipv4Addr::LOCALHOST, endpoint.port).into(), Duration::from_millis(200))?;
    socket.set_read_timeout(Some(timeout))?;
    socket.set_write_timeout(Some(timeout))?;
    socket.write_all(&bytes)?;
    let mut reply = [0; 3];
    socket.read_exact(&mut reply)?;
    if reply == *b"OK\n" { Ok(()) } else { Err(std::io::Error::other("workspace did not accept request")) }
}
/// Routes explicit file commands in an embedded editor. Startup loads happen before embedding is enabled.
pub fn route(app: u8, id: &str, params: &serde_json::Value) -> Option<Result<serde_json::Value, String>> {
    let files = match id {
        "file.new" | "file.newFromTemplate" => Vec::new(),
        "file.open" if params.get("path").and_then(|p| p.as_str()).is_some() => vec![PathBuf::from(params.get("path")?.as_str()?)],
        _ => return None,
    };
    let startup = matches!(id, "file.new" | "file.newFromTemplate").then(|| serde_json::json!({"command":id,"params":params}));
    Some(if forward_startup(app, &files, startup.as_ref()) {
        Ok(serde_json::json!({"forwarded":true}))
    } else {
        Err("AWcontrol não pôde abrir a página. Tente novamente na central.".into())
    })
}
/// Only creation commands cross the startup boundary; no arbitrary command or shell is accepted.
pub fn startup() -> Option<(String, serde_json::Value)> {
    if !embedded() {
        return None;
    }
    let text = std::env::var("AWCONTROL_STARTUP").ok()?;
    if text.len() > 16384 {
        return None;
    }
    let value: serde_json::Value = serde_json::from_str(&text).ok()?;
    let command = value.get("command")?.as_str()?;
    if !matches!(command, "file.new" | "file.newFromTemplate") {
        return None;
    }
    let params = value.get("params").cloned().unwrap_or_else(|| serde_json::json!({}));
    if command == "file.new" && params.as_object().is_some_and(|p| p.is_empty()) {
        return None;
    }
    Some((command.to_owned(), params))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn request_requires_an_ack_and_preserves_paths() {
        let listener = std::net::TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).unwrap();
        let endpoint = Endpoint { port: listener.local_addr().unwrap().port(), token: "a".repeat(64) };
        let server = std::thread::spawn(move || {
            let (mut s, _) = listener.accept().unwrap();
            let mut line = String::new();
            std::io::BufRead::read_line(&mut std::io::BufReader::new(s.try_clone().unwrap()), &mut line).unwrap();
            let r: serde_json::Value = serde_json::from_str(&line).unwrap();
            assert_eq!(r["app"], 1);
            assert!(r["files"][0].as_str().unwrap().ends_with("relatório com espaços.docx"));
            s.write_all(b"OK\n").unwrap();
        });
        assert!(send(&endpoint, 1, &[PathBuf::from("relatório com espaços.docx")], None).is_ok());
        server.join().unwrap();
    }
    #[test]
    fn invalid_or_oversized_requests_are_rejected_before_connecting() {
        let e = Endpoint { port: 1, token: "b".repeat(64) };
        assert!(send(&e, 2, &[], None).is_err());
        assert!(send(&e, 0, &vec![PathBuf::from("file"); 65], None).is_err());
    }
}
