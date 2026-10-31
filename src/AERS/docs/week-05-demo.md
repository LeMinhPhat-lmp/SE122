# Week 5 Dummy Agent Demo

The commands below assume the API is configured for Development and the dummy agent
uses the default `Aers:BaseUrl` and registration key from its configuration.

## 1. Start dependencies and the API

```bash
docker compose -f src/AERS/docker-compose.dev.yml up -d
dotnet run --project src/AERS/API
```

In another terminal, set the client configuration if the API URL or registration key differs:

```bash
export Aers__BaseUrl=https://localhost:5001/
export Aers__RegistrationKey=dev-registration-key-change-me
```

## 2. Run the provider

```bash
dotnet run --project src/AERS.DummyAgent -- provider
```

Expected output includes:

```text
registered provider <guid>; apiKey=<first-eight-characters>...
```

Keep this terminal running. It sends heartbeats every 15 seconds.

## 3. Discover the provider

In a second terminal:

```bash
dotnet run --project src/AERS.DummyAgent -- consumer --capability analyze_cv
```

Expected output:

```text
found CV Analysis Agent (<guid>)
inputSchema: {"type":"object",...}
outputSchema: {"type":"object",...}
input valid
```

The consumer selected the agent by capability and did not use a hard-coded agent ID.

## 4. Demonstrate pagination and online filtering

Register enough offline agents to create multiple pages:

```bash
dotnet run --project src/AERS.DummyAgent -- seed --count 120
dotnet run --project src/AERS.DummyAgent -- consumer --page-size 20 --capability seed_capability
```

Expected output contains at least:

```text
page 1: 20 agents
page 2: 20 agents
page 3: 20 agents
```

Stop the provider with Ctrl+C, wait for the configured 45-second presence TTL, then run:

```bash
dotnet run --project src/AERS.DummyAgent -- consumer --capability analyze_cv --online-only
```

The stopped provider is no longer listed.

## 5. Demonstrate cross-domain policy

With the default configuration, this is denied:

```bash
dotnet run --project src/AERS.DummyAgent -- consumer --domain hr --query-domain jobhunt
```

Expected output:

```text
DOMAIN_FORBIDDEN: ...
```

With the Development rule `hr -> jobhunt` enabled, the same command succeeds.
