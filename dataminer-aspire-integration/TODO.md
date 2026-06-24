# Aspire Integration — TODO

## Multi-instance support
- [x] Check if multiple Aspire integrations can run side-by-side without port conflicts (e.g. dashboard port, API port, ScriptHost JSON-RPC port, frontend dev server port)
- [x] Add configurable port ranges or auto-detect free ports to avoid collisions → `--port-offset N`

## Optional components
- [x] Add ability to remove/disable the AI Coworker from the Aspire integration → `--no-ai-coworker`
- [x] Add ability to remove/disable the local Foundry from the Aspire integration → `--no-foundry`
