FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props Invora.sln dotnet-tools.json ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore Invora.sln --locked-mode
RUN dotnet publish src/Invora.Api/Invora.Api.csproj -c Release --no-restore -o /out/api
RUN dotnet tool restore
ARG TARGETARCH
RUN if [ "$TARGETARCH" = "arm64" ]; then rid=linux-arm64; else rid=linux-x64; fi; dotnet ef migrations bundle --project src/Invora.Infrastructure --target-runtime "$rid" --output /out/migrate --configuration Release
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
WORKDIR /app
COPY --from=build /out/api/ ./
COPY --from=build /out/migrate ./migrate
RUN mkdir -p /var/lib/invora/files && chown -R "$APP_UID" /var/lib/invora
ENV ASPNETCORE_HTTP_PORTS=8080 ASPNETCORE_ENVIRONMENT=Production Files__Root=/var/lib/invora/files
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Invora.Api.dll"]
