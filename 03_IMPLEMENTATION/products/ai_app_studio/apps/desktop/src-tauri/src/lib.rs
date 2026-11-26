//! AI App Studio desktop shell (fork of the AI Website Studio shell, DESIGN section 1).
//!
//! Responsibilities (and nothing else):
//!  0. API port 17875 (AAS_API_PORT overrides for dev/test; the CSP in tauri.conf.json allows 17875 only),
//!  1. generate a 256-bit owner token from the OS CSPRNG at startup,
//!  2. spawn the backend (`python -I` + runpy launcher of `apps/backend/__main__.py`, `--token-stdin`, loopback) and hand it the token on stdin
//!     (first line) - never on a command line, in the environment, in a file or in a log,
//!  3. expose exactly one command, `get_session` -> `{ apiBase, token }`, to the `main` window only
//!     (capability `default`; preview frames are remote `*.localhost:17880` origins and get no IPC),
//!  4. make sure the backend dies with the app (explicit kill on exit plus a Windows job object with
//!     KILL_ON_JOB_CLOSE, which also covers a hard kill of the app).
//!  5. S9 (B3): where the product lives is decided at RUN time, never at compile time. Installed mode (release
//!     build, the bundle carries `runtime/runtime.zip` + `runtime.version`, AAS_REPO_ROOT unset): the runtime
//!     (Python + dependencies + product sources) is extracted once per version with the OS `tar.exe` into
//!     <app local data>\runtime\<version> and the backend runs from `<runtime>\app`. Repo mode (debug builds, or no
//!     bundled runtime): AAS_REPO_ROOT, else the first ancestor of the running exe that holds
//!     `apps/backend/__main__.py` + `core/aas_build`. The backend hosts the preview server itself (one process) and
//!     logs to <data root>\logs\backend.log (capped, `--log-to-data-root`); output before Python takes over its
//!     streams lands in <data root>\logs\backend-boot.log (truncated at every start).

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

/// How to launch the backend: interpreter, product root (cwd + sys.path), extra environment.
pub(crate) struct Launch {
    python: PathBuf,
    root: PathBuf,
    env: Vec<(String, String)>,
}

/// Repo mode: the first ancestor of `start` (the running exe) that is the product root.
pub(crate) fn find_product_root(start: &Path) -> Option<PathBuf> {
    start
        .ancestors()
        .find(|d| {
            d.join("apps").join("backend").join("__main__.py").is_file() && d.join("core").join("aas_build").is_dir()
        })
        .map(Path::to_path_buf)
}

fn repo_launch() -> Result<Launch, String> {
    let root = env_path("AAS_REPO_ROOT")
        .or_else(|| std::env::current_exe().ok().and_then(|exe| find_product_root(&exe)))
        .ok_or_else(|| "AI App Studio sources not found next to this program (set AAS_REPO_ROOT)".to_string())?;
    Ok(Launch { python: python_exe(&root), root, env: Vec::new() })
}

pub(crate) fn is_version(v: &str) -> bool {
    !v.is_empty()
        && v.len() <= 64
        && v.chars().all(|c| c.is_ascii_alphanumeric() || c == '.' || c == '-')
        && !v.starts_with('.')
        && !v.contains("..")
}

pub(crate) fn is_sha256_hex(v: &str) -> bool {
    v.len() == 64 && v.chars().all(|c| c.is_ascii_digit() || ('a'..='f').contains(&c))
}

/// The sha256 line of `certutil -hashfile <file> SHA256` (older Windows prints the hex with spaces).
pub(crate) fn parse_certutil_sha256(out: &str) -> Option<String> {
    out.lines()
        .map(|l| l.trim().replace(' ', "").to_ascii_lowercase())
        .find(|l| is_sha256_hex(l))
}

/// sha256 of a file via the OS certutil (absolute System32 path; no hashing crate in the shell).
fn zip_sha256(path: &Path) -> Result<String, String> {
    let certutil = env_path("SystemRoot")
        .unwrap_or_else(|| PathBuf::from(r"C:\Windows"))
        .join("System32")
        .join("certutil.exe");
    let mut cmd = Command::new(certutil);
    cmd.arg("-hashfile").arg(path).arg("SHA256").stdin(Stdio::null()); // output() captures stdout + stderr
    hide_window(&mut cmd);
    let out = cmd.output().map_err(|e| format!("could not run certutil to verify the runtime: {e}"))?;
    if !out.status.success() {
        return Err(format!("certutil could not hash the runtime ({})", out.status));
    }
    parse_certutil_sha256(&String::from_utf8_lossy(&out.stdout))
        .ok_or_else(|| "certutil printed no sha256 for the runtime".to_string())
}

/// Extracts the bundled runtime once per version (ported from the AI Website Studio shell) and returns its directory.
fn prepare_runtime(bundle: &Path, local: &Path) -> Result<PathBuf, String> {
    // S9b review L3: runtime.version = the full sha256 of runtime.zip; the extraction dir is its first 16 hex
    let expected = std::fs::read_to_string(bundle.join("runtime.version"))
        .map_err(|e| format!("bundled runtime version unreadable: {e}"))?
        .trim()
        .to_ascii_lowercase();
    if !is_sha256_hex(&expected) {
        return Err("bundled runtime version is not a sha256 (rebuild with scripts/build_runtime.ps1)".into());
    }
    let version = expected[..16].to_string();
    let base = local.join("runtime");
    let dir = base.join(&version);
    if dir.join(".complete").is_file() {
        return Ok(dir);
    }
    let zip = bundle.join("runtime.zip");
    let got = zip_sha256(&zip)?;
    if got != expected {
        return Err("runtime.zip does not match runtime.version (damaged or modified bundle)".into());
    }
    std::fs::create_dir_all(&base).map_err(|e| format!("cannot create {}: {e}", base.display()))?;
    let partial = base.join(format!("{version}.partial-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&partial);
    std::fs::create_dir_all(&partial).map_err(|e| format!("cannot create {}: {e}", partial.display()))?;
    // the OS tar (bsdtar, Windows 10+) reads zip; absolute path so PATH cannot substitute another binary
    let tar = env_path("SystemRoot")
        .unwrap_or_else(|| PathBuf::from(r"C:\Windows"))
        .join("System32")
        .join("tar.exe");
    let mut cmd = Command::new(tar);
    cmd.arg("-x")
        .arg("-f")
        .arg(&zip)
        .arg("-C")
        .arg(&partial)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    hide_window(&mut cmd);
    let status = cmd
        .status()
        .map_err(|e| format!("could not run tar to extract the runtime: {e}"))?;
    let python = partial.join("python").join("python.exe");
    let backend = partial.join("app").join("apps").join("backend").join("__main__.py");
    if !status.success() || !python.is_file() || !backend.is_file() {
        let _ = std::fs::remove_dir_all(&partial);
        return Err(format!("extracting the runtime failed ({status})"));
    }
    std::fs::write(partial.join(".complete"), &version).map_err(|e| e.to_string())?;
    let _ = std::fs::remove_dir_all(&dir); // an earlier incomplete attempt
    std::fs::rename(&partial, &dir).map_err(|e| format!("could not finalise the runtime: {e}"))?;
    if let Ok(entries) = std::fs::read_dir(&base) {
        for e in entries.flatten() {
            let name = e.file_name().to_string_lossy().to_string();
            let ours = is_version(&name) || name.split(".partial-").next().map(is_version).unwrap_or(false);
            if ours && name != version {
                let _ = std::fs::remove_dir_all(e.path());
            }
        }
    }
    Ok(dir)
}

fn installed_launch(bundle: &Path, local: &Path) -> Result<Launch, String> {
    let rt = prepare_runtime(bundle, local)?;
    let mut env = Vec::new();
    let pw = rt.join("ms-playwright");
    if pw.is_dir() {
        env.push(("PLAYWRIGHT_BROWSERS_PATH".to_string(), pw.display().to_string()));
    }
    Ok(Launch { python: rt.join("python").join("python.exe"), root: rt.join("app"), env })
}

/// Installed mode when the bundle carries a runtime, this is a release build and AAS_REPO_ROOT is unset.
pub(crate) fn choose_launch(resource_dir: Option<PathBuf>, local_dir: Option<PathBuf>) -> Result<Launch, String> {
    let bundle = resource_dir.map(|r| r.join("runtime"));
    let installed = !cfg!(debug_assertions)
        && env_path("AAS_REPO_ROOT").is_none()
        && bundle.as_ref().map(|b| b.join("runtime.zip").is_file()).unwrap_or(false);
    if !installed {
        return repo_launch();
    }
    let local = env_path("AAS_LOCAL_ROOT")
        .or(local_dir)
        .ok_or_else(|| "cannot determine the local application data folder".to_string())?;
    installed_launch(&bundle.expect("checked above"), &local)
}

/// The studio data root as the backend computes it (studio_core/config.py): AAS_DATA_DIR or LOCALAPPDATA\AIAppStudio.
pub(crate) fn data_root(aas_data_dir: Option<PathBuf>, local_app_data: Option<PathBuf>) -> Option<PathBuf> {
    aas_data_dir.or_else(|| local_app_data.map(|l| l.join("AIAppStudio")))
}

/// stdout/stderr of the backend until Python moves them to its capped log: <data>/logs/backend-boot.log, truncated
/// at every start (it never grows across sessions). Null when it cannot be opened.
fn boot_log() -> (Stdio, Stdio) {
    let opened = data_root(env_path("AAS_DATA_DIR"), env_path("LOCALAPPDATA")).and_then(|d| {
        let logs = d.join("logs");
        std::fs::create_dir_all(&logs).ok()?;
        let f = std::fs::File::create(logs.join("backend-boot.log")).ok()?;
        let g = f.try_clone().ok()?;
        Some((Stdio::from(f), Stdio::from(g)))
    });
    opened.unwrap_or_else(|| (Stdio::null(), Stdio::null()))
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
        .arg("--log-to-data-root")
        .current_dir(root);
    for k in inherited {
        if scrubbed_var(&k) {
            cmd.env_remove(&k);
        }
    }
    cmd.env("AAS_API_PORT", api_port().to_string());
    cmd
}

fn spawn_backend(token: &str, launch: &Launch) -> Result<Child, String> {
    let root = &launch.root;
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
    let mut cmd = backend_command(&launch.python, root, std::env::vars_os().map(|(k, _)| k));
    for (k, v) in &launch.env {
        cmd.env(k, v);
    }
    let (out, err) = boot_log();
    cmd.stdin(Stdio::piped()).stdout(out).stderr(err);
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

    fn start(&self, resource_dir: Option<PathBuf>, local_dir: Option<PathBuf>) {
        if std::env::var("AAS_ATTACH_BACKEND").map(|v| v == "1").unwrap_or(false) {
            return self.attach();
        }
        let token = generate_token();
        let phase = match choose_launch(resource_dir, local_dir).and_then(|launch| spawn_backend(&token, &launch)) {
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
        .setup(move |app| {
            let b = backend.clone();
            let resource_dir = app.path().resource_dir().ok();
            let local_dir = app.path().app_local_data_dir().ok();
            std::thread::spawn(move || b.start(resource_dir, local_dir));
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
        assert_eq!(args[7], "--log-to-data-root");
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
    fn product_root_is_found_from_the_exe_location_at_runtime() {
        let base = std::env::temp_dir().join(format!("aas-root-{}", std::process::id()));
        let prod = base.join("prod");
        std::fs::create_dir_all(prod.join("apps").join("backend")).unwrap();
        std::fs::create_dir_all(prod.join("core").join("aas_build")).unwrap();
        std::fs::write(prod.join("apps").join("backend").join("__main__.py"), "").unwrap();
        let exe = prod.join("apps").join("desktop").join("src-tauri").join("target").join("debug").join("x.exe");
        assert_eq!(find_product_root(&exe).as_deref(), Some(prod.as_path()));
        assert_eq!(find_product_root(&base.join("elsewhere").join("x.exe")), None);
        let _ = std::fs::remove_dir_all(&base);
    }

    #[test]
    fn debug_build_without_a_runtime_bundle_uses_repo_mode() {
        // the test binary lives under src-tauri/target: repo mode finds the real product root (or AAS_REPO_ROOT)
        let l = choose_launch(Some(PathBuf::from("Z:/no/such/resources")), None).expect("repo launch");
        assert!(l.root.join("apps").join("backend").join("__main__.py").is_file());
        assert!(l.env.is_empty());
    }

    #[test]
    fn certutil_output_and_sha_format() {
        let h = "4d6e7fcd88371d36dc92e1ac1710a9ae6e5f52748c8d0be445c6932ffbdff9fd";
        let out = format!("SHA256 hash of runtime.zip:
{h}
CertUtil: -hashfile command completed successfully.
");
        assert_eq!(parse_certutil_sha256(&out).as_deref(), Some(h));
        let spaced = "SHA256 hash of file:
4d 6e 7f cd 88 37 1d 36 dc 92 e1 ac 17 10 a9 ae 6e 5f 52 74 8c 8d 0b e4 45 c6 93 2f fb df f9 fd
";
        assert_eq!(parse_certutil_sha256(spaced).as_deref(), Some(h));
        assert_eq!(parse_certutil_sha256("CertUtil: error"), None);
        assert!(is_sha256_hex(h) && !is_sha256_hex(&h[..16]) && !is_sha256_hex(&h.to_uppercase()));
    }

    #[test]
    fn runtime_versions_and_data_root() {
        assert!(is_version("0.1.0") && is_version("0.1.0-rc1"));
        for bad in ["", "..", "../x", "a/b", "a\\b", ".hidden", "x y"] {
            assert!(!is_version(bad), "{bad}");
        }
        assert_eq!(data_root(Some(PathBuf::from("D:/d")), Some(PathBuf::from("C:/l"))), Some(PathBuf::from("D:/d")));
        assert_eq!(data_root(None, Some(PathBuf::from("C:/l"))), Some(PathBuf::from("C:/l").join("AIAppStudio")));
        assert_eq!(data_root(None, None), None);
    }

    #[test]
    fn launcher_takes_the_root_from_argv_not_from_code() {
        assert!(BACKEND_LAUNCHER.contains("sys.argv.pop(1)"));
        assert!(BACKEND_LAUNCHER.contains("run_module('apps.backend'"));
        assert!(scrubbed_var(OsStr::new("PythonPath")) && !scrubbed_var(OsStr::new("AAS_DATA_DIR")));
    }
}
