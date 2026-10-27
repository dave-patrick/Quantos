"""Quantos tools for agent frameworks that call tools themselves (Release 1.2).

Standard library only. Schemas come from `quantos-cli tools`, calls go through `quantos-cli call`,
so every call runs as the named agent with the permissions granted to it in Quantos.
Before running a framework, pair interactively with `quantos-cli status --agent my-script`
and approve the matching code in the native client. This wrapper captures subprocess output;
it cannot present the first-pairing prompt interactively.

    quantos = QuantosTools(cli=r"C:\\Users\\you\\Documents\\Quantos\\application\\quantos-cli.exe", agent="my-script")
    tools = quantos.schemas("anthropic")              # or "openai", "json"
    text, ok = quantos.call("search_notes", {"query": "budget"})
"""

import json
import subprocess


class QuantosTools:
    def __init__(self, cli="quantos-cli", agent="script", client_data=None, timeout=60):
        self.cli = cli
        self.agent = agent
        self.client_data = client_data
        self.timeout = timeout

    def _extra(self):
        extra = ["--agent", self.agent]
        if self.client_data:
            extra += ["--client-data", self.client_data]
        return extra

    def schemas(self, fmt="openai"):
        """Tool definitions in the shape the given API expects."""
        out = subprocess.run([self.cli, "tools", "--format", fmt], capture_output=True, text=True,
                             encoding="utf-8", timeout=self.timeout, check=True)
        return json.loads(out.stdout)

    def names(self):
        return [t["name"] for t in self.schemas("json")]

    def call(self, name, arguments=None):
        """Runs one tool. Returns (text, ok): the result, or the reason it failed."""
        out = subprocess.run([self.cli, "call", name, "-"] + self._extra(),
                             input=json.dumps(arguments or {}), capture_output=True, text=True,
                             encoding="utf-8", timeout=self.timeout)
        if out.returncode == 0:
            return out.stdout.strip(), True
        return (out.stderr or out.stdout).strip(), False


if __name__ == "__main__":
    import sys

    tools = QuantosTools(cli=sys.argv[1] if len(sys.argv) > 1 else "quantos-cli")
    print("Tools:", ", ".join(tools.names()))
    text, ok = tools.call("search_notes", {"query": sys.argv[2] if len(sys.argv) > 2 else "meeting"})
    print(text if ok else "Failed: " + text)
