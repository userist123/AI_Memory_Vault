//! AI App Studio desktop shell (fork of the AI Website Studio shell, DESIGN section 1).
//!
//! Responsibilities (and nothing else):
//!  0. API port 17875 (AAS_API_PORT overrides for dev/test; the CSP in tauri.conf.json allows 17875 only),
//!  1. generate a 256-bit owner token from the OS CSPRNG at startup,
//!  2. spawn `python -I apps/backend/main.py --token-stdin` (loopback) and hand it the token on stdin
//!     (first line) - never on a command line, in the environment, in a file or in a log,
//!  3. expose exactly one command, `get_session` -> `{ apiBase, token }`, to the `main` window only
//!     (capability `default`; preview frames are remote `*.localhost:17880` origins and get no IPC),
//!  4. make sure the backend dies with the app (explicit kill on exit plus a Windows job object with
//!     KILL_ON_JOB_CLOSE, which also covers a hard kill of the app).
//! Installed-runtime mode of the AWS shell is not carried over yet (repo mode only, see docs/slices/S4.md).

use std::io::Write;
use std::net::{SocketAddr, TcpStream};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::{Arc, Condvar, Mutex};
use std::time::Duration;

use rand::rngs::OsRng;
use rand::RngCore;
use serde::Serialize;
use tauri::{Manager, RunEvent, State};

const DEFAULT_API_PORT: u16 = 17875;

fn api_port() -> u16 {
    std::env::var("AAS_API_PORT")
        .ok()
        .and_then(|v| v.trim().parse::<u16>().ok())
        .filter(|p| *p >= 1024)
        .unwrap_or(DEFAULT_API_PORT)
}

/// Returned to the webview. Deliberately has no Debug impl so the token cannot be logged by accident.
#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct Session {
    api_base: String,
    token: String,
}

enum Phase {
    Starting,
    Ready(Session),
    Failed(String),
}

struct Backend {
    phase: Mutex<Phase>,
    changed: Condvar,
    child: Mutex<Option<Child>>,
    #[cfg(windows)]
    job: Mutex<Option<job::Job>>,
}

fn env_path(name: &str) -> Option<PathBuf> {
    std::env::var(name).ok().filter(|v| !v.trim().is_empty()).map(PathBuf::from)
}

/// Product root (03_IMPLEMENTATION/products/ai_app_studio): src-tauri -> desktop -> apps -> product.
fn product_root() -> PathBuf {
    if let Some(v) = env_path("AAS_REPO_ROOT") {
        return v;
    }
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(3)
        .map(Path::to_path_buf)
        .unwrap_or_else(|| PathBuf::from("."))
}

fn python_exe(root: &Path) -> PathBuf {
    if let Some(v) = env_path("AAS_PYTHON") {
        return v;
    }
    let venv = if cfg!(windows) {
        root.join(".venv").join("Scripts").join("python.exe")
    } else {
        root.join(".venv").join("bin").join("python")
    };
    if venv.exists() {
        venv
    } else {
        PathBuf::from(if cfg!(windows) { "python" } else { "python3" })
    }
}

fn generate_token() -> String {
    let mut bytes = [0u8; 32];
    OsRng.fill_bytes(&mut bytes);
    let mut s = String::with_capacity(64);
    for b in bytes {
        s.push_str(&format!("{b:02x}"));
    }
    s
}

fn port_in_use() -> bool {
    let addr: SocketAddr = ([127, 0, 0, 1], api_port()).into();
    TcpStream::connect_timeout(&addr, Duration::from_millis(300)).is_ok()
}

fn hide_window(cmd: &mut Command) {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        cmd.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    }
    #[cfg(not(windows))]
    let _ = cmd;
}

fn spawn_backend(token: &str) -> Result<Child, String> {
    let root = product_root();
    // `python -m apps.backend` (the package uses relative imports, so `python apps/backend/main.py` cannot work);
    // the current directory is the product root, which is what makes `apps` importable
    let script = root.join("apps").join("backend").join("__main__.py");
    if !script.exists() {
        return Err("backend entry point apps/backend/__main__.py not found (set AAS_REPO_ROOT)".into());
    }
    if port_in_use() {
        return Err(format!(
            "port {} is already in use by another process; refusing to talk to a backend that does not know this session's token",
            api_port()
        ));
    }
    let mut cmd = Command::new(python_exe(&root));
    cmd.arg("-X")
        .arg("utf8")
        .arg("-m")
        .arg("apps.backend")
        .arg("--token-stdin")
        .current_dir(&root)
        .env("AAS_API_PORT", api_port().to_string())
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    hide_window(&mut cmd);
    let mut child = cmd.spawn().map_err(|e| format!("could not start the backend: {e}"))?;
    let mut stdin = child.stdin.take().ok_or_else(|| "backend stdin unavailable".to_string())?;
    let write = stdin
        .write_all(token.as_bytes())
        .and_then(|_| stdin.write_all(b"\n"))
        .and_then(|_| stdin.flush());
    drop(stdin);
    if let Err(e) = write {
        let _ = child.kill();
        return Err(format!("could not hand the token to the backend: {e}"));
    }
    Ok(child)
}

impl Backend {
    fn new() -> Backend {
        Backend {
            phase: Mutex::new(Phase::Starting),
            changed: Condvar::new(),
            child: Mutex::new(None),
            #[cfg(windows)]
            job: Mutex::new(None),
        }
    }

    /// Attach mode (scripts/run_studio.ps1): the launcher already started the backend and hands the owner token to
    /// this process on the FIRST LINE OF STDIN (never argv, environment, file or log). Nothing is spawned here.
    fn attach(&self) {
        let mut line = String::new();
        let phase = match std::io::stdin().read_line(&mut line) {
            Ok(_) => {
                let token = line.trim().to_string();
                if token.len() >= 32 && token.len() <= 256 && token.chars().all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_') {
                    Phase::Ready(Session { api_base: format!("http://127.0.0.1:{}", api_port()), token })
                } else {
                    Phase::Failed("attach mode: the first stdin line is not a valid owner token".into())
                }
            }
            Err(e) => Phase::Failed(format!("attach mode: could not read the token from stdin: {e}")),
        };
        if let Ok(mut g) = self.phase.lock() {
            *g = phase;
        }
        self.changed.notify_all();
    }

    fn start(&self) {
        if std::env::var("AAS_ATTACH_BACKEND").map(|v| v == "1").unwrap_or(false) {
            return self.attach();
        }
        let token = generate_token();
        let phase = match spawn_backend(&token) {
            Ok(child) => {
                #[cfg(windows)]
                if let Some(j) = job::Job::new() {
                    if j.assign(&child) {
                        if let Ok(mut g) = self.job.lock() {
                            *g = Some(j);
                        }
                    }
                }
                if let Ok(mut g) = self.child.lock() {
                    *g = Some(child);
                }
                Phase::Ready(Session { api_base: format!("http://127.0.0.1:{}", api_port()), token })
            }
            Err(e) => Phase::Failed(e),
        };
        if let Ok(mut g) = self.phase.lock() {
            *g = phase;
        }
        self.changed.notify_all();
    }

    fn wait_session(&self) -> Result<Session, String> {
        let guard = self.phase.lock().map_err(|_| "backend state poisoned".to_string())?;
        let (guard, _) = self
            .changed
            .wait_timeout_while(guard, Duration::from_secs(120), |p| matches!(p, Phase::Starting))
            .map_err(|_| "backend state poisoned".to_string())?;
        let session = match &*guard {
            Phase::Ready(s) => s.clone(),
            Phase::Failed(e) => return Err(e.clone()),
            Phase::Starting => return Err("the backend is still starting; try again".into()),
        };
        drop(guard);
        if let Ok(mut g) = self.child.lock() {
            if let Some(child) = g.as_mut() {
                if let Ok(Some(status)) = child.try_wait() {
                    return Err(format!("the backend process exited unexpectedly ({status})"));
                }
            }
        }
        Ok(session)
    }

    fn kill(&self) {
        if let Ok(mut guard) = self.child.lock() {
            if let Some(mut child) = guard.take() {
                let _ = child.kill();
                let _ = child.wait();
            }
        }
    }
}

/// The only command exposed to the webview. Refuses any caller that is not the `main` window.
#[tauri::command]
async fn get_session(window: tauri::WebviewWindow, state: State<'_, Arc<Backend>>) -> Result<Session, String> {
    if window.label() != "main" {
        return Err("not allowed".into());
    }
    let backend = state.inner().clone();
    tauri::async_runtime::spawn_blocking(move || backend.wait_session())
        .await
        .map_err(|e| format!("backend wait failed: {e}"))?
}

pub fn run() {
    let backend = Arc::new(Backend::new());
    let app = tauri::Builder::default()
        .manage(backend.clone())
        .setup(move |_app| {
            let b = backend.clone();
            std::thread::spawn(move || b.start());
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![get_session])
        .build(tauri::generate_context!())
        .expect("error while building the application");

    app.run(|handle, event| {
        if let RunEvent::Exit = event {
            if let Some(backend) = handle.try_state::<Arc<Backend>>() {
                backend.kill();
            }
        }
    });
}

#[cfg(windows)]
mod job {
    use std::os::windows::io::AsRawHandle;
    use std::process::Child;
    use windows_sys::Win32::Foundation::{CloseHandle, HANDLE};
    use windows_sys::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation, SetInformationJobObject,
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    };

    /// When the last handle closes (the app exits, even if killed) the OS kills every process in the job.
    pub struct Job(HANDLE);

    unsafe impl Send for Job {}
    unsafe impl Sync for Job {}

    impl Job {
        pub fn new() -> Option<Job> {
            unsafe {
                let h = CreateJobObjectW(std::ptr::null(), std::ptr::null());
                if h as usize == 0 {
                    return None;
                }
                let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                let ok = SetInformationJobObject(
                    h,
                    JobObjectExtendedLimitInformation,
                    &info as *const _ as *const core::ffi::c_void,
                    std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
                );
                if ok == 0 {
                    CloseHandle(h);
                    return None;
                }
                Some(Job(h))
            }
        }

        pub fn assign(&self, child: &Child) -> bool {
            unsafe { AssignProcessToJobObject(self.0, child.as_raw_handle() as HANDLE) != 0 }
        }
    }

    impl Drop for Job {
        fn drop(&mut self) {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }
}
