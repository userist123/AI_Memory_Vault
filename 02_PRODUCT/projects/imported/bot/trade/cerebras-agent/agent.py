import os
import json
import subprocess
import sys
from cerebras.cloud.sdk import Cerebras

MODEL = "gpt-oss-120b"
SYSTEM_PROMPT = """You are an expert software engineer and autonomous coding agent.
You can read files, write files, execute bash commands, list directories, and create complete programs.

When given a task:
1. Plan the approach first (think step by step)
2. Use tools to explore the codebase if needed
3. Write clean, production-ready code
4. Test your code by running it
5. Fix any errors automatically
6. Report when the task is complete

Always use tools to actually perform actions - never just describe what you would do.
Prefer Python unless the user specifies otherwise.
Write complete, working code - no placeholders, no TODOs."""

TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "read_file",
            "description": "Read the contents of a file",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "Path to the file to read"}
                },
                "required": ["path"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "write_file",
            "description": "Write content to a file (creates or overwrites)",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "Path to the file"},
                    "content": {"type": "string", "description": "Content to write"}
                },
                "required": ["path", "content"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "run_command",
            "description": "Execute a bash command and return stdout + stderr. Use for running code, installing packages, git commands, etc.",
            "parameters": {
                "type": "object",
                "properties": {
                    "command": {"type": "string", "description": "Bash command to execute"},
                    "timeout": {"type": "integer", "description": "Timeout in seconds (default 30)", "default": 30}
                },
                "required": ["command"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "list_directory",
            "description": "List files and directories at a given path",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "Directory path (default: current directory)", "default": "."}
                },
                "required": []
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "create_directory",
            "description": "Create a directory (and parents if needed)",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "Directory path to create"}
                },
                "required": ["path"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "search_in_files",
            "description": "Search for a string/pattern in files using grep",
            "parameters": {
                "type": "object",
                "properties": {
                    "pattern": {"type": "string", "description": "Pattern to search for"},
                    "path": {"type": "string", "description": "Directory or file to search in", "default": "."},
                    "recursive": {"type": "boolean", "description": "Search recursively", "default": True}
                },
                "required": ["pattern"]
            }
        }
    }
]

def execute_tool(name: str, args: dict) -> str:
    try:
        if name == "read_file":
            path = args["path"]
            if not os.path.exists(path):
                return f"ERROR: File not found: {path}"
            with open(path, "r", encoding="utf-8", errors="replace") as f:
                content = f.read()
            return f"[File: {path}]\n{content}"

        elif name == "write_file":
            path = args["path"]
            content = args["content"]
            os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
            with open(path, "w", encoding="utf-8") as f:
                f.write(content)
            return f"Written {len(content)} chars to {path}"

        elif name == "run_command":
            command = args["command"]
            timeout = args.get("timeout", 30)
            print(f"  Running: {command}")
            result = subprocess.run(
                command, shell=True, capture_output=True,
                text=True, timeout=timeout
            )
            output = ""
            if result.stdout:
                output += f"STDOUT:\n{result.stdout}"
            if result.stderr:
                output += f"\nSTDERR:\n{result.stderr}"
            output += f"\nExit code: {result.returncode}"
            return output or "(no output)"

        elif name == "list_directory":
            path = args.get("path", ".")
            if not os.path.exists(path):
                return f"ERROR: Path not found: {path}"
            items = []
            for item in sorted(os.listdir(path)):
                full = os.path.join(path, item)
                prefix = "[DIR] " if os.path.isdir(full) else "[FILE] "
                items.append(f"{prefix}{item}")
            return f"[Directory: {path}]\n" + "\n".join(items) if items else "(empty)"

        elif name == "create_directory":
            path = args["path"]
            os.makedirs(path, exist_ok=True)
            return f"Directory created: {path}"

        elif name == "search_in_files":
            pattern = args["pattern"]
            path = args.get("path", ".")
            recursive = args.get("recursive", True)
            flag = "-r" if recursive else ""
            result = subprocess.run(
                f'grep {flag} -n "{pattern}" {path}',
                shell=True, capture_output=True, text=True
            )
            return result.stdout or f"No matches for '{pattern}'"

    except subprocess.TimeoutExpired:
        return f"ERROR: Command timed out after {args.get('timeout', 30)}s"
    except Exception as e:
        return f"ERROR: {type(e).__name__}: {str(e)}"


def run_agent(task: str, work_dir: str = "."):
    os.chdir(work_dir)

    api_key = os.environ.get("CEREBRAS_API_KEY")

    client = Cerebras(api_key=os.environ.get("CEREBRAS_API_KEY", ""))

    messages = [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": task}
    ]

    print(f"\n{'='*60}")
    print(f"Agent pornit | Model: {MODEL}")
    print(f"Task: {task}")
    print(f"{'='*60}\n")

    iteration = 0
    max_iterations = 20

    while iteration < max_iterations:
        iteration += 1
        print(f"[Iteratie {iteration}] Trimit cerere catre Cerebras...")

        response = client.chat.completions.create(
            model=MODEL,
            messages=messages,
            tools=TOOLS,
            tool_choice="auto",
            max_tokens=4096,
            temperature=0.2
        )

        message = response.choices[0].message
        finish_reason = response.choices[0].finish_reason

        messages.append({
            "role": "assistant",
            "content": message.content or "",
            "tool_calls": [
                {
                    "id": tc.id,
                    "type": "function",
                    "function": {"name": tc.function.name, "arguments": tc.function.arguments}
                }
                for tc in (message.tool_calls or [])
            ] or None
        })

        if message.content:
            print(f"\nAgent: {message.content}\n")

        if message.tool_calls:
            for tool_call in message.tool_calls:
                func_name = tool_call.function.name
                func_args = json.loads(tool_call.function.arguments)

                print(f"  Tool: {func_name}({json.dumps(func_args, ensure_ascii=False)[:80]})")
                result = execute_tool(func_name, func_args)

                if len(result) > 3000:
                    result = result[:3000] + "\n...[truncat]"

                print(f"  -> {result[:150]}{'...' if len(result) > 150 else ''}")

                messages.append({
                    "role": "tool",
                    "tool_call_id": tool_call.id,
                    "content": result
                })

        if finish_reason == "stop" and not message.tool_calls:
            print(f"\n{'='*60}")
            print(f"Task completat in {iteration} iteratii!")
            print(f"{'='*60}\n")
            break

    if iteration >= max_iterations:
        print(f"\nLimita de {max_iterations} iteratii atinsa.")

    return messages


def interactive_mode():
    print("\n" + "="*45)
    print("  Cerebras Coding Agent")
    print("  Model: gpt-oss-120b (GRATUIT)")
    print("="*45)
    print("\n  Comenzi: 'exit' pentru a iesi")
    print("           'dir <path>' pentru a schimba directorul\n")

    work_dir = os.getcwd()

    while True:
        try:
            task = input(f"[{os.path.basename(work_dir)}]> ").strip()
            if not task:
                continue
            if task.lower() in ("exit", "quit", "q"):
                print("La revedere!")
                break
            if task.lower().startswith("dir "):
                new_dir = task[4:].strip()
                if os.path.isdir(new_dir):
                    work_dir = os.path.abspath(new_dir)
                    print(f"  Director schimbat: {work_dir}")
                else:
                    print(f"  Directorul nu exista: {new_dir}")
                continue

            run_agent(task, work_dir)

        except KeyboardInterrupt:
            print("\n\nIntrerupt. La revedere!")
            break
        except Exception as e:
            print(f"Eroare: {e}")


if __name__ == "__main__":
    if len(sys.argv) > 1:
        task = " ".join(sys.argv[1:])
        run_agent(task)
    else:
        interactive_mode()
