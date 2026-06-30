# codebase-memory-mcp Local Verification

Date: 2026-06-30

## Scope

This note records a local-only verification of `codebase-memory-mcp` for the LifeOS / LifeAgent repository.

Constraints followed:

- No project business code was changed.
- No database, Cloud Run, Firestore, or Cloud Storage writes were performed.
- No deployment was performed.
- No `git push` or commit was performed.

## Official Sources Checked

- GitHub repository: `https://github.com/DeusData/codebase-memory-mcp`
- npm package: `codebase-memory-mcp@0.8.1`
- Official installer: `https://raw.githubusercontent.com/DeusData/codebase-memory-mcp/main/install.sh`

The installer supports:

- default install to `~/.local/bin`
- custom install directory via `--dir`
- skipping automatic agent configuration via `--skip-config`

## Commands Run

Package metadata:

```sh
npm view codebase-memory-mcp --json
```

Installer inspection:

```sh
curl -fsSL https://raw.githubusercontent.com/DeusData/codebase-memory-mcp/main/install.sh
```

Local binary install without agent configuration:

```sh
curl -fsSL https://raw.githubusercontent.com/DeusData/codebase-memory-mcp/main/install.sh | bash -s -- --dir /tmp/codebase-memory-mcp-bin --skip-config
```

Version and help:

```sh
/tmp/codebase-memory-mcp-bin/codebase-memory-mcp --version
/tmp/codebase-memory-mcp-bin/codebase-memory-mcp --help
```

Index current repository in fast mode:

```sh
/tmp/codebase-memory-mcp-bin/codebase-memory-mcp cli index_repository '{"repo_path":"/Volumes/fanxiang/01_Development/google_Agent/AIAgent","mode":"fast"}'
```

MCP stdio initialization check:

```sh
printf '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"codex-local-check","version":"0"}}}\n' | /tmp/codebase-memory-mcp-bin/codebase-memory-mcp
```

## Results

- Install succeeded with `codebase-memory-mcp 0.8.1`.
- The binary starts as an MCP stdio server and responds to `initialize`.
- The MCP initialize response reported `serverInfo.version` as `0.10.0`, while `--version` reported `0.8.1`.
- Repository indexing succeeded only after allowing writes to the user cache directory.
- Successful fast index result:
  - project: `Volumes-fanxiang-01_Development-google_Agent-AIAgent`
  - files indexed: 164
  - nodes: 2074
  - edges: 6124
  - cache file: `~/.cache/codebase-memory-mcp/Volumes-fanxiang-01_Development-google_Agent-AIAgent.db`
- Default exclusions included `.git`, `docs`, `scripts`, `bin`, `obj`, `node_modules`, `.next`, and public asset directories.

## Global Configuration Impact

The tool's `install` command targets user-level agent configuration files, including:

- Claude Code:
  - `~/.claude/.mcp.json`
  - `~/.claude.json`
- Codex CLI:
  - `~/.codex/config.toml`
  - `~/.codex/AGENTS.md`
- Gemini CLI:
  - `~/.gemini/settings.json`
  - `~/.gemini/GEMINI.md`
- OpenCode:
  - `~/.config/opencode/opencode.json`
  - `~/.config/opencode/AGENTS.md`
- Antigravity:
  - `~/.gemini/config/mcp_config.json`
  - `~/.gemini/antigravity-cli/AGENTS.md`
- VS Code:
  - `~/Library/Application Support/Code/User/mcp.json`
- Cursor:
  - `~/.cursor/mcp.json`

During verification, running:

```sh
/tmp/codebase-memory-mcp-bin/codebase-memory-mcp install --help
```

did not show help. It executed the installer flow and reported agent installation complete. This should be treated as an upstream CLI sharp edge.

## Recommendation

Do not formally connect this tool to the project yet.

Reasons:

- The MCP server can start.
- The repository can be indexed.
- However, post-index CLI reads showed inconsistent project recognition: `list_projects` found the cache file, but query/status tools did not reliably accept the listed project name.
- The CLI has a surprising `install --help` behavior that can modify global user agent configuration.
- Version metadata is inconsistent between CLI `--version` and MCP `initialize`.

Recommended next steps:

1. Test with a clean user profile or container to isolate existing global agent configuration effects.
2. Open or search upstream issues for the `install --help`, version mismatch, and project recognition problems.
3. If formally adopted, install to a stable path such as `~/.local/bin/codebase-memory-mcp`, then add MCP config intentionally and review diffs before use.
4. Prefer a project-local config example first; avoid automatic multi-agent install until the behavior is better understood.
