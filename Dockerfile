# syntax=docker/dockerfile:1

# Stage 1: Build
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /source
COPY . .
WORKDIR /source/src
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
  dotnet publish AERS.Api/AERS.Api.csproj \
  -c Release \
  -a ${TARGETARCH/amd64/x64} \
  --use-current-runtime \
  --self-contained false \
  -o /app

# Stage 2: Development
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS development
WORKDIR /source
COPY . .
WORKDIR /source/src
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
CMD ["dotnet", "run", "--project", "AERS/API/API.csproj", "--no-launch-profile"]

# Stage 3: Production
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ARG UID=10001
RUN adduser \
  --disabled-password \
  --gecos "" \
  --home "/nonexistent" \
  --shell "/sbin/nologin" \
  --no-create-home \
  --uid "${UID}" \
  appuser
USER appuser
ENTRYPOINT ["dotnet", "AERS.Api.dll"]
