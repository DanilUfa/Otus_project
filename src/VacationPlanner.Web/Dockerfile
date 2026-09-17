# ===================== Stage 1: build React SPA =====================
FROM node:20-alpine AS ui
WORKDIR /app/ClientApp
COPY src/VacationPlanner.Web/ClientApp/ ./
RUN npm install && npm run build          # → wwwroot (убирает placeholder)

# ===================== Stage 2: build + publish API =====================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/VacationPlanner.Web -c Release -o /app/out

# ===================== Stage 3: runtime =====================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
# Накладываем собранный React SPA поверх wwwroot (заменяет placeholder index.html)
COPY --from=ui /app/wwwroot ./wwwroot
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "VacationPlanner.Web.dll"]