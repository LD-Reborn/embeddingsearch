#!/usr/bin/env python3
"""
Generate ten brief-overview files for a given topic using an Ollama model.

▪ The directory ./files is used as a mini knowledge-base.
▪ Two Python functions are exposed to the model as *tools*:
      • list_files()   – return [{name, title}, …] for everything in ./files
      • create_file()  – create/overwrite a file and write the content supplied
▪ The model is instructed to call create_file() ten times (one per sub-topic)
  and put the title on the first line of each file.
"""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import List, Dict, Any

import ollama   # pip install -U ollama


# ---------- constant configuration ------------------------------------------------

FILES_DIR = Path(__file__).parent / "example_content"
FILES_DIR.mkdir(exist_ok=True)


# ---------- tool functions ---------------------------------------------------------

def list_files() -> List[Dict[str, str]]:
    """
    List every regular file in ./example_content together with its first line (title).

    Returns
    -------
    list[dict]
        Each element has:  {"name": "<filename>", "title": "<first line or ''>"}
    """
    results: List[Dict[str, str]] = []
    for path in FILES_DIR.iterdir():
        if path.is_file():
            with path.open("r", encoding="utf-8", errors="ignore") as fh:
                title = fh.readline().rstrip("\n")
            results.append({"name": path.name, "title": title})
    return results


def create_file(filename: str, content: str) -> str:
    """
    Create (or overwrite) a file inside ./files.

    Parameters
    ----------
    filename : str
        A simple name like "quantum_entanglement.md".  Any path components
        beyond the basename are stripped for safety.
    content : str
        The full text to write – the first line *must* be the title.

    Returns
    -------
    str
        Absolute path of the file that was written.
    """
    safe_name = os.path.basename(filename)
    if not safe_name:
        raise ValueError("filename cannot be empty")
    path = FILES_DIR / safe_name
    path.write_text(content, encoding="utf-8")
    return str(path.resolve())


# ---------- main driver ------------------------------------------------------------

def run(topic: str, *, model: str = "qwen3:latest", temperature: float = 0.2) -> None:
    """Ask the model to create ten overview files about *topic*."""

    system_prompt = (
        "You are a file-writing assistant.  For each of ten distinct sub-topics "
        "related to the given topic you will call the `create_file` tool to "
        "write a Markdown file that contains at least 5 sentences.  Use a short, "
        "snake_case filename ending with '.md'.  The very first line of the "
        "file **must** be the title (capitalized).  After you have created all "
        "ten files, reply with only the single word DONE."
    )

    messages: List[Dict[str, Any]] = [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": f"Topic: {topic}"},
    ]

    tools = [list_files, create_file]
    available = {f.__name__: f for f in tools}

    # initial call
    response = ollama.chat(model=model,
                           messages=messages,
                           tools=tools,
                           options={"temperature": temperature})

    for call in response.message.tool_calls or []:
        fn_name = call.function.name
        fn_args = call.function.arguments
        result = available[fn_name](**fn_args) # Run tool calls
        messages.append({"role": "tool",
                            "name": fn_name,
                            "content": json.dumps(result, ensure_ascii=False)})

# ---------- CLI entry-point --------------------------------------------------------

if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(
        description="Generate ten overview files for a topic using Ollama + tool calling")
    parser.add_argument("topic", help="Main subject area, e.g. 'quantum computing'")
    parser.add_argument("--model", default="qwen3:latest",
                        help="Local Ollama model to use (default: qwen3:latest)")
    args = parser.parse_args()

    run(args.topic, model=args.model)