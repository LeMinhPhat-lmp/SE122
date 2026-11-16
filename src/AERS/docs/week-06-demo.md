# Week 6 Dummy Agent Demo

Start the development dependencies and API:

```bash
docker compose -f src/AERS/docker-compose.dev.yml up -d
dotnet run --project src/AERS/API
```

In another terminal, start an agent:

```bash
dotnet run --project src/AERS.DummyAgent -- serve --name "CV Provider" --capability analyze_cv
```

Call it from a temporary consumer:

```bash
dotnet run --project src/AERS.DummyAgent -- call \
  --capability analyze_cv \
  --input '{"cvText":"one two three"}'
```

The call reports `status=completed`, `wordCount=3`, and the deterministic score. The serving terminal reports the same
task id in its `invoke:` and `result:` lines.

Failure and unavailable-target checks:

```bash
dotnet run --project src/AERS.DummyAgent -- serve --name "Failing Agent" --capability always_fail
dotnet run --project src/AERS.DummyAgent -- call --capability always_fail --input '{"reason":"demo failure"}'

dotnet run --project src/AERS.DummyAgent -- serve --name "Offline Agent" --capability echo --no-connect
dotnet run --project src/AERS.DummyAgent -- call --capability echo --input '{"message":"hello"}'
```

Run the scenario runner against the same API:

```bash
dotnet run --project src/AERS.DummyAgent -- scenario sync-ab
dotnet run --project src/AERS.DummyAgent -- scenario target-unavailable
```

Inspect a task without putting an API key on the command line:

```bash
export AERS_API_KEY='...'
dotnet run --project src/AERS.DummyAgent -- inspect \
  --task-id <task-id> --api-key-env AERS_API_KEY
```

Press Ctrl+C in a serving terminal. The agent stops its heartbeat, disconnects, and deregisters.
