# Connecting agent frameworks to Quantos

Quantos gives AI agents the same small set of tools in two ways:

- **MCP** (Model Context Protocol): `quantos-cli mcp --agent <name>` serves the tools over stdin/stdout.
  Most agent frameworks can use an MCP server directly; this is the way to use where it's offered.
- **Function calling**: `quantos-cli tools --format openai|anthropic|json` prints the tool schemas, and
  `quantos-cli call <tool> '<json arguments>'` runs one. Use this with frameworks or scripts that call
  tools themselves.

Either way, every call goes through the Quantos backend **as the agent you name**, so it can only do what
you've granted that agent in *Settings › Plugins & permissions*. A new agent can read but not write.
Quantos must be running (the Windows client, or `Quantos.Backend.exe`).

## The tools

| Tool | What it does |
|---|---|
| `search_notes` | Full-text search; titles, ids and snippets |
| `read_note` | A note's Markdown and frontmatter, by id or exact title |
| `create_note` | A new note (title, body, workspace, tags) |
| `append_to_note` | Adds Markdown to the end of a note |
| `list_tasks` | Open tasks (or all) |
| `create_task` | A new task (title, due, priority, workspace) |
| `complete_task` | Marks a task done |
| `run_query` | Runs a Quantos query (the YAML used in ` ```query ` blocks) |
| `capture` | Quick capture to today's daily note, the inbox or a new task |

The command below is written for the zip layout; use the full path to `quantos-cli.exe` in your
`Quantos\application` folder. On other layouts, add `--client-data <folder>` pointing at Quantos's
`client-data` folder.

```
C:\Users\you\Documents\Quantos\application\quantos-cli.exe mcp --agent research-bot
```

## Claude Desktop and Claude Code

Claude Desktop, in `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "quantos": {
      "command": "C:\\Users\\you\\Documents\\Quantos\\application\\quantos-cli.exe",
      "args": ["mcp", "--agent", "claude"]
    }
  }
}
```

Claude Code:

```
claude mcp add quantos -- "C:\Users\you\Documents\Quantos\application\quantos-cli.exe" mcp --agent claude-code
```

## Frameworks with MCP support

These frameworks start the server as a subprocess and turn its tools into their own. The shapes below
are each framework's documented MCP integration at the time of writing; check its documentation if the
names have moved.

**OpenAI Agents SDK (Python)**

```python
from agents import Agent, Runner
from agents.mcp import MCPServerStdio

QUANTOS = r"C:\Users\you\Documents\Quantos\application\quantos-cli.exe"

async with MCPServerStdio(params={"command": QUANTOS, "args": ["mcp", "--agent", "openai-agent"]}) as quantos:
    agent = Agent(name="Assistant", instructions="Use the user's Quantos notes.", mcp_servers=[quantos])
    result = await Runner.run(agent, "What did we decide about the Apollo launch?")
```

**LangChain / LangGraph** (`langchain-mcp-adapters`)

```python
from langchain_mcp_adapters.client import MultiServerMCPClient

client = MultiServerMCPClient({
    "quantos": {"command": QUANTOS, "args": ["mcp", "--agent", "langchain"], "transport": "stdio"}
})
tools = await client.get_tools()   # LangChain tools, ready for an agent or a LangGraph node
```

**LlamaIndex** (`llama-index-tools-mcp`)

```python
from llama_index.tools.mcp import BasicMCPClient, McpToolSpec

client = BasicMCPClient(QUANTOS, args=["mcp", "--agent", "llamaindex"])
tools = await McpToolSpec(client=client).to_tool_list_async()
```

**CrewAI** (`crewai-tools[mcp]`)

```python
from crewai_tools import MCPServerAdapter
from mcp import StdioServerParameters

params = StdioServerParameters(command=QUANTOS, args=["mcp", "--agent", "crew"])
with MCPServerAdapter(params) as tools:
    ...  # pass `tools` to your Agent
```

**AutoGen** (`autogen-ext[mcp]`)

```python
from autogen_ext.tools.mcp import StdioServerParams, mcp_server_tools

tools = await mcp_server_tools(StdioServerParams(command=QUANTOS, args=["mcp", "--agent", "autogen"]))
```

**Semantic Kernel** (Python)

```python
from semantic_kernel.connectors.mcp import MCPStdioPlugin

async with MCPStdioPlugin(name="quantos", command=QUANTOS, args=["mcp", "--agent", "semantic-kernel"]) as quantos:
    kernel.add_plugin(quantos)
```

## Plain function calling

When a framework or your own code calls tools itself (the Anthropic or OpenAI APIs directly, for
example), print the schemas once and run each call through `quantos-cli call`:

```
quantos-cli tools --format anthropic > quantos-tools.json
quantos-cli call search_notes "{\"query\": \"budget\"}" --agent my-script
echo {"title": "Call Sam", "due": "2026-10-23"} | quantos-cli call create_task - --agent my-script
```

`call` prints the tool's result on stdout and exits with 0; when the tool fails (or isn't allowed), the
reason goes to stderr and the exit code is 1. Formats: `openai` (function tools), `anthropic`
(`name`, `description`, `input_schema`), `json` (`name`, `description`, `parameters`), `mcp`.

`examples/agents/quantos_tools.py` wraps this for Python with the standard library only:

```python
from quantos_tools import QuantosTools

quantos = QuantosTools(cli=QUANTOS, agent="my-script")
tools = quantos.schemas("anthropic")          # pass to the model
text, ok = quantos.call("search_notes", {"query": "budget"})   # when the model asks for a tool
```

## The HTTP API

Agents that can't start a process can call the backend's HTTP API directly with the
`X-Quantos-Agent: <name>` header; see `API.md`. The same grants apply.
