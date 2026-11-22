FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VacationPlanner.Importer.csproj .
RUN dotnet restore VacationPlanner.Importer.csproj
COPY Program.cs ImportRunner.cs ImportSettings.cs RowPipeline.cs appsettings.json ./
COPY Csv/ Csv/
COPY Enums/ Enums/
COPY Http/ Http/
COPY Models/ Models/
COPY Persistence/ Persistence/
RUN dotnet publish --no-restore -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ .
USER $APP_UID
ENTRYPOINT ["dotnet", "VacationPlanner.Importer.dll"]
