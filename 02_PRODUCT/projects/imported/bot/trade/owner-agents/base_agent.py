"""
Baza comuna pentru toti agentii - tools + agent loop Gemini
"""
import os, json, subprocess, sys

try:
    import google.generativeai as genai
except ImportError:
    print("Lipseste: pip install google-generativeai")
    sys.exit(1)

def get_tools():
    def read_file(path: str) -> str:
        """Read the full contents of a file from disk."""
        if not os.path.exists(path):
            return f"ERROR: File not found: {path}"
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return f"[{path}]\n{f.read()}"

    def write_file(path: str, content: str) -> str:
        """Write or overwrite a file with given content. Creates parent directories automatically."""
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            f.write(content)
        return f"Written {len(content)} chars to {path}"

    def run_command(command: str, timeout: int = 60) -> str:
        """Execute a shell command (bash on Linux/Mac, cmd on Windows). Returns stdout and stderr."""
        print(f"  $ {command}")
        try:
            r = subprocess.run(command, shell=True, capture_output=True, text=True, timeout=timeout)
            out = ""
            if r.stdout: out += f"STDOUT:\n{r.stdout}"
            if r.stderr: out += f"\nSTDERR:\n{r.stderr}"
            return (out + f"\nExit: {r.returncode}") or "(no output)"
        except subprocess.TimeoutExpired:
            return f"TIMEOUT after {timeout}s"

    def list_directory(path: str = ".") -> str:
        """List all files and subdirectories at the given path."""
        if not os.path.exists(path): return f"ERROR: Not found: {path}"
        items = []
        for i in sorted(os.listdir(path)):
            full = os.path.join(path, i)
            items.append(("[DIR]  " if os.path.isdir(full) else "[FILE] ") + i)
        return f"[{path}]\n" + "\n".join(items) if items else f"[{path}] empty"

    def create_directory(path: str) -> str:
        """Create a directory and all its parent directories."""
        os.makedirs(path, exist_ok=True)
        return f"Created: {path}"

    def search_in_files(pattern: str, path: str = ".", recursive: bool = True) -> str:
        """Search for a text pattern in files using grep."""
        r = subprocess.run(
            f'grep {"-r" if recursive else ""} -n "{pattern}" {path}',
            shell=True, capture_output=True, text=True
        )
        return r.stdout or f"No matches for '{pattern}'"

    def append_to_file(path: str, content: str) -> str:
        """Append content to the end of an existing file (or create it)."""
        with open(path, "a", encoding="utf-8") as f:
            f.write(content)
        return f"Appended {len(content)} chars to {path}"

    return [read_file, write_file, run_command, list_directory,
            create_directory, search_in_files, append_to_file]

TOOL_MAP = {f.__name__: f for f in get_tools()}

def run_agent(task: str, system_prompt: str, model_name: str = "gemini-2.5-flash",
              work_dir: str = ".", max_iterations: int = 25):

    os.chdir(work_dir)
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        print("[EROARE] GEMINI_API_KEY nu e setat!")
        print("  Windows CMD: setx GEMINI_API_KEY \"AIza...\"")
        print("  Sau editeaza agentul si pune cheia direct: api_key = \"AIza...\"")
        sys.exit(1)

    genai.configure(api_key=api_key)
    model = genai.GenerativeModel(
        model_name=model_name,
        system_instruction=system_prompt,
        tools=list(TOOL_MAP.values())
    )
    chat = model.start_chat(enable_automatic_function_calling=False)

    print(f"\n{\'=\'*60}")
    print(f"Task: {task[:100]}")
    print(f"{\'=\'*60}\n")

    current_message = task
    for iteration in range(1, max_iterations + 1):
        print(f"[{iteration}] Thinking...")
        response = chat.send_message(current_message)

        tool_called = False
        tool_results = []

        for part in response.parts:
            if hasattr(part, "text") and part.text:
                print(f"\nAgent: {part.text}\n")
            if hasattr(part, "function_call") and part.function_call.name:
                fc = part.function_call
                tool_called = True
                args = dict(fc.args)
                print(f"  Tool: {fc.name}({str(args)[:80]})")

                try:
                    result = TOOL_MAP[fc.name](**args) if fc.name in TOOL_MAP else f"ERROR: Unknown tool {fc.name}"
                except Exception as e:
                    result = f"ERROR: {type(e).__name__}: {e}"

                if len(result) > 4000:
                    result = result[:4000] + "\n...[truncat]"
                print(f"  -> {result[:120]}{\'...\' if len(result)>120 else ''}")

                tool_results.append(
                    genai.protos.Part(
                        function_response=genai.protos.FunctionResponse(
                            name=fc.name,
                            response={"result": result}
                        )
                    )
                )

        if tool_called:
            current_message = tool_results
        else:
            print(f"\n[DONE] Completat in {iteration} iteratii!\n")
            break
    else:
        print(f"\n[WARN] Limita de {max_iterations} iteratii atinsa.")
