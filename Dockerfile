# syntax=docker/dockerfile:1
# API with the in-process sherpa-onnx verifier. Build: docker build -t voice-step-up-api .
# The models are fetched at build time by scripts/fetch-models.sh and checked against models/models.lock.

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/StepUp.Api/StepUp.Api.csproj -c Release -o /out \
 && dotnet publish src/StepUp.FakeAuthority/StepUp.FakeAuthority.csproj -c Release -o /out-authority

# The fake sign-in authority of the offline demo (compose service `authority`). It needs no models and no tenant.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS authority
WORKDIR /app
COPY --from=build /out-authority ./
ENV ASPNETCORE_HTTP_PORTS=5100
USER app
EXPOSE 5100
ENTRYPOINT ["dotnet", "StepUp.FakeAuthority.dll"]

FROM debian:trixie-20261005-slim AS models
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl bzip2 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY scripts/fetch-models.sh scripts/fetch-models.sh
COPY models/models.lock models/models.lock
RUN bash scripts/fetch-models.sh

# glibc base (the native sherpa-onnx and onnxruntime libraries do not run on Alpine).
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
WORKDIR /app
COPY --from=build /out ./
COPY --from=models /app/models ./models
RUN mkdir -p /data && chown -R app:app /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DatabasePath=/data/stepup.db \
    Storage__KeyDirectory=/data/keys \
    Verifier__Mode=Sherpa \
    Verifier__Sherpa__ModelsDir=/app/models
VOLUME /data
USER app
EXPOSE 8080
# The runtime image has no curl: a plain TCP request from bash.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /healthz HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -n1 <&3 | grep -q " 200 "'
ENTRYPOINT ["dotnet", "StepUp.Api.dll"]
