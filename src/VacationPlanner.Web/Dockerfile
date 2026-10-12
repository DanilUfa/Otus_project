# ===================== Stage 1: build React SPA =====================
FROM node:20-alpine AS ui
WORKDIR /app/ClientApp
# Тот же параметр CA, что и в dotnet-стадии (для сети с TLS-перехватом).
# Alpine не содержит update-ca-certificates, поэтому Node доверяет CA через NODE_EXTRA_CA_CERTS.
ARG NUGET_CA_B64=""
RUN if [ -n "$NUGET_CA_B64" ]; then mkdir -p /etc/ssl && echo "$NUGET_CA_B64" | base64 -d > /etc/ssl/host-ca.pem && chmod 644 /etc/ssl/host-ca.pem; fi
COPY src/VacationPlanner.Web/ClientApp/ ./
RUN { [ -n "$NUGET_CA_B64" ] && export NODE_EXTRA_CA_CERTS=/etc/ssl/host-ca.pem || true; } && npm install && npm run build    # → wwwroot (убирает placeholder)

# ===================== Stage 2: build + publish API =====================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Корпоративный/локальный CA (TLS-перехват сети): передаётся в build-arg NUGET_CA_B64 (base64 PEM).
# В чистой сети аргумент пуст — шаг ничего не делает.
ARG NUGET_CA_B64=""
RUN if [ -n "$NUGET_CA_B64" ]; then echo "$NUGET_CA_B64" | base64 -d > /usr/local/share/ca-certificates/host-ca.crt && chmod 644 /usr/local/share/ca-certificates/host-ca.crt && update-ca-certificates; fi
COPY . .
RUN dotnet publish src/VacationPlanner.Web -c Release -o /app/out

# ===================== Stage 3: runtime =====================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/out .
# Накладываем собранный React SPA поверх wwwroot (заменяет placeholder index.html)
COPY --from=ui /app/wwwroot ./wwwroot
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "VacationPlanner.Web.dll"]