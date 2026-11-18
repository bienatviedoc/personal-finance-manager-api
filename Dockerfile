# One Dockerfile for both hosts. Build with --build-arg PROJECT=Pfm.Api (default) or Pfm.Worker.
ARG PROJECT=Pfm.Api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /source

# Restore first, from project files only, so the layer is reused until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Pfm.Domain/Pfm.Domain.csproj src/Pfm.Domain/
COPY src/Pfm.Application/Pfm.Application.csproj src/Pfm.Application/
COPY src/Pfm.Infrastructure/Pfm.Infrastructure.csproj src/Pfm.Infrastructure/
COPY src/Pfm.Api/Pfm.Api.csproj src/Pfm.Api/
COPY src/Pfm.Worker/Pfm.Worker.csproj src/Pfm.Worker/
RUN dotnet restore src/${PROJECT}/${PROJECT}.csproj

COPY src/ src/
# The OpenAPI document is produced by the normal build and checked in CI, not in the image build.
RUN dotnet publish src/${PROJECT}/${PROJECT}.csproj -c Release -o /app --no-restore -p:OpenApiGenerateDocuments=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV APP_DLL=${PROJECT}.dll
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]
