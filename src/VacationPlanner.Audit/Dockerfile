# построено в multi-stage: publish + runtime
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Корпоративный/локальный CA (TLS-перехват сети): передаётся в build-arg NUGET_CA_B64 (base64 PEM).
# В чистой сети аргумент пуст — шаг ничего не делает.
ARG NUGET_CA_B64=""
RUN if [ -n "$NUGET_CA_B64" ]; then echo "$NUGET_CA_B64" | base64 -d > /usr/local/share/ca-certificates/host-ca.crt && chmod 644 /usr/local/share/ca-certificates/host-ca.crt && update-ca-certificates; fi
COPY . .
RUN dotnet publish src/VacationPlanner.Audit -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/out .
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "VacationPlanner.Audit.dll"]
