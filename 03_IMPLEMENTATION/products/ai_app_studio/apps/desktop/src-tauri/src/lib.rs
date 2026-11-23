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

/// Isolated launcher (S6 review): `python -I` ignores every PYTHON* variable, the user site and the current
/// directory, so nothing on sys.path comes from the environment or cwd; the product root is put on sys.path
/// explicitly (argv[1], never interpolated into code) and `apps.backend` is run with runpy.
pub(crate) const BACKEND_LAUNCHER: &str = "import runpy, sys; root = sys.argv.pop(1); sys.path.insert(0, root); \
runpy.run_module('apps.backend', run_name='__main__', alter_sys=True)";

/// Variables removed from the backend's environment (on top of `-I`): dev-only switches and interpreter knobs.
pub(crate) fn scrubbed_var(name: &std::ffi::OsStr) -> bool {
    let n = name.to_string_lossy().to_ascii_uppercase();
    n.starts_with("AAS_DEV_") || n.starts_with("PYTHON")
}

pub(crate) fn backend_command(
    python: &Path,
    root: &Path,
    inherited: impl Iterator<Item = std::ffi::OsString>,
) -> Command {
    let mut cmd = Command::new(python);
    cmd.arg("-I")
        .arg("-X")
        .arg("utf8")
        .arg("-c")
        .arg(BACKEND_LAUNCHER)
        .arg(root)
        .arg("--token-stdin")
        .current_dir(root);
    for k in inherited {
        if scrubbed_var(&k) {
            cmd.env_remove(&k);
        }
    }
    cmd.env("AAS_API_PORT", api_port().to_string());
    cmd
}

fn spawn_backend(token: &str) -> Result<Child, String> {
    let root = product_root();
    // `apps.backend` is run as a package (relative imports) by the isolated launcher (`backend_command`)
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
    let mut cmd = backend_command(&python_exe(&root), &root, std::env::vars_os().map(|(k, _)| k));
    cmd.stdin(Stdio::piped()).stdout(Stdio::null()).stderr(Stdio::null());
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

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::{OsStr, OsString};

    #[test]
    fn backend_runs_isolated_with_explicit_root_and_scrubbed_env() {
        let inherited = ["AAS_DEV_ORIGINS", "aas_dev_x", "PYTHONPATH", "PYTHONHOME", "PYTHONSTARTUP", "PATH", "AAS_DATA_DIR"]
            .iter()
            .map(OsString::from);
        let cmd = backend_command(Path::new("py.exe"), Path::new("R:/prod"), inherited);
        let args: Vec<&OsStr> = cmd.get_args().collect();
        assert_eq!(args[0], "-I");
        assert_eq!(&args[1..3], &[OsStr::new("-X"), OsStr::new("utf8")]);
        assert_eq!(args[3], "-c");
        assert_eq!(args[4], BACKEND_LAUNCHER);
        assert_eq!(args[5], "R:/prod");
        assert_eq!(args[6], "--token-stdin");
        assert!(!args.iter().any(|a| *a == "-m"));
        let envs: Vec<(String, Option<String>)> = cmd
            .get_envs()
            .map(|(k, v)| (k.to_string_lossy().into_owned(), v.map(|v| v.to_string_lossy().into_owned())))
            .collect();
        for gone in ["AAS_DEV_ORIGINS", "aas_dev_x", "PYTHONPATH", "PYTHONHOME", "PYTHONSTARTUP"] {
            assert!(envs.iter().any(|(k, v)| k == gone && v.is_none()), "{gone} must be removed: {envs:?}");
        }
        for kept in ["PATH", "AAS_DATA_DIR"] {
            assert!(!envs.iter().any(|(k, _)| k == kept), "{kept} must be inherited untouched");
        }
        assert!(envs.iter().any(|(k, v)| k == "AAS_API_PORT" && v.is_some()));
        assert_eq!(cmd.get_current_dir(), Some(Path::new("R:/prod")));
    }

    #[test]
    fn launcher_takes_the_root_from_argv_not_from_code() {
        assert!(BACKEND_LAUNCHER.contains("sys.argv.pop(1)"));
        assert!(BACKEND_LAUNCHER.contains("run_module('apps.backend'"));
        assert!(scrubbed_var(OsStr::new("PythonPath")) && !scrubbed_var(OsStr::new("AAS_DATA_DIR")));
    }
}
